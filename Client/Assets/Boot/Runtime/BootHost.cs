using System.Collections.Generic;

namespace Boot
{
    /// <summary>游戏层通过此入口获取启动资源状态并报告 Main 的生命周期。</summary>
    public static class BootHost
    {
        public static bool ReportProgress(string message) => Launcher.TryReportMainProgress(message);
        public static bool ReportReady() => Launcher.TryReportMainReady();
        public static bool ReportFailure(IReadOnlyDictionary<string, string> details) => Launcher.TryReportMainFailure(details);
        public static bool TryGetResourceInfo(out BootResourceStartupInfo info) => BootResourceStartupState.TryGet(out info);
        public static bool SupportsApplicationUpdate => AndroidApkInstaller.Supported;
        public static int InstalledVersionCode => AndroidApkInstaller.CurrentVersionCode;
    }
}
