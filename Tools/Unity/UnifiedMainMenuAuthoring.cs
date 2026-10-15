using System;
using System.Linq;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>将模式入口统一到 Start，并将旧街机入口收窄为进度和结果弹窗；只在编辑模式写入 Prefab。</summary>
public static class UnifiedMainMenuAuthoring
{
    private const string StartPath = "Assets/Game/Prefabs/UI/Home/Start.prefab";
    private const string DialogPath = "Assets/Game/Prefabs/UI/Arcade/ArcadeResume.prefab";

    /// <summary>生成的 View 编译完成后调用项目生成器自身的回绑步骤，避免连续制作两页覆盖单槽待绑定任务。</summary>
    public static string BindAfterCompile()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("等待 View 编译完成再回绑");
        var bind = typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        bind.Invoke(null, new object[] { StartPath, "Game.StartUI" });
        bind.Invoke(null, new object[] { DialogPath, "Game.ArcadeResumeUI" });
        foreach (var path in new[] { StartPath, DialogPath })
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var view = root.GetComponent<BaseUI>();
            foreach (var field in view.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                if ((Object)field.GetValue(view) == null) throw new InvalidOperationException(path + " 未完成回绑：" + field.Name);
        }
        return "两个 Prefab 的生成字段已全部解析并回绑";
    }

    public static string ApplyStart()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出运行模式");
        var root = PrefabUtility.LoadPrefabContents(StartPath);
        try
        {
            var view = root.GetComponent<StartUI>();
            var collector = root.GetComponent<ReferenceCollector>();
            var label = view.BtnContinue.GetComponentInChildren<TextMeshProUGUI>(true);
            var localization = label.GetComponent<LocalizedText>();
            if (localization != null) Object.DestroyImmediate(localization);
            label.text = "继续本局";
            var panel = view.ButtonGroup.parent;
            var summary = panel.Find("RunSummaryText");
            if (summary == null)
            {
                var go = new GameObject("RunSummaryText", typeof(RectTransform));
                go.transform.SetParent(panel, false);
                summary = go.transform;
            }
            var text = summary.GetComponent<TextMeshProUGUI>() ?? summary.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = label.font; text.fontSize = 23; text.color = new Color(.82f, .77f, .65f);
            text.alignment = TextAlignmentOptions.TopLeft; text.raycastTarget = false;
            text.enableWordWrapping = true; text.text = "";
            Rect((RectTransform)summary, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -205), new Vector2(470, 60), new Vector2(0, 1));
            collector.Add("RunSummaryText", text); collector.Sort();
            summary.gameObject.SetActive(false);
            foreach (var fixedText in root.GetComponentsInChildren<LocalizedText>(true))
            {
                if (fixedText.Key == "ui.start.new_game") fixedText.GetComponent<TMP_Text>().text = "单人开始";
                if (fixedText.Key == "ui.start.online") fixedText.GetComponent<TMP_Text>().text = "双人联机";
            }
            PrefabUtility.SaveAsPrefabAsset(root, StartPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(StartPath));
        return "Start 主菜单引用已生成";
    }

    public static string ApplyDialog()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出运行模式");
        var root = PrefabUtility.LoadPrefabContents(DialogPath);
        try
        {
            var view = root.GetComponent<ArcadeResumeUI>();
            var board = root.transform.Find("SafeContent/ArtworkBoard");
            var fit = board.GetComponent<AspectRatioFitter>();
            if (fit != null) Object.DestroyImmediate(fit);
            Rect((RectTransform)board, new Vector2(.5f,.5f), new Vector2(.5f,.5f), Vector2.zero, new Vector2(760,580), new Vector2(.5f,.5f));
            var plate = board.GetComponent<Image>(); plate.sprite = null; plate.color = new Color(.055f,.065f,.075f,1);
            root.GetComponent<Image>().color = new Color(0,0,0,.72f);
            foreach (var name in new[] { "Brand", "Subtitle", "SoloTitle", "SoloCaption", "OnlineTitle", "OnlineCaption", "IsolationHint" })
                board.Find(name).gameObject.SetActive(false);
            // 保留旧双人按钮与图集引用，但弹窗不再显示或绑定第二个模式入口。
            view.BtnOnline.gameObject.SetActive(false);
            var title = board.Find("Title").GetComponent<TextMeshProUGUI>();
            var localization = title.GetComponent<LocalizedText>();
            if (localization != null) Object.DestroyImmediate(localization);
            title.fontSharedMaterial = title.font.material;
            title.fontSize = 42; title.fontSizeMax = 42; title.text = "本局进度";
            title.alignment = TextAlignmentOptions.Left;
            Place(title.transform, 42, 28, 600, 60);
            root.GetComponent<ReferenceCollector>().Add("DialogTitle", title);
            Place(view.BtnClose.transform, 674, 26, 48, 48);
            var closeImage = (Image)view.BtnClose.targetGraphic;
            closeImage.sprite = null; closeImage.color = new Color(.22f,.23f,.24f);
            Place(view.ResumeDetails.transform, 42, 110, 676, 320);
            view.ResumeDetails.GetComponent<Image>().color = Color.clear;
            view.ResumeDetails.transform.Find("SavedTitle").gameObject.SetActive(false);
            Place(view.TextSummary.transform, 10, 0, 656, 170, 676, 320);
            view.TextSummary.fontSize = 30; view.TextSummary.fontSizeMax = 30; view.TextSummary.enableWordWrapping = true;
            Place(view.TextRewards.transform, 10, 176, 656, 52, 676, 320);
            view.TextRewards.fontSize = 28; view.TextRewards.fontSizeMax = 28;
            Place(view.TextMessage.transform, 42, 356, 676, 70);
            view.TextMessage.fontSize = 23; view.TextMessage.fontSizeMax = 23; view.TextMessage.enableWordWrapping = true;
            view.BtnContinue.transform.SetParent(board, false);
            view.BtnAbandon.transform.SetParent(board, false);
            view.BtnSettle.transform.SetParent(board, false);
            Place(view.BtnContinue.transform, 394, 470, 324, 66);
            Place(view.BtnAbandon.transform, 42, 470, 324, 66);
            Place(view.BtnSettle.transform, 218, 470, 324, 66);
            foreach (var button in new[] { view.BtnContinue, view.BtnAbandon, view.BtnSettle })
            {
                var image = (Image)button.targetGraphic;
                image.sprite = null;
                image.color = button == view.BtnAbandon ? new Color(.33f,.14f,.12f) : new Color(.63f,.40f,.16f);
                var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
                Rect(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(.5f,.5f));
                label.fontSize = 28; label.fontSizeMax = 28; label.color = new Color(.98f,.93f,.82f);
            }
            root.GetComponent<ReferenceCollector>().Sort();
            PrefabUtility.SaveAsPrefabAsset(root, DialogPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(DialogPath));
        return "进度和结果弹窗引用已生成";
    }

    // 静态布局只在 Prefab 制作阶段保存，运行时 Controller 不复制这些坐标。
    private static void Place(Transform transform, float x, float y, float width, float height, float parentWidth=760, float parentHeight=580)
    {
        Rect((RectTransform)transform, new Vector2(x/parentWidth,1-(y+height)/parentHeight),
            new Vector2((x+width)/parentWidth,1-y/parentHeight), Vector2.zero, Vector2.zero, new Vector2(.5f,.5f));
    }

    private static void Rect(RectTransform rect, Vector2 min, Vector2 max, Vector2 position, Vector2 size, Vector2 pivot)
    { rect.anchorMin=min; rect.anchorMax=max; rect.pivot=pivot; rect.anchoredPosition=position; rect.sizeDelta=size; }
}
