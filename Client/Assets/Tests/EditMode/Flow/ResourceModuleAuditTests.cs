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

        [Test]
        public void ReportAuditCountsFirstDownloadBundleOnlyOnce()
        {
            string directory = Path.Combine(Path.GetTempPath(), "FirepowerLoopResourceModuleAudit", Guid.NewGuid().ToString("N"));
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
