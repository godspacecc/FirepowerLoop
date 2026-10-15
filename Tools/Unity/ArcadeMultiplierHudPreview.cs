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

/// <summary>正式 HUD 的倍率状态与多尺寸渲染；隔离架构及场景，只投影示例快照，不写玩家存档。</summary>
public static class ArcadeMultiplierHudPreview
{
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("仅支持编辑器预览");
        var architectureField = typeof(Architecture<Game.Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
        var priorArchitecture = architectureField.GetValue(null);
        var architecture = new Game.Game();
        architectureField.SetValue(null, architecture);
        typeof(Architecture<Game.Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(architecture, true);
        architecture.RegisterModel(new LocalizationModel());
        architecture.RegisterModel(new ArcadeRunModel());
        var localization = new LocalizationUtility(false);
        foreach (var language in new[] { "zh-CN", "en-US" })
            localization.LoadFromJson(language, File.ReadAllText("Assets/Game/Config/Localization/" + language + ".json"));
        typeof(LocalizationUtility).GetField("loadedConfiguredTables", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(localization, true);
        architecture.RegisterUtility(localization);
        architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() => new Tables(name =>
            new Luban.ByteBuf(File.ReadAllBytes("Assets/Game/Config/" + name + ".bytes")))));
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var oldTexture = RenderTexture.active;
        const string folder = "Assets/Tests/Results~/ArcadeMultiplierHud";
        Directory.CreateDirectory(folder);
        try
        {
            var camera = new GameObject("PreviewCamera", typeof(Camera)).GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .05f, .07f);
            camera.transform.position = new Vector3(0, 0, -10);
            var canvasRoot = new GameObject("PreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
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
            var controller = new ArcadeHudUICtrl();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(BaseUICtrl).GetField("BaseUI", flags).SetValue(controller, hud.GetComponent<ArcadeHudUI>());
            typeof(ArcadeHudUICtrl).GetMethod("OnInit", flags).Invoke(controller, null);
            var render = typeof(ArcadeHudUICtrl).GetMethod("Render", flags);
            foreach (var spec in new[] { ("zh-CN", 1920, 1080), ("en-US", 1920, 1080), ("zh-CN", 2340, 1080) })
            {
                architecture.GetModel<LocalizationModel>().CurrentLanguageCode.Value = spec.Item1;
                foreach (var localized in hud.GetComponentsInChildren<LocalizedText>(true)) localized.Refresh();
                foreach (var state in new[] { (3, 900, "progress"), (4, 150, "carry"), (9, 0, "maximum") })
                {
                    render.Invoke(controller, new object[] { new ArcadeRunSaveData {
                        Score = 2250, ScoreMultiplier = state.Item1, MultiplierProgress = state.Item2,
                        RemainingLives = 3, NextArenaNumber = 1, RunGold = 150 } });
                    if (spec.Item2 == 2340 && state.Item3 == "maximum")
                    {
                        var players = Enumerable.Range(1, 4).Select(i => new OnlineArcadePlayerSnapshot(i,
                            "Player " + i, 3, 150, 2, 2, "arcade.weapon.basic", false, null,
                            score: 2250, scoreMultiplier: i == 1 ? 9 : 3, multiplierProgress: i == 1 ? 0 : i * 150)).ToArray();
                        var battlePlayers = Enumerable.Range(1, 4).Select(i => new OnlineArcadeBattlePlayerSnapshot(i,
                            i, i, 0, 1, 100, 100, true, false, 0)).ToArray();
                        var battle = new OnlineArcadeBattleSnapshot(1, 4, 4, 1, 1, 0, 1, 1000, battlePlayers,
                            Array.Empty<OnlineArcadeEnemySnapshot>(), Array.Empty<OnlineArcadeProjectileSnapshot>(),
                            Array.Empty<OnlineArcadeDropSnapshot>());
                        var online = new OnlineArcadeRunClientSnapshot(true, 1, 2, 1, OnlineArcadeRunPhase.Arena,
                            0, 1, "sample", players, OnlineArcadeOperationState.Idle, OnlineArcadeResultCode.Success, battle);
                        typeof(ArcadeHudUICtrl).GetMethod("RefreshOnlinePlayerStatuses", flags).Invoke(controller,
                            new object[] { online, 1L });
                    }
                    var target = new RenderTexture(spec.Item2, spec.Item3, 24);
                    var image = new Texture2D(spec.Item2, spec.Item3, TextureFormat.RGB24, false);
                    try
                    {
                        camera.targetTexture = target;
                        Canvas.ForceUpdateCanvases();
                        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)hud.transform);
                        Canvas.ForceUpdateCanvases();
                        foreach (var text in hud.GetComponentsInChildren<TextMeshProUGUI>()) text.ForceMeshUpdate();
                        camera.Render();
                        RenderTexture.active = target;
                        image.ReadPixels(new Rect(0, 0, spec.Item2, spec.Item3), 0, 0);
                        image.Apply();
                        var path = folder + "/" + spec.Item1 + "-" + spec.Item2 + "-" + state.Item3;
                        File.WriteAllBytes(path + ".png", image.EncodeToPNG());
                        File.WriteAllLines(path + ".txt", hud.GetComponentsInChildren<TextMeshProUGUI>()
                            .Where(t => t.name.StartsWith("TextMultiplier"))
                            .Select(t => t.name + " overflow=" + t.isTextOverflowing + " text=" + t.text));
                    }
                    finally
                    {
                        camera.targetTexture = null;
                        RenderTexture.active = oldTexture;
                        target.Release();
                        Object.DestroyImmediate(target);
                        Object.DestroyImmediate(image);
                    }
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
        }
    }
}
