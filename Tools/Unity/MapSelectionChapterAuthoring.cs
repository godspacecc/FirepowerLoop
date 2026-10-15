using System;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>制作文明章节弹窗与选中卡片，静态布局只保存在 Prefab；不改变地图、下载及解锁规则。</summary>
public static class MapSelectionChapterAuthoring
{
    const string HostPath = "Assets/Game/Prefabs/UI/Expedition/MapSelection.prefab";
    const string RowPath = "Assets/Game/Prefabs/UI/Expedition/Box/MapSelectionOptionBox.prefab";
    const string ArtPath = "Assets/Game/Art/UI/MapSelection/ValleyChapter.png";
    static TMP_FontAsset font;
    static Sprite landscape;
    static readonly Color Surface = new Color(.055f, .10f, .12f, .98f);
    static readonly Color Amber = new Color(.95f, .68f, .28f);

    public static string Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("只能在编辑模式制作界面");
        font = AssetDatabase.LoadAssetAtPath<GameObject>(HostPath).GetComponentInChildren<TextMeshProUGUI>(true).font;
        AssetDatabase.ImportAsset(ArtPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(ArtPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 1024;
        importer.SaveAndReimport();
        landscape = AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath);
        Row(); Host();
        // 通过项目生成器生成字段，重编译后由生成器绑定，不手写 View。
        BoxUICodeSpawner.AutoUpdateBoxUI(AssetDatabase.LoadAssetAtPath<GameObject>(RowPath));
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(HostPath));
        LocalizationTextGenerator.GenerateTexts();
        return "Chapter prefabs saved; UI/Box generators invoked.";
    }

    static void Clear(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; --i) Object.DestroyImmediate(root.GetChild(i).gameObject);
    }

    static void Row()
    {
        var root = PrefabUtility.LoadPrefabContents(RowPath);
        try
        {
            root.SetActive(false); Clear(root.transform);
            var rc = root.GetComponent<ReferenceCollector>(); rc.Clear();
            Rect((RectTransform)root.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(300, 158));
            var image = root.GetComponent<Image>(); image.color = new Color(.08f, .14f, .16f);
            var button = root.GetComponent<Button>() ?? root.AddComponent<Button>();
            button.targetGraphic = image; button.transition = Selectable.Transition.ColorTint;
            var layout = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = 158; layout.flexibleHeight = 0;
            var preview = Artwork(root.transform, "CardArtwork", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image(root.transform, "Shade", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(.025f, .065f, .08f, .68f));
            rc.Add("TextDetail", Label(root.transform, "TextDetail", "第一章 · 史前初居", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -28), new Vector2(-36, 30), 20));
            rc.Add("TextName", Label(root.transform, "TextName", "旧营地河谷", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -73), new Vector2(-36, 52), 28));
            rc.Add("TextState", Label(root.transform, "TextState", "已解锁", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 30), new Vector2(-36, 38), 18));
            var frame = Node(root.transform, "SelectionFrame");
            Rect((RectTransform)frame.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image(frame.transform, "Top", new Vector2(0, 1), Vector2.one, Vector2.zero, new Vector2(0, 3), Amber);
            Image(frame.transform, "Bottom", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 3), Amber);
            Image(frame.transform, "Left", Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(3, 0), Amber);
            Image(frame.transform, "Right", new Vector2(1, 0), Vector2.one, Vector2.zero, new Vector2(3, 0), Amber);
            frame.SetActive(false);
            rc.Add("SelectionFrame", frame); rc.Add("Preview", preview); rc.Add("BtnSelect", button);
            root.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(root, RowPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void Host()
    {
        var root = PrefabUtility.LoadPrefabContents(HostPath);
        try
        {
            root.SetActive(false); Clear(root.transform);
            var rc = root.GetComponent<ReferenceCollector>(); rc.Clear();
            // 透明根负责定位，内容面板固定居中；框架配对的背景负责压暗和输入阻断。
            Rect((RectTransform)root.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1000, 650));
            root.GetComponent<Image>().color = Color.clear; root.GetComponent<Image>().raycastTarget = false;
            var panel = Image(root.transform, "Panel", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1000, 650), Surface);
            panel.raycastTarget = true;
            panel.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); panel.type = UnityEngine.UI.Image.Type.Sliced;
            var outline = panel.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(.36f, .48f, .47f, .7f); outline.effectDistance = new Vector2(1, -1);
            Fixed(panel.transform, "Title", "文明远征", "ui.map_selection.title", new Vector2(-312, 275), new Vector2(300, 54), 37);
            Fixed(panel.transform, "Subtitle", "探索故事 · 获取科技 · 推进文明", "ui.map_selection.subtitle", new Vector2(80, 275), new Vector2(460, 42), 19);
            var close = Button(panel.transform, "BtnClose", new Vector2(454, 279), new Vector2(54, 54), new Color(.1f, .17f, .19f));
            Label(close.transform, "CloseGlyph", "×", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 38).alignment = TextAlignmentOptions.Center;
            rc.Add("BtnClose", close);
            var scrollImage = Image(panel.transform, "MapScroll", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-322, -30), new Vector2(312, 522), new Color(.04f, .09f, .11f, .5f));
            scrollImage.raycastTarget = true;
            var scroll = scrollImage.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Node(scroll.transform, "Viewport"); Rect((RectTransform)viewport.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewport.AddComponent<RectMask2D>();
            var content = Node(viewport.transform, "MapList"); var contentRect = (RectTransform)content.transform;
            Rect(contentRect, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero); contentRect.pivot = new Vector2(.5f, 1);
            var group = content.AddComponent<VerticalLayoutGroup>(); group.spacing = 14; group.padding = new RectOffset(4, 4, 4, 4);
            group.childControlWidth = group.childControlHeight = true; group.childForceExpandWidth = true; group.childForceExpandHeight = false;
            var fit = content.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var list = content.AddComponent<CustomListView>(); list.itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RowPath);
            scroll.viewport = (RectTransform)viewport.transform; scroll.content = contentRect;
            rc.Add("MapList", list); rc.Add("MapScroll", scroll);
            var detail = Node(panel.transform, "Detail"); Rect((RectTransform)detail.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(164, -16), new Vector2(604, 540));
            var preview = Artwork(detail.transform, "ChapterArtwork", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -104), new Vector2(604, 208));
            rc.Add("Preview", preview);
            rc.Add("SelectedName", Label(detail.transform, "SelectedName", "旧营地河谷", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -240), new Vector2(-20, 56), 34));
            rc.Add("EraText", Label(detail.transform, "EraText", "史前 · 早期定居", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -285), new Vector2(-20, 32), 19));
            rc.Add("StoryText", Label(detail.transform, "StoryText", "沿河探索旧营地，寻找支持远行的生存技艺。", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -345), new Vector2(-20, 76), 20));
            Image(detail.transform, "Divider", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -391), new Vector2(-20, 1), new Color(.32f, .43f, .43f, .7f));
            var tech = Node(detail.transform, "TechRoot"); Rect((RectTransform)tech.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -432), new Vector2(-20, 76));
            Fixed(tech.transform, "TechTitle", "科技目标", "ui.map_selection.technology_title", new Vector2(0, 25), new Vector2(580, 38), 23);
            rc.Add("TechnologyText", Label(tech.transform, "TechnologyText", "干粮工艺 · 尚未掌握", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -12), new Vector2(580, 40), 21));
            rc.Add("TechRoot", tech);
            rc.Add("ResourceText", Label(detail.transform, "ResourceText", "基础资源已就绪", new Vector2(0, 0), new Vector2(1, 0), new Vector2(-145, 38), new Vector2(-318, 58), 18));
            var action = Button(detail.transform, "BtnAction", new Vector2(170, -232), new Vector2(260, 58), Amber);
            var actionText = Label(action.transform, "ActionText", "出发探索", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-12, -4), 27);
            actionText.color = new Color(.06f, .11f, .12f); actionText.alignment = TextAlignmentOptions.Center;
            rc.Add("BtnAction", action); rc.Add("ActionText", actionText);
            Fixed(detail.transform, "ReturnHint", "成功返程后保留科技成果", "ui.map_selection.return_hint", new Vector2(0, -277), new Vector2(584, 26), 16);
            root.SetActive(true); PrefabUtility.SaveAsPrefabAsset(root, HostPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static GameObject Node(Transform parent, string name)
    { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go; }
    static void Rect(RectTransform rect, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
    { rect.anchorMin = min; rect.anchorMax = max; rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size; }
    static Image Image(Transform parent, string name, Vector2 min, Vector2 max, Vector2 position, Vector2 size, Color color)
    { var go = Node(parent, name); Rect((RectTransform)go.transform, min, max, position, size); var image = go.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image; }
    static Image Artwork(Transform parent, string name, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
    {
        var frame = Node(parent, name); Rect((RectTransform)frame.transform, min, max, position, size);
        frame.AddComponent<RectMask2D>();
        var image = Image(frame.transform, "Preview", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Color.white);
        image.sprite = landscape;
        var fit = image.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fit.aspectRatio = landscape.rect.width / landscape.rect.height;
        return image;
    }
    static TextMeshProUGUI Label(Transform parent, string name, string text, Vector2 min, Vector2 max, Vector2 position, Vector2 size, float fontsize)
    { var go = Node(parent, name); go.SetActive(false); Rect((RectTransform)go.transform, min, max, position, size);
      var tmp = go.AddComponent<TextMeshProUGUI>(); tmp.font = font; tmp.fontSize = fontsize; tmp.text = text;
      tmp.color = new Color(.91f, .94f, .9f); tmp.raycastTarget = false; tmp.alignment = TextAlignmentOptions.MidlineLeft;
      tmp.enableAutoSizing = true; tmp.fontSizeMin = fontsize * .75f; tmp.fontSizeMax = fontsize;
      tmp.enableWordWrapping = true; tmp.overflowMode = TextOverflowModes.Ellipsis; go.SetActive(true); return tmp; }
    static void Fixed(Transform parent, string name, string text, string key, Vector2 position, Vector2 size, float fontsize)
    { var tmp = Label(parent, name, text, new Vector2(.5f, .5f), new Vector2(.5f, .5f), position, size, fontsize);
      tmp.gameObject.SetActive(false); var loc = tmp.gameObject.AddComponent<LocalizedText>(); loc.Key = key; tmp.gameObject.SetActive(true); }
    static Button Button(Transform parent, string name, Vector2 position, Vector2 size, Color color)
    { var image = Image(parent, name, new Vector2(.5f, .5f), new Vector2(.5f, .5f), position, size, color);
      image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); image.type = UnityEngine.UI.Image.Type.Sliced;
      image.raycastTarget = true; var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; return button; }
}
