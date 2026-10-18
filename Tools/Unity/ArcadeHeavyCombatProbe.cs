using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Game;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>正式临时局的受控重型、实弹击杀／池复用及受伤帧采样；不改配置，不读写正式存档，不打包。</summary>
public static class ArcadeHeavyCombatProbe
{
    private const string Output = "Temp/HeavyCombat-20261018", OwnerKey = "FirepowerLoop.HeavyCombat.Assembly";
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool running, drive;
    private static object oldStore, oldClock, oldFailure, oldRun, oldPhase;
    private static string directory, runId;
    private static Gamepad pad;
    private static InputSettings.BackgroundBehavior background;
    private static InputSettings.EditorInputBehaviorInPlayMode routing;
    private static readonly List<double> frames = new List<double>(), damageTimes = new List<double>();
    private static readonly List<long> allocations = new List<long>();
    private static object Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(obj, value);
    private static object Runtime => typeof(ArcadeCombatModel).GetProperty("Runtime", Hidden).GetValue(global::Game.Game.Interface.GetModel<ArcadeCombatModel>());
    private static Type Owner() => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.FullName == SessionState.GetString(OwnerKey, ""))?.GetType("ArcadeHeavyCombatProbe");
    public static string Start()
    {
        if (!EditorApplication.isPlaying || Application.dataPath != "E:/GitHub/FirepowerLoop/Client/Assets") throw new Exception("需本项目Play");
        if (Owner() is Type prior && (bool)prior.GetField("running", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)) throw new Exception("已有观察任务");
        SessionState.SetString(OwnerKey, typeof(ArcadeHeavyCombatProbe).Assembly.FullName);
        running = true; Run().Forget(); return "重型与受伤观察启动";
    }
    public static string State()
    {
        if (Owner() is Type owner && owner != typeof(ArcadeHeavyCombatProbe)) return (string)owner.GetMethod("State").Invoke(null, null);
        return "running=" + running + "\n" + (File.Exists(Output + "/probe.log") ? string.Join("\n", File.ReadAllLines(Output + "/probe.log").Reverse().Take(6).Reverse()) : "waiting");
    }
    private static void Log(string text) => File.AppendAllText(Output + "/probe.log", DateTime.UtcNow.ToString("O") + " " + text + "\n");
    private static async UniTask Wait(Func<bool> condition, double seconds, string label)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        while (!condition()) { if (EditorApplication.timeSinceStartup > end) throw new Exception(label + "超时"); await UniTask.Delay(50, true); }
    }
    private static async UniTask Observe(double seconds, object target = null, bool shoot = false)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        while (EditorApplication.timeSinceStartup < end && Runtime != null)
        {
            if (drive && pad != null)
            {
                var direction = target != null ? (Vector2)Get(target, "Position") - (Vector2)Get(Runtime, "PlayerPosition") : Vector2.zero;
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = shoot && direction.sqrMagnitude > .001f ? direction.normalized : Vector2.zero });
            }
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            frames.Add(Time.unscaledDeltaTime * 1000d);
        }
    }
    private static void EnqueueBrute()
    {
        // 仅为受控复用留一个名额，使用正式击杀／回收入口，不改变MaxAlive或敌人数值。
        var combat = global::Game.Game.Interface.GetSystem<ArcadeCombatSystem>();
        var occupied = ((IDictionary)Get(Runtime, "Enemies")).Values.Cast<object>().FirstOrDefault(enemy => (string)Get(enemy, "ConfigId") != "enemy.arcade.brute");
        if (occupied != null)
        {
            typeof(ArcadeCombatSystem).GetMethod("DamageEnemy", Hidden).Invoke(combat, new object[] { occupied, 100000 });
            typeof(ArcadeCombatSystem).GetMethod("KillEnemy", Hidden).Invoke(combat, new object[] { Runtime, occupied, false });
            Log("受控释放1个普通敌人名额，未改生成上限");
        }
        var queue = (Queue<string>)Get(Runtime, "SpawnQueue"); var prior = queue.ToArray(); queue.Clear(); queue.Enqueue("enemy.arcade.brute");
        foreach (var id in prior) queue.Enqueue(id);
        Set(Runtime, "SpawnCooldown", 0f);
    }
    private static object Brute() => ((IDictionary)Get(Runtime, "Enemies")).Values.Cast<object>().FirstOrDefault(enemy => (string)Get(enemy, "ConfigId") == "enemy.arcade.brute");
    private static EnemyView BruteView(int id)
    {
        var combat = global::Game.Game.Interface.GetSystem<ArcadeCombatSystem>();
        var view = Get(combat, "view"); var enemies = Get(view, "enemyPresentation");
        var entries = (IDictionary)Get(enemies, "arcadeEnemies");
        return entries.Contains(id) ? (EnemyView)entries[id].GetType().GetProperty("View").GetValue(entries[id]) : null;
    }
    private static void Place(object enemy)
    {
        Set(enemy, "EnteringIsland", false);
        Set(enemy, "Position", (Vector2)Get(Runtime, "PlayerPosition") + new Vector2(2.5f, .2f));
    }
    private static void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static async UniTaskVoid Run()
    {
        Directory.CreateDirectory(Output); File.WriteAllText(Output + "/probe.log", "受控队首重型与一次近身摆位；其余由正式Spawn／追击／开火／受伤／死亡／池化驱动，不代表自然刷怪分布或实体机性能。\n");
        try
        {
            await Wait(() => ResourceLoader.IsConfigured && UIHelper.GetUICtrl<StartUICtrl>()?.IsShow() == true, 120, "菜单");
            var a = global::Game.Game.Interface; var runs = a.GetSystem<ArcadeRunSystem>(); var model = a.GetModel<ArcadeRunModel>();
            oldStore = Get(runs, "store"); oldClock = Get(runs, "clock"); oldFailure = Get(runs, "nextDeleteFailureForTests");
            oldRun = Get(model, "current"); oldPhase = Get(model, "<Phase>k__BackingField");
            directory = Path.Combine(Path.GetTempPath(), "FirepowerHeavy-" + Guid.NewGuid().ToString("N")); runs.ConfigureForTests(directory);
            UIHelper.HideUI<StartUICtrl>(); var menu = UIHelper.ShowUI<StartUICtrl>();
            ((StartUI)typeof(BaseUICtrl).GetField("BaseUI", Hidden).GetValue(menu)).BtnNewGame.onClick.Invoke();
            await Wait(() => Runtime != null && a.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing && !(bool)Get(Runtime, "IslandTransitionPending"), 100, "现场");
            runId = model.Current?.RunId; Assert(new ArcadeRunFileStore(directory).Inspect().Data?.RunId == runId, "临时局身份不符");
            background = InputSystem.settings.backgroundBehavior; routing = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            pad = InputSystem.AddDevice<Gamepad>("HeavyCombatProbe"); drive = true;
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            await Observe(2); EnqueueBrute(); await Wait(() => Brute() != null, 12, "重型生成");
            var heavy = Brute(); int id = (int)Get(heavy, "Id");
            await Wait(() => BruteView(id) != null, 5, "重型表现"); var firstView = BruteView(id); int instance = firstView.GetInstanceID();
            var skin = firstView.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(renderer => renderer.enabled);
            Assert(firstView.Animator.avatar.isValid && firstView.Animator.avatar.isHuman && skin.name.Contains("Brute"), "现役重型不是有效新模型");
            Log("Spawn id=" + id + " view=" + instance + " mesh=" + skin.sharedMesh.name + " health=" + Get(heavy, "Health") + " speed=" + Get(heavy, "Speed"));
            Place(heavy); var before = (Vector2)Get(heavy, "Position"); int healthBefore = (int)Get(Runtime, "PlayerHealth");
            await Observe(2); Assert(Vector2.Distance(before, (Vector2)Get(heavy, "Position")) > .1f, "重型未实际追击");
            ScreenCapture.CaptureScreenshot(Output + "/chasing.png");
            await Observe(3); Log("Contact attacks=" + Get(heavy, "AttackSequence") + " playerHealth=" + healthBefore + "->" + Get(Runtime, "PlayerHealth"));
            Assert((int)Get(heavy, "AttackSequence") > 0, "重型未进入正式攻击");
            var combat = a.GetSystem<ArcadeCombatSystem>(); combat.TryHealPlayerForTests(out _, out _);
            typeof(ArcadeCombatSystem).GetMethod("DamageEnemy", Hidden).Invoke(combat, new object[] { heavy, 1 });
            await Observe(.03); ScreenCapture.CaptureScreenshot(Output + "/hit.png");
            Log("受控1点正式伤害后Health=" + Get(heavy, "Health"));
            await Observe(6, heavy, true);
            Assert(!((IDictionary)Get(Runtime, "Enemies")).Contains(id), "实弹未击杀重型");
            ScreenCapture.CaptureScreenshot(Output + "/after-live-fire.png"); Log("实弹击杀完成，死亡／回池等待");
            await Observe(3); EnqueueBrute(); await Wait(() => Brute() != null, 12, "重型复用生成");
            var second = Brute(); int secondId = (int)Get(second, "Id"); await Wait(() => BruteView(secondId) != null, 5, "复用表现");
            var reused = BruteView(secondId); Assert(reused.GetInstanceID() == instance, "未实际复用同一重型View");
            var block = new MaterialPropertyBlock(); foreach (var renderer in reused.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { renderer.GetPropertyBlock(block); Assert(!block.HasColor("_BaseColor"), "池复用残留红色覆盖"); }
            Log("PoolReuse=True instance=" + instance + " newRuntime=" + secondId); Place(second); await Observe(4, second, true);
            if (pad != null) InputSystem.QueueStateEvent(pad, new GamepadState());
            // 只取当前局正常可受伤时的同步入口样本，不关闭无敌、不修改伤害规则。
            for (int i = 0; i < 8 && Runtime != null; i++)
            {
                await Wait(() => Runtime == null || ((float)Get(Runtime, "PlayerInvulnerability") <= 0 && (float)Get(Runtime, "RespawnRemaining") <= 0), 12, "普通受伤窗口");
                if (Runtime == null) break; combat.TryHealPlayerForTests(out _, out _);
                long bytes = GC.GetAllocatedBytesForCurrentThread(); var watch = System.Diagnostics.Stopwatch.StartNew();
                bool ok = combat.TryDamagePlayerForTests(1, out _, out var error); watch.Stop();
                Assert(ok, error); damageTimes.Add(watch.Elapsed.TotalMilliseconds); allocations.Add(GC.GetAllocatedBytesForCurrentThread() - bytes);
                await Observe(.8);
            }
            Log("DamageSamples=" + damageTimes.Count + " milliseconds=" + string.Join(",", damageTimes.Select(value => value.ToString("F3"))) + " allocatedCounterBytes=" + string.Join(",", allocations) + "；本机Mono计数返回0不构成零分配证明");
            // 走正式剩余生命及最终失败保存，单独报告，绝不将持久化归入闪红开销。
            for (int i = 0; i < 5 && Runtime != null; i++)
            {
                await Wait(() => Runtime == null || ((float)Get(Runtime, "PlayerInvulnerability") <= 0 && (float)Get(Runtime, "RespawnRemaining") <= 0), 15, "死亡窗口");
                if (Runtime == null) break;
                int lives = model.Current.RemainingLives; var watch = System.Diagnostics.Stopwatch.StartNew();
                combat.TryDamagePlayerForTests(100000, out var summary, out var error); watch.Stop();
                Log("Lethal lives=" + lives + " syncMs=" + watch.Elapsed.TotalMilliseconds.ToString("F3") + " " + summary + " " + error);
                await Observe(1);
            }
            var sorted = frames.OrderBy(value => value).ToArray();
            Log("EditorFrames=" + sorted.Length + " medianMs=" + sorted[sorted.Length / 2].ToString("F2") + " p95Ms=" + sorted[(int)(sorted.Length * .95)].ToString("F2") + " maxMs=" + sorted.Last().ToString("F2") + " over50=" + sorted.Count(value => value > 50));
            Log("PASS 重型追击／攻击／实弹死亡／同实例复用及受伤采样；性能仅本机Editor现状，无修改前对照。");
        }
        catch (Exception error) { Log("FAILED " + error); }
        finally
        {
            try { await Cleanup(); } catch (Exception error) { Log("CLEANUP FAILED " + error); }
            running = false;
        }
    }
    private static async UniTask Cleanup()
    {
        drive = false;
        if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
        if (pad != null) { InputSystem.settings.backgroundBehavior = background; InputSystem.settings.editorInputBehaviorInPlayMode = routing; pad = null; }
        var a = global::Game.Game.Interface; var runs = a.GetSystem<ArcadeRunSystem>();
        try
        {
            if (directory != null && a.GetModel<ArcadeRunModel>().Current?.RunId == runId)
            {
                if (!a.GetModel<ArcadeRunModel>().Current.HasResult && !runs.TryAbandon(out var error)) throw new Exception(error);
                var flow = a.GetSystem<ArcadeSceneFlowSystem>();
                // 最后一命已发起异步返菜单，先等它完成，避免重复返回和过早刷新菜单。
                await Wait(() => !(bool)Get(flow, "busy"), 20, "已有返菜单事务");
                if (a.GetModel<MapSceneModel>().CurrentHandle != null)
                {
                    var returned = await flow.ReturnToMainMenuAsync(); Assert(returned.Success, returned.Error);
                }
            }
        }
        finally
        {
            if (oldStore != null)
            {
                Set(runs, "store", oldStore); Set(runs, "clock", oldClock); Set(runs, "nextDeleteFailureForTests", oldFailure);
                var model = a.GetModel<ArcadeRunModel>(); Set(model, "current", oldRun); Set(model, "<Phase>k__BackingField", oldPhase);
                UIHelper.HideUI<StartUICtrl>(); UIHelper.ShowUI<StartUICtrl>(); oldStore = null;
            }
            Time.timeScale = 1; Log("Restored store/model/input; mapNull=" + (a.GetModel<MapSceneModel>().CurrentHandle == null));
        }
    }
}
