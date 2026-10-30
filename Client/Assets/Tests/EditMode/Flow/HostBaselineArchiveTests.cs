using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>宿主基线只用临时伪文件验证哈希、搬迁和拒绝路径，不执行编译、打包或发布。</summary>
    public sealed class HostBaselineArchiveTests
    {
        private string root, source, archive, restored;
        private const string Commit = "1111111111111111111111111111111111111111";
        [SetUp] public void Setup()
        {
            root = Path.Combine(Path.GetTempPath(), "FirepowerBaseline-" + Guid.NewGuid().ToString("N"));
            source = Path.Combine(root, "source"); archive = Path.Combine(root, "archive"); restored = Path.Combine(root, "restored");
            Write("Aot/mscorlib.dll", "aot"); Write("Dlls/Game.dll.bytes", "game");
            Write("Dlls/mscorlib.dll.bytes", "aot"); Write("Dlls/HybridCLRAssemblyList.bytes", "mscorlib.dll");
            Write("Package/DefaultPackage.version", "v1"); Write("Player/FirepowerLoop.apk", "apk");
            Write("Generated/link.xml", "<linker/>");
        }
        [TearDown] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        private void Write(string relative, string value) { string file = Path.Combine(source, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllText(file, value); }
        private static object Call(string method, params object[] args)
        {
            var type = Type.GetType("Game.Editor.HostBaselineArchive, Assembly-CSharp-Editor"); Assert.That(type, Is.Not.Null);
            try { return type.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, args); }
            catch (TargetInvocationException e) { throw e.InnerException; }
        }
        private void Create(string platform = "Android", bool withPcHotBaseline = false)
        {
            var state = new JObject
            {
                ["LastAndroidHost"] = new JObject { ["BuildTarget"] = "Android", ["Version"] = "v1", ["OutputPackageDirectory"] = Path.Combine(source, "Package"), ["GameDllDirectory"] = Path.Combine(source, "Dlls") },
                ["LastAndroidAotBaseline"] = new JObject { ["BuildTarget"] = "Android", ["AotDirectory"] = Path.Combine(source, "Aot"), ["RuntimeListPath"] = Path.Combine(source, "Dlls/HybridCLRAssemblyList.bytes"), ["GameDllDirectory"] = Path.Combine(source, "Dlls") },
                ["LastAndroidPackage"] = new JObject { ["BuildTarget"] = "Android", ["Version"] = "v1", ["BuildKind"] = "SmallHostBase", ["OutputPackageDirectory"] = Path.Combine(source, "Package") },
                ["LastAndroidPlayer"] = new JObject { ["Version"] = "v1", ["PlayerPath"] = Path.Combine(source, "Player/FirepowerLoop.apk"), ["BootFingerprint"] = "boot-v1:test" }
            };
            if (platform == "PC") { foreach (string key in new[] { "Host", "AotBaseline", "Package", "Player" }) { state["Last" + key] = state["LastAndroid" + key]; state.Remove("LastAndroid" + key); if (key != "Player") state["Last" + key]["BuildTarget"] = "StandaloneWindows64"; } }
            if (withPcHotBaseline)
            {
                Write("PcHostBaseline/baseline.json", "fixture-baseline");
                state["PcHotUpdateBaselineDirectory"] = Path.Combine(source, "PcHostBaseline");
            }
            Call("Create", archive, platform, Commit, "2022.3.test", "boot-v1:test", state.ToString(), Path.Combine(source, "Generated"));
        }
        private object Validate(string platform = "Android", string unity = "2022.3.test", string boot = "boot-v1:test") => Call("Validate", archive, platform, unity, boot);
        [Test] public void PcHotBaselinePathIsPreservedAndRebased()
        {
            Create("PC", true); Directory.Delete(source, true);
            var state = JObject.Parse((string)Call("Restore", archive, restored, "PC", "2022.3.test", "boot-v1:test"));
            Assert.That((string)state["PcHotUpdateBaselineDirectory"], Is.EqualTo(Path.Combine(restored, "PcHostBaseline")));
            Assert.That(File.ReadAllText(Path.Combine(restored, "PcHostBaseline/baseline.json")), Is.EqualTo("fixture-baseline"));
        }
        [TestCase("Android")][TestCase("PC")] public void ArchiveRestoresIntoCleanDirectoryAfterSourceIsRemoved(string platform)
        {
            Create(platform); Directory.Delete(source, true); Validate(platform);
            var state = JObject.Parse((string)Call("Restore", archive, restored, platform, "2022.3.test", "boot-v1:test"));
            string prefix = platform == "Android" ? "LastAndroid" : "Last";
            string aot = (string)state[prefix + "AotBaseline"]["AotDirectory"];
            Assert.That(aot, Does.StartWith(restored)); Assert.That(File.ReadAllText(Path.Combine(aot, "mscorlib.dll")), Is.EqualTo("aot"));
            Assert.That(File.Exists((string)state[prefix + "Player"]["PlayerPath"]), Is.True);
            Assert.That(state[platform == "Android" ? "LastHost" : "LastAndroidHost"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That(File.Exists(Path.Combine(restored, "Generated/link.xml")), Is.True);
        }
        [Test] public void SameLengthTamperingFailsHashBeforeRestore()
        {
            Create(); File.WriteAllText(Path.Combine(archive, "Aot/mscorlib.dll"), "bad");
            Assert.Throws<InvalidOperationException>(() => Validate()); Assert.That(Directory.Exists(restored), Is.False);
        }
        [Test] public void ExtraUnlistedFilesAreRejected() { Create(); File.WriteAllText(Path.Combine(archive, "extra.dll"), "bad"); Assert.Throws<InvalidOperationException>(() => Validate()); }
        [TestCase("PC", "2022.3.test", "boot-v1:test")]
        [TestCase("Android", "other", "boot-v1:test")]
        [TestCase("Android", "2022.3.test", "boot-v1:changed")]
        public void IncompatibleIdentityCannotRestore(string platform, string unity, string boot) { Create(); Assert.Throws<InvalidOperationException>(() => Validate(platform, unity, boot)); }
        [Test] public void ManifestCannotEscapeArchiveRoot()
        {
            Create(); string file = Path.Combine(archive, "baseline.json"); var json = JObject.Parse(File.ReadAllText(file));
            json["Files"][0]["Path"] = "../escape.dll"; File.WriteAllText(file, json.ToString());
            Assert.Throws<InvalidOperationException>(() => Validate());
        }
        [Test] public void ArchiveCannotOverwritePreviousIdentity() { Create(); Assert.Throws<IOException>(() => Create()); Validate(); }
        [Test] public void RestoreCannotOverwriteExistingDirectory()
        {
            Create(); Directory.CreateDirectory(restored); File.WriteAllText(Path.Combine(restored, "keep"), "keep");
            Assert.Throws<IOException>(() => Call("Restore", archive, restored, "Android", "2022.3.test", "boot-v1:test"));
            Assert.That(File.ReadAllText(Path.Combine(restored, "keep")), Is.EqualTo("keep"));
        }
        [Test] public void MissingRequiredAotCannotCreateSuccessfulArchive()
        {
            File.Delete(Path.Combine(source, "Aot/mscorlib.dll")); Assert.Throws<InvalidOperationException>(() => Create());
            Assert.That(File.Exists(Path.Combine(archive, "baseline.json")), Is.False);
        }
    }
}
