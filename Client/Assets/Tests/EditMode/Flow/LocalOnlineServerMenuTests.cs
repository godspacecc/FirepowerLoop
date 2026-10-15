using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class LocalOnlineServerMenuTests
    {
        private const string MenuPath = "Tools/游戏服务器/启动本地联机服务";

        private Type MenuType => RequiredType("Game.Editor.LocalOnlineServerMenu");
        private Type StateType => RequiredType("Game.Editor.LocalOnlineServerProcessState");
        private Type SnapshotType => RequiredType("Game.Editor.LocalOnlineServerProcessSnapshot");
        private Type DecisionKindType => RequiredType("Game.Editor.LocalOnlineServerLaunchDecisionKind");

        [Test]
        public void UnityExposesExactlyOneLocalOnlineServerMenu()
        {
            var menus = TypeCache.GetMethodsWithAttribute<MenuItem>()
                .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>())
                .Where(item => item.menuItem.StartsWith("Tools/游戏服务器/", StringComparison.Ordinal))
                .ToArray();

            Assert.That(menus.Count(item => item.menuItem == MenuPath), Is.EqualTo(1));
            Assert.That(menus.Where(item => item.menuItem == MenuPath), Has.All.Matches<MenuItem>(item => !item.validate));
            Assert.That(menus.Where(item => item.menuItem == MenuPath), Has.All.Matches<MenuItem>(item => !ContainsShortcut(item.menuItem)));
            Assert.That(menus.Select(item => item.menuItem), Has.None.Contains("Tools/游戏服务器/停止本地联机服务"));
            Assert.That(menus.Select(item => item.menuItem), Has.None.Contains("Tools/游戏服务器/重启本地联机服务"));
        }

        [Test]
        public void ResolvedPathsUseWorkspaceGameServerAndClientLibraryState()
        {
            string workspace = Normalize(Path.Combine(Path.GetTempPath(), "AD Menu Tests", Guid.NewGuid().ToString("N")));
            string dataPath = Path.Combine(workspace, "Client", "Assets");

            object paths = Invoke("ResolvePaths", dataPath);

            Assert.That(GetField(paths, "ClientRoot"), Is.EqualTo(Path.Combine(workspace, "Client")));
            Assert.That(GetField(paths, "WorkspaceRoot"), Is.EqualTo(workspace));
            Assert.That(GetField(paths, "GameServerRoot"), Is.EqualTo(Path.Combine(workspace, "Server", "GameServer")));
            Assert.That(Normalize(GetField(paths, "ScriptPath")), Is.EqualTo(Normalize(Path.Combine(workspace, "Server", "GameServer", "Tools", "start-local-online-server.ps1"))));
            Assert.That(Normalize(GetField(paths, "StatePath")), Is.EqualTo(Normalize(Path.Combine(workspace, "Client", "Library", "ServerTools", "LocalOnlineServer.json"))));
        }

        [Test]
        public void LauncherOwnershipRequiresAllRecordedIdentityFields()
        {
            object state = CreateState(123, 456L, "powershell", "C:/Project/Server/GameServer/Tools/start-local-online-server.ps1", "C:/Project/Server/GameServer");
            object snapshot = CreateSnapshot(123, 456L, "powershell");

            Assert.That(InvokeIsOwned(state, snapshot), Is.True);

            SetField(snapshot, "ProcessId", 999);
            Assert.That(InvokeIsOwned(state, snapshot), Is.False);
            SetField(snapshot, "ProcessId", 123);

            SetField(snapshot, "StartTimeUtcTicks", 999L);
            Assert.That(InvokeIsOwned(state, snapshot), Is.False);
            SetField(snapshot, "StartTimeUtcTicks", 456L);

            SetField(snapshot, "ProcessName", "cmd");
            Assert.That(InvokeIsOwned(state, snapshot), Is.False);
            SetField(snapshot, "ProcessName", "powershell");

            SetField(state, "ProcessName", "cmd");
            Assert.That(InvokeIsOwned(state, snapshot), Is.False);
            SetField(state, "ProcessName", "powershell");

            Assert.That(InvokeIsOwned(state, snapshot, expectedScript: "C:/Project/Server/GameServer/Tools/other.ps1"), Is.False);
            Assert.That(InvokeIsOwned(state, snapshot, expectedRoot: "C:/Project/Other"), Is.False);
        }

        [Test]
        public void StaleStateNeverOwnsOrTerminatesMismatchedProcess()
        {
            object state = CreateState(123, 456L, "powershell", "C:/Project/Server/GameServer/Tools/start-local-online-server.ps1", "C:/Project/Server/GameServer");
            Assert.That(InvokeIsOwned(state, null), Is.False);
            Assert.That(MenuType.GetMethod("CreateOwnedTreeStopStartInfo", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic), Is.Null);

            string source = ReadEditorSource();
            Assert.That(source, Does.Not.Contain("taskkill"));
            Assert.That(source, Does.Not.Contain("GetProcessesByName"));
        }

        [Test]
        public void LaunchDecisionRejectsUnknownPortWithoutOwnedWindow()
        {
            string root = CreateTempWorkspace(withPrerequisites: true);
            object paths = Invoke("ResolvePaths", Path.Combine(root, "Client", "Assets"));

            object portOccupied = Invoke("CreateLaunchDecision", paths, new Func<bool>(() => true));
            AssertDecisionKind(portOccupied, "UnknownPortOccupied");

            object ready = Invoke("CreateLaunchDecision", paths, new Func<bool>(() => false));
            AssertDecisionKind(ready, "Ready");
        }

        [Test]
        public void TcpPortProbeDetectsTemporaryListenerAndRecoversAfterRelease()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            try
            {
                Assert.That((bool)Invoke("IsTcpPortInUse", port), Is.True);
            }
            finally
            {
                listener.Stop();
            }

            Assert.That((bool)Invoke("IsTcpPortInUse", port), Is.False);
        }

        [Test]
        public void LaunchDecisionKeepsOwnedWindowAheadOfPortAndDeletesStaleState()
        {
            string root = CreateTempWorkspace(withPrerequisites: true);
            object paths = Invoke("ResolvePaths", Path.Combine(root, "Client", "Assets"));
            string statePath = GetField(paths, "StatePath");
            object state = CreateState(
                123,
                456L,
                "powershell",
                Path.Combine(root, "Server", "GameServer", "Tools", "start-local-online-server.ps1"),
                Path.Combine(root, "Server", "GameServer"));
            Invoke("SaveState", statePath, state);

            object owned = Invoke(
                "CreateLaunchDecisionForTests",
                paths,
                new Func<bool>(() => true),
                new Func<int, object>(_ => CreateSnapshot(123, 456L, "powershell")));
            AssertDecisionKind(owned, "AlreadyStarted");
            Assert.That(File.Exists(statePath), Is.True);

            object stale = Invoke(
                "CreateLaunchDecisionForTests",
                paths,
                new Func<bool>(() => false),
                new Func<int, object>(_ => CreateSnapshot(123, 999L, "powershell")));
            AssertDecisionKind(stale, "Ready");
            Assert.That(File.Exists(statePath), Is.False);

            Directory.CreateDirectory(Path.GetDirectoryName(statePath));
            File.WriteAllText(statePath, "{not-json");
            object corrupt = Invoke(
                "CreateLaunchDecisionForTests",
                paths,
                new Func<bool>(() => false),
                new Func<int, object>(_ => null));
            AssertDecisionKind(corrupt, "Ready");
            Assert.That(File.Exists(statePath), Is.False);
        }

        [Test]
        public void PowerShellStartInfoRequestsBuildBeforeStart()
        {
            string root = CreateTempWorkspace(withPrerequisites: true);
            object paths = Invoke("ResolvePaths", Path.Combine(root, "Client", "Assets"));

            var info = (ProcessStartInfo)Invoke("CreatePowerShellStartInfo", paths);

            Assert.That(info.FileName, Is.EqualTo("powershell.exe"));
            Assert.That(info.WorkingDirectory, Is.EqualTo(Path.Combine(root, "Server", "GameServer")));
            Assert.That(info.UseShellExecute, Is.True);
            Assert.That(info.WindowStyle, Is.EqualTo(ProcessWindowStyle.Normal));
            Assert.That(info.CreateNoWindow, Is.False);
            Assert.That(info.Arguments, Does.Contain("-ExecutionPolicy Bypass"));
            Assert.That(info.Arguments, Does.Contain("-File"));
            Assert.That(info.Arguments, Does.Contain("start-local-online-server.ps1"));
            Assert.That(info.Arguments, Does.Contain("-GameServerRoot"));
            Assert.That(info.Arguments, Does.Contain("\"" + Path.Combine(root, "Server", "GameServer") + "\""));
            Assert.That(info.Arguments, Does.Contain("-Port 31001"));
            Assert.That(info.Arguments, Does.Contain("-BuildBeforeStart"));
        }

        [Test]
        public void LauncherBuildModeUsesFixedReleaseIncrementalBuild()
        {
            string script = ReadLauncherScript();

            Assert.That(script, Does.Contain("dotnet"));
            Assert.That(script, Does.Contain("Command = \"dotnet App.dll --AppType=Server --StartConfig=OnlineLocal --Console=1\""));
            Assert.That(script, Does.Contain("$startInfo.Arguments = \"App.dll --AppType=Server --StartConfig=OnlineLocal --Console=1\""));
            Assert.That(script, Does.Not.Contain("App.dll --AppType=Server --StartConfig=OnlineLocal --Console=0"));
            Assert.That(script, Does.Not.Contain("--LogLevel"));
            Assert.That(script, Does.Contain("WorkingDirectory = $Context.Bin"));
            Assert.That(script, Does.Contain("UseShellExecute = $false"));
            Assert.That(script, Does.Contain("CreateNoWindow = $false"));
            Assert.That(script, Does.Contain("RedirectStandardOutput = $false"));
            Assert.That(script, Does.Contain("RedirectStandardError = $false"));
            Assert.That(script, Does.Contain("GameGate: 127.0.0.1:${Port}"));
            Assert.That(script, Does.Contain("日志目录:"));
            Assert.That(script, Does.Contain("$script:serverProcess"));
            Assert.That(script, Does.Not.Contain("dotnet run"));
            Assert.That(script, Does.Contain("[switch]$BuildBeforeStart"));
            Assert.That(script, Does.Contain("dotnet build ET.sln -c Release -p:NuGetAudit=false"));
            Assert.That(script, Does.Contain("if ($BuildBeforeStart)"));
            Assert.That(script, Does.Not.Contain("Start-Process"));
            Assert.That(script, Does.Not.Contain("Test-UdpPortFree"));
            Assert.That(script, Does.Not.Contain("SocketType]::Dgram"));
            Assert.That(script, Does.Not.Contain("ProtocolType]::Udp"));
            Assert.That(script, Does.Contain("Test-TcpPortFree"));
            Assert.That(script, Does.Contain("TcpListener"));
        }

        [Test]
        public void SourcePrerequisitesDoNotRequireExistingAppBinary()
        {
            string root = CreateTempWorkspace(withPrerequisites: true);
            object paths = Invoke("ResolvePaths", Path.Combine(root, "Client", "Assets"));
            Assert.That(File.Exists(GetField(paths, "AppPath")), Is.False);

            object decision = Invoke("CreateLaunchDecision", paths, new Func<bool>(() => false));
            AssertDecisionKind(decision, "Ready");
        }

        [TestCase("SolutionPath")]
        [TestCase("GlobalJsonPath")]
        [TestCase("AppProjectPath")]
        [TestCase("HotfixProjectPath")]
        public void MissingSourcePrerequisiteRejectsLaunch(string fieldName)
        {
            string root = CreateTempWorkspace(withPrerequisites: true);
            object paths = Invoke("ResolvePaths", Path.Combine(root, "Client", "Assets"));
            File.Delete(GetField(paths, fieldName));

            object decision = Invoke("CreateLaunchDecision", paths, new Func<bool>(() => false));
            AssertDecisionKind(decision, "PrerequisiteFailed");
        }

        [Test]
        public void LauncherBuildModeCompilesBeforeSingleStart()
        {
            using (var fixture = LauncherFixture.Create(buildFails: false, prebuild: false))
            {
                fixture.Start(buildBeforeStart: true);
                string output = fixture.WaitFor("服务器进程已启动", 60000);

                Assert.That(output, Does.Contain("增量编译成功：ExitCode=0"));
                Assert.That(output, Does.Contain("编译产物校验成功：App.dll、Hotfix.dll"));
                Assert.That(IndexOf(output, "增量编译成功"), Is.LessThan(IndexOf(output, "编译产物校验成功")));
                Assert.That(IndexOf(output, "编译产物校验成功"), Is.LessThan(IndexOf(output, "服务器进程已启动")));
                Assert.That(CountOf(output, "服务器进程已启动"), Is.EqualTo(1));
            }
        }

        [Test]
        public void LauncherBuildFailureWithExistingBinaryNeverStartsAndRetryRebuilds()
        {
            using (var fixture = LauncherFixture.Create(buildFails: true, prebuild: true))
            {
                fixture.Start(buildBeforeStart: true);
                fixture.WaitFor("源码编译失败", 60000);
                fixture.SendLine("R");
                string output = fixture.WaitForCount("源码编译失败", 2, 60000);

                Assert.That(CountOf(output, "开始增量编译"), Is.EqualTo(2));
                Assert.That(output, Does.Not.Contain("服务器进程已启动"));
            }
        }

        [Test]
        public void LauncherRejectsOccupiedPortBeforeBuild()
        {
            using (var fixture = LauncherFixture.Create(buildFails: false, prebuild: false))
            {
                var listener = new TcpListener(IPAddress.Loopback, fixture.Port);
                try
                {
                    listener.Start();
                    fixture.Start(buildBeforeStart: true);
                    string output = fixture.WaitFor("已被占用", 15000);

                    Assert.That(output, Does.Not.Contain("开始增量编译"));
                    Assert.That(output, Does.Not.Contain("服务器进程已启动"));
                }
                finally
                {
                    listener.Stop();
                }
            }
        }

        [Test]
        public void LauncherWithoutBuildSwitchStartsExistingBinaryWithoutCompile()
        {
            using (var fixture = LauncherFixture.Create(buildFails: false, prebuild: true))
            {
                fixture.Start(buildBeforeStart: false);
                string output = fixture.WaitFor("服务器进程已启动", 15000);

                Assert.That(output, Does.Not.Contain("开始增量编译"));
                Assert.That(CountOf(output, "服务器进程已启动"), Is.EqualTo(1));
            }
        }

        [Test]
        public void NLogRoutesBusinessLevelsToConsoleWithoutDebugNoise()
        {
            XDocument document = XDocument.Load(NLogConfigPath());
            XNamespace ns = document.Root.Name.Namespace;
            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            var targets = document.Root.Elements(ns + "targets").Elements(ns + "target").ToArray();
            var rules = document.Root.Element(ns + "rules").Elements(ns + "logger").ToArray();

            var consoleTargets = targets
                .Where(target => string.Equals((string)target.Attribute(xsi + "type"), "Console", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Assert.That(consoleTargets.Length, Is.EqualTo(1));
            Assert.That((string)consoleTargets[0].Attribute("name"), Is.EqualTo("ServerConsole"));
            Assert.That((string)consoleTargets[0].Attribute("layout"), Is.EqualTo("${longdate} ${message}"));

            var consoleRules = rules
                .Where(rule => string.Equals((string)rule.Attribute("writeTo"), "ServerConsole", StringComparison.Ordinal))
                .ToArray();
            Assert.That(consoleRules.Length, Is.EqualTo(1));
            Assert.That((string)consoleRules[0].Attribute("name"), Is.EqualTo("*"));
            Assert.That((string)consoleRules[0].Attribute("minlevel"), Is.EqualTo("Info"));
            Assert.That((string)consoleRules[0].Attribute("maxlevel"), Is.EqualTo("Error"));
            Assert.That(rules.Where(WritesToConsole), Has.None.Matches<XElement>(AllowsDebugOrTrace));
        }

        [Test]
        public void NLogKeepsExistingFileTargetsAndRules()
        {
            XDocument document = XDocument.Load(NLogConfigPath());
            XNamespace ns = document.Root.Name.Namespace;
            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            var targets = document.Root.Elements(ns + "targets").Elements(ns + "target").ToArray();
            var rules = document.Root.Element(ns + "rules").Elements(ns + "logger").ToArray();

            AssertFileTarget(targets, xsi, "ServerDebug", "${basedir}/../Logs/${logger}.${date:format=yyyyMMddHH}.Debug.log");
            AssertFileTarget(targets, xsi, "ServerInfo", "${basedir}/../Logs/${logger}.${date:format=yyyyMMddHH}.Info.log");
            AssertFileTarget(targets, xsi, "ServerWarn", "${basedir}/../Logs/${logger}.${date:format=yyyyMMddHH}.Warn.log");
            AssertFileTarget(targets, xsi, "ServerError", "${basedir}/../Logs/${logger}.${date:format=yyyyMMddHH}.Error.log");

            AssertFileRule(rules, "ServerDebug", "Trace", "Error", "ServerDebug");
            AssertFileRule(rules, "ServerInfo", "Info", "Info", "ServerInfo");
            AssertFileRule(rules, "ServerWarn", "Warn", "Warn", "ServerWarn");
            AssertFileRule(rules, "ServerError", "Error", "Error", "ServerError");
        }

        [Test]
        public void WindowsPublishInheritsSourceLauncherAndNLogConfig()
        {
            string script = File.ReadAllText(Path.Combine(WorkspaceRoot(), "Server/GameServer/Publish-win-x64.ps1"));

            Assert.That(script, Does.Contain("SourceConfig = Convert-ToFullPath (Join-Path $normalizedRoot \"Config\")"));
            Assert.That(script, Does.Contain("NLogConfig = Convert-ToFullPath (Join-Path $normalizedRoot \"Config\\NLog\\NLog.config\")"));
            Assert.That(script, Does.Contain("SourceLauncher = Convert-ToFullPath (Join-Path $normalizedRoot \"Tools\\start-local-online-server.ps1\")"));
            Assert.That(script, Does.Contain("Copy-Item -LiteralPath $Context.SourceConfig -Destination $safeConfig -Recurse -Force"));
            Assert.That(script, Does.Contain("Copy-Item -LiteralPath $Context.SourceLauncher -Destination $Context.PublishedLauncher -Force"));
            Assert.That(script, Does.Not.Contain("Publish\\win-x64\\Config\\NLog\\NLog.config"));
            Assert.That(script, Does.Not.Contain("Publish\\win-x64\\Tools\\start-local-online-server.ps1"));
        }

        [Test]
        public void LauncherScriptDefinesRestartStopAndFailureWaitStates()
        {
            string script = ReadLauncherScript();

            Assert.That(script, Does.Contain("TreatControlCAsInput = $true"));
            Assert.That(script, Does.Contain("KeyAvailable"));
            Assert.That(script, Does.Contain("ReadKey($true)"));
            Assert.That(script, Does.Contain("[ConsoleKey]::R"));
            Assert.That(script, Does.Contain("[ConsoleKey]::C"));
            Assert.That(script, Does.Contain("WaitingAfterFailure"));
            Assert.That(script, Does.Contain("服务器异常退出"));
            Assert.That(script, Does.Contain("按 R 重启"));
            Assert.That(script, Does.Not.Contain("while ($true) { Start-Server"));
            Assert.That(script, Does.Not.Contain("Get-Process"));
            Assert.That(script, Does.Not.Contain("taskkill"));
        }

        [Test]
        public void UnityLifecycleDoesNotStopDetachedLauncherWindow()
        {
            string source = ReadEditorSource();

            Assert.That(source, Does.Not.Contain("EditorApplication.quitting"));
            Assert.That(source, Does.Not.Contain("playModeStateChanged"));
            Assert.That(source, Does.Not.Contain("AssemblyReloadEvents"));
            Assert.That(source, Does.Not.Contain("LocalResourceServerUtility"));
            Assert.That(source, Does.Not.Contain("Server/ResourceServer"));
        }

        private static bool ContainsShortcut(string menuItem)
        {
            return menuItem.Contains(" %") || menuItem.Contains(" #") || menuItem.Contains(" &") || menuItem.Contains(" _");
        }

        private object Invoke(string name, params object[] args)
        {
            var methods = MenuType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.Name == name && method.GetParameters().Length == args.Length)
                .Where(method => ParametersMatch(method.GetParameters(), args))
                .ToArray();
            Assert.That(methods.Length, Is.EqualTo(1), name);
            return methods[0].Invoke(null, args);
        }

        private static bool ParametersMatch(ParameterInfo[] parameters, object[] args)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                if (args[i] == null)
                    continue;
                if (!parameters[i].ParameterType.IsInstanceOfType(args[i]))
                    return false;
            }

            return true;
        }

        private bool InvokeIsOwned(object state, object snapshot, string expectedScript = "C:/Project/Server/GameServer/Tools/start-local-online-server.ps1", string expectedRoot = "C:/Project/Server/GameServer")
        {
            return (bool)Invoke("IsOwnedProcess", state, snapshot, expectedScript, expectedRoot);
        }

        private object CreateState(int pid, long ticks, string name, string script, string root)
        {
            object state = Activator.CreateInstance(StateType);
            SetField(state, "ProcessId", pid);
            SetField(state, "StartTimeUtcTicks", ticks);
            SetField(state, "ProcessName", name);
            SetField(state, "ScriptPath", script);
            SetField(state, "GameServerRoot", root);
            return state;
        }

        private object CreateSnapshot(int pid, long ticks, string name)
        {
            object snapshot = Activator.CreateInstance(SnapshotType);
            SetField(snapshot, "ProcessId", pid);
            SetField(snapshot, "StartTimeUtcTicks", ticks);
            SetField(snapshot, "ProcessName", name);
            return snapshot;
        }

        private void AssertDecisionKind(object decision, string expected)
        {
            object kind = GetRawField(decision, "Kind");
            Assert.That(Enum.GetName(DecisionKindType, kind), Is.EqualTo(expected));
        }

        private static string CreateTempWorkspace(bool withPrerequisites)
        {
            string root = Normalize(Path.Combine(Path.GetTempPath(), "AD Menu Tests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(Path.Combine(root, "Client", "Assets"));
            if (withPrerequisites)
            {
                Directory.CreateDirectory(Path.Combine(root, "Server", "GameServer", "Tools"));
                Directory.CreateDirectory(Path.Combine(root, "Server", "GameServer", "Config", "NLog"));
                Directory.CreateDirectory(Path.Combine(root, "Server", "GameServer", "Config", "Luban"));
                Directory.CreateDirectory(Path.Combine(root, "Server", "GameServer", "DotNet", "App"));
                Directory.CreateDirectory(Path.Combine(root, "Server", "GameServer", "DotNet", "Hotfix"));
                File.WriteAllText(Path.Combine(root, "Server", "GameServer", "Tools", "start-local-online-server.ps1"), string.Empty);
                File.WriteAllText(Path.Combine(root, "Server", "GameServer", "ET.sln"), string.Empty);
                File.WriteAllText(Path.Combine(root, "Server", "GameServer", "global.json"), string.Empty);
                File.WriteAllText(Path.Combine(root, "Server", "GameServer", "DotNet", "App", "DotNet.App.csproj"), string.Empty);
                File.WriteAllText(Path.Combine(root, "Server", "GameServer", "DotNet", "Hotfix", "DotNet.Hotfix.csproj"), string.Empty);
                File.WriteAllText(Path.Combine(root, "Server", "GameServer", "Config", "NLog", "NLog.config"), string.Empty);
            }

            return root;
        }

        private static int IndexOf(string value, string token)
        {
            return value.IndexOf(token, StringComparison.Ordinal);
        }

        private static int CountOf(string value, string token)
        {
            int count = 0;
            int index = 0;
            while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }
            return count;
        }

        private sealed class LauncherFixture : IDisposable
        {
            private readonly object outputLock = new object();
            private readonly StringBuilder output = new StringBuilder();
            private Process process;

            public string Root { get; private set; }
            public int Port { get; private set; }

            public static LauncherFixture Create(bool buildFails, bool prebuild)
            {
                var fixture = new LauncherFixture
                {
                    Root = Normalize(Path.Combine(Path.GetTempPath(), "AD Launcher Tests", Guid.NewGuid().ToString("N"))),
                    Port = ReserveFreePort()
                };
                fixture.CreateProject();
                if (prebuild)
                    fixture.BuildProject();
                if (buildFails)
                    File.WriteAllText(Path.Combine(fixture.Root, "DotNet", "App", "Broken.cs"), "public class Broken { this is not valid C#; }");
                return fixture;
            }

            public void Start(bool buildBeforeStart)
            {
                string script = Path.Combine(Root, "Tools", "start-local-online-server.ps1");
                string arguments = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{script}\" -GameServerRoot \"{Root}\" -Port {Port}";
                if (buildBeforeStart)
                    arguments += " -BuildBeforeStart";

                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = arguments,
                        WorkingDirectory = Root,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    },
                    EnableRaisingEvents = true
                };
                process.OutputDataReceived += AppendOutput;
                process.ErrorDataReceived += AppendOutput;
                Assert.That(process.Start(), Is.True);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            public void SendLine(string line)
            {
                process.StandardInput.WriteLine(line);
                process.StandardInput.Flush();
            }

            public string WaitFor(string token, int timeoutMs)
            {
                return WaitForCount(token, 1, timeoutMs);
            }

            public string WaitForCount(string token, int count, int timeoutMs)
            {
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < timeoutMs)
                {
                    string snapshot = Snapshot();
                    if (CountOf(snapshot, token) >= count)
                        return snapshot;
                    if (process.HasExited)
                        Assert.Fail($"启动脚本提前退出，ExitCode={process.ExitCode}\n{snapshot}");
                    Thread.Sleep(50);
                }
                Assert.Fail($"等待启动脚本输出超时：{token}\n{Snapshot()}");
                return null;
            }

            public void Dispose()
            {
                if (process != null)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            int ownedProcessId = process.Id;
                            Assert.That(ownedProcessId, Is.GreaterThan(0));
                            process.Kill();
                            process.WaitForExit(5000);
                        }
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                if (Directory.Exists(Root))
                    Directory.Delete(Root, true);
            }

            private void CreateProject()
            {
                Directory.CreateDirectory(Path.Combine(Root, "Tools"));
                Directory.CreateDirectory(Path.Combine(Root, "Config", "NLog"));
                Directory.CreateDirectory(Path.Combine(Root, "Config", "Luban"));
                Directory.CreateDirectory(Path.Combine(Root, "DotNet", "App"));
                Directory.CreateDirectory(Path.Combine(Root, "DotNet", "Hotfix"));
                File.Copy(Path.Combine(WorkspaceRoot(), "Server", "GameServer", "Tools", "start-local-online-server.ps1"), Path.Combine(Root, "Tools", "start-local-online-server.ps1"));
                File.Copy(Path.Combine(WorkspaceRoot(), "Server", "GameServer", "global.json"), Path.Combine(Root, "global.json"));
                File.WriteAllText(Path.Combine(Root, "Config", "NLog", "NLog.config"), string.Empty);
                File.WriteAllText(Path.Combine(Root, "DotNet", "Hotfix", "DotNet.Hotfix.csproj"), ProjectFile("Hotfix", "Library", null));
                File.WriteAllText(Path.Combine(Root, "DotNet", "Hotfix", "Hotfix.cs"), "public static class HotfixMarker { public static int Value => 1; }");
                File.WriteAllText(Path.Combine(Root, "DotNet", "App", "DotNet.App.csproj"), ProjectFile("App", "Exe", "..\\Hotfix\\DotNet.Hotfix.csproj"));
                File.WriteAllText(Path.Combine(Root, "DotNet", "App", "Program.cs"), "public static class Program { public static int Main(string[] args) { System.Console.WriteLine(\"MINIMAL_APP_STARTED\"); return HotfixMarker.Value - 1; } }");
                File.WriteAllText(Path.Combine(Root, "ET.sln"), SolutionFile());
            }

            private void BuildProject()
            {
                using (var build = Process.Start(new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "build ET.sln -c Release -p:NuGetAudit=false",
                    WorkingDirectory = Root,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }))
                {
                    string stdout = build.StandardOutput.ReadToEnd();
                    string stderr = build.StandardError.ReadToEnd();
                    Assert.That(build.WaitForExit(60000), Is.True, stdout + stderr);
                    Assert.That(build.ExitCode, Is.EqualTo(0), stdout + stderr);
                }
            }

            private void AppendOutput(object sender, DataReceivedEventArgs args)
            {
                if (args.Data == null)
                    return;
                lock (outputLock)
                    output.AppendLine(args.Data);
            }

            private string Snapshot()
            {
                lock (outputLock)
                    return output.ToString();
            }

            private static int ReserveFreePort()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }

            private static string ProjectFile(string assemblyName, string outputType, string projectReference)
            {
                string reference = projectReference == null ? string.Empty : $"<ItemGroup><ProjectReference Include=\"{projectReference}\" /></ItemGroup>";
                return $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>{outputType}</OutputType><AssemblyName>{assemblyName}</AssemblyName><OutputPath>$(MSBuildThisFileDirectory)..\\..\\Bin\\</OutputPath><AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath><ImplicitUsings>disable</ImplicitUsings><Nullable>disable</Nullable></PropertyGroup>{reference}</Project>";
            }

            private static string SolutionFile()
            {
                return "Microsoft Visual Studio Solution File, Format Version 12.00\r\n" +
                       "# Visual Studio Version 17\r\n" +
                       "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"App\", \"DotNet\\App\\DotNet.App.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\r\nEndProject\r\n" +
                       "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Hotfix\", \"DotNet\\Hotfix\\DotNet.Hotfix.csproj\", \"{22222222-2222-2222-2222-222222222222}\"\r\nEndProject\r\n" +
                       "Global\r\nGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\nRelease|Any CPU = Release|Any CPU\r\nEndGlobalSection\r\nGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n{11111111-1111-1111-1111-111111111111}.Release|Any CPU.ActiveCfg = Release|Any CPU\r\n{11111111-1111-1111-1111-111111111111}.Release|Any CPU.Build.0 = Release|Any CPU\r\n{22222222-2222-2222-2222-222222222222}.Release|Any CPU.ActiveCfg = Release|Any CPU\r\n{22222222-2222-2222-2222-222222222222}.Release|Any CPU.Build.0 = Release|Any CPU\r\nEndGlobalSection\r\nEndGlobal\r\n";
            }
        }

        private static Type RequiredType(string fullName)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName))
                .FirstOrDefault(found => found != null);
            Assert.That(type, Is.Not.Null, fullName);
            return type;
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static string GetField(object target, string name)
        {
            return (string)GetRawField(target, name);
        }

        private static object GetRawField(object target, string name)
        {
            return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
        }

        private static string ReadEditorSource()
        {
            return File.ReadAllText(Path.Combine(ApplicationRoot(), "Assets/Editor/GameServer/LocalOnlineServerMenu.cs"));
        }

        private static string ReadLauncherScript()
        {
            return File.ReadAllText(Path.Combine(WorkspaceRoot(), "Server/GameServer/Tools/start-local-online-server.ps1"));
        }

        private static string NLogConfigPath()
        {
            return Path.Combine(WorkspaceRoot(), "Server/GameServer/Config/NLog/NLog.config");
        }

        private static bool WritesToConsole(XElement rule)
        {
            return string.Equals((string)rule.Attribute("writeTo"), "ServerConsole", StringComparison.Ordinal);
        }

        private static bool AllowsDebugOrTrace(XElement rule)
        {
            string minLevel = (string)rule.Attribute("minlevel");
            string maxLevel = (string)rule.Attribute("maxlevel");
            return IsLevelAtOrBelowDebug(minLevel) || IsLevelAtOrBelowDebug(maxLevel);
        }

        private static bool IsLevelAtOrBelowDebug(string level)
        {
            return string.Equals(level, "Trace", StringComparison.OrdinalIgnoreCase)
                || string.Equals(level, "Debug", StringComparison.OrdinalIgnoreCase);
        }

        private static void AssertFileTarget(XElement[] targets, XNamespace xsi, string name, string fileName)
        {
            XElement target = targets.SingleOrDefault(element => string.Equals((string)element.Attribute("name"), name, StringComparison.Ordinal));
            Assert.That(target, Is.Not.Null, name);
            Assert.That((string)target.Attribute(xsi + "type"), Is.EqualTo("File"));
            Assert.That((string)target.Attribute("fileName"), Is.EqualTo(fileName));
            Assert.That((string)target.Attribute("deleteOldFileOnStartup"), Is.EqualTo("false"));
        }

        private static void AssertFileRule(XElement[] rules, string ruleName, string minLevel, string maxLevel, string writeTo)
        {
            XElement rule = rules.SingleOrDefault(element => string.Equals((string)element.Attribute("ruleName"), ruleName, StringComparison.Ordinal));
            Assert.That(rule, Is.Not.Null, ruleName);
            Assert.That((string)rule.Attribute("name"), Is.EqualTo("*"));
            Assert.That((string)rule.Attribute("minlevel"), Is.EqualTo(minLevel));
            Assert.That((string)rule.Attribute("maxlevel"), Is.EqualTo(maxLevel));
            Assert.That((string)rule.Attribute("writeTo"), Is.EqualTo(writeTo));
        }

        private static string ApplicationRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string WorkspaceRoot()
        {
            return Path.GetFullPath(Path.Combine(ApplicationRoot(), ".."));
        }

        private static string Normalize(string path)
        {
            return Path.GetFullPath(path);
        }
    }
}
