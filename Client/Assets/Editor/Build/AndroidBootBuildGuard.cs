#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Game.Editor
{
    /// <summary>锁定 Android 首包包含的 Boot 输入，防止旧宿主接收要求新 Boot 的代码和资源。</summary>
    public static class AndroidBootBuildGuard
    {
        /// <summary>所有拒绝路径给出同一可执行操作，不允许通过重新记录当前指纹跳过首包。</summary>
        private const string RebuildHint = "请执行 Tools/打包/Android/完整打包 或 Tools/打包/Android/启动包（首次下载资源），并在设备安装新 APK 后再更新代码和资源。";

        /// <summary>按项目相对路径和内容计算指纹，覆盖 Boot 目录、根 meta 及 Boot 场景；不使用时间戳。</summary>
        public static string ComputeFingerprint(string projectRoot)
        {
            string root = Path.GetFullPath(projectRoot);
            string bootDirectory = Path.Combine(root, "Assets", "Boot");
            string scenePath = Path.Combine(root, "Assets", "Game", "Scenes", "Boot.unity");
            if (!Directory.Exists(bootDirectory) || !File.Exists(scenePath))
                throw new InvalidOperationException("Boot 目录或启动场景缺失，无法核对首包。" + RebuildHint);

            var files = new List<string>(Directory.GetFiles(bootDirectory, "*", SearchOption.AllDirectories));
            if (files.Count == 0)
                throw new InvalidOperationException("Boot 目录为空，无法核对首包。" + RebuildHint);
            files.Add(scenePath);
            if (File.Exists(bootDirectory + ".meta")) files.Add(bootDirectory + ".meta");
            if (File.Exists(scenePath + ".meta")) files.Add(scenePath + ".meta");
            files.Sort(StringComparer.Ordinal);

            using (var buffer = new MemoryStream())
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
            using (var hash = SHA256.Create())
            {
                foreach (string file in files)
                {
                    // 长度编码的路径和定长内容哈希可区分增删、重命名及同长度内容修改。
                    writer.Write(file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/'));
                    using (var stream = File.OpenRead(file)) writer.Write(hash.ComputeHash(stream));
                }
                writer.Flush();
                buffer.Position = 0;
                return "boot-v1:" + BitConverter.ToString(hash.ComputeHash(buffer)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        /// <summary>只读取首包基线和当前输入；缺失基线或 Boot 变化时，在编译与发布前拒绝增量更新。</summary>
        public static void ValidateBeforeHotUpdate(BuildState state, string projectRoot)
        {
            string baseline = state?.LastAndroidPlayer?.BootFingerprint;
            if (string.IsNullOrWhiteSpace(baseline))
                throw new InvalidOperationException("最近 Android 首包没有 Boot 指纹记录，禁止只更新代码和资源。" + RebuildHint);

            string current;
            try { current = ComputeFingerprint(projectRoot); }
            catch (Exception error)
            {
                throw new InvalidOperationException("无法核对 Android 首包的 Boot 输入。" + RebuildHint, error);
            }
            if (!string.Equals(baseline, current, StringComparison.Ordinal))
                throw new InvalidOperationException("Boot 已改动，当前 APK 的启动代码尚未更新，禁止只更新代码和资源。" + RebuildHint);
        }
    }
}
#endif
