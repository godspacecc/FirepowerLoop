#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using Boot;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>消费已校验 Player 并生成、发布安装器；不调用 Unity 构建、不启动服务器或游戏。</summary>
    public static class WindowsInstallerPublishUtility
    {
        private const string CompilerKey = "FirepowerLoop.WindowsInstaller.Compiler";

        /// <summary>昂贵构建前检查设备本地编译器；交互选取仅记录 EditorPrefs，批处理缺配置直接失败。</summary>
        public static string EnsureCompiler()
        {
            string compiler = EditorPrefs.GetString(CompilerKey, "");
            if (!File.Exists(compiler))
                compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Inno Setup 6", "ISCC.exe");
            if (!File.Exists(compiler) && !Application.isBatchMode)
                compiler = EditorUtility.OpenFilePanel("选择 Inno Setup 6 的 ISCC.exe", "", "exe");
            if (!File.Exists(compiler) || !string.Equals(Path.GetFileName(compiler), "ISCC.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("未配置 Inno Setup 6 ISCC.exe，已停止安装包流程。");
            EditorPrefs.SetString(CompilerKey, compiler);
            return compiler;
        }

        /// <summary>独立批次保留失败产物；清单只在安装器哈希验证成功后发布，不重用发布序号。</summary>
        public static string GenerateAndPublish(string player, string resourceVersion, string compiler)
        {
            string repo = Directory.GetParent(Application.dataPath).Parent.FullName;
            string output = Path.Combine(repo, "Release", "WindowsInstaller", "Installer_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                RunScript(Path.Combine(repo, "Tools", "WindowsInstaller", "build_installer.ps1"),
                    "-PlayerDirectory " + Quote(Path.GetDirectoryName(player)) + " -Compiler " + Quote(compiler) +
                    " -OutputDirectory " + Quote(output));
                var identity = JsonUtility.FromJson<WindowsUpdateManifest>(File.ReadAllText(Path.Combine(
                    Path.GetDirectoryName(player), "FirepowerLoop_Data", "StreamingAssets", "windows-release.json")));
                string installer = ValidateInstaller(output, identity.releaseNumber);
                var publish = ResourceServerPublishUtility.PublishOrUpdateLatestPackage();
                if (!publish.Success || publish.Version != resourceVersion)
                    throw new InvalidOperationException("资源服同步失败或资源版本不一致：" + publish.Error);
                RunScript(Path.Combine(repo, "Server", "ResourceServer", "publish_windows_update.ps1"),
                    "-Installer " + Quote(installer) + " -Manifest " + Quote(Path.Combine(output, "app-update-windows.json")) +
                    " -Bundle " + Quote(publish.BundleDirectory));
                UnityEngine.Debug.Log($"[WindowsInstaller] 安装包与更新发布完成：{installer}\n资源服：{publish.BundleDirectory}");
                return Path.Combine(publish.BundleDirectory, "ResourceFiles");
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("Player 已生成，安装器或发布步骤失败，产物保留于：" + output + "\n" + e.Message, e);
            }
        }

        /// <summary>发布前再次验证生成清单身份、序号及字节，拒绝错包或损坏产物。</summary>
        internal static string ValidateInstaller(string output, int expectedRelease)
        {
            var manifest = WindowsUpdateManifest.ParseAvailable(File.ReadAllText(Path.Combine(output, "app-update-windows.json")), 0);
            if (manifest == null || manifest.releaseNumber != expectedRelease)
                throw new InvalidOperationException("安装器身份或发布序号不一致。");
            string installer = Path.Combine(output, manifest.installerFileName);
            if (!manifest.VerifyFile(installer)) throw new InvalidOperationException("安装器长度或哈希不一致。");
            return installer;
        }

        private static string Quote(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Contains("\"") || path.EndsWith("\\"))
                throw new ArgumentException("脚本路径不能含引号或以反斜线结尾。");
            return "\"" + path + "\"";
        }

        /// <summary>同时异步读取两个输出管道避免阻塞；外部脚本退出成功后才推进发布阶段。</summary>
        private static void RunScript(string script, string arguments)
        {
            var info = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File " + Quote(script) + " " + arguments)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(info))
            {
                if (process == null) throw new InvalidOperationException("无法启动安装包脚本。");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                string output = stdout.GetAwaiter().GetResult();
                string errors = stderr.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new InvalidOperationException(output + "\n" + errors);
            }
        }
    }
}
#endif
