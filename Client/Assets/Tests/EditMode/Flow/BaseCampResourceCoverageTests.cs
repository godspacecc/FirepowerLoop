using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>验证首次更新覆盖基地建筑、建造、文明与对话 UI，并阻止采集遗漏；不构建资源、不访问玩家存档。</summary>
    public sealed class BaseCampResourceCoverageTests
    {
        private const string SettingsPath = "Assets/AssetBundleCollectorSetting.asset";
        private static readonly string[] RequiredPaths =
        {
            "Assets/Game/Prefabs/Building",
            "Assets/Game/Prefabs/UI/TemporaryBuilding",
            "Assets/Game/Art/UI/TemporaryBuilding",
            "Assets/Game/Prefabs/UI/Civilisation",
            "Assets/Game/Prefabs/UI/Dialogue"
        };

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void BaseCollectorProvidesStartupDownloadAndRuntimeAddresses(int index)
        {
            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(SettingsPath);
            var serialized = new SerializedObject(settings);
            FindCollector(serialized, RequiredPaths[index], out var group, out var collector);
            Assert.That(collector, Is.Not.Null, "基地运行资源未收录：" + RequiredPaths[index]);
            var tags = (group.FindPropertyRelative("AssetTags").stringValue + ";" +
                        collector.FindPropertyRelative("AssetTags").stringValue).Split(';');
            Assert.That(tags, Does.Contain("startup"), "旧客户端启动更新必须下载基地同步加载的资源");
            Assert.That(tags, Does.Contain("expedition"));
            Assert.That(collector.FindPropertyRelative("AddressRuleName").stringValue,
                Is.EqualTo("AddressByGameRelativePathNoExtension"));
            Assert.That(collector.FindPropertyRelative("FilterRuleName").stringValue, Is.EqualTo("CollectAll"));
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        [TestCase(4, true)]
        public void BuildAuditRejectsMissingBaseCollectorOrStartupTag(int index, bool removeCollector)
        {
            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(SettingsPath);
            var original = EditorJsonUtility.ToJson(settings);
            try
            {
                var serialized = new SerializedObject(settings);
                FindCollector(serialized, RequiredPaths[index], out var group, out var collector);
                Assert.That(collector, Is.Not.Null, "基地运行资源未收录：" + RequiredPaths[index]);
                if (removeCollector)
                    collector.FindPropertyRelative("CollectPath").stringValue = "Assets/Game/UnusedCampResourceTest";
                else
                {
                    // 仅改变内存中的收集配置，finally 恢复；不落盘、不触发打包。
                    RemoveStartup(group.FindPropertyRelative("AssetTags"));
                    RemoveStartup(collector.FindPropertyRelative("AssetTags"));
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var type = Type.GetType("Game.Editor.ResourceModuleAuditUtility, Assembly-CSharp-Editor", true);
                object[] arguments = { null };
                var valid = (bool)type.GetMethod("TryValidateCollectorTags", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, arguments);
                Assert.That(valid, Is.False, "构建前必须拒绝无法随启动下载的基地资源");
                Assert.That(arguments[0] as string, Does.Contain(RequiredPaths[index]));
            }
            finally { EditorJsonUtility.FromJsonOverwrite(original, settings); }
        }

        /// <summary>执行真实收集查询，确保安卓按正式地址同步加载窗口与选项；不生成 Bundle。</summary>
        [Test]
        public void DialoguePrefabsAreCollectedWithRuntimeAddressesForStartup()
        {
            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(SettingsPath);
            var result = settings.GetType().GetMethod("BeginCollect")
                .Invoke(settings, new object[] { "DefaultPackage", true, false });
            var assets = (IEnumerable)result.GetType().GetProperty("CollectAssets").GetValue(result);
            var expected = new[] { "Prefabs/UI/Dialogue/Dialogue", "Prefabs/UI/Dialogue/Box/DialogueChoice" };
            foreach (var address in expected)
            {
                var matches = 0;
                foreach (var asset in assets)
                {
                    var type = asset.GetType();
                    if ((string)type.GetProperty("Address").GetValue(asset) != address) continue;
                    matches++;
                    Assert.That((IEnumerable)type.GetProperty("AssetTags").GetValue(asset), Does.Contain("startup"));
                }
                Assert.That(matches, Is.EqualTo(1), "交谈同步加载所需地址必须唯一收录：" + address);
            }
        }

        /// <summary>基地准备会预加载打造产物；火把掉落物必须可按正式地址收集并随启动下载。</summary>
        [Test]
        public void TorchPickupCollectorSupportsCraftPreloadAndStartupDownload()
        {
            const string path = "Assets/Game/Prefabs/SceneObjects/HandheldTorchPickup.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, "正式火把掉落 Prefab 缺失");
            Assert.That(prefab.GetComponent<ResourcePickupView>(), Is.Not.Null);
            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(SettingsPath);
            var serialized = new SerializedObject(settings);
            FindCollector(serialized, path, out var group, out var collector);
            Assert.That(collector, Is.Not.Null, "打造产物预加载所需火把掉落物未收录：" + path);
            Assert.That(collector.FindPropertyRelative("CollectorGUID").stringValue,
                Is.EqualTo(AssetDatabase.AssetPathToGUID(path)));
            Assert.That(collector.FindPropertyRelative("CollectorType").intValue, Is.Zero,
                "掉落物必须作为可按地址加载的主资源收集");
            Assert.That(collector.FindPropertyRelative("AddressRuleName").stringValue,
                Is.EqualTo("AddressByGameRelativePathNoExtension"));
            Assert.That(collector.FindPropertyRelative("FilterRuleName").stringValue, Is.EqualTo("CollectAll"));
            var tags = (group.FindPropertyRelative("AssetTags").stringValue + ";" +
                        collector.FindPropertyRelative("AssetTags").stringValue).Split(';');
            Assert.That(tags, Does.Contain("startup"));
            Assert.That(tags, Does.Contain("expedition"));
            Assert.That(tags, Does.Contain("online-world"));
        }

        private static void RemoveStartup(SerializedProperty tags)
        {
            tags.stringValue = string.Join(";", tags.stringValue.Split(';').Where(tag => tag != "startup"));
        }

        private static void FindCollector(SerializedObject settings, string path,
            out SerializedProperty matchedGroup, out SerializedProperty matchedCollector)
        {
            matchedGroup = null;
            matchedCollector = null;
            var packages = settings.FindProperty("Packages");
            for (var p = 0; p < packages.arraySize; p++)
            {
                var package = packages.GetArrayElementAtIndex(p);
                if (package.FindPropertyRelative("PackageName").stringValue != "DefaultPackage") continue;
                var groups = package.FindPropertyRelative("Groups");
                for (var g = 0; g < groups.arraySize; g++)
                {
                    var group = groups.GetArrayElementAtIndex(g);
                    var collectors = group.FindPropertyRelative("Collectors");
                    for (var c = 0; c < collectors.arraySize; c++)
                    {
                        var collector = collectors.GetArrayElementAtIndex(c);
                        if (collector.FindPropertyRelative("CollectPath").stringValue != path) continue;
                        Assert.That(matchedCollector, Is.Null, "收集路径不能重复：" + path);
                        matchedGroup = group;
                        matchedCollector = collector;
                    }
                }
            }
        }
    }
}
