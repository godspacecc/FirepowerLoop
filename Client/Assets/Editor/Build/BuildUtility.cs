#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Compilation;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Game.Editor
{
    /// <summary>
    /// PC 打包入口与产物校验；跨平台切换必须等待编辑器重载后续跑，不负责运行期资源加载。
    /// </summary>
    public static class BuildUtility
    {
        private const string PackageName = "DefaultPackage";
        private const string BootScene = "Assets/Game/Scenes/Boot.unity";
        private const string BurstDebugInformationSuffix = "_BurstDebugInformation_DoNotShip";
        private const string Il2CppDebugInformationSuffix = "_BackUpThisFolder_ButDontShipItWithYourGame";
        private static bool s_isBuilding;
        /// <summary>仅在当前编辑器会话保存待恢复入口；不序列化委托，重载后按白名单重新进入。</summary>
        private const string PendingWorkflowKey = "FirepowerLoop.PCBuild.PendingWorkflow";
        private const string ReloadCompletedKey = "FirepowerLoop.PCBuild.ReloadCompleted";
        /// <summary>运行中或等待重载期间占用编辑器构建入口，防止另一平台抢占目标。</summary>
        internal static bool HasActiveWorkflow => s_isBuilding ||
            !string.IsNullOrEmpty(SessionState.GetString(PendingWorkflowKey, ""));

        [MenuItem("Tools/打包/PC/完整打包", false, 100)]
        private static void BuildLatestMenu()
        {
            if (Confirm("完整打包", "重新生成 HybridCLR、AOT、Game DLL 和完整资源；耗时最长；正式发布或无法判断时使用。", "开始完整打包"))
                BuildLatestCodeAndResourcesBatch();
        }

        [MenuItem("Tools/打包/PC/快速打包/复用全部打包", false, 121)]
        private static void BuildRecentHostMenu()
        {
            if (Confirm("复用全部打包", "复用已准备好的玩法代码、AOT和资源，沿用原包型，只重新生成游戏程序及安装器；输入变化时停止。", "开始打包"))
                BuildQuickReusePlayerBatch();
        }

        [MenuItem("Tools/打包/PC/快速打包/复用 AOT 打包", false, 120)]
        private static void BuildQuickAotMenu()
        {
            if (Confirm("复用 AOT 打包", "重新编译玩法代码、重打资源，复用AOT并沿用完整包或启动包类型。用于日常测试；底层不兼容时停止，正式发布前仍需完整验证。", "开始打包"))
                BuildQuickAotPlayerBatch();
        }

        [MenuItem("Tools/打包/PC/启动包（首次下载资源）", false, 101)]
        private static void BuildSmallHostBaseMenu()
        {
            if (Confirm(
                    "启动包（首次下载资源）",
                    "只在 Player 内置 Boot 和 YooAsset 清单，不内置资源 Bundle。首次启动必须能连接资源服并下载完整资源；无网络且无缓存时不能进入游戏。",
                    "开始打包"))
            {
                BuildSmallHostBaseCodeAndResourcesBatch();
            }
        }

        [MenuItem("Tools/打包/PC/构建热更新（代码+资源）", false, 102)]
        private static void BuildHostMenu()
        {
            BuildHostCodeAndResourcesBatch();
        }

        [MenuItem("Tools/打包/PC/辅助工具/仅重建完整内置资源", false, 140)]
        private static void BuildBuiltinResourcesOnlyMenu()
        {
            BuildBuiltinResourcesOnlyBatch();
        }

        [MenuItem("Tools/服务器/PC/启动资源服", false, 200)]
        private static void StartResourceServerMenu()
        {
            StartResourceServerFromLatestPackage();
        }

        [MenuItem("Tools/服务器/PC/发布资源服", false, 201)]
        private static void PublishResourceServerMenu()
        {
            var result = ResourceServerPublishUtility.PublishLatestValidPackage();
            ShowResourceServerPublishResult(result);
        }

        [MenuItem("Tools/服务器/PC/更新资源服", false, 202)]
        private static void UpdateResourceServerMenu()
        {
            var result = ResourceServerPublishUtility.UpdateLatestBundle();
            ShowResourceServerPublishResult(result);
        }

        public static void BuildLatestCodeAndResourcesBatch()
        {
            RunWorkflow("最新代码资源完整打包", timing =>
            {
                string compiler = timing.Run("CheckInstallerCompiler", WindowsInstallerPublishUtility.EnsureCompiler);
                ValidateProjectSettings();
                BuildTarget target = BuildTarget.StandaloneWindows64;

                ConfigureStandalonePlayerBuild();
                string sourceInputs = PcQuickBuildUtility.Sources();
                string hostInputs = PcHotUpdateBaseline.CaptureCurrentInputs();
                timing.Run("GeneratePrePlayer", () => HybridCLRBuildUtility.GeneratePrePlayer(target));
                timing.Run("GeneratePostPlayer", () => HybridCLRBuildUtility.GeneratePostPlayer(target));
                var copied = timing.Run("CopyGeneratedDlls", () => HybridCLRBuildUtility.CopyGeneratedDlls(target, false));
                timing.Run("ValidateGeneratedDlls", HybridCLRBuildUtility.ValidateGeneratedDlls);
                string runtimeDllInputs = PcHotUpdateBaseline.CaptureRuntimeDllInputs();
                string generatedInputs = PcQuickBuildUtility.Generated();

                string version = CreateVersion();
                var package = timing.Run("BuildDefaultPackage", () => BuildPackageOrThrow(version, true));
                timing.Run("ValidateBuiltinPackage", () => ValidatePackageOrThrow(package.OutputPackageDirectory, version));
                // 构建前冻结内置资源，构建后核对实际 Player，避免把旧资源包误认成本轮结果。
                var builtinSnapshot = timing.Run("CaptureBuiltinPackage", () =>
                    PcBuiltinResourceValidator.Capture(YooAssetBuildUtility.GetStreamingAssetsDirectory(), version));
                var prepared = PcQuickBuildUtility.Save(version, "Full", package.OutputPackageDirectory, sourceInputs, hostInputs, runtimeDllInputs, generatedInputs);
                string player = timing.Run("BuildWindowsPlayer", BuildWindowsPlayer);
                timing.Run("ValidatePlayerAndBuiltin", () => builtinSnapshot.ValidatePlayer(player));
                PcQuickBuildUtility.Validate(prepared, true);

                string baselineDirectory = timing.Run("PreservePcHostBaseline", () => PreservePcHostBaseline(player, hostInputs, runtimeDllInputs));
                SaveSuccessfulState(version, "Full", package.OutputPackageDirectory, player, updateAot: true, updateHost: false, pcBaselineDirectory: baselineDirectory);
                Debug.Log($"[BuildUtility] Player 构建成功（已保存，可补做安装器与发布）：Version={version}，Player={player}，DLLs={copied.Count}");
                string serverResources = timing.Run("GenerateAndPublishInstaller", () => WindowsInstallerPublishUtility.GenerateAndPublish(player, version, compiler, package.OutputPackageDirectory));
                Debug.Log($"[BuildUtility] 完整打包完成：Version={version}，Player={player}，资源服={serverResources}");
                OfferStartServer(version, serverResources, "安装包发布成功", timing);
            }, cleanupPrepPlayer: false);
        }

        /// <summary>
        /// 生成只携带 Boot 与资源清单的 Windows 首包，并发布同版本完整远端资源。
        /// </summary>
        public static void BuildSmallHostBaseCodeAndResourcesBatch()
        {
            RunWorkflow("首包完整打包", timing =>
            {
                string compiler = timing.Run("CheckInstallerCompiler", WindowsInstallerPublishUtility.EnsureCompiler);
                ValidateProjectSettings();
                BuildTarget target = BuildTarget.StandaloneWindows64;

                ConfigureStandalonePlayerBuild();
                string sourceInputs = PcQuickBuildUtility.Sources();
                string hostInputs = PcHotUpdateBaseline.CaptureCurrentInputs();
                timing.Run("GeneratePrePlayer", () => HybridCLRBuildUtility.GeneratePrePlayer(target));
                timing.Run("GeneratePostPlayer", () => HybridCLRBuildUtility.GeneratePostPlayer(target));
                var copied = timing.Run("CopyGeneratedDlls", () => HybridCLRBuildUtility.CopyGeneratedDlls(target, false));
                timing.Run("ValidateGeneratedDlls", HybridCLRBuildUtility.ValidateGeneratedDlls);
                string runtimeDllInputs = PcHotUpdateBaseline.CaptureRuntimeDllInputs();
                string generatedInputs = PcQuickBuildUtility.Generated();

                string version = CreateVersion();
                var package = timing.Run("BuildSmallHostPackage", () => BuildManifestOnlyPackageOrThrow(version));
                timing.Run("ValidateHostPackage", () => ValidatePackageOrThrow(package.OutputPackageDirectory, version));
                var startupSnapshot = timing.Run("CaptureStartupPackage", () =>
                    PcBuiltinResourceValidator.CaptureStartup(YooAssetBuildUtility.GetStreamingAssetsDirectory(), package.OutputPackageDirectory, version));
                var prepared = PcQuickBuildUtility.Save(version, "SmallHostBase", package.OutputPackageDirectory, sourceInputs, hostInputs, runtimeDllInputs, generatedInputs);
                string player = timing.Run("BuildSmallHostPlayer", BuildWindowsPlayer);
                timing.Run("ValidateSmallHostPlayer", () => startupSnapshot.ValidatePlayer(player));
                PcQuickBuildUtility.Validate(prepared, true);

                string baselineDirectory = timing.Run("PreservePcHostBaseline", () => PreservePcHostBaseline(player, hostInputs, runtimeDllInputs));
                SaveSuccessfulState(
                    version,
                    "SmallHostBase",
                    package.OutputPackageDirectory,
                    player,
                    updateAot: true,
                    updateHost: true,
                    pcBaselineDirectory: baselineDirectory);
                if (CiBuildSession.Current != null)
                {
                    string installer = timing.Run("GenerateInstallerOnly", () => WindowsInstallerPublishUtility.GenerateOnly(player, compiler));
                    CiBuildSession.Current.Capture(version, package.OutputPackageDirectory, player, installer);
                    return;
                }
                string serverResources = timing.Run("GenerateAndPublishInstaller", () => WindowsInstallerPublishUtility.GenerateAndPublish(player, version, compiler, package.OutputPackageDirectory));
                Debug.Log(
                    $"[BuildUtility] PC 首包完成：版本={version}，Player路径={player}，" +
                    $"DLL数量={copied.Count}，资源服目录={Path.GetDirectoryName(serverResources)}");
                // 首包发布完成后自动启动同版本资源服，成功仅写日志；失败保留已生成的安装包。
                var server = timing.Run("StartResourceServer", () => LocalResourceServerUtility.StartAndProbe(serverResources, version));
                if (!server.Success)
                    ShowDialog("PC 首包已发布，资源服启动失败", BuildWorkflowUtility.FormatFailure(timing, server.Error, serverResources));
                else
                    Debug.Log($"[BuildUtility] PC 首包资源服已启动：版本={version}，地址={server.BaseUrl}");
            }, cleanupPrepPlayer: false);
        }

        public static void BuildHostCodeAndResourcesBatch()
        {
            RunWorkflow("Host代码+资源更新", timing =>
            {
                ValidateProjectSettings();
                BuildTarget target = BuildTarget.StandaloneWindows64;

                var baseline = LoadStateOrThrow();
                string hostInputs = timing.Run("CapturePcHostInputs", PcHotUpdateBaseline.CaptureCurrentInputs);
                timing.Run("ValidatePcHostBaseline", () => PcHotUpdateBaseline.Validate(
                    baseline.PcHotUpdateBaselineDirectory, baseline.LastPlayer?.PlayerPath, hostInputs));
                timing.Run("CompileHotUpdateDll", () => HybridCLRBuildUtility.CompileHotUpdateDll(target));
                timing.Run("ValidateGameNativeBridge", () => PcHotUpdateBaseline.ValidateHotUpdateAssembly(
                    baseline.PcHotUpdateBaselineDirectory, Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                        "HybridCLRData", "HotUpdateDlls", target.ToString(), "Game.dll")));
                var copied = timing.Run("CopyGeneratedDllsFromBaseline", () => HybridCLRBuildUtility.CopyGeneratedDllsFromBaseline(
                    target, Path.Combine(baseline.PcHotUpdateBaselineDirectory, "Aot"),
                    Path.Combine(baseline.PcHotUpdateBaselineDirectory, "HybridCLRAssemblyList.bytes")));
                timing.Run("ValidateGeneratedDlls", HybridCLRBuildUtility.ValidateGeneratedDlls);

                string builtinDir = YooAssetBuildUtility.GetStreamingAssetsDirectory();
                var before = timing.Run("SnapshotBuiltinBefore", () => SnapshotDirectory(builtinDir));

                string version = CreateVersion();
                var package = timing.Run("BuildHostPackage", () => BuildPackageOrThrow(version, false));
                var remoteSnapshot = timing.Run("ValidateCompleteRemotePackage", () => PcBuiltinResourceValidator.CaptureRemote(package.OutputPackageDirectory, version));
                var after = timing.Run("SnapshotBuiltinAfter", () => SnapshotDirectory(builtinDir));
                timing.Run("CompareBuiltinSnapshot", () => EnsureSnapshotsEqual(before, after));

                timing.Run("RevalidatePcHostBaseline", () => PcHotUpdateBaseline.Validate(
                    baseline.PcHotUpdateBaselineDirectory, baseline.LastPlayer?.PlayerPath, PcHotUpdateBaseline.CaptureCurrentInputs()));
                timing.Run("RevalidateRemotePackage", () => remoteSnapshot.ValidateDirectory(package.OutputPackageDirectory));
                SaveSuccessfulState(version, "Host", package.OutputPackageDirectory, null, updateAot: false, updateHost: true);
                if (CiBuildSession.Current != null) { CiBuildSession.Current.Capture(version, package.OutputPackageDirectory); return; }
                var server = timing.Run("StartResourceServer", () => LocalResourceServerUtility.StartAndProbe(package.OutputPackageDirectory, version));
                if (!server.Success)
                {
                    ShowDialog("Host 资源构建成功，资源服启动失败",
                        BuildWorkflowUtility.FormatFailure(timing, server.Error, package.OutputPackageDirectory));
                    return;
                }

                Debug.Log($"[BuildUtility] Host 更新完成：Version={version}，DLLs={copied.Count}，Server={server.BaseUrl}");
                Debug.Log($"[BuildUtility] Host 更新成功：版本={version}，资源目录={package.OutputPackageDirectory}，资源服={server.BaseUrl}");
            }, cleanupPrepPlayer: false);
        }

        /// <summary>旧入口语义不再有效，禁止重载恢复时静默改为另一种复用。</summary>
        public static void BuildRecentHostPlayerBatch()
        {
            throw new InvalidOperationException("旧的复用代码入口已移除，请重新选择复用全部打包。");
        }

        /// <summary>沿用可信准备记录，只补做最终Player及发布。</summary>
        public static void BuildQuickReusePlayerBatch()
        {
            RunWorkflow("复用代码和资源打包", timing => BuildPreparedPlayer(timing, PcQuickBuildUtility.Load(true)), cleanupPrepPlayer: false);
        }

        /// <summary>重新编译玩法和资源，沿用冻结的AOT与原包型。</summary>
        public static void BuildQuickAotPlayerBatch()
        {
            RunWorkflow("复用 AOT 快速打包", timing =>
            {
                ValidateProjectSettings();
                ConfigureStandalonePlayerBuild();
                var previous = PcQuickBuildUtility.Load(false);
                string source = PcQuickBuildUtility.Sources();
                string host = PcHotUpdateBaseline.CaptureCurrentInputs();
                BuildTarget target = BuildTarget.StandaloneWindows64;
                timing.Run("CompileHotUpdateDll", () => HybridCLRBuildUtility.CompileHotUpdateDll(target));
                PcHotUpdateBaseline.ValidateHotUpdateAssembly(previous.Directory,
                    Path.Combine(Directory.GetParent(Application.dataPath).FullName, "HybridCLRData", "HotUpdateDlls", target.ToString(), "Game.dll"));
                timing.Run("CopyGeneratedDllsFromBaseline", () => HybridCLRBuildUtility.CopyGeneratedDllsFromBaseline(
                    target, Path.Combine(previous.Directory, "Aot"), Path.Combine(previous.Directory, "HybridCLRAssemblyList.bytes")));
                HybridCLRBuildUtility.ValidateGeneratedDlls();
                PcQuickBuildUtility.Validate(previous, false);
                string runtimeDllInputs = PcHotUpdateBaseline.CaptureRuntimeDllInputs();
                string generatedInputs = PcQuickBuildUtility.Generated();
                string version = CreateVersion();
                var package = previous.Kind == "Full" ? BuildPackageOrThrow(version, true) : BuildManifestOnlyPackageOrThrow(version);
                ValidatePackageOrThrow(package.OutputPackageDirectory, version);
                var prepared = PcQuickBuildUtility.Save(version, previous.Kind, package.OutputPackageDirectory, source, host, runtimeDllInputs, generatedInputs);
                BuildPreparedPlayer(timing, prepared);
            }, cleanupPrepPlayer: false);
        }

        /// <summary>最终输出必须匹配准备快照，再绑定新宿主基线并发布。</summary>
        private static void BuildPreparedPlayer(BuildStageTiming timing, PcQuickBuildUtility.Prepared prepared)
        {
            string compiler = WindowsInstallerPublishUtility.EnsureCompiler();
            ValidateProjectSettings();
            PcQuickBuildUtility.Validate(prepared, true);
            Debug.Log($"[BuildUtility] 本次快速打包类型：{(prepared.Kind == "Full" ? "完整包" : "启动包（首次下载资源）")}，资源版本：{prepared.Version}");
            var builtin = PcQuickBuildUtility.Builtin(prepared);
            string player = timing.Run("BuildWindowsPlayer", BuildWindowsPlayer);
            builtin.ValidatePlayer(player);
            PcQuickBuildUtility.Validate(prepared, true);
            string baseline = PreservePcHostBaseline(player, prepared.Host, prepared.Runtime);
            SaveSuccessfulState(prepared.Version, prepared.Kind, prepared.Package, player, updateAot: false,
                updateHost: prepared.Kind == "SmallHostBase", pcBaselineDirectory: baseline);
            string serverResources = WindowsInstallerPublishUtility.GenerateAndPublish(player, prepared.Version, compiler, prepared.Package);
            OfferStartServer(prepared.Version, serverResources, "安装包发布成功", timing);
        }
        public static void BuildBuiltinResourcesOnlyBatch()
        {
            RunWorkflow("仅重建完整内置资源", timing =>
            {
                ValidateProjectSettings();
                string version = CreateVersion();
                var package = timing.Run("BuildDefaultPackage", () => BuildPackageOrThrow(version, true));
                timing.Run("ValidateOutputPackage", () => ValidatePackageOrThrow(package.OutputPackageDirectory, version));
                string builtinDir = YooAssetBuildUtility.GetStreamingAssetsDirectory();
                timing.Run("ValidateStreamingAssetsPackage", () => ValidatePackageOrThrow(builtinDir, version));
                Debug.Log($"[BuildUtility] 仅重建完整内置资源完成：Version={version}, Output={package.OutputPackageDirectory}, StreamingAssets={builtinDir}");
            }, cleanupPrepPlayer: false);
        }

        public static void StartResourceServerFromLatestPackage()
        {
            try
            {
                string packageRoot = YooAssetBuildUtility.GetPackageRoot(BuildTarget.StandaloneWindows64);
                if (!YooAssetBuildUtility.TryFindLatestValidPackage(
                        packageRoot,
                        out string dir,
                        out string version,
                        out string error))
                    throw new InvalidOperationException($"查找最新资源包失败：{error}");

                // 优先运行含安装包路由的发布目录，避免新 Player 发布后仍启动资源专用服务。
                string releaseRoot = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "Release", "ResourceServer");
                if (ResourceServerPublishUtility.TryFindLatestBundle(releaseRoot, out string bundle, out _))
                {
                    string published = Path.Combine(bundle, "ResourceFiles");
                    ValidatePackageOrThrow(published, version);
                    dir = published;
                }
                var result = LocalResourceServerUtility.StartAndProbe(dir, version);
                if (!result.Success)
                    throw new InvalidOperationException($"资源服启动失败：{result.Error}");

                Debug.Log($"[BuildUtility] 资源服已启动：版本={version}，资源目录={dir}，地址={result.BaseUrl}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BuildUtility] 启动资源服失败：{e.Message}");
                ShowDialog("资源服启动失败", e.Message);
            }
        }

        internal static Dictionary<string, string> SnapshotDirectory(string directory)
        {
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"快照目录不存在：{directory}");

            var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(directory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(file))
                    snapshot[relative] = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
            return snapshot;
        }

        /// <summary>
        /// 统一执行四个打包工作流，期间强制非 Development，并在成功、失败和异常路径恢复用户原设置。
        /// </summary>
        private static void RunWorkflow(string workflowName, Action<BuildStageTiming> action, bool cleanupPrepPlayer)
        {
            if (!BeginBuild())
                return;

            try
            {
                WorkspaceBuildLock.RunExclusive(Directory.GetParent(Application.dataPath).FullName,
                    () => RunWorkflowLocked(workflowName, action, cleanupPrepPlayer));
            }
            finally { s_isBuilding = false; }
        }

        /// <summary>进入前已持有工作区锁，跨平台切换、异常清理及设置恢复全部在租约内。</summary>
        private static void RunWorkflowLocked(string workflowName, Action<BuildStageTiming> action, bool cleanupPrepPlayer)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                try
                {
                    // 切换返回时旧域仍可能带 UNITY_ANDROID；必须退出调用栈，让编译和重载完成。
                    SessionState.SetString(PendingWorkflowKey, workflowName);
                    SessionState.SetBool(ReloadCompletedKey, false);
                    EnsureWindowsBuildTarget();
                    Debug.Log($"[BuildUtility] 已暂停 {workflowName}，等待 Windows 编辑器程序集重载后自动继续。");
                }
                catch
                {
                    ClearPendingWorkflow();
                    throw;
                }
                finally
                {
                    s_isBuilding = false;
                }
                return;
            }

            var previousDevelopment = EditorUserBuildSettings.development;
            var timing = new BuildStageTiming(workflowName);
            try
            {
                timing.Run("EnsureWindowsBuildTarget", EnsureWindowsBuildTarget);
                EditorUserBuildSettings.development = false;
                action(timing);
                Debug.Log($"[BuildUtility] {workflowName} 已完成\n{timing.BuildSummary()}");
            }
            catch (Exception e)
            {
                if (cleanupPrepPlayer)
                    CleanupPreparatoryPlayer();
                Debug.LogError($"[BuildUtility] {workflowName} 在阶段 {timing.CurrentStage} 失败：{e.Message}\n{timing.BuildSummary()}");
                ShowDialog("构建失败", BuildWorkflowUtility.FormatFailure(timing, e.Message));
                throw;
            }
            finally
            {
                EditorUserBuildSettings.development = previousDevelopment;
                EditorUtility.ClearProgressBar();
                s_isBuilding = false;
            }
        }

        private static bool BeginBuild()
        {
            if (HasActiveWorkflow || AndroidBuildUtility.HasActiveWorkflow)
            {
                Debug.LogWarning("[BuildUtility] 构建流程正在执行，请勿重复触发。");
                return false;
            }
            if (EditorApplication.isCompiling)
                throw new InvalidOperationException("Unity 正在编译，请稍后再试。");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Unity 正在进入或退出 PlayMode，请稍后再试。");

            s_isBuilding = true;
            return true;
        }

        /// <summary>每次编辑器程序集加载时重新订阅；这里只恢复监听，不启动任何无请求的打包。</summary>
        [InitializeOnLoadMethod]
        private static void ObservePendingWorkflow()
        {
            EditorApplication.update -= ResumePendingWorkflow;
            EditorApplication.update += ResumePendingWorkflow;
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        /// <summary>只由真正的程序集重载确认平台宏已经更新，旧域的 update 回调不能自行放行。</summary>
        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingWorkflowKey, "")))
                SessionState.SetBool(ReloadCompletedKey, true);
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (string.IsNullOrEmpty(SessionState.GetString(PendingWorkflowKey, "")) ||
                !messages.Any(message => message.type == CompilerMessageType.Error))
                return;
            ClearPendingWorkflow();
            Debug.LogError($"[BuildUtility] 平台切换后脚本编译失败，已取消待恢复的 PC 打包：{assemblyPath}");
        }

        /// <summary>编辑器空闲后一次性消费请求；失败不自动重试，避免重复构建或发布。</summary>
        private static void ResumePendingWorkflow()
        {
            string workflow = SessionState.GetString(PendingWorkflowKey, "");
            if (string.IsNullOrEmpty(workflow)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ClearPendingWorkflow();
                Debug.LogWarning("[BuildUtility] 已进入 PlayMode，取消待恢复的 PC 打包。");
                return;
            }
            if (!WindowsBuildSafety.CanResume(SessionState.GetBool(ReloadCompletedKey, false),
                    EditorApplication.isCompiling, EditorApplication.isUpdating)) return;

            ClearPendingWorkflow();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                Debug.LogError("[BuildUtility] 重载后目标不是 Windows，已取消 PC 打包。");
                return;
            }
            try
            {
                switch (workflow)
                {
                    case "最新代码资源完整打包": BuildLatestCodeAndResourcesBatch(); break;
                    case "首包完整打包": BuildSmallHostBaseCodeAndResourcesBatch(); break;
                    case "Host代码+资源更新": BuildHostCodeAndResourcesBatch(); break;
                    case "使用最近 Host 结果打包": BuildRecentHostPlayerBatch(); break;
                    case "复用代码和资源打包": BuildQuickReusePlayerBatch(); break;
                    case "复用 AOT 快速打包": BuildQuickAotPlayerBatch(); break;
                    case "仅重建完整内置资源": BuildBuiltinResourcesOnlyBatch(); break;
                    default: Debug.LogError($"[BuildUtility] 无法恢复未知工作流：{workflow}"); break;
                }
            }
            catch (Exception exception)
            {
                // 工作流内部已记录失败；隔离 update 回调异常，且请求已消费，不会每帧重跑。
                Debug.LogException(exception);
            }
        }

        private static void ClearPendingWorkflow()
        {
            SessionState.EraseString(PendingWorkflowKey);
            SessionState.EraseBool(ReloadCompletedKey);
        }

        /// <summary>
        /// 请求切换到 Windows；返回成功仅代表平台切换成功，调用方必须等待程序集重载。
        /// </summary>
        private static void EnsureWindowsBuildTarget()
        {
            const BuildTarget target = BuildTarget.StandaloneWindows64;
            if (EditorUserBuildSettings.activeBuildTarget == target)
                return;

            if (Application.isBatchMode)
                throw new InvalidOperationException(
                    $"批处理模式不能自动切换 Build Target，请使用 -buildTarget Win64 启动 Unity；当前为 {EditorUserBuildSettings.activeBuildTarget}。");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
                throw new InvalidOperationException("当前 Unity 未安装可用的 Windows Build Support。");

            BuildTarget previous = EditorUserBuildSettings.activeBuildTarget;
            Debug.Log($"[BuildUtility] 正在自动切换 Build Target：{previous} -> {target}");
            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, target);
            if (!switched || EditorUserBuildSettings.activeBuildTarget != target)
                throw new InvalidOperationException($"Build Target 自动切换失败：{previous} -> {target}");
        }

        private static void ValidateProjectSettings()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new InvalidOperationException($"Build Target 必须是 StandaloneWindows64，当前为 {EditorUserBuildSettings.activeBuildTarget}");
            if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone) != ScriptingImplementation.IL2CPP)
                throw new InvalidOperationException("Standalone 脚本后端必须是 IL2CPP。");

            ValidateInsecureHttpOption(PlayerSettings.insecureHttpOption);

            var enabledScenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
            if (enabledScenes.Length != 1 || enabledScenes[0].path != BootScene)
                throw new InvalidOperationException($"Build Settings 必须只启用 {BootScene}。");

            RequireFile("Assets/StreamingAssets/hostConfig.json");
            RequireDirectory("Assets/Game/Config");
            RequireDirectory("Assets/Game/Dlls");
            RequireFile("Assets/Editor/YooAsset/LocalServer/start_yooasset_local_server.ps1");
        }

        private static void ValidateInsecureHttpOption(InsecureHttpOption option)
        {
            if (option == InsecureHttpOption.AlwaysAllowed)
            {
                return;
            }

            throw new InvalidOperationException(
                $"非 Development Windows Player 需要 PlayerSettings.insecureHttpOption 为 AlwaysAllowed，当前为 {option}。");
        }

        private static string BuildPreparatoryPlayer()
        {
            ConfigureStandalonePlayerBuild();
            string tempPath = GetPreparatoryPlayerBuildPath();
            string exePath = Path.Combine(tempPath, "FirepowerLoop.exe");
            DeleteDirectory(tempPath);
            Directory.CreateDirectory(tempPath);
            BuildPlayer(exePath);
            return exePath;
        }

        private static void CleanupPreparatoryPlayer()
        {
            DeleteDirectory(GetPreparatoryPlayerBuildPath());
        }

        private static string BuildWindowsPlayer()
        {
            ConfigureStandalonePlayerBuild();
            string projectPath = Directory.GetParent(Application.dataPath).FullName;
            string outputRoot = Path.Combine(projectPath, "Build", "Windows");
            string batchDirectory = GetWindowsPlayerBatchDirectory(outputRoot, DateTime.Now);
            if (Directory.Exists(batchDirectory) || File.Exists(batchDirectory))
                throw new InvalidOperationException($"Windows Player 发布批次已存在，拒绝覆盖：{batchDirectory}");

            string playerDirectory = Path.Combine(batchDirectory, "FirepowerLoop");
            Directory.CreateDirectory(playerDirectory);
            string exePath = Path.Combine(playerDirectory, "FirepowerLoop.exe");
            using (var release = new WindowsReleaseIdentity())
            {
                BuildPlayer(exePath);
                string symbolsDirectory = SeparateWindowsPlayerDebugSymbols(playerDirectory);
                release.ValidatePlayer(playerDirectory);
                release.Commit();
                Debug.Log($"[BuildUtility] Windows Player 输出完成：玩家目录={playerDirectory}，调试资料目录={symbolsDirectory}");
            }
            return exePath;
        }

        private static string GetWindowsPlayerBatchDirectory(string outputRoot, DateTime timestamp)
        {
            return Path.GetFullPath(Path.Combine(outputRoot, timestamp.ToString("yyyyMMdd_HHmmss")));
        }

        private static string SeparateWindowsPlayerDebugSymbols(string playerDirectory)
        {
            string normalizedPlayerDirectory = Path.GetFullPath(playerDirectory);
            string batchDirectory = Directory.GetParent(normalizedPlayerDirectory)?.FullName;
            if (string.IsNullOrEmpty(batchDirectory))
                throw new InvalidOperationException($"无法确定 Windows Player 批次目录：{normalizedPlayerDirectory}");
            if (!Directory.Exists(normalizedPlayerDirectory))
                throw new DirectoryNotFoundException($"Windows Player 目录不存在，需要人工检查本批目录：{batchDirectory}");

            string burstDirectory = FindOptionalDebugDirectory(normalizedPlayerDirectory, BurstDebugInformationSuffix, batchDirectory);
            string il2CppDirectory = FindSingleDebugDirectory(normalizedPlayerDirectory, Il2CppDebugInformationSuffix, batchDirectory);
            string symbolsDirectory = Path.Combine(batchDirectory, "Symbols");
            string burstTarget = burstDirectory == null ? null : Path.Combine(symbolsDirectory, Path.GetFileName(burstDirectory));
            string il2CppTarget = Path.Combine(symbolsDirectory, Path.GetFileName(il2CppDirectory));
            if (Directory.Exists(symbolsDirectory) || File.Exists(symbolsDirectory))
                throw new InvalidOperationException($"调试资料目录已存在，拒绝覆盖：{symbolsDirectory}。请人工检查本批目录：{batchDirectory}");
            if (burstTarget != null && (Directory.Exists(burstTarget) || File.Exists(burstTarget)) ||
                Directory.Exists(il2CppTarget) || File.Exists(il2CppTarget))
                throw new InvalidOperationException($"调试资料目标目录已存在。请人工检查本批目录：{batchDirectory}");

            try
            {
                Directory.CreateDirectory(symbolsDirectory);
                if (burstDirectory != null)
                    Directory.Move(burstDirectory, burstTarget);
                Directory.Move(il2CppDirectory, il2CppTarget);
                if (burstDirectory == null)
                    Debug.LogWarning($"[BuildUtility] 本次 Player 未生成 Burst 调试资料目录，按 script-only build 产物继续：{batchDirectory}");
                return symbolsDirectory;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"调试资料分离失败，请人工检查本批目录：{batchDirectory}", e);
            }
        }

        private static string FindSingleDebugDirectory(string playerDirectory, string suffix, string batchDirectory)
        {
            string[] matches = Directory.GetDirectories(playerDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(directory => Path.GetFileName(directory).EndsWith(suffix, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"调试资料目录应唯一匹配后缀 {suffix}，实际为 {matches.Length} 个。请人工检查本批目录：{batchDirectory}");
            return matches[0];
        }

        private static string FindOptionalDebugDirectory(string playerDirectory, string suffix, string batchDirectory)
        {
            string[] matches = Directory.GetDirectories(playerDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(directory => Path.GetFileName(directory).EndsWith(suffix, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length > 1)
                throw new InvalidOperationException($"调试资料目录最多匹配一个后缀 {suffix}，实际为 {matches.Length} 个。请人工检查本批目录：{batchDirectory}");
            return matches.Length == 0 ? null : matches[0];
        }

        /// <summary>
        /// 生成 Windows Player；诊断正式版固定使用 BuildOptions.None，不打开 Development、Profiler 或脚本调试。
        /// </summary>
        private static void BuildPlayer(string exePath)
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { BootScene },
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report == null)
                throw new InvalidOperationException($"Player 构建未返回报告，请查看 Unity Console 获取底层错误。输出路径: {exePath}");

            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Player 构建失败: {summary.result}");
            string assemblyList = Path.Combine(Path.GetDirectoryName(exePath),
                Path.GetFileNameWithoutExtension(exePath) + "_Data", "ScriptingAssemblies.json");
            RequireFile(assemblyList);
            if (!WindowsBuildSafety.TryValidateAssemblyList(File.ReadAllText(assemblyList),
                    HybridCLR.Editor.SettingsUtil.HotUpdateAssemblyFilesIncludePreserved, out string error))
                throw new InvalidOperationException($"Player 热更程序集清单校验失败：{assemblyList}；{error}");
            Debug.Log($"[BuildUtility] Player 构建成功：{exePath}，大小={summary.totalSize}");
        }

        private static void ConfigureStandalonePlayerBuild()
        {
            EditorUserBuildSettings.SetPlatformSettings("Standalone", "CreateSolution", "false");
            EditorUserBuildSettings.SetPlatformSettings("Standalone", "CreateSolutionBuild", "false");
        }

        private static YooAssetBuildUtility.BuildResult BuildPackageOrThrow(string version, bool fullBuiltin)
        {
            var result = YooAssetBuildUtility.BuildDefaultPackage(version, fullBuiltin);
            if (!result.Success)
                throw new InvalidOperationException(result.Error ?? "YooAsset 构建失败。");
            return result;
        }

        private static YooAssetBuildUtility.BuildResult BuildManifestOnlyPackageOrThrow(string version)
        {
            var result = YooAssetBuildUtility.BuildManifestOnlyBuiltinPackage(version);
            if (!result.Success)
                throw new InvalidOperationException(result.Error ?? "PC 首包 YooAsset 资源构建失败。");
            return result;
        }

        private static void ValidatePackageOrThrow(string directory, string version)
        {
            if (!YooAssetBuildUtility.ValidateBuildResult(directory, version))
                throw new InvalidOperationException($"资源包校验失败: {directory}, version={version}");
        }

        private static void ValidatePlayerAndBuiltin(string playerPath, string version)
        {
            if (!File.Exists(playerPath))
                throw new FileNotFoundException($"Player 不存在: {playerPath}");
            string builtinDirectory = YooAssetBuildUtility.GetStreamingAssetsDirectory();
            ValidatePackageOrThrow(builtinDirectory, version);
            RequireFile(Path.Combine(builtinDirectory, "BuildinCatalog.bytes"));
            RequireFile(Path.Combine(builtinDirectory, "BuildinCatalog.json"));
        }

        private static void ValidateManifestOnlyBuiltinOrThrow(string version)
        {
            if (!YooAssetBuildUtility.ValidateManifestOnlyBuiltinPackage(version))
                throw new InvalidOperationException("PC 首包内置清单校验失败。");
        }

        private static void ValidateExistingPlayerAot()
        {
            string aotDir = HybridCLRBuildUtility.GetAotDirectory(BuildTarget.StandaloneWindows64);
            if (!Directory.Exists(aotDir))
                throw new DirectoryNotFoundException($"Player AOT 输出目录不存在: {aotDir}");
            if (Directory.GetFiles(aotDir, "*.dll").Length == 0)
                throw new InvalidOperationException($"Player AOT 输出为空: {aotDir}");
        }

        private static void SaveSuccessfulState(string version, string buildKind, string packageDirectory, string playerPath, bool updateAot, bool updateHost, string pcBaselineDirectory = null)
        {
            var state = BuildStateStore.Load(out string warning);
            if (!string.IsNullOrEmpty(warning))
                Debug.LogWarning(warning);

            if (updateHost)
            {
                state.LastHost = new HostBuildRecord
                {
                    Version = version,
                    BuildTarget = BuildTarget.StandaloneWindows64.ToString(),
                    OutputPackageDirectory = Path.GetFullPath(packageDirectory),
                    GameDllDirectory = HybridCLRBuildUtility.GetGameDllDirectory(),
                    CompletedUtcTicks = DateTime.UtcNow.Ticks
                };
            }

            if (updateAot)
            {
                state.LastAotBaseline = new AotBaselineRecord
                {
                    BuildTarget = BuildTarget.StandaloneWindows64.ToString(),
                    AotDirectory = HybridCLRBuildUtility.GetAotDirectory(BuildTarget.StandaloneWindows64),
                    RuntimeListPath = HybridCLRBuildUtility.GetRuntimeListPath(),
                    GameDllDirectory = HybridCLRBuildUtility.GetGameDllDirectory(),
                    CompletedUtcTicks = DateTime.UtcNow.Ticks
                };
            }

            if (pcBaselineDirectory != null) state.PcHotUpdateBaselineDirectory = pcBaselineDirectory;
            state.LastPackage = NewPackageRecord(version, buildKind, packageDirectory);
            if (!string.IsNullOrEmpty(playerPath))
                state.LastPlayer = NewPlayerRecord(version, buildKind, playerPath);
            BuildStateStore.Save(state);
        }

        /// <summary>只在新宿主通过检查后归档本次AOT；输入中途变化时拒绝把它记成可热更基线。</summary>
        private static string PreservePcHostBaseline(string player, string expectedInputs, string expectedRuntimeDlls)
        {
            if (PcHotUpdateBaseline.CaptureCurrentInputs() != expectedInputs)
                throw new InvalidOperationException("宿主构建期间底层输入变化，请重新完整打包或生成启动包。");
            if (PcHotUpdateBaseline.CaptureRuntimeDllInputs() != expectedRuntimeDlls)
                throw new InvalidOperationException("构建期间代码资源变化，不能归档与游戏不匹配的AOT。请重新完整打包或生成启动包。");
            string directory = Path.Combine(Directory.GetParent(Path.GetDirectoryName(player)).FullName, "HostBaseline");
            PcHotUpdateBaseline.Capture(directory, player, HybridCLRBuildUtility.GetGameDllDirectory(), expectedInputs);
            return directory;
        }

        private static PackageBuildRecord NewPackageRecord(string version, string buildKind, string outputPackageDirectory)
        {
            return new PackageBuildRecord
            {
                Version = version,
                BuildTarget = BuildTarget.StandaloneWindows64.ToString(),
                OutputPackageDirectory = Path.GetFullPath(outputPackageDirectory),
                BuildKind = buildKind,
                CompletedUtcTicks = DateTime.UtcNow.Ticks
            };
        }

        private static PlayerBuildRecord NewPlayerRecord(string version, string buildKind, string playerPath)
        {
            return new PlayerBuildRecord
            {
                PlayerPath = Path.GetFullPath(playerPath),
                Version = version,
                BuildKind = buildKind,
                CompletedUtcTicks = DateTime.UtcNow.Ticks
            };
        }

        private static BuildState LoadStateOrThrow()
        {
            var state = BuildStateStore.Load(out string warning);
            if (!string.IsNullOrEmpty(warning))
                Debug.LogWarning(warning);
            return state;
        }

        private static void EnsureSnapshotsEqual(Dictionary<string, string> before, Dictionary<string, string> after)
        {
            if (before.Count != after.Count)
                throw new InvalidOperationException("Host 构建修改了内置包文件集合。");
            foreach (var kv in before)
            {
                if (!after.TryGetValue(kv.Key, out string hash) || hash != kv.Value)
                    throw new InvalidOperationException($"Host 构建修改了内置包文件: {kv.Key}");
            }
        }

        /// <summary>展示一次打包结果与启动选择；选择稍后直接结束，批处理仅记录结果。</summary>
        private static void OfferStartServer(string version, string packageDirectory, string title, BuildStageTiming timing)
        {
            var state = BuildStateStore.Load(out _);
            string playerPath = state.LastPlayer?.PlayerPath;
            string playerDirectory = string.IsNullOrEmpty(playerPath) ? "未记录" : Path.GetDirectoryName(playerPath);
            string symbolsDirectory = string.IsNullOrEmpty(playerPath) ? "未记录" : Path.Combine(Directory.GetParent(playerDirectory).FullName, "Symbols");
            string successMessage = $"版本: {version}\n玩家目录: {playerDirectory}\n调试资料目录: {symbolsDirectory}\n资源目录: {packageDirectory}\nPlayer 和内置包已验证。\n\n是否启动资源服？";
            if (!Application.isBatchMode && EditorUtility.DisplayDialog(title, successMessage, "启动资源服", "稍后"))
            {
                var result = timing.Run("StartResourceServer", () => LocalResourceServerUtility.StartAndProbe(packageDirectory, version));
                if (!result.Success)
                    ShowDialog("打包成功，资源服启动失败", BuildWorkflowUtility.FormatFailure(timing, result.Error, packageDirectory));
                else
                    Debug.Log($"[BuildUtility] 资源服已启动：{result.BaseUrl}");
            }
            else if (Application.isBatchMode)
            {
                ShowDialog(title, successMessage);
            }
        }

        private static bool Confirm(string title, string message, string ok)
        {
            return Application.isBatchMode || EditorUtility.DisplayDialog(title, message, ok, "取消");
        }

        private static void ShowDialog(string title, string message)
        {
            if (Application.isBatchMode)
                Debug.Log($"[BuildUtility] 对话框消息：{title}：{message}");
            else
                EditorUtility.DisplayDialog(title, message, "OK");
        }

        private static void ShowResourceServerPublishResult(ResourceServerPublishResult result)
        {
            if (result == null)
            {
                Debug.LogError("[BuildUtility] 资源服发布结果为空。");
                ShowDialog("资源服操作失败", "资源服发布结果为空。");
                return;
            }

            if (!result.Success)
            {
                Debug.LogError($"[BuildUtility] {result.Operation}失败：{result.Error}");
                ShowDialog($"{result.Operation}失败", result.Error);
                return;
            }

            if (string.Equals(result.Operation, "更新资源服", StringComparison.Ordinal))
            {
                var unchanged = string.Equals(result.PreviousVersion, result.Version, StringComparison.Ordinal);
                var title = unchanged ? "资源版本未变化，已重新校验并替换" : "资源服更新成功";
                ShowDialog(
                    title,
                    $"旧版本: {result.PreviousVersion}\n新版本: {result.Version}\n发布包: {result.BundleDirectory}\n\n请停止服务器、整体替换 ResourceFiles、重新启动。");
                return;
            }

            ShowDialog(
                "资源服发布成功",
                $"版本: {result.Version}\n源资源: {result.SourcePackageDirectory}\n发布包: {result.BundleDirectory}\n\n请复制整个包到服务器设备并双击启动。");
        }

        private static string CreateVersion()
        {
            return BuildWorkflowUtility.CreateResourceVersion(DateTime.Now);
        }

        private static string GetBuildStateRoot()
        {
            string projectPath = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectPath, "Library", "FirepowerLoopBuild");
        }

        private static string GetPreparatoryPlayerBuildPath()
        {
            string projectPath = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectPath, "Temp", "FirepowerLoopBuild", "PrepPlayer_Temp");
        }

        private static void RequireFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"必要文件缺失: {path}");
        }

        private static void RequireDirectory(string path)
        {
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException($"必要目录缺失: {path}");
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }
    /// <summary>PC 构建的只读安全规则；不切换平台、不加载程序集，也不执行构建或发布。</summary>
    public static class WindowsBuildSafety
    {
        [Serializable]
        private sealed class AssemblyList
        {
            public string[] names;
            public int[] types;
        }

        /// <summary>平台重载已确认且编辑器不再编译、导入时才能消费待恢复请求。</summary>
        public static bool CanResume(bool reloaded, bool compiling, bool updating)
        {
            return BuildWorkflowUtility.CanResume(reloaded, compiling, updating);
        }

        /// <summary>校验所有配置热更程序集都唯一登记为用户 DLL；失败仅返回诊断，不修改产物。</summary>
        public static bool TryValidateAssemblyList(string json, IEnumerable<string> hotUpdateAssemblies, out string error)
        {
            error = null;
            AssemblyList list;
            try
            {
                list = JsonUtility.FromJson<AssemblyList>(json);
            }
            catch (Exception exception)
            {
                error = $"程序集清单 JSON 无效：{exception.Message}";
                return false;
            }
            if (list?.names == null || list.types == null || list.names.Length != list.types.Length)
            {
                error = "程序集 names 与 types 缺失或长度不一致。";
                return false;
            }
            var required = hotUpdateAssemblies?.ToArray();
            if (required == null || required.Length == 0)
            {
                error = "热更程序集配置为空，无法验证 Player。";
                return false;
            }
            foreach (string name in required)
            {
                int index = Array.IndexOf(list.names, name);
                // HybridCLR 使用 type=16 表示用户 DLL；仅包含名字不足以保证反序列化可见。
                if (string.IsNullOrWhiteSpace(name) || index < 0 || list.types[index] != 16 ||
                    Array.LastIndexOf(list.names, name) != index)
                {
                    error = $"热更程序集 {name} 未唯一登记为用户 DLL（type=16）。";
                    return false;
                }
            }
            return true;
        }
    }
}
#endif
