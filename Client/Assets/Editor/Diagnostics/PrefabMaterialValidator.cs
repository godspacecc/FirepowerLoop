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

        /// <summary>手动只读检查并报告范围与完成数量；扫描失败不显示校验通过。</summary>
        [MenuItem(MenuPath)]
        public static void RunFromMenu()
        {
            Debug.Log($"[PrefabMaterialValidator] 范围：{GamePrefabRoot} 下全部 Prefab 的 Renderer（含未激活子节点）；" +
                      "不覆盖全部场景或 UI 材质，不修改资源。当前编辑器检查不能替代目标设备渲染验证。");
            try
            {
                IReadOnlyList<PrefabMaterialIssue> issues = ScanGamePrefabs(out int scannedCount);
                foreach (PrefabMaterialIssue issue in issues)
                    Debug.LogError($"[PrefabMaterialValidator] {issue}");
                string summary = $"已完整扫描 {scannedCount} 个 Prefab，发现 {issues.Count} 个材质问题。";
                if (issues.Count == 0)
                    Debug.Log($"[PrefabMaterialValidator] 校验通过：{summary}");
                else
                    Debug.LogError($"[PrefabMaterialValidator] 校验失败：{summary}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[PrefabMaterialValidator] 校验未完成：{exception.Message}");
            }
        }

        /// <summary>
        /// 构建和测试共用的失败关闭入口；发现任意问题时返回 false，并提供完整定位信息。
        /// </summary>
        public static bool TryValidateGamePrefabs(out string error)
        {
            IReadOnlyList<PrefabMaterialIssue> issues;
            try
            {
                issues = ScanGamePrefabs();
            }
            catch (Exception exception)
            {
                error = $"Prefab 材质校验未完成：{exception.Message}";
                return false;
            }
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
            return ScanGamePrefabs(out _);
        }

        /// <summary>统计完整扫描数量；缺目录、空集合或读取失败时抛错，禁止将漏检视为通过。</summary>
        private static IReadOnlyList<PrefabMaterialIssue> ScanGamePrefabs(out int scannedCount)
        {
            scannedCount = 0;
            if (!AssetDatabase.IsValidFolder(GamePrefabRoot))
                throw new InvalidOperationException($"扫描目录不存在：{GamePrefabRoot}；已扫描 0 个 Prefab。");

            string[] prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { GamePrefabRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (prefabPaths.Length == 0)
                throw new InvalidOperationException($"扫描目录未找到 Prefab：{GamePrefabRoot}；已扫描 0 个 Prefab。");

            var issues = new List<PrefabMaterialIssue>();
            foreach (string prefabPath in prefabPaths)
            {
                try
                {
                    GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (root == null)
                        throw new InvalidOperationException("无法加载 Prefab。");
                    issues.AddRange(ScanGameObject(root, prefabPath));
                    scannedCount++;
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"已完整扫描 {scannedCount}/{prefabPaths.Length} 个 Prefab；读取或检查失败：{prefabPath}；{exception.Message}", exception);
                }
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
