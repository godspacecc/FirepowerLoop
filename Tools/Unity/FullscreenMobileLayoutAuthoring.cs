using System;
using System.Linq;
using System.Reflection;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>原位升级触屏布局编辑器：全屏实尺编辑，工具栏独立浮在控件上方。</summary>
public static class FullscreenMobileLayoutAuthoring
{
    private const string PrefabPath = "Assets/Game/Prefabs/UI/Home/MobileControlLayout.prefab";

    public static string Run()
    {
        if (Application.dataPath != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying)
            throw new InvalidOperationException("仅允许在本项目编辑模式制作");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var ui = root.GetComponent<MobileControlLayoutUI>();
            var references = root.GetComponent<ReferenceCollector>();
            var toolbar = ui.ControlSafeArea.Find("Toolbar");
            root.GetComponent<Image>().color = new Color(.025f, .04f, .055f, 1);
            Stretch(ui.PreviewViewport);
            Stretch(ui.LayoutArea);
            ui.PreviewViewport.GetComponent<Image>().color = Color.clear;
            ui.LayoutArea.GetComponent<Image>().color = Color.clear;
            toolbar.SetAsLastSibling();
            references.Add("ToolbarRoot", toolbar.gameObject);

            if (!references.data.Any(entry => entry.key == "CollapseToolbarButton"))
            {
                var collapse = MakeButton(toolbar, "CollapseToolbarButton", new Vector2(160, 64));
                var rect = (RectTransform)collapse.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
                rect.pivot = new Vector2(.5f, 1); rect.anchoredPosition = Vector2.zero;
                Chevron(collapse.transform, true, Vector2.zero);
                references.Add("CollapseToolbarButton", collapse);
            }
            if (!references.data.Any(entry => entry.key == "ExpandToolbarButton"))
            {
                var expand = MakeButton(ui.ControlSafeArea, "ExpandToolbarButton", new Vector2(220, 64));
                var rect = (RectTransform)expand.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
                rect.pivot = new Vector2(.5f, 1); rect.anchoredPosition = new Vector2(0, -8);
                var label = Object.Instantiate(ui.TrialText, expand.transform, false);
                label.name = "Label";
                label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(8, 4); label.rectTransform.offsetMax = new Vector2(-48, -4);
                label.fontSize = 28; label.fontSizeMin = 18; label.fontSizeMax = 28;
                label.enableAutoSizing = true; label.raycastTarget = false;
                label.text = "操控布局";
                var localized = label.GetComponent<LocalizedText>() ?? label.gameObject.AddComponent<LocalizedText>();
                localized.Key = "ui.mobile_layout.title"; localized.Texts.Clear();
                Chevron(expand.transform, false, new Vector2(78, 0));
                expand.gameObject.SetActive(false);
                references.Add("ExpandToolbarButton", expand);
            }
            references.Sort();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (!EditorApplication.ExecuteMenuItem("Assets/智能更新UI"))
            throw new InvalidOperationException("正式UI生成器未执行");
        return "全屏编辑Prefab已保存，正式View生成器已执行";
    }

    /// <summary>保持安全区内完整拉伸，静态布局只在Prefab中定义。</summary>
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one * .5f;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static Button MakeButton(Transform parent, string name, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        ((RectTransform)go.transform).sizeDelta = size;
        go.GetComponent<Image>().color = new Color(.15f, .38f, .35f, 1);
        var button = go.GetComponent<Button>(); button.targetGraphic = go.GetComponent<Image>();
        var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
        return button;
    }

    /// <summary>使用原生UI线段绘制折叠图标，不引入字体字形或额外图片依赖。</summary>
    private static void Chevron(Transform parent, bool up, Vector2 center)
    {
        for (var i = 0; i < 2; i++)
        {
            var go = new GameObject(i == 0 ? "ChevronLeft" : "ChevronRight", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.sizeDelta = new Vector2(19, 4);
            rect.anchoredPosition = center + new Vector2(i == 0 ? -6 : 6, 0);
            rect.localRotation = Quaternion.Euler(0, 0, (i == 0 ? 45 : -45) * (up ? 1 : -1));
            var image = go.GetComponent<Image>(); image.color = Color.white; image.raycastTarget = false;
        }
    }

    public static string Bind()
    {
        typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { PrefabPath, "Game.MobileControlLayoutUI" });
        return "正式生成View引用已绑定";
    }
}
