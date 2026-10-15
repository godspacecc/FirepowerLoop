using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Game;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>借用真实界面测试夹具渲染临时预览；关闭临时场景，不保存或修改正式场景与 Prefab。</summary>
public static class MapSelectionChapterPreview
{
    public static string Render()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("预览仅用于编辑模式");
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Game.Tests.MapSelectionUiTests")).First(t => t != null);
        var fixture = Activator.CreateInstance(type);
        try
        {
            type.GetMethod("SetUp").Invoke(fixture, null);
            var ui = (MapSelectionUI)type.GetMethod("OpenDialog", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            var canvas = ui.GetComponentInParent<Canvas>();
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var camera = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>()).Single();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.18f, .24f, .25f);
            camera.transform.position = new Vector3(0, 0, -10);
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            string folder = "Assets/Tests/Results~/MapSelectionChapter"; Directory.CreateDirectory(folder);
            Capture(camera, ui, folder + "/dialog-zh.png");
            Game.Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });
            Capture(camera, ui, folder + "/dialog-en.png");
            Game.Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "zh-CN" });
            var other = ui.MapList.GetDraggableItems().Select(t => t.GetComponent<MapSelectionOptionBoxUI>())
                .Single(row => row.TextName.text == Loc.T("map.survival_sample.name"));
            other.BtnSelect.onClick.Invoke();
            Capture(camera, ui, folder + "/other-expedition.png");
            var next = ui.MapList.GetDraggableItems().Select(t => t.GetComponent<MapSelectionOptionBoxUI>())
                .Single(row => row.TextName.text == Loc.T("ui.map_selection.next_name"));
            next.BtnSelect.onClick.Invoke();
            Capture(camera, ui, folder + "/next-chapter.png");
            return Path.GetFullPath(folder);
        }
        finally
        {
            type.GetMethod("TearDown").Invoke(fixture, null);
            SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void Capture(Camera camera, MapSelectionUI ui, string path)
    {
        var target = new RenderTexture(1920, 1080, 24);
        var texture = new Texture2D(1920, 1080, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)ui.MapList.transform);
            Canvas.ForceUpdateCanvases();
            camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG());
            // 截图同时记录实际 TMP 的溢出，不能仅凭字符数量推断显示效果。
            File.WriteAllLines(path + ".txt", ui.GetComponentsInChildren<TextMeshProUGUI>()
                .Select(t => t.name + ": overflow=" + t.isTextOverflowing + " text=" + t.text));
        }
        finally
        { camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(target); Object.DestroyImmediate(texture); }
    }
}
