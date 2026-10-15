using System;
using System.Linq;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>离线制作手机建造入口和操作条，布局及引用直接序列化，不在运行时拼 UI。</summary>
public static class BaseCampMobileUiAuthoring
{
    public static string Apply()
    {
        Mobile(); Selection(); Hud(); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        return "Saved vertical inventory/build tools, selection demolition entry and mobile placement controls.";
    }
    private static void Mobile()
    {
        const string path = "Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var ui = root.GetComponent<MobileJoystickUI>();
            Place((RectTransform)ui.InventoryButton.transform, Vector2.one, new Vector2(-112, -105), new Vector2(160, 100));
            var old = ui.InventoryButton.transform.parent.Find("BuildButton");
            var build = old != null ? old.GetComponent<Button>() : Object.Instantiate(ui.InventoryButton, ui.InventoryButton.transform.parent);
            build.name = "BuildButton";
            Place((RectTransform)build.transform, Vector2.one, new Vector2(-112, -225), new Vector2(160, 100));
            Label(build, "建造", "ui.camp.build");
            ui.BuildButton = build;
            root.GetComponent<ReferenceCollector>().Add("BuildButton", build);
            SaveAndGenerate(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    private static void Selection()
    {
        const string path = "Assets/Game/Prefabs/UI/TemporaryBuilding/TemporaryBuildingSelect.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var ui = root.GetComponent<TemporaryBuildingSelectUI>();
            ((RectTransform)root.transform).sizeDelta = new Vector2(544, 300);
            var parent = ui.BuildingScroll.transform.parent;
            var button = Button(parent, "BtnDestroy", "拆除", "ui.camp.demolish", new Vector2(1,0), new Vector2(-90, 32), new Vector2(140, 40));
            ui.BtnDestroy = button;
            root.GetComponent<ReferenceCollector>().Add("BtnDestroy", button);
            var scroll = (RectTransform)ui.BuildingScroll.transform;
            Place(scroll, Vector2.one * .5f, Vector2.zero, new Vector2(520, 152));
            SaveAndGenerate(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    private static void Hud()
    {
        const string path = "Assets/Game/Prefabs/UI/TemporaryBuilding/TemporaryBuildingHud.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var ui = root.GetComponent<TemporaryBuildingHudUI>();
            var rect = (RectTransform)root.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var panel = root.transform.Find("Panel") as RectTransform;
            Place(panel, new Vector2(.5f, 0), new Vector2(0, 245), new Vector2(620, 140));
            foreach (var text in new[] { ui.TitleText, ui.ModeText, ui.ResourceText, ui.HintText, ui.StatusText })
            {
                var textRect = (RectTransform)text.transform;
                var position = textRect.anchoredPosition;
                textRect.SetParent(panel, false);
                textRect.anchoredPosition = position;
            }
            var old = root.transform.Find("TouchControlsRoot");
            var controls = old != null ? old.gameObject : new GameObject("TouchControlsRoot", typeof(RectTransform));
            controls.transform.SetParent(root.transform, false);
            Place((RectTransform)controls.transform, new Vector2(.5f, 0), new Vector2(0, 95), new Vector2(620, 100));
            ui.TouchControlsRoot = controls;
            ui.BtnRotate = Button(controls.transform, "BtnRotate", "旋转", "ui.camp.rotate", Vector2.one * .5f, new Vector2(-210,0), new Vector2(180,88));
            ui.BtnConfirm = Button(controls.transform, "BtnConfirm", "确认", "ui.camp.confirm", Vector2.one * .5f, Vector2.zero, new Vector2(180,88));
            ui.BtnCancel = Button(controls.transform, "BtnCancel", "取消", "ui.camp.cancel", Vector2.one * .5f, new Vector2(210,0), new Vector2(180,88));
            var collector = root.GetComponent<ReferenceCollector>();
            collector.Add("TouchControlsRoot", controls); collector.Add("BtnRotate", ui.BtnRotate);
            collector.Add("BtnConfirm", ui.BtnConfirm); collector.Add("BtnCancel", ui.BtnCancel);
            controls.SetActive(false);
            SaveAndGenerate(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    private static void SaveAndGenerate(GameObject root, string path)
    {
        EditorUtility.SetDirty(root.GetComponent<BaseUI>());
        PrefabUtility.SaveAsPrefabAsset(root, path);
        // 正式生成工具同步 View 字段与 collector；已有 Controller 被工具保留。
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(path));
    }
    private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = Vector2.one * .5f; rect.anchoredPosition = position; rect.sizeDelta = size; }
    private static Button Button(Transform parent, string name, string text, string key, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var existing = parent.Find(name);
        var go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Place((RectTransform)go.transform, anchor, position, size);
        var image = go.GetComponent<Image>(); image.color = new Color(.11f,.16f,.2f,.92f);
        var button = go.GetComponent<Button>(); button.targetGraphic = image;
        if (go.GetComponentInChildren<TextMeshProUGUI>(true) == null)
        {
            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); label.transform.SetParent(go.transform, false);
            var rect = (RectTransform)label.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var tmp = label.GetComponent<TextMeshProUGUI>(); tmp.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Fonts/Chinese/SourceHanSansCN-Normal SDF.asset");
            tmp.fontSize = 30; tmp.alignment = TextAlignmentOptions.Center; tmp.raycastTarget = false;
        }
        Label(button, text, key); return button;
    }
    private static void Label(Button button, string text, string key)
    {
        var tmp = button.GetComponentInChildren<TextMeshProUGUI>(true); tmp.text = text;
        var active = tmp.gameObject.activeSelf;
        tmp.gameObject.SetActive(false);
        var localized = tmp.GetComponent<LocalizedText>() ?? tmp.gameObject.AddComponent<LocalizedText>();
        localized.Key = key;
        localized.Texts.Clear();
        tmp.gameObject.SetActive(active);
    }
}
