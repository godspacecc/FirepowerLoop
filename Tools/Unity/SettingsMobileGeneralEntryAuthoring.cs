using System;
using System.Reflection;
using Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>保留设置页样式，让侧栏按可见分页自然排列；手机隐藏操作分页后不留下空槽。</summary>
public static class SettingsMobileGeneralEntryAuthoring
{
    private const string PrefabPath = "Assets/Game/Prefabs/UI/Home/Setting.prefab";

    public static string Run()
    {
        if (Application.dataPath != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying)
            throw new InvalidOperationException("仅允许本工程编辑模式制作");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var ui = root.GetComponent<SettingUI>();
            var sidebar = ui.GeneralTabButton.transform.parent;
            var layout = sidebar.GetComponent<VerticalLayoutGroup>() ?? sidebar.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 32, 0);
            layout.spacing = 24;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childScaleWidth = layout.childScaleHeight = false;
            foreach (var button in new[] { ui.GeneralTabButton, ui.AudioTabButton, ui.ControlsTabButton, ui.NetworkTabButton })
            {
                var element = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
                element.minHeight = element.preferredHeight = 72;
                element.flexibleHeight = 0;
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        return "设置侧栏已使用原生布局，已执行正式UI生成器";
    }

    public static string Bind()
    {
        typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { PrefabPath, "Game.SettingUI" });
        return "设置View已由正式生成器绑定";
    }
}
