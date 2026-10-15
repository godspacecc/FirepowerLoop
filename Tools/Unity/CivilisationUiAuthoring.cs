using System;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>离线制作文明结算与工艺窗口；引用和布局保存到 Prefab，通过正式生成器生成 View。</summary>
public static class CivilisationUiAuthoring
{
    private const string Root = "Assets/Game/Prefabs/UI/Civilisation";
    private static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var index = path.LastIndexOf('/'); Folder(path.Substring(0, index));
        AssetDatabase.CreateFolder(path.Substring(0, index), path.Substring(index + 1));
    }
    public static string Workshop() => Build("CivilisationWorkshop", "civilisation.workshop.title", "干粮制作", true);
    public static string Settlement() => Build("CivilisationSettlement", "civilisation.settlement.title", "远征结算", false);
    private static string Build(string name, string titleKey, string title, bool production)
    {
        Folder(Root);
        var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(ReferenceCollector));
        root.SetActive(false);
        var rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(920, 760);
        root.GetComponent<Image>().color = new Color(.08f, .12f, .11f, .98f);
        var collector = root.GetComponent<ReferenceCollector>();
        var template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Expedition/MapSelection.prefab");
        var font = template.GetComponentInChildren<TextMeshProUGUI>(true).font;
        Label(root.transform, font, "Title", title, new Vector2(0, 326), new Vector2(850, 65), 40, titleKey);
        // 单个可滚动的动态文本区域，没有由 Controller 管理的重复 UI 对象。
        var viewport = new GameObject("BodyViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        viewport.transform.SetParent(root.transform, false);
        Place((RectTransform)viewport.transform, new Vector2(0, 10), new Vector2(850, 520));
        viewport.GetComponent<Image>().color = new Color(0, 0, 0, .15f);
        var body = Label(viewport.transform, font, "BodyText", "", Vector2.zero, new Vector2(800, 520), 30, null);
        body.alignment = TextAlignmentOptions.TopLeft;
        var bodyRect = (RectTransform)body.transform; bodyRect.anchorMin = bodyRect.anchorMax = new Vector2(.5f, 1); bodyRect.pivot = new Vector2(.5f, 1); bodyRect.anchoredPosition = new Vector2(0, -12);
        var fitter = body.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = viewport.GetComponent<ScrollRect>(); scroll.viewport = (RectTransform)viewport.transform; scroll.content = bodyRect; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
        collector.Add("BodyText", body); collector.Add("BodyScroll", scroll);
        collector.Add("BtnContinue", Button(root.transform, font, "BtnContinue", "继续", "civilisation.continue", new Vector2(production ? 210 : 0, -315)));
        if (production) collector.Add("BtnProduce", Button(root.transform, font, "BtnProduce", "制作一份", "civilisation.produce", new Vector2(-210, -315)));
        var path = Root + "/" + name + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path); Object.DestroyImmediate(root);
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        return path;
    }
    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    { rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; rect.anchoredPosition = position; rect.sizeDelta = size; }
    private static TextMeshProUGUI Label(Transform parent, TMP_FontAsset font, string name, string text, Vector2 position, Vector2 size, float fontsize, string key)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.SetActive(false); go.transform.SetParent(parent, false);
        Place((RectTransform)go.transform, position, size);
        var tmp = go.GetComponent<TextMeshProUGUI>(); tmp.font = font; tmp.fontSize = fontsize; tmp.text = text; tmp.color = new Color(.92f,.89f,.77f); tmp.raycastTarget = false; tmp.alignment = TextAlignmentOptions.Center;
        if (key != null) go.AddComponent<LocalizedText>().Key = key;
        go.SetActive(true); return tmp;
    }
    private static Button Button(Transform parent, TMP_FontAsset font, string name, string text, string key, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false);
        Place((RectTransform)go.transform, position, new Vector2(320, 80));
        go.GetComponent<Image>().color = new Color(.28f, .36f, .25f);
        Label(go.transform, font, "Label", text, Vector2.zero, new Vector2(300, 75), 30, key);
        var button = go.GetComponent<Button>(); button.targetGraphic = go.GetComponent<Image>(); return button;
    }
}
