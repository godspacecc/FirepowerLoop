#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Game.Network;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Compilation;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Game.Editor
{
    /// <summary>
    /// Android 个人真机测试包与 Host 热更新的构建入口。
    /// 负责 Android HybridCLR、离线完整包、Host 基础包及后续代码资源更新；不负责正式签名和商店发布。
    /// </summary>
    public static class AndroidBuildUtility
    {
        private const string BootScene = "Assets/Game/Scenes/Boot.unity";
        /// <summary>本项目独立应用身份；Android 校验与 APK 发布描述共用，禁止复用旧项目包名。</summary>
        internal const string ApplicationIdentifier = "com.firepowerloop.game";
        private const string HostConfigPath = "Assets/StreamingAssets/hostConfig.json";
        private const string NetworkConfigPath = "Assets/StreamingAssets/networkConfig.json";
        private const string PlayerGuidePath = "安卓联机玩家说明.md";
        private const string PlayerGuideFileName = "安卓联机玩家说明.md";
        /// <summary>仅随 Android 完整内置测试包传入 Player 编译，不改变 Windows 与 Host 包的启动策略。</summary>
        internal const string BuiltinResourcesScriptingDefine = "ANOTHERDAY_ANDROID_BUILTIN_RESOURCES";
        private static bool s_isBuilding;
        /// <summary>只在本编辑器会话保存请求身份；真实重载后按白名单恢复一次，不保存委托。</summary>
        private const string PendingWorkflowKey = "AnotherDay.AndroidBuild.PendingWorkflow";
        private const string ReloadCompletedKey = "AnotherDay.AndroidBuild.ReloadCompleted";
        internal static bool HasActiveWorkflow => s_isBuilding ||
            !string.IsNullOrEmpty(SessionState.GetString(PendingWorkflowKey, ""));

        [MenuItem("Tools/打包/Android/最新代码资源完整打包", false, 150)]
        private static void BuildLatestMenu()
        {
            if (Application.isBatchMode || EditorUtility.DisplayDialog(
                    "Android 最新代码资源完整打包",
                    "将生成 ARM64、IL2CPP、完整内置资源的 Development APK。需要时会切换到 Android，并等待脚本重载后继续。",
                    "开始完整打包",
                    "取消"))
            {
                BuildLatestCodeAndResourcesBatch();
            }
        }

        [MenuItem("Tools/打包/Android/Host基础包完整打包", false, 151)]
        private static void BuildHostBaseMenu()
        {
            if (Application.isBatchMode || EditorUtility.DisplayDialog(
                    "Android Host 基础包完整打包",
                    "将生成 ARM64、IL2CPP、完整内置回退资源且启动时连接资源服的 Development APK。请先把 hostConfig.json 配置为手机可访问的 HTTP/HTTPS 地址。",
                    "开始打包",
                    "取消"))
            {
                BuildHostBaseCodeAndResourcesBatch();
            }
        }

        [MenuItem("Tools/打包/Android/Host代码+资源更新", false, 153)]
        private static void BuildHostUpdateMenu()
        {
            BuildHostCodeAndResourcesBatch();
        }

        [MenuItem("Tools/打包/Android/首包完整打包", false, 152)]
        private static void BuildSmallHostBaseMenu()
        {
            if (Application.isBatchMode || EditorUtility.DisplayDialog(
                    "Android 首包完整打包",
                    "只在 APK 内置 Boot 和 YooAsset 清单，不内置资源 Bundle。首次启动必须能连接资源服并下载完整资源；无网络且无缓存时不能进入游戏。",
                    "开始打包",
                    "取消"))
            {
                BuildSmallHostBaseCodeAndResourcesBatch();
            }
        }

        [MenuItem("Tools/服务器/Android/发布资源服", false, 210)]
        private static void PublishAndroidResourceServerMenu()
        {
            ShowResourceServerResult(ResourceServerPublishUtility.PublishLatestValidAndroidPackage());
        }

        [MenuItem("Tools/服务器/Android/更新资源服", false, 211)]
        private static void UpdateAndroidResourceServerMenu()
        {
            ShowResourceServerResult(ResourceServerPublishUtility.UpdateLatestAndroidBundle());
        }

        [MenuItem("Tools/服务器/Android/启动资源服", false, 212)]
        private static void StartAndroidResourceServerMenu()
        {
            StartAndroidResourceServerFromLatestPublishedBundle();
        }

        /// <summary>
        /// 从最新 Android 发布包启动统一资源服，提供资源、公告与 APK 更新；不构建、不发布。
        /// </summary>
        public static void StartAndroidResourceServerFromLatestPublishedBundle()
        {
            try
            {
                string workspaceRoot = Directory.GetParent(Application.dataPath).Parent.FullName;
                string releaseRoot = Path.Combine(workspaceRoot, "Release", "ResourceServer", "Android");
                if (!ResourceServerPublishUtility.TryFindLatestBundle(
                        releaseRoot,
                        out string bundleDirectory,
                        out string findError))
                {
                    throw new InvalidOperationException($"查找最新 Android 资源服发布包失败：{findError}");
                }

                string resourceRoot = Path.Combine(bundleDirectory, "ResourceFiles");
                string versionFile = Path.Combine(resourceRoot, "DefaultPackage.version");
                if (!File.Exists(versionFile))
                    throw new FileNotFoundException("Android 资源服版本文件不存在。", versionFile);

                string version = File.ReadAllText(versionFile).Trim();
                var result = LocalResourceServerUtility.StartAndProbe(resourceRoot, version, true);
                if (!result.Success)
                    throw new InvalidOperationException($"Android 资源服启动失败：{result.Error}");

                Debug.Log(
                    $"[AndroidBuildUtility] Android 资源服已启动：版本={version}，" +
                    $"资源目录={resourceRoot}，本机地址={result.BaseUrl}，整包更新接口={result.BaseUrl}/app-update.json");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[AndroidBuildUtility] Android 资源服启动失败：{exception.Message}");
                ShowDialog("Android 资源服启动失败", exception.Message);
            }
        }

        /// <summary>
        /// 按 Android 完整链路构建内置资源 APK 并发布整包更新；发布失败保留已验证 APK。
        /// </summary>
        public static void BuildLatestCodeAndResourcesBatch()
        {
            if (!BeginWorkflow("Android 最新代码资源完整打包"))
                return;

            var timing = new BuildStageTiming("Android 最新代码资源完整打包");
            var context = new AndroidBuildContext(DateTime.Now, Guid.NewGuid().ToString("N"));
            var previous = AndroidEditorOptions.Capture();
            bool finalApkValidated = false;
            try
            {
                timing.Run("EnsureAndroidBuildTarget", EnsureAndroidBuildTarget);
                timing.Run("ValidateAndroidEnvironment", ValidateAndroidEnvironment);
                timing.Run("ValidateAndroidNetworkConfig", ValidateAndroidNetworkConfig);
                previous.ApplyTestBuildOptions();

                timing.Run("PrepareOutputDirectories", context.PrepareDirectories);
                timing.Run("GeneratePrePlayer", () => HybridCLRBuildUtility.GeneratePrePlayer(BuildTarget.Android));
                timing.Run("BuildPreparatoryPlayer", () => BuildPlayer(context.PreparatoryApkPath, true));
                timing.Run("ValidatePreparatoryAot", ValidateAndroidAotOutput);
                timing.Run("GeneratePostPlayer", () => HybridCLRBuildUtility.GeneratePostPlayer(BuildTarget.Android));
                var copied = timing.Run("CopyGeneratedDlls", () => HybridCLRBuildUtility.CopyGeneratedDlls(BuildTarget.Android, false));
                timing.Run("ValidateGeneratedDlls", HybridCLRBuildUtility.ValidateGeneratedDlls);

                string version = BuildWorkflowUtility.CreateResourceVersion(context.Timestamp);
                var package = timing.Run("BuildAndroidBuiltinPackage", () => BuildPackageOrThrow(version));
                timing.Run("ValidateAndroidBuiltinPackage", () => ValidateBuiltinPackage(package.OutputPackageDirectory, version));
                string appVersion = timing.Run("BuildFinalApk", () =>
                    BuildFinalPlayerWithIncrementedPatchVersion(context, true));
                timing.Run("ValidateFinalApk", context.ValidateFinalApk);
                finalApkValidated = true;
                timing.Run("CopyPlayerGuide", () => context.CopyPlayerGuide(PlayerGuidePath, PlayerGuideFileName));
                timing.Run("CleanupPreparatoryPlayer", context.CleanupPreparatoryDirectory);

                string appUpdatePath = timing.Run("PublishAndroidApk", () => PublishCompleteApk(context.FinalApkPath, appVersion));

                Debug.Log(
                    $"[AndroidBuildUtility] Android 完整打包完成：Version={version}，AppVersion={appVersion}，APK={context.FinalApkPath}，" +
                    $"Size={new FileInfo(context.FinalApkPath).Length}，DLLs={copied.Count}，整包更新={appUpdatePath}\n{timing.BuildSummary()}");
                OfferStartServer("Android 打包成功", $"APK: {context.FinalApkPath}\n应用版本: {appVersion}\n整包更新: {appUpdatePath}\nPlayer、HybridCLR DLL 与内置资源均已校验。\n\n真机安装、启动、画面、安全区和后台恢复仍需人工检查。");
            }
            catch (Exception exception)
            {
                // 发布阶段异常不能删除已经生成并验证的安装包。
                if (finalApkValidated) context.CleanupPreparatoryDirectory();
                else context.CleanupFailedBuild();
                Debug.LogError(
                    $"[AndroidBuildUtility] Android 完整打包在阶段 {timing.CurrentStage ?? "（无）"} 失败：{exception.Message}\n" +
                    timing.BuildSummary());
                ShowDialog(
                    "Android 构建失败",
                    BuildWorkflowUtility.FormatFailure(timing, exception.Message,
                        finalApkValidated ? context.FinalApkPath : null));
                throw;
            }
            finally
            {
                previous.Restore();
                EditorUtility.ClearProgressBar();
                s_isBuilding = false;
            }
        }

        /// <summary>
        /// 生成并发布 Android Host 基础 APK；完整内置资源仍作为断网回退，发布失败保留已验证 APK。
        /// </summary>
        public static void BuildHostBaseCodeAndResourcesBatch()
        {
            if (!BeginWorkflow("Android Host 基础包完整打包"))
                return;

            var timing = new BuildStageTiming("Android Host 基础包完整打包");
            var context = new AndroidBuildContext(DateTime.Now, Guid.NewGuid().ToString("N"));
            var previous = AndroidEditorOptions.Capture();
            bool finalApkValidated = false;
            try
            {
                timing.Run("EnsureAndroidBuildTarget", EnsureAndroidBuildTarget);
                timing.Run("ValidateAndroidEnvironment", ValidateAndroidEnvironment);
                timing.Run("ValidateAndroidHostConfig", ValidateAndroidHostConfig);
                timing.Run("ValidateAndroidNetworkConfig", ValidateAndroidNetworkConfig);
                previous.ApplyTestBuildOptions();

                timing.Run("PrepareOutputDirectories", context.PrepareDirectories);
                timing.Run("GeneratePrePlayer", () => HybridCLRBuildUtility.GeneratePrePlayer(BuildTarget.Android));
                timing.Run("BuildPreparatoryPlayer", () => BuildPlayer(context.PreparatoryApkPath, false));
                timing.Run("ValidatePreparatoryAot", ValidateAndroidAotOutput);
                timing.Run("GeneratePostPlayer", () => HybridCLRBuildUtility.GeneratePostPlayer(BuildTarget.Android));
                var copied = timing.Run("CopyGeneratedDlls", () => HybridCLRBuildUtility.CopyGeneratedDlls(BuildTarget.Android, false));
                timing.Run("ValidateGeneratedDlls", HybridCLRBuildUtility.ValidateGeneratedDlls);

                string version = BuildWorkflowUtility.CreateResourceVersion(context.Timestamp);
                var package = timing.Run("BuildAndroidHostBasePackage", () => BuildPackageOrThrow(version, true));
                timing.Run("ValidateAndroidBuiltinPackage", () => ValidateBuiltinPackage(package.OutputPackageDirectory, version));
                string appVersion = timing.Run("BuildFinalHostApk", () =>
                    BuildFinalPlayerWithIncrementedPatchVersion(context, false));
                timing.Run("ValidateFinalHostApk", context.ValidateFinalApk);
                finalApkValidated = true;
                timing.Run("CopyPlayerGuide", () => context.CopyPlayerGuide(PlayerGuidePath, PlayerGuideFileName));
                timing.Run("CleanupPreparatoryPlayer", context.CleanupPreparatoryDirectory);
                timing.Run("SaveAndroidHostState", () => SaveAndroidHostState(version, "HostBase", package.OutputPackageDirectory, context.FinalApkPath, true, context.BootFingerprint));

                string appUpdatePath = timing.Run("PublishAndroidApk", () => PublishCompleteApk(context.FinalApkPath, appVersion));

                Debug.Log(
                    $"[AndroidBuildUtility] Android Host 基础包完成：Version={version}，AppVersion={appVersion}，APK={context.FinalApkPath}，" +
                    $"Size={new FileInfo(context.FinalApkPath).Length}，DLLs={copied.Count}，整包更新={appUpdatePath}\n{timing.BuildSummary()}");
                OfferStartServer("Android Host 基础包成功", $"APK: {context.FinalApkPath}\n应用版本: {appVersion}\n资源版本: {version}\n整包更新: {appUpdatePath}\n\n首次安装此 APK 后，后续 Game 代码和 YooAsset 资源可走 Android Host 更新。");
            }
            catch (Exception exception)
            {
                if (finalApkValidated) context.CleanupPreparatoryDirectory();
                else context.CleanupFailedBuild();
                Debug.LogError(
                    $"[AndroidBuildUtility] Android Host 基础包在阶段 {timing.CurrentStage ?? "（无）"} 失败：{exception.Message}\n" +
                    timing.BuildSummary());
                ShowDialog("Android Host 基础包失败", BuildWorkflowUtility.FormatFailure(timing, exception.Message,
                    finalApkValidated ? context.FinalApkPath : null));
                throw;
            }
            finally
            {
                previous.Restore();
                EditorUtility.ClearProgressBar();
                s_isBuilding = false;
            }
        }

        /// <summary>
        /// 生成只携带 Boot 与资源清单的 Android 首包，并发布同版本完整远端资源。
        /// </summary>
        public static void BuildSmallHostBaseCodeAndResourcesBatch()
        {
            if (!BeginWorkflow("Android 首包完整打包"))
                return;

            var timing = new BuildStageTiming("Android 首包完整打包");
            var context = new AndroidBuildContext(DateTime.Now, Guid.NewGuid().ToString("N"));
            var previous = AndroidEditorOptions.Capture();
            bool finalApkValidated = false;
            try
            {
                timing.Run("EnsureAndroidBuildTarget", EnsureAndroidBuildTarget);
                timing.Run("ValidateAndroidEnvironment", ValidateAndroidEnvironment);
                timing.Run("ValidateAndroidHostConfig", ValidateAndroidHostConfig);
                timing.Run("ValidateAndroidNetworkConfig", ValidateAndroidNetworkConfig);
                previous.ApplyTestBuildOptions();

                timing.Run("PrepareOutputDirectories", context.PrepareDirectories);
                timing.Run("GeneratePrePlayer", () => HybridCLRBuildUtility.GeneratePrePlayer(BuildTarget.Android));
                timing.Run("BuildPreparatoryPlayer", () => BuildPlayer(context.PreparatoryApkPath, false));
                timing.Run("ValidatePreparatoryAot", ValidateAndroidAotOutput);
                timing.Run("GeneratePostPlayer", () => HybridCLRBuildUtility.GeneratePostPlayer(BuildTarget.Android));
                var copied = timing.Run("CopyGeneratedDlls", () => HybridCLRBuildUtility.CopyGeneratedDlls(BuildTarget.Android, false));
                timing.Run("ValidateGeneratedDlls", HybridCLRBuildUtility.ValidateGeneratedDlls);

                string version = BuildWorkflowUtility.CreateResourceVersion(context.Timestamp);
                var package = timing.Run("BuildAndroidSmallHostPackage", () => BuildSmallHostPackageOrThrow(version));
                timing.Run("ValidateAndroidHostPackage", () => ValidateHostPackage(package.OutputPackageDirectory, version));
                timing.Run("ValidateManifestOnlyBuiltin", () => ValidateManifestOnlyBuiltin(version));
                string appVersion = timing.Run("BuildFinalSmallHostApk", () =>
                    BuildFinalPlayerWithIncrementedPatchVersion(context, false));
                timing.Run("ValidateFinalSmallHostApk", context.ValidateFinalApk);
                finalApkValidated = true;
                timing.Run("CopyPlayerGuide", () => context.CopyPlayerGuide(PlayerGuidePath, PlayerGuideFileName));
                timing.Run("CleanupPreparatoryPlayer", context.CleanupPreparatoryDirectory);

                var publish = timing.Run("PublishAndroidResourceServer", ResourceServerPublishUtility.PublishOrUpdateLatestAndroidPackage);
                if (!publish.Success)
                    throw new InvalidOperationException($"首包资源服同步失败：{publish.Error}");

                string appUpdatePath = timing.Run("PublishAndroidApk", () => AndroidApkPublishUtility.Publish(
                    context.FinalApkPath, publish.BundleDirectory, appVersion, PlayerSettings.Android.bundleVersionCode));

                timing.Run("SaveAndroidHostState", () => SaveAndroidHostState(
                    version,
                    "SmallHostBase",
                    package.OutputPackageDirectory,
                    context.FinalApkPath,
                    true,
                    context.BootFingerprint));

                Debug.Log(
                    $"[AndroidBuildUtility] Android 首包完成：Version={version}，AppVersion={appVersion}，APK={context.FinalApkPath}，" +
                    $"大小={new FileInfo(context.FinalApkPath).Length}，DLL数量={copied.Count}，资源服目录={publish.BundleDirectory}，整包更新={appUpdatePath}\n" +
                    timing.BuildSummary());
                StartPublishedResourceServer(publish.BundleDirectory, version, timing);
            }
            catch (Exception exception)
            {
                // 发布失败时保留已经验证的首包，便于修复资源服后重新发布。
                if (finalApkValidated) context.CleanupPreparatoryDirectory();
                else context.CleanupFailedBuild();
                Debug.LogError(
                    $"[AndroidBuildUtility] Android 首包在阶段 {timing.CurrentStage ?? "（无）"} 失败：{exception.Message}\n" +
                    timing.BuildSummary());
                ShowDialog("Android 首包失败", BuildWorkflowUtility.FormatFailure(timing, exception.Message,
                    finalApkValidated ? context.FinalApkPath : null));
                throw;
            }
            finally
            {
                previous.Restore();
                EditorUtility.ClearProgressBar();
                s_isBuilding = false;
            }
        }

        /// <summary>
        /// 复用最近一次 Android Host 基础 APK 的 AOT 基线，只重编 Game.dll 并生成远端 YooAsset 资源版本。
        /// 同步发布包后启动局域网资源服并检测版本；启动失败保留已生成和发布的资源。
        /// </summary>
        public static void BuildHostCodeAndResourcesBatch()
        {
            if (!BeginWorkflow("Android Host 代码+资源更新", () => AndroidBootBuildGuard.ValidateBeforeHotUpdate(
                    LoadStateOrThrow(), Directory.GetParent(Application.dataPath).FullName)))
                return;

            var timing = new BuildStageTiming("Android Host 代码+资源更新");
            string retainedPackageDirectory = null;
            try
            {
                BuildState state = timing.Run("LoadAndroidHostState", LoadStateOrThrow);
                timing.Run("ValidateAndroidBootBaseline", () => AndroidBootBuildGuard.ValidateBeforeHotUpdate(
                    state, Directory.GetParent(Application.dataPath).FullName));
                timing.Run("EnsureAndroidBuildTarget", EnsureAndroidBuildTarget);
                timing.Run("ValidateAndroidEnvironment", ValidateAndroidEnvironment);
                timing.Run("ValidateAndroidHostConfig", ValidateAndroidHostConfig);
                timing.Run("ValidateAndroidAotBaseline", () => ValidateAndroidAotBaseline(state));

                timing.Run("CompileHotUpdateDll", () => HybridCLRBuildUtility.CompileHotUpdateDll(BuildTarget.Android));
                var copied = timing.Run("CopyGeneratedDllsFromBaseline", () =>
                    HybridCLRBuildUtility.CopyGeneratedDllsFromBaseline(
                        BuildTarget.Android,
                        state.LastAndroidAotBaseline.AotDirectory,
                        state.LastAndroidAotBaseline.RuntimeListPath));
                timing.Run("ValidateGeneratedDlls", HybridCLRBuildUtility.ValidateGeneratedDlls);

                string version = BuildWorkflowUtility.CreateResourceVersion(DateTime.Now);
                var package = timing.Run("BuildAndroidHostPackage", () => BuildPackageOrThrow(version, false));
                timing.Run("ValidateAndroidHostPackage", () => ValidateHostPackage(package.OutputPackageDirectory, version));
                retainedPackageDirectory = package.OutputPackageDirectory;
                // 长流程中 Boot 可能被编辑；发布前复核，不能推广与宿主不兼容的资源版本。
                timing.Run("RevalidateAndroidBootBaselineBeforePublish", () => AndroidBootBuildGuard.ValidateBeforeHotUpdate(
                    state, Directory.GetParent(Application.dataPath).FullName));
                timing.Run("SaveAndroidHostState", () => SaveAndroidHostState(version, "Host", package.OutputPackageDirectory, null, false));

                var publish = timing.Run("PublishAndroidResourceServer", ResourceServerPublishUtility.PublishOrUpdateLatestAndroidPackage);
                if (!publish.Success)
                {
                    ShowDialog("Android Host 更新已生成，资源服同步失败",
                        BuildWorkflowUtility.FormatFailure(timing, publish.Error, package.OutputPackageDirectory));
                    return;
                }

                Debug.Log($"[AndroidBuildUtility] Android Host 更新完成：Version={version}，DLLs={copied.Count}，ResourceServer={publish.BundleDirectory}\n{timing.BuildSummary()}");
                StartPublishedResourceServer(publish.BundleDirectory, version, timing);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[AndroidBuildUtility] Android Host 更新在阶段 {timing.CurrentStage ?? "（无）"} 失败：{exception.Message}\n" +
                    timing.BuildSummary());
                ShowDialog("Android Host 更新失败", BuildWorkflowUtility.FormatFailure(timing, exception.Message,
                    retainedPackageDirectory));
                throw;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                s_isBuilding = false;
            }
        }

        /// <summary>
        /// Android 工作流启动时同步切换目标平台；切换失败时在任何构建产物生成前终止。
        /// </summary>
        private static void EnsureAndroidBuildTarget()
        {
            const BuildTarget target = BuildTarget.Android;
            if (EditorUserBuildSettings.activeBuildTarget == target)
                return;

            if (Application.isBatchMode)
                throw new InvalidOperationException(
                    $"批处理模式不能自动切换 Build Target，请使用 -buildTarget Android 启动 Unity；当前为 {EditorUserBuildSettings.activeBuildTarget}。");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, target))
                throw new InvalidOperationException("当前 Unity 未安装可用的 Android Build Support。");

            BuildTarget previous = EditorUserBuildSettings.activeBuildTarget;
            Debug.Log($"[AndroidBuildUtility] 正在自动切换 Build Target：{previous} -> {target}");
            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, target);
            if (!switched || EditorUserBuildSettings.activeBuildTarget != target)
                throw new InvalidOperationException($"Build Target 自动切换失败：{previous} -> {target}");
        }

        /// <summary>
        /// 在任何产物目录创建前验证构建机和项目配置；失败不产生可误认为成功的 APK。
        /// </summary>
        internal static void ValidateAndroidEnvironment()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("当前 Unity 未安装可用的 Android Build Support。");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException(
                    $"Build Target 自动切换后仍不是 Android，当前为 {EditorUserBuildSettings.activeBuildTarget}。");

            ValidateEmbeddedTool("SDK", "SDK");
            ValidateEmbeddedTool("NDK", "NDK");
            ValidateEmbeddedTool("OpenJDK", "OpenJDK");

            if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) != ScriptingImplementation.IL2CPP)
                throw new InvalidOperationException("Android 脚本后端必须是 IL2CPP。");
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
                throw new InvalidOperationException($"Android CPU 必须仅为 ARM64，当前为 {PlayerSettings.Android.targetArchitectures}。");
            if (!string.Equals(PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android), ApplicationIdentifier, StringComparison.Ordinal))
                throw new InvalidOperationException($"Android 包名必须是 {ApplicationIdentifier}。");
            if (PlayerSettings.Android.useCustomKeystore)
                throw new InvalidOperationException("Android 测试包必须使用 Unity 自动调试签名，不能启用自定义 keystore。");
            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.AutoRotation ||
                PlayerSettings.allowedAutorotateToPortrait ||
                PlayerSettings.allowedAutorotateToPortraitUpsideDown ||
                !PlayerSettings.allowedAutorotateToLandscapeLeft ||
                !PlayerSettings.allowedAutorotateToLandscapeRight)
            {
                throw new InvalidOperationException("Android 屏幕方向必须为自动旋转但仅允许左右横屏。");
            }

            var enabledScenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
            if (enabledScenes.Length != 1 || !string.Equals(enabledScenes[0].path, BootScene, StringComparison.Ordinal))
                throw new InvalidOperationException($"Build Settings 必须只启用 {BootScene}。");

            RequireDirectory("Assets/Game/Config");
            RequireDirectory("Assets/Game/Dlls");
            Debug.Log("[AndroidBuildUtility] Android 构建环境校验通过：Build Support、SDK、NDK、OpenJDK、IL2CPP、ARM64、包名、横屏、调试签名和 Boot 场景均符合要求。");
        }

        /// <summary>
        /// 校验 Host 地址确实能由真机访问；localhost/回环地址只指向手机自身，禁止写入基础 APK。
        /// </summary>
        internal static void ValidateAndroidHostConfig()
        {
            RequireFile(HostConfigPath);
            var json = File.ReadAllText(HostConfigPath);
            var config = JsonUtility.FromJson<AndroidHostConfigData>(json);
            ValidateAndroidHostServerUrl(config?.resourceServer);
        }

        internal static void ValidateAndroidHostServerUrl(string resourceServer)
        {
            if (string.IsNullOrWhiteSpace(resourceServer) ||
                !Uri.TryCreate(resourceServer.Trim(), UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("Android Host resourceServer 必须是绝对 HTTP/HTTPS URL。");
            }

            if (uri.IsLoopback ||
                string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Host, "0.0.0.0", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Android 真机不能使用回环资源地址 {uri}；请将 {HostConfigPath} 改为手机可访问的局域网 IP、域名或 HTTPS 地址。");
            }

            if (uri.Scheme == Uri.UriSchemeHttp && PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
            {
                throw new InvalidOperationException(
                    $"Android HTTP Host 需要 PlayerSettings.insecureHttpOption=AlwaysAllowed，当前为 {PlayerSettings.insecureHttpOption}。");
            }
        }

        /// <summary>
        /// 校验将写入 APK 的 GameGate 端点，防止 Android 真机把回环地址误认为电脑服务器。
        /// </summary>
        internal static void ValidateAndroidNetworkConfig()
        {
            RequireFile(NetworkConfigPath);
            string json = File.ReadAllText(NetworkConfigPath);
            var config = JsonUtility.FromJson<AndroidNetworkConfigData>(json);
            ValidateAndroidGameServerEndpoint(config?.gameGateEndpoint);
        }

        internal static void ValidateAndroidGameServerEndpoint(string endpointText)
        {
            if (!NetworkEndpoint.TryParse(endpointText, out _))
            {
                throw new InvalidOperationException(
                    $"Android gameGateEndpoint 必须是有效的 host:port，配置文件：{NetworkConfigPath}。");
            }
        }

        private static void ValidateEmbeddedTool(string directoryName, string displayName)
        {
            string path = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer", directoryName);
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException($"Unity 内置 Android {displayName} 不存在: {path}");
        }

        private static void ValidateAndroidAotOutput()
        {
            string directory = HybridCLRBuildUtility.GetAotDirectory(BuildTarget.Android);
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"Android Player AOT 输出目录不存在: {directory}");
            if (Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly).Length == 0)
                throw new InvalidOperationException($"Android Player AOT 输出为空: {directory}");
            Debug.Log($"[AndroidBuildUtility] Android AOT 输出校验通过：{directory}");
        }

        private static YooAssetBuildUtility.BuildResult BuildPackageOrThrow(string version, bool fullBuiltin)
        {
            var result = YooAssetBuildUtility.BuildDefaultPackage(version, fullBuiltin);
            if (!result.Success)
                throw new InvalidOperationException(result.Error ?? "Android YooAsset 资源构建失败。");
            return result;
        }

        private static YooAssetBuildUtility.BuildResult BuildPackageOrThrow(string version)
        {
            return BuildPackageOrThrow(version, true);
        }

        private static YooAssetBuildUtility.BuildResult BuildSmallHostPackageOrThrow(string version)
        {
            var result = YooAssetBuildUtility.BuildManifestOnlyBuiltinPackage(version);
            if (!result.Success)
                throw new InvalidOperationException(result.Error ?? "Android 首包 YooAsset 资源构建失败。");
            return result;
        }

        private static void ValidateBuiltinPackage(string outputDirectory, string version)
        {
            if (!YooAssetBuildUtility.ValidateBuildResult(outputDirectory, version))
                throw new InvalidOperationException($"Android YooAsset 输出校验失败: {outputDirectory}");

            string builtinDirectory = YooAssetBuildUtility.GetStreamingAssetsDirectory();
            if (!YooAssetBuildUtility.ValidateBuildResult(builtinDirectory, version))
                throw new InvalidOperationException($"Android StreamingAssets 内置资源校验失败: {builtinDirectory}");
            RequireFile(Path.Combine(builtinDirectory, "BuildinCatalog.bytes"));
            RequireFile(Path.Combine(builtinDirectory, "BuildinCatalog.json"));
        }

        private static void ValidateHostPackage(string outputDirectory, string version)
        {
            if (!YooAssetBuildUtility.ValidateBuildResult(outputDirectory, version))
                throw new InvalidOperationException($"Android Host YooAsset 输出校验失败: {outputDirectory}");
        }

        private static void ValidateManifestOnlyBuiltin(string version)
        {
            if (!YooAssetBuildUtility.ValidateManifestOnlyBuiltinPackage(version))
                throw new InvalidOperationException("Android 首包内置清单校验失败。");
        }

        private static BuildState LoadStateOrThrow()
        {
            BuildState state = BuildStateStore.Load(out string warning);
            if (!string.IsNullOrEmpty(warning))
                Debug.LogWarning(warning);
            return state;
        }

        private static void ValidateAndroidAotBaseline(BuildState state)
        {
            if (!BuildStateStore.TryValidateAndroidAotBaseline(state, BuildTarget.Android, out string error))
                throw new InvalidOperationException(
                    $"Android Host AOT 基线不可用：{error} 请先执行 Android Host 基础包或首包完整打包。");
        }

        /// <summary>
        /// 只更新 Android 专属记录；Windows LastHost/LastAotBaseline 始终保持不变。
        /// </summary>
        private static void SaveAndroidHostState(
            string version,
            string buildKind,
            string packageDirectory,
            string playerPath,
            bool updateAot,
            string bootFingerprint = null)
        {
            if (!string.IsNullOrEmpty(playerPath) && string.IsNullOrWhiteSpace(bootFingerprint))
                throw new InvalidOperationException("Android 首包缺少本次构建的 Boot 指纹，不能保存宿主基线，请重新执行首包完整打包。");
            BuildState state = LoadStateOrThrow();
            long completedUtcTicks = DateTime.UtcNow.Ticks;
            string gameDllDirectory = HybridCLRBuildUtility.GetGameDllDirectory();

            state.LastAndroidHost = new HostBuildRecord
            {
                Version = version,
                BuildTarget = BuildTarget.Android.ToString(),
                OutputPackageDirectory = Path.GetFullPath(packageDirectory),
                GameDllDirectory = gameDllDirectory,
                CompletedUtcTicks = completedUtcTicks
            };
            if (updateAot)
            {
                state.LastAndroidAotBaseline = new AotBaselineRecord
                {
                    BuildTarget = BuildTarget.Android.ToString(),
                    AotDirectory = HybridCLRBuildUtility.GetAotDirectory(BuildTarget.Android),
                    RuntimeListPath = HybridCLRBuildUtility.GetRuntimeListPath(),
                    GameDllDirectory = gameDllDirectory,
                    CompletedUtcTicks = completedUtcTicks
                };
            }

            state.LastAndroidPackage = new PackageBuildRecord
            {
                Version = version,
                BuildTarget = BuildTarget.Android.ToString(),
                OutputPackageDirectory = Path.GetFullPath(packageDirectory),
                BuildKind = buildKind,
                CompletedUtcTicks = completedUtcTicks
            };
            if (!string.IsNullOrEmpty(playerPath))
            {
                state.LastAndroidPlayer = new PlayerBuildRecord
                {
                    PlayerPath = Path.GetFullPath(playerPath),
                    Version = version,
                    BuildKind = buildKind,
                    CompletedUtcTicks = completedUtcTicks,
                    BootFingerprint = bootFingerprint
                };
            }

            BuildStateStore.Save(state);
        }

        /// <summary>
        /// 构建临时或最终 Android Player；报告失败、产物缺失或空文件均视为失败。
        /// </summary>
        private static void BuildPlayer(string apkPath, bool forceBuiltinResources)
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { BootScene },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.CompressWithLz4,
                extraScriptingDefines = forceBuiltinResources
                    ? new[] { BuiltinResourcesScriptingDefine }
                    : Array.Empty<string>()
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report == null)
                throw new InvalidOperationException($"Android Player 构建未返回 BuildReport: {apkPath}");
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Android Player 构建失败: {report.summary.result}");
            if (!File.Exists(apkPath) || new FileInfo(apkPath).Length <= 0)
                throw new InvalidOperationException($"Android Player 报告成功但 APK 不存在或为空: {apkPath}");
            Debug.Log($"[AndroidBuildUtility] Android Player 构建成功：{apkPath}，报告大小={report.summary.totalSize}，文件大小={new FileInfo(apkPath).Length}");
        }

        /// <summary>
        /// 最终 APK 构建前递增应用版本的补丁号；构建失败时恢复原版本，临时 Player 不消耗版本号。
        /// </summary>
        private static string BuildFinalPlayerWithIncrementedPatchVersion(AndroidBuildContext context, bool forceBuiltinResources)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string bootFingerprint = AndroidBootBuildGuard.ComputeFingerprint(projectRoot);
            string previousVersion = PlayerSettings.bundleVersion;
            int previousCode = PlayerSettings.Android.bundleVersionCode;
            if (previousCode == int.MaxValue) throw new InvalidOperationException("Android 版本码已达到上限。");
            string nextVersion = IncrementAndroidPatchVersion(previousVersion);
            context.SetFinalApkVersion(nextVersion);
            PlayerSettings.bundleVersion = nextVersion;
            // 整包覆盖更新要求发布版本码递增，与版本名在失败时一起回滚。
            PlayerSettings.Android.bundleVersionCode = previousCode + 1;

            try
            {
                BuildPlayer(context.FinalApkPath, forceBuiltinResources);
                // 打包中途修改 Boot 时不能把新源码错误记录为已生成 APK 的基线。
                if (!string.Equals(bootFingerprint, AndroidBootBuildGuard.ComputeFingerprint(projectRoot), StringComparison.Ordinal))
                    throw new InvalidOperationException("打包期间 Boot 发生变化，请重新执行 Android 首包完整打包。");
                context.BootFingerprint = bootFingerprint;
                AssetDatabase.SaveAssets();
                Debug.Log($"[AndroidBuildUtility] Android 应用版本已递增：{previousVersion} -> {nextVersion}");
                return nextVersion;
            }
            catch
            {
                // 最终 APK 未生成时不消耗版本号，便于下一次修复后重新构建同一版本。
                PlayerSettings.bundleVersion = previousVersion;
                PlayerSettings.Android.bundleVersionCode = previousCode;
                AssetDatabase.SaveAssets();
                throw;
            }
        }

        /// <summary>完整包只发布 APK 到最近资源服；首次创建资源服时才复制资源，不替换既有热更版本。</summary>
        private static string PublishCompleteApk(string apkPath, string appVersion)
        {
            string workspaceRoot = Directory.GetParent(Application.dataPath).Parent.FullName;
            string releaseRoot = Path.Combine(workspaceRoot, "Release", "ResourceServer", "Android");
            if (!ResourceServerPublishUtility.TryFindLatestBundle(releaseRoot, out _, out _))
            {
                var publish = ResourceServerPublishUtility.PublishLatestValidAndroidPackage();
                if (!publish.Success)
                    throw new InvalidOperationException($"创建 Android 资源服失败：{publish.Error}");
            }
            return AndroidApkPublishUtility.PublishToLatestBundle(
                apkPath, releaseRoot, appVersion, PlayerSettings.Android.bundleVersionCode);
        }

        /// <summary>最终安装包以英文游戏名和包内应用版本命名；构建批次目录负责区分不同产物。</summary>
        internal static string BuildAndroidApkFileName(string appVersion)
        {
            if (string.IsNullOrWhiteSpace(appVersion))
                throw new ArgumentException("Android 应用版本不能为空。", nameof(appVersion));
            return $"FirepowerLoop-{appVersion}.apk";
        }

        /// <summary>
        /// 将严格的 major.minor.patch 版本递增一位补丁号，例如 0.1.0 -> 0.1.1。
        /// </summary>
        internal static string IncrementAndroidPatchVersion(string version)
        {
            string[] parts = version?.Split('.');
            if (parts == null || parts.Length != 3 ||
                !int.TryParse(parts[0], out int major) || major < 0 ||
                !int.TryParse(parts[1], out int minor) || minor < 0 ||
                !int.TryParse(parts[2], out int patch) || patch < 0 || patch == int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Android 应用版本必须是 major.minor.patch 格式，当前值：{version ?? "<null>"}");
            }

            return $"{major}.{minor}.{patch + 1}";
        }

        private static void ShowResourceServerResult(ResourceServerPublishResult result)
        {
            if (result.Success)
            {
                ShowDialog(result.Operation + "成功", $"版本: {result.Version}\n目录: {result.BundleDirectory}");
                return;
            }

            ShowDialog(result.Operation + "失败", result.Error ?? "未知错误");
        }

        /// <summary>先完成只读前置检查；切平台时登记请求并退出当前调用栈，同平台才创建构建上下文。</summary>
        private static bool BeginWorkflow(string workflowName, Action preflight = null)
        {
            if (!BeginBuild()) return false;
            try { preflight?.Invoke(); }
            catch (Exception exception)
            {
                // 前置检查失败不得切平台或留下构建锁；此时尚未登记恢复请求。
                s_isBuilding = false;
                ShowDialog(workflowName + "被阻止", exception.Message);
                throw;
            }
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) return true;

            var timing = new BuildStageTiming(workflowName);
            try
            {
                SessionState.SetString(PendingWorkflowKey, workflowName);
                SessionState.SetBool(ReloadCompletedKey, false);
                timing.Run("EnsureAndroidBuildTarget", EnsureAndroidBuildTarget);
                Debug.Log($"[AndroidBuildUtility] 已暂停 {workflowName}，等待 Android 脚本重载后自动继续。");
                return false;
            }
            catch (Exception exception)
            {
                ClearPendingWorkflow();
                ShowDialog("Android 平台切换失败", BuildWorkflowUtility.FormatFailure(timing, exception.Message));
                throw;
            }
            finally
            {
                s_isBuilding = false;
            }
        }

        /// <summary>重载后恢复监听，只有已登记的请求才能推进；普通脚本刷新不触发打包。</summary>
        [InitializeOnLoadMethod]
        private static void ObservePendingWorkflow()
        {
            EditorApplication.update -= ResumePendingWorkflow;
            EditorApplication.update += ResumePendingWorkflow;
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        /// <summary>旧域不能自行确认平台宏变化，必须等真实程序集重载完成。</summary>
        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingWorkflowKey, "")))
                SessionState.SetBool(ReloadCompletedKey, true);
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (!messages.Any(message => message.type == CompilerMessageType.Error)) return;
            CancelPendingWorkflow("PlatformScriptsCompilation", $"平台切换后脚本编译失败：{assemblyPath}");
        }

        /// <summary>空闲后消费请求一次，未知入口和错误平台一律取消；异常不安排自动重试。</summary>
        private static void ResumePendingWorkflow()
        {
            string workflow = SessionState.GetString(PendingWorkflowKey, "");
            if (string.IsNullOrEmpty(workflow)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                CancelPendingWorkflow("ResumeAfterPlatformReload", "已进入 PlayMode，取消待恢复的 Android 打包。");
                return;
            }
            if (!BuildWorkflowUtility.CanResume(SessionState.GetBool(ReloadCompletedKey, false),
                    EditorApplication.isCompiling, EditorApplication.isUpdating)) return;
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                CancelPendingWorkflow("ResumeAfterPlatformReload", "重载后目标不是 Android，已取消打包。");
                return;
            }
            var action = ResolveWorkflow(workflow);
            if (action == null)
            {
                CancelPendingWorkflow("ResumeAfterPlatformReload", "无法恢复未知工作流：" + workflow);
                return;
            }
            ClearPendingWorkflow();
            try { action(); }
            catch (Exception exception)
            {
                // 入口已显示失败原因；隔离编辑器更新回调，且消费过的请求不会每帧重跑。
                Debug.LogException(exception);
            }
        }

        /// <summary>只解析已授权的四个菜单入口；解析本身不执行构建。</summary>
        private static Action ResolveWorkflow(string workflow)
        {
            switch (workflow)
            {
                case "Android 最新代码资源完整打包": return BuildLatestCodeAndResourcesBatch;
                case "Android Host 基础包完整打包": return BuildHostBaseCodeAndResourcesBatch;
                case "Android 首包完整打包": return BuildSmallHostBaseCodeAndResourcesBatch;
                case "Android Host 代码+资源更新": return BuildHostCodeAndResourcesBatch;
                default: return null;
            }
        }

        private static void CancelPendingWorkflow(string stage, string reason)
        {
            string workflow = SessionState.GetString(PendingWorkflowKey, "");
            if (string.IsNullOrEmpty(workflow)) return;
            ClearPendingWorkflow();
            var timing = new BuildStageTiming(workflow);
            timing.Run(stage, () => { });
            string details = BuildWorkflowUtility.FormatFailure(timing, reason);
            Debug.LogError("[AndroidBuildUtility] " + details);
            ShowDialog("Android 平台切换流程已取消", details);
        }

        private static void ClearPendingWorkflow()
        {
            SessionState.EraseString(PendingWorkflowKey);
            SessionState.EraseBool(ReloadCompletedKey);
        }

        private static bool BeginBuild()
        {
            if (HasActiveWorkflow || BuildUtility.HasActiveWorkflow)
            {
                Debug.LogWarning("[AndroidBuildUtility] Android 构建流程正在执行，请勿重复触发。");
                return false;
            }
            if (EditorApplication.isCompiling)
                throw new InvalidOperationException("Unity 正在编译，请稍后再试。");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Unity 正在进入或退出 PlayMode，请稍后再试。");

            s_isBuilding = true;
            return true;
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

        /// <summary>首包和 Host 更新同步后启动本次发布包；成功不弹窗，失败保留产物并显示原因。</summary>
        private static void StartPublishedResourceServer(string bundleDirectory, string version, BuildStageTiming timing)
        {
            string resourceRoot = Path.Combine(bundleDirectory, "ResourceFiles");
            var server = timing.Run("StartAndroidResourceServer", () =>
                LocalResourceServerUtility.StartAndProbe(resourceRoot, version, true));
            if (!server.Success)
            {
                ShowDialog("Android 资源已发布，资源服启动失败",
                    BuildWorkflowUtility.FormatFailure(timing, server.Error, bundleDirectory));
                return;
            }
            Debug.Log($"[AndroidBuildUtility] 资源服已启动：版本={version}，目录={bundleDirectory}，地址={server.BaseUrl}");
        }

        /// <summary>
        /// 完整包和 Host 基础包展示成功结果和启动选择；仅确认后启动 Android 局域网资源服，启动失败不撤销构建产物。
        /// 选择稍后不改变服务状态，批处理仅记录结果。
        /// </summary>
        private static void OfferStartServer(string title, string message)
        {
            string successMessage = message + "\n\n是否启动资源服？";
            if (Application.isBatchMode)
            {
                ShowDialog(title, successMessage);
                return;
            }

            if (EditorUtility.DisplayDialog(title, successMessage, "启动资源服", "稍后"))
                StartAndroidResourceServerFromLatestPublishedBundle();
        }

        private static void ShowDialog(string title, string message)
        {
            if (Application.isBatchMode)
                Debug.Log($"[AndroidBuildUtility] {title}：{message}");
            else
                EditorUtility.DisplayDialog(title, message, "OK");
        }

        /// <summary>
        /// 保存菜单调用前的全局构建选项；测试 APK 使用固定值，成功和失败都恢复调用者原设置。
        /// </summary>
        private readonly struct AndroidEditorOptions
        {
            private readonly bool _development;
            private readonly bool _allowDebugging;
            private readonly bool _connectProfiler;
            private readonly bool _buildAppBundle;

            private AndroidEditorOptions(bool development, bool allowDebugging, bool connectProfiler, bool buildAppBundle)
            {
                _development = development;
                _allowDebugging = allowDebugging;
                _connectProfiler = connectProfiler;
                _buildAppBundle = buildAppBundle;
            }

            public static AndroidEditorOptions Capture()
            {
                return new AndroidEditorOptions(
                    EditorUserBuildSettings.development,
                    EditorUserBuildSettings.allowDebugging,
                    EditorUserBuildSettings.connectProfiler,
                    EditorUserBuildSettings.buildAppBundle);
            }

            public void ApplyTestBuildOptions()
            {
                EditorUserBuildSettings.development = true;
                EditorUserBuildSettings.allowDebugging = false;
                EditorUserBuildSettings.connectProfiler = false;
                EditorUserBuildSettings.buildAppBundle = false;
            }

            public void Restore()
            {
                EditorUserBuildSettings.development = _development;
                EditorUserBuildSettings.allowDebugging = _allowDebugging;
                EditorUserBuildSettings.connectProfiler = _connectProfiler;
                EditorUserBuildSettings.buildAppBundle = _buildAppBundle;
            }
        }

        /// <summary>
        /// 持有单次构建的唯一目录身份，只允许清理本次临时目录和本次未完成批次。
        /// </summary>
        private sealed class AndroidBuildContext
        {
            public AndroidBuildContext(DateTime timestamp, string runId)
            {
                Timestamp = timestamp;
                string projectPath = Directory.GetParent(Application.dataPath)?.FullName ??
                                     throw new InvalidOperationException("无法确定 Unity 项目根目录。");
                BatchDirectory = Path.GetFullPath(Path.Combine(projectPath, "Build", "Android", timestamp.ToString("yyyyMMdd_HHmmss")));
                PreparatoryDirectory = Path.GetFullPath(Path.Combine(projectPath, "Temp", "AnotherDayBuild", "Android", runId));
                PreparatoryApkPath = Path.Combine(PreparatoryDirectory, "FirepowerLoop-preparatory.apk");
            }

            public DateTime Timestamp { get; }
            public string BatchDirectory { get; }
            public string PreparatoryDirectory { get; }
            /// <summary>仅在最终应用版本确定后设置；此前的预备 Player 不占用正式 APK 路径。</summary>
            public string FinalApkPath { get; private set; }
            /// <summary>本次最终 APK 构建前后核对一致的 Boot 指纹，仅成功构建后赋值。</summary>
            public string BootFingerprint { get; set; }
            public string PreparatoryApkPath { get; }

            public void SetFinalApkVersion(string appVersion)
            {
                FinalApkPath = Path.Combine(BatchDirectory, BuildAndroidApkFileName(appVersion));
            }

            public void PrepareDirectories()
            {
                if (Directory.Exists(BatchDirectory) || File.Exists(BatchDirectory))
                    throw new InvalidOperationException($"Android APK 批次已存在，拒绝覆盖: {BatchDirectory}");
                if (Directory.Exists(PreparatoryDirectory) || File.Exists(PreparatoryDirectory))
                    throw new InvalidOperationException($"Android 临时 Player 目录已存在，拒绝覆盖: {PreparatoryDirectory}");
                Directory.CreateDirectory(BatchDirectory);
                Directory.CreateDirectory(PreparatoryDirectory);
            }

            public void ValidateFinalApk()
            {
                if (!File.Exists(FinalApkPath))
                    throw new FileNotFoundException($"最终 Android APK 不存在: {FinalApkPath}");
                if (new FileInfo(FinalApkPath).Length <= 0)
                    throw new InvalidOperationException($"最终 Android APK 为空: {FinalApkPath}");
            }

            /// <summary>把面向玩家的安装与联机步骤放到 APK 同批目录，避免交付时遗漏。</summary>
            public void CopyPlayerGuide(string sourcePath, string fileName)
            {
                if (!File.Exists(sourcePath))
                    throw new FileNotFoundException($"Android 玩家说明不存在: {sourcePath}");

                string destination = Path.Combine(BatchDirectory, fileName);
                File.Copy(sourcePath, destination, false);
            }

            public void CleanupPreparatoryDirectory()
            {
                DeleteDirectory(PreparatoryDirectory);
            }

            public void CleanupFailedBuild()
            {
                DeleteDirectory(PreparatoryDirectory);
                DeleteDirectory(BatchDirectory);
            }

            private static void DeleteDirectory(string path)
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
        }

        [Serializable]
        private sealed class AndroidHostConfigData
        {
            public string resourceServer;
        }

        [Serializable]
        private sealed class AndroidNetworkConfigData
        {
            public string gameGateEndpoint;
        }
    }
}
#endif
