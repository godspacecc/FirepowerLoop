using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game
{
    public enum EditorDiagnosticIssueType
    {
        MissingScript,
        ReferenceCollectorEmptyKey,
        ReferenceCollectorNullObject,
        MissingTmpFont,
        MissingAnimatorController,
        MissingImageSprite,
        MissingSpriteRendererSprite,
    }

    public sealed class EditorDiagnosticIssue
    {
        public EditorDiagnosticIssue(
            EditorDiagnosticIssueType issueType,
            string assetPath,
            string hierarchyPath,
            string message)
        {
            IssueType = issueType;
            AssetPath = assetPath ?? string.Empty;
            HierarchyPath = hierarchyPath ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public EditorDiagnosticIssueType IssueType { get; }

        public string AssetPath { get; }

        public string HierarchyPath { get; }

        public string Message { get; }

        public override string ToString()
        {
            return $"{GetIssueTypeName(IssueType)}：{AssetPath} :: {HierarchyPath} - {Message}";
        }

        private static string GetIssueTypeName(EditorDiagnosticIssueType issueType)
        {
            return issueType switch
            {
                EditorDiagnosticIssueType.MissingScript => "脚本丢失",
                EditorDiagnosticIssueType.ReferenceCollectorEmptyKey => "ReferenceCollector 空键",
                EditorDiagnosticIssueType.ReferenceCollectorNullObject => "ReferenceCollector 空对象",
                EditorDiagnosticIssueType.MissingTmpFont => "TMP 字体缺失",
                EditorDiagnosticIssueType.MissingAnimatorController => "Animator Controller 缺失",
                EditorDiagnosticIssueType.MissingImageSprite => "Image Sprite 缺失",
                EditorDiagnosticIssueType.MissingSpriteRendererSprite => "SpriteRenderer Sprite 缺失",
                _ => issueType.ToString(),
            };
        }
    }

    public static class MissingScriptDiagnostics
    {
        private const string MenuPath = "Tools/诊断/执行只读诊断";

        [MenuItem(MenuPath)]
        public static void RunProjectScanFromMenu()
        {
            IReadOnlyList<EditorDiagnosticIssue> issues = ScanProject();
            LogIssues(issues, "项目");
        }

        public static IReadOnlyList<EditorDiagnosticIssue> ScanProject()
        {
            var issues = new List<EditorDiagnosticIssue>();
            issues.AddRange(ScanPrefabAssets(GetPrefabAssetPaths()));
            issues.AddRange(ScanSceneAssets(GetSceneAssetPaths()));
            return SortIssues(issues);
        }

        public static IReadOnlyList<EditorDiagnosticIssue> ScanPrefabAssets(IEnumerable<string> prefabPaths)
        {
            var issues = new List<EditorDiagnosticIssue>();
            foreach (string prefabPath in prefabPaths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(prefabPath))
                {
                    continue;
                }

                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (root == null)
                {
                    continue;
                }

                issues.AddRange(ScanGameObject(root, prefabPath));
            }

            return SortIssues(issues);
        }

        public static IReadOnlyList<EditorDiagnosticIssue> ScanGameObject(GameObject root, string assetPath)
        {
            var issues = new List<EditorDiagnosticIssue>();
            if (root == null)
            {
                return issues;
            }

            var stack = new Stack<Transform>();
            stack.Push(root.transform);

            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                GameObject currentObject = current.gameObject;
                string hierarchyPath = GetHierarchyPath(current);

                ScanMissingScript(currentObject, assetPath, hierarchyPath, issues);
                ScanReferenceCollector(currentObject, assetPath, hierarchyPath, issues);
                ScanTmpText(currentObject, assetPath, hierarchyPath, issues);
                ScanAnimator(currentObject, assetPath, hierarchyPath, issues);
                ScanSpriteReferences(currentObject, assetPath, hierarchyPath, issues);

                for (int i = current.childCount - 1; i >= 0; i--)
                {
                    stack.Push(current.GetChild(i));
                }
            }

            return SortIssues(issues);
        }

        public static IReadOnlyList<EditorDiagnosticIssue> ScanBuildSettingsScenes()
        {
            string[] scenePaths = EditorBuildSettings.scenes
                .Where(scene => scene != null && scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                .Select(scene => scene.path)
                .ToArray();

            return ScanSceneAssets(scenePaths);
        }

        public static IReadOnlyList<EditorDiagnosticIssue> ScanGameScenes()
        {
            return ScanSceneAssets(GetGameSceneAssetPaths());
        }

        public static IReadOnlyList<string> GetPrefabAssetPaths()
        {
            return AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static IReadOnlyList<string> GetSceneAssetPaths()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene != null && scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                .Select(scene => scene.path)
                .Concat(GetGameSceneAssetPaths())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IReadOnlyList<EditorDiagnosticIssue> ScanSceneAssets(IEnumerable<string> scenePaths)
        {
            var issues = new List<EditorDiagnosticIssue>();
            string[] paths = (scenePaths ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (paths.Length == 0)
            {
                return issues;
            }

            SceneSetup[] originalSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (string scenePath in paths)
                {
                    Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        issues.AddRange(ScanGameObject(root, scenePath));
                    }
                }
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
            }

            return SortIssues(issues);
        }

        private static IReadOnlyList<string> GetGameSceneAssetPaths()
        {
            const string gameScenesRoot = "Assets/Game/Scenes";
            if (!AssetDatabase.IsValidFolder(gameScenesRoot))
            {
                return Array.Empty<string>();
            }

            return AssetDatabase.FindAssets("t:Scene", new[] { gameScenesRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void ScanMissingScript(
            GameObject gameObject,
            string assetPath,
            string hierarchyPath,
            ICollection<EditorDiagnosticIssue> issues)
        {
            int missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
            for (int i = 0; i < missingCount; i++)
            {
                issues.Add(new EditorDiagnosticIssue(
                    EditorDiagnosticIssueType.MissingScript,
                    assetPath,
                    hierarchyPath,
                    "对象存在丢失的 MonoBehaviour 脚本引用。"));
            }
        }

        private static void ScanReferenceCollector(
            GameObject gameObject,
            string assetPath,
            string hierarchyPath,
            ICollection<EditorDiagnosticIssue> issues)
        {
            var collector = gameObject.GetComponent<ReferenceCollector>();
            if (collector == null || collector.data == null)
            {
                return;
            }

            foreach (ReferenceCollectorData item in collector.data)
            {
                if (item == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.key))
                {
                    issues.Add(new EditorDiagnosticIssue(
                        EditorDiagnosticIssueType.ReferenceCollectorEmptyKey,
                        assetPath,
                        hierarchyPath,
                        "ReferenceCollector 存在空键。"));
                }

                if (item.gameObject == null)
                {
                    issues.Add(new EditorDiagnosticIssue(
                        EditorDiagnosticIssueType.ReferenceCollectorNullObject,
                        assetPath,
                        hierarchyPath,
                        $"ReferenceCollector 条目“{item.key}”没有目标对象。"));
                }
            }
        }

        private static void ScanTmpText(
            GameObject gameObject,
            string assetPath,
            string hierarchyPath,
            ICollection<EditorDiagnosticIssue> issues)
        {
            TMP_Text text = gameObject.GetComponent<TMP_Text>();
            if (text == null || HasSerializedTmpFont(text))
            {
                return;
            }

            issues.Add(new EditorDiagnosticIssue(
                EditorDiagnosticIssueType.MissingTmpFont,
                assetPath,
                hierarchyPath,
                "TMP 文本缺少字体资源。"));
        }

        private static bool HasSerializedTmpFont(TMP_Text text)
        {
            using var serializedObject = new SerializedObject(text);
            SerializedProperty fontProperty = serializedObject.FindProperty("m_fontAsset");
            return fontProperty == null || fontProperty.objectReferenceValue != null;
        }

        private static void ScanAnimator(
            GameObject gameObject,
            string assetPath,
            string hierarchyPath,
            ICollection<EditorDiagnosticIssue> issues)
        {
            Animator animator = gameObject.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController != null)
            {
                return;
            }

            issues.Add(new EditorDiagnosticIssue(
                EditorDiagnosticIssueType.MissingAnimatorController,
                assetPath,
                hierarchyPath,
                "Animator 缺少 Controller。"));
        }

        private static void ScanSpriteReferences(
            GameObject gameObject,
            string assetPath,
            string hierarchyPath,
            ICollection<EditorDiagnosticIssue> issues)
        {
            Image image = gameObject.GetComponent<Image>();
            if (image != null && image.sprite == null)
            {
                issues.Add(new EditorDiagnosticIssue(
                    EditorDiagnosticIssueType.MissingImageSprite,
                    assetPath,
                    hierarchyPath,
                    "Image 缺少 Sprite。"));
            }

            SpriteRenderer spriteRenderer = gameObject.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null && spriteRenderer.sprite == null)
            {
                issues.Add(new EditorDiagnosticIssue(
                    EditorDiagnosticIssueType.MissingSpriteRendererSprite,
                    assetPath,
                    hierarchyPath,
                    "SpriteRenderer 缺少 Sprite。"));
            }
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        private static IReadOnlyList<EditorDiagnosticIssue> SortIssues(IEnumerable<EditorDiagnosticIssue> issues)
        {
            return (issues ?? Enumerable.Empty<EditorDiagnosticIssue>())
                .OrderBy(issue => issue.AssetPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.HierarchyPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.IssueType)
                .ToArray();
        }

        private static void LogIssues(IReadOnlyList<EditorDiagnosticIssue> issues, string scope)
        {
            if (issues.Count == 0)
            {
                Debug.Log($"[只读诊断] {scope}扫描完成，未发现问题。");
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine($"[只读诊断] {scope}扫描发现 {issues.Count} 个问题。");
            foreach (EditorDiagnosticIssue issue in issues)
            {
                builder.AppendLine(issue.ToString());
            }

            Debug.LogWarning(builder.ToString());
        }

        private static string GetIssueTypeName(EditorDiagnosticIssueType issueType)
        {
            return issueType switch
            {
                EditorDiagnosticIssueType.MissingScript => "脚本丢失",
                EditorDiagnosticIssueType.ReferenceCollectorEmptyKey => "ReferenceCollector 空键",
                EditorDiagnosticIssueType.ReferenceCollectorNullObject => "ReferenceCollector 空对象",
                EditorDiagnosticIssueType.MissingTmpFont => "TMP 字体缺失",
                EditorDiagnosticIssueType.MissingAnimatorController => "Animator Controller 缺失",
                EditorDiagnosticIssueType.MissingImageSprite => "Image Sprite 缺失",
                EditorDiagnosticIssueType.MissingSpriteRendererSprite => "SpriteRenderer Sprite 缺失",
                _ => issueType.ToString(),
            };
        }
    }
}
