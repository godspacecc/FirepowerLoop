using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Boot;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>通过正式设置页入口验证地址保存、即时检查及旧服务器在途结果隔离，不修改正式资源。</summary>
    public sealed class LiveResourceServerTests
    {
        private const string OriginalServer = "http://server-a:8001";
        private const string NewServer = "http://server-b:8001";
        private ResourceUpdateSystem system;
        private SettingNetworkPageCtrl page;
        private SettingNetworkPageUI view;
        private GameObject instance;
        private SavedServerProvider config;
        private VersionProbe probe;
        private RuntimeChecker runtime;
        private ApkChecker apk;
        private WindowsChecker windows;
        private NoticeClient notice;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            LocalizationLanguages.ResetForTests();
            config = new SavedServerProvider();
            probe = new VersionProbe(); runtime = new RuntimeChecker();
            apk = new ApkChecker(); windows = new WindowsChecker(); notice = new NoticeClient();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IResourceServerConfigProvider>(config);
                architecture.RegisterUtility<IRemoteResourceVersionProbe>(probe);
                architecture.RegisterUtility<IResourceUpdateRuntimeChecker>(runtime);
                architecture.RegisterUtility<IApplicationUpdateChecker>(apk);
                architecture.RegisterUtility<IWindowsApplicationUpdateChecker>(windows);
                architecture.RegisterUtility<IResourceUpdateNoticeClient>(notice);
            };
            system = Game.Interface.GetSystem<ResourceUpdateSystem>();
            Initialize(BootResourceSource.HostLatest);
            instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Home/Page/SettingNetwork.prefab"));
            view = instance.GetComponent<SettingNetworkPageUI>();
            page = new SettingNetworkPageCtrl();
            page.SetUI(view);
        }

        [TearDown]
        public void TearDown()
        {
            page?.SetUI(null);
            if (instance != null) Object.DestroyImmediate(instance);
            config.Dispose();
            ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = _ => { };
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
        }

        [Test]
        public void ApplyUsesSavedAddressImmediatelyAndPreservesLoadedVersion()
        {
            Apply("  " + NewServer + "/  ");
            Assert.That(ResourceServerPreference.TryLoad(config.Root, out var saved, out _), Is.True);
            Assert.That(saved, Is.EqualTo(NewServer));
            Assert.That(system.Snapshot.ResourceServer, Is.EqualTo(saved));
            Assert.That(system.Snapshot.CurrentVersion, Is.EqualTo("loaded-1"));
            Assert.That(system.Snapshot.ResourceSource, Is.EqualTo(BootResourceSource.HostLatest));
            Assert.That(system.Snapshot.Status, Is.EqualTo(ResourceUpdateStatus.NotChecked));
        }

        [UnityTest]
        public IEnumerator NewHostChecksResourcesPackagesAndNoticeAtNewServer()
        {
            Apply(NewServer);
            var task = system.CheckWithNoticeAsync(true).AsTask();
            yield return Await(task);
            Assert.That(probe.Server, Is.EqualTo(NewServer));
            Assert.That(runtime.Calls, Is.Zero, "已初始化的资源包仍绑定旧服，不得用它检查新服");
            Assert.That(apk.Server, Is.EqualTo(NewServer));
            Assert.That(windows.Server, Is.EqualTo(NewServer));
            Assert.That(notice.Server, Is.EqualTo(NewServer));
            Assert.That(task.Result.Snapshot.CurrentVersion, Is.EqualTo("loaded-1"));
            Assert.That(task.Result.Snapshot.ResourceSource, Is.EqualTo(BootResourceSource.HostLatest));
            Assert.That(task.Result.Snapshot.Status, Is.EqualTo(ResourceUpdateStatus.UpdateAvailable));
            Assert.That(task.Result.Snapshot.RequiresRestartToUpdate, Is.True);
            Assert.That(task.Result.Snapshot.HasKnownDownloadSize, Is.False);
        }

        [UnityTest]
        public IEnumerator BuiltinFallbackAlsoChecksNewServerWithoutRestart()
        {
            Initialize(BootResourceSource.BuiltinFallback);
            Apply(NewServer);
            yield return Await(system.CheckAsync(true).AsTask());
            Assert.That(probe.Server, Is.EqualTo(NewServer));
            Assert.That(system.Snapshot.ResourceSource, Is.EqualTo(BootResourceSource.BuiltinFallback));
            Assert.That(system.Snapshot.RequiresRestartToUpdate, Is.True);
        }

        [Test]
        public void FailedSaveKeepsActiveServer()
        {
            Apply("not-a-url");
            Assert.That(system.Snapshot.ResourceServer, Is.EqualTo(OriginalServer));
            config.RejectSave = true;
            Apply(NewServer);
            Assert.That(system.Snapshot.ResourceServer, Is.EqualTo(OriginalServer));
        }

        [UnityTest]
        public IEnumerator ResetImmediatelyRestoresDefaultAndOriginalRuntimeChecker()
        {
            Apply(NewServer);
            Assert.That(system.Snapshot.ResourceServer, Is.EqualTo(NewServer));
            InvokePage("ResetResourceServer");
            yield return null;
            Assert.That(system.Snapshot.ResourceServer, Is.EqualTo(OriginalServer));
            Assert.That(ResourceServerPreference.TryLoad(config.Root, out _, out _), Is.False);
            yield return Await(system.CheckAsync(true).AsTask());
            Assert.That(runtime.Calls, Is.EqualTo(1));
            Assert.That(probe.Calls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ResetStillAppliesWhenSettingsAreClosedDuringLoad()
        {
            Apply(NewServer);
            page.Show();
            config.LoadGate = new TaskCompletionSource<ResourceServerConfigResult>();
            InvokePage("ResetResourceServer");
            page.Hide();
            config.LoadGate.SetResult(new ResourceServerConfigResult(true, OriginalServer, ""));
            yield return null;
            Assert.That(system.Snapshot.ResourceServer, Is.EqualTo(OriginalServer));
        }

        [UnityTest]
        public IEnumerator PendingResetCannotUndoLaterApplyOfSameAddress()
        {
            Apply(NewServer);
            config.LoadGate = new TaskCompletionSource<ResourceServerConfigResult>();
            InvokePage("ResetResourceServer");
            Apply(NewServer);
            config.LoadGate.SetResult(new ResourceServerConfigResult(true, OriginalServer, ""));
            yield return null;
            Assert.That(system.Snapshot.ResourceServer, Is.EqualTo(NewServer));
            Assert.That(view.ResourceServerInput.text, Is.EqualTo(NewServer));
        }

        [UnityTest]
        public IEnumerator OldResourceReplyCannotOverwriteNewServerResult()
        {
            runtime.Gate = new TaskCompletionSource<ResourceUpdateCheckResult>();
            var old = system.CheckWithNoticeAsync(true).AsTask();
            Apply(NewServer);
            yield return Await(system.CheckWithNoticeAsync(true).AsTask());
            runtime.Gate.SetResult(ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(
                ResourceUpdateStatus.UpdateAvailable, "loaded-1", "old-target", 3, 99, null)));
            yield return Await(old);
            Assert.That(system.Snapshot.ResourceServer, Is.EqualTo(NewServer));
            Assert.That(system.Snapshot.TargetVersion, Is.EqualTo("new-2"));
            Assert.That(notice.Calls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SwitchingBackRejectsLateProbeFromPreviousVisit()
        {
            Apply(NewServer);
            probe.Gate = new TaskCompletionSource<RemoteResourceVersionProbeResult>();
            var old = system.CheckAsync(true).AsTask();
            Assert.That(probe.Pending, Is.Not.Null, "切换地址后应访问新服版本探测器");
            Apply(OriginalServer);
            Apply(NewServer);
            probe.Gate = null;
            yield return Await(system.CheckAsync(true).AsTask());
            // 第一次访问同一地址的请求即使晚到，也不属于第三次地址上下文。
            probe.Pending.SetResult(RemoteResourceVersionProbeResult.Ok("stale-version"));
            yield return Await(old);
            Assert.That(system.Snapshot.TargetVersion, Is.EqualTo("new-2"));
        }

        [UnityTest]
        public IEnumerator OldApkCandidateIsDiscardedAndKnownFlagsAreCleared()
        {
            apk.Gate = new TaskCompletionSource<ApkUpdateManifest>();
            var old = system.CheckWithNoticeAsync(true).AsTask();
            Apply(NewServer);
            apk.Gate.SetResult(new ApkUpdateManifest { versionName = "stale-apk" });
            yield return Await(old);
            Assert.That(old.Result.ApplicationUpdate, Is.Null);
            Assert.That(Game.Interface.GetModel<ResourceUpdateModel>().HasKnownUpdate, Is.False);
        }

        [UnityTest]
        public IEnumerator OldNoticeIsDiscardedAfterServerSwitch()
        {
            notice.Gate = new TaskCompletionSource<ResourceUpdateNoticeResult>();
            var old = system.CheckWithNoticeAsync(true).AsTask();
            Apply(NewServer);
            notice.Gate.SetResult(ResourceUpdateNoticeResult.Ok(new ResourceUpdateNoticeContent(
                "loaded-1", "stale notice", "", Array.Empty<string>())));
            yield return Await(old);
            Assert.That(old.Result.Notice.HasNotice, Is.False);
        }

        [UnityTest]
        public IEnumerator NewServerFailureKeepsCurrentResourcesAndReportsFailure()
        {
            Apply(NewServer);
            probe.Next = RemoteResourceVersionProbeResult.Fail("unreachable");
            yield return Await(system.CheckAsync(true).AsTask());
            Assert.That(system.Snapshot.Status, Is.EqualTo(ResourceUpdateStatus.CheckFailed));
            Assert.That(system.Snapshot.ResourceServer, Is.EqualTo(NewServer));
            Assert.That(system.Snapshot.CurrentVersion, Is.EqualTo("loaded-1"));
            Assert.That(system.Snapshot.Error, Does.Contain("unreachable"));
        }

        [UnityTest]
        public IEnumerator ReapplyingSameAddressKeepsPendingCheck()
        {
            runtime.Gate = new TaskCompletionSource<ResourceUpdateCheckResult>();
            var first = system.CheckAsync(true).AsTask();
            Apply(OriginalServer + "/");
            var second = system.CheckAsync(true).AsTask();
            Assert.That(runtime.Calls, Is.EqualTo(1));
            runtime.Gate.SetResult(runtime.Next);
            yield return Await(Task.WhenAll(first, second));
            Assert.That(system.Snapshot.Status, Is.EqualTo(ResourceUpdateStatus.NoUpdate));
        }

        private void Initialize(BootResourceSource source) => system.InitializeStartupInfo(
            new BootResourceStartupInfo(source, OriginalServer, "loaded-1", "loaded-1"));
        private void Apply(string server) { view.ResourceServerInput.text = server; InvokePage("ApplyResourceServer"); }
        private void InvokePage(string name) => typeof(SettingNetworkPageCtrl)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(page, null);
        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }
        private static IEnumerator Await(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "检查应在五秒内完成");
            if (task.IsFaulted) throw task.Exception;
            Assert.That(task.IsCanceled, Is.False);
        }

        /// <summary>临时目录使用正式地址持久化实现；不覆盖编辑器或手机的玩家设置。</summary>
        private sealed class SavedServerProvider : IResourceServerConfigProvider, IDisposable
        {
            public readonly string Root = Path.Combine(Path.GetTempPath(), "FirepowerLoopLiveServer", Guid.NewGuid().ToString("N"));
            public bool RejectSave;
            public TaskCompletionSource<ResourceServerConfigResult> LoadGate;
            public Task<ResourceServerConfigResult> LoadAsync(CancellationToken token) => LoadGate?.Task ?? Task.FromResult(
                new ResourceServerConfigResult(true, ResourceServerPreference.TryLoad(Root, out var server, out _) ? server : OriginalServer, ""));
            public bool TrySave(string value, out string server, out string diagnostic)
            {
                if (!RejectSave) return ResourceServerPreference.TrySave(value, Root, out server, out diagnostic);
                server = ""; diagnostic = "disk-error"; return false;
            }
            public bool TryReset(out string diagnostic) => ResourceServerPreference.TryReset(Root, out diagnostic);
            public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
        }
        private sealed class VersionProbe : IRemoteResourceVersionProbe
        {
            public string Server;
            public int Calls;
            public RemoteResourceVersionProbeResult Next = RemoteResourceVersionProbeResult.Ok("new-2");
            public TaskCompletionSource<RemoteResourceVersionProbeResult> Gate, Pending;
            public UniTask<RemoteResourceVersionProbeResult> ProbeAsync(string server, int timeout)
            {
                Calls++; Server = server;
                if (Gate == null) return UniTask.FromResult(Next);
                Pending = Gate; return Gate.Task.AsUniTask();
            }
        }
        private sealed class RuntimeChecker : IResourceUpdateRuntimeChecker
        {
            public int Calls;
            public bool CanCheck => true;
            public TaskCompletionSource<ResourceUpdateCheckResult> Gate;
            public ResourceUpdateCheckResult Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(
                ResourceUpdateStatus.NoUpdate, "loaded-1", "loaded-1", 0, 0, null));
            public UniTask<ResourceUpdateCheckResult> CheckAsync(int timeout, bool versionOnly = false)
            { Calls++; return Gate == null ? UniTask.FromResult(Next) : Gate.Task.AsUniTask(); }
        }
        private sealed class ApkChecker : IApplicationUpdateChecker
        {
            public string Server;
            public TaskCompletionSource<ApkUpdateManifest> Gate;
            public UniTask<ApkUpdateManifest> CheckAsync(string server, CancellationToken token)
            { Server = server; return Gate == null ? UniTask.FromResult<ApkUpdateManifest>(null) : Gate.Task.AsUniTask(); }
        }
        private sealed class WindowsChecker : IWindowsApplicationUpdateChecker
        {
            public string Server;
            public UniTask<WindowsUpdateManifest> CheckAsync(string server, CancellationToken token)
            { Server = server; return UniTask.FromResult<WindowsUpdateManifest>(null); }
        }
        private sealed class NoticeClient : IResourceUpdateNoticeClient
        {
            public string Server;
            public int Calls;
            public TaskCompletionSource<ResourceUpdateNoticeResult> Gate;
            public UniTask<ResourceUpdateNoticeResult> FetchAsync(ResourceUpdateNoticeRequest request)
            { Calls++; Server = request.ResourceServer; return Gate == null ? UniTask.FromResult(ResourceUpdateNoticeResult.NoNotice) : Gate.Task.AsUniTask(); }
        }
    }
}
