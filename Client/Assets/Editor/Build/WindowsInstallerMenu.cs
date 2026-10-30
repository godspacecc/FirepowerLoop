#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>用户手动把已有正式 Player 转为安装器；不触发 Unity 构建，不自动发布。</summary>
    public static class WindowsInstallerMenu
    {
        /// <summary>防止同一会话重复启动安装器生成。</summary>
        private static bool running;

        /// <summary>选择已有游戏，仅补做安装器，输出到统一发布目录。</summary>
        [MenuItem("Tools/打包/PC/快速打包/生成安装器", false, 122)]
        private static async void Generate()
        {
            if (running) return;
            string exe = EditorUtility.OpenFilePanel("选择正式 Player 的 FirepowerLoop.exe", GetPlayerSelectionDirectory(), "exe");
            if (string.IsNullOrEmpty(exe)) return;
            if (Path.GetFileName(exe) != "FirepowerLoop.exe") { UnityEngine.Debug.LogError("请选择 FirepowerLoop.exe。"); return; }
            string output = WindowsInstallerPublishUtility.CreateOutputDirectory();
            string repo = Directory.GetParent(Application.dataPath).Parent.FullName;
            string script = Path.Combine(repo, "Tools", "WindowsInstaller", "build_installer.ps1");
            running = true;
            try
            {
                string compiler = WindowsInstallerPublishUtility.EnsureCompiler();
                // 参数以独立引号传入脚本文件；不拼接 PowerShell 命令或 cmd 命令字符串。
                var info = new ProcessStartInfo("powershell.exe") {
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(script) +
                        " -PlayerDirectory " + Quote(Path.GetDirectoryName(exe)) + " -Compiler " + Quote(compiler) +
                        " -OutputDirectory " + Quote(output), UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (var process = Process.Start(info))
                {
                    if (process == null) throw new InvalidOperationException("无法启动安装器编译脚本。");
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();
                    await Task.Run(() => process.WaitForExit());
                    string result = await stdout; string errors = await stderr;
                    if (process.ExitCode != 0) throw new InvalidOperationException(result + "\n" + errors);
                    UnityEngine.Debug.Log(result);
                    EditorUtility.RevealInFinder(output);
                }
            }
            catch (Exception e) { UnityEngine.Debug.LogError("安装器生成失败：" + e.Message + "\n输出批次（若已创建则保留）：" + output); }
            finally { running = false; }
        }

        /// <summary>优先定位最近成功的Windows游戏；旧记录失效时回到平台输出目录或其现存父目录。</summary>
        private static string GetPlayerSelectionDirectory()
        {
            string project = Directory.GetParent(Application.dataPath).FullName;
            var state = BuildStateStore.Load(out string warning);
            if (!string.IsNullOrEmpty(warning)) UnityEngine.Debug.LogWarning(warning);
            string player = state?.LastPlayer?.PlayerPath;
            // 仅使用PC成功记录，不从Android记录或磁盘时间猜测最近游戏。
            if (!string.IsNullOrWhiteSpace(player) && File.Exists(player) && Path.IsPathRooted(player) &&
                string.Equals(Path.GetFileName(player), "FirepowerLoop.exe", StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(player);

            string directory = Path.Combine(project, "Build", "Windows");
            // 尚未打包时目录可能不存在，使用现存父目录避免文件窗口沿用任意历史位置。
            while (!Directory.Exists(directory) && !string.Equals(directory, project, StringComparison.OrdinalIgnoreCase))
                directory = Path.GetDirectoryName(directory);
            return directory;
        }

        /// <summary>为脚本参数保留路径边界，拒绝无法安全引用的路径。</summary>
        private static string Quote(string path)
        {
            if (path.Contains("\"") || path.EndsWith("\\")) throw new ArgumentException("路径不能含引号或以反斜线结束。");
            return "\"" + path + "\"";
        }
    }
}
#endif
