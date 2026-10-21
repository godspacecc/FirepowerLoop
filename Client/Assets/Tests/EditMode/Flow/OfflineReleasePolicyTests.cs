using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Boot;
using Cysharp.Threading.Tasks;
using Game.Network;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>纯离线模式的真实调用边界；诊断、更新和连接均使用隔离端口，不访问业务服务器。</summary>
    public sealed class OfflineReleasePolicyTests
    {
        private string root;
        [SetUp] public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "FirepowerOffline-" + Guid.NewGuid().ToString("N"));
            DiagnosticReportSender.UseStoreForTests(new DiagnosticReportStore(Path.Combine(root, "queue.json")));
        }
        [TearDown] public void TearDown()
        {
            Type.GetType("Boot.ClientReleasePolicy, Boot")?.GetProperty("OfflineOverrideForTests")?.SetValue(null, null);
            DiagnosticReportSender.UseStoreForTests(null);
            var field = typeof(Architecture<UpdateArchitecture>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
            (field.GetValue(null) as IArchitecture)?.Deinit(); field.SetValue(null, null);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        private static void Profile(bool offline)
        {
            var type = Type.GetType("Boot.ClientReleasePolicy, Boot");
            Assert.That(type, Is.Not.Null, "缺少稳定 Boot 发布模式入口");
            type.GetProperty("OfflineOverrideForTests").SetValue(null, offline);
        }
        private static void Render(Type controller, object view)
        {
            var method = controller.GetMethod("ApplyReleaseVisibility", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null); method.Invoke(null, new[] { view });
        }
        [Test] public void InternalProfileRemainsAvailable()
        {
            Profile(false);
            Assert.That((bool)Type.GetType("Boot.ClientReleasePolicy, Boot").GetProperty("OfflineSinglePlayer").GetValue(null), Is.False);
        }
        [Test] public void OfflineBootDoesNotOwnOrUploadDiagnostics()
        {
            Profile(true);
            DiagnosticReportSender.BeginBoot(); DiagnosticReportSender.RegisterGame();
            DiagnosticReportSender.RecordBoot(BootFailure.Create(BootFailureCode.GameDllMissing, BootFailureStage.GameAssembly, BootRetryScope.None));
            DiagnosticReportSender.CaptureRuntime("synthetic-error-location", LogType.Error);
            DiagnosticReportSender.TickBoot(); DiagnosticReportSender.TickGame();
            Assert.That(DiagnosticReportSender.RequestUpload(), Is.False);
            Assert.That(DiagnosticReportSender.ActiveDriverForTests, Is.EqualTo("None"));
            Assert.That(DiagnosticReportSender.PendingGroups, Is.Zero);
            Assert.That(DiagnosticReportSender.LastAttemptUtc, Is.EqualTo(DateTime.MinValue));
            Assert.That(File.Exists(Path.Combine(root, "queue.json")), Is.False);
        }
        [Test] public void InternalProfileStillCollectsLocalDiagnosticSummaries()
        {
            Profile(false); DiagnosticReportSender.CaptureRuntime("synthetic-error-location", LogType.Error);
            Assert.That(DiagnosticReportSender.RequestUpload(), Is.False, "隔离测试没有上传地址");
            Assert.That(DiagnosticReportSender.PendingGroups, Is.EqualTo(1));
        }
        [Test] public void OfflineUpdateChecksNeverInvokeRemotePorts()
        {
            Profile(true);
            var architecture = (UpdateArchitecture)UpdateArchitecture.Interface;
            var system = architecture.GetSystem<ResourceUpdateSystem>();
            system.InitializeStartupInfo(new BootResourceStartupInfo(BootResourceSource.BuiltinFallback, "http://127.0.0.1:1", "local-version", ""));
            system.CheckAsync(false).GetAwaiter().GetResult();
            system.CheckWithNoticeAsync(false).GetAwaiter().GetResult();
            system.CheckWithNoticeAsync(true).GetAwaiter().GetResult();
            Assert.That(architecture.Probe.Calls, Is.Zero);
            Assert.That(architecture.Apk.Calls, Is.Zero);
            Assert.That(system.Snapshot.Status, Is.EqualTo(ResourceUpdateStatus.NotChecked));
        }
        [Test] public void OfflineNetworkConnectFailsBeforeTransportIsTouched()
        {
            Profile(true); var transport = new CountingTransport();
            var runtime = new NetworkRuntime(() => new NetworkClock(), () => transport,
                () => new MemoryPackMessageCodec(), codec => new RpcMessageDispatcher(codec));
            try
            {
                runtime.Initialize();
                var task = runtime.ConnectAsync(new NetworkEndpoint("127.0.0.1", 1), 1000, CancellationToken.None);
                Assert.That(transport.Connects, Is.Zero);
                Assert.That(task.IsFaulted, Is.True);
                Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
                Assert.That(runtime.ConnectionState, Is.EqualTo(NetworkConnectionState.Disconnected));
            }
            finally { runtime.Dispose(); }
        }
        [Test] public void OfflineBootAlwaysChoosesBuiltinResources()
        {
            Profile(true); var owner = new GameObject("OfflineBootPolicy");
            try
            {
                var boot = owner.AddComponent<ResourceBoot>();
                typeof(ResourceBoot).GetMethod("DeterminePlayMode", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(boot, null);
                Assert.That(typeof(ResourceBoot).GetField("_actualPlayMode", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(boot), Is.EqualTo(ResourcePlayMode.OfflinePlayMode));
            }
            finally { Object.DestroyImmediate(owner); }
        }
        [Test] public void ActualStartPrefabHidesOnlineAndUpdateEntriesAndRestoresInternalMode()
        {
            Profile(true); var owner = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab"));
            try
            {
                var view = owner.GetComponent<StartUI>(); Render(typeof(StartUICtrl), view);
                Assert.That(view.BtnOnline.gameObject.activeSelf, Is.False);
                Assert.That(view.BtnCheckUpdate.gameObject.activeSelf, Is.False);
                Assert.That(view.UpdateStatusText.gameObject.activeSelf, Is.False);
                Assert.That(view.BtnNewGame.gameObject.activeSelf, Is.True);
                Profile(false); Render(typeof(StartUICtrl), view);
                Assert.That(view.BtnOnline.gameObject.activeSelf, Is.True);
                Assert.That(view.BtnCheckUpdate.gameObject.activeSelf, Is.True);
            }
            finally { Object.DestroyImmediate(owner); }
        }
        [Test] public void ActualSettingsPrefabHidesNetworkTabAndRestoresInternalMode()
        {
            Profile(true); var owner = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Setting.prefab"));
            try
            {
                var view = owner.GetComponent<SettingUI>(); Render(typeof(SettingUICtrl), view);
                Assert.That(view.NetworkTabButton.gameObject.activeSelf, Is.False);
                Assert.That(view.SettingNetworkRoot.activeSelf, Is.False);
                Assert.That(view.GeneralTabButton.gameObject.activeSelf, Is.True);
                Profile(false); Render(typeof(SettingUICtrl), view);
                Assert.That(view.NetworkTabButton.gameObject.activeSelf, Is.True);
            }
            finally { Object.DestroyImmediate(owner); }
        }
        private sealed class UpdateArchitecture : Architecture<UpdateArchitecture>
        {
            public readonly CountingProbe Probe = new CountingProbe();
            public readonly CountingApk Apk = new CountingApk();
            protected override void Init()
            {
                RegisterModel(new ResourceUpdateModel());
                RegisterUtility<IRemoteResourceVersionProbe>(Probe); RegisterUtility<IApplicationUpdateChecker>(Apk);
                RegisterSystem(new ResourceUpdateSystem());
            }
        }
        private sealed class CountingProbe : IRemoteResourceVersionProbe
        {
            public int Calls;
            public UniTask<RemoteResourceVersionProbeResult> ProbeAsync(string server, int timeout)
            { Calls++; return UniTask.FromResult(RemoteResourceVersionProbeResult.Fail("isolated")); }
        }
        private sealed class CountingApk : IApplicationUpdateChecker
        {
            public int Calls;
            public UniTask<ApkUpdateManifest> CheckAsync(string server, CancellationToken token)
            { Calls++; return UniTask.FromResult<ApkUpdateManifest>(null); }
        }
        private sealed class CountingTransport : IClientTransport
        {
            public int Connects;
            public bool IsConnected => false;
            public int ConnectionGeneration => 0;
            public Task ConnectAsync(int generation, NetworkEndpoint endpoint, int timeout, CancellationToken token) { Connects++; return Task.CompletedTask; }
            public void Send(int generation, byte[] body) => Assert.Fail("离线模式不能发送");
            public bool TryDequeue(out ClientTransportEvent value) { value = default; return false; }
            public void Disconnect(NetworkDisconnectReason reason) { }
            public void Pump(long now) { }
            public void Dispose() { }
        }
    }
}
