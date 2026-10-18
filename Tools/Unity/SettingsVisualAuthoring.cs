using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Game;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>将已确认的深青与琥珀黄设置方案写入正式Prefab，保留原业务控件与外部列表项来源。</summary>
public static class SettingsVisualAuthoring
{
    private const string Home = "Assets/Game/Prefabs/UI/Home/";
    private const string Art = "Assets/Game/Art/UI/Settings/";
    private static readonly Color Shell = C("12343C"), Row = C("1D4851"), Inset = C("103038"), Ink = C("F3EBD8"), Muted = C("9EBAB4"), Amber = C("F4B84B"), Edge = C("37616A");
    private static Sprite round, circle;
    private static TMP_FontAsset font;
    private static Newtonsoft.Json.Linq.JObject words;

    /// <summary>先制作外部Box和Page，再制作宿主；新View字段随后交给正式生成器。</summary>
    public static string Run()
    {
        if (Application.dataPath != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying)
            throw new InvalidOperationException("仅允许本工程编辑模式制作");
        words = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
        font = AssetDatabase.LoadAssetAtPath<GameObject>(Home + "Setting.prefab").GetComponentInChildren<TMP_Text>(true).font;
        round = MakeSprite("Rounded", 12); circle = MakeSprite("Circle", 32);
        Edit("Box/SettingItemBox", SettingRow);
        Edit("Box/HotKeyItemBox", HotKeyRow);
        Edit("Page/SettingGeneral", General);
        Edit("Page/SettingAudio", Audio);
        Edit("Page/SettingControls", Controls);
        Edit("Page/SettingNetwork", Network);
        Edit("Setting", Host);
        return "设置宿主、四页和两种Box已制作；等待正式生成器生成并绑定";
    }

    private static void Edit(string name, Action<GameObject> action)
    {
        var path = Home + name + ".prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try { action(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>调用项目既有生成器，静态文案不加入ReferenceCollector。</summary>
    public static string Generate()
    {
        foreach (var name in new[] { "Box/SettingItemBox", "Box/HotKeyItemBox", "Page/SettingGeneral", "Page/SettingAudio", "Page/SettingControls", "Page/SettingNetwork", "Setting" })
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Home + name + ".prefab");
            if (name.StartsWith("Box/")) BoxUICodeSpawner.AutoUpdateBoxUI(asset);
            else if (name.StartsWith("Page/")) PageCodeSpawner.AutoUpdatePage(asset);
            else UICodeSpawner.AutoUpdateUI(asset);
        }
        return "七个正式Prefab已执行项目生成器";
    }

    /// <summary>程序集重载后仍逐个调用原生成器的绑定步骤，防止多资源生成只保留最后一个延迟任务。</summary>
    public static string Bind()
    {
        foreach (var name in new[] { "Box/SettingItemBox", "Box/HotKeyItemBox", "Page/SettingGeneral", "Page/SettingAudio", "Page/SettingControls", "Page/SettingNetwork", "Setting" })
        {
            var type = name.StartsWith("Box/") ? typeof(BoxUICodeSpawner) : name.StartsWith("Page/") ? typeof(PageCodeSpawner) : typeof(UICodeSpawner);
            var method = name.Contains("/") ? "BindGeneratedComponent" : "BindGeneratedUiComponent";
            var suffix = name.StartsWith("Page/") ? "PageUI" : "UI";
            type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { Home + name + ".prefab", "Game." + name.Split('/').Last() + suffix });
        }
        return "七个正式View已绑定";
    }

    private static void Host(GameObject root)
    {
        var ui = root.GetComponent<SettingUI>(); var panel = root.transform.Find("Panel");
        Place(panel, 0, 0, 1260, 850); Paint(panel.GetComponent<Image>(), Shell);
        var shadow = panel.GetComponent<Shadow>() ?? panel.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0, 0, 0, .25f); shadow.effectDistance = new Vector2(0, -8);
        var title = panel.Find("TextSetting"); Place(title, -110, 358, 940, 64); Text(title.GetComponent<TMP_Text>(), 42, Ink, TextAlignmentOptions.MidlineLeft); Localize(title.GetComponent<TMP_Text>(), "ui.start.settings");
        Place(ui.BtnExit.transform, 574, 358, 64, 64); ButtonStyle(ui.BtnExit, Color.clear); ui.BtnExit.image.sprite = null;
        var x = FindOrCreate(ui.BtnExit.transform, "CloseGlyph", typeof(Image)); Place(x, 0, 0, 28, 28); x.GetComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Art/UI/Icon_Close.png"); x.GetComponent<Image>().color = Ink; x.GetComponent<Image>().raycastTarget = false;
        Line(panel, "HeaderLine", 0, 310, 1196);
        var sidebar = panel.Find("Sidebar"); Place(sidebar, -495, -45, 220, 650); Paint(sidebar.GetComponent<Image>(), Inset);
        var layout = sidebar.GetComponent<VerticalLayoutGroup>(); if (layout != null) Object.DestroyImmediate(layout);
        var buttons = new[] { ui.GeneralTabButton, ui.AudioTabButton, ui.ControlsTabButton, ui.NetworkTabButton };
        for (int i = 0; i < buttons.Length; i++)
        {
            var b = buttons[i]; Place(b.transform, 0, 257 - i * 96, 196, 72); ButtonStyle(b, Color.clear);
            var colors = b.colors; colors.normalColor = Color.clear; colors.selectedColor = Amber; b.colors = colors; b.transition = Selectable.Transition.None; b.interactable = true;
            Text(b.GetComponentInChildren<TMP_Text>(true), 28, Ink, TextAlignmentOptions.Center);
        }
        Place(panel.Find("Content"), 125, -45, 940, 690);
    }

    private static void SettingRow(GameObject root)
    {
        Place(root.transform, 0, 0, 880, 92); Paint(root.GetComponent<Image>(), Row);
        var le = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>(); le.preferredHeight = 92; le.minHeight = 92;
        var ui = root.GetComponent<SettingItemBoxUI>();
        Place(ui.TextSetting.transform, -280, 0, 260, 72); Text(ui.TextSetting, 27, Ink, TextAlignmentOptions.MidlineLeft);
        Place(ui.BtnContext.transform, 190, 0, 440, 64); ButtonStyle(ui.BtnContext, Inset);
        foreach (var t in ui.BtnContext.GetComponentsInChildren<TMP_Text>(true)) if (t != ui.TextContext) t.gameObject.SetActive(false);
        ui.TextContext.transform.SetParent(ui.BtnContext.transform, false); Stretch(ui.TextContext.rectTransform, 22, 6, 56, 6); Text(ui.TextContext, 26, Ink, TextAlignmentOptions.MidlineRight);
        Chevron(ui.BtnContext.transform, "Chevron", 188, 0);
    }

    private static void HotKeyRow(GameObject root)
    {
        Place(root.transform, 0, 0, 864, 82); Paint(root.GetComponent<Image>() ?? root.AddComponent<Image>(), Row);
        var le = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>(); le.minHeight = le.preferredHeight = 82;
        var ui = root.GetComponent<HotKeyItemBoxUI>();
        Place(ui.ActionNameText.transform, -236, 0, 340, 64); Text(ui.ActionNameText, 25, Ink, TextAlignmentOptions.MidlineLeft);
        Place(ui.BindingText.transform, 90, 0, 220, 60); Text(ui.BindingText, 25, Amber, TextAlignmentOptions.Center);
        Place(ui.RebindButton.transform, 315, 0, 178, 56); ButtonStyle(ui.RebindButton, Inset);
    }

    private static void General(GameObject root)
    {
        PageHeader(root, "general_title", "general_subtitle"); var ui = root.GetComponent<SettingGeneralPageUI>();
        var rect = (RectTransform)ui.SettingList.transform; rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1); rect.anchoredPosition = new Vector2(0, -160); rect.sizeDelta = new Vector2(-60, 360);
        var layout = ui.SettingList.GetComponent<VerticalLayoutGroup>(); layout.spacing = 18; layout.padding = new RectOffset(); layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        Footer(root, ui.ResetButton);
    }

    private static void Audio(GameObject root)
    {
        var oldTitle = root.transform.Find("AudioTitle"); if (oldTitle != null) oldTitle.gameObject.SetActive(false);
        PageHeader(root, "audio_title", "audio_subtitle"); var ui = root.GetComponent<SettingAudioPageUI>(); var rc = root.GetComponent<ReferenceCollector>();
        var sliders = new[] { ui.MasterVolumeSlider, ui.MusicVolumeSlider, ui.SoundVolumeSlider }; var names = new[] { "MasterVolumeValue", "MusicVolumeValue", "SoundVolumeValue" };
        for (int i = 0; i < sliders.Length; i++)
        {
            var slider = sliders[i]; var row = slider.transform.parent;
            var oldLayout = row.GetComponent<HorizontalLayoutGroup>(); if (oldLayout != null) Object.DestroyImmediate(oldLayout);
            Place(row, 0, 156 - i * 96, 880, 80); Paint(row.GetComponent<Image>() ?? row.gameObject.AddComponent<Image>(), Row);
            var label = row.Find("Label").GetComponent<TMP_Text>(); Place(label.transform, -278, 0, 276, 64); Text(label, 26, Ink, TextAlignmentOptions.MidlineLeft);
            if (i == 2) Localize(label, "ui.settings.visual.sound_volume");
            Place(slider.transform, 80, 0, 390, 64);
            var background = slider.transform.Find("Background"); Place(background, 0, 0, 390, 12); Paint(background.GetComponent<Image>(), Inset);
            var fillArea = slider.fillRect.parent; Stretch((RectTransform)fillArea, 0, 26, 0, 26); Stretch(slider.fillRect, 0, 0, 0, 0); Paint(slider.fillRect.GetComponent<Image>(), Amber);
            var handleArea = (RectTransform)slider.handleRect.parent; handleArea.anchorMin = new Vector2(0, .5f); handleArea.anchorMax = new Vector2(1, .5f); handleArea.sizeDelta = Vector2.zero; handleArea.anchoredPosition = Vector2.zero; slider.handleRect.sizeDelta = new Vector2(28, 28); slider.handleRect.anchoredPosition = Vector2.zero;
            var handle = slider.handleRect.GetComponent<Image>(); handle.sprite = circle; handle.type = Image.Type.Simple; handle.color = Ink;
            slider.transition = Selectable.Transition.None;
            var value = Label(row, "VolumeValue", "", 28, Ink); Place(value.transform, 355, 0, 110, 64); value.alignment = TextAlignmentOptions.MidlineRight; value.text = "100%"; rc.Add(names[i], value);
        }
        var mute = ui.MuteToggle.transform.parent; var oldMuteLayout = mute.GetComponent<HorizontalLayoutGroup>(); if (oldMuteLayout != null) Object.DestroyImmediate(oldMuteLayout);
        Place(mute, 0, -132, 880, 80); Paint(mute.GetComponent<Image>() ?? mute.gameObject.AddComponent<Image>(), Row);
        var muteLabel = mute.Find("Label").GetComponent<TMP_Text>(); Place(muteLabel.transform, -278, 0, 276, 64); Text(muteLabel, 26, Ink, TextAlignmentOptions.MidlineLeft);
        var toggle = ui.MuteToggle; Place(toggle.transform, 354, 0, 100, 58); toggle.transition = Selectable.Transition.None;
        foreach (var image in toggle.GetComponentsInChildren<Image>(true)) image.enabled = false;
        var track = FindOrCreate(toggle.transform, "SwitchTrack", typeof(Image)); Place(track, 0, 0, 90, 44); Paint(track.GetComponent<Image>(), Edge); track.GetComponent<Image>().enabled = true; track.GetComponent<Image>().raycastTarget = true;
        var thumb = FindOrCreate(track, "Thumb", typeof(Image)); Place(thumb, -23, 0, 34, 34); thumb.GetComponent<Image>().enabled = true; thumb.GetComponent<Image>().sprite = circle; thumb.GetComponent<Image>().color = Ink; thumb.GetComponent<Image>().raycastTarget = false;
        toggle.graphic = null; toggle.targetGraphic = track.GetComponent<Image>(); rc.Add("MuteThumb", (RectTransform)thumb); rc.Add("MuteTrack", track.GetComponent<Image>());
        Place(ui.TestSoundButton.transform, -337, -210, 206, 56); ButtonStyle(ui.TestSoundButton, Inset); Footer(root, ui.ResetButton); rc.Sort();
    }

    private static void Controls(GameObject root)
    {
        var old = root.transform.Find("Title"); if (old != null) old.gameObject.SetActive(false);
        PageHeader(root, "controls_title", "controls_subtitle"); var ui = root.GetComponent<SettingControlsPageUI>();
        var scroll = root.GetComponentInChildren<ScrollRect>(true); Stretch(scroll.viewport, 30, 98, 30, 160);
        var image = scroll.viewport.GetComponent<Image>(); if (image != null) { Paint(image, Color.clear); var mask = image.GetComponent<Mask>(); if (mask != null) mask.showMaskGraphic = false; }
        var list = ui.HotKeyList.GetComponent<VerticalLayoutGroup>(); list.spacing = 12; list.childControlWidth = true; list.childControlHeight = true; list.childForceExpandHeight = false;
        Footer(root, ui.ResetButton);
    }

    private static void Network(GameObject root)
    {
        PageHeader(root, "network_title", null); var ui = root.GetComponent<SettingNetworkPageUI>();
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) Text(text, 23, Ink, TextAlignmentOptions.MidlineLeft);
        foreach (var b in root.GetComponentsInChildren<Button>(true)) ButtonStyle(b, Inset);
        Text(root.transform.Find("VisualTitle").GetComponent<TMP_Text>(), 34, Ink, TextAlignmentOptions.MidlineLeft);
        var clearLabel = ui.DiagnosticClearButton.GetComponentInChildren<TMP_Text>(true); var oldClearLocalization = clearLabel.GetComponent<LocalizedText>(); if (oldClearLocalization != null) Object.DestroyImmediate(oldClearLocalization); clearLabel.text = (string)words["ui.setting.diagnostic.clear"];
        NetworkGroup(root, "GameServer", ui.GameServerInput, ui.GameServerApplyButton, ui.GameServerResetButton, ui.GameServerFeedback, 220);
        NetworkGroup(root, "ResourceServer", ui.ResourceServerInput, ui.ResourceServerApplyButton, ui.ResourceServerResetButton, ui.ResourceServerFeedback, 60);
        Line(root.transform, "NetworkDivider", 0, -84, 880);
        Place(ui.DiagnosticNotice.transform, 0, -123, 880, 66); Text(ui.DiagnosticNotice, 20, Muted, TextAlignmentOptions.TopLeft); ui.DiagnosticNotice.enableWordWrapping = true;
        Place(ui.DiagnosticPending.transform, -290, -192, 300, 52); Text(ui.DiagnosticPending, 22, Ink, TextAlignmentOptions.MidlineLeft);
        Place(ui.DiagnosticUploadButton.transform, 75, -192, 200, 52); Place(ui.DiagnosticClearButton.transform, 316, -192, 248, 52);
        ui.DiagnosticClearButton.image.color = C("503D3D");
        var toggleRoot = FindOrCreate(root.transform, "DiagnosticDetailsToggle", typeof(Image), typeof(Toggle)); Place(toggleRoot, -300, -252, 280, 46); Paint(toggleRoot.GetComponent<Image>(), Inset);
        var toggle = toggleRoot.GetComponent<Toggle>(); toggle.targetGraphic = toggleRoot.GetComponent<Image>(); toggle.transition = Selectable.Transition.ColorTint;
        var caption = Label(toggleRoot, "Label", "ui.settings.visual.details", 22, Ink); Stretch(caption.rectTransform, 16, 0, 45, 0);
        var mark = FindOrCreate(toggleRoot, "ExpandedMark", typeof(Image)); Place(mark, 111, 0, 10, 10); mark.GetComponent<Image>().color = Amber; mark.GetComponent<Image>().raycastTarget = false; toggle.graphic = mark.GetComponent<Image>();
        Place(ui.DiagnosticStatus.transform, 0, -315, 880, 84); Text(ui.DiagnosticStatus, 18, Muted, TextAlignmentOptions.TopLeft); ui.DiagnosticStatus.enableWordWrapping = true;
        toggle.onValueChanged = new Toggle.ToggleEvent(); UnityEventTools.AddPersistentListener(toggle.onValueChanged, ui.DiagnosticStatus.gameObject.SetActive); toggle.isOn = false; ui.DiagnosticStatus.gameObject.SetActive(false);
    }

    private static void NetworkGroup(GameObject root, string prefix, TMP_InputField input, Button apply, Button reset, TMP_Text feedback, float y)
    {
        var label = root.transform.Find(prefix + "Label").GetComponent<TMP_Text>(); Place(label.transform, 0, y, 880, 36); Text(label, 23, Ink, TextAlignmentOptions.MidlineLeft);
        Place(input.transform, -162, y - 50, 556, 56); Paint(input.GetComponent<Image>(), Inset); input.textComponent.color = Ink; input.textComponent.fontSize = 22; var state = input.colors; state.normalColor = Color.white; state.selectedColor = C("D9F1EA"); input.colors = state;
        Place(apply.transform, 200, y - 50, 142, 56); Place(reset.transform, 365, y - 50, 150, 56);
        Place(feedback.transform, 0, y - 105, 880, 42); Text(feedback, 19, Muted, TextAlignmentOptions.MidlineLeft);
    }

    private static void PageHeader(GameObject root, string title, string subtitle)
    {
        var t = Label(root.transform, "VisualTitle", "ui.settings.visual." + title, 34, Ink); Place(t.transform, 0, 287, 880, 60); t.fontStyle = FontStyles.Bold;
        if (subtitle != null) { var s = Label(root.transform, "VisualSubtitle", "ui.settings.visual." + subtitle, 23, Muted); Place(s.transform, 0, 243, 880, 34); Line(root.transform, "TitleDivider", 0, 214, 880); }
    }

    private static void Footer(GameObject root, Button reset)
    {
        Line(root.transform, "FooterDivider", 0, -270, 880); var status = Label(root.transform, "AutosaveHint", "ui.settings.visual.autosave", 21, Muted); Place(status.transform, -170, -313, 540, 56);
        Place(reset.transform, 318, -313, 244, 56); ButtonStyle(reset, Inset); Localize(reset.GetComponentInChildren<TMP_Text>(true), "ui.settings.visual.reset");
    }

    private static void ButtonStyle(Button b, Color color)
    {
        Paint(b.image, color); var state = b.colors; state.normalColor = Color.white; state.highlightedColor = C("D9F1EA"); state.pressedColor = C("A7C9C3"); state.selectedColor = Color.white; state.disabledColor = new Color(.6f, .7f, .68f, .6f); b.colors = state;
        foreach (var text in b.GetComponentsInChildren<TMP_Text>(true)) { Text(text, 23, Ink, TextAlignmentOptions.Center); Stretch(text.rectTransform, 10, 4, 10, 4); }
    }
    private static TMP_Text Label(Transform parent, string name, string key, float size, Color color)
    {
        var go = FindOrCreate(parent, name, typeof(TextMeshProUGUI)); var t = go.GetComponent<TextMeshProUGUI>(); t.font = font; Text(t, size, color, TextAlignmentOptions.MidlineLeft); if (!string.IsNullOrEmpty(key)) Localize(t, key); return t;
    }
    private static void Localize(TMP_Text text, string key)
    {
        var loc = text.GetComponent<LocalizedText>() ?? text.gameObject.AddComponent<LocalizedText>(); loc.Key = key; loc.Texts.Clear(); text.text = (string)words[key] ?? throw new Exception("缺少文案：" + key);
    }
    private static void Text(TMP_Text t, float size, Color color, TextAlignmentOptions alignment)
    {
        t.fontSize = size; t.enableAutoSizing = false; t.color = color; t.alignment = alignment; t.raycastTarget = false; t.enableWordWrapping = false;
    }
    private static Transform FindOrCreate(Transform parent, string name, params Type[] types)
    {
        var t = parent.Find(name); if (t != null) return t; var go = new GameObject(name, new[] { typeof(RectTransform) }.Concat(types).ToArray()); go.transform.SetParent(parent, false); return go.transform;
    }
    private static void Paint(Image image, Color color) { image.sprite = round; image.type = Image.Type.Sliced; image.color = color; }
    private static void Place(Transform t, float x, float y, float w, float h) { var r = (RectTransform)t; r.anchorMin = r.anchorMax = r.pivot = Vector2.one * .5f; r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h); r.localScale = Vector3.one; }
    private static void Stretch(RectTransform r, float left, float bottom, float right, float top) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top); }
    private static void Line(Transform parent, string name, float x, float y, float width) { var t = FindOrCreate(parent, name, typeof(Image)); Place(t, x, y, width, 1); t.GetComponent<Image>().color = Edge; t.GetComponent<Image>().raycastTarget = false; }
    private static void Chevron(Transform parent, string name, float x, float y)
    {
        var t = FindOrCreate(parent, name); Place(t, x, y, 24, 24);
        for(int i=0;i<2;i++){var arm=FindOrCreate(t,"Arm"+i,typeof(Image));Place(arm,i==0?-4:4,0,12,2);arm.localEulerAngles=new Vector3(0,0,i==0?-45:45);arm.GetComponent<Image>().color=Ink;arm.GetComponent<Image>().raycastTarget=false;}
    }
    private static Color C(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }
    /// <summary>生成可九宫格缩放的简单几何UI源图，不依赖AI效果图中的像素或新增外部素材。</summary>
    private static Sprite MakeSprite(string name, float radius)
    {
        Directory.CreateDirectory(Art); var path = Art + name + ".png";
        if (!File.Exists(path))
        {
            var texture = new Texture2D(64,64,TextureFormat.RGBA32,false);
            for(int y=0;y<64;y++)for(int x=0;x<64;x++){var q=new Vector2(Mathf.Abs(x+.5f-32)-(32-radius),Mathf.Abs(y+.5f-32)-(32-radius));var d=new Vector2(Mathf.Max(q.x,0),Mathf.Max(q.y,0)).magnitude+Mathf.Min(Mathf.Max(q.x,q.y),0)-radius;texture.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(.5f-d)));}
            texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());Object.DestroyImmediate(texture);
        }
        AssetDatabase.ImportAsset(path); var importer=(TextureImporter)AssetImporter.GetAtPath(path); importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single; importer.alphaIsTransparency=true; importer.mipmapEnabled=false; importer.textureCompression=TextureImporterCompression.Uncompressed; importer.spriteBorder=name=="Circle"?Vector4.zero:new Vector4(16,16,16,16); importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
