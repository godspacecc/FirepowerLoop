using System;
using System.Reflection;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>一次性补充正式街机 HUD 的触屏第一人称开火按钮，并通过项目生成器生成 View 绑定。</summary>
public static class ArcadeMobileFirstPersonAuthoring
{
    private const string HudPath = "Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab";

    /// <summary>稳定布局保存到 Prefab；复用已有按钮视觉、触点组件和正式本地化文案。</summary>
    public static string CreateFireButton()
    {
        var root = PrefabUtility.LoadPrefabContents(HudPath);
        try
        {
            var ui = root.GetComponent<ArcadeHudUI>();
            var collector = root.GetComponent<ReferenceCollector>();
            if (collector.data.Exists(item => item.key == "FirstPersonFirePress"))
                throw new InvalidOperationException("第一人称开火组件已经存在，拒绝重复创建");
            var button = UnityEngine.Object.Instantiate(ui.BtnExitFirstPerson, ui.JoystickLayoutArea, false);
            button.name = "FirstPersonFire";
            button.onClick.RemoveAllListeners();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(-190f, 190f);
            rect.sizeDelta = new Vector2(150f, 150f);
            var text = button.GetComponentInChildren<TextMeshProUGUI>(true);
            text.text = "开火";
            text.raycastTarget = false;
            var localized = text.GetComponent<LocalizedText>() ?? text.gameObject.AddComponent<LocalizedText>();
            localized.Key = "ui.mobile_base.fire";
            localized.Texts.Clear();
            collector.Add("FirstPersonFirePress", button.gameObject.AddComponent<MobileCombatPressView>());
            button.gameObject.SetActive(false);
            collector.Sort();
            PrefabUtility.SaveAsPrefabAsset(root, HudPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        if (!EditorApplication.ExecuteMenuItem("Assets/智能更新UI"))
            throw new InvalidOperationException("正式 UI 生成入口未执行");
        return "开火按钮和 ReferenceCollector 已保存，已执行正式 UI 生成器";
    }

    /// <summary>程序集编译完成后，使用同一生成器的绑定阶段核实正式 View 引用。</summary>
    public static string BindAfterCompilation()
    {
        typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { HudPath, "Game.ArcadeHudUI" });
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        var field = typeof(ArcadeHudUI).GetField("FirstPersonFirePress");
        if (field?.GetValue(prefab.GetComponent<ArcadeHudUI>()) == null)
            throw new InvalidOperationException("开火 View 字段未成功绑定");
        return "正式开火 View 绑定已核对";
    }
}
