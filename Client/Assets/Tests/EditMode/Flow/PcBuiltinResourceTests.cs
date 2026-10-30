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
