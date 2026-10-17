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
using Object = UnityEngine.Object;

/// <summary>复用布局夹具制作隔离编辑器画面，只验证真实Prefab与Controller的展示，不读写玩家存档。</summary>
public static class ArcadeMobileLayoutPreview
{
    public static string Run()
    {
        if (Application.dataPath != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying)
            throw new InvalidOperationException("仅本项目编辑模式预览");
        var fixtureType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Game.Tests.MobileControlLayoutTests"))
            .First(t => t != null);
        var fixture = Activator.CreateInstance(fixtureType);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var architectureField = typeof(Architecture<Game.Game>).GetField("mArchitecture", flags);
        var priorArchitecture = architectureField.GetValue(null);
        var priorLoader = ResourceLoader.Current;
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var priorTexture = RenderTexture.active;
        var folder = Path.GetFullPath("Temp/ArcadeMobileLayoutPreview"); Directory.CreateDirectory(folder);
        try
        {
            fixtureType.GetMethod("SetUp").Invoke(fixture, null);
            var architecture = (Game.Game)fixtureType.GetMethod("CreateEditorArchitecture", flags).Invoke(null, null);
            foreach (var spec in new[] { ("zh-CN", 1920, 1080, false), ("zh-CN", 2340, 1080, true), ("en-US", 1440, 1080, false) })
            {
                var args = new object[] { null, null };
                var root = (GameObject)fixtureType.GetMethod("CreateEditor", flags).Invoke(null, args);
                var ctrl = (MobileControlLayoutUICtrl)args[0]; var ui = (MobileControlLayoutUI)args[1];
                var camera = new GameObject("LayoutPreviewCamera", typeof(Camera)).GetComponent<Camera>();
                var target = new RenderTexture(spec.Item2, spec.Item3, 24);
                var pixels = new Texture2D(spec.Item2, spec.Item3, TextureFormat.RGB24, false);
                try
                {
                    ((RectTransform)root.transform).sizeDelta = new Vector2(spec.Item2, spec.Item3);
                    foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                    camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.02f, .035f, .05f); camera.orthographic = true;
                    camera.orthographicSize = spec.Item3 * .5f; camera.aspect = (float)spec.Item2 / spec.Item3;
                    camera.transform.position = new Vector3(0, 0, -10); camera.targetTexture = target;
                    architecture.GetModel<LocalizationModel>().CurrentLanguageCode.Value = spec.Item1;
                    foreach (var localized in ui.GetComponentsInChildren<LocalizedText>(true)) localized.Refresh();
                    Canvas.ForceUpdateCanvases(); ctrl.Show();
                    if (spec.Item4) ui.ModeButton.onClick.Invoke();
                    Canvas.ForceUpdateCanvases();
                    foreach (var text in ui.GetComponentsInChildren<TextMeshProUGUI>()) text.ForceMeshUpdate();
                    camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, spec.Item2, spec.Item3), 0, 0); pixels.Apply();
                    var file = Path.Combine(folder, spec.Item1 + "-" + spec.Item2 + (spec.Item4 ? "-first-person" : "-top-down"));
                    File.WriteAllBytes(file + ".png", pixels.EncodeToPNG());
                    File.WriteAllLines(file + ".txt", ui.GetComponentsInChildren<TextMeshProUGUI>()
                        .Select(t => t.name + ": overflow=" + t.isTextOverflowing + "; " + t.text));
                    ctrl.Hide();
                }
                finally
                {
                    RenderTexture.active = priorTexture; camera.targetTexture = null; target.Release();
                    Object.DestroyImmediate(root); Object.DestroyImmediate(camera.gameObject);
                    Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
                }
            }
            return folder;
        }
        finally
        {
            fixtureType.GetMethod("TearDown").Invoke(fixture, null);
            ResourceLoader.Use(priorLoader); architectureField.SetValue(null, priorArchitecture);
            RenderTexture.active = priorTexture; SceneManager.SetActiveScene(previousScene); EditorSceneManager.CloseScene(scene, true);
            Time.timeScale = 1;
        }
    }
}
