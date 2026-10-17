using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

/// <summary>临时存档隔离武器起点，以正式掉落入口切枪并用虚拟手柄实战射击；不改生命、敌人或伤害。</summary>
public static class ArcadeInfectedSoldierPlayProbe
{
    static string Output => wallMode ? "Temp/InfectedIslandPlay/Wall" : "Temp/InfectedIslandPlay/Actual";
    static bool wallMode;
    static bool reloadLocked;
    // 仅验证期间改变输入路由，退出或异常时恢复原设置，避免Console抢焦点使样本无效。
    static InputSettings.BackgroundBehavior priorBackground; static InputSettings.EditorInputBehaviorInPlayMode priorRouting; static bool settingsChanged;
    static bool[] capturedSurface = new bool[3];
    static Gamepad pad; static Mouse mouse; static bool mouseEnabled;
    static double started; static int stage, sample; static string temp;
    public static string Start() => Begin(false);
    /// <summary>同一正式新局观察流程，改为移动到首图障碍前开火，不生成测试敌人或弹丸。</summary>
    public static string StartWalls() => Begin(true);
    static string Begin(bool walls) { wallMode = walls; Run().Forget(); return "三枪隔离实玩起点初始化"; }
    static async UniTaskVoid Run()
    {
        try
        {
            if (!EditorApplication.isPlaying || Application.dataPath.Replace('\\','/') != "E:/GitHub/FirepowerLoop/Client/Assets") throw new Exception("需本项目Play");
            Directory.CreateDirectory(Output);
            temp = Path.Combine(Path.GetTempPath(), "FirepowerLoopInfected-" + Guid.NewGuid().ToString("N"));
            var a = Game.Game.Interface;
            var runs = a.GetSystem<Game.ArcadeRunSystem>();
            var menu = await a.GetSystem<Game.ArcadeSceneFlowSystem>().ReturnToMainMenuAsync();
            if (!menu.Success) throw new Exception(menu.Error);
            runs.ConfigureForTests(temp);
            Game.UIHelper.HideUI<Game.StartUICtrl>(); Game.UIHelper.ShowUI<Game.StartUICtrl>();
            double deadline = EditorApplication.timeSinceStartup + 60;
            Button button;
            while ((button = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(b => b.name == "BtnNewGame")) == null)
            { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("主菜单超时"); await UniTask.Delay(100); }
            button.onClick.Invoke();
            while (typeof(Game.ArcadeCombatModel).GetProperty("Runtime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(a.GetModel<Game.ArcadeCombatModel>()) == null)
            { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("正式新局现场准备超时"); await UniTask.Delay(100); }
            File.WriteAllText(Output + "/play.log", "隔离临时档=" + temp + "\n正式新局按钮进入Island第一轮；非自然拾取，后续用正式测试掉落入口切枪。\n");
            Drive();
        }
        catch (Exception e) { Stop(); Directory.CreateDirectory(Output); File.AppendAllText(Output + "/play.log", e + "\n"); }
    }
    static object Field(object obj, string name) => obj.GetType().GetField(name).GetValue(obj);
    /// <summary>只重试日志所绑定的隔离局输入，不覆盖存档或重新创建权威现场。</summary>
    public static string Drive()
    {
        temp = File.ReadLines(Output + "/play.log").First().Substring("隔离临时档=".Length);
        if (!Path.GetFileName(temp).StartsWith("FirepowerLoopInfected-") ||
            new Game.ArcadeRunFileStore(temp).Inspect().Data?.RunId != Game.Game.Interface.GetModel<Game.ArcadeRunModel>().Current?.RunId)
            throw new Exception("隔离局身份不符，拒绝重试");
        Stop();
        try
        {
        // GameView聚焦可能触发自动刷新；只在24秒观察期间持有本工具的重载锁，结束或异常均释放。
        EditorApplication.LockReloadAssemblies(); reloadLocked = true;
        priorBackground=InputSystem.settings.backgroundBehavior;priorRouting=InputSystem.settings.editorInputBehaviorInPlayMode;settingsChanged=true;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        mouse = Mouse.current; mouseEnabled = mouse != null && mouse.enabled;
        if (mouseEnabled) InputSystem.DisableDevice(mouse);
        pad = InputSystem.AddDevice<Gamepad>("InfectedVisualProbe");
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        started = EditorApplication.timeSinceStartup; stage = sample = 0;
        capturedSurface = new bool[3];
        File.AppendAllText(Output + "/play.log", "聚焦GameView后开始输入观察\n");
        EditorApplication.update += Tick; return "当前隔离局输入已启动";
        }
        catch { Stop(); throw; }
    }
    static void Tick()
    {
        try { TickCore(); }
        catch (Exception e) { Stop(); File.AppendAllText(Output + "/play.log", "观察异常：" + e + "\n"); }
    }
    static void TickCore()
    {
        if (!EditorApplication.isPlaying || pad == null) { Stop(); return; }
        var a = Game.Game.Interface; var combat = a.GetSystem<Game.ArcadeCombatSystem>();
        var r = typeof(Game.ArcadeCombatModel).GetProperty("Runtime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(a.GetModel<Game.ArcadeCombatModel>());
        if (r == null) { File.AppendAllText(Output + "/play.log", "现场结束\n"); Stop(); return; }
        double elapsed = EditorApplication.timeSinceStartup - started;
        int desired = elapsed >= 16 ? 2 : elapsed >= 8 ? 1 : 0;
        if (desired != stage)
        {
            string drop = desired == 1 ? "arcade.drop.weapon.rapid" : "arcade.drop.weapon.spread";
            bool applied = combat.TryApplyDropForTests(drop, out var text, out var error);
            File.AppendAllText(Output + "/play.log", drop + "=" + applied + " " + text + " " + error + "\n"); stage = desired;
        }
        var p = (Vector2)Field(r, "PlayerPosition");
        var target = wallMode ? new Vector2(8, -4) : new Vector2(Mathf.Cos((float)elapsed * .3f), Mathf.Sin((float)elapsed * .3f)) * 8;
        var aim = wallMode ? (new Vector2(12, -4) - p).normalized : Vector2.up;
        if (!wallMode)
        {
            float closest = float.MaxValue;
            foreach (var enemy in ((IDictionary)Field(r, "Enemies")).Values)
            { var d = (Vector2)Field(enemy, "Position") - p; if (d.sqrMagnitude < closest) { closest = d.sqrMagnitude; aim = d.normalized; } }
        }
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = (target - p).sqrMagnitude > .1f ? (target - p).normalized : Vector2.zero, rightStick = aim });
        if (wallMode && !capturedSurface[stage] && elapsed >= stage * 8 + 3)
        {
            var owner = a.GetModel<Game.MapSceneModel>().CurrentHandle.Context.DynamicObjectRoot;
            var sparks = owner.GetComponentsInChildren<ParticleSystem>().FirstOrDefault(ps => ps.name.StartsWith("BulletImpact") && ps.shape.shapeType == ParticleSystemShapeType.Cone && ps.particleCount > 0);
            if (sparks != null)
            {
                capturedSurface[stage] = true;
                ScreenCapture.CaptureScreenshot(Output + "/surface-" + stage + ".png");
                File.AppendAllText(Output + "/play.log", "实际墙面火星 stage=" + stage + " position=" + sparks.transform.localPosition + " particles=" + sparks.particleCount + "\n");
            }
        }
        if (elapsed >= sample)
        {
            combat.TryGetTestStatus(out var text, out var error);
            var input=(Game.PlayerInputCtrl)typeof(Game.ArcadeCombatSystem).GetField("input",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(combat);
            File.AppendAllText(Output+"/play.log","focus="+Application.isFocused+" window="+EditorWindow.focusedWindow?.GetType().Name+" enabled="+input?.IsEnabled+" move="+input?.MoveInput+" aim="+input?.AimInput+" pad="+pad?.rightStick.ReadValue()+" app="+a.GetModel<Game.AppStateModel>().CurrentState.Value+" blocking="+Game.UIHelper.HasUIOpen+"\n");
            var map = a.GetModel<Game.MapSceneModel>();
            var projectiles = map.CurrentHandle.Context.DynamicObjectRoot.GetComponentsInChildren<Game.ProjectileView>();
            var enemies=map.CurrentHandle.Context.DynamicObjectRoot.GetComponentsInChildren<Game.EnemyView>();
            string bodies=string.Join(";",enemies.SelectMany(e=>e.GetComponentsInChildren<SkinnedMeshRenderer>()).Where(s=>s.enabled).Select(s=>s.sharedMesh.name).Distinct());
            File.AppendAllText(Output+"/play.log","enemyViews="+enemies.Length+" bodyMeshes="+bodies+"\n");
            string visuals = string.Join(";", projectiles.SelectMany(v => v.GetComponentsInChildren<MeshRenderer>()).Where(v => v.enabled).Select(v => v.name).Distinct());
            File.AppendAllText(Output + "/play.log", elapsed.ToString("F2") + " stage=" + stage + " " + text + " " + error + " visible=" + visuals + "\n");
            if (sample == 5 || sample == 13 || sample == 21) ScreenCapture.CaptureScreenshot(Output + "/gun-" + stage + ".png");
            sample++;
        }
        if (elapsed > 24) { Stop(); File.AppendAllText(Output + "/play.log", "观察完成，输入设备已回收\n"); }
    }
    public static string Stop()
    {
        EditorApplication.update -= Tick;
        try
        {
            if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
            if (mouseEnabled && mouse != null && mouse.added) InputSystem.EnableDevice(mouse);
        }
        finally
        {
            if(settingsChanged){InputSystem.settings.backgroundBehavior=priorBackground;InputSystem.settings.editorInputBehaviorInPlayMode=priorRouting;settingsChanged=false;}
            pad = null; mouse = null; mouseEnabled = false;
            if (reloadLocked) { reloadLocked = false; EditorApplication.UnlockReloadAssemblies(); }
        }
        return "临时输入已释放";
    }

    /// <summary>核对临时档身份后正式放弃和卸载；不写玩家正式档，退出Play前恢复时间倍率。</summary>
    public static string Cleanup() => BeginCleanup(false);
    public static string CleanupWalls() => BeginCleanup(true);
    static string BeginCleanup(bool walls) { wallMode = walls; CleanupAsync().Forget(); return "隔离局清理已启动"; }
    static async UniTaskVoid CleanupAsync()
    {
        try
        {
            string directory = File.ReadLines(Output + "/play.log").First().Substring("隔离临时档=".Length);
            var a = Game.Game.Interface; var current = a.GetModel<Game.ArcadeRunModel>().Current;
            if (!Path.GetFileName(directory).StartsWith("FirepowerLoopInfected-") ||
                new Game.ArcadeRunFileStore(directory).Inspect().Data?.RunId != current?.RunId) throw new Exception("拒绝清理身份不符的局");
            if (!a.GetSystem<Game.ArcadeRunSystem>().TryAbandon(out var error)) throw new Exception(error);
            var result = await a.GetSystem<Game.ArcadeSceneFlowSystem>().ReturnToMainMenuAsync();
            if (!result.Success) throw new Exception(result.Error);
            Time.timeScale = 1;
            File.AppendAllText(Output + "/play.log", "临时局已正式放弃，地图句柄=" + (a.GetModel<Game.MapSceneModel>().CurrentHandle == null ? "null" : "存在") + " TimeScale=" + Time.timeScale + "\n");
            EditorApplication.isPlaying = false;
        }
        catch (Exception e) { File.AppendAllText(Output + "/play.log", "清理失败：" + e + "\n"); }
    }
}
