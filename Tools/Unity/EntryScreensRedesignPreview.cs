using System;
using System.IO;
using Game;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>入口与加载页的完整静态设计候选；正式场景/角色的隔离构图，不保存产品资源。</summary>
public static class EntryScreensRedesignPreview
{
    private const string Folder = "../Backup/Artifacts/EntryScreensRedesign-v1-20261024";
    private static readonly Color Ink = new Color(.035f, .06f, .075f);
    private static readonly Color Amber = new Color(1, .68f, .16f);

    public static string Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling ||
            Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅本工程编辑态制作临时视觉候选");
        var prior = SceneManager.GetActiveScene(); var previousTarget = RenderTexture.active;
        Directory.CreateDirectory(Folder);
        try
        {
            Render("boot", "ArcadeDocksVisual", "", 0);
            Render("start", "ArcadeDocksVisual", "", 1);
            Render("loading-docks", "ArcadeDocksVisual", "码头", 2);
            Render("loading-farm", "ArcadeFarmVisual", "农场", 2);
            Render("loading-graveyard", "ArcadeGraveyardVisual", "墓园", 2);
            Render("loading-temple", "ArcadeTempleVisual", "神庙庭院", 2);
            return Path.GetFullPath(Folder);
        }
        finally { RenderTexture.active = previousTarget; SceneManager.SetActiveScene(prior); }
    }

    private static void Render(string name, string scenery, string title, int mode)
    {
        var prior = SceneManager.GetActiveScene(); var previousTarget = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        RenderTexture target = null, portraitTarget = null;
        Texture2D pixels = null, sceneryPixels = null, portraitPixels = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.65f, .68f, .72f);
            RenderSettings.fog = false;
            var environment = Object.Instantiate(Load("SceneObjects/" + scenery));
            SetLayer(environment, 28);
            var sun = new GameObject("CandidateSun", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.25f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48, -35, 0);
            sun.cullingMask = (1 << 28) | (1 << 29);
            target = new RenderTexture(1920, 1080, 24);
            pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            var camera = Camera("SceneryCamera", 28, target);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .27f, .32f);
            camera.orthographic = true; camera.orthographicSize = mode < 2 ? 12 : 13;
            var focus = new Vector3(0, 1.5f, 1);
            camera.transform.position = focus + new Vector3(16, 17, -28); camera.transform.LookAt(focus);
            camera.Render();
            sceneryPixels = Read(target, TextureFormat.RGB24);

            if (mode < 2)
            {
                var hero = Object.Instantiate(Load("Characters/GrayboxPlayer"));
                var presenter = hero.GetComponent<CharacterWeaponPresenter>();
                if (!presenter.SetWeaponId("arcade.weapon.basic")) throw new InvalidOperationException(presenter.LastError);
                presenter.SetManualPoseSampling(true); presenter.BodyAnimator.Rebind(); presenter.BodyAnimator.Update(0);
                presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject, .1f);
                presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
                hero.transform.rotation = Quaternion.Euler(0, 150, 0);
                foreach (var canvas in hero.GetComponentsInChildren<Canvas>(true)) canvas.enabled = false;
                SetLayer(hero, 29);
                portraitTarget = new RenderTexture(1200, 1080, 24, RenderTextureFormat.ARGB32);
                var heroCamera = Camera("HeroPortraitCamera", 29, portraitTarget);
                heroCamera.clearFlags = CameraClearFlags.SolidColor; heroCamera.backgroundColor = Color.clear;
                heroCamera.orthographic = true; heroCamera.orthographicSize = 1.25f;
                heroCamera.transform.position = new Vector3(2.5f, 1.65f, -4);
                heroCamera.transform.LookAt(new Vector3(0, .95f, 0)); heroCamera.Render();
                portraitPixels = Read(portraitTarget, TextureFormat.RGBA32);
            }

            var uiCamera = Camera("CandidateUiCamera", 31, target);
            uiCamera.clearFlags = CameraClearFlags.Depth; uiCamera.transform.position = new Vector3(0, 0, -10);
            var ui = new GameObject("TEMP_CANDIDATE_UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var root = ui.GetComponent<RectTransform>();
            var canvasUi = ui.GetComponent<Canvas>(); canvasUi.renderMode = RenderMode.ScreenSpaceCamera;
            canvasUi.worldCamera = uiCamera; canvasUi.planeDistance = 1;
            var scaler = ui.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var font = Load("UI/Home/Start").GetComponent<StartUI>().BtnSetting.GetComponentInChildren<TextMeshProUGUI>(true).font;
            // URP基础相机会清理色缓冲；先将独立实拍作为临时RawImage合成，再渲染完整候选UI。
            Artwork(root, sceneryPixels, 0, 0, 1920, 1080);
            Box(root, "SceneryShade", 0, 0, 1920, 1080, new Color(Ink.r, Ink.g, Ink.b, mode < 2 ? .24f : .06f));
            if (portraitPixels != null) Artwork(root, portraitPixels, mode == 0 ? 800 : 0, 0, 1200, 1080);

            if (mode == 0)
            {
                Box(root, "BrandPanel", 0, 0, 880, 1080, new Color(Ink.r, Ink.g, Ink.b, .89f));
                Label(root, font, "FIREPOWER", 112, 890, 740, 120, 96, Color.white);
                Label(root, font, "LOOP", 112, 762, 740, 140, 128, Amber);
                Box(root, "Accent", 118, 718, 72, 5, Amber);
                Label(root, font, "ARCADE SURVIVAL", 118, 665, 650, 38, 26, new Color(.68f, .78f, .79f));
                Label(root, font, "正在准备游戏", 118, 250, 620, 44, 31, Color.white);
                Label(root, font, "正在加载资源", 118, 199, 620, 35, 23, new Color(.7f, .79f, .8f));
                Progress(root, 118, 151, 640, .56f);
                Label(root, font, "56%", 680, 191, 100, 38, 26, Amber);
                Label(root, font, "FirepowerLoop", 118, 70, 650, 34, 20, new Color(.6f, .69f, .71f));
            }
            else if (mode == 1)
            {
                Box(root, "MenuPanel", 1230, 0, 690, 1080, new Color(Ink.r, Ink.g, Ink.b, .96f));
                Box(root, "Accent", 1230, 0, 5, 1080, Amber);
                Label(root, font, "FIREPOWER", 1312, 910, 550, 90, 66, Color.white);
                Label(root, font, "LOOP", 1312, 816, 550, 105, 86, Amber);
                Label(root, font, "街机生存", 1317, 747, 500, 40, 25, new Color(.65f, .77f, .78f));
                Button(root, font, "开始游戏", 1320, 555, true);
                Button(root, font, "设置", 1320, 435, false);
                Button(root, font, "退出游戏", 1320, 315, false);
                Label(root, font, "FIREPOWERLOOP", 1320, 82, 500, 36, 18, new Color(.55f, .67f, .7f));
            }
            else
            {
                Box(root, "TopBar", 0, 970, 1920, 110, new Color(Ink.r, Ink.g, Ink.b, .8f));
                Label(root, font, "FIREPOWER LOOP", 74, 1004, 900, 46, 30, Color.white);
                Box(root, "LoadingPanel", 0, 0, 1920, 264, new Color(Ink.r, Ink.g, Ink.b, .96f));
                Box(root, "Accent", 76, 221, 48, 5, Amber);
                Label(root, font, title, 76, 154, 1230, 62, 47, Color.white);
                Label(root, font, "正在加载场景", 78, 104, 1400, 37, 24, new Color(.68f, .79f, .8f));
                Progress(root, 78, 62, 1764, .56f);
                Label(root, font, "56%", 1705, 107, 160, 46, 30, Amber);
            }
            Label(root, font, "视觉候选 v1 · 正式模型临时构图 · 示例进度", 24, 25, 1800, 26, 16, new Color(.63f, .71f, .72f));
            SetLayer(ui, 31);
            Canvas.ForceUpdateCanvases();
            foreach (var text in ui.GetComponentsInChildren<TextMeshProUGUI>()) text.ForceMeshUpdate();
            uiCamera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (sceneryPixels != null) Object.DestroyImmediate(sceneryPixels);
            if (portraitPixels != null) Object.DestroyImmediate(portraitPixels);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (portraitTarget != null) { portraitTarget.Release(); Object.DestroyImmediate(portraitTarget); }
            SceneManager.SetActiveScene(prior); EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static Camera Camera(string name, int layer, RenderTexture target)
    { var camera = new GameObject(name, typeof(Camera)).GetComponent<Camera>(); camera.cullingMask = 1 << layer; camera.targetTexture = target; return camera; }
    private static Texture2D Read(RenderTexture target, TextureFormat format)
    {
        RenderTexture.active = target; var image = new Texture2D(target.width, target.height, format, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); return image;
    }
    private static void Artwork(Transform root, Texture texture, float x, float y, float width, float height)
    { var image = Rect(root, "TemporaryArtwork", x, y, width, height).gameObject.AddComponent<RawImage>(); image.texture = texture; image.raycastTarget = false; }
    private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero; rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height); return rect;
    }
    private static void Box(Transform parent, string name, float x, float y, float width, float height, Color color)
    { var rect = Rect(parent, name, x, y, width, height); var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; }
    private static void Label(Transform parent, TMP_FontAsset font, string value, float x, float y, float width, float height, int size, Color color)
    {
        var text = Rect(parent, "PreviewText", x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.text = value; text.fontSize = size; text.color = color; text.raycastTarget = false;
        text.fontStyle = FontStyles.Bold; text.overflowMode = TextOverflowModes.Overflow;
    }
    private static void Progress(Transform root, float x, float y, float width, float value)
    { Box(root, "ProgressTrack", x, y, width, 8, new Color(.16f, .23f, .25f)); Box(root, "ProgressFill", x, y, width * value, 8, Amber); }
    private static void Button(Transform root, TMP_FontAsset font, string text, float x, float y, bool primary)
    {
        Box(root, "PreviewButton", x, y, 500, 91, primary ? Amber : new Color(.1f, .17f, .2f));
        Label(root, font, text, x + 30, y + 25, 435, 45, 32, primary ? Ink : Color.white);
    }
    private static void SetLayer(GameObject root, int layer)
    { foreach (var transform in root.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = layer; }
    private static GameObject Load(string name) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/" + name + ".prefab") ?? throw new InvalidOperationException(name);
}
