#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Game.Editor
{
    public static class LocalOnlineServerPublishMenu
    {
        internal const string MenuPath = "Tools/游戏服务器/生成联机服务器包";
        internal const string ScriptRelativePath = "Server/GameServer/Publish-win-x64.ps1";

        [MenuItem(MenuPath, false, 211)]
        private static void PublishLocalOnlineServer()
        {
            var paths = ResolvePaths(Application.dataPath);
            if (!TryValidatePrerequisites(paths, out string error))
            {
                ShowDialog("联机服务器包生成失败", error);
                return;
            }

            try
            {
                using (var process = new Process { StartInfo = CreatePowerShellStartInfo(paths) })
                {
                    if (!process.Start())
                        throw new InvalidOperationException("PowerShell 生成窗口创建失败。");
                }

                Debug.Log($"[LocalOnlineServerPublish] 已打开 Windows 联机服务器包生成窗口，请在窗口查看最终结果：GameServer={paths.GameServerRoot}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LocalOnlineServerPublish] 生成窗口创建失败：{e.Message}");
                ShowDialog("联机服务器包生成失败", $"{e.Message}\n\n游戏服务器: {paths.GameServerRoot}\n生成脚本: {paths.ScriptPath}");
            }
        }

        internal static LocalOnlineServerPublishPaths ResolvePaths(string dataPath)
        {
            string clientRoot = Path.GetFullPath(Directory.GetParent(Path.GetFullPath(dataPath)).FullName);
            string workspaceRoot = Path.GetFullPath(Directory.GetParent(clientRoot).FullName);
            string gameServerRoot = Path.GetFullPath(Path.Combine(workspaceRoot, "Server", "GameServer"));
            return new LocalOnlineServerPublishPaths
            {
                ClientRoot = clientRoot,
                WorkspaceRoot = workspaceRoot,
                GameServerRoot = gameServerRoot,
                ScriptPath = Path.GetFullPath(Path.Combine(workspaceRoot, ScriptRelativePath))
            };
        }

        internal static bool TryValidatePrerequisites(LocalOnlineServerPublishPaths paths, out string error)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                error = $"Windows Editor: 当前平台 {Application.platform}";
                return false;
            }

            if (ResolvePowerShellPath() == null)
            {
                error = "Windows PowerShell 不可用：powershell.exe";
                return false;
            }

            if (!File.Exists(paths.ScriptPath))
            {
                error = $"发布脚本不存在：{paths.ScriptPath}";
                return false;
            }

            error = null;
            return true;
        }

        internal static ProcessStartInfo CreatePowerShellStartInfo(LocalOnlineServerPublishPaths paths)
        {
            return new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File {QuoteArgument(paths.ScriptPath)} -GameServerRoot {QuoteArgument(paths.GameServerRoot)}",
                WorkingDirectory = paths.GameServerRoot,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                CreateNoWindow = false
            };
        }

        private static string ResolvePowerShellPath()
        {
            string windir = Environment.GetEnvironmentVariable("WINDIR");
            if (!string.IsNullOrEmpty(windir))
            {
                string systemPowerShell = Path.Combine(windir, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
                if (File.Exists(systemPowerShell))
                    return systemPowerShell;
            }

            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string entry in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(entry))
                    continue;
                string candidate = Path.Combine(entry.Trim(), "powershell.exe");
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static void ShowDialog(string title, string message)
        {
            if (Application.isBatchMode)
                Debug.Log($"[LocalOnlineServerPublish] {title}: {message}");
            else
                EditorUtility.DisplayDialog(title, message, "OK");
        }
    }

    public sealed class LocalOnlineServerPublishPaths
    {
        public string ClientRoot;
        public string WorkspaceRoot;
        public string GameServerRoot;
        public string ScriptPath;
    }
}
#endif
