using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>临时文件校验PC宿主身份及不可变AOT，不构建或启动游戏。</summary>
    public class PcHotUpdateBaselineTests
    {
        // 每例独立目录，避免接触正式产物。
        private string root, player, dlls, baseline;
        private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [SetUp] public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "PcBaseline_" + Guid.NewGuid().ToString("N"));
            player = Path.Combine(root, "Player", "FirepowerLoop.exe");
            dlls = Path.Combine(root, "Dlls"); baseline = Path.Combine(root, "Baseline");
            Directory.CreateDirectory(Path.GetDirectoryName(player)); Directory.CreateDirectory(dlls);
            foreach (string file in new[] { "FirepowerLoop.exe", "UnityPlayer.dll", "GameAssembly.dll", "FirepowerLoop_Data/StreamingAssets/windows-release.json" })
            {
                string path = Path.Combine(Path.GetDirectoryName(player), file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "valid");
            }
            File.WriteAllText(Path.Combine(dlls, "Core.dll.bytes"), "aot-v1");
            File.WriteAllText(Path.Combine(dlls, "HybridCLRAssemblyList.bytes"), "Core.dll\n");
            File.WriteAllText(Path.Combine(dlls, "Game.dll.bytes"), "original game");
        }
        [TearDown] public void TearDown() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        private static object Call(string name, params object[] args)
        {
            var type = Type.GetType("Game.Editor.PcHotUpdateBaseline, Assembly-CSharp-Editor", true);
            return type.GetMethod(name, Flags).Invoke(null, args);
        }
        private void Capture() => Call("Capture", baseline, player, dlls, "inputs-v1");
        private void Validate(string inputs = "inputs-v1") => Call("Validate", baseline, player, inputs);

        [Test] public void CopiedAotSurvivesLaterGeneratedOutputChanges()
        {
            Capture(); File.WriteAllText(Path.Combine(dlls, "Core.dll.bytes"), "later");
            Validate(); Assert.That(File.ReadAllText(Path.Combine(baseline, "Aot", "Core.dll")), Is.EqualTo("aot-v1"));
        }
        [TestCase("aot")][TestCase("player")][TestCase("inputs")][TestCase("missing")]
        public void RejectsMismatch(string defect)
        {
            Capture();
            if (defect == "aot") File.WriteAllText(Path.Combine(baseline, "Aot", "Core.dll"), "broken");
            if (defect == "player") File.WriteAllText(player, "wrong");
            if (defect == "missing") File.Delete(Path.Combine(baseline, "baseline.json"));
            Assert.Throws<TargetInvocationException>(() => Validate(defect == "inputs" ? "inputs-v2" : "inputs-v1"));
        }
        [Test] public void CaptureCannotOverwriteExistingBaseline()
        {
            Capture(); Assert.Throws<TargetInvocationException>(() => Capture()); Validate();
        }
        [Test] public void TraversalInAotListIsRejectedBeforeWriting()
        {
            File.WriteAllText(Path.Combine(dlls, "HybridCLRAssemblyList.bytes"), "../Core.dll");
            Assert.Throws<TargetInvocationException>(() => Capture()); Assert.That(Directory.Exists(baseline), Is.False);
        }
        [Test] public void HostEntryNeverRegeneratesOrReplacesHostAot()
        {
            string source = File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath, "Editor/Build/BuildUtility.cs"));
            int start = source.IndexOf("public static void BuildHostCodeAndResourcesBatch()");
            string body = source.Substring(start, source.IndexOf("public static void BuildRecentHostPlayerBatch()", start) - start);
            Assert.That(body, Does.Not.Contain("GenerateAll"));
            Assert.That(body, Does.Contain("PcHotUpdateBaseline.Validate"));
            Assert.That(body, Does.Contain("CaptureRemote"));
            Assert.That(body, Does.Contain("updateAot: false"));
        }

        [TestCase("Assets/Boot/Launcher.cs")]
        [TestCase("Assets/Game/Scenes/Boot.unity")]
        [TestCase("Assets/Plugins/native.dll")]
        [TestCase("ProjectSettings/ProjectSettings.asset")]
        [TestCase("Assets/Game/Game.asmdef")]
        [TestCase("Assets/Game/Runtime/NewAot/Logic.cs")]
        public void HostInputChangesRequireNewPlayer(string changed)
        {
            string project = CreateProject();
            string before = Fingerprint(project);
            File.AppendAllText(Path.Combine(project, changed), "changed");
            Assert.That(Fingerprint(project), Is.Not.EqualTo(before));
        }
        [Test] public void OrdinaryGameLogicAndRemoteArtCanChange()
        {
            string project = CreateProject(); string before = Fingerprint(project);
            File.AppendAllText(Path.Combine(project, "Assets/Game/Runtime/Logic.cs"), "changed");
            File.WriteAllText(Path.Combine(project, "Assets/Game/new-art.asset"), "resource");
            Assert.That(Fingerprint(project), Is.EqualTo(before));
        }
        [Test] public void AddingHostSourceIsDetectedWithoutCompilation()
        {
            string project = CreateProject(); string before = Fingerprint(project);
            File.WriteAllText(Path.Combine(project, "Assets/Boot/New.cs"), "new source");
            Assert.That(Fingerprint(project), Is.Not.EqualTo(before));
        }
        /// <summary>构造最小文件树，仅测试输入选择规则，不导入Unity。</summary>
        private string CreateProject()
        {
            string project = Path.Combine(root, "Project");
            foreach (string file in new[] { "Assets/Boot/Launcher.cs", "Assets/Game/Scenes/Boot.unity", "Assets/Plugins/native.dll",
                "ProjectSettings/ProjectSettings.asset", "Packages/manifest.json", "Assets/Game/Game.asmdef", "Assets/Game/Runtime/Logic.cs",
                "Assets/Game/Runtime/NewAot/Aot.asmdef", "Assets/Game/Runtime/NewAot/Logic.cs" })
            {
                string path = Path.Combine(project, file); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "initial");
            }
            return project;
        }
        private static string Fingerprint(string project) => (string)Call("ComputeInputFingerprint", project, Array.Empty<string>(), "test-unity");

        [Test] public void ExternalReferenceRelocationDoesNotChangeInputs()
        {
            string project = CreateProject();
            string one = Path.Combine(root, "unity-a", "runtime.dll"), two = Path.Combine(root, "unity-b", "runtime.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(one)); Directory.CreateDirectory(Path.GetDirectoryName(two));
            File.WriteAllText(one, "same runtime"); File.WriteAllText(two, "same runtime");
            var first = Call("ComputeInputFingerprint", project, new[] { one }, "test-unity");
            var second = Call("ComputeInputFingerprint", project, new[] { two }, "test-unity");
            Assert.That(first, Is.EqualTo(second));
            File.WriteAllText(two, "changed runtime");
            Assert.That(Call("ComputeInputFingerprint", project, new[] { two }, "test-unity"), Is.Not.EqualTo(first));
        }

        // 仅供读取元数据的PInvoke标记，测试永远不调用它。
        [System.Runtime.InteropServices.DllImport("never-loaded-test-library")]
        private static extern void NativeFixture();

        [TestCase(false)][TestCase(true)]
        public void NativeInteropRequiresUnchangedBaselineDll(bool unchanged)
        {
            string compiled = typeof(PcHotUpdateBaselineTests).Assembly.Location;
            if (unchanged) File.Copy(compiled, Path.Combine(dlls, "Game.dll.bytes"), true);
            Capture();
            if (unchanged) Assert.DoesNotThrow(() => Call("ValidateHotUpdateAssembly", baseline, compiled));
            else Assert.That(Assert.Throws<TargetInvocationException>(() => Call("ValidateHotUpdateAssembly", baseline, compiled)).InnerException.Message,
                Does.Contain("原生调用或回调"));
        }

        [TestCase(false)][TestCase(true)]
        public void ActualAssemblyReferencesCannotExceedInitialRequirements(bool same)
        {
            string original = typeof(YooAsset.ResourcePackage).Assembly.Location;
            string candidate = same ? original : typeof(PcHotUpdateBaselineTests).Assembly.Location;
            if (same) Assert.DoesNotThrow(() => Call("ValidateManagedReferences", original, candidate));
            else Assert.That(Assert.Throws<TargetInvocationException>(() => Call("ValidateManagedReferences", original, candidate)).InnerException.Message,
                Does.Contain("新增外部类型或成员需求"));
        }

        [Test] public void ProjectRelocationKeepsSameInputFingerprint()
        {
            string project = CreateProject(), other = Path.Combine(root, "OtherProject");
            foreach (string source in Directory.GetFiles(project, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(other, Path.GetRelativePath(project, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(source, target);
            }
            Assert.That(Fingerprint(other), Is.EqualTo(Fingerprint(project)));
        }
    }
}
