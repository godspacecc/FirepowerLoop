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
            // 本局信息统一由详情框展示，移除重复摘要并收回其占用的按钮上方空间。
            collector.Remove("RunSummaryText");
            if (summary != null) Object.DestroyImmediate(summary.gameObject);
            collector.Sort();
            view.ButtonGroup.anchoredPosition = new Vector2(0, -205);
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

    /// <summary>复用已确认的详情制作入口，避免旧工具重新覆盖正式样式。</summary>
    public static string ApplyDialog()
    {
        var author = typeof(UICodeSpawner).Assembly.GetType("ArcadeResumeDesignAuthoring", true);
        return (string)author.GetMethod("Apply").Invoke(null, null);
    }
    private static void Rect(RectTransform rect, Vector2 min, Vector2 max, Vector2 position, Vector2 size, Vector2 pivot)
    { rect.anchorMin=min; rect.anchorMax=max; rect.pivot=pivot; rect.anchoredPosition=position; rect.sizeDelta=size; }
}
