using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;

namespace Game.Tests
{
    /// <summary>旧 APK 的 Boot 必需地址兼容检查；只读正式资源，不生成或修补产物。</summary>
    public sealed class LegacyBootResourceCompatibilityTests
    {
        private const string PrefabPath = "Assets/Game/Prefabs/UI/Inventory/QuickBarHud.prefab";

        [Test]
        public void RetiredQuickBarRemainsLoadableWithoutRestoringGameplay()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, "旧 APK 的 Boot 仍校验此地址，不能随快捷栏功能一起删除");
            Assert.That(AssetDatabase.AssetPathToGUID(PrefabPath), Is.EqualTo("591390542a88c9f4aa562ee010173b08"));
            Assert.That(prefab.transform.childCount, Is.Zero);
            Assert.That(prefab.GetComponentsInChildren<UnityEngine.MonoBehaviour>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<UnityEngine.Renderer>(true), Is.Empty);
            Assert.That(prefab.activeSelf, Is.False, "兼容资源不应显示界面或参与交互");
        }

        [Test]
        public void LegacyQuickBarAddressIsCollectedForStartup()
        {
            var setting = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/AssetBundleCollectorSetting.asset");
            // 仅执行库的资源收集查询，不调用构建流程，也不生成 Bundle 或 Player。
            var result = setting.GetType().GetMethod("BeginCollect").Invoke(setting, new object[] { "DefaultPackage", true, false });
            var assets = (IEnumerable)result.GetType().GetProperty("CollectAssets").GetValue(result);
            var matches = 0;
            foreach (var asset in assets)
            {
                var type = asset.GetType();
                if ((string)type.GetProperty("Address").GetValue(asset) != "Prefabs/UI/Inventory/QuickBarHud") continue;
                matches++;
                Assert.That((IEnumerable)type.GetProperty("AssetTags").GetValue(asset), Does.Contain("startup"));
            }
            Assert.That(matches, Is.EqualTo(1), "必须以旧 Boot 所需的原地址发布，并纳入启动下载");
        }
    }

    [TestFixture]
    public sealed class ResourceModuleAuditTests
    {
        private Type AuditType => Type.GetType(
            "Game.Editor.ResourceModuleAuditUtility, Assembly-CSharp-Editor",
            throwOnError: true);

        [Test]
        public void CollectorSettingsHaveKnownTagsAndDedicatedSharedFontBundle()
        {
            MethodInfo method = AuditType.GetMethod("TryValidateCollectorTags", BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] arguments = { null };
            bool valid = (bool)method.Invoke(null, arguments);

            Assert.That(valid, Is.True, arguments[0] as string);
            Assert.That(arguments[0], Is.Null);
        }

        [TestCase("Assets/Game/Prefabs/UI/Inventory")]
        [TestCase("Assets/Game/Prefabs/UI/Craft")]
        [TestCase("Assets/Game/Prefabs/UI/Shop")]
        [TestCase("Assets/Game/Art/UI/Items")]
        public void BaseFunctionCollectorIsDownloadedAtStartupAndRemainsInExpedition(string path)
        {
            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/AssetBundleCollectorSetting.asset");
            var serialized = new SerializedObject(settings);
            var packages = serialized.FindProperty("Packages");
            var matches = 0;
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
                        matches++;
                        var tags = (group.FindPropertyRelative("AssetTags").stringValue + ";" +
                                    collector.FindPropertyRelative("AssetTags").stringValue).Split(';');
                        Assert.That(tags, Does.Contain("startup"), "基地同步加载库存 UI 前，启动下载必须覆盖该目录");
                        Assert.That(tags, Does.Contain("expedition"), "保留远征资源下载归属");
                    }
                }
            }
            Assert.That(matches, Is.EqualTo(1));
        }

        /// <summary>从正式地图配置核对所有远征场景，防止新增地图漏收集而阻断整个远征入口。</summary>
        [Test]
        public void EveryExpeditionSceneHasTaggedCollector()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new Luban.ByteBuf(AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>($"Assets/Game/Config/{name}.bytes").bytes)));
            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/AssetBundleCollectorSetting.asset");
            var serialized = new SerializedObject(settings);
            var packages = serialized.FindProperty("Packages");
            var collectedScenes = new HashSet<string>(StringComparer.Ordinal);
            for (var p = 0; p < packages.arraySize; p++)
            {
                var package = packages.GetArrayElementAtIndex(p);
                if (package.FindPropertyRelative("PackageName").stringValue != "DefaultPackage") continue;
                var groups = package.FindPropertyRelative("Groups");
                for (var g = 0; g < groups.arraySize; g++)
                {
                    var group = groups.GetArrayElementAtIndex(g);
                    if (group.FindPropertyRelative("ActiveRuleName").stringValue != "EnableGroup") continue;
                    var collectors = group.FindPropertyRelative("Collectors");
                    for (var c = 0; c < collectors.arraySize; c++)
                    {
                        var collector = collectors.GetArrayElementAtIndex(c);
                        var tags = (group.FindPropertyRelative("AssetTags").stringValue + ";" +
                                    collector.FindPropertyRelative("AssetTags").stringValue).Split(';');
                        if (Array.IndexOf(tags, "expedition") < 0) continue;
                        if (collector.FindPropertyRelative("CollectorType").intValue != 0) continue;
                        if (collector.FindPropertyRelative("AddressRuleName").stringValue != "AddressByGameRelativePathNoExtension") continue;
                        if (collector.FindPropertyRelative("FilterRuleName").stringValue != "CollectAll") continue;
                        var path = collector.FindPropertyRelative("CollectPath").stringValue;
                        if (AssetDatabase.IsValidFolder(path))
                        {
                            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { path }))
                                collectedScenes.Add(AssetDatabase.GUIDToAssetPath(guid));
                        }
                        else if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
                            collectedScenes.Add(path);
                    }
                }
            }

            var maps = MapCatalog.GetExpeditionMaps();
            Assert.That(maps, Is.Not.Empty);
            foreach (var map in maps)
            {
                Assert.That(provider.TryGetMap(map.MapId, out var definition, out var error), Is.True, error);
                Assert.That(definition.SceneAddresses, Is.Not.Empty, map.MapId);
                foreach (var address in definition.SceneAddresses)
                    Assert.That(collectedScenes, Does.Contain("Assets/Game/" + address + ".unity"),
                        $"远征场景缺少 expedition 主资源收集器：{map.MapId} {address}");
            }
        }

        [Test]
        public void ReportAuditCountsFirstDownloadBundleOnlyOnce()
        {
            string directory = Path.Combine(Path.GetTempPath(), "AnotherDayResourceModuleAudit", Guid.NewGuid().ToString("N"));
            string reportPath = Path.Combine(directory, "DefaultPackage_test.report");
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(reportPath,
                    "{\"Summary\":{\"BuildTarget\":13,\"BuildPackageVersion\":\"test\",\"BuildDate\":\"now\"}," +
                    "\"BundleInfos\":[" +
                    "{\"BundleName\":\"startup\",\"FileSize\":100,\"Tags\":[\"startup\"]}," +
                    "{\"BundleName\":\"shared\",\"FileSize\":50,\"Tags\":[\"startup\",\"arcade\"]}," +
                    "{\"BundleName\":\"expedition\",\"FileSize\":200,\"Tags\":[\"expedition\"]}," +
                    "{\"BundleName\":\"untagged\",\"FileSize\":10,\"Tags\":[]}" +
                    "]}");

                MethodInfo method = AuditType.GetMethod("TryAnalyzeReport", BindingFlags.Public | BindingFlags.Static);
                Assert.That(method, Is.Not.Null);
                object[] arguments = { reportPath, null, null };

                bool parsed = (bool)method.Invoke(null, arguments);

                Assert.That(parsed, Is.True, arguments[2] as string);
                object result = arguments[1];
                Assert.That(Read<long>(result, "TotalBytes"), Is.EqualTo(360));
                Assert.That(Read<long>(result, "FirstDownloadBytes"), Is.EqualTo(150));
                Assert.That(((ICollection)Read<object>(result, "UntaggedBundles")).Count, Is.EqualTo(1));
                var tagBytes = (IDictionary)Read<object>(result, "TagBytes");
                Assert.That(tagBytes["startup"], Is.EqualTo(150L));
                Assert.That(tagBytes["arcade"], Is.EqualTo(50L));
                Assert.That(tagBytes["expedition"], Is.EqualTo(200L));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        private static T Read<T>(object instance, string propertyName)
        {
            return (T)instance.GetType().GetProperty(propertyName).GetValue(instance);
        }
    }
}
