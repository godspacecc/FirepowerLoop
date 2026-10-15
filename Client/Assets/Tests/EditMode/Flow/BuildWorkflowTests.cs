using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>仅验证 PC 构建的重载门禁与程序集清单，不切平台、不执行 Player 构建。</summary>
    public class WindowsBuildSafetyTests
    {
        private static MethodInfo SafetyMethod(string name)
        {
            Type type = Array.Find(AppDomain.CurrentDomain.GetAssemblies(),
                assembly => assembly.GetType("Game.Editor.WindowsBuildSafety") != null)
                ?.GetType("Game.Editor.WindowsBuildSafety");
            Assert.That(type, Is.Not.Null, "PC 构建缺少防止旧平台回调继续执行的安全门禁。");
            MethodInfo method = type.GetMethod(name);
            Assert.That(method, Is.Not.Null);
            return method;
        }

        [TestCase(false, false, false, false)]
        [TestCase(true, true, false, false)]
        [TestCase(true, false, true, false)]
        [TestCase(true, true, true, false)]
        [TestCase(true, false, false, true)]
        public void ResumeRequiresReloadAndIdleEditor(bool reloaded, bool compiling, bool updating, bool expected)
        {
            bool ready = (bool)SafetyMethod("CanResume").Invoke(null, new object[] { reloaded, compiling, updating });
            Assert.That(ready, Is.EqualTo(expected));
        }

        [TestCase("{\"names\":[\"Boot.dll\"],\"types\":[16]}", false)]
        [TestCase("{\"names\":[\"Boot.dll\",\"Game.dll\"],\"types\":[16,16]}", true)]
        [TestCase("{\"names\":[\"Game.dll\"],\"types\":[0]}", false)]
        [TestCase("{\"names\":[\"Game.dll\"],\"types\":[]}", false)]
        [TestCase("{\"names\":[\"Game.dll\",\"Game.dll\"],\"types\":[16,16]}", false)]
        [TestCase("{not-json", false)]
        public void PlayerAssemblyListRejectsMissingOrInvalidHotUpdateRegistration(string json, bool expected)
        {
            object[] args = { json, new[] { "Game.dll" }, null };
            bool valid = (bool)SafetyMethod("TryValidateAssemblyList").Invoke(null, args);
            Assert.That(valid, Is.EqualTo(expected));
            if (!expected) Assert.That(args[2], Is.Not.Null.And.Not.Empty);
        }
    }

    [TestFixture]
    public class BuildWorkflowTests
    {
        [Test]
        public void PlayerWorkflowsPublishInstallerAfterPlayerValidation()
        {
            string source = File.ReadAllText(Path.Combine(Application.dataPath, "Editor/Build/BuildUtility.cs"));
            foreach (string name in new[] { "BuildLatestCodeAndResourcesBatch", "BuildSmallHostBaseCodeAndResourcesBatch", "BuildRecentHostPlayerBatch", "BuildQuickAotPlayerBatch" })
            {
                int start = source.IndexOf("public static void " + name + "()", StringComparison.Ordinal);
                int end = source.IndexOf("public static void ", start + 1, StringComparison.Ordinal);
                string body = source.Substring(start, end - start);
                Assert.That(body, Does.Contain("WindowsInstallerPublishUtility.EnsureCompiler"), name);
                Assert.That(body, Does.Contain("WindowsInstallerPublishUtility.GenerateAndPublish"), name);
                Assert.That(body.IndexOf("WindowsInstallerPublishUtility.GenerateAndPublish", StringComparison.Ordinal),
                    Is.GreaterThan(body.IndexOf("BuildWindowsPlayer", StringComparison.Ordinal)), name);
            }
        }

        [Test]
        public void InstallerAllowsFirstDirectoryChoiceAndLocksUpgradeToRegisteredDirectory()
        {
            string source = File.ReadAllText(Path.Combine(Application.dataPath, "../../Tools/WindowsInstaller/AnotherDay.iss"));
            Assert.That(source, Does.Contain("UsePreviousAppDir=yes"));
            Assert.That(source, Does.Contain("DisableDirPage=auto"));
            Assert.That(source, Does.Contain("ExistingInstallDir"));
            Assert.That(source, Does.Contain("GetSpaceOnDisk64(ExtractFileDrive(Root)"));
            Assert.That(source, Does.Not.Contain("This installer uses a fixed current-user install directory."));
        }

        [Test]
        public void InstallerPublicationRejectsWrongReleaseAndCorruptedBytes()
        {
            string root = Path.Combine(Path.GetTempPath(), "AnotherDayInstallerValidation_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string installer = Path.Combine(root, "Setup.exe");
                File.WriteAllBytes(installer, new byte[] { 1, 2, 3 });
                string hash;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(installer))).Replace("-", "");
                var manifest = new Boot.WindowsUpdateManifest { schemaVersion = 1, productId = "com.firepowerloop.game",
                    platform = "Windows", architecture = "x64", versionName = "0.1.0", releaseNumber = 3,
                    installerFileName = "Setup.exe", sizeBytes = 3, sha256 = hash };
                File.WriteAllText(Path.Combine(root, "app-update-windows.json"), JsonUtility.ToJson(manifest));
                var method = RequiredType("Game.Editor.WindowsInstallerPublishUtility").GetMethod("ValidateInstaller", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method.Invoke(null, new object[] { root, 3 }), Is.EqualTo(installer));
                Assert.That(Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { root, 4 })).InnerException,
                    Is.TypeOf<InvalidOperationException>());
                File.WriteAllBytes(installer, new byte[] { 3, 2, 1 });
                Assert.That(Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { root, 3 })).InnerException,
                    Is.TypeOf<InvalidOperationException>());
            }
            finally { Directory.Delete(root, true); }
        }

        private Type BuildStateType => RequiredType("Game.Editor.BuildState");
        private Type HostRecordType => RequiredType("Game.Editor.HostBuildRecord");
        private Type AotRecordType => RequiredType("Game.Editor.AotBaselineRecord");
        private Type PackageRecordType => RequiredType("Game.Editor.PackageBuildRecord");
        private Type PlayerRecordType => RequiredType("Game.Editor.PlayerBuildRecord");
        private Type BuildStateStoreType => RequiredType("Game.Editor.BuildStateStore");
        private Type LocalStateType => RequiredType("Game.Editor.LocalServerProcessState");
        private Type ProcessSnapshotType => RequiredType("Game.Editor.ProcessSnapshot");
        private Type LocalServerUtilityType => RequiredType("Game.Editor.LocalResourceServerUtility");
        private Type YooAssetBuildUtilityType => RequiredType("Game.Editor.YooAssetBuildUtility");

        [Test]
        public void WindowsSmallHostBaseBuildCreatesManifestOnlyPlayerAndPublishesMatchingResources()
        {
            string buildSource = File.ReadAllText(Path.Combine(Application.dataPath, "Editor/Build/BuildUtility.cs"));
            string publishSource = File.ReadAllText(Path.Combine(Application.dataPath, "Editor/YooAsset/ResourceServerPublishUtility.cs"));
            string body = ExtractSourceRange(
                buildSource,
                "public static void BuildSmallHostBaseCodeAndResourcesBatch()",
                "public static void BuildHostCodeAndResourcesBatch()");

            Assert.That(buildSource, Does.Contain("Tools/打包/PC/首包完整打包"));
            Assert.That(body, Does.Contain("HybridCLRBuildUtility.GeneratePrePlayer(target)"));
            Assert.That(body, Does.Contain("BuildPreparatoryPlayer"));
            Assert.That(body, Does.Contain("HybridCLRBuildUtility.GeneratePostPlayer(target)"));
            Assert.That(body, Does.Contain("BuildManifestOnlyPackageOrThrow(version)"));
            Assert.That(body, Does.Contain("ValidateManifestOnlyBuiltinOrThrow(version)"));
            Assert.That(body, Does.Contain("BuildWindowsPlayer"));
            Assert.That(body, Does.Contain("WindowsInstallerPublishUtility.GenerateAndPublish"));
            Assert.That(body, Does.Contain("\"SmallHostBase\""));
            Assert.That(body, Does.Contain("updateAot: true"));
            Assert.That(body, Does.Contain("updateHost: true"));
            Assert.That(publishSource, Does.Contain("public static ResourceServerPublishResult PublishOrUpdateLatestPackage()"));
            Assert.That(publishSource, Does.Contain("Client\", \"Bundles\", \"StandaloneWindows64\""));
        }

        [Test]
        public void BuildStateRoundTripKeepsAllRecordGroups()
        {
            object state = Activator.CreateInstance(BuildStateType);
            SetField(state, "LastHost", CreateHostRecord("202607161500", "StandaloneWindows64", "C:/Host", "C:/Dlls"));
            SetField(state, "LastAotBaseline", CreateAotRecord("StandaloneWindows64", "C:/Aot", "C:/Dlls/HybridCLRAssemblyList.bytes", "C:/Dlls"));
            SetField(state, "LastPackage", CreatePackageRecord("202607161500", "StandaloneWindows64", "C:/Package", "Host"));
            SetField(state, "LastPlayer", CreatePlayerRecord("C:/Player/AnotherDay.exe", "202607161500", "Full"));

            string json = JsonUtility.ToJson(state, true);
            object restored = JsonUtility.FromJson(json, BuildStateType);

            Assert.That(GetField(GetField(restored, "LastHost"), "Version"), Is.EqualTo("202607161500"));
            Assert.That(GetField(GetField(restored, "LastAotBaseline"), "AotDirectory"), Is.EqualTo("C:/Aot"));
            Assert.That(GetField(GetField(restored, "LastPackage"), "BuildKind"), Is.EqualTo("Host"));
            Assert.That(GetField(GetField(restored, "LastPlayer"), "PlayerPath"), Is.EqualTo("C:/Player/AnotherDay.exe"));
        }

        [Test]
        public void CorruptJsonLoadReturnsEmptyStateAndWarning()
        {
            string statePath = (string)GetProperty(BuildStateStoreType, "StatePath").GetValue(null);
            string backup = BackupFile(statePath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(statePath));
                File.WriteAllText(statePath, "{not-valid-json", System.Text.Encoding.UTF8);

                object[] args = { null };
                object state = BuildStateStoreType.GetMethod("Load").Invoke(null, args);

                Assert.That(state, Is.Not.Null);
                Assert.That(args[0] as string, Is.Not.Null.And.Not.Empty);
                Assert.That(GetField(state, "LastHost"), Is.Null);
            }
            finally
            {
                RestoreFile(statePath, backup);
            }
        }

        [Test]
        public void MissingHostRecordFailsRecentHostValidation()
        {
            object state = Activator.CreateInstance(BuildStateType);
            object[] args = { state, UnityEditor.BuildTarget.StandaloneWindows64, null };

            bool ok = (bool)BuildStateStoreType.GetMethod("TryValidateRecentHost").Invoke(null, args);

            Assert.That(ok, Is.False);
            Assert.That(args[2] as string, Does.Contain("Host"));
        }

        [Test]
        public void PlatformMismatchFailsRecentHostAndAotValidation()
        {
            object state = Activator.CreateInstance(BuildStateType);
            SetField(state, "LastHost", CreateHostRecord("202607161500", "StandaloneOSX", "C:/Host", "C:/Dlls"));
            SetField(state, "LastAotBaseline", CreateAotRecord("StandaloneOSX", "C:/Aot", "C:/List.bytes", "C:/Dlls"));

            object[] hostArgs = { state, UnityEditor.BuildTarget.StandaloneWindows64, null };
            object[] aotArgs = { state, UnityEditor.BuildTarget.StandaloneWindows64, null };

            bool hostOk = (bool)BuildStateStoreType.GetMethod("TryValidateRecentHost").Invoke(null, hostArgs);
            bool aotOk = (bool)BuildStateStoreType.GetMethod("TryValidateAotBaseline").Invoke(null, aotArgs);

            Assert.That(hostOk, Is.False);
            Assert.That(aotOk, Is.False);
            Assert.That(hostArgs[2] as string, Does.Contain("平台"));
            Assert.That(aotArgs[2] as string, Does.Contain("平台"));
        }

        [Test]
        public void MissingAotBaselineFileReportsConcretePath()
        {
            string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "HybridCLRAssemblyList.bytes");
            object state = Activator.CreateInstance(BuildStateType);
            SetField(state, "LastAotBaseline", CreateAotRecord("StandaloneWindows64", Path.GetDirectoryName(missing), missing, Path.GetDirectoryName(missing)));

            object[] args = { state, UnityEditor.BuildTarget.StandaloneWindows64, null };
            bool ok = (bool)BuildStateStoreType.GetMethod("TryValidateAotBaseline").Invoke(null, args);

            Assert.That(ok, Is.False);
            Assert.That(args[2] as string, Does.Contain(missing));
        }

        [Test]
        public void LocalServerOwnershipRequiresMatchingPidStartTimeNameAndScript()
        {
            object state = Activator.CreateInstance(LocalStateType);
            SetField(state, "ProcessId", 123);
            SetField(state, "StartTimeUtcTicks", 456L);
            SetField(state, "ProcessName", "cmd");
            SetField(state, "ScriptPath", "C:/Project/start_yooasset_local_server.bat");

            object snapshot = Activator.CreateInstance(ProcessSnapshotType);
            SetField(snapshot, "ProcessId", 123);
            SetField(snapshot, "StartTimeUtcTicks", 456L);
            SetField(snapshot, "ProcessName", "cmd");

            bool owned = InvokeIsOwned(state, snapshot, "C:/Project/start_yooasset_local_server.bat");

            SetField(snapshot, "ProcessId", 999);
            Assert.That(InvokeIsOwned(state, snapshot, "C:/Project/start_yooasset_local_server.bat"), Is.False);
            SetField(snapshot, "ProcessId", 123);

            SetField(snapshot, "StartTimeUtcTicks", 999L);
            bool stale = InvokeIsOwned(state, snapshot, "C:/Project/start_yooasset_local_server.bat");
            SetField(snapshot, "StartTimeUtcTicks", 456L);

            SetField(snapshot, "ProcessName", "notepad");
            Assert.That(InvokeIsOwned(state, snapshot, "C:/Project/start_yooasset_local_server.bat"), Is.False);

            SetField(snapshot, "ProcessName", "powershell");
            Assert.That(InvokeIsOwned(state, snapshot, "C:/Project/start_yooasset_local_server.bat"), Is.False);

            SetField(snapshot, "ProcessName", "cmd");
            SetField(state, "ProcessName", "powershell");
            Assert.That(InvokeIsOwned(state, snapshot, "C:/Project/start_yooasset_local_server.bat"), Is.False);
            SetField(state, "ProcessName", "cmd");

            Assert.That(InvokeIsOwned(state, snapshot, "C:/Project/other_server.bat"), Is.False);
            Assert.That(InvokeIsOwned(null, snapshot, "C:/Project/start_yooasset_local_server.bat"), Is.False);
            Assert.That(InvokeIsOwned(state, null, "C:/Project/start_yooasset_local_server.bat"), Is.False);

            Assert.That(owned, Is.True);
            Assert.That(stale, Is.False);
        }

        [Test]
        public void LocalServerStartInfoUsesVisibleCmdWindow()
        {
            string normalizedRoot = Path.GetFullPath("C:/Project/Bundles");
            ProcessStartInfo info = InvokeCreateServerStartInfo(normalizedRoot, false);
            ProcessStartInfo lanInfo = InvokeCreateServerStartInfo(normalizedRoot, true);

            Assert.That(info.FileName, Does.EndWith("start_yooasset_local_server.bat").IgnoreCase);
            Assert.That(info.FileName.Replace('\\', '/'), Does.Contain("Assets/Editor/YooAsset/LocalServer"));
            Assert.That(info.FileName.Replace('\\', '/'), Does.Not.Contain("Assets/Editor/AnotherDay"));
            Assert.That(info.UseShellExecute, Is.True);
            Assert.That(info.WindowStyle, Is.EqualTo(ProcessWindowStyle.Normal));
            Assert.That(info.Arguments, Does.Contain("8001"));
            Assert.That(info.Arguments, Does.Contain(normalizedRoot));
            Assert.That(info.Arguments, Does.Not.Contain(" lan"));
            Assert.That(lanInfo.Arguments, Does.Contain(" lan"));
        }

        /// <summary>使用启动器实际调用的 Windows PowerShell 解析文件，捕获中文脚本的编码兼容问题。</summary>
        [Test]
        public void WindowsPowerShellParsesResourceServerEntryScripts()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            var paths = new[] { "Server/ResourceServer/resource_server.ps1",
                "Server/ResourceServer/publish_app_update.ps1", "Server/ResourceServer/publish_windows_update.ps1",
                "Server/ResourceServer/validate_resource_update_notices.ps1",
                "Client/Assets/Editor/YooAsset/LocalServer/start_yooasset_local_server.ps1" };
            var quoted = Array.ConvertAll(paths, path => "'" + Path.Combine(root, path).Replace("'", "''") + "'");
            var command = "$ErrorActionPreference='Stop'; foreach($path in @(" + string.Join(",", quoted) +
                ")) { $tokens=$null; $errors=$null; [System.Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors) | Out-Null; " +
                "if(@($errors).Count -gt 0) { Write-Output $path; $errors | ForEach-Object { Write-Output $_.Message }; exit 1 } }; exit 0";
            var info = new ProcessStartInfo {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe"),
                Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand " +
                    Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command)),
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            };
            using (var process = Process.Start(info))
            {
                Assert.That(process.WaitForExit(10000), Is.True, "脚本语法检查超时");
                Assert.That(process.ExitCode, Is.Zero, process.StandardOutput.ReadToEnd());
            }
        }

        [Test]
        public void LocalServerOwnedTreeStopUsesTaskkill()
        {
            ProcessStartInfo info = InvokeCreateOwnedTreeStopStartInfo(123);

            Assert.That(info.FileName, Is.EqualTo("taskkill.exe"));
            Assert.That(info.Arguments, Does.Contain("/PID 123"));
            Assert.That(info.Arguments, Does.Contain("/T"));
            Assert.That(info.Arguments, Does.Contain("/F"));
            Assert.That(info.UseShellExecute, Is.False);
            Assert.That(info.CreateNoWindow, Is.True);
        }

        [Test]
        public void LocalServerRejectsUnknownPortOccupant()
        {
            bool free = InvokeCanStartOnPort(false, out string freeError);
            Assert.That(free, Is.True);
            Assert.That(freeError, Is.Null);

            bool occupied = InvokeCanStartOnPort(true, out string occupiedError);
            Assert.That(occupied, Is.False);
            Assert.That(occupiedError, Does.Contain("8000"));
            Assert.That(occupiedError, Does.Contain("未知"));
        }

        [Test]
        public void LocalServerDoesNotKeepLegacyProcessScanKillEntry()
        {
            var legacy = LocalServerUtilityType.GetMethod(
                "TryStopLegacyProjectServer",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(legacy, Is.Null, "不得保留绕过状态文件的旧进程扫描/强杀入口");
        }

        [Test]
        public void YooAssetFindLatestValidPackageIgnoresBuildState()
        {
            string packageRoot = CreateTempPackageRoot();
            try
            {
                CreateValidPackage(packageRoot, "202607170900");
                string newerDirectory = CreateValidPackage(packageRoot, "202607170930");

                bool ok = InvokeTryFindLatestValidPackage(packageRoot, out string packageDirectory, out string version, out string error);

                Assert.That(ok, Is.True);
                Assert.That(version, Is.EqualTo("202607170930"));
                Assert.That(packageDirectory, Is.EqualTo(Path.GetFullPath(newerDirectory)));
                Assert.That(error, Is.Null);
            }
            finally
            {
                DeleteDirectory(packageRoot);
            }
        }

        [Test]
        public void YooAssetFindLatestValidPackageSkipsNewerIncompleteDirectory()
        {
            string packageRoot = CreateTempPackageRoot();
            try
            {
                string validDirectory = CreateValidPackage(packageRoot, "202607170930");
                string incompleteDirectory = Path.Combine(packageRoot, "202607171000");
                Directory.CreateDirectory(incompleteDirectory);
                File.WriteAllText(Path.Combine(incompleteDirectory, "DefaultPackage.version"), "202607171000");

                bool ok = InvokeTryFindLatestValidPackage(packageRoot, out string packageDirectory, out string version, out string error);

                Assert.That(ok, Is.True);
                Assert.That(version, Is.EqualTo("202607170930"));
                Assert.That(packageDirectory, Is.EqualTo(Path.GetFullPath(validDirectory)));
                Assert.That(error, Is.Null);
            }
            finally
            {
                DeleteDirectory(packageRoot);
            }
        }

        [Test]
        public void YooAssetFindLatestValidPackageSkipsVersionFileMismatch()
        {
            string packageRoot = CreateTempPackageRoot();
            try
            {
                string validDirectory = CreateValidPackage(packageRoot, "202607170930");
                string mismatchDirectory = CreateValidPackage(packageRoot, "202607171100");
                File.WriteAllText(Path.Combine(mismatchDirectory, "DefaultPackage.version"), "202607171059");

                bool ok = InvokeTryFindLatestValidPackage(packageRoot, out string packageDirectory, out string version, out string error);

                Assert.That(ok, Is.True);
                Assert.That(version, Is.EqualTo("202607170930"));
                Assert.That(packageDirectory, Is.EqualTo(Path.GetFullPath(validDirectory)));
                Assert.That(error, Is.Null);
            }
            finally
            {
                DeleteDirectory(packageRoot);
            }
        }

        [Test]
        public void YooAssetFindLatestValidPackageReportsRootWhenNoneValid()
        {
            string packageRoot = CreateTempPackageRoot();
            try
            {
                string incompleteDirectory = Path.Combine(packageRoot, "202607171000");
                Directory.CreateDirectory(incompleteDirectory);
                File.WriteAllText(Path.Combine(incompleteDirectory, "DefaultPackage.version"), "202607171000");

                bool ok = InvokeTryFindLatestValidPackage(packageRoot, out string packageDirectory, out string version, out string error);

                Assert.That(ok, Is.False);
                Assert.That(packageDirectory, Is.Null);
                Assert.That(version, Is.Null);
                Assert.That(error, Does.Contain(Path.GetFullPath(packageRoot)));
                Assert.That(error, Does.Contain("有效"));
            }
            finally
            {
                DeleteDirectory(packageRoot);
            }
        }

        [Test]
        public void RecentHostPlayerWorkflowCreatesNewBuiltinPackageVersion()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string source = File.ReadAllText(Path.Combine(root, "Assets/Editor/Build/BuildUtility.cs"));

            string body = ExtractSourceRange(
                source,
                "public static void BuildRecentHostPlayerBatch()",
                "public static void BuildQuickAotPlayerBatch()");

            Assert.That(body, Does.Contain("BuildStateStore.TryValidateRecentHost"));
            Assert.That(body, Does.Contain("string version = CreateVersion();"));
            Assert.That(body, Does.Contain("BuildPackageOrThrow(version, true)"));
            Assert.That(body, Does.Contain("ValidateBuiltinPackage"));
            Assert.That(body, Does.Contain("ValidatePlayerAndBuiltin(player, version)"));
            Assert.That(body, Does.Contain("NewPackageRecord(version, \"RecentHost\", package.OutputPackageDirectory)"));
            Assert.That(body, Does.Contain("NewPlayerRecord(version, \"RecentHost\", player)"));
            Assert.That(body, Does.Not.Contain("state.LastHost.Version"));
            Assert.That(body, Does.Not.Contain("state.LastHost.OutputPackageDirectory"));
            Assert.That(body, Does.Not.Contain("BuildDefaultPackageFromHostVersion"));
            Assert.That(body, Does.Not.Contain("ReplaceBuiltinPackageFromRecentHost"));
            Assert.That(body, Does.Not.Contain("PrepareBuiltinFromRecentHost"));
            Assert.That(body, Does.Not.Contain("hostPackageDirectory"));
        }

        [Test]
        public void RecentHostWorkflowDoesNotKeepDirectHostBuiltinCopyHelper()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string source = File.ReadAllText(Path.Combine(root, "Assets/Editor/Build/BuildUtility.cs"));

            Assert.That(source, Does.Not.Contain("ReplaceBuiltinPackageFromRecentHost"));
            Assert.That(source, Does.Not.Contain("EnsureBuiltinPackageTarget"));
            Assert.That(source, Does.Not.Contain("PrepareBuiltinFromRecentHost"));
        }

        [Test]
        public void ValidatePlayerAndBuiltinRequiresBuildinCatalogFiles()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string source = File.ReadAllText(Path.Combine(root, "Assets/Editor/Build/BuildUtility.cs"));

            string body = ExtractSourceRange(
                source,
                "private static void ValidatePlayerAndBuiltin(string playerPath, string version)",
                "private static void ValidateExistingPlayerAot()");

            Assert.That(body, Does.Contain("YooAssetBuildUtility.GetStreamingAssetsDirectory()"));
            Assert.That(body, Does.Contain("ValidatePackageOrThrow(builtinDirectory, version)"));
            Assert.That(body, Does.Contain("BuildinCatalog.bytes"));
            Assert.That(body, Does.Contain("BuildinCatalog.json"));
        }

        [Test]
        public void DiagnosticReleaseWorkflowsForceNonDevelopmentAndRestoreEditorSetting()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string source = File.ReadAllText(Path.Combine(root, "Assets/Editor/Build/BuildUtility.cs"));

            Assert.That(source, Does.Contain("var previousDevelopment = EditorUserBuildSettings.development;"));
            Assert.That(source, Does.Contain("EditorUserBuildSettings.development = false;"));
            Assert.That(source, Does.Contain("EditorUserBuildSettings.development = previousDevelopment;"));
            StringAssert.IsMatch(@"finally\s*\{[\s\S]*EditorUserBuildSettings\.development\s*=\s*previousDevelopment;", source);
            Assert.That(source, Does.Contain("options = BuildOptions.None"));
            Assert.That(source, Does.Not.Contain("BuildOptions.Development"));
        }

        [Test]
        public void PcWorkflowsAutomaticallySwitchToWindowsTargetBeforeBuilding()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string source = File.ReadAllText(Path.Combine(root, "Assets/Editor/Build/BuildUtility.cs"));

            string runWorkflow = ExtractSourceRange(
                source,
                "private static void RunWorkflow(string workflowName, Action<BuildStageTiming> action, bool cleanupPrepPlayer)",
                "private static bool BeginBuild()");
            string ensureTarget = ExtractSourceRange(
                source,
                "private static void EnsureWindowsBuildTarget()",
                "private static void ValidateProjectSettings()");

            Assert.That(runWorkflow, Does.Contain("timing.Run(\"EnsureWindowsBuildTarget\", EnsureWindowsBuildTarget)"));
            Assert.That(ensureTarget, Does.Contain("EditorUserBuildSettings.SwitchActiveBuildTarget"));
            Assert.That(ensureTarget, Does.Contain("BuildTarget.StandaloneWindows64"));
            Assert.That(ensureTarget, Does.Contain("BuildPipeline.IsBuildTargetSupported"));
        }

        [Test]
        public void HybridClrHotUpdateCompilationIsAlwaysNonDevelopment()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string source = File.ReadAllText(Path.Combine(root, "Assets/Editor/Build/HybridCLRBuildUtility.cs"));

            Assert.That(source, Does.Contain("CompileDllCommand.CompileDll(target, false);"));
            Assert.That(source, Does.Not.Contain("CompileDllCommand.CompileDll(target, EditorUserBuildSettings.development);"));
        }

        private object CreateHostRecord(string version, string target, string output, string dllDir)
        {
            object record = Activator.CreateInstance(HostRecordType);
            SetField(record, "Version", version);
            SetField(record, "BuildTarget", target);
            SetField(record, "OutputPackageDirectory", output);
            SetField(record, "GameDllDirectory", dllDir);
            SetField(record, "CompletedUtcTicks", DateTime.UtcNow.Ticks);
            return record;
        }

        private object CreateAotRecord(string target, string aotDir, string listPath, string dllDir)
        {
            object record = Activator.CreateInstance(AotRecordType);
            SetField(record, "BuildTarget", target);
            SetField(record, "AotDirectory", aotDir);
            SetField(record, "RuntimeListPath", listPath);
            SetField(record, "GameDllDirectory", dllDir);
            SetField(record, "CompletedUtcTicks", DateTime.UtcNow.Ticks);
            return record;
        }

        private object CreatePackageRecord(string version, string target, string output, string kind)
        {
            object record = Activator.CreateInstance(PackageRecordType);
            SetField(record, "Version", version);
            SetField(record, "BuildTarget", target);
            SetField(record, "OutputPackageDirectory", output);
            SetField(record, "BuildKind", kind);
            SetField(record, "CompletedUtcTicks", DateTime.UtcNow.Ticks);
            return record;
        }

        private object CreatePlayerRecord(string path, string version, string kind)
        {
            object record = Activator.CreateInstance(PlayerRecordType);
            SetField(record, "PlayerPath", path);
            SetField(record, "Version", version);
            SetField(record, "BuildKind", kind);
            SetField(record, "CompletedUtcTicks", DateTime.UtcNow.Ticks);
            return record;
        }

        private static Type RequiredType(string fullName)
        {
            var type = Type.GetType($"{fullName}, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null, $"Missing editor type: {fullName}");
            return type;
        }

        private static PropertyInfo GetProperty(Type type, string name)
        {
            return type.GetProperty(name, BindingFlags.Static | BindingFlags.Public);
        }

        private bool InvokeIsOwned(object state, object snapshot, string expectedScript)
        {
            var method = LocalServerUtilityType.GetMethod(
                "IsOwnedProcess",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (bool)method.Invoke(null, new[] { state, snapshot, expectedScript });
        }

        private ProcessStartInfo InvokeCreateServerStartInfo(string normalizedRoot, bool listenOnLan)
        {
            var method = LocalServerUtilityType.GetMethod(
                "CreateServerStartInfo",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (ProcessStartInfo)method.Invoke(null, new object[] { normalizedRoot, listenOnLan });
        }

        private ProcessStartInfo InvokeCreateOwnedTreeStopStartInfo(int processId)
        {
            var method = LocalServerUtilityType.GetMethod(
                "CreateOwnedTreeStopStartInfo",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (ProcessStartInfo)method.Invoke(null, new object[] { processId });
        }

        private bool InvokeCanStartOnPort(bool portInUse, out string error)
        {
            var method = LocalServerUtilityType.GetMethod(
                "CanStartOnPort",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            object[] args = { portInUse, null };
            bool result = (bool)method.Invoke(null, args);
            error = args[1] as string;
            return result;
        }

        private bool InvokeTryFindLatestValidPackage(string packageRoot, out string packageDirectory, out string version, out string error)
        {
            var method = YooAssetBuildUtilityType.GetMethod(
                "TryFindLatestValidPackage",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            object[] args = { packageRoot, null, null, null };
            bool result = (bool)method.Invoke(null, args);
            packageDirectory = args[1] as string;
            version = args[2] as string;
            error = args[3] as string;
            return result;
        }

        private static string CreateTempPackageRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "AnotherDayPackageTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static string CreateValidPackage(string packageRoot, string version)
        {
            string directory = Path.Combine(packageRoot, version);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "DefaultPackage.version"), version);
            File.WriteAllBytes(Path.Combine(directory, $"DefaultPackage_{version}.bytes"), new byte[] { 1 });
            File.WriteAllText(Path.Combine(directory, $"DefaultPackage_{version}.hash"), "hash");
            File.WriteAllBytes(Path.Combine(directory, "test.bundle"), new byte[] { 1 });
            return directory;
        }

        private static object GetField(object instance, string name)
        {
            return instance.GetType().GetField(name).GetValue(instance);
        }

        private static void SetField(object instance, string name, object value)
        {
            instance.GetType().GetField(name).SetValue(instance, value);
        }

        private static string BackupFile(string path)
        {
            if (!File.Exists(path))
                return null;
            string backup = path + ".testbak";
            File.Copy(path, backup, true);
            return backup;
        }

        private static void RestoreFile(string path, string backup)
        {
            if (File.Exists(path))
                File.Delete(path);
            if (backup != null && File.Exists(backup))
            {
                File.Move(backup, path);
            }
        }

        private static string ExtractSourceRange(string source, string startMarker, string endMarker)
        {
            int start = source.IndexOf(startMarker, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThan(start));
            return source.Substring(start, end - start);
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }
}
