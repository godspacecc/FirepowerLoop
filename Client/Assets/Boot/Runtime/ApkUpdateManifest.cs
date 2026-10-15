using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Boot
{
    /// <summary>整包发布描述及兼容校验；只决定更新候选和文件完整性，不下载、不安装、不管理资源版本。</summary>
    [Serializable]
    public sealed class ApkUpdateManifest
    {
        public int schemaVersion;
        public string platform;
        /// <summary>必须与当前 Android 包身份完全一致。</summary>
        public string packageName;
        public string versionName;
        public int versionCode;
        /// <summary>服务器更新目录内的单一文件名，不允许路径或外部地址。</summary>
        public string apkFileName;
        /// <summary>完整 APK 的字节数，必须大于零。</summary>
        public long sizeBytes;
        public string sha256;

        /// <summary>无效、不兼容或非新版描述返回 null；不得阻断原有资源启动。</summary>
        public static ApkUpdateManifest ParseAvailable(string json, string currentPackage, string currentVersion, int currentCode)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 65536) return null;
            try
            {
                var data = JsonUtility.FromJson<ApkUpdateManifest>(json);
                if (data == null || data.schemaVersion != 1 || data.platform != "Android" ||
                    data.packageName != currentPackage || data.versionCode <= 0 || data.versionCode < currentCode ||
                    data.sizeBytes <= 0 || data.sizeBytes > 8L * 1024 * 1024 * 1024 ||
                    !Regex.IsMatch(data.apkFileName ?? "", @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,150}\.apk\z") ||
                    !Regex.IsMatch(data.sha256 ?? "", @"\A[0-9a-fA-F]{64}\z") ||
                    !TryVersion(data.versionName, out var next) || !TryVersion(currentVersion, out var current) ||
                    next.CompareTo(current) <= 0) return null;
                return data;
            }
            catch (ArgumentException) { return null; }
        }

        private static bool TryVersion(string value, out Version version)
        {
            version = null;
            if (!Regex.IsMatch(value ?? "", @"\A[0-9]+\.[0-9]+\.[0-9]+\z")) return false;
            var parts = value.Split('.');
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch)) return false;
            version = new Version(major, minor, patch);
            return true;
        }

        /// <summary>下载结束和复用缓存前校验磁盘实际内容；任何读取失败均拒绝安装。</summary>
        public bool VerifyFile(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                using (var hash = SHA256.Create())
                {
                    if (stream.Length != sizeBytes) return false;
                    string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
                    return string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
