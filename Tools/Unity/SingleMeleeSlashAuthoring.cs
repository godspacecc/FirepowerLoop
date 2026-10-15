using System;
using Game;
using UnityEditor;
using UnityEngine;

/// <summary>按已验证的单刀方案接线五种武器；只更新动作定义，不改变基础片段、连招或战斗配置。</summary>
public static class SingleMeleeSlashAuthoring
{
    public static string Run()
    {
        const string folder = "Assets/Game/Art/Arcade/Weapons/Animations/";
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(
            "Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
        var source = AssetDatabase.LoadAssetAtPath<CharacterAnimationDefinition>(folder + "SteelBladeSlash1.asset");
        var ids = new[] { "weapon.knife", "weapon.axe", "weapon.pickaxe", "weapon.han.axe", "weapon.han.pick" };
        if (catalog == null || source?.Clip == null) throw new InvalidOperationException("正式武器目录或钢刀第一刀缺失");
        foreach (var id in ids)
            if (catalog.Find(id) == null) throw new InvalidOperationException("武器身份缺失：" + id);
        var slash = AssetDatabase.LoadAssetAtPath<CharacterAnimationDefinition>(folder + "SingleMeleeSlash.asset");
        if (slash == null)
        {
            slash = UnityEngine.Object.Instantiate(source);
            slash.name = "SingleMeleeSlash";
            AssetDatabase.CreateAsset(slash, folder + "SingleMeleeSlash.asset");
        }
        var definition = new SerializedObject(slash);
        definition.FindProperty("clip").objectReferenceValue = source.Clip;
        definition.FindProperty("displayName").stringValue = "单次单手劈砍";
        definition.FindProperty("singleAttackDurationSeconds").floatValue = .35f;
        definition.ApplyModifiedPropertiesWithoutUndo();
        var serialized = new SerializedObject(catalog);
        var entries = serialized.FindProperty("weapons");
        for (int i = 0; i < entries.arraySize; i++)
        {
            var entry = entries.GetArrayElementAtIndex(i);
            if (Array.IndexOf(ids, entry.FindPropertyRelative("id").stringValue) < 0) continue;
            entry.FindPropertyRelative("animations").FindPropertyRelative("attack").objectReferenceValue = slash;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(slash);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        return "已更新五种单次劈砍武器，共用前刺与钢刀四段连招保持原引用";
    }
}
