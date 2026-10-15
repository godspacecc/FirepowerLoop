using System;
using System.IO;
using System.Text;
using System.Threading;
using Boot.UI;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Boot
{
    /// <summary>启动前可选整包更新事务；拥有请求和临时文件，失败放行，不管理 YooAsset 或游戏存档。</summary>
    public sealed class ApkUpdateFlow
    {
        private readonly HotUpdateView view;
        /// <summary>本次启动已提示的服务器与版本组合；资源重试不得重复弹出同一提示。</summary>
        private string offered;
        private string installationDetails;
#if UNITY_INCLUDE_TESTS
        public Func<string, ApkUpdateManifest, CancellationToken, UniTask<ApkInstallResult>> TestInstallRequest;
        public Action<string> TestClipboardWriter;
#endif
        public ApkUpdateFlow(HotUpdateView view) { this.view = view; }

        public async UniTask RunAsync(string server, CancellationToken cancellation)
        {
            if (!AndroidApkInstaller.Supported) return;
            try
            {
                var manifest = await CheckAsync(server, Application.identifier, Application.version,
                    AndroidApkInstaller.CurrentVersionCode, cancellation);
                if (manifest == null) return;
                string key = server + "/" + manifest.versionName + "/" + manifest.sha256;
                if (offered == key) return;
                offered = key;
                string root = AndroidApkInstaller.UpdateDirectory;
                Directory.CreateDirectory(root);
                string path = Path.Combine(root, manifest.sha256.ToLowerInvariant() + ".apk");
                string message = "新版游戏安装包已发布，是否下载更新？你也可以稍后更新，继续游戏。" +
                    $"\n当前 {Application.version} → 新版 {manifest.versionName}\n大小：{manifest.sizeBytes / 1048576d:F1} MB";
                bool ready = await VerifyAsync(manifest, path, cancellation);
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    bool primary = await ChooseAsync(message, ready ? "安装更新" : "下载更新", cancellation);
                    if (!primary) return;
                    if (!ready)
                    {
                        bool? downloaded = await DownloadAsync(server, manifest, path, cancellation);
                        ready = downloaded == true;
                        message = ready ? "安装包已下载，点击安装更新，由系统引导完成安装。" :
                            downloaded == null ? "下载已取消，你可以重新下载或稍后更新。" :
                            "下载失败或文件校验未通过，请检查网络和存储空间后重新下载。";
                        continue;
                    }
                    // 复用文件与系统交接前再次验证，损坏文件永远不能进入安装入口。
                    if (!await VerifyAsync(manifest, path, cancellation))
                    {
                        installationDetails = null;
                        ready = false;
                        message = "安装包校验未通过，请重新下载。";
                        continue;
                    }
                    var result = await InstallAsync(path, manifest, cancellation);
                    if (result == null) return;
                    installationDetails = result.result < 0 ? result.ToCopyDetails(manifest) : null;
                    message = result.result == 0 ? "已打开系统安装界面。若取消安装，可以稍后更新并继续游戏。" :
                        result.result == 1 ? "请在系统设置中允许安装此来源的应用，返回游戏后再次点击安装更新。" : result.PlayerMessage;
                    // 交接成功不等于安装成功；恢复新选择，不等待焦点或缩放时间。
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ApkUpdate] 可选整包更新已跳过：{exception.GetType().Name}");
            }
            finally
            {
                if (view) view.RenderNormal("正在检查资源更新...");
            }
        }

        /// <summary>Boot 默认失败放行；游戏版本查询启用失败传播，避免把请求失败当作已无更新。</summary>
        public static async UniTask<ApkUpdateManifest> CheckAsync(string server, string package, string version,
            int code, CancellationToken cancellation, bool throwOnFailure = false)
        {
            using (var handler = new LimitedTextHandler())
            using (var request = new UnityWebRequest(server.TrimEnd('/') + "/app-update.json", "GET", handler, null))
            {
                request.timeout = 5;
                request.redirectLimit = 0;
                request.SetRequestHeader("Cache-Control", "no-cache");
                try
                {
                    await request.SendWebRequest().ToUniTask(cancellationToken: cancellation);
                    return request.result == UnityWebRequest.Result.Success
                        ? ApkUpdateManifest.ParseAvailable(handler.Text, package, version, code, throwOnFailure) : null;
                }
                catch (UnityWebRequestException)
                {
                    if (throwOnFailure) throw;
                    return null;
                }
            }
        }

        private async UniTask<bool> ChooseAsync(string message, string primary, CancellationToken cancellation)
        {
            var completion = new UniTaskCompletionSource<bool>();
            void Complete(bool accepted)
            {
                // 点击当帧清除旧标题、正文与已完成回调，不等待下一次资源状态渲染。
                view.RenderNormal(accepted ? "正在处理安装包..." : "正在检查资源更新...");
                completion.TrySetResult(accepted);
            }
            if (string.IsNullOrEmpty(installationDetails))
                view.RenderApkChoice(message, primary, () => Complete(true), "稍后更新", () => Complete(false));
            else
                view.RenderApkInstallFailure(message, installationDetails, () => Complete(true), () => Complete(false), () =>
                {
#if UNITY_INCLUDE_TESTS
                    if (TestClipboardWriter != null) TestClipboardWriter(installationDetails);
                    else
#endif
                    GUIUtility.systemCopyBuffer = installationDetails;
                    view.ShowApkDetailsCopied();
                });
            using (cancellation.Register(() => completion.TrySetCanceled(cancellation)))
                return await completion.Task;
        }

        /// <summary>安装交接期间提供独立稍后入口；取消立即释放选择，原生排队请求也随之失效。</summary>
        private async UniTask<ApkInstallResult> InstallAsync(string path, ApkUpdateManifest manifest, CancellationToken owner)
        {
            using (var request = CancellationTokenSource.CreateLinkedTokenSource(owner))
            {
                bool deferred = false;
                view.RenderApkInstalling(() =>
                {
                    deferred = true;
                    view.RenderNormal("正在检查资源更新...");
                    request.Cancel();
                });
                try
                {
#if UNITY_INCLUDE_TESTS
                    var operation = TestInstallRequest != null ? TestInstallRequest(path, manifest, request.Token) :
                        AndroidApkInstaller.RequestInstallAsync(path, manifest, request.Token);
#else
                    var operation = AndroidApkInstaller.RequestInstallAsync(path, manifest, request.Token);
#endif
                    return await operation.AttachExternalCancellation(request.Token);
                }
                catch (OperationCanceledException) when (deferred && !owner.IsCancellationRequested) { return null; }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { return ApkInstallResult.Failure("BRIDGE_EXCEPTION", "bridge", exception.ToString()); }
            }
        }

        private static async UniTask<bool> VerifyAsync(ApkUpdateManifest manifest, string path, CancellationToken cancellation)
        {
            bool valid = await UniTask.RunOnThreadPool(() => manifest.VerifyFile(path), cancellationToken: cancellation);
            await UniTask.SwitchToMainThread(cancellation);
            if (valid && Path.GetFileName(path) == manifest.sha256.ToLowerInvariant() + ".apk")
                UpdatePackageCache.RecordVerified(Path.GetDirectoryName(path), ".apk", manifest.sha256,
                    manifest.packageName, manifest.versionName, manifest.versionCode);
            return valid;
        }

        /// <summary>true 表示已验证提交，false 表示失败，null 表示玩家取消；仅临时文件由本次下载清理。</summary>
        private async UniTask<bool?> DownloadAsync(string server, ApkUpdateManifest manifest, string path, CancellationToken owner)
        {
            string temporary = path + ".part";
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(owner))
            {
                view.RenderApkDownload(() => cancellation.Cancel());
                view.SetProgress(0);
                view.SetSize(0, (ulong)manifest.sizeBytes);
                view.SetSpeed(0);
                try
                {
                    using (var request = new UnityWebRequest(server.TrimEnd('/') + "/app-updates/android/" +
                        manifest.apkFileName, "GET"))
                    {
                        request.downloadHandler = new DownloadHandlerFile(temporary) { removeFileOnAbort = true };
                        request.redirectLimit = 0;
                        var operation = request.SendWebRequest();
                        ulong previous = 0;
                        float sampleTime = Time.realtimeSinceStartup;
                        float lastProgress = sampleTime;
                        while (!operation.isDone)
                        {
                            cancellation.Token.ThrowIfCancellationRequested();
                            ulong bytes = request.downloadedBytes;
                            float now = Time.realtimeSinceStartup;
                            if (bytes > (ulong)manifest.sizeBytes || now - lastProgress > 30f)
                            { request.Abort(); return false; }
                            if (bytes != previous) lastProgress = now;
                            view.SetProgress(Mathf.Clamp01(bytes / (float)manifest.sizeBytes));
                            view.SetSize(bytes, (ulong)manifest.sizeBytes);
                            if (now - sampleTime >= 0.5f)
                            {
                                ulong speed = (ulong)((bytes - previous) / (now - sampleTime));
                                view.SetDownloadEstimate(speed, speed == 0 ? (double?)null : (manifest.sizeBytes - (double)bytes) / speed);
                                previous = bytes;
                                sampleTime = now;
                            }
                            await UniTask.Yield(PlayerLoopTiming.Update, cancellation.Token);
                        }
                        cancellation.Token.ThrowIfCancellationRequested();
                        if (request.result != UnityWebRequest.Result.Success) return false;
                    }
                    view.RenderBusy("正在校验安装包……");
                    if (!await VerifyAsync(manifest, temporary, owner)) return false;
                    owner.ThrowIfCancellationRequested();
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temporary, path);
                    UpdatePackageCache.RecordVerified(Path.GetDirectoryName(path), ".apk", manifest.sha256,
                        manifest.packageName, manifest.versionName, manifest.versionCode);
                    return true;
                }
                catch (OperationCanceledException) when (!owner.IsCancellationRequested) { return null; }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                { Debug.LogWarning($"[ApkUpdate] 下载失败：{exception.GetType().Name}"); return false; }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
        }

        /// <summary>只接收最多 64 KiB 的描述，拒绝服务器误发大文件。</summary>
        private sealed class LimitedTextHandler : DownloadHandlerScript
        {
            private readonly MemoryStream bytes = new MemoryStream();
            public string Text => Encoding.UTF8.GetString(bytes.ToArray());
            public LimitedTextHandler() : base(new byte[8192]) { }
            protected override bool ReceiveData(byte[] data, int length)
            {
                if (data == null || length < 0 || bytes.Length + length > 65536) return false;
                bytes.Write(data, 0, length);
                return true;
            }
            public override void Dispose() { bytes.Dispose(); base.Dispose(); }
        }
    }
}
