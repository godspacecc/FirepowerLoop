using System;
using System.Linq;
using System.Reflection;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>将已确认的手机信息区制作到正式 HUD；布局保存在 Prefab，桌面复用原内容。</summary>
public static class ArcadeMobileHudAuthoring
{
    const string Hud = "Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab";
    const string Box = "Assets/Game/Prefabs/UI/Arcade/Box/ArcadePlayerStatusBox.prefab";
    const string Layout = "Assets/Game/Prefabs/UI/Home/MobileControlLayout.prefab";

    public static string Run()
    {
        if (EditorApplication.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅在 FirepowerLoop 编辑模式制作 HUD");
        BuildPlayerBox();
        BuildHud();
        BuildLayoutPreview();
        BoxUICodeSpawner.AutoUpdateBoxUI(AssetDatabase.LoadAssetAtPath<GameObject>(Box));
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(Hud));
        return "手机信息区与技能列已保存，已运行正式 Box/UI 生成器";
    }

    public static string Bind()
    {
        typeof(BoxUICodeSpawner).GetMethod("BindGeneratedComponent", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { Box, "Game.ArcadePlayerStatusBoxUI" });
        typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { Hud, "Game.ArcadeHudUI" });
        return "手机 HUD 与玩家 Box 引用已回填";
    }

    /// <summary>按项目字体的真实行高留出空间，避免中文姓名与大分数触发纵向溢出。</summary>
    public static string RefineTextMetrics()
    {
        var root = PrefabUtility.LoadPrefabContents(Box);
        try
        {
            var view = root.GetComponent<ArcadePlayerStatusBoxUI>();
            view.MobileName.rectTransform.anchoredPosition = new Vector2(12, 0);
            view.MobileName.rectTransform.sizeDelta = new Vector2(264, 40);
            view.MobileName.overflowMode = TextOverflowModes.Ellipsis;
            view.MobileScore.rectTransform.anchoredPosition = new Vector2(12, -30);
            view.MobileScore.rectTransform.sizeDelta = new Vector2(254, 56);
            view.MobileScore.fontSizeMin = 26;
            view.MobileState.rectTransform.anchoredPosition = new Vector2(12, -142);
            view.MobileState.rectTransform.sizeDelta = new Vector2(338, 30);
            PrefabUtility.SaveAsPrefabAsset(root, Box);
            return "手机玩家行已按实际字体行高修正";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void BuildPlayerBox()
    {
        var root = PrefabUtility.LoadPrefabContents(Box);
        try
        {
            if (root.transform.Find("MobileContentRoot") != null) return;
            var view = root.GetComponent<ArcadePlayerStatusBoxUI>();
            var collector = root.GetComponent<ReferenceCollector>();
            var original = root.transform.Cast<Transform>().ToArray();
            var desktop = Rect(root.transform, "DesktopContentRoot", 0, 0, 500, 124);
            foreach (var child in original) child.SetParent(desktop, false);
            var mobile = Rect(root.transform, "MobileContentRoot", 0, 0, 368, 172);
            var shade = mobile.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .055f, .065f, .68f); shade.raycastTarget = false;
            collector.Add("DesktopContentRoot", desktop.gameObject);
            collector.Add("MobileContentRoot", mobile.gameObject);
            var name = Text(mobile, view.TextName, "MobileName", 12, 0, 264, 40, 24, new Color(.45f, 1, .3f));
            name.overflowMode = TextOverflowModes.Ellipsis; collector.Add("MobileName", name);
            var score = Text(mobile, view.TextName, "MobileScore", 12, -30, 254, 56, 40, Color.white);
            score.fontSizeMin = 26; collector.Add("MobileScore", score);
            collector.Add("MobileMultiplier", Text(mobile, view.TextName, "MobileMultiplier", 280, -38, 62, 40, 29, Color.white));
            var health = Image(mobile, "MobileHealthTrack", 12, -90, 308, 13, new Color(.12f, .18f, .14f));
            collector.Add("MobileHealthFill", Fill(health.transform, "MobileHealthFill", false));
            var multiplier = Image(mobile, "MobileMultiplierTrack", 344, -12, 12, 122, new Color(.12f, .18f, .14f));
            collector.Add("MobileMultiplierFill", Fill(multiplier.transform, "MobileMultiplierFill", true));
            Icon(mobile, "LifeIcon", "Assets/Game/Art/UI/Status/HealthHeart.png", 12, -116, 25);
            collector.Add("MobileLives", Text(mobile, view.TextName, "MobileLives", 44, -111, 46, 32, 25, Color.white));
            Icon(mobile, "DashIcon", "Assets/Game/Art/UI/UtilityButtons/DashLightning.png", 100, -116, 25);
            collector.Add("MobileDashes", Text(mobile, view.TextName, "MobileDashes", 132, -111, 46, 32, 25, Color.white));
            Icon(mobile, "BombIcon", "Assets/Game/Art/UI/Items/IconAmmoGrenade.png", 188, -116, 25);
            collector.Add("MobileBombs", Text(mobile, view.TextName, "MobileBombs", 220, -111, 46, 32, 25, Color.white));
            collector.Add("MobileState", Text(mobile, view.TextName, "MobileState", 12, -142, 338, 30, 18, new Color(1, .8f, .3f)));
            mobile.gameObject.SetActive(false);
            collector.Sort();
            PrefabUtility.SaveAsPrefabAsset(root, Box);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void BuildHud()
    {
        var root = PrefabUtility.LoadPrefabContents(Hud);
        try
        {
            if (root.transform.Find("MobileInformationRoot") != null) return;
            var ui = root.GetComponent<ArcadeHudUI>(); var collector = root.GetComponent<ReferenceCollector>();
            var info = Rect(root.transform, "MobileInformationRoot", 24, -24, 368, 280);
            var column = info.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing = 10; column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = column.childForceExpandHeight = false;
            column.childAlignment = TextAnchor.UpperLeft;
            info.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var players = Rect(info, "MobilePlayerStatusList", 0, 0, 368, 172);
            var rows = players.gameObject.AddComponent<VerticalLayoutGroup>();
            rows.spacing = 8; rows.childControlWidth = rows.childControlHeight = true;
            rows.childForceExpandWidth = rows.childForceExpandHeight = false;
            var playerList = players.gameObject.AddComponent<CustomListView>();
            playerList.itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Box);
            collector.Add("MobileInformationRoot", info.gameObject);
            collector.Add("MobilePlayerStatusList", playerList);
            var effects = Rect(info, "MobileEffectsRoot", 0, 0, 368, 76);
            var flow = effects.gameObject.AddComponent<HorizontalLayoutGroup>();
            flow.spacing = 8; flow.childControlWidth = flow.childControlHeight = true;
            flow.childForceExpandWidth = flow.childForceExpandHeight = false;
            flow.childAlignment = TextAnchor.UpperLeft;
            MakeEffectList(effects, "MobileWeaponEffectList", ui.WeaponEffectList.itemPrefab, 1, collector);
            MakeEffectList(effects, "MobileEffectList", ui.EffectList.itemPrefab, 4, collector);
            collector.Add("MobileEffectsRoot", effects.gameObject);
            info.gameObject.SetActive(false);

            var button = ui.BtnFieldUpgrade;
            var original = button.transform.Cast<Transform>().ToArray();
            var desktop = Rect(button.transform, "DesktopFieldUpgradeFace", 0, 0, 280, 96);
            desktop.anchorMin = desktop.anchorMax = desktop.pivot = new Vector2(.5f, .5f); desktop.anchoredPosition = Vector2.zero;
            foreach (var child in original) child.SetParent(desktop, false);
            var face = Rect(button.transform, "MobileFieldUpgradeFace", 0, 0, 160, 100);
            face.anchorMin = face.anchorMax = face.pivot = new Vector2(.5f, .5f); face.anchoredPosition = Vector2.zero;
            var image = face.gameObject.AddComponent<Image>(); image.sprite = ui.BtnBomb.GetComponent<Image>().sprite;
            image.type = ui.BtnBomb.GetComponent<Image>().type; image.color = new Color(.08f, .18f, .26f, .95f);
            var icon = Icon(face, "StormIcon", "Assets/Game/Art/UI/UtilityButtons/StormVortex.png", 28, -18, 60);
            icon.color = new Color(.2f, .8f, 1);
            var charge = Text(face, ui.TextWave, "MobileStormCharge", 90, -33, 65, 36, 22, Color.white);
            var track = Image(face, "StormChargeTrack", 14, -87, 132, 6, new Color(.12f, .23f, .3f));
            var fill = Fill(track.transform, "MobileStormFill", false); fill.color = new Color(.2f, .8f, 1);
            collector.Add("MobileFieldUpgradeFace", face); collector.Add("DesktopFieldUpgradeFace", desktop);
            collector.Add("MobileStormCharge", charge); collector.Add("MobileStormFill", fill);
            face.gameObject.SetActive(false);
            collector.Sort(); PrefabUtility.SaveAsPrefabAsset(root, Hud);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void MakeEffectList(Transform parent, string name, GameObject box, int columns, ReferenceCollector collector)
    {
        var rect = Rect(parent, name, 0, 0, columns * 68 + (columns - 1) * 6, 76);
        var grid = rect.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(68, 76); grid.spacing = new Vector2(6, 8);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = columns;
        var list = rect.gameObject.AddComponent<CustomListView>(); list.itemPrefab = box; collector.Add(name, list);
        var element = rect.gameObject.AddComponent<LayoutElement>(); element.preferredWidth = rect.sizeDelta.x;
    }

    static void BuildLayoutPreview()
    {
        var root = PrefabUtility.LoadPrefabContents(Layout);
        try
        {
            var preview = root.GetComponent<MobileControlLayoutUI>();
            var target = preview.FieldUpgradePreview.GetComponent<RectTransform>();
            target.sizeDelta = new Vector2(160, 100);
            PrefabUtility.SaveAsPrefabAsset(root, Layout);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height); return rect;
    }
    static Image Image(Transform parent, string name, float x, float y, float width, float height, Color color)
    {
        var image = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Image>();
        image.color = color; image.raycastTarget = false; return image;
    }
    static Image Icon(Transform parent, string name, string asset, float x, float y, float size)
    {
        var image = Image(parent, name, x, y, size, size, Color.white);
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(asset);
        if (image.sprite == null) throw new InvalidOperationException("HUD 图标缺失：" + asset);
        image.preserveAspect = true; return image;
    }
    static Image Fill(Transform parent, string name, bool vertical)
    {
        var image = Image(parent, name, 0, 0, 0, 0, new Color(.35f, .95f, .22f));
        image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one;
        image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = UnityEngine.UI.Image.Type.Filled;
        image.fillMethod = vertical ? UnityEngine.UI.Image.FillMethod.Vertical : UnityEngine.UI.Image.FillMethod.Horizontal;
        image.fillOrigin = 0; image.fillAmount = 0; return image;
    }
    static TextMeshProUGUI Text(Transform parent, TextMeshProUGUI template, string name, float x, float y,
        float width, float height, float size, Color color)
    {
        var text = Rect(parent, name, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = template.font; text.fontSharedMaterial = template.fontSharedMaterial;
        text.fontSize = text.fontSizeMax = size; text.fontSizeMin = size - 3; text.enableAutoSizing = true;
        text.fontStyle = FontStyles.Bold; text.alignment = TextAlignmentOptions.Left;
        text.enableWordWrapping = false; text.raycastTarget = false; text.color = color; text.text = string.Empty; return text;
    }
}
