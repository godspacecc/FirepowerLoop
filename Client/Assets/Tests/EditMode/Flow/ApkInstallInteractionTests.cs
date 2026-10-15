using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using Boot;
using Boot.UI;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests
{
    /// <summary>克隆真实 Boot 界面验证安装交接、复制与稍后退出，不触发系统安装。</summary>
    public sealed class ApkInstallInteractionTests
    {
        [Test]
        public void DeferredChoiceImmediatelyClearsUpdateText()
        {
            var view = CloneView();
            try
            {
                var flow = new ApkUpdateFlow(view);
                var choice = Choose(flow);
                Button(view, "_copyDetailsButton").onClick.Invoke();
                Assert.That(choice.GetAwaiter().GetResult(), Is.False);
                Assert.That(Field<Text>(view, "_titleText").gameObject.activeSelf, Is.False);
                Assert.That(Field<Text>(view, "_guidanceText").gameObject.activeSelf, Is.False);
                Assert.That(Field<Text>(view, "_statusText").gameObject.activeSelf, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
        }

        [Test]
        public void AcceptedChoiceDisablesCompletedChoiceCallbacks()
        {
            var view = CloneView();
            try
            {
                var choice = Choose(new ApkUpdateFlow(view));
                Button(view, "_retryButton").onClick.Invoke();
                Assert.That(choice.GetAwaiter().GetResult(), Is.True);
                Assert.That(Button(view, "_retryButton").gameObject.activeSelf, Is.False);
                Assert.That(Button(view, "_copyDetailsButton").gameObject.activeSelf, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
        }

        [UnityTest]
        public IEnumerator PendingInstallCanBeDeferredWithoutWaitingForNativeResult()
        {
            var view = CloneView();
            var gate = new UniTaskCompletionSource<ApkInstallResult>();
            try
            {
                var flow = new ApkUpdateFlow(view);
                var hook = typeof(ApkUpdateFlow).GetField("TestInstallRequest");
                Assert.That(hook, Is.Not.Null, "安装交接缺少可验证端口");
                hook.SetValue(flow, (Func<string, ApkUpdateManifest, CancellationToken, UniTask<ApkInstallResult>>)((p,m,c) => gate.Task));
                var method = typeof(ApkUpdateFlow).GetMethod("InstallAsync", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                var task = (UniTask<ApkInstallResult>)method.Invoke(flow, new object[] { "test.apk", new ApkUpdateManifest(), CancellationToken.None });
                Button(view, "_copyDetailsButton").onClick.Invoke();
                yield return task.ToCoroutine(result => Assert.That(result, Is.Null));
                Assert.That(Field<Text>(view, "_guidanceText").gameObject.activeSelf, Is.False);
            }
            finally { gate.TrySetResult(new ApkInstallResult()); UnityEngine.Object.DestroyImmediate(view.gameObject); }
        }

        [Test]
        public void InstallFailureOffersIndependentCopyRetryAndDeferActions()
        {
            var view = CloneView();
            try
            {
                var method = typeof(HotUpdateView).GetMethod("RenderApkInstallFailure");
                Assert.That(method, Is.Not.Null, "安装失败缺少三种独立操作");
                int retries=0, copies=0, defers=0, quits=0;
                view.OnQuitClicked += () => quits++;
                string details = ApkInstallResult.Failure("SIGNATURE_MISMATCH", "signature_validation", "native stack should stay off screen")
                    .ToCopyDetails(new ApkUpdateManifest());
                method.Invoke(view, new object[] { "签名不匹配", details, (Action)(() => retries++), (Action)(() => defers++), (Action)(() => copies++) });
                Button(view, "_quitButton").onClick.Invoke();
                Button(view, "_copyDetailsButton").onClick.Invoke();
                Button(view, "_retryButton").onClick.Invoke();
                Assert.That(copies, Is.EqualTo(1)); Assert.That(defers, Is.EqualTo(1)); Assert.That(retries, Is.EqualTo(1)); Assert.That(quits, Is.Zero);
                Assert.That(Field<Text>(view, "_detailsText").text, Does.Contain("SIGNATURE_MISMATCH"));
                Assert.That(Field<Text>(view, "_detailsText").text, Does.Not.Contain("native stack"));
            }
            finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
        }

        private static UniTask<bool> Choose(ApkUpdateFlow flow)
        {
            var method = typeof(ApkUpdateFlow).GetMethod("ChooseAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            var args = method.GetParameters().Length == 3 ? new object[] { "安装包已下载", "安装更新", CancellationToken.None } : new object[] { "安装包已下载", "安装更新", CancellationToken.None, null };
            return (UniTask<bool>)method.Invoke(flow, args);
        }

        [Test]
        public void FailureCopyDoesNotCompleteChoiceAndCopiesNativeDetails()
        {
            var view = CloneView();
            try
            {
                var flow = new ApkUpdateFlow(view);
                string details = ApkInstallResult.Failure("SIGNATURE_MISMATCH", "signature_validation", "native exception details")
                    .ToCopyDetails(new ApkUpdateManifest { versionName = "0.1.16", versionCode = 6 });
                typeof(ApkUpdateFlow).GetField("installationDetails", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(flow, details);
                string copied = null;
                flow.TestClipboardWriter = value => copied = value;
                var choice = Choose(flow);
                Button(view, "_quitButton").onClick.Invoke();
                Assert.That(choice.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(copied, Does.Contain("SIGNATURE_MISMATCH").And.Contain("0.1.16").And.Contain("native exception details"));
                Button(view, "_copyDetailsButton").onClick.Invoke();
                Assert.That(choice.GetAwaiter().GetResult(), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
        }
        private static T Field<T>(HotUpdateView view, string name) => (T)typeof(HotUpdateView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
        private static Button Button(HotUpdateView view,string name) => Field<Button>(view,name);
        private static HotUpdateView CloneView()
        {
            var source = Resources.FindObjectsOfTypeAll<HotUpdateView>().First(v => v.gameObject.scene.path == "Assets/Game/Scenes/Boot.unity");
            var view = UnityEngine.Object.Instantiate(source.gameObject).GetComponent<HotUpdateView>();
            view.InitializeForTests(); return view;
        }
    }
}
