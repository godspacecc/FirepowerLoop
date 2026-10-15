using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
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
    /// <summary>通过本机真实 HTTP 和 Boot 场景克隆验证可选检查与按钮交接，不安装 PC 包。</summary>
    public sealed class WindowsUpdateFlowTests
    {
        private const string Valid = "{\"schemaVersion\":1,\"architecture\":\"x64\",\"platform\":\"Windows\",\"productId\":\"com.anotherday.game\",\"versionName\":\"0.1.10\",\"releaseNumber\":2,\"installerFileName\":\"release.exe\",\"sizeBytes\":3,\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}";

        [UnityTest] public IEnumerator NewPackageIsDetectedOverHttp() => Check(200, Valid, true).ToCoroutine();
        [UnityTest] public IEnumerator OldServer404AllowsStartup() => Check(404, "missing", false).ToCoroutine();
        [UnityTest] public IEnumerator InvalidManifestAllowsStartup() => Check(200, "invalid", false).ToCoroutine();
        [UnityTest] public IEnumerator OversizedManifestAllowsStartup() => Check(200, new string('x', 70000), false).ToCoroutine();
        [UnityTest] public IEnumerator CancelledOwnerCannotReturnCandidate() => CancelCheck().ToCoroutine();
        [UnityTest] public IEnumerator ValidDownloadCommitsVerifiedFile() => DownloadCheck(true, false).ToCoroutine();
        [UnityTest] public IEnumerator InvalidDownloadDoesNotCommitFile() => DownloadCheck(false, false).ToCoroutine();
        [UnityTest] public IEnumerator PlayerCancellationRemovesPartialFile() => DownloadCheck(true, true).ToCoroutine();
        [UnityTest] public IEnumerator GameQueryDistinguishesFailureFromCurrentVersion() => StrictCheck().ToCoroutine();

        private static async UniTask StrictCheck()
        {
            foreach (int status in new[] { 404, 500, 200 })
            {
                using (var server = new OneReplyServer(status, "invalid"))
                {
                    bool failed = false;
                    try { await WindowsUpdateFlow.CheckAsync(server.Url, 2, CancellationToken.None, throwOnFailure: true); }
                    catch (Exception) { failed = true; }
                    Assert.That(failed, Is.True, "HTTP " + status + " must not confirm no update");
                }
            }
            using (var server = new OneReplyServer(200, Valid))
                Assert.That(await WindowsUpdateFlow.CheckAsync(server.Url, 2, CancellationToken.None, throwOnFailure: true), Is.Null);
        }

        private static async UniTask DownloadCheck(bool validHash, bool cancel)
        {
            var view = CloneView();
            string root = Path.Combine(Path.GetTempPath(), "AnotherDayDownload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string hash = validHash ? "039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81" : new string('a', 64);
            string path = Path.Combine(root, hash + ".exe");
            try
            {
                using (var server = new OneReplyServer(200, "\u0001\u0002\u0003"))
                {
                    // SHA-256 是固定字节 01 02 03 的独立已知值。
                    string json = validHash ? Valid.Replace(new string('a', 64), "039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81") : Valid;
                    var manifest = WindowsUpdateManifest.ParseAvailable(json, 1);
                    var flow = new WindowsUpdateFlow(view);
                    var task = (UniTask<bool?>)typeof(WindowsUpdateFlow).GetMethod("DownloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(flow, new object[] { server.Url, manifest, path, CancellationToken.None });
                    if (cancel) Button(view, "_retryButton").onClick.Invoke();
                    bool? result = await task;
                    Assert.That(result, Is.EqualTo(cancel ? (bool?)null : validHash));
                    Assert.That(File.Exists(path), Is.EqualTo(validHash && !cancel));
                    Assert.That(Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(path) + ".part-*"), Is.Empty);
                    Assert.That(File.Exists(path + ".receipt.json"), Is.EqualTo(validHash && !cancel));
                    if (File.Exists(path)) Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 1, 2, 3 }));
                    if (validHash && !cancel)
                    {
                        UpdatePackageCache.CleanInstalled(root, ".exe", manifest.productId, "0.1.9", 1);
                        Assert.That(File.Exists(path), Is.True);
                        UpdatePackageCache.CleanInstalled(root, ".exe", manifest.productId, manifest.versionName, manifest.releaseNumber);
                        Assert.That(File.Exists(path), Is.False);
                    }
                }
            }
            finally { Directory.Delete(root, true); UnityEngine.Object.DestroyImmediate(view.gameObject); }
        }

        private static async UniTask Check(int status, string body, bool expected)
        {
            using (var server = new OneReplyServer(status, body))
            {
                var value = await WindowsUpdateFlow.CheckAsync(server.Url, 1, CancellationToken.None);
                Assert.That(value != null, Is.EqualTo(expected));
            }
        }

        private static async UniTask CancelCheck()
        {
            using (var server = new OneReplyServer(200, Valid))
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                bool cancelled = false;
                try { await WindowsUpdateFlow.CheckAsync(server.Url, 1, cancellation.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                Assert.That(cancelled, Is.True);
            }
        }

        [Test]
        public void InstallerButtonsDoNotTriggerResourceFailureActions()
        {
            var view = CloneView();
            try
            {
                int accepted = 0, deferred = 0, resourceRetry = 0, copyError = 0;
                view.OnRetryClicked += () => resourceRetry++;
                view.OnCopyDetailsClicked += () => copyError++;
                view.RenderApkChoice("更新可选", "下载更新", () => accepted++, "稍后更新", () => deferred++);
                Button(view, "_retryButton").onClick.Invoke();
                Button(view, "_copyDetailsButton").onClick.Invoke();
                Assert.That(accepted, Is.EqualTo(1)); Assert.That(deferred, Is.EqualTo(1));
                Assert.That(resourceRetry, Is.Zero); Assert.That(copyError, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
        }

        [Test]
        public void LeavingInstallerStateRestoresFailureButtonRolesAndLabels()
        {
            var view = CloneView();
            try
            {
                int retries = 0, obsoleteChoice = 0;
                var button = Button(view, "_retryButton");
                string original = button.GetComponentInChildren<Text>(true).text;
                view.OnRetryClicked += () => retries++;
                view.RenderApkChoice("更新可选", "下载更新", () => obsoleteChoice++, "稍后更新", () => { });
                view.RenderFailure(BootFailure.Create(BootFailureCode.HostInitializationFailed,
                    BootFailureStage.ResourceInitialization, BootRetryScope.ResourceInitialization));
                button.onClick.Invoke();
                Assert.That(retries, Is.EqualTo(1)); Assert.That(obsoleteChoice, Is.Zero);
                Assert.That(button.GetComponentInChildren<Text>(true).text, Is.EqualTo(original));
            }
            finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
        }

        [Test]
        public void DownloadShowsOnlyCancelAction()
        {
            var view = CloneView();
            try
            {
                int cancellations = 0;
                view.RenderApkDownload(() => cancellations++);
                Assert.That(Button(view, "_copyDetailsButton").gameObject.activeSelf, Is.False);
                Assert.That(Button(view, "_quitButton").gameObject.activeSelf, Is.False);
                Button(view, "_retryButton").onClick.Invoke();
                Assert.That(cancellations, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
        }

        private static HotUpdateView CloneView()
        {
            var source = Resources.FindObjectsOfTypeAll<HotUpdateView>().First(v => v.gameObject.scene.path == "Assets/Game/Scenes/Boot.unity");
            var view = UnityEngine.Object.Instantiate(source.gameObject).GetComponent<HotUpdateView>();
            view.InitializeForTests();
            return view;
        }
        private static Button Button(HotUpdateView view, string field) =>
            (Button)typeof(HotUpdateView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);

        private sealed class OneReplyServer : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly Thread thread;
            public string Url { get; }
            public OneReplyServer(int status, string body)
            {
                listener.Start(); Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
                thread = new Thread(() =>
                {
                    try
                    {
                        using (var client = listener.AcceptTcpClient())
                        using (var stream = client.GetStream())
                        {
                            client.ReceiveTimeout = 3000;
                            using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                                while (!string.IsNullOrEmpty(reader.ReadLine())) { }
                            byte[] content = Encoding.UTF8.GetBytes(body);
                            byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Test\r\nContent-Type: application/json\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n");
                            stream.Write(header, 0, header.Length); stream.Write(content, 0, content.Length);
                        }
                    }
                    catch (SocketException) { }
                    catch (IOException) { }
                    catch (ObjectDisposedException) { }
                }) { IsBackground = true };
                thread.Start();
            }
            public void Dispose() { listener.Stop(); thread.Join(3500); }
        }
    }
}
