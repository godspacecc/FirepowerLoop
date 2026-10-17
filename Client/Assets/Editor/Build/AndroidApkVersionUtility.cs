#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;
using Boot;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>已预留的 APK 身份；即使构建或发布失败，也不再分配相同身份。</summary>
    [Serializable]
    public sealed class AndroidApkVersion
    {
        /// <summary>写入 APK 内部的三段版本名。</summary>
        public string VersionName;
        /// <summary>写入 APK 内部、用于覆盖安装的递增版本码。</summary>
        public int VersionCode;
    }

    /// <summary>综合项目、已发布 APK 与本地预留记录分配版本；不构建、不发布。</summary>
    public static class AndroidApkVersionUtility
    {
        private static readonly Regex PublishedName = new Regex(@"\AFirepowerLoop-([0-9]+\.[0-9]+\.[0-9]+)-([0-9]+)\.apk\z");

        /// <summary>取得独占文件锁后分配并持久化版本；版本名和版本码分别高于全部已知身份。</summary>
        public static AndroidApkVersion Reserve(string currentVersion, int currentCode, string releaseRoot, string reservationPath)
        {
            Version maximum = ParseVersion(currentVersion);
            if (currentCode <= 0) throw new InvalidOperationException("Android 版本码必须大于零。");
            int maximumCode = currentCode;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reservationPath)));
            using (new FileStream(reservationPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                if (File.Exists(reservationPath))
                {
                    AndroidApkVersion previous;
                    try { previous = JsonUtility.FromJson<AndroidApkVersion>(File.ReadAllText(reservationPath)); }
                    catch (Exception e) { throw new InvalidOperationException("APK 版本预留记录损坏，禁止重用版本。", e); }
                    if (previous == null) throw new InvalidOperationException("APK 版本预留记录为空。");
                    Accumulate(previous.VersionName, previous.VersionCode, ref maximum, ref maximumCode);
                }
                if (Directory.Exists(releaseRoot))
                {
                    foreach (string bundle in Directory.GetDirectories(releaseRoot, "ResourceServerBundle_*"))
                    {
                        string directory = Path.Combine(bundle, "AppUpdates", "Android");
                        if (!Directory.Exists(directory)) continue;
                        foreach (string file in Directory.GetFiles(directory, "FirepowerLoop-*.apk"))
                        {
                            Match match = PublishedName.Match(Path.GetFileName(file));
                            if (!match.Success || !int.TryParse(match.Groups[2].Value, out int code))
                                throw new InvalidOperationException("已发布 APK 文件名无效，无法安全分配版本：" + file);
                            Accumulate(match.Groups[1].Value, code, ref maximum, ref maximumCode);
                        }
                        string descriptor = Path.Combine(directory, "app-update.json");
                        if (!File.Exists(descriptor)) continue;
                        ApkUpdateManifest manifest;
                        try { manifest = JsonUtility.FromJson<ApkUpdateManifest>(File.ReadAllText(descriptor)); }
                        catch (Exception e) { throw new InvalidOperationException("已发布 APK 描述损坏：" + descriptor, e); }
                        if (manifest == null || manifest.schemaVersion != 1 || manifest.platform != "Android" ||
                            manifest.packageName != AndroidBuildUtility.ApplicationIdentifier)
                            throw new InvalidOperationException("已发布 APK 描述身份不符：" + descriptor);
                        Accumulate(manifest.versionName, manifest.versionCode, ref maximum, ref maximumCode);
                    }
                }
                if (maximumCode == int.MaxValue || maximum.Build == int.MaxValue)
                    throw new InvalidOperationException("Android 应用版本或版本码已达到上限。");
                var next = new AndroidApkVersion
                {
                    VersionName = $"{maximum.Major}.{maximum.Minor}.{maximum.Build + 1}",
                    VersionCode = maximumCode + 1
                };
                // 先写临时文件，再原子切换；失败保留旧记录，不发出未落盘的版本。
                string pending = reservationPath + ".pending";
                try
                {
                    File.WriteAllText(pending, JsonUtility.ToJson(next, true));
                    if (File.Exists(reservationPath)) File.Replace(pending, reservationPath, null);
                    else File.Move(pending, reservationPath);
                }
                finally { if (File.Exists(pending)) File.Delete(pending); }
                return next;
            }
        }

        /// <summary>严格解析三段非负数字，拒绝损坏或越界记录。</summary>
        private static Version ParseVersion(string value)
        {
            if (!Regex.IsMatch(value ?? "", @"\A[0-9]+\.[0-9]+\.[0-9]+\z") || !Version.TryParse(value, out Version version))
                throw new InvalidOperationException("Android 应用版本无效：" + value);
            return version;
        }

        /// <summary>分别累计版本名及版本码的上界，避免两者来自不同历史记录时发生回退。</summary>
        private static void Accumulate(string name, int code, ref Version maximum, ref int maximumCode)
        {
            Version version = ParseVersion(name);
            if (code <= 0) throw new InvalidOperationException("历史 Android 版本码无效。");
            if (version > maximum) maximum = version;
            maximumCode = Math.Max(maximumCode, code);
        }
    }
}
#endif
