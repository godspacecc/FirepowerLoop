using System;
using System.Reflection;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>制作 PC/触屏双布局、紧凑效果列表与同步避让预览；静态布局全部保存在正式 Prefab。</summary>
public static class ArcadeCompactHudAuthoring
{
    const string Hud = "Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab";
    const string Player = "Assets/Game/Prefabs/UI/Arcade/Box/ArcadePlayerStatusBox.prefab";
    const string Effect = "Assets/Game/Prefabs/UI/Arcade/Box/ArcadeEffectBox.prefab";
    const string Layout = "Assets/Game/Prefabs/UI/Home/MobileControlLayout.prefab";
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("仅在编辑模式制作 HUD");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Hud).transform.Find("WeaponEffectRoot") != null)
            return "紧凑 HUD 已制作；重复执行不会创建第二套控件";
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Hud).GetComponent<ArcadeHudUI>().TextWave;
        var box = new GameObject("ArcadeEffectBox", typeof(RectTransform), typeof(ReferenceCollector), typeof(Image));
        try
        {
            Place((RectTransform)box.transform, new Vector2(0, 1), Vector2.zero, new Vector2(68, 76));
            box.GetComponent<Image>().color = new Color(.035f, .075f, .1f, .9f);
            box.GetComponent<Image>().raycastTarget = false;
            var icon = Image(box.transform, "Icon", new Vector2(0, -2), new Vector2(50, 50));
            icon.preserveAspect = true;
            var fallback = Text(box.transform, source, "TextFallback", "", new Vector2(0, -3), new Vector2(60, 45), 26);
            var charges = Text(box.transform, source, "TextCharges", "", new Vector2(18, -38), new Vector2(30, 24), 18);
            var track = Image(box.transform, "Track", new Vector2(0, -62), new Vector2(56, 7));
            track.color = new Color(.12f, .16f, .2f);
            var fill = Image(track.transform, "ProgressFill", Vector2.zero, new Vector2(56, 7));
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            fill.color = new Color(.3f, .85f, .92f);
            var collector = box.GetComponent<ReferenceCollector>();
            collector.Add("Icon", icon); collector.Add("TextFallback", fallback); collector.Add("TextCharges", charges); collector.Add("ProgressFill", fill);
            collector.Sort(); PrefabUtility.SaveAsPrefabAsset(box, Effect);
        }
        finally { Object.DestroyImmediate(box); }
        var player = PrefabUtility.LoadPrefabContents(Player);
        try
        {
            var view = player.GetComponent<ArcadePlayerStatusBoxUI>();
            Place((RectTransform)player.transform, new Vector2(0, 1), Vector2.zero, new Vector2(500, 124));
            var element = player.GetComponent<LayoutElement>(); element.preferredHeight = 124;
            At(view.TextName, 12, -17, 88, 28, 23);
            Place((RectTransform)player.transform.Find("HealthBackground"), new Vector2(0, 1), new Vector2(108, -17), new Vector2(180, 18), new Vector2(0, .5f));
            At(view.TextLives, 310, -17, 180, 28, 23);
            At(view.TextGold, 12, -48, 218, 28, 23);
            var score = Text(player.transform, view.TextGold, "TextScore", "积分 0", new Vector2(0, -48), new Vector2(250, 28), 23);
            At(score, 238, -48, 250, 28, 23);
            At(view.TextMultiplier, 12, -79, 132, 28, 23);
            Place((RectTransform)player.transform.Find("MultiplierBackground"), new Vector2(0, 1), new Vector2(150, -79), new Vector2(334, 20), new Vector2(0, .5f));
            At(view.TextState, 12, -108, 475, 25, 21);
            player.GetComponent<ReferenceCollector>().Add("TextScore", score);
            PrefabUtility.SaveAsPrefabAsset(player, Player);
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        var root = PrefabUtility.LoadPrefabContents(Hud);
        try
        {
            var ui = root.GetComponent<ArcadeHudUI>(); var collector = root.GetComponent<ReferenceCollector>();
            var area = ui.JoystickLayoutArea;
            foreach (var button in new[] { ui.BtnPause, ui.BtnBomb, ui.BtnDash }) button.transform.SetParent(area, false);
            Place((RectTransform)ui.BtnPause.transform, Vector2.one, new Vector2(-102, -64), new Vector2(160, 88));
            Place((RectTransform)ui.BtnBomb.transform, Vector2.one, new Vector2(-102, -170), new Vector2(160, 100));
            Place((RectTransform)ui.BtnDash.transform, Vector2.one, new Vector2(-102, -286), new Vector2(160, 100));
            Ability(ui.BtnBomb.transform, source, "炸", "IconAmmoGrenade");
            Ability(ui.BtnDash.transform, source, "冲", null);
            var desktop = new GameObject("DesktopAbilityRoot", typeof(RectTransform)); desktop.transform.SetParent(area, false);
            Place((RectTransform)desktop.transform, Vector2.one, new Vector2(-24, -128), new Vector2(164, 64), Vector2.one);
            var bomb = Image(desktop.transform, "BombIcon", new Vector2(-46, -4), new Vector2(38, 38));
            bomb.sprite = Sprite("IconAmmoGrenade");
            var dash = Text(desktop.transform, source, "DashIcon", "冲", new Vector2(30, -5), new Vector2(36, 36), 25);
            var bombCount = Text(desktop.transform, source, "TextBombCount", "0", new Vector2(-20, -30), new Vector2(40, 30), 24);
            var dashCount = Text(desktop.transform, source, "TextDashCount", "0", new Vector2(55, -30), new Vector2(40, 30), 24);
            collector.Add("DesktopAbilityRoot", desktop); collector.Add("TextBombCount", bombCount); collector.Add("TextDashCount", dashCount);
            collector.Add("DesktopBombIcon", bomb); collector.Add("DesktopDashIcon", dash);
            Place((RectTransform)ui.PlayerStatusList.transform, new Vector2(0, 1), new Vector2(24, -24), new Vector2(500, 520), new Vector2(0, 1));
            var old = root.transform.Find("TemporaryStatus"); if (old != null) Object.DestroyImmediate(old.gameObject);
            collector.data.RemoveAll(entry => entry.key == "TemporaryStatusRoot" || entry.key == "TextTemporary" || entry.key == "TextSupport");
            var effects = new GameObject("EffectList", typeof(RectTransform), typeof(GridLayoutGroup), typeof(CustomListView));
            effects.transform.SetParent(root.transform, false);
            Place((RectTransform)effects.transform, new Vector2(0, 1), new Vector2(540, -24), new Vector2(360, 240), new Vector2(0, 1));
            var grid = effects.GetComponent<GridLayoutGroup>(); grid.cellSize = new Vector2(68, 76); grid.spacing = new Vector2(6, 8);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 5;
            var list = effects.GetComponent<CustomListView>(); list.itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Effect);
            collector.Add("EffectList", list);
            foreach (var pair in new[] { ("RapidIcon", "IconWeaponRifle"), ("SpreadIcon", "IconWeaponShotgun"), ("PiercingIcon", "IconWeaponPiercing"), ("RocketIcon", "IconWeaponRocket"), ("GrenadeIcon", "IconWeaponGrenade"), ("RaygunIcon", "IconWeaponPlasma"), ("BootsIcon", "IconClothing_Feet_BrownBoots"), ("GlovesIcon", "IconClothing_Hands_WorkGloves") }) collector.Add(pair.Item1, Sprite(pair.Item2));
            var wave = (RectTransform)ui.TextWave.transform.parent;
            Place(wave, new Vector2(.5f, 0), new Vector2(0, 32), new Vector2(620, 82), new Vector2(.5f, 0));
            Place(ui.TextWave.rectTransform, new Vector2(.5f, 0), new Vector2(0, 2), new Vector2(600, 42), new Vector2(.5f, 0));
            ui.TextWave.fontSizeMax = 28; ui.TextWave.fontSizeMin = 20;
            Place(ui.TextArena.rectTransform, new Vector2(.5f, 1), new Vector2(0, -1), new Vector2(600, 32), new Vector2(.5f, 1));
            ui.TextArena.alignment = ui.TextWave.alignment = TextAlignmentOptions.Center;
            ui.TextArena.fontSizeMax = 23; ui.TextArena.fontSizeMin = 18;
            Place((RectTransform)ui.StageBannerRoot.transform, new Vector2(.5f, .68f), Vector2.zero, new Vector2(680, 92));
            Place((RectTransform)ui.BtnExitFirstPerson.transform, Vector2.one, new Vector2(-160, -390), new Vector2(270, 64));
            collector.Sort(); PrefabUtility.SaveAsPrefabAsset(root, Hud);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var layout = PrefabUtility.LoadPrefabContents(Layout);
        try
        {
            var ui = layout.GetComponent<MobileControlLayoutUI>();
            Place(ui.BombArea, Vector2.one, new Vector2(-102, -170), new Vector2(160, 100));
            Place(ui.DashArea, Vector2.one, new Vector2(-102, -286), new Vector2(160, 100));
            var pause = Image(ui.LayoutArea, "ArcadePauseArea", new Vector2(-102, -64), new Vector2(160, 88));
            Place(pause.rectTransform, Vector2.one, new Vector2(-102, -64), new Vector2(160, 88));
            Text(pause.transform, source, "Label", "暂停", Vector2.zero, new Vector2(150, 80), 28);
            layout.GetComponent<ReferenceCollector>().Add("ArcadePauseArea", pause.rectTransform);
            PrefabUtility.SaveAsPrefabAsset(layout, Layout);
        }
        finally { PrefabUtility.UnloadPrefabContents(layout); }
        BoxUICodeSpawner.AutoUpdateBoxUI(AssetDatabase.LoadAssetAtPath<GameObject>(Effect));
        BoxUICodeSpawner.AutoUpdateBoxUI(AssetDatabase.LoadAssetAtPath<GameObject>(Player));
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(Hud));
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(Layout));
        return "双端 HUD 与外部效果 Box 已保存，已调用正式生成器";
    }
    public static string FinishBindings()
    {
        var box = typeof(BoxUICodeSpawner).GetMethod("BindGeneratedComponent", BindingFlags.Static | BindingFlags.NonPublic);
        box.Invoke(null, new object[] { Effect, "Game.ArcadeEffectBoxUI" }); box.Invoke(null, new object[] { Player, "Game.ArcadePlayerStatusBoxUI" });
        var ui = typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent", BindingFlags.Static | BindingFlags.NonPublic);
        ui.Invoke(null, new object[] { Hud, "Game.ArcadeHudUI" }); ui.Invoke(null, new object[] { Layout, "Game.MobileControlLayoutUI" });
        return "生成引用已回填";
    }
    /// <summary>武器与辅助使用同一种 Box 的两组正式列表；分类标题不是道具名称或倒计时。</summary>
    public static string SeparateGroups()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Hud).transform.Find("WeaponEffectRoot") != null)
            return "分类已经制作";
        var root = PrefabUtility.LoadPrefabContents(Hud);
        try
        {
            var collector = root.GetComponent<ReferenceCollector>();
            var source = root.GetComponent<ArcadeHudUI>().TextWave;
            var effects = root.transform.Find("EffectList");
            foreach (var weapon in new[] { true, false })
            {
                var group = new GameObject(weapon ? "WeaponEffectRoot" : "SupportEffectRoot", typeof(RectTransform));
                group.transform.SetParent(root.transform, false);
                Place((RectTransform)group.transform, new Vector2(0, 1), new Vector2(weapon ? 540 : 620, -24), new Vector2(weapon ? 68 : 360, 280), new Vector2(0, 1));
                var title = Text(group.transform, source, "Category", weapon ? "武器" : "辅助", new Vector2(0, 0), new Vector2(weapon ? 68 : 140, 24), 19);
                Place(title.rectTransform, new Vector2(0, 1), Vector2.zero, title.rectTransform.sizeDelta, new Vector2(0, 1));
                var localized = title.gameObject.AddComponent<LocalizedText>(); localized.Key = weapon ? "ui.arcade.hud.weapon_group" : "ui.arcade.hud.support_group";
                Transform listTransform;
                if (weapon)
                {
                    var list = new GameObject("WeaponEffectList", typeof(RectTransform), typeof(CustomListView), typeof(GridLayoutGroup));
                    listTransform = list.transform;
                    list.GetComponent<CustomListView>().itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Effect);
                    var grid = list.GetComponent<GridLayoutGroup>(); grid.cellSize = new Vector2(68, 76);
                    collector.Add("WeaponEffectList", list.GetComponent<CustomListView>());
                }
                else listTransform = effects;
                listTransform.SetParent(group.transform, false);
                Place((RectTransform)listTransform, new Vector2(0, 1), new Vector2(0, -28), new Vector2(weapon ? 68 : 360, 250), new Vector2(0, 1));
                collector.Add(weapon ? "WeaponEffectRoot" : "SupportEffectRoot", group);
            }
            var dash = root.GetComponent<ArcadeHudUI>().BtnDash.transform.Find("AbilityIcon");
            dash.GetComponent<TextMeshProUGUI>().text = "冲";
            dash.gameObject.AddComponent<LocalizedText>().Key = "ui.arcade.hud.icon.dash";
            collector.Sort(); PrefabUtility.SaveAsPrefabAsset(root, Hud);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var layout = PrefabUtility.LoadPrefabContents(Layout);
        try
        {
            var label = layout.transform.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var text in label)
                if (text.transform.parent.name == "ArcadePauseArea") text.gameObject.AddComponent<LocalizedText>().Key = "ui.arcade.pause";
            PrefabUtility.SaveAsPrefabAsset(layout, Layout);
        }
        finally { PrefabUtility.UnloadPrefabContents(layout); }
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(Hud));
        return "武器与辅助分类已分开，已调用生成器";
    }
    public static string Polish()
    {
        var box = PrefabUtility.LoadPrefabContents(Effect);
        try
        {
            box.GetComponent<Image>().color = new Color(.18f, .23f, .28f, .94f);
            box.transform.Find("Icon").GetComponent<RectTransform>().sizeDelta = new Vector2(62, 50);
            PrefabUtility.SaveAsPrefabAsset(box, Effect);
        }
        finally { PrefabUtility.UnloadPrefabContents(box); }
        var player = PrefabUtility.LoadPrefabContents(Player);
        try
        {
            At(player.GetComponent<ArcadePlayerStatusBoxUI>().TextName, 12, -17, 132, 28, 23);
            Place((RectTransform)player.transform.Find("HealthBackground"), new Vector2(0, 1), new Vector2(154, -17), new Vector2(134, 18), new Vector2(0, .5f));
            PrefabUtility.SaveAsPrefabAsset(player, Player);
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        var hud = PrefabUtility.LoadPrefabContents(Hud);
        try
        {
            var ui = hud.GetComponent<ArcadeHudUI>();
            Place(ui.TextFirstPerson.rectTransform, new Vector2(.5f, 0), new Vector2(0, 130), new Vector2(440, 64), new Vector2(.5f, 0));
            PrefabUtility.SaveAsPrefabAsset(hud, Hud);
        }
        finally { PrefabUtility.UnloadPrefabContents(hud); }
        return "图标对比度和玩家姓名宽度已调整";
    }
    static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Art/UI/Items/" + name + ".png");
    static void Ability(Transform button, TextMeshProUGUI source, string fallback, string sprite)
    {
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        Place(label.rectTransform, new Vector2(.5f, .5f), new Vector2(35, 0), new Vector2(65, 70));
        if (sprite != null) { var icon = Image(button, "AbilityIcon", new Vector2(-37, -25), new Vector2(48, 48)); icon.sprite = Sprite(sprite); }
        else Text(button, source, "AbilityIcon", fallback, new Vector2(-37, -25), new Vector2(48, 48), 32);
    }
    static Image Image(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(Image)); obj.transform.SetParent(parent, false);
        var image = obj.GetComponent<Image>(); image.raycastTarget = false;
        Place(image.rectTransform, new Vector2(.5f, 1), position, size, new Vector2(.5f, 1)); return image;
    }
    static TextMeshProUGUI Text(Transform parent, TextMeshProUGUI template, string name, string value, Vector2 position, Vector2 size, float fontSize)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); obj.transform.SetParent(parent, false);
        var text = obj.GetComponent<TextMeshProUGUI>(); text.font = template.font; text.fontSharedMaterial = template.fontSharedMaterial;
        text.text = value; text.fontSize = text.fontSizeMax = fontSize; text.fontSizeMin = fontSize - 4; text.enableAutoSizing = true;
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        Place(text.rectTransform, new Vector2(.5f, 1), position, size, new Vector2(.5f, 1)); return text;
    }
    static void At(TextMeshProUGUI text, float x, float y, float width, float height, float size)
    { Place(text.rectTransform, new Vector2(0, 1), new Vector2(x, y), new Vector2(width, height), new Vector2(0, .5f)); text.fontSizeMax = size; text.fontSizeMin = size - 4; text.alignment = TextAlignmentOptions.Left; }
    static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null)
    { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot ?? new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size; }
}

