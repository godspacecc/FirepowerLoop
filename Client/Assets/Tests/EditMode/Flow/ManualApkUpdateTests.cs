using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Boot;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using QFramework;
using UnityEngine.TestTools;

namespace Game.Tests
{
    /// <summary>验证主动检查同时提供安装包候选，静默检查不重复提示，不依赖 Android 真机或实际重启。</summary>
    public sealed class ManualApkUpdateTests
    {
        private FakeApkChecker apk;
        private ResourceUpdateSystem system;

        [SetUp]
        public void SetUp()
        {
            Reset();
            apk = new FakeApkChecker();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IApplicationUpdateChecker>(apk);
                architecture.RegisterUtility<IResourceUpdateRuntimeChecker>(new FakeResourceChecker());
                architecture.RegisterUtility<IResourceUpdateNoticeClient>(new FakeNoticeClient());
            };
            system = Game.Interface.GetSystem<ResourceUpdateSystem>();
            SetServer("http://server-a:8000");
        }

        [TearDown] public void TearDown() { Reset(); Architecture<Game>.OnRegisterPatch = _ => { }; }

        [UnityTest]
        public IEnumerator ManualCheckOffersApkEvenWhenResourcesAreCurrent()
        {
            var task = system.CheckWithNoticeAsync(true).AsTask();
            yield return Await(task);
            Assert.That(task.Result.Snapshot.Status, Is.EqualTo(ResourceUpdateStatus.NoUpdate));
            Assert.That(task.Result.ApplicationUpdate, Is.SameAs(apk.Candidate));
            Assert.That(apk.Server, Is.EqualTo("http://server-a:8000"));
        }

        [UnityTest]
        public IEnumerator SilentCheckDoesNotProbeApk()
        {
            var task = system.CheckWithNoticeAsync(false).AsTask();
            yield return Await(task);
            Assert.That(task.Result.ApplicationUpdate, Is.Null);
            Assert.That(apk.Calls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator LaterManualCheckCanOfferSameApkAgain()
        {
            var first = system.CheckWithNoticeAsync(true).AsTask();
            yield return Await(first);
            var second = system.CheckWithNoticeAsync(true).AsTask();
            yield return Await(second);
            Assert.That(second.Result.ApplicationUpdate, Is.SameAs(apk.Candidate));
            Assert.That(apk.Calls, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator ConcurrentManualChecksShareApkRequest()
        {
            apk.Gate = new TaskCompletionSource<ApkUpdateManifest>();
            var first = system.CheckWithNoticeAsync(true).AsTask();
            var second = system.CheckWithNoticeAsync(true).AsTask();
            apk.Gate.SetResult(apk.Candidate);
            yield return Await(Task.WhenAll(first, second));
            Assert.That(apk.Calls, Is.EqualTo(1));
            Assert.That(first.Result.ApplicationUpdate, Is.SameAs(second.Result.ApplicationUpdate));
        }

        [UnityTest]
        public IEnumerator ChangedServerRejectsLateApkCandidate()
        {
            apk.Gate = new TaskCompletionSource<ApkUpdateManifest>();
            var task = system.CheckWithNoticeAsync(true).AsTask();
            SetServer("http://server-b:8000");
            apk.Gate.SetResult(apk.Candidate);
            yield return Await(task);
            Assert.That(task.Result.ApplicationUpdate, Is.Null);
            Assert.That(apk.Calls, Is.EqualTo(1));
        }

        private void SetServer(string server) => system.InitializeStartupInfo(new BootResourceStartupInfo(BootResourceSource.HostLatest, server, "resources-1", "resources-1"));
        private static void Reset()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }
        private static IEnumerator Await(Task task)
        {
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception;
            Assert.That(task.IsCanceled, Is.False);
        }
        private sealed class FakeApkChecker : IApplicationUpdateChecker
        {
            public readonly ApkUpdateManifest Candidate = new ApkUpdateManifest { versionName = "0.1.15", sizeBytes = 1048576 };
            public int Calls;
            public string Server;
            public TaskCompletionSource<ApkUpdateManifest> Gate;
            public UniTask<ApkUpdateManifest> CheckAsync(string server, CancellationToken cancellation)
            {
                Calls++; Server = server;
                return Gate == null ? UniTask.FromResult(Candidate) : Gate.Task.AsUniTask();
            }
        }
        private sealed class FakeResourceChecker : IResourceUpdateRuntimeChecker
        {
            public bool CanCheck => true;
            public UniTask<ResourceUpdateCheckResult> CheckAsync(int timeoutSeconds) => UniTask.FromResult(
                ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.NoUpdate, "resources-1", "resources-1", 0, 0, null)));
        }
        private sealed class FakeNoticeClient : IResourceUpdateNoticeClient
        {
            public UniTask<ResourceUpdateNoticeResult> FetchAsync(ResourceUpdateNoticeRequest request) => UniTask.FromResult(ResourceUpdateNoticeResult.NoNotice);
        }
    }
}
