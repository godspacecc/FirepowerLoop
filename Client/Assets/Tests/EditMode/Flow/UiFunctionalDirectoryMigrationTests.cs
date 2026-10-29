using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class UiFunctionalDirectoryMigrationTests
    {
        private readonly struct AssetEntry
        {
            public AssetEntry(string path, string guid, string oldPath, string view = null)
            {
                Path = path; Guid = guid; OldPath = oldPath; View = view;
            }
            public string Path { get; }
            public string Guid { get; }
            public string OldPath { get; }
            public string View { get; }
        }

        // 固定为迁移前记录的 GUID；测试不得从当前 AssetDatabase、.meta 或扫描结果推导期望值。
        private static readonly AssetEntry[] Prefabs =
        {
            new AssetEntry("Assets/Game/Prefabs/UI/Diagnostics/RuntimeLog.prefab","de5223b045130984fa4d2f53794b1749","Assets/Game/Prefabs/UI/Common/RuntimeLog.prefab","RuntimeLogUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Diagnostics/TestCmd.prefab","3c6c3ac513d9dbf4eb1ee05827647a97","Assets/Game/Prefabs/UI/Common/TestCmd.prefab","TestCmdUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Diagnostics/TestCmdButton.prefab","889a13e3200dfd645bad25e8c9ee7c13","Assets/Game/Prefabs/UI/Common/TestCmdButton.prefab","TestCmdButtonUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Diagnostics/Box/RuntimeLogItemBox.prefab","a04661945b1ca7840b03bce84fa808ad","Assets/Game/Prefabs/UI/Common/Box/RuntimeLogItemBox.prefab","RuntimeLogItemBoxUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Diagnostics/Box/TestCmdCategoryBox.prefab","2ec4168844fa61748a8869db21f7b86d","Assets/Game/Prefabs/UI/Common/Box/TestCmdCategoryBox.prefab","TestCmdCategoryBoxUI"),
            new AssetEntry("Assets/Game/Prefabs/UI/Diagnostics/Box/TestCmdEntryBox.prefab","b198bcaac4766e445bdc2ad036e21654","Assets/Game/Prefabs/UI/Common/Box/TestCmdEntryBox.prefab","TestCmdEntryBoxUI")
        };

        private static readonly AssetEntry[] OtherAssets = ParseEntries(@"
Assets/Game/Scripts/Controller/UI/Diagnostics/RuntimeLogUI.cs|6bccda9ff000f29438b8ef0e1aaa1d24|Assets/Game/Scripts/Controller/UI/Common/RuntimeLogUI.cs
Assets/Game/Scripts/Controller/UI/Diagnostics/TestCmdUI.cs|1a2d3f4cd2c8eed4ca49105a566f78f6|Assets/Game/Scripts/Controller/UI/Common/TestCmdUI.cs
Assets/Game/Scripts/Controller/UI/Diagnostics/TestCmdButtonUI.cs|3609bfa8d3bef1b41a14dea427eca73b|Assets/Game/Scripts/Controller/UI/Common/TestCmdButtonUI.cs
Assets/Game/Scripts/Controller/UI/Diagnostics/Box/RuntimeLogItemBoxUI.cs|ffe9984e15e08d94da61485dc9af1197|Assets/Game/Scripts/Controller/UI/Common/Box/RuntimeLogItemBoxUI.cs
Assets/Game/Scripts/Controller/UI/Diagnostics/Box/TestCmdCategoryBoxUI.cs|9490759637b7e1a4f991e0fddacba7f5|Assets/Game/Scripts/Controller/UI/Common/Box/TestCmdCategoryBoxUI.cs
Assets/Game/Scripts/Controller/UI/Diagnostics/Box/TestCmdEntryBoxUI.cs|b51e1ed8eef9f284eb8a61b469836ff6|Assets/Game/Scripts/Controller/UI/Common/Box/TestCmdEntryBoxUI.cs
Assets/Game/Scripts/Controller/UI/Home/RedPointView.cs|d1681b0156a6c4a4a80cccda959bb169|Assets/Game/Scripts/Controller/UI/Common/RedPointView.cs
Assets/Game/Scripts/Controller/UICtrl/Diagnostics/RuntimeLogUICtrl.cs|fe1ce3f15e6953f418a89bfee7905734|Assets/Game/Scripts/Controller/UICtrl/Common/RuntimeLogUICtrl.cs
Assets/Game/Scripts/Controller/UICtrl/Diagnostics/TestCmdUICtrl.cs|1038abc2e6731ea4d83c7f415cc4bcf2|Assets/Game/Scripts/Controller/UICtrl/Common/TestCmdUICtrl.cs
Assets/Game/Scripts/Controller/UICtrl/Diagnostics/TestCmdButtonUICtrl.cs|f0be32e26dd46a4478cd3c23e4aa3c2a|Assets/Game/Scripts/Controller/UICtrl/Common/TestCmdButtonUICtrl.cs");

        [Test]
        public void RemainingMigrationAssets_KeepOriginalGuidsAndRemoveOldPaths()
        {
            foreach (var asset in Prefabs.Concat(OtherAssets))
                AssertAsset(asset);
            Assert.That(Prefabs.Length + OtherAssets.Length, Is.EqualTo(16));
        }

        [Test]
        public void RemainingPrefabs_HaveExactRootCollectorAndGeneratedViewBindings()
        {
            foreach (var asset in Prefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset.Path);
                var collectors = prefab.GetComponents<ReferenceCollector>();
                Assert.That(collectors, Has.Length.EqualTo(1), asset.Path + ": root collector");
                var views = prefab.GetComponents<MonoBehaviour>().Where(item => item.GetType().Name == asset.View).ToArray();
                Assert.That(views, Has.Length.EqualTo(1), asset.Path + ": generated view");
                var fields = views[0].GetType().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                IEnumerable<ReferenceCollectorData> data = collectors[0].data ?? new List<ReferenceCollectorData>();
                Assert.That(data.Select(item => item.key), Is.Unique, asset.Path + ": duplicate collector key");
                Assert.That(data.Select(item => item.key), Is.EquivalentTo(fields.Select(item => item.Name)), asset.Path + ": key/field set");
                foreach (var field in fields)
                {
                    var item = data.Single(candidate => candidate.key == field.Name);
                    var source = item.gameObject;
                    Assert.That(source, Is.Not.Null, asset.Path + ": " + field.Name + " reference");
                    var expected = CollectorTarget(source, field.FieldType, asset.Path, field.Name);
                    Assert.That(
                        item.TypeName == field.FieldType.FullName || item.TypeName == expected.GetType().FullName,
                        Is.True,
                        asset.Path + ": " + field.Name + " type");
                    Assert.That(field.GetValue(views[0]), Is.SameAs(expected), asset.Path + ": " + field.Name + " binding");
                }
            }
            var button = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Diagnostics/TestCmdButton.prefab");
            Assert.That(button.GetComponents<TestCmdButtonPointerRelay>(), Has.Length.EqualTo(1));
            Assert.That(button.GetComponent<ReferenceCollector>().data.Select(item => item.key), Is.EquivalentTo(new[] { "Button", "TextTitle" }));
        }

        [Test]
        public void RuntimePrefabSources_RemainCollectedAndValid()
        {
            var utility = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Game.Editor.YooAssetBuildUtility", false))
                .FirstOrDefault(type => type != null);
            Assert.That(utility, Is.Not.Null);
            var method = utility.GetMethod("ValidateRequiredRuntimePrefabSources",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments = { null };
            Assert.That((bool)method.Invoke(null, arguments), Is.True, arguments[0] as string);
            Assert.That(arguments[0], Is.Null.Or.Empty);
        }

        private static void AssertAsset(AssetEntry asset)
        {
            Assert.That(AssetDatabase.LoadMainAssetAtPath(asset.Path), Is.Not.Null, asset.Path);
            Assert.That(AssetDatabase.AssetPathToGUID(asset.Path), Is.EqualTo(asset.Guid), asset.Path);
            if (!string.IsNullOrWhiteSpace(asset.OldPath))
                Assert.That(AssetDatabase.LoadMainAssetAtPath(asset.OldPath), Is.Null, asset.OldPath);
        }

        private static UnityEngine.Object CollectorTarget(UnityEngine.Object source, Type type, string path, string field)
        {
            if (!typeof(Component).IsAssignableFrom(type))
                return source as GameObject;
            if (source is Component component)
            {
                Assert.That(type.IsInstanceOfType(component), Is.True, path + ": " + field + " component type");
                Assert.That(component.gameObject.GetComponents(type), Is.EqualTo(new[] { component }), path + ": " + field + " unique component");
                return component;
            }
            var gameObject = source as GameObject;
            Assert.That(gameObject, Is.Not.Null, path + ": " + field + " GameObject source");
            var components = gameObject.GetComponents(type);
            Assert.That(components, Has.Length.EqualTo(1), path + ": " + field + " unique component");
            return components[0];
        }

        private static void AssertDirectFiles(string relativeDirectory, params string[] expected)
        {
            var directory = Path.Combine(Application.dataPath, relativeDirectory);
            Assert.That(Directory.GetFiles(directory).Select(Path.GetFileName), Is.EquivalentTo(expected), relativeDirectory);
        }

        private static AssetEntry[] ParseEntries(string text)
        {
            return ParseLines(text).Select(line => line.Split('|')).Select(item => new AssetEntry(item[0], item[1], item[2])).ToArray();
        }

        private static string[] ParseLines(string text)
        {
            return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
