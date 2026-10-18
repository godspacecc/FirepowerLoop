using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>CI取消和恢复只操作临时文件；注入动作代替构建／发布，不触发Unity Player Build。</summary>
    public sealed class CiCancellationRecoveryTests
    {
        private string root, client, signal, report, artifact;
        private const string Commit = "1111111111111111111111111111111111111111";
        [SetUp] public void Setup()
        {
            root = Path.Combine(Path.GetTempPath(), "FirepowerCancel-" + Guid.NewGuid().ToString("N"));
            client = Path.Combine(root, "Client"); Directory.CreateDirectory(client);
            signal = Path.Combine(root, "cancel.request"); report = Path.Combine(root, "report.json"); artifact = Path.Combine(root, "artifact.json");
            Write("Assets/Game/Dlls/Game.dll.bytes", "original"); Write("ProjectSettings/ProjectSettings.asset", "original-settings");
            Write("Assets/Game/Scripts/keep.cs", "user-code"); Write("Assets/Editor/Build/windows-release-version.json", "reserved-1");
        }
        [TearDown] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        private void Write(string relative, string content) { string path = Path.Combine(client, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, content); }
        private static object Call(string name, string method, params object[] args)
        {
            var type = Type.GetType("Game.Editor." + name + ", Assembly-CSharp-Editor"); Assert.That(type, Is.Not.Null, name);
            try { return type.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, args); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }
        private JObject Request(bool publish = false) => new JObject
        {
            ["SchemaVersion"] = 1, ["ProjectId"] = "FirepowerLoop", ["RequestId"] = "test", ["ProjectPath"] = client,
            ["Platform"] = "PC", ["BuildType"] = "FirstPackage", ["ExpectedCommit"] = Commit,
            ["ValidateOnly"] = false, ["CancellationFile"] = signal, ["Publish"] = publish,
            ["PublishDirectory"] = Path.Combine(root, "published")
        };
        private JObject Run(JObject request, Func<string, string, string> build, Action<string, string> publish = null) =>
            JObject.Parse((string)Call("CiBuildRunner", "Run", request.ToString(), client, Commit, report, build, publish));
        private string Build(string route, string baseline) { File.WriteAllText(artifact, "artifact"); return artifact; }
        private string Journal => (string)Call("CiWorkspaceRecovery", "JournalPath", client);
        private void Interrupt()
        {
            Call("CiWorkspaceRecovery", "Begin", client, "test", Commit);
            Call("CiWorkspaceRecovery", "MarkInterrupted", client, "test", "test interruption");
        }
        [Test] public void CancelBeforeBuildDoesNotRunOrDirtyWorkspace()
        {
            File.WriteAllText(signal, "cancel"); bool called = false;
            var value = Run(Request(), (_, __) => { called = true; return artifact; });
            Assert.That((string)value["Status"], Is.EqualTo("Cancelled")); Assert.That((int)value["ExitCode"], Is.EqualTo(2));
            Assert.That(called, Is.False); Assert.That((bool)value["RecoveryRequired"], Is.False);
        }
        [Test] public void CancelAfterBuildPreservesArtifactAndSkipsPublishing()
        {
            bool published = false;
            var value = Run(Request(true), (route, baseline) => { Build(route, baseline); File.WriteAllText(signal, "cancel"); return artifact; }, (_, __) => published = true);
            Assert.That((string)value["Status"], Is.EqualTo("Cancelled")); Assert.That((string)value["BuildStatus"], Is.EqualTo("Succeeded"));
            Assert.That(File.Exists(artifact), Is.True); Assert.That(published, Is.False); Assert.That((bool)value["RecoveryRequired"], Is.True);
        }
        [Test] public void StageBoundaryStopsBeforeNextLongAction()
        {
            bool second = false;
            var value = Run(Request(), (_, __) =>
            {
                var type = Type.GetType("Game.Editor.BuildStageTiming, Assembly-CSharp-Editor"); var timing = Activator.CreateInstance(type, "test");
                try { type.GetMethod("Run", new[] { typeof(string), typeof(Action) }).Invoke(timing, new object[] { "first", (Action)(() => { Write("Assets/Game/Dlls/Game.dll.bytes", "changed"); File.WriteAllText(signal, "cancel"); }) }); }
                catch (TargetInvocationException error) { throw error.InnerException; }
                second = true; return artifact;
            });
            Assert.That((string)value["Status"], Is.EqualTo("Cancelled")); Assert.That(second, Is.False);
            Assert.That((string)value["Checkpoint"], Does.Contain("first"));
        }
        [Test] public void CancelDuringPublishPreparationNeverExposesTarget()
        {
            string destination = Path.Combine(root, "published");
            var value = Run(Request(true), Build, (_, __) => Call("CiBuildPublisher", "PublishTransaction", destination, (Action<string>)(staging => { File.WriteAllText(Path.Combine(staging, "partial"), "data"); File.WriteAllText(signal, "cancel"); })));
            Assert.That((string)value["Status"], Is.EqualTo("Cancelled")); Assert.That(Directory.Exists(destination), Is.False);
        }
        [Test] public void CancellationAfterPublishCommitReportsActualSuccess()
        {
            string destination = Path.Combine(root, "published");
            var value = Run(Request(true), Build, (_, __) => { Call("CiBuildPublisher", "PublishTransaction", destination, (Action<string>)(staging => File.WriteAllText(Path.Combine(staging, "complete"), "data"))); File.WriteAllText(signal, "cancel"); });
            Assert.That((string)value["Status"], Is.EqualTo("Succeeded")); Assert.That((bool)value["CancellationDeferred"], Is.True);
            Assert.That(File.Exists(Path.Combine(destination, "complete")), Is.True); Assert.That((bool)value["RecoveryRequired"], Is.False);
        }
        [Test] public void InterruptedWorkspaceRejectsNewValidationOrBuild()
        {
            Interrupt(); var request = Request(); request["ValidateOnly"] = true;
            var value = Run(request, (_, __) => throw new Exception("must not build"));
            Assert.That((string)value["Status"], Is.EqualTo("Failed")); Assert.That((bool)value["RecoveryRequired"], Is.True);
        }
        [Test] public void RecoverySurvivesUnityTempRemoval()
        {
            Interrupt(); Write("Assets/Game/Dlls/Game.dll.bytes", "changed");
            string temp = Path.Combine(client, "Temp");
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            Assert.That((bool)Call("CiWorkspaceRecovery", "RequiresRecovery", client), Is.True, "Unity退出清除Temp后必须仍阻止复用");
            Call("CiWorkspaceRecovery", "Restore", client, "test", Commit);
            Assert.That(File.ReadAllText(Path.Combine(client, "Assets/Game/Dlls/Game.dll.bytes")), Is.EqualTo("original"));
        }
        [Test] public void RecoveryProofBindsPreservedLedgerAndOriginalCommit()
        {
            Interrupt(); Write("Assets/Editor/Build/windows-release-version.json", "reserved-2");
            Call("CiWorkspaceRecovery", "Restore", client, "test", Commit);
            string state = (string)Call("CiWorkspaceRecovery", "RecoveryRoot", client);
            var proof = JObject.Parse(File.ReadAllText(Path.Combine(state, "retry.json")));
            Assert.That((string)proof["Client"], Is.EqualTo(client)); Assert.That((string)proof["Commit"], Is.EqualTo(Commit));
            Assert.That((string)proof["LedgerPath"], Is.EqualTo("Client/Assets/Editor/Build/windows-release-version.json"));
            using (var hash = System.Security.Cryptography.SHA256.Create())
                Assert.That((string)proof["LedgerHash"], Is.EqualTo(BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes("reserved-2"))).Replace("-", "").ToLowerInvariant()));
        }
        [Test] public void RecoveryProofCannotBeOverwrittenByReport()
        {
            report = Path.Combine((string)Call("CiWorkspaceRecovery", "RecoveryRoot", client), "retry.json");
            Assert.Throws<ArgumentException>(() => Run(Request(), Build));
            Assert.That(File.Exists(report), Is.False);
        }
        [Test] public void RestoreRevertsOnlyOwnedInputsAndPreservesConsumedVersion()
        {
            Interrupt(); Write("Assets/Game/Dlls/Game.dll.bytes", "changed"); Write("Assets/Game/Dlls/extra.bytes", "new");
            Write("Assets/HybridCLRGenerate/new.cs", "generated"); Write("ProjectSettings/ProjectSettings.asset", "changed-settings");
            Write("Assets/Editor/Build/windows-release-version.json", "reserved-2");
            var recovery = JObject.Parse((string)Call("CiWorkspaceRecovery", "Restore", client, "test", Commit));
            Assert.That(File.ReadAllText(Path.Combine(client, "Assets/Game/Dlls/Game.dll.bytes")), Is.EqualTo("original"));
            Assert.That(File.Exists(Path.Combine(client, "Assets/Game/Dlls/extra.bytes")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(client, "Assets/HybridCLRGenerate")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(client, "ProjectSettings/ProjectSettings.asset")), Is.EqualTo("original-settings"));
            Assert.That(File.ReadAllText(Path.Combine(client, "Assets/Game/Scripts/keep.cs")), Is.EqualTo("user-code"));
            Assert.That(File.ReadAllText(Path.Combine(client, "Assets/Editor/Build/windows-release-version.json")), Is.EqualTo("reserved-2"));
            Assert.That(File.Exists(Journal), Is.False); Assert.That(Directory.Exists((string)recovery["QuarantineDirectory"]), Is.True);
        }
        [Test] public void WrongRequestCannotRestoreAnotherAttempt() { Interrupt(); Assert.Throws<InvalidOperationException>(() => Call("CiWorkspaceRecovery", "Restore", client, "other", Commit)); Assert.That(File.Exists(Journal), Is.True); }
        [Test] public void ChangedCheckoutCannotRestorePreviousCommitInputs()
        {
            Interrupt(); Write("Assets/Game/Dlls/Game.dll.bytes", "current-branch");
            Assert.Throws<InvalidOperationException>(() => Call("CiWorkspaceRecovery", "Restore", client, "test", new string('2', 40)));
            Assert.That(File.ReadAllText(Path.Combine(client, "Assets/Game/Dlls/Game.dll.bytes")), Is.EqualTo("current-branch"));
        }
        [Test] public void ActiveOwnerCannotBeRecovered() { Call("CiWorkspaceRecovery", "Begin", client, "test", Commit); Assert.Throws<InvalidOperationException>(() => Call("CiWorkspaceRecovery", "Restore", client, "test", Commit)); }
        [Test] public void TamperedSnapshotCannotOverwriteCurrentFiles()
        {
            Interrupt(); var journal = JObject.Parse(File.ReadAllText(Journal)); string snapshot = (string)journal["SnapshotDirectory"];
            string file = Directory.GetFiles(Path.Combine(snapshot, "backup"), "Game.dll.bytes", SearchOption.AllDirectories)[0]; File.WriteAllText(file, "tampered");
            Write("Assets/Game/Dlls/Game.dll.bytes", "current");
            Assert.Throws<InvalidOperationException>(() => Call("CiWorkspaceRecovery", "Restore", client, "test", Commit));
            Assert.That(File.ReadAllText(Path.Combine(client, "Assets/Game/Dlls/Game.dll.bytes")), Is.EqualTo("current")); Assert.That(File.Exists(Journal), Is.True);
        }
        [Test] public void DeadProcessMarkerStillBlocksUntilExplicitRecovery()
        {
            Call("CiWorkspaceRecovery", "Begin", client, "test", Commit);
            var journal = JObject.Parse(File.ReadAllText(Journal)); journal["ProcessId"] = int.MaxValue; File.WriteAllText(Journal, journal.ToString());
            var request = Request(); request["ValidateOnly"] = true;
            Assert.That((bool)Run(request, Build)["RecoveryRequired"], Is.True);
            Call("CiWorkspaceRecovery", "Restore", client, "test", Commit); Assert.That(File.Exists(Journal), Is.False);
        }
        [Test] public void RestoreFailureRollsBackPreviousSlotsAndKeepsJournal()
        {
            Interrupt(); Write("Assets/Game/Dlls/Game.dll.bytes", "changed");
            using (var held = new FileStream(Path.Combine(client, "ProjectSettings/ProjectSettings.asset"), FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Throws<IOException>(() => Call("CiWorkspaceRecovery", "Restore", client, "test", Commit));
            Assert.That(File.ReadAllText(Path.Combine(client, "Assets/Game/Dlls/Game.dll.bytes")), Is.EqualTo("changed"));
            Assert.That(File.Exists(Journal), Is.True);
        }
        [Test] public void RecoveryScopeCannotBeForgedOutsideOwnedPaths()
        {
            Interrupt(); var journal = JObject.Parse(File.ReadAllText(Journal)); string manifest = Path.Combine((string)journal["SnapshotDirectory"], "snapshot.json");
            var snapshot = JObject.Parse(File.ReadAllText(manifest)); snapshot["Slots"][0]["Index"] = 99; File.WriteAllText(manifest, snapshot.ToString());
            using (var hash = System.Security.Cryptography.SHA256.Create()) journal["ManifestHash"] = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(manifest))).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(Journal, journal.ToString());
            Assert.Throws<InvalidOperationException>(() => Call("CiWorkspaceRecovery", "Restore", client, "test", Commit));
            Assert.That(File.ReadAllText(Path.Combine(client, "Assets/Game/Scripts/keep.cs")), Is.EqualTo("user-code"));
        }
        [Test] public void InternalJournalCannotBeUsedAsReport() => CheckInternalJournalPath(false);
        [Test] public void InternalJournalCannotBeUsedAsCancellationSignal() => CheckInternalJournalPath(true);
        private void CheckInternalJournalPath(bool signalInsteadOfReport)
        {
            var request = Request(); bool built = false;
            if (signalInsteadOfReport)
            {
                request["CancellationFile"] = Journal;
                Assert.That((string)Run(request, (_, __) => { built = true; return artifact; })["Status"], Is.EqualTo("Failed"));
            }
            else { report = Journal; Assert.Throws<ArgumentException>(() => Run(request, (_, __) => { built = true; return artifact; })); }
            Assert.That(built, Is.False); Assert.That(File.Exists(Journal), Is.False);
        }
    }
}
