using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 描述一个 Prefab Renderer 材质问题；只记录定位与原因，不负责自动替换材质。
    /// </summary>
    public sealed class PrefabMaterialIssue
    {
        public PrefabMaterialIssue(
            string assetPath,
            string hierarchyPath,
            string rendererType,
            int materialSlot,
            string message)
        {
            AssetPath = assetPath ?? string.Empty;
            HierarchyPath = hierarchyPath ?? string.Empty;
            RendererType = rendererType ?? string.Empty;
            MaterialSlot = materialSlot;
            Message = message ?? string.Empty;
        }

        public string AssetPath { get; }

        public string HierarchyPath { get; }

        public string RendererType { get; }

        public int MaterialSlot { get; }

        public string Message { get; }

        public override string ToString()
        {
            string slot = MaterialSlot >= 0 ? MaterialSlot.ToString() : "无";
            return $"{AssetPath} :: {HierarchyPath} ({RendererType}, 材质槽 {slot}) - {Message}";
        }
    }

    /// <summary>
    /// 只读扫描游戏 Prefab 的 Renderer 材质，阻止空材质、旧管线材质和不受支持 Shader 进入资源包。
    /// 不负责选择或生成替代材质，避免破坏不同表现对象的颜色与渲染语义。
    /// </summary>
    public static class PrefabMaterialValidator
    {
        private const string GamePrefabRoot = "Assets/Game/Prefabs";
        private const string MenuPath = "Tools/诊断/扫描 Prefab 材质";
        private const string UniversalPipelineTag = "UniversalPipeline";
        private const string BuiltinResourcePath = "Resources/unity_builtin_extra";

        [MenuItem(MenuPath)]
        public static void RunFromMenu()
        {
            IReadOnlyList<PrefabMaterialIssue> issues = ScanGamePrefabs();
            if (issues.Count == 0)
            {
                Debug.Log($"[PrefabMaterialValidator] 校验通过：{GamePrefabRoot} 未发现异常 Renderer 材质。");
                return;
            }

            for (int i = 0; i < issues.Count; i++)
            {
                Debug.LogError($"[PrefabMaterialValidator] {issues[i]}");
            }

            Debug.LogError($"[PrefabMaterialValidator] 校验失败：共发现 {issues.Count} 个异常材质槽。");
        }

        /// <summary>
        /// 构建和测试共用的失败关闭入口；发现任意问题时返回 false，并提供完整定位信息。
        /// </summary>
        public static bool TryValidateGamePrefabs(out string error)
        {
            IReadOnlyList<PrefabMaterialIssue> issues = ScanGamePrefabs();
            if (issues.Count == 0)
            {
                error = null;
                return true;
            }

            var builder = new StringBuilder();
            builder.AppendLine($"Prefab 材质校验失败，共发现 {issues.Count} 个问题：");
            for (int i = 0; i < issues.Count; i++)
            {
                builder.AppendLine(issues[i].ToString());
            }

            error = builder.ToString().TrimEnd();
            return false;
        }

        /// <summary>
        /// 扫描游戏 Prefab 根目录，包括所有未激活子节点上的 Renderer。
        /// </summary>
        public static IReadOnlyList<PrefabMaterialIssue> ScanGamePrefabs()
        {
            string[] prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { GamePrefabRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var issues = new List<PrefabMaterialIssue>();
            for (int i = 0; i < prefabPaths.Length; i++)
            {
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPaths[i]);
                if (root == null)
                {
                    continue;
                }

                issues.AddRange(ScanGameObject(root, prefabPaths[i]));
            }

            return SortIssues(issues);
        }

        /// <summary>
        /// 扫描单个根对象，供项目扫描和聚焦测试复用；不会修改对象或材质。
        /// </summary>
        public static IReadOnlyList<PrefabMaterialIssue> ScanGameObject(GameObject root, string assetPath)
        {
            var issues = new List<PrefabMaterialIssue>();
            if (root == null)
            {
                return issues;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                string hierarchyPath = GetHierarchyPath(renderer.transform, root.transform);
                if (materials == null || materials.Length == 0)
                {
                    issues.Add(CreateIssue(assetPath, hierarchyPath, renderer, -1, "Renderer 没有材质槽。"));
                    continue;
                }

                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    ScanMaterial(assetPath, hierarchyPath, renderer, materialIndex, materials[materialIndex], issues);
                }
            }

            return SortIssues(issues);
        }

        private static void ScanMaterial(
            string assetPath,
            string hierarchyPath,
            Renderer renderer,
            int materialIndex,
            Material material,
            ICollection<PrefabMaterialIssue> issues)
        {
            if (material == null)
            {
                issues.Add(CreateIssue(assetPath, hierarchyPath, renderer, materialIndex, "材质槽为空。"));
                return;
            }

            Shader shader = material.shader;
            if (shader == null)
            {
                issues.Add(CreateIssue(assetPath, hierarchyPath, renderer, materialIndex,
                    $"材质 {material.name} 的 Shader 为空。"));
                return;
            }

            string shaderName = shader.name ?? string.Empty;
            string materialPath = AssetDatabase.GetAssetPath(material) ?? string.Empty;
            if (materialPath.StartsWith(BuiltinResourcePath, StringComparison.Ordinal))
            {
                issues.Add(CreateIssue(assetPath, hierarchyPath, renderer, materialIndex,
                    $"材质 {material.name} 引用了 Unity 内置默认资源 {materialPath}，URP 下可能显示为粉色。"));
                return;
            }

            if (string.Equals(shaderName, "Standard", StringComparison.Ordinal) ||
                shaderName.StartsWith("Legacy Shaders/", StringComparison.Ordinal))
            {
                issues.Add(CreateIssue(assetPath, hierarchyPath, renderer, materialIndex,
                    $"材质 {material.name} 使用旧渲染管线 Shader：{shaderName}。"));
                return;
            }

            if (!shader.isSupported)
            {
                issues.Add(CreateIssue(assetPath, hierarchyPath, renderer, materialIndex,
                    $"材质 {material.name} 的 Shader 在当前编辑器中不受支持：{shaderName}。"));
                return;
            }

            string pipelineTag = material.GetTag("RenderPipeline", false, string.Empty);
            if (!string.IsNullOrWhiteSpace(pipelineTag) &&
                !string.Equals(pipelineTag, UniversalPipelineTag, StringComparison.Ordinal))
            {
                issues.Add(CreateIssue(assetPath, hierarchyPath, renderer, materialIndex,
                    $"材质 {material.name} 的 RenderPipeline 标记为 {pipelineTag}，不是 {UniversalPipelineTag}。"));
            }
        }

        private static PrefabMaterialIssue CreateIssue(
            string assetPath,
            string hierarchyPath,
            Renderer renderer,
            int materialSlot,
            string message)
        {
            return new PrefabMaterialIssue(
                assetPath,
                hierarchyPath,
                renderer != null ? renderer.GetType().Name : string.Empty,
                materialSlot,
                message);
        }

        private static IReadOnlyList<PrefabMaterialIssue> SortIssues(IEnumerable<PrefabMaterialIssue> issues)
        {
            return issues
                .OrderBy(issue => issue.AssetPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.HierarchyPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.RendererType, StringComparer.Ordinal)
                .ThenBy(issue => issue.MaterialSlot)
                .ToArray();
        }

        private static string GetHierarchyPath(Transform current, Transform root)
        {
            if (current == null)
            {
                return string.Empty;
            }

            string path = current.name;
            while (current.parent != null && current != root)
            {
                current = current.parent;
                path = current.name + "/" + path;
            }

            return path;
        }
    }
}
