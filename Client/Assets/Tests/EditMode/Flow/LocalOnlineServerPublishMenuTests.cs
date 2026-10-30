using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class LocalOnlineServerPublishMenuTests
    {
        private const string PublishMenuPath = "Tools/游戏服务器/发布联机服务器";
        private const string LocalStartMenuPath = "Tools/游戏服务器/启动本地联机服务器";

        private Type MenuType => RequiredType("Game.Editor.LocalOnlineServerPublishMenu");

        [Test]
        public void UnityExposesSingleWindowsServerPublishMenuWithoutShortcut()
        {
            var publishMenus = TypeCache.GetMethodsWithAttribute<MenuItem>()
                .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>())
                .Where(item => item.menuItem == PublishMenuPath)
                .ToArray();

            Assert.That(publishMenus.Length, Is.EqualTo(1));
            Assert.That(publishMenus[0].validate, Is.False);
            Assert.That(ContainsShortcut(publishMenus[0].menuItem), Is.False);
            Assert.That(publishMenus[0].priority, Is.EqualTo(211));
        }

        [Test]
        public void PublishMenuPreservesExistingLocalStartMenu()
        {
            var menus = TypeCache.GetMethodsWithAttribute<MenuItem>()
                .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>())
                .ToArray();

            Assert.That(menus.Count(item => item.menuItem == LocalStartMenuPath), Is.EqualTo(1));
            Assert.That(menus.Single(item => item.menuItem == LocalStartMenuPath).priority, Is.EqualTo(210));
            Assert.That(ReadEditorSource("LocalOnlineServerMenu.cs"), Does.Contain(LocalStartMenuPath));
        }

        [Test]
        public void ResolvedPublishPathsStayUnderWorkspaceGameServer()
        {
            string workspace = Normalize(Path.Combine(Path.GetTempPath(), "AD Publish Menu Tests", Guid.NewGuid().ToString("N")));
            string dataPath = Path.Combine(workspace, "Client", "Assets");

            object paths = Invoke("ResolvePaths", dataPath);

            Assert.That(GetField(paths, "ClientRoot"), Is.EqualTo(Path.Combine(workspace, "Client")));
            Assert.That(GetField(paths, "WorkspaceRoot"), Is.EqualTo(workspace));
            Assert.That(GetField(paths, "GameServerRoot"), Is.EqualTo(Path.Combine(workspace, "Server", "GameServer")));
            Assert.That(GetField(paths, "ScriptPath"), Is.EqualTo(Path.Combine(workspace, "Server", "GameServer", "Publish-win-x64.ps1")));
        }

        [Test]
        public void PublishStartInfoUsesVisiblePowerShellAndQuotedRoot()
        {
            string workspace = Normalize(Path.Combine(Path.GetTempPath(), "AD Publish Menu Tests With Spaces", Guid.NewGuid().ToString("N")));
            object paths = Invoke("ResolvePaths", Path.Combine(workspace, "Client", "Assets"));

            var info = (ProcessStartInfo)Invoke("CreatePowerShellStartInfo", paths);

            Assert.That(info.FileName, Is.EqualTo("powershell.exe"));
            Assert.That(info.WorkingDirectory, Is.EqualTo(Path.Combine(workspace, "Server", "GameServer")));
            Assert.That(info.UseShellExecute, Is.True);
            Assert.That(info.WindowStyle, Is.EqualTo(ProcessWindowStyle.Normal));
            Assert.That(info.CreateNoWindow, Is.False);
            Assert.That(info.Arguments, Does.Contain("-NoLogo"));
            Assert.That(info.Arguments, Does.Contain("-NoProfile"));
            Assert.That(info.Arguments, Does.Contain("-ExecutionPolicy Bypass"));
            Assert.That(info.Arguments, Does.Contain("-File"));
            Assert.That(info.Arguments, Does.Contain("\"" + Path.Combine(workspace, "Server", "GameServer", "Publish-win-x64.ps1") + "\""));
            Assert.That(info.Arguments, Does.Contain("-GameServerRoot"));
            Assert.That(info.Arguments, Does.Contain("\"" + Path.Combine(workspace, "Server", "GameServer") + "\""));
            Assert.That(info.Arguments, Does.Not.Contain("-Port"));
        }

        [Test]
        public void SourcePublishBatchDelegatesToPowerShellWithOwnRootAndExitCode()
        {
            string batch = ReadSourcePublishBatch();

            Assert.That(batch, Does.Contain("%~dp0"));
            Assert.That(batch, Does.Contain("%%~fI"));
            Assert.That(batch, Does.Not.Contain("set \"GAME_SERVER_ROOT=%~dp0\""));
            Assert.That(batch, Does.Contain("Publish-win-x64.ps1"));
            Assert.That(batch, Does.Contain("-GameServerRoot"));
            Assert.That(batch, Does.Contain("powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File"));
            Assert.That(batch, Does.Contain("%ERRORLEVEL%"));
            Assert.That(batch, Does.Contain("exit /b"));
            Assert.That(batch, Does.Not.Contain("dotnet publish"));
            Assert.That(batch, Does.Not.Contain("pause"));
            Assert.That(batch, Does.Not.Contain("App.dll --AppType"));
        }

        [Test]
        public void PublishedStartBatchDelegatesToOwnedLauncherWithPublishedRoot()
        {
            string batch = ReadPublishedStartBatch();

            Assert.That(batch, Does.Contain("%~dp0"));
            Assert.That(batch, Does.Contain("%%~fI"));
            Assert.That(batch, Does.Not.Contain("set \"GAME_SERVER_ROOT=%~dp0\""));
            Assert.That(batch, Does.Contain("Tools\\start-local-online-server.ps1"));
            Assert.That(batch, Does.Contain("-GameServerRoot"));
            Assert.That(batch, Does.Contain("-Port 31001"));
            Assert.That(batch, Does.Contain("powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File"));
            Assert.That(batch, Does.Contain("%ERRORLEVEL%"));
            Assert.That(batch, Does.Contain("exit /b"));
            Assert.That(batch, Does.Not.Contain("App.exe"));
            Assert.That(batch, Does.Not.Contain("dotnet App.dll"));
            Assert.That(batch, Does.Not.Contain("pause"));
            Assert.That(batch, Does.Not.Contain("taskkill"));
            Assert.That(batch, Does.Not.Contain("goto"));
        }

        [Test]
        public void PublishedStartDoesNotRequestSourceBuild()
        {
            string batch = ReadPublishedStartBatch();

            Assert.That(batch, Does.Not.Contain("BuildBeforeStart"));
            Assert.That(batch, Does.Not.Contain("dotnet build"));
            Assert.That(batch, Does.Contain("Tools\\start-local-online-server.ps1"));
        }

        [Test]
        public void PublishScriptUsesExplicitFrameworkDependentWinX64AppProject()
        {
            string script = ReadPublishScript();

            Assert.That(script, Does.Contain("DotNet\\App\\DotNet.App.csproj"));
            Assert.That(script, Does.Contain("publish"));
            Assert.That(script, Does.Contain("win-x64"));
            Assert.That(script, Does.Contain("Release"));
            Assert.That(script, Does.Contain("--self-contained"));
            Assert.That(script, Does.Contain("false"));
            Assert.That(script, Does.Contain("Publish\\win-x64"));
            Assert.That(script, Does.Contain("Bin"));
            Assert.That(script, Does.Contain("Copy-PublishConfig"));
            Assert.That(script, Does.Contain("Copy-Item"));
            Assert.That(script, Does.Not.Contain("--self-contained true"));
            Assert.That(script, Does.Not.Contain("ET.sln"));
            Assert.That(script, Does.Not.Contain("--no-dependencies"));
            Assert.That(script, Does.Not.Contain("--runtime"));
        }

        [Test]
        public void PublishScriptCleansOnlyValidatedWinX64Outputs()
        {
            string script = ReadPublishScript();

            Assert.That(script, Does.Contain("Assert-ChildPath"));
            Assert.That(script, Does.Contain("Remove-PublishOutputs"));
            Assert.That(script, Does.Contain("Remove-Item"));
            Assert.That(script, Does.Contain("$Context.Bin"));
            Assert.That(script, Does.Contain("$Context.Config"));
            Assert.That(script, Does.Contain("$Context.Tools"));
            Assert.That(script, Does.Contain("$Context.PublishedStartBat"));
            Assert.That(script, Does.Contain("$Context.PublishedDeploymentGuide"));
            Assert.That(script, Does.Contain("StartsWith"));
            Assert.That(script, Does.Contain("拒绝修改"));
            Assert.That(script, Does.Not.Contain("Remove-Item -LiteralPath $Context.PublishRoot"));
            Assert.That(script, Does.Not.Contain("Remove-Item -LiteralPath $Context.GameServerRoot"));
            Assert.That(script, Does.Not.Contain("Remove-Item -LiteralPath $Context.SourceLauncher"));
        }

        [Test]
        public void PublishScriptValidatesRequiredDeploymentFilesAndConfig()
        {
            string script = ReadPublishScript();

            Assert.That(script, Does.Contain("dotnet --list-sdks"));
            Assert.That(script, Does.Contain("^\\s*8\\."));
            Assert.That(script, Does.Contain("dotnet --version"));
            Assert.That(script, Does.Contain("global.json"));
            Assert.That(script, Does.Contain("Config\\NLog\\NLog.config"));
            Assert.That(script, Does.Contain("Config\\Luban"));
            Assert.That(script, Does.Contain("Validate-PublishOutput"));
            Assert.That(script, Does.Contain("App.exe"));
            Assert.That(script, Does.Contain("App.dll"));
            Assert.That(script, Does.Contain("App.runtimeconfig.json"));
            Assert.That(script, Does.Contain("App.deps.json"));
            Assert.That(script, Does.Contain("启动联机服务器.bat"));
            Assert.That(script, Does.Contain("Tools\\start-local-online-server.ps1"));
            Assert.That(script, Does.Contain("局域网部署说明.md"));
        }

        [Test]
        public void PublishScriptUsesExclusiveLockAndFailureCleanup()
        {
            string script = ReadPublishScript();

            Assert.That(script, Does.Contain("Acquire-PublishLock"));
            Assert.That(script, Does.Contain("Release-PublishLock"));
            Assert.That(script, Does.Contain("win-x64.publish.lock"));
            Assert.That(script, Does.Contain("[System.IO.FileStream]::new"));
            Assert.That(script, Does.Contain("[System.IO.FileShare]::None"));
            Assert.That(script, Does.Contain("finally"));
            Assert.That(script, Does.Contain("$LASTEXITCODE -ne 0"));
            Assert.That(script, Does.Contain("$script:rebuildStarted"));
            Assert.That(script, Does.Contain("Read-Host"));
            Assert.That(script, Does.Contain("按 Enter"));
            Assert.That(script, Does.Contain("exit $script:exitCode"));
        }

        [Test]
        public void PublishScriptDeploysStartBatchAndLauncherTransactionally()
        {
            string script = ReadPublishScript();

            Assert.That(script, Does.Contain("SourcePublishBat"));
            Assert.That(script, Does.Contain("SourceStartBat"));
            Assert.That(script, Does.Contain("SourceLauncher"));
            Assert.That(script, Does.Contain("Copy-DeploymentLaunchers"));
            Assert.That(script, Does.Contain("PublishedLauncher"));
            Assert.That(script, Does.Contain("PublishedStartBat"));
            Assert.That(script, Does.Contain("New-Item -ItemType Directory -Force -Path $safeTools"));
            Assert.That(script, Does.Contain("$script:rebuildStarted = $true"));
        }

        [Test]
        public void PublishScriptWithLocalizedDeploymentNameUsesUtf8BomForWindowsPowerShell()
        {
            AssertUtf8Bom(PublishScriptPath());
            Assert.That(ReadPublishScript(), Does.Contain("启动联机服务器.bat"));
        }

        [Test]
        public void PublishUsesProjectScopedNet8SdkSelectionWithoutChangingGlobalNuGet()
        {
            string globalJson = ReadGlobalJson();
            string script = ReadPublishScript();

            Assert.That(globalJson, Does.Contain("\"version\": \"8.0.100\""));
            Assert.That(globalJson, Does.Contain("\"rollForward\": \"latestFeature\""));
            Assert.That(globalJson, Does.Contain("\"allowPrerelease\": false"));
            Assert.That(script, Does.Contain("Push-Location -LiteralPath $Root"));
            Assert.That(script, Does.Contain("Push-Location -LiteralPath $Context.GameServerRoot"));
            Assert.That(script, Does.Contain("dotnet --version"));
            Assert.That(script, Does.Contain("dotnet --list-sdks"));
            Assert.That(script, Does.Not.Contain("dotnet nuget enable source"));
            Assert.That(script, Does.Not.Contain("dotnet nuget disable source"));
            Assert.That(script, Does.Not.Contain("dotnet nuget add source"));
            Assert.That(script, Does.Not.Contain("dotnet nuget remove source"));
            Assert.That(script, Does.Not.Contain("NuGet.Config"));
        }

        [Test]
        public void FrameworkDependentPublishDoesNotRequireRemoteNuGetOrRidRuntimePacks()
        {
            string script = ReadPublishScript();

            Assert.That(script, Does.Contain("--self-contained"));
            Assert.That(script, Does.Contain("false"));
            Assert.That(script, Does.Contain("-p:NuGetAudit=false"));
            Assert.That(script, Does.Not.Contain("--runtime"));
            Assert.That(script, Does.Not.Contain("--source"));
            Assert.That(script, Does.Not.Contain("RuntimeIdentifier"));
            Assert.That(script, Does.Not.Contain("RuntimeIdentifiers"));
            Assert.That(script, Does.Not.Contain("Microsoft.NETCore.App.Runtime.win-x64"));
        }

        [Test]
        public void PublishAndLauncherScriptsUseChinesePromptsWithRequiredTechnicalTokens()
        {
            string publish = ReadPublishScript();
            string launcher = ReadLauncherScript();

            AssertUtf8Bom(PublishScriptPath());
            AssertUtf8Bom(LauncherScriptPath());
            Assert.That(publish, Does.Contain("正在准备 Windows 联机服务器发布"));
            Assert.That(publish, Does.Contain("发布成功"));
            Assert.That(publish, Does.Contain("发布失败"));
            Assert.That(publish, Does.Contain(".NET 8 Runtime"));
            Assert.That(publish, Does.Contain("App.dll"));
            Assert.That(launcher, Does.Contain("[本地联机服务器]"));
            Assert.That(launcher, Does.Not.Contain("[LocalOnlineServer]"));
            Assert.That(launcher, Does.Contain("按 R 重启"));
            Assert.That(launcher, Does.Contain("Ctrl+C"));
            Assert.That(launcher, Does.Contain("关闭窗口"));
            Assert.That(launcher, Does.Contain("异常退出后可按 R 重启"));
            Assert.That(launcher, Does.Contain("日志目录"));
            Assert.That(launcher, Does.Contain("OnlineLocal"));
            Assert.That(launcher, Does.Contain("PID"));
            Assert.That(launcher, Does.Contain("ExitCode"));
            Assert.That(launcher, Does.Contain("同一 Wi-Fi 安卓地址"));
            Assert.That(launcher, Does.Contain("0.0.0.0"));
            Assert.That(launcher, Does.Contain("TCP"));
        }

        [Test]
        public void PublishScriptDoesNotStartServerOrTouchLinuxLogsStore()
        {
            string script = ReadPublishScript();

            Assert.That(script, Does.Not.Contain("linux-x64"));
            Assert.That(script, Does.Not.Contain("Start-Process App"));
            Assert.That(script, Does.Not.Contain("Start-Process"));
            Assert.That(script, Does.Not.Contain("App.dll --AppType"));
            Assert.That(script, Does.Not.Contain("Logs"));
            Assert.That(script, Does.Not.Contain("Store"));
            Assert.That(script, Does.Not.Contain("Publish-linux"));
        }

        [Test]
        public void SharedLauncherForwardsNetLogCommandsAndPreservesLifecycleControls()
        {
            string launcher = ReadLauncherScript();
            string publish = ReadPublishScript();

            AssertUtf8Bom(LauncherScriptPath());
            Assert.That(launcher, Does.Contain("RedirectStandardInput = $true"));
            Assert.That(launcher, Does.Contain("netlog on"));
            Assert.That(launcher, Does.Contain("netlog off"));
            Assert.That(launcher, Does.Contain("netlog status"));
            Assert.That(launcher, Does.Contain("按 R 重启"));
            Assert.That(launcher, Does.Contain("Ctrl+C"));
            Assert.That(launcher, Does.Contain("异常退出后可按 R 重启"));
            Assert.That(publish, Does.Contain("SourceLauncher"));
            Assert.That(publish, Does.Contain("Copy-Item -LiteralPath $Context.SourceLauncher -Destination $Context.PublishedLauncher -Force"));
            Assert.That(publish, Does.Not.Contain("start-local-online-server-copy.ps1"));
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

        private static Type RequiredType(string fullName)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName))
                .FirstOrDefault(found => found != null);
            Assert.That(type, Is.Not.Null, fullName);
            return type;
        }

        private static string GetField(object target, string name)
        {
            return (string)target.GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .GetValue(target);
        }

        private static void AssertUtf8Bom(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(3), path);
            Assert.That(bytes.Take(3).ToArray(), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }), path);
        }

        private static string ReadEditorSource(string fileName)
        {
            return File.ReadAllText(Path.Combine(ApplicationRoot(), "Assets", "Editor", "GameServer", fileName), Encoding.UTF8);
        }

        private static string ReadPublishScript()
        {
            return File.ReadAllText(PublishScriptPath(), Encoding.UTF8);
        }

        private static string ReadLauncherScript()
        {
            return File.ReadAllText(LauncherScriptPath(), Encoding.UTF8);
        }

        private static string ReadSourcePublishBatch()
        {
            return File.ReadAllText(Path.Combine(WorkspaceRoot(), "Server", "GameServer", "Publish-win-x64.bat"), Encoding.UTF8);
        }

        private static string ReadPublishedStartBatch()
        {
            return File.ReadAllText(Path.Combine(WorkspaceRoot(), "Server", "GameServer", "Tools", "start-published-online-server.bat"), Encoding.UTF8);
        }

        private static string ReadGlobalJson()
        {
            return File.ReadAllText(Path.Combine(WorkspaceRoot(), "Server", "GameServer", "global.json"), Encoding.UTF8);
        }

        private static string PublishScriptPath()
        {
            return Path.Combine(WorkspaceRoot(), "Server", "GameServer", "Publish-win-x64.ps1");
        }

        private static string LauncherScriptPath()
        {
            return Path.Combine(WorkspaceRoot(), "Server", "GameServer", "Tools", "start-local-online-server.ps1");
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
