using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>CI适配只操作临时目录与注入文件动作，禁止测试调用Unity构建或真实发布。</summary>
    public sealed class BuildCiTests
    {
        private string root, client, report;
        private const string Commit = "1111111111111111111111111111111111111111";
        [SetUp] public void Setup()
        {
            root = Path.Combine(Path.GetTempPath(), "FirepowerCi-" + Guid.NewGuid().ToString("N"));
            client = Path.Combine(root, "Client"); Directory.CreateDirectory(client);
            report = Path.Combine(root, "report.json");
        }
        [TearDown] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        private static object Call(string type, string method, params object[] args)
        {
            var target = Type.GetType("Game.Editor." + type + ", Assembly-CSharp-Editor");
            Assert.That(target, Is.Not.Null, type);
            try { return target.GetMethod(method, BindingFlags.Static | BindingFlags.Public).Invoke(null, args); }
            catch (TargetInvocationException e) { throw e.InnerException; }
        }
        private JObject Request(string platform = "PC", string kind = "FirstPackage") => new JObject
        {
            ["SchemaVersion"] = 1, ["ProjectId"] = "FirepowerLoop", ["RequestId"] = "test",
            ["ProjectPath"] = client, ["Platform"] = platform, ["BuildType"] = kind,
            ["ExpectedCommit"] = Commit, ["ValidateOnly"] = true
        };
        private JObject Run(JObject request, Func<string, string, string> build = null, Action<string, string> publish = null) =>
            JObject.Parse((string)Call("CiBuildRunner", "Run", request.ToString(), client, Commit, report, build, publish));

        [TestCase("PC", "FirstPackage", "PcFirst")]
        [TestCase("PC", "HostUpdate", "PcHost")]
        [TestCase("Android", "FirstPackage", "AndroidFirst")]
        [TestCase("Android", "HostUpdate", "AndroidHost")]
        public void ValidateOnlyRoutesNeverExecuteBuildOrPublish(string platform, string kind, string route)
        {
            var request = Request(platform, kind);
            if (kind == "HostUpdate") { var dir = Path.Combine(root, "baseline"); Directory.CreateDirectory(dir); request["BaselineDirectory"] = dir; }
            var value = Run(request, (_, __) => throw new Exception("不应执行构建"), (_, __) => throw new Exception("不应发布"));
            Assert.That((string)value["Status"], Is.EqualTo("Validated"));
            Assert.That((string)value["Route"], Is.EqualTo(route));
            Assert.That(File.Exists(report), Is.True);
            Assert.That((string)JObject.Parse(File.ReadAllText(report))["BuildStatus"], Is.EqualTo("NotStarted"));
        }

        [TestCase("Linux", "FirstPackage")]
        [TestCase("Android", "Unknown")]
        public void UnsupportedRouteFailsBeforeSideEffects(string platform, string kind)
        {
            var value = Run(Request(platform, kind));
            Assert.That((string)value["Status"], Is.EqualTo("Failed"));
            Assert.That((string)value["BuildStatus"], Is.EqualTo("NotStarted"));
        }

        [Test] public void UnknownConfigurationFieldDoesNotSilentlyChangeWorkflow()
        {
            var request = Request(); request["Publsih"] = true;
            Assert.That((string)Run(request)["Status"], Is.EqualTo("Failed"));
        }
        [Test] public void WrongProjectCannotUseThisBuildAdapter()
        {
            var request = Request(); request["ProjectId"] = "3DBase";
            Assert.That((string)Run(request)["Status"], Is.EqualTo("Failed"));
        }
        [Test] public void HostUpdateRequiresExplicitBaselineInsteadOfLatestState()
        {
            Assert.That((string)Run(Request("Android", "HostUpdate"))["Status"], Is.EqualTo("Failed"));
        }
        [Test] public void CommitMismatchDoesNotStartBuild()
        {
            var request = Request(); request["ExpectedCommit"] = new string('2', 40);
            Assert.That((string)Run(request)["Status"], Is.EqualTo("Failed"));
        }
        [Test] public void BuildDefaultsToNoPublishAndReportsActualArtifact()
        {
            var request = Request(); request["ValidateOnly"] = false;
            string artifact = Path.Combine(root, "artifact.json");
            var value = Run(request, (route, baseline) => { File.WriteAllText(artifact, route); return artifact; },
                (_, __) => throw new Exception("默认禁止发布"));
            Assert.That(File.ReadAllText(artifact), Is.EqualTo("PcFirst"));
            Assert.That((string)value["Status"], Is.EqualTo("Succeeded"));
            Assert.That((string)value["PublishStatus"], Is.EqualTo("NotRequested"));
            Assert.That((string)value["ArtifactPath"], Is.EqualTo(artifact));
        }
        [Test] public void BuildFailureIsReportedAndWorkspaceLockIsReleased()
        {
            var request = Request(); request["ValidateOnly"] = false;
            var value = Run(request, (_, __) => throw new IOException("build failed"));
            Assert.That((string)value["Status"], Is.EqualTo("Failed"));
            Assert.That((int)value["ExitCode"], Is.EqualTo(1));
            Assert.That((string)value["Error"], Does.Contain("build failed"));
            using ((IDisposable)Call("WorkspaceBuildLock", "Acquire", client)) { }
        }
        [Test] public void PublishFailureRetainsBuiltArtifactAndSeparatesFailureStage()
        {
            var request = Request(); request["ValidateOnly"] = false; request["Publish"] = true;
            request["PublishDirectory"] = Path.Combine(root, "published");
            string artifact = Path.Combine(root, "artifact.json");
            var value = Run(request, (_, __) => { File.WriteAllText(artifact, "retained"); return artifact; },
                (_, __) => throw new IOException("publish failed"));
            Assert.That(File.ReadAllText(artifact), Is.EqualTo("retained"));
            Assert.That((string)value["Status"], Is.EqualTo("Failed"));
            Assert.That((string)value["BuildStatus"], Is.EqualTo("Succeeded"));
            Assert.That((string)value["PublishStatus"], Is.EqualTo("Failed"));
        }
        [Test] public void MissingArtifactCannotAdvanceToPublish()
        {
            var request = Request(); request["ValidateOnly"] = false;
            Assert.That((string)Run(request, (_, __) => Path.Combine(root, "missing"))["Status"], Is.EqualTo("Failed"));
        }
        [Test] public void WorkspaceLockSupportsNestedOwnerButBlocksOtherThreads()
        {
            using ((IDisposable)Call("WorkspaceBuildLock", "Acquire", client))
            {
                using ((IDisposable)Call("WorkspaceBuildLock", "Acquire", client)) { }
                bool rejected = Task.Run(() => { try { using ((IDisposable)Call("WorkspaceBuildLock", "Acquire", client)) { } return false; } catch (IOException) { return true; } }).Result;
                Assert.That(rejected, Is.True);
            }
            using ((IDisposable)Call("WorkspaceBuildLock", "Acquire", client)) { }
        }
        [Test] public void ReportCannotOverwriteProjectAssets()
        {
            report = Path.Combine(client, "Assets", "result.json");
            Assert.Throws<ArgumentException>(() => Run(Request()));
            Assert.That(File.Exists(report), Is.False);
        }
    }
}
