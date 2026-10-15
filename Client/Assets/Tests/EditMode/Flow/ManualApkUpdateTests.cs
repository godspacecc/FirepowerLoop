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
        private FakeResourceChecker resources;
        private FakeNoticeClient notices;
        private FakeWindowsChecker windows;

        [SetUp]
        public void SetUp()
        {
            Reset();
            apk = new FakeApkChecker();
            resources = new FakeResourceChecker();
            notices = new FakeNoticeClient();
            windows = new FakeWindowsChecker();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IApplicationUpdateChecker>(apk);
                architecture.RegisterUtility<IResourceUpdateRuntimeChecker>(resources);
                architecture.RegisterUtility<IResourceUpdateNoticeClient>(notices);
                architecture.RegisterUtility<IWindowsApplicationUpdateChecker>(windows);
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
        public IEnumerator SilentCheckProbesApkAndLightsRedPointWithoutNotice()
        {
            var task = system.CheckWithNoticeAsync(false).AsTask();
            yield return Await(task);
            Assert.That(task.Result.ApplicationUpdate, Is.SameAs(apk.Candidate));
            Assert.That(apk.Calls, Is.EqualTo(1));
            Assert.That(notices.Calls, Is.Zero);
            AssertRedPoint(true);
        }

        [UnityTest]
        public IEnumerator SilentAndManualChecksSharePendingPackageRequest()
        {
            apk.Gate = new TaskCompletionSource<ApkUpdateManifest>();
            var silent = system.CheckWithNoticeAsync(false).AsTask();
            yield return AwaitPackageRequest();
            var again = system.CheckWithNoticeAsync(false).AsTask();
            Assert.That(apk.Calls, Is.EqualTo(1));
            var manual = system.CheckWithNoticeAsync(true).AsTask();
            apk.Gate.SetResult(apk.Candidate);
            yield return Await(Task.WhenAll(silent, again, manual));
            Assert.That(apk.Calls, Is.EqualTo(1));
            Assert.That(manual.Result.ApplicationUpdate, Is.SameAs(apk.Candidate));
            Assert.That(notices.Calls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PackageFailureRetainsKnownUpdateUntilSuccessfulNoUpdate()
        {
            yield return Await(system.CheckWithNoticeAsync(false).AsTask());
            AssertRedPoint(true);
            apk.Failure = true;
            yield return Await(system.CheckWithNoticeAsync(false).AsTask());
            AssertRedPoint(true);
            apk.Failure = false;
            apk.Candidate = null;
            yield return Await(system.CheckWithNoticeAsync(false).AsTask());
            AssertRedPoint(false);
        }

        [UnityTest]
        public IEnumerator ResourceFailureAndCheckingRetainKnownUpdate()
        {
            apk.Candidate = null;
            resources.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.UpdateAvailable, "1", "2", 0, 0, null));
            yield return Await(system.CheckWithNoticeAsync(false).AsTask());
            AssertRedPoint(true);
            resources.Gate = new TaskCompletionSource<ResourceUpdateCheckResult>();
            var pending = system.CheckWithNoticeAsync(false).AsTask();
            AssertRedPoint(true);
            resources.Gate.SetResult(ResourceUpdateCheckResult.Fail("1", "offline"));
            yield return Await(pending);
            AssertRedPoint(true);
            resources.Gate = null;
            resources.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.NoUpdate, "1", "1", 0, 0, null));
            yield return Await(system.CheckWithNoticeAsync(false).AsTask());
            AssertRedPoint(false);
        }

        [UnityTest]
        public IEnumerator WindowsCandidateKeepsRedPointWhenResourcesAndApkAreCurrent()
        {
            apk.Candidate = null;
            windows.Candidate = new WindowsUpdateManifest { releaseNumber = 2 };
            yield return Await(system.CheckWithNoticeAsync(false).AsTask());
            AssertRedPoint(true);
            windows.Failure = true;
            yield return Await(system.CheckWithNoticeAsync(false).AsTask());
            AssertRedPoint(true);
            windows.Failure = false;
            windows.Candidate = null;
            yield return Await(system.CheckWithNoticeAsync(false).AsTask());
            AssertRedPoint(false);
        }

        private static void AssertRedPoint(bool active)
        {
            LifeCycle.LateUpdate();
            Assert.That(Game.Interface.GetSystem<RedPointSystem>().GetState(RedPointIds.StartResourceUpdate).Active, Is.EqualTo(active));
        }

        [UnityTest]
        public IEnumerator RedPointClearsOnlyAfterAllVersionSourcesConfirmNoUpdate()
        {
            apk.Candidate = null;
            resources.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.UpdateAvailable, "1", "2", 0, 0, null));
            yield return Await(system.CheckWithNoticeAsync(false).AsTask());
            AssertRedPoint(true);
            resources.Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.NoUpdate, "1", "1", 0, 0, null));
            apk.Gate = new TaskCompletionSource<ApkUpdateManifest>();
            var pending = system.CheckWithNoticeAsync(false).AsTask();
            yield return null;
            AssertRedPoint(true);
            apk.Gate.SetException(new InvalidOperationException("offline"));
            yield return Await(pending);
            AssertRedPoint(true);
            apk.Gate = null;
            yield return Await(system.CheckWithNoticeAsync(false).AsTask());
            AssertRedPoint(false);
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
            yield return AwaitPackageRequest();
            Assert.That(apk.Calls, Is.EqualTo(1));
            SetServer("http://server-b:8000");
            apk.Gate.SetResult(apk.Candidate);
            yield return Await(task);
            Assert.That(task.Result.ApplicationUpdate, Is.Null);
            Assert.That(apk.Calls, Is.EqualTo(1));
        }

        private void SetServer(string server) => system.InitializeStartupInfo(new BootResourceStartupInfo(BootResourceSource.HostLatest, server, "resources-1", "resources-1"));
        private IEnumerator AwaitPackageRequest()
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (apk.Calls == 0 && DateTime.UtcNow < deadline) yield return null;
            Assert.That(apk.Calls, Is.EqualTo(1), "Package query must reach the pending gate");
        }
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
            public ApkUpdateManifest Candidate = new ApkUpdateManifest { versionName = "0.1.15", sizeBytes = 1048576 };
            public bool Failure;
            public int Calls;
            public string Server;
            public TaskCompletionSource<ApkUpdateManifest> Gate;
            public UniTask<ApkUpdateManifest> CheckAsync(string server, CancellationToken cancellation)
            {
                Calls++; Server = server;
                if (Failure) throw new InvalidOperationException("offline");
                return Gate == null ? UniTask.FromResult(Candidate) : Gate.Task.AsUniTask();
            }
        }
        private sealed class FakeResourceChecker : IResourceUpdateRuntimeChecker
        {
            public bool CanCheck => true;
            public ResourceUpdateCheckResult Next = ResourceUpdateCheckResult.Ok(new ResourceUpdateSnapshot(ResourceUpdateStatus.NoUpdate, "resources-1", "resources-1", 0, 0, null));
            public TaskCompletionSource<ResourceUpdateCheckResult> Gate;
            public UniTask<ResourceUpdateCheckResult> CheckAsync(int timeoutSeconds, bool versionOnly = false) => Gate == null ? UniTask.FromResult(Next) : Gate.Task.AsUniTask();
        }
        private sealed class FakeWindowsChecker : IWindowsApplicationUpdateChecker
        {
            public WindowsUpdateManifest Candidate;
            public bool Failure;
            public UniTask<WindowsUpdateManifest> CheckAsync(string server, CancellationToken token)
            {
                if (Failure) throw new InvalidOperationException("offline");
                return UniTask.FromResult(Candidate);
            }
        }
        private sealed class FakeNoticeClient : IResourceUpdateNoticeClient
        {
            public int Calls;
            public UniTask<ResourceUpdateNoticeResult> FetchAsync(ResourceUpdateNoticeRequest request)
            {
                Calls++;
                return UniTask.FromResult(ResourceUpdateNoticeResult.NoNotice);
            }
        }
    }
}
