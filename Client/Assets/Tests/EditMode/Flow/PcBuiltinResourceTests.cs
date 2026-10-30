using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using YooAsset;

namespace Game.Tests
{
    /// <summary>使用真实YooAsset二进制清单和临时目录，不调用构建。</summary>
    public class PcBuiltinResourceTests
    {
        private string root, source, player, output;
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "PcBuiltin_" + Guid.NewGuid().ToString("N"));
            source = Path.Combine(root, "source");
            player = Path.Combine(root, "game", "FirepowerLoop.exe");
            var utility = Type.GetType("Game.Editor.YooAssetBuildUtility, Assembly-CSharp-Editor", true);
            var configured = (string)utility.GetMethod("GetStreamingAssetsDirectory", Flags).Invoke(null, null);
            string relative = Path.GetRelativePath(UnityEngine.Application.streamingAssetsPath, configured);
            output = Path.Combine(Path.GetDirectoryName(player), "FirepowerLoop_Data", "StreamingAssets", relative);
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(output);
            File.WriteAllText(player, "fixture");
            CreateManifest();
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(output, Path.GetFileName(file)));
        }

        [TearDown]
        public void TearDown() { if (Directory.Exists(root)) Directory.Delete(root, true); }

        [Test]
        public void CompletePlayerMatchesFrozenResources() => Validate(Capture());

        [Test]
        public void MissingPlayerBundleFailsEvenWhenProjectCopyExists()
        {
            var snapshot = Capture();
            File.Delete(Path.Combine(output, "second.bundle"));
            Assert.That(Assert.Throws<TargetInvocationException>(() => Validate(snapshot)).InnerException.Message, Does.Contain("second.bundle"));
        }

        [Test]
        public void SourceMissingOneOfTwoManifestBundlesIsRejected()
        {
            File.Delete(Path.Combine(source, "second.bundle"));
            Assert.That(Assert.Throws<TargetInvocationException>(() => Capture()).InnerException.Message, Does.Contain("second.bundle"));
        }

        [Test]
        public void SameSizeCorruptionCannotPassByChangingSourceAfterCapture()
        {
            var snapshot = Capture();
            File.WriteAllText(Path.Combine(output, "second.bundle"), "xxxxx");
            File.WriteAllText(Path.Combine(source, "second.bundle"), "xxxxx");
            Assert.That(Assert.Throws<TargetInvocationException>(() => Validate(snapshot)).InnerException.Message, Does.Contain("second.bundle"));
        }

        [TestCase("DefaultPackage.version")]
        [TestCase("DefaultPackage_v1.bytes")]
        [TestCase("DefaultPackage_v1.hash")]
        [TestCase("BuildinCatalog.bytes")]
        [TestCase("BuildinCatalog.json")]
        public void PlayerMetadataMustMatchThisBuild(string file)
        {
            var snapshot = Capture();
            File.WriteAllText(Path.Combine(output, file), "wrong");
            Assert.That(Assert.Throws<TargetInvocationException>(() => Validate(snapshot)).InnerException.Message, Does.Contain(file));
        }

        [TestCase("corrupt")]
        [TestCase("missing")]
        [TestCase("version")]
        public void SourceBinaryCatalogMustBeUsableEvenWhenJsonIsCorrect(string defect)
        {
            string path = Path.Combine(source, "BuildinCatalog.bytes");
            if (defect == "corrupt") File.WriteAllText(path, "invalid catalog");
            else
            {
                var assembly = typeof(ResourcePackage).Assembly;
                object catalog = UnityEngine.JsonUtility.FromJson(File.ReadAllText(Path.Combine(source, "BuildinCatalog.json")), assembly.GetType("YooAsset.DefaultBuildinFileCatalog"));
                if (defect == "missing") ((IList)catalog.GetType().GetField("Wrappers", Flags).GetValue(catalog)).RemoveAt(1);
                else Set(catalog, "PackageVersion", "old");
                assembly.GetType("YooAsset.CatalogTools").GetMethod("SerializeToBinary", Flags).Invoke(null, new[] { path, catalog });
            }
            Assert.That(Assert.Throws<TargetInvocationException>(() => Capture()).InnerException.Message, Does.Contain("BuildinCatalog.bytes"));
        }

        [Test]
        public void StartupPlayerAndCompleteRemotePackagePass()
        {
            string builtin = CreateStartupBuiltin();
            // 远程包不要求内置catalog；它只属于Player内置目录。
            File.Delete(Path.Combine(source, "BuildinCatalog.bytes"));
            File.Delete(Path.Combine(source, "BuildinCatalog.json"));
            Validate(CaptureStartup(builtin));
        }

        [Test]
        public void StartupRejectsDifferentValidManifestWithSameVersion()
        {
            string builtin = CreateStartupBuiltin();
            string path = Path.Combine(builtin, "DefaultPackage_v1.bytes");
            var tools = typeof(ResourcePackage).Assembly.GetType("YooAsset.ManifestTools");
            object manifest = tools.GetMethod("DeserializeFromBinary", Flags).Invoke(null,
                new object[] { File.ReadAllBytes(path), null });
            Set(manifest, "PackageNote", "different build with same version");
            tools.GetMethod("SerializeToBinary", Flags).Invoke(null, new[] { path, manifest, null });
            using (var md5 = MD5.Create()) File.WriteAllText(Path.Combine(builtin, "DefaultPackage_v1.hash"),
                BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant());
            var error = Assert.Throws<TargetInvocationException>(() => CaptureStartup(builtin));
            Assert.That(error.InnerException.Message, Does.Contain("启动包清单与完整下载资源不一致"));
        }

        [TestCase("missing-remote")]
        [TestCase("corrupt-remote")]
        [TestCase("catalog")]
        [TestCase("catalog-not-empty")]
        [TestCase("manifest")]
        [TestCase("bundle-in-source")]
        public void StartupCaptureRejectsInvalidInputs(string defect)
        {
            string builtin = CreateStartupBuiltin();
            if (defect == "missing-remote") File.Delete(Path.Combine(source, "second.bundle"));
            if (defect == "corrupt-remote") File.WriteAllText(Path.Combine(source, "second.bundle"), "xxxxx");
            if (defect == "catalog") File.WriteAllText(Path.Combine(builtin, "BuildinCatalog.bytes"), "corrupt");
            if (defect == "catalog-not-empty") File.Copy(Path.Combine(source, "BuildinCatalog.bytes"), Path.Combine(builtin, "BuildinCatalog.bytes"), true);
            if (defect == "manifest") File.WriteAllText(Path.Combine(builtin, "DefaultPackage_v1.bytes"), "wrong");
            if (defect == "bundle-in-source") File.WriteAllText(Path.Combine(builtin, "unexpected.bundle"), "extra");
            var error = Assert.Throws<TargetInvocationException>(() => CaptureStartup(builtin));
            Assert.That(error.InnerException, Is.Not.TypeOf<MissingMethodException>());
        }

        [TestCase("missing-manifest")]
        [TestCase("extra-bundle")]
        [TestCase("nested-bundle")]
        [TestCase("wrong-version")]
        [TestCase("corrupt-catalog")]
        [TestCase("remote-changed")]
        public void StartupValidationRejectsInvalidFinalFiles(string defect)
        {
            string builtin = CreateStartupBuiltin();
            object snapshot = CaptureStartup(builtin);
            if (defect == "missing-manifest") File.Delete(Path.Combine(output, "DefaultPackage_v1.bytes"));
            if (defect == "extra-bundle") File.WriteAllText(Path.Combine(output, "unexpected.bundle"), "extra");
            if (defect == "nested-bundle")
            {
                Directory.CreateDirectory(Path.Combine(output, "nested"));
                File.WriteAllText(Path.Combine(output, "nested", "unexpected.bundle"), "extra");
            }
            if (defect == "wrong-version") File.WriteAllText(Path.Combine(output, "DefaultPackage.version"), "old");
            if (defect == "corrupt-catalog") File.WriteAllText(Path.Combine(output, "BuildinCatalog.bytes"), "corrupt");
            if (defect == "remote-changed") File.WriteAllText(Path.Combine(source, "second.bundle"), "xxxxx");
            Assert.Throws<TargetInvocationException>(() => Validate(snapshot));
        }

        /// <summary>清单由真实序列化器生成；启动包仅有元数据，远程目录保留完整资源。</summary>
        private string CreateStartupBuiltin()
        {
            string builtin = Path.Combine(root, "builtin");
            Directory.CreateDirectory(builtin);
            foreach (string file in Directory.GetFiles(source))
                if (!file.EndsWith(".bundle")) File.Copy(file, Path.Combine(builtin, Path.GetFileName(file)));
            string json = "{\"PackageName\":\"DefaultPackage\",\"PackageVersion\":\"v1\",\"Wrappers\":[]}";
            File.WriteAllText(Path.Combine(builtin, "BuildinCatalog.json"), json);
            var assembly = typeof(ResourcePackage).Assembly;
            object catalog = UnityEngine.JsonUtility.FromJson(json, assembly.GetType("YooAsset.DefaultBuildinFileCatalog"));
            assembly.GetType("YooAsset.CatalogTools").GetMethod("SerializeToBinary", Flags).Invoke(null, new[] { Path.Combine(builtin, "BuildinCatalog.bytes"), catalog });
            foreach (string file in Directory.GetFiles(output)) File.Delete(file);
            foreach (string file in Directory.GetFiles(builtin)) File.Copy(file, Path.Combine(output, Path.GetFileName(file)));
            return builtin;
        }

        private object CaptureStartup(string builtin)
        {
            var method = Type.GetType("Game.Editor.PcBuiltinResourceValidator, Assembly-CSharp-Editor", true).GetMethod("CaptureStartup", Flags);
            Assert.That(method, Is.Not.Null, "缺少启动包实际产物校验入口");
            return method.Invoke(null, new object[] { builtin, source, "v1" });
        }

        [TestCase("valid")]
        [TestCase("missing")]
        [TestCase("corrupt")]
        public void RemotePackageRequiresEveryBundleWithoutBuiltinCatalog(string state)
        {
            File.Delete(Path.Combine(source, "BuildinCatalog.bytes")); File.Delete(Path.Combine(source, "BuildinCatalog.json"));
            if (state == "missing") File.Delete(Path.Combine(source, "second.bundle"));
            if (state == "corrupt") File.WriteAllText(Path.Combine(source, "second.bundle"), "xxxxx");
            var method = Type.GetType("Game.Editor.PcBuiltinResourceValidator, Assembly-CSharp-Editor", true).GetMethod("CaptureRemote", Flags);
            Assert.That(method, Is.Not.Null);
            if (state == "valid") Assert.That(method.Invoke(null, new object[] { source, "v1" }), Is.Not.Null);
            else Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { source, "v1" }));
        }

        private object Capture() => Type.GetType("Game.Editor.PcBuiltinResourceValidator, Assembly-CSharp-Editor", true)
            .GetMethod("Capture", Flags).Invoke(null, new object[] { source, "v1" });
        private void Validate(object snapshot) => snapshot.GetType().GetMethod("ValidatePlayer", Flags).Invoke(snapshot, new object[] { player });
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Flags).SetValue(owner, value);

        /// <summary>由安装的真实序列化器生成两包清单，避免测试复刻解析逻辑。</summary>
        private void CreateManifest()
        {
            var assembly = typeof(ResourcePackage).Assembly;
            var manifest = Activator.CreateInstance(assembly.GetType("YooAsset.PackageManifest"), true);
            Set(manifest, "FileVersion", assembly.GetType("YooAsset.ManifestDefine").GetField("FileVersion", Flags).GetValue(null));
            Set(manifest, "PackageName", "DefaultPackage");
            Set(manifest, "PackageVersion", "v1");
            Set(manifest, "BuildPipeline", "BuiltinBuildPipeline");
            Set(manifest, "OutputNameStyle", 1);
            var bundles = (IList)manifest.GetType().GetField("BundleList", Flags).GetValue(manifest);
            var entries = new System.Collections.Generic.List<string>();
            foreach (string name in new[] { "first.bundle", "second.bundle" })
            {
                File.WriteAllText(Path.Combine(source, name), name == "first.bundle" ? "first" : "valid");
                string hash;
                using (var md5 = MD5.Create()) hash = BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(Path.Combine(source, name)))).Replace("-", "").ToLowerInvariant();
                var bundle = Activator.CreateInstance(assembly.GetType("YooAsset.PackageBundle"), true);
                Set(bundle, "BundleName", name);
                Set(bundle, "FileHash", hash);
                Set(bundle, "FileSize", 5L);
                Set(bundle, "DependBundleIDs", Array.Empty<int>());
                Set(bundle, "Tags", Array.Empty<string>());
                bundles.Add(bundle);
                entries.Add("{\"BundleGUID\":\"" + hash + "\",\"FileName\":\"" + name + "\"}");
            }
            string path = Path.Combine(source, "DefaultPackage_v1.bytes");
            assembly.GetType("YooAsset.ManifestTools").GetMethod("SerializeToBinary", Flags).Invoke(null, new[] { path, manifest, null });
            using (var md5 = MD5.Create()) File.WriteAllText(Path.Combine(source, "DefaultPackage_v1.hash"), BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant());
            File.WriteAllText(Path.Combine(source, "DefaultPackage.version"), "v1");
            string catalogJson = "{\"PackageName\":\"DefaultPackage\",\"PackageVersion\":\"v1\",\"Wrappers\":[" + string.Join(",", entries) + "]}";
            File.WriteAllText(Path.Combine(source, "BuildinCatalog.json"), catalogJson);
            object catalog = UnityEngine.JsonUtility.FromJson(catalogJson, assembly.GetType("YooAsset.DefaultBuildinFileCatalog"));
            assembly.GetType("YooAsset.CatalogTools").GetMethod("SerializeToBinary", Flags).Invoke(null, new[] { Path.Combine(source, "BuildinCatalog.bytes"), catalog });
        }
    }
}
