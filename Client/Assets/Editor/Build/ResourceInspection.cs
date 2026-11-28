using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using YooAsset.Editor;

namespace Shared.UnityTools
{
    public abstract class ResourceInspectionWindow : EditorWindow
    {
        protected const string MenuPath = "Tools/诊断/资源诊断窗口";
        private (string Path, string Object, string Message)[] findings;
        private string status = "尚未扫描";
        private Vector2 scroll;

        protected static void Open<T>() where T : ResourceInspectionWindow
        {
            var window = GetWindow<T>("资源诊断");
            window.minSize = new Vector2(620, 300);
        }

        protected abstract IEnumerable<(string Path, string Object, string Message)> ScanFindings();

        protected void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("扫描", EditorStyles.toolbarButton, GUILayout.Width(64)))
                {
                    findings = null;
                    scroll = Vector2.zero;
                    try
                    {
                        findings = ScanFindings().OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(item => item.Object, StringComparer.OrdinalIgnoreCase).ToArray();
                        status = $"扫描完成，发现 {findings.Length} 项。";
                    }
                    catch (Exception error)
                    {
                        status = "扫描未完成：" + error.GetBaseException().Message;
                    }
                }
                if (GUILayout.Button("清空", EditorStyles.toolbarButton, GUILayout.Width(64)))
                {
                    findings = null;
                    status = "已清空";
                }
            }
            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);
            if (findings == null) return;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var item in findings)
            {
                if (GUILayout.Button(item.Path, EditorStyles.linkLabel))
                    EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(item.Path));
                EditorGUILayout.LabelField(item.Object, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(item.Message, EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space();
            }
            EditorGUILayout.EndScrollView();
        }
    }

    /// <summary>逐一检查场景和预制体资源并保留当前编辑场景；按工程配置根目录、渲染规则和收集地址。</summary>
    internal static class ResourceInspection
    {
        internal sealed class Finding
        {
            internal string Kind, Path, Object, Message;
        }

        internal static void Visit(IEnumerable<string> scenes, IEnumerable<string> prefabs, Action<string, GameObject> visit)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出运行模式后再扫描。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty || string.IsNullOrEmpty(scene.path))
                    throw new InvalidOperationException("请先保存或处理未保存的场景：" + scene.name);
            }
            if (PrefabStageUtility.GetCurrentPrefabStage()?.scene.isDirty == true)
                throw new InvalidOperationException("请先保存或处理未保存的预制体。");

            foreach (string path in scenes.Distinct(StringComparer.Ordinal))
            {
                var active = SceneManager.GetActiveScene();
                var scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened)
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                if (!scene.IsValid())
                    throw new InvalidOperationException("无法读取场景：" + path);
                try
                {
                    foreach (var root in scene.GetRootGameObjects())
                        visit(path, root);
                }
                finally
                {
                    if (opened)
                        EditorSceneManager.CloseScene(scene, true);
                    if (active.isLoaded && SceneManager.GetActiveScene() != active)
                        SceneManager.SetActiveScene(active);
                }
            }
            foreach (string path in prefabs.Distinct(StringComparer.Ordinal))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try { visit(path, root); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        internal static string[] FindPrefabs(params string[] roots)
        {
            string[] available = roots.Where(AssetDatabase.IsValidFolder).ToArray();
            return available.Length == 0 ? Array.Empty<string>() : AssetDatabase.FindAssets("t:Prefab", available)
                .Select(AssetDatabase.GUIDToAssetPath).Where(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        internal static IReadOnlyList<Finding> Scan(IEnumerable<string> scenes, IEnumerable<string> prefabs, string package,
            IEnumerable<(string path, string address)> required, bool urp)
        {
            var results = new List<Finding>();
            Visit(scenes, prefabs, (path, root) =>
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    var gameObject = transform.gameObject;
                    string hierarchy = HierarchyPath(transform);
                    void Add(string kind, string message) =>
                        results.Add(new Finding { Kind = kind, Path = path, Object = hierarchy, Message = message });

                    int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                    if (missing > 0)
                        Add("MissingScript", $"缺少 {missing} 个脚本。");
                    foreach (Component component in gameObject.GetComponents<Component>())
                    {
                        if (component == null)
                            continue;
                        using var serialized = new SerializedObject(component);
                        if (component is TMP_Text && serialized.FindProperty("m_fontAsset")?.objectReferenceValue == null)
                            Add("MissingTmpFont", "TMP 文本未绑定字体。");
                        if (component is Animator animator && animator.runtimeAnimatorController == null)
                            Add("MissingAnimatorController", "Animator 未绑定控制器。");
                        if (component is Image image && image.sprite == null)
                            Add("MissingImageSprite", "Image 未绑定 Sprite。");
                        if (component is SpriteRenderer sprite && sprite.sprite == null)
                            Add("MissingSpriteRendererSprite", "SpriteRenderer 未绑定 Sprite。");
                        if (component.GetType().Name == "ReferenceCollector")
                            InspectBindings(serialized, Add);

                        var property = serialized.GetIterator();
                        while (property.NextVisible(true))
                            if (property.propertyType == SerializedPropertyType.ObjectReference &&
                                property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                                Add("BrokenReference", component.GetType().Name + "." + property.propertyPath + " 引用已断开。");

                        if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) && component is Renderer renderer)
                        {
                            foreach (var material in renderer.sharedMaterials)
                            {
                                if (material == null || material.shader == null)
                                    Add("Material", renderer.GetType().Name + " 缺少材质或着色器。");
                                else if (material.shader.name == "Hidden/InternalErrorShader" ||
                                    (urp && (material.shader.name == "Standard" ||
                                        AssetDatabase.GetAssetPath(material) == "Resources/unity_builtin_extra")))
                                    Add("Material", $"{material.name} 使用不适用于当前渲染管线的着色器或默认材质。");
                            }
                        }
                    }
                }
            });
            InspectCollectors(package, required, results);
            return results.OrderBy(result => result.Path, StringComparer.Ordinal)
                .ThenBy(result => result.Object, StringComparer.Ordinal).ThenBy(result => result.Kind, StringComparer.Ordinal).ToArray();
        }

        private static void InspectBindings(SerializedObject data, Action<string, string> add)
        {
            var rows = data.FindProperty("data") ?? data.FindProperty("bindings") ?? data.FindProperty("_entries") ?? data.FindProperty("entries");
            if (rows == null || !rows.isArray) return;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < rows.arraySize; i++)
            {
                var row = rows.GetArrayElementAtIndex(i);
                string key = (row.FindPropertyRelative("key") ?? row.FindPropertyRelative("Key"))?.stringValue;
                var value = row.FindPropertyRelative("gameObject") ?? row.FindPropertyRelative("value") ?? row.FindPropertyRelative("Value") ??
                    row.FindPropertyRelative("target") ?? row.FindPropertyRelative("Target");
                if (string.IsNullOrWhiteSpace(key)) add("ReferenceCollectorEmptyKey", "收集器有空绑定名。");
                else if (!keys.Add(key)) add("ReferenceCollectorDuplicateKey", $"收集器绑定名重复：{key}。");
                if (value != null && value.objectReferenceValue == null)
                    add("ReferenceCollectorNullObject", $"收集器绑定缺少对象：{key}。");
            }
        }

        private static void InspectCollectors(string package, IEnumerable<(string path, string address)> required, List<Finding> results)
        {
            const string path = "Assets/AssetBundleCollectorSetting.asset";
            void Add(string message) => results.Add(new Finding { Kind = "Collection", Path = path, Object = package, Message = message });
            var settings = AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(path);
            var packageSetting = settings?.Packages.FirstOrDefault(item => item.PackageName == package);
            if (packageSetting == null) { Add("找不到资源收集配置。"); return; }

            foreach (var collector in packageSetting.Groups.SelectMany(group => group.Collectors))
                if (string.IsNullOrEmpty(collector.CollectPath) || string.IsNullOrEmpty(collector.CollectorGUID) ||
                    AssetDatabase.AssetPathToGUID(collector.CollectPath) != collector.CollectorGUID || !collector.IsValid())
                    Add("收集器路径、GUID 或规则无效：" + collector.CollectPath);

            try
            {
                var assets = settings.BeginCollect(package, true, false).CollectAssets;
                foreach (var entry in required)
                    if (!assets.Any(asset => asset.Address == entry.address &&
                        (string.IsNullOrEmpty(entry.path) || asset.AssetInfo.AssetPath == entry.path)))
                        Add("资源缺少必需地址：" + entry.address);
            }
            catch (Exception error) { Add("资源收集失败：" + error.GetBaseException().Message); }
        }

        internal static string HierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            for (; transform != null; transform = transform.parent)
                names.Push(transform.name);
            return string.Join("/", names);
        }
    }
}
