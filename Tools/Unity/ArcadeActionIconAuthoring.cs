using System;
using System.Reflection;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>只将暂停和炸弹制作成紧凑图标；编辑器复制同一套静态视觉，不复制战斗输入组件。</summary>
public static class ArcadeActionIconAuthoring
{
    private const string HudPath = "Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab";
    private const string LayoutPath = "Assets/Game/Prefabs/UI/Home/MobileControlLayout.prefab";

    public static string Run()
    {
        if (Application.dataPath != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying)
            throw new InvalidOperationException("仅允许在本工程编辑模式制作");
        var hud = PrefabUtility.LoadPrefabContents(HudPath);
        try
        {
            var ui = hud.GetComponent<ArcadeHudUI>();
            var references = hud.GetComponent<ReferenceCollector>();
            var circle = ui.MoveJoystick.transform.Find("Handle").GetComponent<Image>().sprite;
            Style(ui.BtnPause.GetComponent<Image>(), circle);
            Style(ui.BtnBomb.GetComponent<Image>(), circle);

            Remove(ui.BtnPause.transform, "PauseIconRoot");
            var pause = new GameObject("PauseIconRoot", typeof(RectTransform));
            pause.transform.SetParent(ui.BtnPause.transform, false);
            Place((RectTransform)pause.transform, Vector2.zero, new Vector2(48, 48));
            foreach (var x in new[] { -10f, 10f })
            {
                var bar = Picture(pause.transform, x < 0 ? "LeftBar" : "RightBar", new Vector2(x, 0), new Vector2(8, 34));
                bar.color = new Color32(244, 240, 218, 255);
            }
            var exit = ui.BtnPause.GetComponentInChildren<TextMeshProUGUI>(true);
            exit.name = "OnlineExitLabel";
            var localized = exit.GetComponent<LocalizedText>();
            if (localized != null) Object.DestroyImmediate(localized);
            Place(exit.rectTransform, Vector2.zero, new Vector2(80, 68));
            exit.alignment = TextAlignmentOptions.Center;
            exit.fontSize = exit.fontSizeMax = 24; exit.fontSizeMin = 14;
            exit.enableAutoSizing = true; exit.text = "退出"; exit.gameObject.SetActive(false);
            references.Add("PauseIconRoot", pause);
            references.Add("TextPauseOnlineExit", exit);

            var icon = ui.BtnBomb.transform.Find("AbilityIcon").GetComponent<Image>();
            Place(icon.rectTransform, new Vector2(0, 5), new Vector2(74, 74));
            icon.preserveAspect = true; icon.raycastTarget = false;
            var count = ui.BtnBomb.GetComponentInChildren<TextMeshProUGUI>(true);
            var countLocalization = count.GetComponent<LocalizedText>();
            if (countLocalization != null) Object.DestroyImmediate(countLocalization);
            count.transform.SetParent(ui.BtnBomb.transform, false);
            Remove(ui.BtnBomb.transform, "CountBadge");
            var badge = Picture(ui.BtnBomb.transform, "CountBadge", new Vector2(28, -28), new Vector2(36, 36));
            badge.sprite = circle; badge.color = new Color(.08f, .12f, .14f, 1);
            count.transform.SetParent(badge.transform, false); count.name = "Count";
            Place(count.rectTransform, Vector2.zero, new Vector2(32, 32));
            count.alignment = TextAlignmentOptions.Center;
            count.fontSize = count.fontSizeMax = 24; count.fontSizeMin = 14;
            count.enableAutoSizing = true; count.raycastTarget = false; count.text = "1";
            references.Add("TextMobileBombCount", count);
            references.Sort(); PrefabUtility.SaveAsPrefabAsset(hud, HudPath);

            var layout = PrefabUtility.LoadPrefabContents(LayoutPath);
            try
            {
                var editor = layout.GetComponent<MobileControlLayoutUI>();
                Copy(ui.BtnPause.transform, editor.PausePreview.transform, circle, "PauseIconRoot");
                Copy(ui.BtnBomb.transform, editor.BombPreview.transform, circle, "AbilityIcon", "CountBadge");
                PrefabUtility.SaveAsPrefabAsset(layout, LayoutPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(layout); }
        }
        finally { PrefabUtility.UnloadPrefabContents(hud); }
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath));
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(LayoutPath));
        // 生成器只保留一个自动回填请求；两份View已随工具入库，重复制作须显式回填两份引用。
        Bind();
        return "暂停与炸弹图标已同步到正式HUD和全屏编辑器，生成引用已回填";
    }

    /// <summary>只同步显示子对象；预览保留自己的拖动句柄，不获得战斗按钮回调。</summary>
    private static void Copy(Transform source, Transform target, Sprite circle, params string[] children)
    {
        for (var i = target.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(target.GetChild(i).gameObject);
        Style(target.GetComponent<Image>(), circle);
        foreach (var name in children)
        {
            var copy = Object.Instantiate(source.Find(name).gameObject, target, false);
            copy.name = name;
        }
    }

    private static void Style(Image image, Sprite circle)
    {
        image.sprite = circle; image.type = Image.Type.Simple; image.preserveAspect = true;
        image.color = new Color(.16f, .23f, .26f, .92f); image.raycastTarget = true;
        image.rectTransform.sizeDelta = new Vector2(96, 96);
    }

    private static Image Picture(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>(); image.raycastTarget = false;
        Place(image.rectTransform, position, size); return image;
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.anchoredPosition = position; rect.sizeDelta = size;
    }

    private static void Remove(Transform parent, string name)
    {
        var child = parent.Find(name); if (child != null) Object.DestroyImmediate(child.gameObject);
    }

    public static string Bind()
    {
        var bind = typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent", BindingFlags.Static | BindingFlags.NonPublic);
        bind.Invoke(null, new object[] { HudPath, "Game.ArcadeHudUI" });
        bind.Invoke(null, new object[] { LayoutPath, "Game.MobileControlLayoutUI" });
        return "HUD和布局编辑器的正式生成引用已绑定";
    }
}
