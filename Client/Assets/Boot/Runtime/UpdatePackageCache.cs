using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Boot
{
    /// <summary>拥有已校验整包的版本回执和启动清理；安装成功以当前宿主身份为准，不管理安装器、资源缓存或存档。</summary>
    public static class UpdatePackageCache
    {
        private const string ReceiptSuffix = ".receipt.json";

        [Serializable]
        private sealed class Receipt
        {
            public int schemaVersion;
            public string identity;
            public string version;
            /// <summary>Android 为 versionCode，Windows 为 releaseNumber；大于零才允许清理。</summary>
            public int release;
        }

        /// <summary>Boot 所有者首次启动时调用；只读取真实安装身份，失败放行且下次启动再次尝试。</summary>
        public static void CleanCurrentInstallation()
        {
            try
            {
                if (AndroidApkInstaller.Supported)
                    CleanInstalled(AndroidApkInstaller.UpdateDirectory, ".apk", Application.identifier,
                        Application.version, AndroidApkInstaller.CurrentVersionCode);
                else if (WindowsInstaller.Supported)
                {
                    var installed = WindowsUpdateManifest.ReadInstalled();
                    if (installed != null)
                        CleanInstalled(WindowsInstaller.UpdateDirectory, ".exe", installed.productId,
                            installed.versionName, installed.releaseNumber);
                }
            }
            catch (Exception)
            {
                // 可选空间回收不能让原生桥接或磁盘故障阻断游戏启动。
            }
        }

        /// <summary>完整文件通过校验后记录版本；不保存任意路径，写入失败不影响下载及安装。</summary>
        public static void RecordVerified(string root, string extension, string hash, string identity, string version, int release)
        {
            if (!ValidExtension(extension) || !ValidHash(hash) || string.IsNullOrEmpty(identity) ||
                !TryVersion(version, out _) || release <= 0) return;
            string temporary = null;
            try
            {
                if (!Directory.Exists(root) || IsLink(root)) return;
                string package = Path.Combine(root, hash.ToLowerInvariant() + extension);
                if (!File.Exists(package) || IsLink(package)) return;
                string receipt = package + ReceiptSuffix;
                if (File.Exists(receipt) && IsLink(receipt)) return;
                temporary = receipt + ".tmp-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(temporary, JsonUtility.ToJson(new Receipt
                    { schemaVersion = 1, identity = identity, version = version, release = release }));
                // 只提交完整回执；异常中断留下的临时记录永远不是清理依据。
                if (File.Exists(receipt)) File.Delete(receipt);
                File.Move(temporary, receipt);
            }
            catch (Exception) { }
            finally
            {
                try { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception) { }
            }
        }

        /// <summary>每次启动幂等回收已安装及更旧版本；未安装新版、无回执旧文件和删除失败的回执均保留。</summary>
        public static void CleanInstalled(string root, string extension, string identity, string version, int release)
        {
            if (!ValidExtension(extension) || string.IsNullOrEmpty(identity) || release <= 0 ||
                !TryVersion(version, out var installed)) return;
            try
            {
                if (!Directory.Exists(root) || IsLink(root)) return;
                foreach (string receiptPath in Directory.GetFiles(root, "*" + extension + ReceiptSuffix))
                {
                    try
                    {
                        string fileName = Path.GetFileName(receiptPath);
                        string hash = fileName.Substring(0, fileName.Length - extension.Length - ReceiptSuffix.Length);
                        if (!ValidHash(hash) || IsLink(receiptPath) || new FileInfo(receiptPath).Length > 65536) continue;
                        var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(receiptPath));
                        if (receipt == null || receipt.schemaVersion != 1 || receipt.identity != identity ||
                            receipt.release <= 0 || receipt.release > release ||
                            !TryVersion(receipt.version, out var downloaded) || downloaded.CompareTo(installed) > 0) continue;
                        string package = receiptPath.Substring(0, receiptPath.Length - ReceiptSuffix.Length);
                        if (File.Exists(package) && IsLink(package)) continue;
                        // 文件占用等删除失败时保留回执，避免永久丢失下次启动的重试依据。
                        if (File.Exists(package)) File.Delete(package);
                        File.Delete(receiptPath);
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }

        private static bool ValidExtension(string extension) => extension == ".apk" || extension == ".exe";
        private static bool ValidHash(string hash) => Regex.IsMatch(hash ?? "", @"\A[0-9a-fA-F]{64}\z");
        private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        private static bool TryVersion(string value, out Version version)
        {
            version = null;
            return Regex.IsMatch(value ?? "", @"\A[0-9]+\.[0-9]+\.[0-9]+\z") && Version.TryParse(value, out version);
        }
    }
}
