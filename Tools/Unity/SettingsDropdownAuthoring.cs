using System;
using System.Reflection;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>在正式设置Box中制作原生TMP下拉框；普通动作按钮保留独立外观和引用。</summary>
public static class SettingsDropdownAuthoring
{
    private const string Path = "Assets/Game/Prefabs/UI/Home/Box/SettingItemBox.prefab";

    public static string Run()
    {
        if (Application.dataPath != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying)
            throw new InvalidOperationException("仅允许本工程编辑模式制作");
        var root = PrefabUtility.LoadPrefabContents(Path);
        try
        {
            var ui = root.GetComponent<SettingItemBoxUI>();
            var existing = root.transform.Find("ValueDropdown");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
            var dropdownObject = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources
            {
                standard = ui.BtnContext.image.sprite,
                background = ui.BtnContext.image.sprite,
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd")
            });
            dropdownObject.name = "ValueDropdown";
            dropdownObject.transform.SetParent(root.transform, false);
            var rect = (RectTransform)dropdownObject.transform;
            var source = (RectTransform)ui.BtnContext.transform;
            rect.anchorMin = source.anchorMin; rect.anchorMax = source.anchorMax;
            rect.pivot = source.pivot; rect.sizeDelta = source.sizeDelta; rect.anchoredPosition = source.anchoredPosition;
            var dropdown = dropdownObject.GetComponent<TMP_Dropdown>();
            var image = dropdownObject.GetComponent<Image>(); image.color = ui.BtnContext.image.color;
            dropdown.transition = Selectable.Transition.ColorTint;
            var colors = dropdown.colors; colors.highlightedColor = new Color(.8f, .9f, .85f); dropdown.colors = colors;
            dropdown.alphaFadeSpeed = 0;
            foreach (var text in dropdownObject.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                text.font = ui.TextContext.font; text.fontSharedMaterial = ui.TextContext.fontSharedMaterial;
                text.fontSize = 26; text.fontSizeMin = 20; text.fontSizeMax = 26;
                text.enableAutoSizing = true; text.color = ui.TextContext.color; text.raycastTarget = false;
            }
            Stretch(dropdown.captionText.rectTransform, 22, 6, 56, 6);
            dropdown.captionText.alignment = TextAlignmentOptions.MidlineRight;
            dropdown.itemText.alignment = TextAlignmentOptions.MidlineLeft;
            Stretch(dropdown.itemText.rectTransform, 38, 4, 12, 4);
            var template = dropdown.template;
            template.sizeDelta = new Vector2(0, 256); template.anchoredPosition = new Vector2(0, -8);
            template.GetComponent<Image>().color = new Color32(16, 48, 56, 255);
            var scroll = template.GetComponent<ScrollRect>();
            // 候选项较少时不显示滚动条；超长列表沿用原生滚动行为和本页配色。
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbar.GetComponent<Image>().color = new Color32(16, 48, 56, 255);
            scroll.verticalScrollbar.targetGraphic.color = new Color32(77, 126, 133, 255);
            scroll.content.sizeDelta = new Vector2(0, 64);
            var item = dropdown.itemText.transform.parent.GetComponent<Toggle>();
            ((RectTransform)item.transform).sizeDelta = new Vector2(0, 64);
            item.targetGraphic.color = new Color32(29, 72, 81, 255);
            var itemColors = item.colors; itemColors.highlightedColor = new Color32(55, 97, 106, 255);
            itemColors.selectedColor = itemColors.highlightedColor; item.colors = itemColors;
            var oldArrow = dropdownObject.transform.Find("Arrow"); Object.DestroyImmediate(oldArrow.gameObject);
            var buttonChevron = ui.BtnContext.transform.Find("Chevron");
            var arrow = Object.Instantiate(buttonChevron.gameObject, dropdownObject.transform, false);
            arrow.name = "Arrow"; arrow.SetActive(true);
            var arrowRect = (RectTransform)arrow.transform;
            arrowRect.anchorMin = arrowRect.anchorMax = new Vector2(1, .5f);
            arrowRect.anchoredPosition = new Vector2(-28, 0);
            buttonChevron.gameObject.SetActive(false);
            Stretch(ui.TextContext.rectTransform, 22, 6, 22, 6);
            ui.TextContext.alignment = TextAlignmentOptions.Center;
            dropdown.ClearOptions(); dropdown.AddOptions(new System.Collections.Generic.List<string> { "选择" });
            dropdown.template.gameObject.SetActive(false);
            root.GetComponent<ReferenceCollector>().Add("ValueDropdown", dropdown);
            root.GetComponent<ReferenceCollector>().Sort();
            PrefabUtility.SaveAsPrefabAsset(root, Path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        BoxUICodeSpawner.AutoUpdateBoxUI(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
        return "正式设置Box已制作下拉模板并执行Box生成器";
    }

    private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top);
    }

    public static string Bind()
    {
        typeof(BoxUICodeSpawner).GetMethod("BindGeneratedComponent", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { Path, "Game.SettingItemBoxUI" });
        return "正式设置Box引用已由生成器绑定";
    }
}
