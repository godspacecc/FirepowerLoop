using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>在正式 HUD 中制作单条不拦截输入的阶段横幅，并通过现有生成器绑定引用。</summary>
public static class ArcadeStageBannerAuthoring
{
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("横幅制作仅支持编辑模式");
        const string path = "Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var banner = root.transform.Find("StageBannerRoot")?.gameObject;
            if (banner == null) banner = new GameObject("StageBannerRoot", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            banner.transform.SetParent(root.transform, false);
            var rect = (RectTransform)banner.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .8f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(760, 112);
            var background = banner.GetComponent<Image>();
            background.color = new Color(.035f, .055f, .075f, .8f);
            background.raycastTarget = false;
            var opacity = banner.GetComponent<CanvasGroup>();
            opacity.alpha = 0f;
            opacity.interactable = false;
            opacity.blocksRaycasts = false;
            var text = banner.transform.Find("TextStageBanner")?.GetComponent<TextMeshProUGUI>();
            if (text == null)
            {
                var child = new GameObject("TextStageBanner", typeof(RectTransform), typeof(TextMeshProUGUI));
                child.transform.SetParent(banner.transform, false);
                text = child.GetComponent<TextMeshProUGUI>();
            }
            var template = root.GetComponent<ArcadeHudUI>().TextWave;
            text.font = template.font;
            text.fontSharedMaterial = template.fontSharedMaterial;
            text.fontSize = 54;
            text.fontSizeMin = 32;
            text.fontSizeMax = 54;
            text.enableAutoSizing = true;
            text.fontStyle = FontStyles.Bold;
            text.color = new Color(1f, .91f, .64f, 1f);
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            text.text = string.Empty;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(24, 12);
            text.rectTransform.offsetMax = new Vector2(-24, -12);
            banner.SetActive(false);
            var collector = root.GetComponent<ReferenceCollector>();
            collector.Add("StageBannerRoot", banner);
            collector.Add("StageBannerOpacity", opacity);
            collector.Add("TextStageBanner", text);
            collector.Sort();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        return "ArcadeHud stage banner saved; generated view binding requested.";
    }

    /// <summary>待新 View 编译完成后，通过项目生成器的正式回填步骤完成序列化绑定。</summary>
    public static string FinishBindings()
    {
        if (typeof(ArcadeHudUI).GetField("TextStageBanner") == null)
            throw new System.InvalidOperationException("请先等待 ArcadeHudUI 生成字段完成编译");
        typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Invoke(null, new object[] { "Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab", "Game.ArcadeHudUI" });
        return "ArcadeHud generated references bound.";
    }
}
