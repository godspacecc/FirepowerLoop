using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class CommonEditorToolsTests
    {
        private const string TempFolder = "Assets/Tests/EditMode/Temp";
        private const string TempPrefabPath = TempFolder + "/DiagnosticsHash.prefab";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TempPrefabPath) != null)
            {
                AssetDatabase.DeleteAsset(TempPrefabPath);
            }
        }

        [Test]
        public void Diagnostics_ReportExpectedResourceIssues()
        {
            GameObject root = new GameObject("DiagnosticsRoot");
            try
            {
                var collector = root.AddComponent<ReferenceCollector>();
                collector.data.Add(new ReferenceCollectorData
                {
                    key = string.Empty,
                    gameObject = root,
                });
                collector.data.Add(new ReferenceCollectorData
                {
                    key = "MissingTarget",
                    gameObject = null,
                });

                var textObject = new GameObject("Title", typeof(RectTransform));
                textObject.transform.SetParent(root.transform);
                TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
                typeof(TMP_Text)
                    .GetField("m_fontAsset", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(text, null);

                var animatorObject = new GameObject("Animator");
                animatorObject.transform.SetParent(root.transform);
                animatorObject.AddComponent<Animator>();

                var imageObject = new GameObject("Image", typeof(RectTransform));
                imageObject.transform.SetParent(root.transform);
                imageObject.AddComponent<Image>().sprite = null;

                var spriteObject = new GameObject("Sprite");
                spriteObject.transform.SetParent(root.transform);
                spriteObject.AddComponent<SpriteRenderer>().sprite = null;

                var issues = InvokeScanGameObject(root, "Assets/Tests/EditMode/Temp/InMemory.prefab")
                    .Select(GetIssueTypeName)
                    .ToArray();

                CollectionAssert.Contains(issues, "ReferenceCollectorEmptyKey");
                CollectionAssert.Contains(issues, "ReferenceCollectorNullObject");
                CollectionAssert.Contains(issues, "MissingTmpFont");
                CollectionAssert.Contains(issues, "MissingAnimatorController");
                CollectionAssert.Contains(issues, "MissingImageSprite");
                CollectionAssert.Contains(issues, "MissingSpriteRendererSprite");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Diagnostics_DoNotMutatePrefabAssetFile()
        {
            EnsureTempFolder();

            GameObject root = new GameObject("DiagnosticsHashRoot", typeof(RectTransform));
            try
            {
                root.AddComponent<Image>().sprite = null;
                PrefabUtility.SaveAsPrefabAsset(root, TempPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.Refresh();
            string beforeHash = HashFile(TempPrefabPath);

            object result = InvokeStatic(
                ResolveEditorType("Game.MissingScriptDiagnostics"),
                "ScanPrefabAssets",
                new object[] { new[] { TempPrefabPath } });

            Assert.That(((IEnumerable)result).Cast<object>().Count(), Is.GreaterThanOrEqualTo(1));
            Assert.That(HashFile(TempPrefabPath), Is.EqualTo(beforeHash));
        }

        [Test]
        public void SceneSwitcher_EnumeratesScenesAndUsesAnotherDayPrefsKeys()
        {
            Type sceneSwitcherType = ResolveEditorType("Game.SceneSwitcherWindow");

            var scenePaths = ((IEnumerable)InvokeStatic(sceneSwitcherType, "GetScenePaths", Array.Empty<object>()))
                .Cast<string>()
                .ToArray();

            CollectionAssert.Contains(scenePaths, "Assets/Game/Scenes/Main.unity");
            Assert.That(scenePaths, Has.All.EndsWith(".unity"));

            FieldInfo searchKey = sceneSwitcherType.GetField("SearchPrefsKey", BindingFlags.Public | BindingFlags.Static);
            FieldInfo favoritesKey = sceneSwitcherType.GetField("FavoritesPrefsKey", BindingFlags.Public | BindingFlags.Static);
            Assert.That(searchKey?.GetValue(null), Does.StartWith("AnotherDay.SceneSwitcher."));
            Assert.That(favoritesKey?.GetValue(null), Does.StartWith("AnotherDay.SceneSwitcher."));

            InvokeStatic(sceneSwitcherType, "SetFavoriteScenePathsForTests", new object[] { new[] { "Assets/Game/Scenes/Main.unity" } });
            var favorites = ((IEnumerable)InvokeStatic(sceneSwitcherType, "GetFavoriteScenePathsForTests", Array.Empty<object>()))
                .Cast<string>()
                .ToArray();

            CollectionAssert.Contains(favorites, "Assets/Game/Scenes/Main.unity");
        }

        [Test]
        public void SceneSwitcher_SearchFieldUsesDedicatedToolbarRow()
        {
            string source = File.ReadAllText(Path.Combine(Application.dataPath, "Editor/Scene/SceneSwitcherWindow.cs"));
            string toolbarBody = ExtractMethodBody(source, "DrawToolbar");

            StringAssert.Contains("window.minSize = new Vector2(420f, 260f)", source);
            StringAssert.Contains("EditorGUILayout.BeginVertical(EditorStyles.toolbar)", toolbarBody);

            int verticalStart = toolbarBody.IndexOf("EditorGUILayout.BeginVertical(EditorStyles.toolbar)", StringComparison.Ordinal);
            int firstRowStart = toolbarBody.IndexOf("EditorGUILayout.BeginHorizontal()", verticalStart, StringComparison.Ordinal);
            int labelIndex = toolbarBody.IndexOf("GUILayout.Label(", firstRowStart, StringComparison.Ordinal);
            int labelWidthIndex = toolbarBody.IndexOf("GUILayout.Width(48f)", labelIndex, StringComparison.Ordinal);
            int textFieldIndex = toolbarBody.IndexOf("EditorStyles.toolbarTextField", labelWidthIndex, StringComparison.Ordinal);
            int expandWidthIndex = toolbarBody.IndexOf("GUILayout.ExpandWidth(true)", textFieldIndex, StringComparison.Ordinal);
            int firstRowEnd = toolbarBody.IndexOf("EditorGUILayout.EndHorizontal()", expandWidthIndex, StringComparison.Ordinal);
            int secondRowStart = toolbarBody.IndexOf("EditorGUILayout.BeginHorizontal()", firstRowEnd, StringComparison.Ordinal);
            int refreshButtonIndex = toolbarBody.IndexOf("GUILayout.Button(", secondRowStart, StringComparison.Ordinal);
            int refreshWidthIndex = toolbarBody.IndexOf("GUILayout.Width(64f)", refreshButtonIndex, StringComparison.Ordinal);
            int refreshCallIndex = toolbarBody.IndexOf("RefreshSceneList()", refreshWidthIndex, StringComparison.Ordinal);
            int secondRowEnd = toolbarBody.IndexOf("EditorGUILayout.EndHorizontal()", refreshCallIndex, StringComparison.Ordinal);
            int verticalEnd = toolbarBody.IndexOf("EditorGUILayout.EndVertical()", secondRowEnd, StringComparison.Ordinal);

            Assert.That(firstRowStart, Is.GreaterThan(verticalStart), "Search row must be inside the toolbar vertical group.");
            Assert.That(labelIndex, Is.GreaterThan(firstRowStart), "Search row must contain the search label.");
            Assert.That(labelWidthIndex, Is.GreaterThan(labelIndex), "Search label must keep the 48px width.");
            Assert.That(textFieldIndex, Is.GreaterThan(labelWidthIndex), "Search field must share the first row with the label.");
            Assert.That(expandWidthIndex, Is.GreaterThan(textFieldIndex), "Search field must expand horizontally.");
            Assert.That(firstRowEnd, Is.GreaterThan(expandWidthIndex), "Search row must end after the expanding text field.");
            Assert.That(secondRowStart, Is.GreaterThan(firstRowEnd), "Refresh must be drawn on a second row.");
            Assert.That(refreshButtonIndex, Is.GreaterThan(secondRowStart), "Second row must contain the refresh button.");
            Assert.That(refreshWidthIndex, Is.GreaterThan(refreshButtonIndex), "Refresh button must keep the 64px width.");
            Assert.That(refreshCallIndex, Is.GreaterThan(refreshWidthIndex), "Refresh button must still refresh the scene list.");
            Assert.That(secondRowEnd, Is.GreaterThan(refreshCallIndex), "Refresh row must close after the refresh callback.");
            Assert.That(verticalEnd, Is.GreaterThan(secondRowEnd), "Toolbar vertical group must close after both rows.");
            Assert.That(
                Regex.IsMatch(toolbarBody, @"TextField\s*\([^;]*GUILayout\.Width\s*\("),
                Is.False,
                "Search field must not use a fixed width.");
        }

        private static object[] InvokeScanGameObject(GameObject root, string assetPath)
        {
            object result = InvokeStatic(
                ResolveEditorType("Game.MissingScriptDiagnostics"),
                "ScanGameObject",
                new object[] { root, assetPath });

            return ((IEnumerable)result).Cast<object>().ToArray();
        }

        private static string GetIssueTypeName(object issue)
        {
            object issueType = issue.GetType().GetProperty("IssueType")?.GetValue(issue);
            return issueType?.ToString() ?? string.Empty;
        }

        private static object InvokeStatic(Type type, string methodName, object[] args)
        {
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, $"{type.FullName}.{methodName} was not found.");
            return method.Invoke(null, args);
        }

        private static Type ResolveEditorType(string typeName)
        {
            Type type = Type.GetType($"{typeName}, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null, $"{typeName} was not found in Assembly-CSharp-Editor.");
            return type;
        }

        private static string ExtractMethodBody(string source, string methodName)
        {
            string signature = $"private void {methodName}()";
            int signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.That(signatureIndex, Is.GreaterThanOrEqualTo(0), $"{signature} was not found.");

            int bodyStart = source.IndexOf('{', signatureIndex);
            Assert.That(bodyStart, Is.GreaterThan(signatureIndex), $"{signature} body was not found.");

            int depth = 0;
            for (int index = bodyStart; index < source.Length; index++)
            {
                if (source[index] == '{')
                {
                    depth++;
                }
                else if (source[index] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(bodyStart + 1, index - bodyStart - 1);
                    }
                }
            }

            Assert.Fail($"{signature} body was not closed.");
            return string.Empty;
        }

        private static void EnsureTempFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Tests/EditMode/Temp"))
            {
                AssetDatabase.CreateFolder("Assets/Tests/EditMode", "Temp");
            }
        }

        private static string HashFile(string assetPath)
        {
            using var sha256 = SHA256.Create();
            string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            byte[] hash = sha256.ComputeHash(File.ReadAllBytes(fullPath));
            return BitConverter.ToString(hash);
        }
    }
}
