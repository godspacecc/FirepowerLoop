using System;
using System.Collections;
using System.Reflection;
using Boot;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YooAsset;

namespace Game.Tests.Flow
{
    /// <summary>使用真实 YooAsset 清单和内置文件系统，验证首包回退边界；不读取玩家缓存或联网。</summary>
    public sealed class BootResourceFallbackTests
    {
        private GameObject _root;
        private ResourceBoot _boot;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("BootResourceFallbackTests");
            _boot = _root.AddComponent<ResourceBoot>();
            Set(_boot, "_actualPlayMode", ResourcePlayMode.OfflinePlayMode);
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_root);

        [Test]
        public void ManifestOnlyBuiltinFailsBeforeLoadingAotList()
        {
            Set(_boot, "_package", CreatePackage(-1));
            LogAssert.Expect(LogType.Error, "Can not found belong file system : fixture_0.bundle");
            var result = Validate();
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure.Code, Is.EqualTo(BootFailureCode.BuiltinResourceInvalid));
            Assert.That(result.Failure.Stage, Is.EqualTo(BootFailureStage.ResourceValidation));
        }

        [Test]
        public void MissingStartupDependencyRejectsBuiltinFallback()
        {
            Set(_boot, "_package", CreatePackage(5));
            LogAssert.Expect(LogType.Error, "Can not found belong file system : fixture_5.bundle");
            var result = Validate();
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure.Code, Is.EqualTo(BootFailureCode.BuiltinResourceInvalid));
        }

        [Test]
        public void CompleteStartupAllowsOptionalBundleToRemainAbsent()
        {
            Set(_boot, "_package", CreatePackage(4));
            Assert.That(Validate().Succeeded, Is.True);
        }

        [Test]
        public void CompleteBuiltinPassesAvailabilityCheck()
        {
            Set(_boot, "_package", CreatePackage(99));
            Assert.That(Validate().Succeeded, Is.True);
        }

        [TestCase(7, "Bag")]
        [TestCase(8, "Warehouse")]
        [TestCase(9, "SearchContainer")]
        [TestCase(10, "Craft")]
        [TestCase(11, "Shop")]
        public void MissingInventoryBundleRejectsStartupEvenWithExpeditionTag(int bundleId, string resourceName)
        {
            Set(_boot, "_package", CreatePackage(bundleId));
            LogAssert.Expect(LogType.Error, $"Can not found belong file system : fixture_{bundleId}.bundle");
            var result = Validate();
            Assert.That(result.Succeeded, Is.False, "基地立即使用的库存 UI 不能推迟到远征下载");
            Assert.That(result.Failure.Code, Is.EqualTo(BootFailureCode.BuiltinResourceInvalid));
            Assert.That(result.Failure.DiagnosticDetails, Does.Contain("resourceAddress: " + resourceName));
        }

        [Test]
        public void MissingInventoryAddressRejectsStartupBeforeUiLoad()
        {
            Set(_boot, "_package", CreatePackage(99, omittedAddress: "Prefabs/UI/Inventory/Bag"));
            var result = Validate();
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure.Code, Is.EqualTo(BootFailureCode.RequiredResourceMissing));
            Assert.That(result.Failure.DiagnosticDetails, Does.Contain("resourceAddress: Bag"));
        }

        [Test]
        public void MissingTaggedStartupAssetRejectsBuiltinFallback()
        {
            Set(_boot, "_package", CreatePackage(4, "startup"));
            LogAssert.Expect(LogType.Error, "Can not found belong file system : fixture_4.bundle");
            var result = Validate();
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure.Code, Is.EqualTo(BootFailureCode.BuiltinResourceInvalid));
            Assert.That(result.Failure.DiagnosticDetails, Does.Contain("OptionalMap"));
        }

        [TestCase(BootFailureCode.PackageVersionRequestFailed, BootFailureStage.ResourceVersion)]
        [TestCase(BootFailureCode.PackageManifestUpdateFailed, BootFailureStage.ResourceManifest)]
        [TestCase(BootFailureCode.ResourceDownloadFailed, BootFailureStage.ResourceDownload)]
        public void UnusableBuiltinPreservesOriginalHostFailureAndRetry(
            BootFailureCode code, BootFailureStage stage)
        {
            var host = BootOperationResult.Failed(BootFailure.Create(code, stage,
                BootRetryScope.ResourceInitialization));
            var builtin = BootOperationResult.Failed(BootFailure.Create(
                BootFailureCode.BuiltinResourceInvalid, BootFailureStage.ResourceValidation));
            var result = Resolve(builtin, host);
            Assert.That(result, Is.SameAs(host));
            Assert.That(result.Failure.RetryScope, Is.EqualTo(BootRetryScope.ResourceInitialization));
            Assert.That(result.Failure.Allows(BootFailureAction.Retry), Is.True);
        }

        [Test]
        public void UsableBuiltinWinsOverHostFailure()
        {
            var builtin = BootOperationResult.Success();
            var host = BootOperationResult.Failed(BootFailure.Create(
                BootFailureCode.PackageVersionRequestFailed, BootFailureStage.ResourceVersion));
            Assert.That(Resolve(builtin, host), Is.SameAs(builtin));
        }

        private BootOperationResult Validate() => Invoke("ValidateStartupResources");
        private BootOperationResult Resolve(BootOperationResult builtin, BootOperationResult host)
            => Invoke("ResolveHostFallbackResult", builtin, host);

        private BootOperationResult Invoke(string name, params object[] arguments)
        {
            var method = typeof(ResourceBoot).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"启动流程缺少 {name}");
            return (BootOperationResult)method.Invoke(_boot, arguments);
        }

        // 仅组装真实库对象的内存状态，不模拟 Belong 或依赖查询，避免把库的缺包行为替换掉。
        private static ResourcePackage CreatePackage(int missingBundle, string extraTag = "optional", string omittedAddress = null)
        {
            var assembly = typeof(ResourcePackage).Assembly;
            object manifest = Activator.CreateInstance(assembly.GetType("YooAsset.PackageManifest"), true);
            Set(manifest, "PackageName", "BootFallbackFixture");
            var assets = (IList)Get(manifest, "AssetList");
            var bundles = (IList)Get(manifest, "BundleList");
            var assetDic = NewDictionary(manifest, "AssetDic");
            Set(manifest, "AssetPathMapping1", new System.Collections.Generic.Dictionary<string, string>());
            var mapping = (IDictionary)Get(manifest, "AssetPathMapping1");
            string[] addresses = { "Scenes/Main", "tbitem.bytes", "Game.dll.bytes",
                "HybridCLRAssemblyList.bytes", "OptionalMap", null,
                null, "Prefabs/UI/Inventory/Bag",
                "Prefabs/UI/Inventory/Warehouse", "Prefabs/UI/Inventory/SearchContainer",
                "Prefabs/UI/Craft/Craft", "Prefabs/UI/Shop/Shop" };
            for (int i = 0; i < addresses.Length; i++)
            {
                object bundle = Activator.CreateInstance(assembly.GetType("YooAsset.PackageBundle"), true);
                Set(bundle, "BundleName", $"fixture_{i}.bundle");
                Set(bundle, "FileHash", $"hash-{i}");
                Set(bundle, "DependBundleIDs", Array.Empty<int>());
                bundles.Add(bundle);
                if (addresses[i] == null || addresses[i] == omittedAddress) continue;
                object asset = Activator.CreateInstance(assembly.GetType("YooAsset.PackageAsset"), true);
                Set(asset, "Address", addresses[i]);
                Set(asset, "AssetPath", $"Assets/Fixture/{i}");
                Set(asset, "AssetTags", new[] { i >= 6 ? "expedition" : i == 4 ? extraTag : "startup" });
                Set(asset, "BundleID", i);
                Set(asset, "DependBundleIDs", i == 0 ? new[] { 5 } : Array.Empty<int>());
                assets.Add(asset);
                assetDic.Add($"Assets/Fixture/{i}", asset);
                mapping.Add(addresses[i], $"Assets/Fixture/{i}");
            }
            object mode = Activator.CreateInstance(assembly.GetType("YooAsset.PlayModeImpl"),
                new object[] { "BootFallbackFixture", EPlayMode.OfflinePlayMode });
            mode.GetType().GetProperty("ActiveManifest").SetValue(mode, manifest);
            object fileSystem = Activator.CreateInstance(assembly.GetType("YooAsset.DefaultBuildinFileSystem"), true);
            var wrappers = (IDictionary)Get(fileSystem, "_wrappers");
            for (int i = 0; i < addresses.Length; i++)
            {
                if (missingBundle == -1 || i == missingBundle) continue;
                object wrapper = Activator.CreateInstance(
                    fileSystem.GetType().GetNestedType("FileWrapper"), new object[] { $"fixture_{i}.bundle" });
                wrappers.Add($"hash-{i}", wrapper);
            }
            ((IList)Get(mode, "FileSystems")).Add(fileSystem);
            var package = (ResourcePackage)Activator.CreateInstance(typeof(ResourcePackage),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { "BootFallbackFixture" }, null);
            Set(package, "_initializeStatus", EOperationStatus.Succeed);
            Set(package, "_playModeImpl", mode);
            Set(package, "_bundleQuery", mode);
            return package;
        }

        private static IDictionary NewDictionary(object owner, string name)
        {
            var field = Field(owner, name);
            var dictionary = (IDictionary)Activator.CreateInstance(field.FieldType);
            field.SetValue(owner, dictionary);
            return dictionary;
        }
        private static FieldInfo Field(object owner, string name) => owner.GetType().GetField(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static object Get(object owner, string name) => Field(owner, name).GetValue(owner);
        private static void Set(object owner, string name, object value) => Field(owner, name).SetValue(owner, value);
    }
}
