using System;
using System.Linq;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>将用户确认的入口原图底板接入真实 UI。布局仅写入 Prefab，不改变街机规则和存档。</summary>
public static class ArcadeEntryAuthoring
{
    const string PrefabPath = "Assets/Game/Prefabs/UI/Arcade/ArcadeResume.prefab";
    const string ArtPath = "Assets/Game/Art/UI/ArcadeEntry/EntryPlate.png";
    const float W = 1672f, H = 941f;
    static TMP_FontAsset font;
    static Material headingMaterial;
    static Sprite[] sprites;
    static Color Ink = new Color(.065f, .045f, .02f);
    static Color Cream = new Color(.98f, .93f, .82f);

    /// <summary>仅在编辑模式重建该入口并调用项目生成器；可重复执行，不修改其他界面。</summary>
    public static string Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("请退出运行模式再制作入口");
        font = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponentInChildren<TextMeshProUGUI>(true).font;
        const string headingPath = "Assets/Game/Art/UI/ArcadeEntry/EntryHeading.mat";
        headingMaterial = AssetDatabase.LoadAssetAtPath<Material>(headingPath);
        if (headingMaterial == null) { headingMaterial = new Material(font.material); AssetDatabase.CreateAsset(headingMaterial, headingPath); }
        headingMaterial.SetFloat("_FaceDilate", .18f);
        EditorUtility.SetDirty(headingMaterial);
        AssetDatabase.ImportAsset(ArtPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(ArtPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        // 使用同一图集切片保留原按钮纹理；点击态只调色，不把按钮文字烘焙进贴图。
        importer.spritesheet = new[] {
            Slice("Plate", 0, 0, W, H), Slice("SoloButton", 391, 689, 431, 80),
            Slice("OnlineButton", 923, 689, 432, 80), Slice("CloseButton", 1574, 38, 66, 65)
        };
        importer.SaveAndReimport();
        sprites = AssetDatabase.LoadAllAssetsAtPath(ArtPath).OfType<Sprite>().ToArray();
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            root.SetActive(false);
            for (int i = root.transform.childCount - 1; i >= 0; --i) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            var collector = root.GetComponent<ReferenceCollector>(); collector.Clear();
            Stretch((RectTransform)root.transform);
            var screen = root.GetComponent<Image>() ?? root.AddComponent<Image>();
            screen.color = new Color(.018f,.026f,.042f); screen.raycastTarget = true;
            if (root.GetComponent<SafeAreaFitter>() == null) root.AddComponent<SafeAreaFitter>();
            var safe = Node(root.transform, "SafeContent"); Stretch((RectTransform)safe.transform);
            var board = Node(safe.transform, "ArtworkBoard"); Stretch((RectTransform)board.transform);
            var fit = board.AddComponent<AspectRatioFitter>(); fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = W / H;
            var plate = board.AddComponent<Image>(); plate.sprite = sprites.Single(s => s.name == "Plate"); plate.raycastTarget = true;

            Fixed(board.transform, "Brand", "ANOTHER DAY", "ui.arcade.entry.brand", 90, 39, 290, 28, 19, false, TextAlignmentOptions.Left);
            Fixed(board.transform, "Title", "街机挑战", "ui.arcade.entry.title", 85, 60, 480, 117, 106, true, TextAlignmentOptions.Left);
            Fixed(board.transform, "Subtitle", "选择游玩方式", "ui.arcade.entry.choose", 92, 180, 360, 33, 25, true, TextAlignmentOptions.Left);
            Fixed(board.transform, "SoloTitle", "单人挑战", "ui.arcade.entry.solo", 378, 567, 460, 71, 59, true);
            Fixed(board.transform, "SoloCaption", "独自迎战，突破重围", "ui.arcade.entry.solo_hint", 383, 644, 450, 32, 25);
            Fixed(board.transform, "OnlineTitle", "双人联机", "ui.arcade.online.entry", 910, 567, 460, 71, 59, true);
            Fixed(board.transform, "OnlineCaption", "并肩作战，协力闯关", "ui.arcade.entry.online_hint", 915, 644, 450, 32, 25);
            Fixed(board.transform, "IsolationHint", "本模式独立开局，不携带基地装备", "ui.arcade.entry.isolation", 435, 841, 805, 40, 21);

            var solo = Button(board.transform, "BtnContinue", "SoloButton", 391, 689, 431, 80);
            var soloLabel = Label(solo.transform, "Label", "开始游戏", 0, 0, W, H, 34, true);
            soloLabel.color = Ink;
            collector.Add("BtnContinue", solo);
            var online = Button(board.transform, "BtnOnline", "OnlineButton", 923, 689, 432, 80);
            var onlineLabel = Label(online.transform, "Label", "进入大厅", 0, 0, W, H, 34, true); onlineLabel.color = Ink;
            Localize(onlineLabel, "ui.arcade.entry.lobby"); collector.Add("BtnOnline", online);
            var close = Button(board.transform, "BtnClose", "CloseButton", 1574, 38, 66, 65);
            Label(close.transform, "CloseGlyph", "×", 0, 0, W, H, 57, true);
            collector.Add("BtnClose", close);

            // 恢复资料覆盖单人插画的局部区域，保留两张卡片与主要入口位置。
            var resume = Node(board.transform, "ResumeDetails"); Box((RectTransform)resume.transform, 376, 223, 462, 317);
            var shade = resume.AddComponent<Image>(); shade.color = new Color(.025f,.031f,.036f,.95f); shade.raycastTarget = false;
            Fixed(resume.transform, "SavedTitle", "未结束的街机局", "ui.arcade.resume.title", 65, 45, 1542, 112, 26, true);
            var summary = Label(resume.transform, "TextSummary", "已完成竞技场 4\n剩余生命 2\n金币 280", 85, 195, 1502, 290, 24);
            summary.alignment = TextAlignmentOptions.Left; collector.Add("TextSummary", summary);
            var rewards = Label(resume.transform, "TextRewards", "", 85, 485, 1502, 125, 20);
            rewards.alignment = TextAlignmentOptions.Left; collector.Add("TextRewards", rewards);
            var settle = PlainButton(resume.transform, "BtnSettle", 85, 680, 720, 180, new Color(.18f,.23f,.24f));
            var settleLabel = Label(settle.transform, "Label", "结算并返回", 0, 0, W, H, 22, true); Localize(settleLabel, "ui.arcade.settle");
            var abandon = PlainButton(resume.transform, "BtnAbandon", 867, 680, 720, 180, new Color(.27f,.10f,.085f));
            var abandonLabel = Label(abandon.transform, "Label", "放弃本局", 0, 0, W, H, 22, true); Localize(abandonLabel, "ui.arcade.abandon");
            collector.Add("BtnSettle", settle); collector.Add("BtnAbandon", abandon); collector.Add("ResumeDetails", resume);
            resume.SetActive(false);
            var message = Label(board.transform, "TextMessage", "", 352, 802, 1040, 35, 22);
            message.color = new Color(1f,.78f,.47f); collector.Add("TextMessage", message);
            collector.Sort();
            root.SetActive(true); PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        LocalizationTextGenerator.GenerateTexts();
        return "Arcade entry prefab saved; generated UI binding and localization requested.";
    }

    static SpriteMetaData Slice(string name, float x, float y, float width, float height) =>
        new SpriteMetaData { name = name, rect = new Rect(x, H-y-height, width, height), pivot = new Vector2(.5f,.5f), alignment = 0 };
    static GameObject Node(Transform parent, string name)
    { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go; }
    static void Stretch(RectTransform r) { r.anchorMin=Vector2.zero; r.anchorMax=Vector2.one; r.pivot=new Vector2(.5f,.5f); r.offsetMin=r.offsetMax=Vector2.zero; }
    // 所有位置均映射到原图归一化坐标，随等比画板缩放，避免宽屏上按钮偏离底图。
    static void Box(RectTransform r, float x, float y, float width, float height)
    { r.anchorMin=new Vector2(x/W,1-(y+height)/H); r.anchorMax=new Vector2((x+width)/W,1-y/H); r.pivot=new Vector2(.5f,.5f); r.offsetMin=r.offsetMax=Vector2.zero; }
    static TextMeshProUGUI Label(Transform parent, string name, string value, float x,float y,float w,float h,float size,bool bold=false)
    {
        var go=Node(parent,name); Box((RectTransform)go.transform,x,y,w,h);
        var text=go.AddComponent<TextMeshProUGUI>(); text.font=font; text.text=value; text.color=Cream;
        text.fontSize=size*1.15f; text.fontSizeMax=size*1.15f; text.fontSizeMin=12; text.enableAutoSizing=true;
        text.fontStyle=bold?FontStyles.Bold:FontStyles.Normal; text.alignment=TextAlignmentOptions.Center;
        if (bold && size >= 50) text.fontSharedMaterial = headingMaterial;
        text.raycastTarget=false; text.enableWordWrapping=false; text.overflowMode=TextOverflowModes.Ellipsis;
        return text;
    }
    static void Localize(TextMeshProUGUI text,string key) { var loc=text.gameObject.AddComponent<LocalizedText>(); loc.Key=key; loc.Texts.Clear(); }
    static void Fixed(Transform p,string name,string value,string key,float x,float y,float w,float h,float size,bool bold=false,TextAlignmentOptions align=TextAlignmentOptions.Center)
    { var t=Label(p,name,value,x,y,w,h,size,bold); t.alignment=align; Localize(t,key); }
    static Button Button(Transform p,string name,string sprite,float x,float y,float w,float h)
    { var b=PlainButton(p,name,x,y,w,h,Color.white); ((Image)b.targetGraphic).sprite=sprites.Single(s=>s.name==sprite); return b; }
    static Button PlainButton(Transform p,string name,float x,float y,float w,float h,Color color)
    {
        var go=Node(p,name); Box((RectTransform)go.transform,x,y,w,h);
        var image=go.AddComponent<Image>(); image.color=color;
        var b=go.AddComponent<Button>(); b.targetGraphic=image;
        var c=b.colors; c.normalColor=Color.white; c.highlightedColor=new Color(1.15f,1.15f,1.15f); c.selectedColor=c.highlightedColor;
        c.pressedColor=new Color(.73f,.73f,.73f); c.disabledColor=new Color(.45f,.45f,.45f,.8f); c.fadeDuration=.1f; b.colors=c;
        return b;
    }
}
