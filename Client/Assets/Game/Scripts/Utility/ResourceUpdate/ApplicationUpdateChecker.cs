using System.Threading;
using System;
using Boot;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>安装包版本探测端口；只返回兼容候选，不拥有下载、安装或界面状态。</summary>
    public interface IApplicationUpdateChecker : IUtility
    {
        UniTask<ApkUpdateManifest> CheckAsync(string resourceServer, CancellationToken cancellation);
    }

    /// <summary>复用稳定 Boot 的版本兼容校验与限时请求；只在 Android 检查 APK，不修改宿主。</summary>
    public sealed class ApplicationUpdateChecker : IApplicationUpdateChecker
    {
        public async UniTask<ApkUpdateManifest> CheckAsync(string resourceServer, CancellationToken cancellation)
        {
            if (!AndroidApkInstaller.Supported || string.IsNullOrWhiteSpace(resourceServer)) return null;
            try
            {
                return await ApkUpdateFlow.CheckAsync(resourceServer, Application.identifier, Application.version,
                    AndroidApkInstaller.CurrentVersionCode, cancellation);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                Log.Warning($"[ApplicationUpdateChecker] 安装包检查已跳过：{exception.GetType().Name}");
                return null;
            }
        }
    }
    /// <summary>PC 安装包检查端口；版本来自 Player 内置身份，不使用 Android 版本号。</summary>
    public interface IWindowsApplicationUpdateChecker : IUtility
    {
        UniTask<WindowsUpdateManifest> CheckAsync(string server, CancellationToken token);
    }

    public sealed class WindowsApplicationUpdateChecker : IWindowsApplicationUpdateChecker
    {
        public async UniTask<WindowsUpdateManifest> CheckAsync(string server, CancellationToken token)
        {
            if (!WindowsInstaller.Supported || string.IsNullOrWhiteSpace(server)) return null;
            var local = WindowsUpdateManifest.ReadInstalled();
            if (local == null) return null;
            return await WindowsUpdateFlow.CheckAsync(server, local.releaseNumber, token);
        }
    }
}
