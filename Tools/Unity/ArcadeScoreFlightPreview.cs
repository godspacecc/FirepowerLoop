using System;
using System.IO;
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

/// <summary>用正式 HUD 渲染财宝飞行的逐帧证据，隔离临时场景及架构，不写玩家存档。</summary>
public static class ArcadeScoreFlightPreview
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("请在编辑模式渲染积分飞入预览");
        var singleton = typeof(Architecture<Game.Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
        var previousArchitecture = singleton.GetValue(null); var previousLoader = ResourceLoader.Current;
        var languages = typeof(LocalizationLanguages).GetField("cachedConfig", BindingFlags.Static | BindingFlags.NonPublic);
        var previousLanguages = languages.GetValue(null);
        var previousScene = SceneManager.GetActiveScene(); var previousTexture = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var architecture = new Game.Game(); singleton.SetValue(null, architecture);
        typeof(Architecture<Game.Game>).GetField("mInited", Hidden).SetValue(architecture, true);
        const string folder = "../Docs/Plans/Artifacts/ScoreFlight-20261019";
        Directory.CreateDirectory(folder);
        try
        {
            ResourceLoader.Use(new PreviewLoader()); LocalizationLanguages.ResetCache();
            architecture.RegisterModel(new LocalizationModel()); architecture.RegisterModel(new ArcadeRunModel());
            architecture.RegisterSystem(new MobileInputSystem());
            var localization = new LocalizationUtility(false);
            localization.LoadFromJson("zh-CN", File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
            typeof(LocalizationUtility).GetField("loadedConfiguredTables", Hidden).SetValue(localization, true);
            architecture.RegisterUtility(localization);
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() => new Tables(name =>
                new Luban.ByteBuf(File.ReadAllBytes("Assets/Game/Config/" + name + ".bytes")))));
            foreach (bool mobile in new[] { false, true })
            {
                architecture.GetSystem<MobileInputSystem>().TrySetEditorSimulation(mobile);
                var camera = new GameObject("ScoreFlightPreviewCamera", typeof(Camera)).GetComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.045f, .065f, .08f);
                var target = new RenderTexture(1920, 1080, 24); camera.targetTexture = target;
                var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                var canvasRoot = new GameObject("ScoreFlightPreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                try
                {
                    var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera; canvas.planeDistance = 1;
                    var scaler = canvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
                    var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"), canvasRoot.transform, false);
                    foreach (var fitter in instance.GetComponentsInChildren<SafeAreaFitter>(true)) fitter.enabled = false;
                    var ui = instance.GetComponent<ArcadeHudUI>(); var controller = new ArcadeHudUICtrl();
                    typeof(BaseUICtrl).GetField("BaseUI", Hidden).SetValue(controller, ui);
                    typeof(BaseUICtrl).GetField("_isActive", Hidden).SetValue(controller, true);
                    Call(controller, "OnInit");
                    Call(controller, "Render", new ArcadeRunSaveData { Score = 10450, ScoreMultiplier = 3, RemainingLives = 3 });
                    Call(controller, "ApplyInputLayout", mobile, false);
                    Canvas.ForceUpdateCanvases();
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)instance.transform);
                    Canvas.ForceUpdateCanvases();
                    var source = (RectTransform)ui.ScoreFlightList.transform;
                    var local = new Vector3(source.rect.width * .15f, -source.rect.height * .08f);
                    var screen = RectTransformUtility.WorldToScreenPoint(camera, source.TransformPoint(local));
                    controller.TryPlayScorePickup(250, screen);
                    for (int frame = 0; frame < 12; frame++)
                    {
                        if (frame > 0) Call(controller, "AdvanceScorePickups", .08f);
                        Canvas.ForceUpdateCanvases();
                        foreach (var text in instance.GetComponentsInChildren<TextMeshProUGUI>()) text.ForceMeshUpdate();
                        camera.Render(); RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); pixels.Apply();
                        File.WriteAllBytes(folder + "/" + (mobile ? "phone" : "pc") + "-" + frame.ToString("D2") + ".png", pixels.EncodeToPNG());
                    }
                    controller.ClearScorePickupFeedback();
                }
                finally
                {
                    Object.DestroyImmediate(canvasRoot); camera.targetTexture = null; Object.DestroyImmediate(camera.gameObject);
                    RenderTexture.active = previousTexture; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
                }
            }
            return Path.GetFullPath(folder);
        }
        finally
        {
            RenderTexture.active = previousTexture; SceneManager.SetActiveScene(previousScene); EditorSceneManager.CloseScene(scene, true);
            architecture.Deinit(); singleton.SetValue(null, previousArchitecture); ResourceLoader.Use(previousLoader); languages.SetValue(null, previousLanguages);
        }
    }

    private static object Call(ArcadeHudUICtrl controller, string method, params object[] args) =>
        typeof(ArcadeHudUICtrl).GetMethod(method, Hidden).Invoke(controller, args);

    private sealed class PreviewLoader : IResourceLoader
    {
        public T LoadAsset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/" + path);
        public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> loaded) where T : Object { loaded?.Invoke(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
        public void Release(ResourceLoadHandle handle) { }
    }
}
