using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

/// <summary>只用临时本局档和正式掉落入口观察七枪；每次注入前核对现场，停止会取消初始化。</summary>
public static class ArcadeSpecialWeaponPlayProbe
{
    const string Output = "Temp/SpecialWeaponPlay/Actual";
    const string OwnerKey = "FirepowerLoop.Tools.SpecialWeaponProbe.Owner";
    // script-execute 每次加载新程序集；用 SessionState 定位原所有者，跨调用取消同一个事务。
    static string ownerToken;
    static readonly string[] Drops = { "", "rapid", "spread", "raygun", "rocket", "grenade", "piercing" };
    static CancellationTokenSource cancellation;
    static int generation, activeGeneration, stage, sample;
    static bool initializing, cleaning, reloadLocked, settingsSaved, mouseEnabled;
    static Gamepad pad;
    static Mouse mouse;
    static string directory, runId;
    static object activeRuntime;
    // ConfigureForTests 会替换存档依赖；回菜单后恢复原对象，不留下临时档路由。
    static object originalStore, originalClock, originalDeleteFailure;
    static double started;
    static Vector2 desiredMove, desiredAim;
    static bool[] capturedBlasts;
    static InputSettings.UpdateMode priorUpdate;
    static InputSettings.BackgroundBehavior priorBackground;
    static InputSettings.EditorInputBehaviorInPlayMode priorRouting;

    public static string Start()
    {
        if (!EditorApplication.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets") throw new Exception("需本项目Play");
        var owner = FindOwner();
        if (owner != null && (bool)owner.GetMethod("HasTransaction", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null)) return "已有观察事务，拒绝重复启动";
        ownerToken = Guid.NewGuid().ToString("N"); SessionState.SetString(OwnerKey, ownerToken);
        cancellation?.Dispose(); cancellation = new CancellationTokenSource();
        EditorApplication.playModeStateChanged += OnPlayState;
        initializing = true; Run(++generation, cancellation.Token).Forget(); return "七枪临时局初始化已启动";
    }
    static Type FindOwner()
    {
        string key = SessionState.GetString(OwnerKey, ""); if (string.IsNullOrEmpty(key)) return null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType("ArcadeSpecialWeaponPlayProbe", false);
            if (type != null && (string)type.GetField("ownerToken", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) == key) return type;
        }
        return null;
    }
    static bool HasTransaction() => initializing || cleaning || pad != null || directory != null;
    public static string State()
    {
        var owner = FindOwner(); if (owner == null) return "无观察所有者";
        if (owner != typeof(ArcadeSpecialWeaponPlayProbe)) return (string)owner.GetMethod("State").Invoke(null, null);
        return "initializing=" + initializing + " cleaning=" + cleaning + " input=" + (pad != null) + " generation=" + generation + " run=" + runId;
    }
    static async UniTaskVoid Run(int expectedGeneration, CancellationToken token)
    {
        try
        {
            Directory.CreateDirectory(Output);
            double deadline = EditorApplication.timeSinceStartup + 180;
            // 菜单暂停时间倍率，所有验证工具等待必须使用真实时间并响应取消。
            while (!Game.ResourceLoader.IsConfigured || !Buttons().Any(b => b.name == "BtnNewGame" || b.name == "BtnContinue"))
            { token.ThrowIfCancellationRequested(); if (EditorApplication.timeSinceStartup > deadline) throw new Exception("真实Boot菜单超时"); await UniTask.Delay(100, ignoreTimeScale: true, cancellationToken: token); }
            var a = Game.Game.Interface;
            var menu = await a.GetSystem<Game.ArcadeSceneFlowSystem>().ReturnToMainMenuAsync(token);
            token.ThrowIfCancellationRequested(); if (expectedGeneration != generation) return;
            if (!menu.Success) throw new Exception(menu.Error);
            directory = Path.Combine(Path.GetTempPath(), "FirepowerLoopSpecial-" + Guid.NewGuid().ToString("N"));
            var runSystem = a.GetSystem<Game.ArcadeRunSystem>();
            originalStore = RunField("store").GetValue(runSystem);
            originalClock = RunField("clock").GetValue(runSystem);
            originalDeleteFailure = RunField("nextDeleteFailureForTests").GetValue(runSystem);
            a.GetSystem<Game.ArcadeRunSystem>().ConfigureForTests(directory);
            Game.UIHelper.HideUI<Game.StartUICtrl>(); Game.UIHelper.ShowUI<Game.StartUICtrl>();
            deadline = EditorApplication.timeSinceStartup + 60; Button button;
            while ((button = Buttons().FirstOrDefault(b => b.name == "BtnNewGame")) == null)
            { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("临时档新局按钮超时"); await UniTask.Delay(100, ignoreTimeScale: true, cancellationToken: token); }
            token.ThrowIfCancellationRequested(); if (expectedGeneration != generation) return;
            button.onClick.Invoke();
            while (Runtime() == null || a.GetModel<Game.AppStateModel>().CurrentState.Value != Game.AppState.Playing || Game.UIHelper.HasUIOpen || (bool)Field(Runtime(), "IslandTransitionPending") || (bool)Field(Runtime(), "IslandRoomTransitionPending"))
            { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("正式战场初始化超时"); await UniTask.Delay(100, ignoreTimeScale: true, cancellationToken: token); }
            token.ThrowIfCancellationRequested(); if (expectedGeneration != generation) return;
            runId = new Game.ArcadeRunFileStore(directory).Inspect().Data?.RunId;
            if (runId == null || a.GetModel<Game.ArcadeRunModel>().Current?.RunId != runId) throw new Exception("临时档身份不符");
            activeRuntime = Runtime(); activeGeneration = expectedGeneration;
            File.WriteAllText(Output + "/play.log", "隔离临时档=" + directory + "\nRunId=" + runId + "\n正式新局按钮进入；每6秒正式测试掉落切枪，非自然拾取，不改生命、敌人或伤害。\n");
            BeginInput();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (expectedGeneration == generation) { ReleaseInput(); Directory.CreateDirectory(Output); File.AppendAllText(Output + "/play.log", "初始化失败：" + e + "\n"); } }
        finally { if (expectedGeneration == generation) initializing = false; }
    }
    static Button[] Buttons() => UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
    static object Runtime() => typeof(Game.ArcadeCombatModel).GetProperty("Runtime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Game.Game.Interface.GetModel<Game.ArcadeCombatModel>());
    static object Field(object value, string name) => value.GetType().GetField(name).GetValue(value);
    static FieldInfo RunField(string name) => typeof(Game.ArcadeRunSystem).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    static void RestoreStore()
    {
        if (originalStore == null) return;
        var system = Game.Game.Interface.GetSystem<Game.ArcadeRunSystem>();
        RunField("store").SetValue(system, originalStore);
        RunField("clock").SetValue(system, originalClock);
        RunField("nextDeleteFailureForTests").SetValue(system, originalDeleteFailure);
        originalStore = originalClock = originalDeleteFailure = null;
    }
    static bool OwnsRun() => EditorApplication.isPlaying && activeGeneration == generation && activeRuntime != null && ReferenceEquals(Runtime(), activeRuntime) && Game.Game.Interface.GetModel<Game.ArcadeRunModel>().Current?.RunId == runId;

    static void BeginInput()
    {
        // 只清理这些验证脚本自己创建的非原生设备，保留物理手柄和正式屏幕摇杆。
        foreach (var stale in Gamepad.all.Where(d => !d.native && (d.name.StartsWith("ThreeGunVisualProbe") || d.name.StartsWith("InfectedVisualProbe") || d.name.StartsWith("SpecialWeaponVisualProbe"))).ToArray()) InputSystem.RemoveDevice(stale);
        var input=(Game.PlayerInputCtrl)typeof(Game.ArcadeCombatSystem).GetField("input",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Game.Game.Interface.GetSystem<Game.ArcadeCombatSystem>());
        input.Enable(); // 只在已确认Playing且无阻断界面后恢复正式输入，不绕过释放闸门。
        EditorApplication.LockReloadAssemblies(); reloadLocked = true;
        priorUpdate = InputSystem.settings.updateMode; priorBackground = InputSystem.settings.backgroundBehavior;
        priorRouting = InputSystem.settings.editorInputBehaviorInPlayMode; settingsSaved = true;
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        mouse = Mouse.current; mouseEnabled = mouse != null && mouse.enabled; if (mouseEnabled) InputSystem.DisableDevice(mouse);
        pad = InputSystem.AddDevice<Gamepad>("SpecialWeaponVisualProbe"); desiredMove = Vector2.zero; desiredAim = Vector2.zero;
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        started = EditorApplication.timeSinceStartup; stage = sample = 0; capturedBlasts = new bool[7];
        InputSystem.onBeforeUpdate += BeforeInput; EditorApplication.update += Tick;
    }
    static void OnPlayState(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Stop(); }
    static void BeforeInput()
    {
        if (pad == null) return;
        if (!OwnsRun()) { Stop(); return; }
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = desiredMove, rightStick = desiredAim });
    }
    static void Tick()
    {
        try
        {
            if (!OwnsRun()) { Stop(); File.AppendAllText(Output + "/play.log", "现场身份变化，已停止注入\n"); return; }
            var a = Game.Game.Interface; var combat = a.GetSystem<Game.ArcadeCombatSystem>(); var r = activeRuntime;
            double elapsed = EditorApplication.timeSinceStartup - started; int desired = Mathf.Min(6, (int)(elapsed / 6));
            if (desired != stage)
            {
                string drop = "arcade.drop.weapon." + Drops[desired];
                bool applied = combat.TryApplyDropForTests(drop, out var message, out var error);
                File.AppendAllText(Output + "/play.log", drop + "=" + applied + " " + message + " " + error + "\n"); stage = desired;
            }
            var p = (Vector2)Field(r, "PlayerPosition");
            var target = new Vector2(Mathf.Cos((float)elapsed * .3f), Mathf.Sin((float)elapsed * .3f)) * 8;
            desiredMove = (target - p).sqrMagnitude > .1f ? (target - p).normalized : Vector2.zero; desiredAim = Vector2.up;
            float closest = float.MaxValue;
            foreach (var enemy in ((IDictionary)Field(r, "Enemies")).Values)
            { var d = (Vector2)Field(enemy, "Position") - p; if (d.sqrMagnitude > .0001f && d.sqrMagnitude < closest) { closest = d.sqrMagnitude; desiredAim = d.normalized; } }
            // 开局和每段按压前留出真实释放帧，遵守暂停/切视角后的旧按压闸门。
            if (elapsed % 1.5 < .18) desiredAim = Vector2.zero;
            var owner = a.GetModel<Game.MapSceneModel>().CurrentHandle.Context.DynamicObjectRoot;
            if (stage >= 3 && stage <= 5 && !capturedBlasts[stage] && elapsed >= stage * 6 + 3)
            {
                var flash = owner.GetComponentsInChildren<ParticleSystem>().FirstOrDefault(ps => ps.name == "ImpactFlash" && ps.particleCount > 0 && ps.time >= .08f && (stage == 3 ? ps.main.startColor.color.b > ps.main.startColor.color.r : ps.main.startColor.color.r > ps.main.startColor.color.b));
                if (flash != null)
                {
                    capturedBlasts[stage] = true;
                    ScreenCapture.CaptureScreenshot(Output + "/blast-" + stage + ".png");
                    CaptureBlastDetail(flash.transform.position, stage);
                    File.AppendAllText(Output + "/play.log", "实际爆心 stage=" + stage + " particles=" + flash.particleCount + " color=" + flash.main.startColor.color + "；detail为额外俯视诊断镜头，未移动正式相机或粒子\n");
                }
            }
            if (elapsed >= sample)
            {
                combat.TryGetTestStatus(out var message, out var error);
                var input = (Game.PlayerInputCtrl)typeof(Game.ArcadeCombatSystem).GetField("input", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(combat);
                var projectiles = owner.GetComponentsInChildren<Game.ProjectileView>();
                string visuals = string.Join(";", projectiles.SelectMany(v => v.GetComponentsInChildren<MeshRenderer>()).Where(v => v.enabled).Select(v => v.name).Distinct());
                File.AppendAllText(Output + "/play.log", elapsed.ToString("F2") + " stage=" + stage + " fireSequence=" + Field(r, "PlayerFireSequence") + " queued=" + desiredAim + " aim=" + input?.AimInput + " " + message + " " + error + " visible=" + visuals + "\n");
                if (sample % 6 == 4) ScreenCapture.CaptureScreenshot(Output + "/gun-" + stage + ".png"); sample++;
            }
            if (elapsed > 42) { Stop(); File.AppendAllText(Output + "/play.log", "观察完成，输入/路由/重载锁已恢复\n"); }
        }
        catch (Exception e) { Stop(); File.AppendAllText(Output + "/play.log", "观察异常：" + e + "\n"); }
    }
    /// <summary>爆心可能在主角镜头外；额外镜头只渲染当前真实现场，不改相机、时间或粒子模拟。</summary>
    static void CaptureBlastDetail(Vector3 center, int index)
    {
        var source = Camera.main; if (source == null) return;
        var go = new GameObject("SpecialWeaponDiagnosticCamera");
        var target = new RenderTexture(1280, 720, 24);
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            var camera = go.AddComponent<Camera>(); camera.CopyFrom(source); camera.enabled = false;
            camera.orthographic = true; camera.orthographicSize = 5; camera.aspect = 1280f / 720;
            camera.transform.SetPositionAndRotation(center + Vector3.up * 15, Quaternion.Euler(90, 0, 0));
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            File.WriteAllBytes(Output + "/blast-detail-" + index + ".png", texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(texture);
        }
    }
    public static string Stop()
    {
        var owner = FindOwner();
        if (owner != null && owner != typeof(ArcadeSpecialWeaponPlayProbe)) return (string)owner.GetMethod("Stop").Invoke(null, null);
        generation++; cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null; initializing = cleaning = false;
        ReleaseInput(); return "异步观察与临时输入已停止";
    }
    static void ReleaseInput()
    {
        InputSystem.onBeforeUpdate -= BeforeInput; EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= OnPlayState;
        try
        {
            if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
            if (mouseEnabled && mouse != null && mouse.added) InputSystem.EnableDevice(mouse);
        }
        finally
        {
            if (settingsSaved) { InputSystem.settings.updateMode = priorUpdate; InputSystem.settings.backgroundBehavior = priorBackground; InputSystem.settings.editorInputBehaviorInPlayMode = priorRouting; settingsSaved = false; }
            pad = null; mouse = null; mouseEnabled = false;
            if (reloadLocked) { reloadLocked = false; EditorApplication.UnlockReloadAssemblies(); }
        }
    }
    /// <summary>先取消等待与输入，再核对临时档和RunId，正式放弃、回主菜单并退出Play。</summary>
    public static string Cleanup()
        => BeginCleanup(true);
    public static string CleanupToMenu()
        => BeginCleanup(false);
    static string BeginCleanup(bool exitPlay)
    {
        var owner = FindOwner(); if (owner == null) return "无观察所有者，未改变当前局";
        if (owner != typeof(ArcadeSpecialWeaponPlayProbe)) return (string)owner.GetMethod(exitPlay ? "Cleanup" : "CleanupToMenu").Invoke(null, null);
        if (directory == null) { Stop(); return "等待已取消，没有临时局需要清理"; }
        // 停止可能发生在新局按钮之后、Runtime就绪之前，仍只接受本工具目录中的同一RunId。
        if (runId == null) runId = new Game.ArcadeRunFileStore(directory).Inspect().Data?.RunId;
        if (runId == null) { Stop(); RestoreStore(); directory = null; return "没有匹配临时档，已恢复原存档依赖"; }
        Stop(); cleaning = true; cancellation = new CancellationTokenSource(); EditorApplication.playModeStateChanged += OnPlayState;
        CleanupAsync(++generation, cancellation.Token, exitPlay).Forget(); return "临时局清理已启动";
    }
    static async UniTaskVoid CleanupAsync(int expectedGeneration, CancellationToken token, bool exitPlay)
    {
        try
        {
            var a = Game.Game.Interface;
            if (string.IsNullOrEmpty(directory) || !Path.GetFileName(directory).StartsWith("FirepowerLoopSpecial-") ||
                new Game.ArcadeRunFileStore(directory).Inspect().Data?.RunId != runId || a.GetModel<Game.ArcadeRunModel>().Current?.RunId != runId)
                throw new Exception("拒绝清理身份不符的局");
            if (!a.GetSystem<Game.ArcadeRunSystem>().TryAbandon(out var error)) throw new Exception(error);
            var result = await a.GetSystem<Game.ArcadeSceneFlowSystem>().ReturnToMainMenuAsync(token);
            token.ThrowIfCancellationRequested(); if (expectedGeneration != generation) return;
            if (!result.Success) throw new Exception(result.Error);
            string currentId = a.GetModel<Game.ArcadeRunModel>().Current?.RunId;
            if (currentId != null && currentId != runId) throw new Exception("清理期间已有另一局，拒绝退出Play");
            RestoreStore();
            Time.timeScale = 1; File.AppendAllText(Output + "/play.log", "临时局正式放弃，地图句柄=" + (a.GetModel<Game.MapSceneModel>().CurrentHandle == null ? "null" : "存在") + " TimeScale=" + Time.timeScale + "\n");
            directory = runId = null; activeRuntime = null;
            if (exitPlay) EditorApplication.isPlaying = false;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Directory.CreateDirectory(Output); File.AppendAllText(Output + "/play.log", "清理失败：" + e + "\n"); }
        finally { if (expectedGeneration == generation) { cleaning = false; cancellation?.Dispose(); cancellation = null; EditorApplication.playModeStateChanged -= OnPlayState; } }
    }
}
