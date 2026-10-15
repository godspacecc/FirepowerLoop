using System;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>为河谷已制作的树木与岩石登记俯视遮挡表现；不修改碰撞、LOD、地面、河流或原始 Prefab。</summary>
public static class NaturalOcclusionAuthoring
{
    private const string ScenePath = "Assets/Game/Scenes/CivilisationValleyMap.unity";
    private const string MaterialPath = "Assets/Game/Art/Environment/BaseCamp/Materials/OcclusionTransparent.mat";
    // 仅选择已制作的自然遮挡物根；不把地面、道路、交互提示或水面加入淡化范围。
    private static readonly string[] Prefixes = { "ValleyTree_", "ValleyRock_", "RiverbankRock_", "BoundaryRock_" };

    /// <summary>通过 Unity MCP 执行；先验证所有引用，再幂等配置并只保存河谷场景。场景有未保存改动时拒绝覆盖。</summary>
    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请退出 Play Mode 后配置正式场景。");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null || !material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
            throw new InvalidOperationException("缺少正式透明材质依赖。");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        var opened = !scene.isLoaded;
        if (!opened && scene.isDirty)
            throw new InvalidOperationException("河谷场景有未保存改动，未执行配置。");
        var active = SceneManager.GetActiveScene();
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var blockers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(t => Prefixes.Any(prefix => t.name.StartsWith(prefix, StringComparison.Ordinal)))
                .ToArray();
            foreach (var prefix in Prefixes)
                if (!blockers.Any(t => t.name.StartsWith(prefix, StringComparison.Ordinal)))
                    throw new InvalidOperationException("未找到自然物件类别：" + prefix);
            var renderers = blockers.Select(t => t.GetComponentsInChildren<Renderer>(true)).ToArray();
            for (var i = 0; i < blockers.Length; i++)
            {
                if (renderers[i].Length == 0 || renderers[i].Any(r => r.sharedMaterials.Any(m => m == null || m.shader.name != "Universal Render Pipeline/Lit")))
                    throw new InvalidOperationException("自然物件不满足已验证的 URP Lit 材质契约：" + blockers[i].name);
            }
            var changed = 0;
            for (var i = 0; i < blockers.Length; i++)
            {
                var view = blockers[i].GetComponent<CameraOccluderView>();
                if (view != null && view.BodyRenderers.SequenceEqual(renderers[i]) && view.RoofRenderers.Length == 0 &&
                    view.InteriorCollider == null && view.TransparentMaterial == material) continue;
                if (view == null) view = Undo.AddComponent<CameraOccluderView>(blockers[i].gameObject);
                Undo.RecordObject(view, "配置自然物件半透明遮挡");
                view.BodyRenderers = renderers[i];
                view.RoofRenderers = Array.Empty<Renderer>();
                view.InteriorCollider = null;
                view.TransparentMaterial = material;
                // 只形成场景实例覆盖，保留正式来源 Prefab 与各 LOD 的原始碰撞、材质。
                PrefabUtility.RecordPrefabInstancePropertyModifications(view);
                EditorUtility.SetDirty(view);
                changed++;
            }
            if (changed > 0)
            {
                // 提交延迟的 Undo 记录后再保存，避免保存完成后场景又被同一批修改标脏。
                Undo.FlushUndoRecordObjects();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("河谷场景保存失败。");
            }
            return $"自然物件 {blockers.Length} 个，Renderer {renderers.Sum(r => r.Length)} 个；本次更新 {changed} 个。";
        }
        finally
        {
            if (opened && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }
}
