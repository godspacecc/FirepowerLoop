using System;
using UnityEngine;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Boot
{
    /// <summary>稳定 Boot 的 Android 安装适配器；权限与安装确认交给系统，不认定安装已成功。</summary>
    public static class AndroidApkInstaller
    {
        public static bool Supported => Application.platform == RuntimePlatform.Android;

        public static int CurrentVersionCode => Call<int>("getVersionCode");
        public static string UpdateDirectory => Call<string>("getUpdateDirectory");

        /// <summary>原生工作线程验证 APK 后由 UI 线程打开系统界面；Unity 只异步轮询，取消阻止未发起的意图。</summary>
        public static async UniTask<ApkInstallResult> RequestInstallAsync(string path, ApkUpdateManifest manifest, CancellationToken cancellation)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            string requestId = Guid.NewGuid().ToString("N");
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var bridge = new AndroidJavaClass("com.firepowerloop.update.ApkInstaller"))
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    bridge.CallStatic("beginInstall", activity, path, manifest.versionName, manifest.versionCode, requestId);
                    float deadline = Time.realtimeSinceStartup + 8f;
                    while (true)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var result = ApkInstallResult.Parse(bridge.CallStatic<string>("getInstallResult", requestId));
                        if (result.result != 2) return result;
                        if (Time.realtimeSinceStartup >= deadline)
                            return ApkInstallResult.Failure("INSTALL_TIMEOUT", "system_intent", "安装请求在 8 秒内未完成交接。");
                        await UniTask.Delay(100, ignoreTimeScale: true, cancellationToken: cancellation);
                    }
                }
                finally { bridge.CallStatic("cancelInstall", requestId); }
            }
#else
            await UniTask.CompletedTask;
            throw new PlatformNotSupportedException();
#endif
        }

        private static T Call<T>(string method)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var bridge = new AndroidJavaClass("com.firepowerloop.update.ApkInstaller"))
                return bridge.CallStatic<T>(method, activity);
#else
            throw new PlatformNotSupportedException();
#endif
        }
    }
}
