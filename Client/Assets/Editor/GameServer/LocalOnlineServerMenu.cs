#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Game.Editor
{
    public static class LocalOnlineServerMenu
    {
        internal const string MenuPath = "Tools/游戏服务器/启动本地联机服务器";
        internal const int Port = 31011;
        internal const string ScriptRelativePath = "Server/GameServer/Tools/start-local-online-server.ps1";
        internal const string AppRelativePath = "Server/GameServer/Bin/App.dll";
        internal const string SolutionRelativePath = "Server/GameServer/ET.sln";
        internal const string GlobalJsonRelativePath = "Server/GameServer/global.json";
        internal const string AppProjectRelativePath = "Server/GameServer/DotNet/App/DotNet.App.csproj";
        internal const string HotfixProjectRelativePath = "Server/GameServer/DotNet/Hotfix/DotNet.Hotfix.csproj";
        internal const string NLogRelativePath = "Server/GameServer/Config/NLog/NLog.config";
        internal const string LubanRelativePath = "Server/GameServer/Config/Luban";
        internal const string LogsRelativePath = "Server/GameServer/Logs";
        internal const string StateRelativePath = "Library/ServerTools/LocalOnlineServer.json";

        [MenuItem(MenuPath, false, 210)]
        private static void StartLocalOnlineServer()
        {
            var paths = ResolvePaths(Application.dataPath);
            var decision = CreateLaunchDecision(paths, IsPortInUse);
            if (decision.Kind == LocalOnlineServerLaunchDecisionKind.AlreadyStarted)
            {
                ShowDialog("本地联机服务器启动窗口已打开", decision.Message);
                return;
            }

            if (decision.Kind == LocalOnlineServerLaunchDecisionKind.PrerequisiteFailed ||
                decision.Kind == LocalOnlineServerLaunchDecisionKind.UnknownPortOccupied)
            {
                ShowDialog("本地联机服务启动失败", decision.Message);
                return;
            }

            Process process = null;
            try
            {
                process = new Process { StartInfo = CreatePowerShellStartInfo(paths) };
                if (!process.Start())
                    throw new InvalidOperationException("PowerShell 进程启动失败。");

                var snapshot = CaptureProcessSnapshot(process);
                if (snapshot == null)
                {
                    TryTerminateStartedProcess(process);
                    throw new InvalidOperationException("PowerShell 已创建，但无法取得可验证的进程身份。");
                }

                var state = new LocalOnlineServerProcessState
                {
                    ProcessId = snapshot.ProcessId,
                    StartTimeUtcTicks = snapshot.StartTimeUtcTicks,
                    ProcessName = snapshot.ProcessName,
                    ScriptPath = paths.ScriptPath,
                    GameServerRoot = paths.GameServerRoot
                };
                SaveState(paths.StatePath, state);
                Debug.Log($"[LocalOnlineServer] 已打开本地联机服务窗口：PID={state.ProcessId}，GameServer={paths.GameServerRoot}");
            }
            catch (Exception e)
            {
                if (process != null)
                    TryTerminateStartedProcess(process);
                Debug.LogError($"[LocalOnlineServer] 启动失败：{e.Message}");
                ShowDialog("本地联机服务启动失败", $"{e.Message}\n\n游戏服务器: {paths.GameServerRoot}\n日志目录: {paths.LogsPath}");
            }
        }

        internal static LocalOnlineServerPaths ResolvePaths(string dataPath)
        {
            string clientRoot = Path.GetFullPath(Directory.GetParent(Path.GetFullPath(dataPath)).FullName);
            string workspaceRoot = Path.GetFullPath(Directory.GetParent(clientRoot).FullName);
            string gameServerRoot = Path.GetFullPath(Path.Combine(workspaceRoot, "Server", "GameServer"));
            return new LocalOnlineServerPaths
            {
                ClientRoot = clientRoot,
                WorkspaceRoot = workspaceRoot,
                GameServerRoot = gameServerRoot,
                ScriptPath = Path.GetFullPath(Path.Combine(workspaceRoot, ScriptRelativePath)),
                AppPath = Path.GetFullPath(Path.Combine(workspaceRoot, AppRelativePath)),
                SolutionPath = Path.GetFullPath(Path.Combine(workspaceRoot, SolutionRelativePath)),
                GlobalJsonPath = Path.GetFullPath(Path.Combine(workspaceRoot, GlobalJsonRelativePath)),
                AppProjectPath = Path.GetFullPath(Path.Combine(workspaceRoot, AppProjectRelativePath)),
                HotfixProjectPath = Path.GetFullPath(Path.Combine(workspaceRoot, HotfixProjectRelativePath)),
                NLogPath = Path.GetFullPath(Path.Combine(workspaceRoot, NLogRelativePath)),
                LubanPath = Path.GetFullPath(Path.Combine(workspaceRoot, LubanRelativePath)),
                LogsPath = Path.GetFullPath(Path.Combine(workspaceRoot, LogsRelativePath)),
                StatePath = Path.GetFullPath(Path.Combine(clientRoot, StateRelativePath))
            };
        }

        internal static LocalOnlineServerLaunchDecision CreateLaunchDecision(
            LocalOnlineServerPaths paths,
            Func<bool> isPortInUse)
        {
            return CreateLaunchDecision(paths, isPortInUse, TryGetProcessSnapshot);
        }

        internal static LocalOnlineServerLaunchDecision CreateLaunchDecision(
            LocalOnlineServerPaths paths,
            Func<bool> isPortInUse,
            Func<int, LocalOnlineServerProcessSnapshot> getProcessSnapshot)
        {
            return CreateLaunchDecisionCore(paths, isPortInUse, getProcessSnapshot);
        }

        internal static LocalOnlineServerLaunchDecision CreateLaunchDecisionForTests(
            LocalOnlineServerPaths paths,
            Func<bool> isPortInUse,
            Func<int, object> getProcessSnapshot)
        {
            return CreateLaunchDecisionCore(
                paths,
                isPortInUse,
                processId => getProcessSnapshot(processId) as LocalOnlineServerProcessSnapshot);
        }

        private static LocalOnlineServerLaunchDecision CreateLaunchDecisionCore(
            LocalOnlineServerPaths paths,
            Func<bool> isPortInUse,
            Func<int, LocalOnlineServerProcessSnapshot> getProcessSnapshot)
        {
            if (!TryValidatePrerequisites(paths, out string prerequisiteError))
                return LocalOnlineServerLaunchDecision.PrerequisiteFailed(prerequisiteError);

            LocalOnlineServerProcessState state = LoadState(paths.StatePath, out bool hadInvalidState);
            if (state != null)
            {
                LocalOnlineServerProcessSnapshot snapshot = getProcessSnapshot(state.ProcessId);
                if (IsOwnedProcess(state, snapshot, paths.ScriptPath, paths.GameServerRoot))
                    return LocalOnlineServerLaunchDecision.AlreadyStarted("启动窗口已打开，不代表服务器已运行。请在现有窗口查看编译结果与运行状态；失败原因处理后按 R 重试，按 Ctrl+C 停止并退出。");

                DeleteState(paths.StatePath);
            }
            else if (hadInvalidState)
            {
                DeleteState(paths.StatePath);
            }

            if (isPortInUse())
                return LocalOnlineServerLaunchDecision.UnknownPortOccupied($"127.0.0.1:{Port} 已被未知进程占用；未创建本地联机服务窗口。");

            return LocalOnlineServerLaunchDecision.Ready();
        }

        internal static bool IsOwnedProcess(
            LocalOnlineServerProcessState state,
            LocalOnlineServerProcessSnapshot actual,
            string expectedScriptPath,
            string expectedGameServerRoot)
        {
            if (state == null || actual == null)
                return false;

            try
            {
                bool isPowerShell = string.Equals(actual.ProcessName, "powershell", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(actual.ProcessName, "powershell.exe", StringComparison.OrdinalIgnoreCase);
                return isPowerShell &&
                       state.ProcessId == actual.ProcessId &&
                       state.StartTimeUtcTicks == actual.StartTimeUtcTicks &&
                       string.Equals(state.ProcessName, actual.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                       SamePath(state.ScriptPath, expectedScriptPath) &&
                       SamePath(state.GameServerRoot, expectedGameServerRoot);
            }
            catch
            {
                return false;
            }
        }

        internal static ProcessStartInfo CreatePowerShellStartInfo(LocalOnlineServerPaths paths)
        {
            return new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File {QuoteArgument(paths.ScriptPath)} -GameServerRoot {QuoteArgument(paths.GameServerRoot)} -Port {Port} -BuildBeforeStart",
                WorkingDirectory = paths.GameServerRoot,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                CreateNoWindow = false
            };
        }

        internal static bool TryValidatePrerequisites(LocalOnlineServerPaths paths, out string error)
        {
            var missing = new List<string>();
            if (Application.platform != RuntimePlatform.WindowsEditor)
                missing.Add($"Windows Editor: 当前平台 {Application.platform}");
            if (ResolvePowerShellPath() == null)
                missing.Add("Windows PowerShell: powershell.exe");
            if (!File.Exists(paths.ScriptPath))
                missing.Add($"启动脚本 {ScriptRelativePath}: {paths.ScriptPath}");
            if (!File.Exists(paths.SolutionPath))
                missing.Add($"服务端解决方案 {SolutionRelativePath}: {paths.SolutionPath}");
            if (!File.Exists(paths.GlobalJsonPath))
                missing.Add($".NET SDK 配置 {GlobalJsonRelativePath}: {paths.GlobalJsonPath}");
            if (!File.Exists(paths.AppProjectPath))
                missing.Add($"App 项目 {AppProjectRelativePath}: {paths.AppProjectPath}");
            if (!File.Exists(paths.HotfixProjectPath))
                missing.Add($"Hotfix 项目 {HotfixProjectRelativePath}: {paths.HotfixProjectPath}");
            if (!File.Exists(paths.NLogPath))
                missing.Add($"NLog 配置 {NLogRelativePath}: {paths.NLogPath}");
            if (!Directory.Exists(paths.LubanPath))
                missing.Add($"Luban 配置 {LubanRelativePath}: {paths.LubanPath}");

            if (missing.Count == 0)
            {
                error = null;
                return true;
            }

            error = "前置条件缺失，未创建窗口：\n" + string.Join("\n", missing.ToArray());
            return false;
        }

        internal static LocalOnlineServerProcessState LoadState(string statePath, out bool invalid)
        {
            invalid = false;
            if (!File.Exists(statePath))
                return null;

            try
            {
                var state = JsonUtility.FromJson<LocalOnlineServerProcessState>(File.ReadAllText(statePath, Encoding.UTF8));
                if (state == null ||
                    state.ProcessId <= 0 ||
                    state.StartTimeUtcTicks <= 0 ||
                    string.IsNullOrEmpty(state.ProcessName) ||
                    string.IsNullOrEmpty(state.ScriptPath) ||
                    string.IsNullOrEmpty(state.GameServerRoot))
                {
                    invalid = true;
                    return null;
                }

                return state;
            }
            catch
            {
                invalid = true;
                return null;
            }
        }

        internal static void SaveState(string statePath, LocalOnlineServerProcessState state)
        {
            string directory = Path.GetDirectoryName(statePath);
            Directory.CreateDirectory(directory);
            string tempPath = Path.Combine(directory, Path.GetFileName(statePath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(tempPath, JsonUtility.ToJson(state, true), new UTF8Encoding(false));
                if (File.Exists(statePath))
                    File.Replace(tempPath, statePath, null);
                else
                    File.Move(tempPath, statePath);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        internal static void DeleteState(string statePath)
        {
            try
            {
                if (File.Exists(statePath))
                    File.Delete(statePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalOnlineServer] 删除过期状态失败：{e.Message}");
            }
        }

        internal static LocalOnlineServerProcessSnapshot TryGetProcessSnapshot(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                    return CaptureProcessSnapshot(process);
            }
            catch
            {
                return null;
            }
        }

        private static LocalOnlineServerProcessSnapshot CaptureProcessSnapshot(Process process)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    process.Refresh();
                    if (process.HasExited)
                        return null;
                    return new LocalOnlineServerProcessSnapshot
                    {
                        ProcessId = process.Id,
                        StartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks,
                        ProcessName = process.ProcessName
                    };
                }
                catch (InvalidOperationException)
                {
                    System.Threading.Thread.Sleep(100);
                }
            }

            return null;
        }

        private static bool IsPortInUse()
        {
            return IsTcpPortInUse(Port);
        }

        internal static bool IsTcpPortInUse(int port)
        {
            Socket socket = null;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.ExclusiveAddressUse = true;
                socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
                socket.Listen(1);
                return false;
            }
            catch
            {
                return true;
            }
            finally
            {
                if (socket != null)
                    socket.Dispose();
            }
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

        private static void TryTerminateStartedProcess(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                    process.Kill();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalOnlineServer] 无法停止本次启动失败的 PowerShell：{e.Message}");
            }
        }

        private static bool SamePath(string left, string right)
        {
            return string.Equals(Path.GetFullPath(left ?? string.Empty), Path.GetFullPath(right ?? string.Empty), StringComparison.OrdinalIgnoreCase);
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static void ShowDialog(string title, string message)
        {
            if (Application.isBatchMode)
                Debug.Log($"[LocalOnlineServer] {title}: {message}");
            else
                EditorUtility.DisplayDialog(title, message, "OK");
        }
    }

    public enum LocalOnlineServerLaunchDecisionKind
    {
        Ready,
        AlreadyStarted,
        UnknownPortOccupied,
        PrerequisiteFailed
    }

    public sealed class LocalOnlineServerLaunchDecision
    {
        public LocalOnlineServerLaunchDecisionKind Kind;
        public string Message;

        public static LocalOnlineServerLaunchDecision Ready()
        {
            return new LocalOnlineServerLaunchDecision { Kind = LocalOnlineServerLaunchDecisionKind.Ready };
        }

        public static LocalOnlineServerLaunchDecision AlreadyStarted(string message)
        {
            return new LocalOnlineServerLaunchDecision { Kind = LocalOnlineServerLaunchDecisionKind.AlreadyStarted, Message = message };
        }

        public static LocalOnlineServerLaunchDecision UnknownPortOccupied(string message)
        {
            return new LocalOnlineServerLaunchDecision { Kind = LocalOnlineServerLaunchDecisionKind.UnknownPortOccupied, Message = message };
        }

        public static LocalOnlineServerLaunchDecision PrerequisiteFailed(string message)
        {
            return new LocalOnlineServerLaunchDecision { Kind = LocalOnlineServerLaunchDecisionKind.PrerequisiteFailed, Message = message };
        }
    }

    [Serializable]
    public sealed class LocalOnlineServerProcessState
    {
        public int ProcessId;
        public long StartTimeUtcTicks;
        public string ProcessName;
        public string ScriptPath;
        public string GameServerRoot;
    }

    public sealed class LocalOnlineServerProcessSnapshot
    {
        public int ProcessId;
        public long StartTimeUtcTicks;
        public string ProcessName;
    }

    public sealed class LocalOnlineServerPaths
    {
        public string ClientRoot;
        public string WorkspaceRoot;
        public string GameServerRoot;
        public string ScriptPath;
        public string AppPath;
        public string SolutionPath;
        public string GlobalJsonPath;
        public string AppProjectPath;
        public string HotfixProjectPath;
        public string NLogPath;
        public string LubanPath;
        public string LogsPath;
        public string StatePath;
    }
}
#endif
