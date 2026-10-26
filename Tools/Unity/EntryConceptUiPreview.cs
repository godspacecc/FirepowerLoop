using System;
using System.IO;
using Game;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>将概念插画与原生文字/按钮临时组合成完整屏幕，不导入或替换正式资源。</summary>
public static class EntryConceptUiPreview
{
    private const string Folder = "../Backup/Artifacts/EntryScreensConcept-v2-20261024";
    private static readonly Color Amber = new Color(1, .69f, .18f);
    private static readonly Color Ink = new Color(.025f, .055f, .07f);
    public static string Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling ||
            Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅本项目编辑态预览");
        Render(false); Render(true); return Path.GetFullPath(Folder);
    }

    private static void Render(bool menu)
    {
        var previous = SceneManager.GetActiveScene(); var oldTarget = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        Texture2D art = null, pixels = null; RenderTexture target = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            art = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!art.LoadImage(File.ReadAllBytes(Path.Combine(Folder, menu ? "start-concept-art.png" : "boot-concept-art.png"))))
                throw new InvalidOperationException("概念图读取失败");
            var camera = new GameObject("ConceptUiCamera", typeof(Camera)).GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(0, 0, -10); camera.cullingMask = 1 << 31;
            var root = new GameObject("TEMP_CONCEPT_UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var parent = root.transform;
            var font = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab")
                .GetComponent<StartUI>().BtnSetting.GetComponentInChildren<TextMeshProUGUI>(true).font;
            var image = Rect(parent, "ConceptArtwork", 0, 0, 1920, 1080).gameObject.AddComponent<RawImage>();
            image.texture = art; image.raycastTarget = false;
            float x = menu ? 1318 : 112;
            Label(parent, font, "FIREPOWER", x, 900, menu ? 540 : 760, 110, menu ? 66 : 95, Color.white);
            Label(parent, font, "LOOP", x, 785, menu ? 540 : 760, 130, menu ? 91 : 124, Amber);
            Box(parent, "Accent", x + 4, 747, 56, 4, Amber);
            Label(parent, font, "街机生存", x + 4, 689, 500, 45, 25, new Color(.7f, .81f, .83f));
            if (menu)
            {
                Button(parent, font, "开始游戏", x + 4, 491, true);
                Button(parent, font, "设置", x + 4, 377, false);
                Button(parent, font, "退出游戏", x + 4, 263, false);
                Label(parent, font, "FIREPOWERLOOP", x + 4, 90, 480, 32, 18, new Color(.6f, .72f, .75f));
            }
            else
            {
                Label(parent, font, "正在准备游戏", x + 4, 242, 640, 45, 30, Color.white);
                Label(parent, font, "正在加载资源", x + 4, 192, 580, 35, 22, new Color(.7f, .81f, .83f));
                Label(parent, font, "56%", x + 554, 192, 110, 38, 25, Amber);
                Box(parent, "ProgressTrack", x + 4, 154, 640, 7, new Color(.2f, .3f, .33f));
                Box(parent, "ProgressFill", x + 4, 154, 640 * .56f, 7, Amber);
                Label(parent, font, "FirepowerLoop", x + 4, 83, 640, 30, 19, new Color(.62f, .74f, .77f));
            }
            Label(parent, font, "概念预览 v2 · 非实机画面 · 示例界面状态", 24, 24, 1500, 28, 16, new Color(.7f, .77f, .8f));
            foreach (var node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 31;
            target = new RenderTexture(1920, 1080, 24); camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>()) text.ForceMeshUpdate();
            camera.Render(); RenderTexture.active = target;
            pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Folder, menu ? "start-full-preview.png" : "boot-full-preview.png"), pixels.EncodeToPNG());
            camera.targetTexture = null;
        }
        finally
        {
            RenderTexture.active = oldTarget;
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (art != null) Object.DestroyImmediate(art);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true);
        }
    }
    private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero; rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height); return rect;
    }
    private static void Box(Transform parent, string name, float x, float y, float width, float height, Color color)
    { var image = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; }
    private static void Label(Transform parent, TMP_FontAsset font, string text, float x, float y, float width, float height, int size, Color color)
    {
        var label = Rect(parent, "PreviewText", x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.text = text; label.fontSize = size; label.color = color; label.fontStyle = FontStyles.Bold;
        label.overflowMode = TextOverflowModes.Overflow; label.raycastTarget = false;
    }
    private static void Button(Transform parent, TMP_FontAsset font, string text, float x, float y, bool primary)
    {
        Box(parent, "PreviewButton", x, y, 480, 88, primary ? Amber : new Color(.035f, .09f, .11f, .75f));
        Label(parent, font, text, x + 28, y + 23, 425, 45, 31, primary ? Ink : Color.white);
    }
}
