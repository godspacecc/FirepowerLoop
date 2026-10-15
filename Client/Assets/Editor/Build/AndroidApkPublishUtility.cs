using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Boot;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>发布已经生成的 APK 与版本描述；负责文件完整性和原子替换，不执行构建或安装。</summary>
    public static class AndroidApkPublishUtility
    {
        /// <summary>完整包验证成功后选择最近 Android 资源服，仅发布 APK，不切换已上线的热更资源。</summary>
        public static string PublishToLatestBundle(string apkPath, string releaseRoot, string versionName, int versionCode)
        {
            if (!ResourceServerPublishUtility.TryFindLatestBundle(releaseRoot, out string bundle, out string error))
                throw new InvalidOperationException(error);
            return Publish(apkPath, bundle, versionName, versionCode);
        }

        /// <summary>整包验证成功后调用；同版本内容不可变，失败不会替换已有版本描述或删除源 APK。</summary>
        /// <returns>资源服内版本描述的绝对路径。</returns>
        public static string Publish(string apkPath, string bundleDirectory, string versionName, int versionCode)
        {
            if (!Regex.IsMatch(versionName ?? "", @"\A[0-9]+\.[0-9]+\.[0-9]+\z") ||
                !Version.TryParse(versionName, out _) || versionName.Length > 60 || versionCode <= 0)
                throw new ArgumentException("APK 版本必须为有效的三段数字版本，版本号必须大于零。");
            if (!File.Exists(apkPath)) throw new FileNotFoundException("待发布 APK 不存在。", apkPath);
            long size = new FileInfo(apkPath).Length;
            if (size <= 0 || size > 8L * 1024 * 1024 * 1024)
                throw new InvalidOperationException("APK 大小无效。");
            if (!File.Exists(Path.Combine(bundleDirectory, "ResourceFiles", "DefaultPackage.version")))
                throw new InvalidOperationException("资源服尚未发布有效资源版本。");

            var manifest = new ApkUpdateManifest
            {
                schemaVersion = 1, platform = "Android", packageName = "com.anotherday.game",
                versionName = versionName, versionCode = versionCode,
                apkFileName = $"FirepowerLoop-{versionName}-{versionCode}.apk", sizeBytes = size
            };
            using (var stream = File.OpenRead(apkPath))
            using (var hash = SHA256.Create())
                manifest.sha256 = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();

            string directory = Path.GetFullPath(Path.Combine(bundleDirectory, "AppUpdates", "Android"));
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, manifest.apkFileName);
            string descriptor = Path.Combine(directory, "app-update.json");
            string token = Guid.NewGuid().ToString("N");
            string pendingApk = Path.Combine(directory, token + ".apk.pending");
            string pendingDescriptor = Path.Combine(directory, token + ".json.pending");
            try
            {
                if (File.Exists(destination))
                {
                    if (!manifest.VerifyFile(destination))
                        throw new InvalidOperationException("该版本 APK 已发布且内容不同，请使用新版本号。");
                }
                else
                {
                    File.Copy(apkPath, pendingApk);
                    if (!manifest.VerifyFile(pendingApk))
                        throw new IOException("APK 复制后完整性校验失败。");
                    File.Move(pendingApk, destination);
                }
                // 先保证 APK 可下载，再原子切换描述；正在下载旧版的玩家仍可访问旧文件。
                File.WriteAllText(pendingDescriptor, JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
                if (File.Exists(descriptor)) File.Replace(pendingDescriptor, descriptor, null);
                else File.Move(pendingDescriptor, descriptor);
                return descriptor;
            }
            finally
            {
                if (File.Exists(pendingApk)) File.Delete(pendingApk);
                if (File.Exists(pendingDescriptor)) File.Delete(pendingDescriptor);
            }
        }
    }
}
