using System;
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

/// <summary>短时真实输入验收探针；只用临时档定位第五轮，不改敌人、生命、命中或奖励。</summary>
public static class ArcadeBabylonPlayProbe
{
    const string Output = "Assets/Tests/Results~/ArcadeBabylon-20261012";
    static Gamepad pad;
    static Mouse mouse;
    static Mouse virtualMouse;
    static bool mouseEnabled;
    static double started;
    static bool first, broken;
    static bool armorOnly;
    public static string Start()
    {
        Run().Forget(); return "Temporary save/map initialization started";
    }
    public static string StartArmor() { armorOnly = true; return Start(); }
    static async UniTaskVoid Run()
    {
        try
        {
            Directory.CreateDirectory(Output);
            string temp = Path.Combine(Path.GetTempPath(), "AnotherDayBabylonPlay-" + Guid.NewGuid().ToString("N"));
            var a = Game.Game.Interface;
            a.GetSystem<Game.SaveSystem>().ConfigureForTests(temp);
            a.GetSystem<Game.ArcadeRunSystem>().ConfigureForTests(temp);
            double deadline = EditorApplication.timeSinceStartup + 25;
            Button startButton = null;
            while ((startButton = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(b => b.name == "BtnNewGame")) == null)
            { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("startup UI timeout"); await UniTask.Delay(100); }
            startButton.onClick.Invoke();
            while (a.GetModel<Game.MapSceneModel>().CurrentMapId != Game.MapIds.Base)
            { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("base load timeout"); await UniTask.Delay(100); }
            var runs = a.GetSystem<Game.ArcadeRunSystem>();
            if (!runs.TryStartConfiguredRun(out var data, out var error)) throw new Exception(error);
            data.NextArenaNumber = 2; data.CompletedArenaCount = 1; data.ArenaConfigNumber = 2;
            if (!runs.TryBuildConfiguredShop(2, 123, data, out var shop, out error)) throw new Exception(error);
            data.Shop = shop;
            if (!new Game.ArcadeRunFileStore(temp).Save(data, out error)) throw new Exception(error);
            if (!runs.TryResume(out _, out error)) throw new Exception(error);
            var loaded = await a.GetSystem<Game.ArcadeSceneFlowSystem>().EnterArenaMapAsync();
            if (!loaded.Success) throw new Exception(loaded.Error);
            if (!runs.TryContinue(out _, out error)) throw new Exception(error);
            Game.UIHelper.ShowUI<Game.ArcadeHudUICtrl>();
            pad = InputSystem.AddDevice<Gamepad>("BabylonProbePad"); mouse = Mouse.current; mouseEnabled = mouse != null && mouse.enabled;
            if (mouseEnabled) InputSystem.DisableDevice(mouse);
            virtualMouse = InputSystem.AddDevice<Mouse>("BabylonProbeMouse");
            started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            File.AppendAllText(Output + "/play.log", "Temporary root=" + temp + "\nMap and domain API entered fifth round; formal start button bypassed.\n");
        }
        catch (Exception e) { File.AppendAllText(Output + "/play.log", e.ToString()); Stop(); }
    }
    static object Field(object r, string n) => r.GetType().GetField(n).GetValue(r);
    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Stop(); return; }
        var a = Game.Game.Interface;
        var r = typeof(Game.ArcadeCombatModel).GetProperty("Runtime", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(a.GetModel<Game.ArcadeCombatModel>());
        if (r == null) { Stop(); return; }
        var p = (Vector2)Field(r, "PlayerPosition");
        var target = new Vector2(-12, -12);
        var move = (target - p).sqrMagnitude > .25f ? (target - p).normalized : Vector2.zero;
        var aim = !armorOnly && move == Vector2.zero ? (new Vector2(-15, -12) - p).normalized : Vector2.zero;
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = move, rightStick = aim });
        var view = a.GetUtility<Game.IArcadeCombatViewPort>();
        var mapRoot = (Transform)view.GetType().GetField("mapRoot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
        if (mapRoot != null && Camera.main != null)
        {
            var screen = Camera.main.WorldToScreenPoint(mapRoot.TransformPoint(new Vector3(-15, .6f, -12)));
            InputSystem.QueueStateEvent(virtualMouse, new MouseState { position = new Vector2(screen.x, screen.y) });
        }
        double elapsed = EditorApplication.timeSinceStartup - started;
        if (!armorOnly && !first && elapsed > 1) { first = true; ScreenCapture.CaptureScreenshot(Output + "/round5-before.png"); }
        if (!broken && elapsed > 8)
        {
            broken = true; ScreenCapture.CaptureScreenshot(Output + (armorOnly ? "/round5-armor.png" : "/round5-after-shooting.png"));
            a.GetSystem<Game.ArcadeCombatSystem>().TryGetTestStatus(out var summary, out _);
            File.AppendAllText(Output + "/play.log", summary + "\nCrates=" + ((System.Collections.IDictionary)Field(r, "Crates")).Count + "\n");
        }
        if (elapsed > 9) Stop();
    }
    public static string Stop()
    {
        EditorApplication.update -= Tick;
        if (pad != null && pad.added) InputSystem.RemoveDevice(pad); pad = null;
        if (virtualMouse != null && virtualMouse.added) InputSystem.RemoveDevice(virtualMouse); virtualMouse = null;
        if (mouseEnabled && mouse != null && mouse.added) InputSystem.EnableDevice(mouse);
        mouse = null; mouseEnabled = false; return "Probe input released";
    }
}
