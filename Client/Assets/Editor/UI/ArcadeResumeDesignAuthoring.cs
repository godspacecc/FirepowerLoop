using System;
using System.Reflection;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>将已确认的深青灰、暖金详情样式制作到正式 Prefab，并通过现有生成器维护 View 契约。</summary>
public static class ArcadeResumeDesignAuthoring
{
    /// <summary>本次制作的唯一正式资源路径。</summary>
    private const string PrefabPath = "Assets/Game/Prefabs/UI/Arcade/ArcadeResume.prefab";
    /// <summary>标题、数值使用的暖白色。</summary>
    private static readonly Color Ivory = new Color(.96f, .92f, .80f);
    /// <summary>次级标签与说明使用的低强调色。</summary>
    private static readonly Color Muted = new Color(.69f, .68f, .57f);
    /// <summary>主操作、状态与图标使用的暖金色。</summary>
    private static readonly Color Gold = new Color(.94f, .62f, .21f);

    /// <summary>仅修改详情框的静态布局、装饰及引用；不读取或改变任何存档。</summary>
    public static string Apply()
    {
        if (EditorApplication.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅允许在 FirepowerLoop 编辑模式制作详情框");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var view = root.GetComponent<ArcadeResumeUI>();
            var collector = root.GetComponent<ReferenceCollector>();
            var board = (RectTransform)root.transform.Find("SafeContent/ArtworkBoard");
            var fit = board.GetComponent<AspectRatioFitter>();
            if (fit != null) Object.DestroyImmediate(fit);
            board.anchorMin = board.anchorMax = board.pivot = new Vector2(.5f, .5f);
            board.anchoredPosition = Vector2.zero; board.sizeDelta = new Vector2(880, 620);
            board.GetComponent<Image>().color = Color.clear;
            root.GetComponent<Image>().color = new Color(0, .025f, .035f, .68f);
            foreach (var name in new[] { "Brand", "Subtitle", "SoloTitle", "SoloCaption", "OnlineTitle", "OnlineCaption", "IsolationHint" })
                board.Find(name).gameObject.SetActive(false);
            view.BtnOnline.gameObject.SetActive(false);
            var frame = Decor(board, "DetailsFrame", ArcadeResumeDecoration.DecorationShape.Frame,
                0, 0, 880, 620, new Color(.74f, .50f, .23f), new Color(.025f, .065f, .075f, 1), 22);
            frame.transform.SetAsFirstSibling();
            Rule(board, "TopAccent", 24, 0, 334, 3, Gold);
            var template = view.DialogTitle;
            FixedText(board, template, "RecordBrand", "ui.arcade.details.brand", "FIREPOWERLOOP", 42, 24, 650, 24, 14).characterSpacing = 5;
            TextStyle(template, 42, Ivory); Box(template.rectTransform, 42, 54, 680, 64);
            template.fontStyle = FontStyles.Bold;
            Rule(board, "HeaderRule", 42, 132, 796, 1, new Color(.60f, .56f, .41f, .65f));
            Box((RectTransform)view.BtnClose.transform, 800, 19, 54, 54);
            view.BtnClose.GetComponent<Image>().color = Color.clear;
            var close = view.BtnClose.GetComponentInChildren<TMP_Text>(true);
            TextStyle(close, 38, Ivory); close.alignment = TextAlignmentOptions.Center;
            Box(close.rectTransform, 0, 0, 54, 54);
            TextButton(view.BtnClose, close, Ivory);

            // 内容根保留原显隐合同；三项单人数据与双人文本列由控制器互斥展示。
            Box((RectTransform)view.ResumeDetails.transform, 42, 148, 796, 282);
            view.ResumeDetails.GetComponent<Image>().color = Color.clear;
            view.ResumeDetails.transform.Find("SavedTitle").gameObject.SetActive(false);
            FixedText(view.ResumeDetails.transform, template, "ScoreLabel", "ui.arcade.details.score", "本局积分", 0, 0, 500, 36, 24);
            Box(view.TextRewards.rectTransform, 0, 35, 548, 102); TextStyle(view.TextRewards, 76, Ivory);
            view.TextRewards.fontSizeMin = 30; view.TextRewards.text = "12,750";
            view.TextRewards.fontStyle = FontStyles.Bold;
            Rule(view.ResumeDetails.transform, "ScoreRule", 428, 92, 178, 1, new Color(.66f, .46f, .21f, .8f));
            Decor(view.ResumeDetails.transform, "StatusBadge", ArcadeResumeDecoration.DecorationShape.Frame,
                618, 66, 178, 46, new Color(.64f, .43f, .21f), new Color(.08f, .10f, .09f), 5);
            Box(view.TextSummary.rectTransform, 626, 66, 162, 46); TextStyle(view.TextSummary, 24, Gold);
            view.TextSummary.alignment = TextAlignmentOptions.Center;
            view.TextSummary.text = "可继续";
            view.TextSummary.transform.SetAsLastSibling();
            var solo = Node(view.ResumeDetails.transform, "SoloStats", 0, 158, 796, 124);
            Decor(solo, "StripFrame", ArcadeResumeDecoration.DecorationShape.Frame, 0, 0, 796, 124,
                new Color(.26f, .33f, .31f), new Color(.055f, .095f, .10f), 8).transform.SetAsFirstSibling();
            Rule(solo, "DividerOne", 265, 22, 1, 80, new Color(.35f, .40f, .36f, .6f));
            Rule(solo, "DividerTwo", 530, 22, 1, 80, new Color(.35f, .40f, .36f, .6f));
            Stat(solo, collector, template, 0, "TextArenaCount", "ui.arcade.details.arenas", "已完成竞技场", "04", ArcadeResumeDecoration.DecorationShape.Trophy);
            Stat(solo, collector, template, 265, "TextLives", "ui.arcade.details.lives", "剩余生命", "02", ArcadeResumeDecoration.DecorationShape.Heart);
            Stat(solo, collector, template, 530, "TextGold", "ui.arcade.details.gold", "本局金币", "280", ArcadeResumeDecoration.DecorationShape.Coins);
            collector.Add("SoloStats", solo.gameObject);
            var online = Node(view.ResumeDetails.transform, "OnlinePlayers", 0, 158, 796, 124);
            Decor(online, "StripFrame", ArcadeResumeDecoration.DecorationShape.Frame, 0, 0, 796, 124,
                new Color(.26f, .33f, .31f), new Color(.055f, .095f, .10f), 8).transform.SetAsFirstSibling();
            // 双人模式始终为两行固定文本列，不在运行时创建列表元素；姓名独立截断，分数与状态保持可见。
            var names = DynamicText(online, template, "TextPlayerNames", 24, 10, 336, 108, 25);
            names.overflowMode = TextOverflowModes.Ellipsis;
            var scores = DynamicText(online, template, "TextPlayerScores", 370, 10, 220, 108, 25);
            scores.alignment = TextAlignmentOptions.MidlineRight;
            var states = DynamicText(online, template, "TextPlayerStates", 606, 10, 166, 108, 23);
            states.alignment = TextAlignmentOptions.MidlineRight;
            foreach (var text in new[] { names, scores, states })
            {
                text.enableWordWrapping = false; text.lineSpacing = 16; text.richText = false;
                collector.Add(text.name, text);
            }
            collector.Add("OnlinePlayers", online.gameObject); online.gameObject.SetActive(false);
            Box(view.TextMessage.rectTransform, 42, 448, 796, 64); TextStyle(view.TextMessage, 21, Muted);
            view.TextMessage.enableWordWrapping = true; view.TextMessage.text = "从已保存的进度继续";
            view.TextMessage.alignment = TextAlignmentOptions.MidlineLeft;
            PrimaryButton(view.BtnContinue, board, 350, 532, 488, 64);
            PrimaryButton(view.BtnSettle, board, 350, 532, 488, 64);
            view.BtnAbandon.transform.SetParent(board, false);
            Box((RectTransform)view.BtnAbandon.transform, 42, 532, 254, 64);
            view.BtnAbandon.GetComponent<Image>().color = Color.clear;
            var abandon = view.BtnAbandon.GetComponentInChildren<TMP_Text>(true);
            TextStyle(abandon, 27, new Color(.80f, .39f, .25f)); abandon.alignment = TextAlignmentOptions.Center;
            Box(abandon.rectTransform, 0, 0, 254, 64);
            TextButton(view.BtnAbandon, abandon, new Color(.80f, .39f, .25f));
            Rule(view.BtnAbandon.transform, "UnderLine", 20, 61, 214, 1, new Color(.59f, .29f, .19f));
            collector.Sort();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        return "详情框 Prefab 已制作并运行正式 UI 生成器";
    }

    /// <summary>生成代码编译完成后，复用生成器的正式字段回绑步骤。</summary>
    public static string Bind()
    {
        if (EditorApplication.isCompiling) throw new InvalidOperationException("等待生成代码编译完成");
        typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { PrefabPath, "Game.ArcadeResumeUI" });
        return "详情框生成字段已回绑";
    }

    /// <summary>制作一项固定单人指标，标签由本地化组件持有，值由 Controller 持有。</summary>
    private static void Stat(Transform parent, ReferenceCollector collector, TMP_Text template, float x,
        string name, string key, string label, string preview, ArcadeResumeDecoration.DecorationShape icon)
    {
        Decor(parent, name + "Icon", icon, x + 24, 43, 40, 40, Gold, Color.clear, 0);
        FixedText(parent, template, name + "Label", key, label, x + 88, 15, 165, 36, 22);
        var value = DynamicText(parent, template, name, x + 88, 51, 165, 60, 40);
        value.fontStyle = FontStyles.Bold;
        value.text = preview; collector.Add(name, value);
    }

    /// <summary>制作主操作，保留原 Button 和监听目标，只替换静态面片与箭头。</summary>
    private static void PrimaryButton(Button button, Transform parent, float x, float y, float width, float height)
    {
        button.transform.SetParent(parent, false); Box((RectTransform)button.transform, x, y, width, height);
        button.GetComponent<Image>().color = Color.clear;
        var face = Decor(button.transform, "ActionFace", ArcadeResumeDecoration.DecorationShape.Frame,
            0, 0, width, height, new Color(1, .76f, .36f), Gold, 10);
        face.transform.SetAsFirstSibling(); button.targetGraphic = face;
        var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1, 1, .86f);
        colors.selectedColor = new Color(1, 1, .72f);
        colors.pressedColor = new Color(.80f, .75f, .66f); colors.disabledColor = new Color(.5f, .5f, .5f, .5f);
        button.colors = colors;
        var label = button.GetComponentInChildren<TMP_Text>(true);
        TextStyle(label, 30, new Color(.075f, .075f, .055f)); label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Bold;
        Box(label.rectTransform, 20, 0, width - 90, height);
        Decor(button.transform, "ActionArrow", ArcadeResumeDecoration.DecorationShape.Arrow,
            width - 58, 18, 28, 28, new Color(.075f, .075f, .055f), Color.clear, 0);
    }

    /// <summary>透明底按钮把可见文字作为着色目标，保留悬停、键盘选中和禁用反馈。</summary>
    private static void TextButton(Button button, TMP_Text label, Color idle)
    {
        label.color = Color.white; button.targetGraphic = label;
        var colors = button.colors;
        colors.normalColor = idle; colors.highlightedColor = Gold; colors.selectedColor = Gold;
        colors.pressedColor = Muted; colors.disabledColor = new Color(idle.r, idle.g, idle.b, .4f);
        button.colors = colors;
    }

    /// <summary>创建或更新具有可读中文预览的固定本地化标签。</summary>
    private static TMP_Text FixedText(Transform parent, TMP_Text template, string name, string key, string preview,
        float x, float y, float width, float height, float size)
    {
        var text = DynamicText(parent, template, name, x, y, width, height, size);
        var loc = text.GetComponent<LocalizedText>() ?? text.gameObject.AddComponent<LocalizedText>();
        loc.Key = key; loc.Texts.Clear(); text.text = preview; text.color = Muted;
        return text;
    }

    /// <summary>创建动态 TMP；不挂本地化组件，避免语言事件覆盖 Controller 写入的当前值。</summary>
    private static TextMeshProUGUI DynamicText(Transform parent, TMP_Text template, string name,
        float x, float y, float width, float height, float size)
    {
        var rect = Node(parent, name, x, y, width, height);
        var text = rect.GetComponent<TextMeshProUGUI>() ?? rect.gameObject.AddComponent<TextMeshProUGUI>();
        var loc = text.GetComponent<LocalizedText>();
        if (loc != null) Object.DestroyImmediate(loc);
        text.font = template.font; text.fontSharedMaterial = template.font.material;
        TextStyle(text, size, Ivory); return text;
    }

    /// <summary>统一字体、自动缩放和非交互文字的静态设置。</summary>
    private static void TextStyle(TMP_Text text, float size, Color color)
    {
        text.fontSize = text.fontSizeMax = size; text.fontSizeMin = size * .65f;
        text.enableAutoSizing = true; text.color = color; text.raycastTarget = false;
        text.enableWordWrapping = false; text.alignment = TextAlignmentOptions.MidlineLeft;
        text.overflowMode = TextOverflowModes.Overflow;
    }

    /// <summary>创建序列化的装饰图形，不生成临时图片、材质或纹理。</summary>
    private static ArcadeResumeDecoration Decor(Transform parent, string name, ArcadeResumeDecoration.DecorationShape shape,
        float x, float y, float width, float height, Color line, Color fill, float cut)
    {
        var rect = Node(parent, name, x, y, width, height);
        if (rect.GetComponent<CanvasRenderer>() == null) rect.gameObject.AddComponent<CanvasRenderer>();
        var graphic = rect.GetComponent<ArcadeResumeDecoration>() ?? rect.gameObject.AddComponent<ArcadeResumeDecoration>();
        graphic.Shape = shape; graphic.color = line; graphic.FillColor = fill;
        graphic.CornerCut = cut; graphic.StrokeWidth = shape == ArcadeResumeDecoration.DecorationShape.Frame ? 1.2f : 2f;
        graphic.raycastTarget = false; graphic.SetVerticesDirty(); return graphic;
    }

    /// <summary>创建固定的细分隔线。</summary>
    private static void Rule(Transform parent, string name, float x, float y, float width, float height, Color color)
    {
        var rect = Node(parent, name, x, y, width, height);
        var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
        image.color = color; image.raycastTarget = false;
    }

    /// <summary>复用已有节点以确保制作操作幂等。</summary>
    private static RectTransform Node(Transform parent, string name, float x, float y, float width, float height)
    {
        var child = parent.Find(name) as RectTransform;
        if (child == null) { child = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); child.SetParent(parent, false); }
        Box(child, x, y, width, height); return child;
    }

    /// <summary>静态布局统一使用父节点左上角坐标；运行时不再复制这些坐标。</summary>
    private static void Box(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
    }
}
