using System;
using System.Reflection;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>按D方案将冲刺显示为爆发闪电；触屏、桌面指示与编辑预览共用一个Sprite。</summary>
public static class ArcadeDashIconAuthoring
{
    private const string HudPath = "Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab";
    private const string LayoutPath = "Assets/Game/Prefabs/UI/Home/MobileControlLayout.prefab";
    private const string IconPath = "Assets/Game/Art/UI/UtilityButtons/DashLightning.png";

    public static string Run()
    {
        if (Application.dataPath != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying)
            throw new InvalidOperationException("仅本工程编辑模式制作");
        AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        var textureSettings = new TextureImporterSettings();
        importer.ReadTextureSettings(textureSettings);
        textureSettings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(textureSettings);
        importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None; importer.maxTextureSize = 512;
        importer.SaveAndReimport();
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
        var hud = PrefabUtility.LoadPrefabContents(HudPath);
        try
        {
            var ui = hud.GetComponent<ArcadeHudUI>();
            var references = hud.GetComponent<ReferenceCollector>();
            var button = ui.BtnDash.transform;
            var image = ui.BtnDash.GetComponent<Image>();
            image.sprite = ui.BtnBomb.image.sprite; image.color = ui.BtnBomb.image.color;
            image.type = Image.Type.Simple; image.preserveAspect = true;
            image.rectTransform.sizeDelta = new Vector2(96, 96);
            var oldIcon = button.Find("AbilityIcon");
            if (oldIcon != null) Object.DestroyImmediate(oldIcon.gameObject);
            var count = button.GetComponentInChildren<TextMeshProUGUI>(true);
            count.transform.SetParent(button, false);
            var oldBadge = button.Find("CountBadge");
            if (oldBadge != null) Object.DestroyImmediate(oldBadge.gameObject);
            var badge = Object.Instantiate(ui.BtnBomb.transform.Find("CountBadge").gameObject, button, false);
            badge.name = "CountBadge";
            Object.DestroyImmediate(badge.GetComponentInChildren<TextMeshProUGUI>().gameObject);
            count.transform.SetParent(badge.transform, false); count.name = "Count";
            var localized = count.GetComponent<LocalizedText>();
            if (localized != null) Object.DestroyImmediate(localized);
            Place(count.rectTransform, new Vector2(32, 32));
            count.text = "2"; count.fontSize = count.fontSizeMax = 24; count.fontSizeMin = 14;
            count.alignment = TextAlignmentOptions.Center; count.enableAutoSizing = true; count.raycastTarget = false;
            var icon = new GameObject("AbilityIcon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            icon.transform.SetParent(button, false); icon.transform.SetAsFirstSibling();
            Place(icon.rectTransform, new Vector2(96, 96));
            icon.sprite = sprite; icon.preserveAspect = true; icon.raycastTarget = false;
            references.Add("TextMobileDashCount", count);

            var desktop = ui.DesktopDashIcon.gameObject;
            var desktopLocalization = desktop.GetComponent<LocalizedText>();
            if (desktopLocalization != null) Object.DestroyImmediate(desktopLocalization);
            Object.DestroyImmediate(ui.DesktopDashIcon);
            var desktopIcon = desktop.AddComponent<Image>();
            desktopIcon.sprite = sprite; desktopIcon.preserveAspect = true; desktopIcon.raycastTarget = false;
            desktopIcon.rectTransform.sizeDelta = new Vector2(48, 48);
            references.Add("DesktopDashIcon", desktopIcon);
            references.Sort(); PrefabUtility.SaveAsPrefabAsset(hud, HudPath);

            var layout = PrefabUtility.LoadPrefabContents(LayoutPath);
            try
            {
                var preview = layout.GetComponent<MobileControlLayoutUI>().DashPreview;
                for (var i = preview.transform.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(preview.transform.GetChild(i).gameObject);
                var previewImage = preview.GetComponent<Image>();
                previewImage.sprite = image.sprite; previewImage.color = image.color;
                previewImage.type = Image.Type.Simple; previewImage.preserveAspect = true;
                previewImage.rectTransform.sizeDelta = image.rectTransform.sizeDelta;
                foreach (var name in new[] { "AbilityIcon", "CountBadge" })
                {
                    var copy = Object.Instantiate(button.Find(name).gameObject, preview.transform, false);
                    copy.name = name;
                }
                PrefabUtility.SaveAsPrefabAsset(layout, LayoutPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(layout); }
        }
        finally { PrefabUtility.UnloadPrefabContents(hud); }
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(LayoutPath));
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath));
        // 首次类型变更需先完成编译；后续重复制作可直接回填两份现成View，避免单一待绑定请求遗漏。
        if (typeof(ArcadeHudUI).GetField("DesktopDashIcon").FieldType == typeof(Image)
            && typeof(ArcadeHudUI).GetField("TextMobileDashCount") != null) return Bind();
        return "已生成冲刺图标和View；等待编译后调用Bind完成两份正式引用回填";
    }

    private static void Place(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.anchoredPosition = Vector2.zero; rect.sizeDelta = size;
    }

    public static string Bind()
    {
        var bind = typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent", BindingFlags.Static | BindingFlags.NonPublic);
        bind.Invoke(null, new object[] { HudPath, "Game.ArcadeHudUI" });
        bind.Invoke(null, new object[] { LayoutPath, "Game.MobileControlLayoutUI" });
        return "冲刺闪电已同步触屏、桌面和全屏编辑，两份生成引用已回填";
    }
}
