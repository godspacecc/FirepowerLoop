using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Game;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>用正式 Prefab 和示例权威快照渲染已确认的 PC HUD；隔离临时场景与架构，不写入玩家数据。</summary>
public static class DropHudFullPreview
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string Run()
    {
        if (EditorApplication.isPlaying || !Application.dataPath.Replace('\\', '/').EndsWith("FirepowerLoop/Client/Assets"))
            throw new InvalidOperationException("仅在 FirepowerLoop 编辑器非运行模式预览");
        var architectureField = typeof(Architecture<Game.Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
        var priorArchitecture = architectureField.GetValue(null);
        var priorLoader = ResourceLoader.Current;
        var languageCache = typeof(LocalizationLanguages).GetField("cachedConfig", BindingFlags.Static | BindingFlags.NonPublic);
        var priorLanguages = languageCache.GetValue(null);
        ResourceLoader.Use(new PreviewResourceLoader());
        LocalizationLanguages.ResetCache();
        var architecture = new Game.Game();
        architectureField.SetValue(null, architecture);
        typeof(Architecture<Game.Game>).GetField("mInited", Private).SetValue(architecture, true);
        architecture.RegisterModel(new LocalizationModel());
        architecture.RegisterModel(new ArcadeRunModel());
        architecture.RegisterModel(new MobileControlLayoutModel());
        architecture.RegisterModel(new HotKeyModel());
        architecture.RegisterModel(new AppStateModel());
        architecture.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
        architecture.RegisterSystem(new MobileInputSystem());
        var localization = new LocalizationUtility(false);
        foreach (var language in new[] { "zh-CN", "en-US" })
            localization.LoadFromJson(language, File.ReadAllText("Assets/Game/Config/Localization/" + language + ".json"));
        typeof(LocalizationUtility).GetField("loadedConfiguredTables", Private).SetValue(localization, true);
        architecture.RegisterUtility(localization);
        architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() => new Tables(name =>
            new Luban.ByteBuf(File.ReadAllBytes("Assets/Game/Config/" + name + ".bytes")))));
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var oldTexture = RenderTexture.active;
        const string folder = "../Backup/Artifacts/DropHudScale-v1-20261024";
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var spec in new[] {
                ("hud-pc-progress-current", 1920, 1080, false, 1, "zh-CN", true),
                ("hud-pc-progress-candidate", 1920, 1080, false, 1, "zh-CN", true),
                ("hud-pc-zero-current", 1920, 1080, false, 1, "zh-CN", true),
                ("hud-pc-zero-candidate", 1920, 1080, false, 1, "zh-CN", true),
                ("hud-phone-zero-current", 1920, 1080, true, 1, "zh-CN", true),
                ("hud-phone-zero-candidate", 1920, 1080, true, 1, "zh-CN", true) })
            {
                architecture.GetModel<LocalizationModel>().CurrentLanguageCode.Value = spec.Item6;
                architecture.GetSystem<MobileInputSystem>().TrySetEditorSimulation(spec.Item4);
                var camera = new GameObject("PreviewCamera", typeof(Camera)).GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.035f, .05f, .07f);
                camera.transform.position = new Vector3(0, 0, -10);
                var target = new RenderTexture(spec.Item2, spec.Item3, 24);
                var image = new Texture2D(spec.Item2, spec.Item3, TextureFormat.RGB24, false);
                var canvasRoot = new GameObject("PreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                try
                {
                    camera.targetTexture = target;
                    var canvas = canvasRoot.GetComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 1;
                    var scaler = canvasRoot.GetComponent<CanvasScaler>();
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920, 1080);
                    scaler.matchWidthOrHeight = .5f;
                    var hud = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"), canvasRoot.transform, false);
                    foreach (var fitter in hud.GetComponentsInChildren<SafeAreaFitter>(true)) fitter.enabled = false;
                    var ui = hud.GetComponent<ArcadeHudUI>();
                    var controller = new ArcadeHudUICtrl();
                    typeof(BaseUICtrl).GetField("BaseUI", Private).SetValue(controller, ui);
                    Call(controller, "OnInit");
                    typeof(ArcadeHudUICtrl).GetField("combat", Private).SetValue(controller, new ArcadeCombatHudSnapshot {
                        ArenaNumber = spec.Item7 ? 4 : 1, WaveNumber = 3, TotalWaves = 4, RemainingEnemies = 37,
                        PlayerHealth = 75, PlayerMaxHealth = 100, Bombs = 1, Dashes = 2,
                        TemporaryWeaponNameKey = "ui.arcade.product.arcade.weapon.rapid.name", TemporaryWeaponSeconds = 12.5f,
                        Powerups = new[] { new ArcadePowerupHudEntry("ui.arcade.product.arcade.powerup.skull.small.name", 10, 0),
                            new ArcadePowerupHudEntry("ui.arcade.product.arcade.powerup.skull.large.name", 8, 0) },
                        SlayerMartRulesVersion = 1, HasStorm = spec.Item7, StormCharge = 75, VestSlots = spec.Item7 ? 2 : 0, Plates = 1 });
                    typeof(ArcadeHudUICtrl).GetField("hasCombatSnapshot", Private).SetValue(controller, true);
                    Call(controller, "Render", new ArcadeRunSaveData {
                        Score = 42350, ScoreMultiplier = 3, MultiplierProgress = spec.Item1.Contains("zero") ? 0 : 600, RemainingLives = 3, NextArenaNumber = 1 });
                    if (spec.Item5 == 2) RenderCoop(controller);
                    Call(controller, "ApplyInputLayout", spec.Item4, spec.Item5 == 2);
                    ui.MoveJoystickRoot.SetActive(spec.Item4);
                    ui.AimJoystickRoot.SetActive(spec.Item4);
                    Call(controller, "SyncFieldUpgradeUi");
                    // 候选只改临时HUD实例，正式图标、Prefab及Controller不写入。
                    if (spec.Item1.EndsWith("candidate"))
                    {
                        foreach (var box in hud.GetComponentsInChildren<ArcadeEffectBoxUI>(true))
                            if (box.Icon.sprite == ui.SkullRedIcon || box.Icon.sprite == ui.SkullPurpleIcon)
                                box.Icon.rectTransform.localScale *= .9f;
                        if (spec.Item1.Contains("zero"))
                            foreach (var row in hud.GetComponentsInChildren<ArcadePlayerStatusBoxUI>(true))
                            {
                                row.MultiplierFill.transform.parent.gameObject.SetActive(false);
                                row.MobileMultiplierFill.transform.parent.gameObject.SetActive(false);
                            }
                    }
                    Canvas.ForceUpdateCanvases();
                    if (spec.Item1 == "phone-coop")
                    {
                        var safe = hud.AddComponent<SafeAreaFitter>();
                        safe.enabled = false;
                        typeof(SafeAreaFitter).GetMethod("ApplyAnchors", Private | BindingFlags.Public).Invoke(safe,
                            new object[] { new Vector2(.03f, .02f), new Vector2(.97f, .98f) });
                    }
                    Call(controller, "ApplyJoystickLayout");
                    for (var pass = 0; pass < 3; pass++)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)hud.transform);
                        Canvas.ForceUpdateCanvases();
                    }
                    foreach (var text in hud.GetComponentsInChildren<TextMeshProUGUI>()) text.ForceMeshUpdate();
                    camera.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, spec.Item2, spec.Item3), 0, 0);
                    image.Apply();
                    File.WriteAllBytes(folder + "/" + spec.Item1 + ".png", image.EncodeToPNG());
                    File.WriteAllLines(folder + "/" + spec.Item1 + ".txt", hud.GetComponentsInChildren<TextMeshProUGUI>()
                        .Select(t => t.name + " overflow=" + t.isTextOverflowing + " text=" + t.text));
                }
                finally
                {
                    Object.DestroyImmediate(canvasRoot);
                    camera.targetTexture = null;
                    Object.DestroyImmediate(camera.gameObject);
                    RenderTexture.active = oldTexture;
                    target.Release();
                    Object.DestroyImmediate(target);
                    Object.DestroyImmediate(image);
                }
            }
            return Path.GetFullPath(folder);
        }
        finally
        {
            RenderTexture.active = oldTexture;
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
            architecture.Deinit();
            architectureField.SetValue(null, priorArchitecture);
            ResourceLoader.Use(priorLoader);
            languageCache.SetValue(null, priorLanguages);
        }
    }

    /// <summary>双人样例使用正式联机快照，复用同一玩家行渲染入口。</summary>
    private static void RenderCoop(ArcadeHudUICtrl controller)
    {
        var players = Enumerable.Range(1, 2).Select(i => new OnlineArcadePlayerSnapshot(i, "Player " + i,
            3, 0, 1, 2, "arcade.weapon.basic", false, null, score: i == 1 ? 42350 : 16800,
            scoreMultiplier: i == 1 ? 3 : 2, multiplierProgress: i * 300)).ToArray();
        var battlePlayers = Enumerable.Range(1, 2).Select(i => new OnlineArcadeBattlePlayerSnapshot(i,
            i, i, 0, 1, i == 1 ? 75 : 50, 100, true, false, 0)).ToArray();
        var battle = new OnlineArcadeBattleSnapshot(1, 4, 4, 1, 1, 0, 1, 1000, battlePlayers,
            Array.Empty<OnlineArcadeEnemySnapshot>(), Array.Empty<OnlineArcadeProjectileSnapshot>(), Array.Empty<OnlineArcadeDropSnapshot>());
        var snapshot = new OnlineArcadeRunClientSnapshot(true, 1, 2, 1, OnlineArcadeRunPhase.Arena, 0, 1, "preview", players,
            OnlineArcadeOperationState.Idle, OnlineArcadeResultCode.Success, battle);
        Call(controller, "RefreshOnlinePlayerStatuses", snapshot, 1L);
    }

    private static void Call(ArcadeHudUICtrl controller, string name, params object[] args) =>
        typeof(ArcadeHudUICtrl).GetMethod(name, Private).Invoke(controller, args);

    /// <summary>预览只读取当前工程本地化文件，不依赖运行中的 YooAsset 包。</summary>
    private sealed class PreviewResourceLoader : IResourceLoader
    {
        public T LoadAsset<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/" + path);
        public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> callback) where T : Object
        { callback?.Invoke(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
        public void Release(ResourceLoadHandle handle) { }
    }
}
