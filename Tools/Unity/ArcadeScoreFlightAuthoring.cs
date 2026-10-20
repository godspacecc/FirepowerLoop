using System;
using System.Reflection;
using Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>制作财宝飞行的独立 Box 和 HUD 集合，所有静态图形及引用由正式 Prefab 持有。</summary>
public static class ArcadeScoreFlightAuthoring
{
    private const string Box = "Assets/Game/Prefabs/UI/Arcade/Box/ArcadeScoreFlightBox.prefab";
    private const string Hud = "Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab";

    public static string Run()
    {
        if (EditorApplication.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅在 FirepowerLoop 编辑模式制作积分飞入效果");
        BuildBox();
        var root = PrefabUtility.LoadPrefabContents(Hud);
        try
        {
            var layer = root.transform.Find("ScoreFlightList")?.gameObject;
            if (layer == null) layer = new GameObject("ScoreFlightList", typeof(RectTransform), typeof(CustomListView));
            layer.transform.SetParent(root.transform, false);
            layer.transform.SetAsLastSibling();
            var rect = (RectTransform)layer.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            layer.GetComponent<CustomListView>().itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Box);
            var references = root.GetComponent<ReferenceCollector>();
            references.Add("ScoreFlightList", layer.GetComponent<CustomListView>()); references.Sort();
            PrefabUtility.SaveAsPrefabAsset(root, Hud);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        BoxUICodeSpawner.AutoUpdateBoxUI(AssetDatabase.LoadAssetAtPath<GameObject>(Box));
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(Hud));
        return "积分飞行 Box、HUD 集合已保存并执行 Box/UI 生成器";
    }

    /// <summary>编译生成 View 后显式回填两个正式资源，避免依赖编辑器延迟回调时序。</summary>
    public static string Bind()
    {
        typeof(BoxUICodeSpawner).GetMethod("BindGeneratedComponent", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { Box, "Game.ArcadeScoreFlightBoxUI" });
        typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { Hud, "Game.ArcadeHudUI" });
        return "积分飞行正式 Box/UI 引用已回填";
    }

    /// <summary>用 Unity 内置圆形 Sprite 制作金币轮廓，无额外文字、运行时拼装或输入遮挡。</summary>
    private static void BuildBox()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Box) != null) return;
        var circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        if (circle == null) throw new InvalidOperationException("Unity 内置圆形 UI Sprite 不可用");
        var root = new GameObject("ArcadeScoreFlightBox", typeof(RectTransform), typeof(CanvasGroup), typeof(ReferenceCollector));
        try
        {
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(38, 38);
            var opacity = root.GetComponent<CanvasGroup>(); opacity.blocksRaycasts = opacity.interactable = false;
            Image(root.transform, "Glow", circle, new Vector2(54, 54), new Color(1, .72f, .15f, .18f));
            var shadow = Image(root.transform, "Shadow", circle, new Vector2(36, 36), new Color(.32f, .13f, .015f));
            shadow.rectTransform.anchoredPosition = new Vector2(1, -3);
            Image(root.transform, "Rim", circle, new Vector2(36, 36), new Color(1, .92f, .4f));
            Image(root.transform, "Face", circle, new Vector2(27, 27), new Color(1, .64f, .07f));
            var shine = Image(root.transform, "Shine", null, new Vector2(4, 15), new Color(1, .97f, .67f));
            shine.rectTransform.localRotation = Quaternion.Euler(0, 0, -18);
            var references = root.GetComponent<ReferenceCollector>();
            references.Add("Motion", rect); references.Add("Opacity", opacity); references.Sort();
            PrefabUtility.SaveAsPrefabAsset(root, Box);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static Image Image(Transform parent, string name, Sprite sprite, Vector2 size, Color color)
    {
        var child = new GameObject(name, typeof(RectTransform), typeof(Image)); child.transform.SetParent(parent, false);
        var rect = (RectTransform)child.transform; rect.sizeDelta = size;
        var image = child.GetComponent<Image>(); image.sprite = sprite; image.color = color; image.raycastTarget = false;
        return image;
    }
}
