using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>在正式玩家状态 Box 增加倍率与进度条，保留 CustomListView 外部条目归属并执行项目生成器。</summary>
public static class ArcadeMultiplierHudAuthoring
{
    public static string Run()
    {
        const string boxPath = "Assets/Game/Prefabs/UI/Arcade/Box/ArcadePlayerStatusBox.prefab";
        const string hudPath = "Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab";
        var root = PrefabUtility.LoadPrefabContents(boxPath);
        try
        {
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(500, 108);
            var layout = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
            layout.preferredWidth = 500;
            layout.preferredHeight = 108;
            foreach (var name in new[] { "TextName", "HealthBackground", "TextLives", "TextGold" })
            {
                var child = (RectTransform)root.transform.Find(name);
                child.anchoredPosition = new Vector2(child.anchoredPosition.x, 22);
            }
            var template = root.transform.Find("TextName").GetComponent<TextMeshProUGUI>();
            var multiplier = Text(root.transform, template, "TextMultiplier", "倍率 ×1", 16, -31, 180, 36, 28);
            var barRoot = root.transform.Find("MultiplierBackground")?.gameObject;
            if (barRoot == null) barRoot = new GameObject("MultiplierBackground", typeof(RectTransform), typeof(Image));
            barRoot.transform.SetParent(root.transform, false);
            Rect((RectTransform)barRoot.transform, 206, -31, 280, 24);
            var background = barRoot.GetComponent<Image>();
            background.color = new Color(.09f, .12f, .16f, 1);
            background.raycastTarget = false;
            var fillRoot = barRoot.transform.Find("MultiplierFill")?.gameObject;
            if (fillRoot == null) fillRoot = new GameObject("MultiplierFill", typeof(RectTransform), typeof(Image));
            fillRoot.transform.SetParent(barRoot.transform, false);
            var fillRect = (RectTransform)fillRoot.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(0, 2);
            fillRect.offsetMax = new Vector2(0, -2);
            var fill = fillRoot.GetComponent<Image>();
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 0;
            fill.color = new Color(.95f, .69f, .21f, 1);
            fill.raycastTarget = false;
            var state = Text(barRoot.transform, template, "TextMultiplierState", "", 0, 0, 280, 28, 22);
            state.alignment = TextAlignmentOptions.Center;
            state.rectTransform.anchorMin = state.rectTransform.anchorMax = new Vector2(.5f, .5f);
            state.rectTransform.pivot = new Vector2(.5f, .5f);
            state.rectTransform.anchoredPosition = Vector2.zero;
            var collector = root.GetComponent<ReferenceCollector>();
            collector.Add("TextMultiplier", multiplier);
            collector.Add("MultiplierFill", fill);
            collector.Add("TextMultiplierState", state);
            collector.Sort();
            PrefabUtility.SaveAsPrefabAsset(root, boxPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var hud = PrefabUtility.LoadPrefabContents(hudPath);
        try
        {
            hud.GetComponent<ArcadeHudUI>().PlayerStatusList.GetComponent<RectTransform>().sizeDelta = new Vector2(500, 464);
            PrefabUtility.SaveAsPrefabAsset(hud, hudPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(hud); }
        BoxUICodeSpawner.AutoUpdateBoxUI(AssetDatabase.LoadAssetAtPath<GameObject>(boxPath));
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(hudPath));
        return "已更新玩家状态 Box 并调用 Box/UI 正式生成器";
    }

    private static TextMeshProUGUI Text(Transform parent, TextMeshProUGUI template, string name, string value,
        float x, float y, float width, float height, float size)
    {
        var text = parent.Find(name)?.GetComponent<TextMeshProUGUI>();
        if (text == null) text = Object.Instantiate(template, parent);
        text.name = name;
        text.text = value;
        text.fontSize = text.fontSizeMax = size;
        text.fontSizeMin = size - 4;
        text.enableAutoSizing = true;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        Rect(text.rectTransform, x, y, width, height);
        return text;
    }

    private static void Rect(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0, .5f);
        rect.pivot = new Vector2(0, .5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
    }
}
