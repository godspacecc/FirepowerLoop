using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>真实CI作用域只消费临时伪产物，验证独立状态、归档完整性和退出恢复；不调用任何构建。</summary>
    public sealed class CiBuildSessionTests
    {
        private string root, client, source, package, player, installer, beforeState;
        private const string Commit = "1111111111111111111111111111111111111111";
        private static Type EditorType(string name) => Type.GetType("Game.Editor." + name + ", Assembly-CSharp-Editor");
        private static object Invoke(object target, string name, params object[] args)
        {
            try { return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, args); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }
        private string StatePath => (string)EditorType("BuildStateStore").GetProperty("StatePath").GetValue(null);
        [SetUp] public void Setup()
        {
            root = Path.Combine(Path.GetTempPath(), "FirepowerSession-" + Guid.NewGuid().ToString("N"));
            client = Path.Combine(root, "Client"); source = Path.Combine(root, "source");
            Write(client, "Assets/Boot/test.cs", "boot"); Write(client, "Assets/Game/Scenes/Boot.unity", "scene"); Write(client, "Assets/HybridCLRGenerate/link.xml", "<linker/>");
            Write(source, "Aot/mscorlib.dll", "aot"); Write(source, "Dlls/Game.dll.bytes", "game"); Write(source, "Dlls/mscorlib.dll.bytes", "aot"); Write(source, "Dlls/HybridCLRAssemblyList.bytes", "mscorlib.dll");
            package = Path.Combine(source, "v1"); Write(package, "DefaultPackage.version", "v1"); Write(package, "data.bundle", "data");
            installer = Path.Combine(source, "installer"); Write(installer, "Setup.exe", "installer"); beforeState = StatePath;
        }
        [TearDown] public void Cleanup() { Assert.That(StatePath, Is.EqualTo(beforeState)); if (Directory.Exists(root)) Directory.Delete(root, true); }
        private static void Write(string directory, string relative, string value) { string file = Path.Combine(directory, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllText(file, value); }
        private object Open(string platform)
        {
            var request = JsonConvert.DeserializeObject(new JObject { ["ProjectPath"] = client, ["RequestId"] = "test", ["Platform"] = platform, ["BuildType"] = "FirstPackage" }.ToString(), EditorType("CiBuildRequest"));
            var session = Activator.CreateInstance(EditorType("CiBuildSession"), BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { request, (object)Commit }, null);
            string prefix = platform == "Android" ? "LastAndroid" : "Last", target = platform == "Android" ? "Android" : "StandaloneWindows64";
            player = Path.Combine(source, platform == "Android" ? "Player/Game.apk" : "Player/FirepowerLoop.exe"); Write(Path.GetDirectoryName(player), Path.GetFileName(player), "player");
            string boot = (string)EditorType("AndroidBootBuildGuard").GetMethod("ComputeFingerprint").Invoke(null, new object[] { client });
            var state = new JObject
            {
                [prefix + "Host"] = new JObject { ["Version"] = "v1", ["BuildTarget"] = target, ["OutputPackageDirectory"] = package, ["GameDllDirectory"] = Path.Combine(source, "Dlls") },
                [prefix + "AotBaseline"] = new JObject { ["BuildTarget"] = target, ["AotDirectory"] = Path.Combine(source, "Aot"), ["RuntimeListPath"] = Path.Combine(source, "Dlls/HybridCLRAssemblyList.bytes"), ["GameDllDirectory"] = Path.Combine(source, "Dlls") },
                [prefix + "Package"] = new JObject { ["Version"] = "v1", ["BuildTarget"] = target, ["OutputPackageDirectory"] = package, ["BuildKind"] = "SmallHostBase" },
                [prefix + "Player"] = new JObject { ["Version"] = "v1", ["PlayerPath"] = player, ["BootFingerprint"] = boot }
            };
            File.WriteAllText(StatePath, state.ToString()); return session;
        }
        [TestCase("PC")][TestCase("Android")]
        public void CompleteArtifactRemainsAfterOriginalBuildOutputsAreRemoved(string platform)
        {
            var session = Open(platform);
            try
            {
                Assert.That(StatePath, Is.Not.EqualTo(beforeState));
                Invoke(session, "Capture", "v1", package, player, platform == "PC" ? installer : null, "1.0.0", 1);
                string marker = (string)session.GetType().GetProperty("ArtifactPath", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
                Directory.Delete(source, true);
                EditorType("CiBuildArtifacts").GetMethod("Validate").Invoke(null, new object[] { marker });
                var artifact = JObject.Parse(File.ReadAllText(marker));
                Assert.That(Path.IsPathRooted((string)artifact["PackageDirectory"]), Is.False);
                Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(marker), (string)artifact["PlayerPath"])), Is.True);
            }
            finally { ((IDisposable)session).Dispose(); }
        }
        [Test] public void CaptureFailureStillRestoresNormalStateRoute()
        {
            var session = Open("PC");
            try { Assert.Throws<InvalidOperationException>(() => Invoke(session, "Capture", "v1", package, "missing.exe", installer, "1.0.0", 1)); }
            finally { ((IDisposable)session).Dispose(); }
            Assert.That(StatePath, Is.EqualTo(beforeState));
        }
    }
}
