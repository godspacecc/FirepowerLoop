using System;
using System.Linq;
using System.Reflection;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>以正式街机HUD的实际按钮尺寸制作无玩法回调的布局预览，再执行项目UI生成器。</summary>
public static class ArcadeMobileLayoutAuthoring
{
    private const string Path = "Assets/Game/Prefabs/UI/Home/MobileControlLayout.prefab";

    public static string Run()
    {
        if (Application.dataPath != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying)
            throw new InvalidOperationException("仅本项目编辑模式允许制作");
        var root = PrefabUtility.LoadPrefabContents(Path);
        try
        {
            var collector = root.GetComponent<ReferenceCollector>();
            if (collector.data.Any(item => item.key == "PreviewViewport"))
                throw new InvalidOperationException("街机布局已制作，拒绝重复创建");
            var old = root.GetComponent<MobileControlLayoutUI>();
            var viewport = new GameObject("PreviewViewport", typeof(RectTransform), typeof(Image));
            viewport.transform.SetParent(old.LayoutArea, false);
            viewport.transform.SetAsFirstSibling();
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero; viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(24, 24); viewportRect.offsetMax = new Vector2(-24, -300);
            viewport.GetComponent<Image>().color = new Color(.025f, .05f, .065f, .96f);
            viewport.GetComponent<Image>().raycastTarget = false;
            var area = new GameObject("ArcadePreviewArea", typeof(RectTransform), typeof(Image));
            area.transform.SetParent(viewport.transform, false);
            var rect = (RectTransform)area.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.sizeDelta = new Vector2(1920, 1080);
            area.GetComponent<Image>().color = new Color(.07f, .12f, .15f, 1);
            area.GetComponent<Image>().raycastTarget = false;
            old.MovePreview.transform.SetParent(rect, false);
            old.AimPreview.transform.SetParent(rect, false);
            foreach (var key in new[] { "PausePreview", "InventoryPreview", "BuildPreview", "AttackPreview", "ReloadPreview", "StatusPreview", "HealthPreview", "WeaponPreview", "DashArea", "BombArea", "ArcadePauseArea" })
            {
                var item = collector.data.Single(value => value.key == key);
                var component = item.gameObject as Component;
                Object.DestroyImmediate(component != null ? component.gameObject : (GameObject)item.gameObject);
                collector.Remove(key);
            }
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab").GetComponent<ArcadeHudUI>();
            var sources = new[] { hud.BtnPause, hud.BtnBomb, hud.BtnDash, hud.BtnFieldUpgrade, hud.BtnDonateLife };
            var names = new[] { "Pause", "Bomb", "Dash", "FieldUpgrade", "DonateLife" };
            var keys = new[] { "ui.arcade.pause", "ui.arcade.bomb", "ui.arcade.dash", "ui.arcade.slayermart.hud_upgrade", "ui.arcade.coop.donate" };
            for (var i = 0; i < names.Length; i++)
            {
                var go = new GameObject(names[i] + "Preview", typeof(RectTransform), typeof(Image), typeof(MobileLayoutStickHandle));
                go.transform.SetParent(rect, false);
                ((RectTransform)go.transform).sizeDelta = ((RectTransform)sources[i].transform).sizeDelta;
                var image = go.GetComponent<Image>(); image.sprite = sources[i].image.sprite; image.type = sources[i].image.type;
                image.color = sources[i].image.color; image.raycastTarget = true;
                var label = Object.Instantiate(sources[i].GetComponentInChildren<TextMeshProUGUI>(true), go.transform, false);
                label.name = "Label"; label.raycastTarget = false;
                label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(4, 4); label.rectTransform.offsetMax = new Vector2(-4, -4);
                label.fontSize = 28; label.enableAutoSizing = true; label.fontSizeMin = 18; label.fontSizeMax = 28;
                var localized = label.GetComponent<LocalizedText>() ?? label.gameObject.AddComponent<LocalizedText>();
                localized.Key = keys[i]; localized.Texts.Clear(); localized.Refresh();
                collector.Add(names[i] + "Preview", go.GetComponent<MobileLayoutStickHandle>());
            }
            collector.Add("ControlSafeArea", old.LayoutArea);
            collector.Add("LayoutArea", rect); collector.Add("PreviewViewport", viewportRect);
            collector.Sort(); PrefabUtility.SaveAsPrefabAsset(root, Path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        if (!EditorApplication.ExecuteMenuItem("Assets/智能更新UI")) throw new InvalidOperationException("UI生成器未执行");
        return "正式街机预览已制作并执行UI生成器";
    }

    public static string Bind()
    {
        typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { Path, "Game.MobileControlLayoutUI" });
        return "正式View已由生成器绑定";
    }

    /// <summary>将预览放回原安全区容器，虚拟几何使用完整安全区而非物理屏幕尺寸。</summary>
    public static string UpgradeSafeArea()
    {
        var root = PrefabUtility.LoadPrefabContents(Path);
        try
        {
            var collector = root.GetComponent<ReferenceCollector>();
            var ui = root.GetComponent<MobileControlLayoutUI>();
            var area = root.GetComponentsInChildren<RectTransform>(true).Single(value => value.name == "LayoutArea");
            ui.PreviewViewport.SetParent(area, false);
            ui.PreviewViewport.anchorMin = Vector2.zero; ui.PreviewViewport.anchorMax = Vector2.one;
            ui.PreviewViewport.offsetMin = new Vector2(24, 24); ui.PreviewViewport.offsetMax = new Vector2(-24, -300);
            collector.Add("ControlSafeArea", area); collector.Sort();
            PrefabUtility.SaveAsPrefabAsset(root, Path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        if (!EditorApplication.ExecuteMenuItem("Assets/智能更新UI")) throw new InvalidOperationException("UI生成器未执行");
        return "预览已归原安全区容器，生成器已执行";
    }

    /// <summary>固定文案的设计时预览与默认语言表保持一致，动态文本仍由Controller渲染。</summary>
    public static string RefreshLabels()
    {
        var table = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
        var changed = 0;
        foreach (var path in new[] { Path, "Assets/Game/Prefabs/UI/Home/Page/SettingMobile.prefab" })
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var localized in root.GetComponentsInChildren<LocalizedText>(true))
                {
                    var text = localized.GetComponent<TextMeshProUGUI>();
                    if (text != null && !string.IsNullOrEmpty(localized.Key) && table[localized.Key] != null)
                    { var value = (string)table[localized.Key]; if (text.text != value) { text.text = value; changed++; } }
                }
                var ui = root.GetComponent<MobileControlLayoutUI>();
                if (ui != null)
                {
                    ui.HintText.text = (string)table["ui.mobile_layout.edit_hint"];
                    ui.HintText.fontSize = 24; ui.HintText.enableAutoSizing = true;
                    ui.HintText.fontSizeMin = 18; ui.HintText.fontSizeMax = 24;
                    ui.HintText.rectTransform.sizeDelta = new Vector2(1600, 72);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        Time.timeScale = 1;
        return "固定默认文案同步数=" + changed;
    }
}
