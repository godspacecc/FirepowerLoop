using System;
using System.Reflection;
using Game;
using UnityEditor;
using UnityEngine;

/// <summary>通过正式 UI 生成器恢复编译中断后的绑定，并审计序列化引用；不手写生成字段。</summary>
public static class CivilisationUiBindingAudit
{
    public static string Run()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("等待 Unity 编译完成后再绑定 UI");
        var process = typeof(UICodeSpawner).GetMethod("ProcessPendingBindingsWhenReady", BindingFlags.Static | BindingFlags.NonPublic);
        if (process == null) throw new InvalidOperationException("正式 UI 生成器的待绑定入口不存在");
        foreach (var name in new[] { "CivilisationWorkshop", "CivilisationSettlement" })
        {
            var path = "Assets/Game/Prefabs/UI/Civilisation/" + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException(path + " 缺失");
            UICodeSpawner.AutoUpdateUI(prefab);
            // 两个请求共用生成器的待绑定槽，逐个消费，避免后一个请求覆盖前一个。
            process.Invoke(null, null);
            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var view = loaded.GetComponent<BaseUI>();
            if (view == null) throw new InvalidOperationException(name + " 未生成 View");
            foreach (var entry in loaded.GetComponent<ReferenceCollector>().data)
            {
                var field = view.GetType().GetField(entry.key);
                if (entry.gameObject == null || field == null || !Equals(field.GetValue(view), entry.gameObject))
                    throw new InvalidOperationException(name + "." + entry.key + " 绑定无效");
            }
        }
        return "两个文明 UI 已通过正式生成器绑定，全部 collected 字段有效。";
    }
}
