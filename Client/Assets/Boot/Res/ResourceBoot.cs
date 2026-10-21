using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;
using Boot.UI;

namespace Boot
{
    /// <summary>
    /// 在加载热更代码前初始化并校验启动资源，决定在线、缓存与内置回退来源；不负责加载程序集或进入游戏。
    /// </summary>
    public class ResourceBoot : MonoBehaviour
    {
        private const string PACKAGE_NAME = "DefaultPackage";
        private const int MAX_DOWNLOAD_COUNT = 10;
        /// <summary>版本文件很小，远端在该秒数内无响应便进入本地资源降级。</summary>
        private const int REMOTE_VERSION_TIMEOUT_SECONDS = 8;
        /// <summary>资源清单允许比版本文件更长的传输时间，超时后仍自动降级。</summary>
        private const int REMOTE_MANIFEST_TIMEOUT_SECONDS = 15;
        private const string LAST_SUCCESSFUL_HOST_VERSION_KEY = "FirepowerLoop.LastSuccessfulHostVersion";
        /// <summary>独立游戏在启动时统一准备基础与街机资源；在线下载和本地完整性校验共用此范围。</summary>
        private static readonly string[] StartupResourceTags =
        {
            "startup",
            "arcade"
        };
        private const double DOWNLOAD_SPEED_SMOOTHING = 0.25d;

        [Header("编辑器资源模式")]
        [SerializeField]
#if UNITY_EDITOR
        [Tooltip("编辑器运行时使用的资源加载模式")]
#endif
        private ResourcePlayMode _editorPlayMode = ResourcePlayMode.EditorSimulateMode;

        private ResourcePlayMode _actualPlayMode;
        private bool _isReady;
        private string _lastError;
        private ResourcePackage _package;
        private ResourceDownloaderOperation _downloader;
        private HotUpdateView _updateView;
        private string _resourceServer;
        private bool _isInitializing;
        /// <summary>当前轮次结束后废弃结果；仅在安全的操作完成边界停止推进，并由 Launcher 串行重启。</summary>
        private bool _serverChangeRequested;
        private long _lastDownloadBytes;
        private float _lastDownloadTime;
        /// <summary>指数平滑后的下载速度，单位为字节/秒；0 表示尚无可用样本。</summary>
        private double _smoothedDownloadBytesPerSecond;
        private string _pendingHostVersion;
        private BootResourceSource _pendingResourceSource;
        private string _detectedHostVersion;
        private ApkUpdateFlow _apkUpdate;
        private WindowsUpdateFlow _windowsUpdate;
        /// <summary>安装器已启动，启动流程只能退出，不能加载 Game.dll 或进入 Main。</summary>
        public bool InstallerExitRequested { get; private set; }
        private CancellationTokenSource _apkCancellation;
        public bool IsApkUpdateActive { get; private set; }
        public event Action ApkUpdateActivityChanged;

        public ResourcePlayMode PlayMode => _actualPlayMode;
        public bool IsReady => _isReady;
        public string LastError => _lastError;

#if UNITY_INCLUDE_TESTS
        public static Func<UniTask<string>> TestRawInitializationOutcome { get; set; }

        public static void ResetTestHooks()
        {
            TestRawInitializationOutcome = null;
        }
#endif

        private void Awake()
        {
            _isInitializing = false;
            _isReady = false;
            _lastError = null;
            _pendingHostVersion = null;
            _pendingResourceSource = BootResourceSource.EditorSimulate;
            _detectedHostVersion = string.Empty;
        }

        public async UniTask<BootOperationResult> InitializeAsync()
        {
            if (_updateView == null)
            {
                throw new InvalidOperationException(
                    "ResourceBoot 尚未配置 HotUpdateView，不能启动资源流程。");
            }

            if (_isInitializing)
            {
                Debug.LogWarning("[ResourceBoot] 资源系统正在初始化，请勿重复触发。");
                return _isReady
                    ? BootOperationResult.Success()
                    : BootOperationResult.Failed(BootFailure.Create(
                        BootFailureCode.HostInitializationFailed,
                        BootFailureStage.ResourceInitialization,
                        BootRetryScope.ResourceInitialization,
                        DiagnosticFields(("resourcePackage", PACKAGE_NAME))));
            }

            if (_isReady)
                return BootOperationResult.Success();

            _isInitializing = true;
            _serverChangeRequested = false;
            _lastError = null;
            _pendingHostVersion = null;
            _pendingResourceSource = BootResourceSource.EditorSimulate;
            _detectedHostVersion = string.Empty;
            BootResourceStartupState.Reset();

            try
            {
#if UNITY_INCLUDE_TESTS
                if (TestRawInitializationOutcome != null)
                {
                    return await InitializeFromTestRawOutcome();
                }
#endif

                WindowsInstaller.InvalidatePreviousHostCache();
                YooAssets.Initialize();
                DeterminePlayMode();

                BootOperationResult packageResult = await InitializePackage();
                if (!packageResult.Succeeded)
                    return packageResult;

                if (InstallerExitRequested) return Success();
                BootOperationResult validationResult = ValidateRequiredLocations();
                if (!validationResult.Succeeded)
                    return validationResult;

                BootResourceStartupState.Publish(new BootResourceStartupInfo(
                    _pendingResourceSource,
                    _resourceServer,
                    _package.GetPackageVersion(),
                    _detectedHostVersion));
                _isReady = true;
                _lastError = null;
                RefreshVersion();
                return BootOperationResult.Success();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _lastError = "资源初始化异常";
                return BootOperationResult.Failed(BootFailure.Unexpected(e, BootFailureStage.ResourceInitialization));
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public async UniTask<BootOperationResult> ReinitializeForRetryAsync()
        {
            _isReady = false;
            return await InitializeAsync();
        }

        /// <summary>请求更换地址：取消未完成下载，版本和清单操作等待完成后不再推进旧服务器流程。</summary>
        internal void RequestServerChange()
        {
            _serverChangeRequested = true;
            _apkCancellation?.Cancel();
            if (_downloader != null && !_downloader.IsDone)
                _downloader.CancelDownload();
        }

        private void RefreshVersion()
        {
            if (_updateView == null || _package == null)
                return;

            _updateView.SetVersion(_package.GetPackageVersion(), _detectedHostVersion);
        }

        private static IReadOnlyDictionary<string, string> DiagnosticFields(params (string key, string value)[] fields)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                result[field.key] = field.value;
            }

            return result;
        }

        private BootOperationResult Fail(
            BootFailureCode code,
            BootFailureStage stage,
            BootRetryScope retryScope,
            params (string key, string value)[] fields)
        {
            _lastError = code.ToString();
            return BootOperationResult.Failed(BootFailure.Create(code, stage, retryScope, DiagnosticFields(fields)));
        }

        private BootOperationResult Success()
        {
            return BootOperationResult.Success();
        }

        private void DeterminePlayMode()
        {
#if UNITY_EDITOR
            _actualPlayMode = _editorPlayMode;
#elif FIREPOWERLOOP_ANDROID_BUILTIN_RESOURCES
            // Android 完整测试包只从内置资源启动；服务器地址供进入游戏后的主动更新检查使用。
            _actualPlayMode = ResourcePlayMode.OfflinePlayMode;
#else
            _actualPlayMode = ResourcePlayMode.HostPlayMode;
#endif
        }

        /// <summary>
        /// 读取包内默认地址及玩家覆盖地址；Host 配置失败阻断启动，完整内置模式仅停用远端检查。
        /// </summary>
        private async UniTask<BootOperationResult> LoadBootstrapConfigAsync()
        {
            try
            {
                var config = await BootstrapConfig.LoadAsync();
                _resourceServer = config.ResourceServer;
                return null;
            }
            catch (Exception e)
            {
                if (_actualPlayMode == ResourcePlayMode.OfflinePlayMode)
                {
                    // 地址读取失败不影响内置资源启动，也不能沿用上轮地址或手机自身的回环地址。
                    _resourceServer = string.Empty;
                    Debug.LogWarning($"[ResourceBoot] 内置模式资源更新地址读取失败：{e.GetType().Name}；继续使用内置资源。");
                    return null;
                }

                Debug.LogException(e);
                return Fail(
                    BootFailureCode.BootstrapConfigInvalid,
                    BootFailureStage.BootstrapConfig,
                    BootRetryScope.None,
                    ("resourcePackage", PACKAGE_NAME));
            }
        }

        private async UniTask<BootOperationResult> InitializePackage()
        {
            await ResetPackageAsync();

            switch (_actualPlayMode)
            {
#if UNITY_EDITOR
                case ResourcePlayMode.EditorSimulateMode:
                    return await InitializeEditorSimulateMode();
#endif
                case ResourcePlayMode.OfflinePlayMode:
                    return await InitializeOfflineMode();
                case ResourcePlayMode.HostPlayMode:
                    return await InitializeHostMode();
                default:
                    return Fail(
                        BootFailureCode.HostInitializationFailed,
                        BootFailureStage.ResourceInitialization,
                        BootRetryScope.ResourceInitialization,
                        ("playMode", _actualPlayMode.ToString()));
            }
        }

        private async UniTask ResetPackageAsync()
        {
            var oldPackage = YooAssets.TryGetPackage(PACKAGE_NAME);
            if (oldPackage != null)
            {
                var destroyOperation = oldPackage.DestroyAsync();
                await destroyOperation.WaitAsync();
                YooAssets.RemovePackage(oldPackage);
            }

            _package = YooAssets.CreatePackage(PACKAGE_NAME);
            YooAssets.SetDefaultPackage(_package);
        }

#if UNITY_EDITOR
        private async UniTask<BootOperationResult> InitializeEditorSimulateMode()
        {
            SetStatus("编辑器模拟模式...");

            var simulateResult = EditorSimulateModeHelper.SimulateBuild(PACKAGE_NAME);

            var parameters = new EditorSimulateModeParameters();
            parameters.EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(simulateResult.PackageRootDirectory);

            var operation = _package.InitializeAsync(parameters);
            await operation.WaitAsync();

            if (operation.Status != EOperationStatus.Succeed)
            {
                return Fail(
                    BootFailureCode.HostInitializationFailed,
                    BootFailureStage.ResourceInitialization,
                    BootRetryScope.ResourceInitialization,
                    ("operationStatus", operation.Status.ToString()),
                    ("resourcePackage", PACKAGE_NAME));
            }

            _pendingResourceSource = BootResourceSource.EditorSimulate;
            return await EnsurePackageManifestAsync("编辑器模拟模式");
        }
#endif

        private async UniTask<BootOperationResult> InitializeOfflineMode()
        {
            // 只读取本地配置，不联网、不切换资源来源；更新检查沿用设置页保存的服务器地址。
            await LoadBootstrapConfigAsync();
            SetStatus("离线模式...");

            var parameters = new OfflinePlayModeParameters();
            parameters.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();

            var operation = _package.InitializeAsync(parameters);
            await operation.WaitAsync();

            if (operation.Status != EOperationStatus.Succeed)
            {
                return Fail(
                    BootFailureCode.BuiltinResourceInvalid,
                    BootFailureStage.ResourceInitialization,
                    BootRetryScope.None,
                    ("operationStatus", operation.Status.ToString()),
                    ("resourcePackage", PACKAGE_NAME));
            }

            _pendingResourceSource = BootResourceSource.BuiltinFallback;
            return await EnsurePackageManifestAsync("离线模式");
        }

        private async UniTask<BootOperationResult> EnsurePackageManifestAsync(string stage)
        {
            var versionOp = _package.RequestPackageVersionAsync(true, 30);
            await versionOp.WaitAsync();

            if (versionOp.Status != EOperationStatus.Succeed)
            {
                return Fail(
                    BootFailureCode.PackageVersionRequestFailed,
                    BootFailureStage.ResourceVersion,
                    BootRetryScope.ResourceInitialization,
                    ("operationStatus", versionOp.Status.ToString()),
                    ("resourcePackage", PACKAGE_NAME),
                    ("resourceStage", stage));
            }

            if (!string.IsNullOrEmpty(versionOp.PackageVersion))
            {
                _detectedHostVersion = versionOp.PackageVersion;
            }

            var manifestOp = _package.UpdatePackageManifestAsync(versionOp.PackageVersion, 30);
            await manifestOp.WaitAsync();

            if (manifestOp.Status != EOperationStatus.Succeed)
            {
                return Fail(
                    BootFailureCode.PackageManifestUpdateFailed,
                    BootFailureStage.ResourceManifest,
                    BootRetryScope.ResourceInitialization,
                    ("operationStatus", manifestOp.Status.ToString()),
                    ("resourceVersion", versionOp.PackageVersion),
                    ("resourceStage", stage));
            }

            RefreshVersion();
            return ValidateStartupResources();
        }

        /// <summary>
        /// 按在线、缓存、内置顺序尝试启动；仅清单首包回退失败时保留原始在线失败及重试能力。
        /// </summary>
        private async UniTask<BootOperationResult> InitializeHostMode()
        {
            BootOperationResult configResult = await LoadBootstrapConfigAsync();
            if (configResult != null)
                return configResult;

            // 稳定 Boot 先处理整包选择，完成或跳过后才请求热更新资源。
            if (AndroidApkInstaller.Supported || WindowsInstaller.Supported)
            {
                IsApkUpdateActive = true;
                ApkUpdateActivityChanged?.Invoke();
                try
                {
                    using (_apkCancellation = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy()))
                    {
                        if (WindowsInstaller.Supported)
                            InstallerExitRequested = await _windowsUpdate.RunAsync(_resourceServer, _apkCancellation.Token);
                        else await _apkUpdate.RunAsync(_resourceServer, _apkCancellation.Token);
                    }
                }
                catch (OperationCanceledException) when (_serverChangeRequested) { }
                finally
                {
                    _apkCancellation = null;
                    IsApkUpdateActive = false;
                    ApkUpdateActivityChanged?.Invoke();
                }
                if (_serverChangeRequested) return Fail(BootFailureCode.HostInitializationFailed,
                    BootFailureStage.ResourceInitialization, BootRetryScope.ResourceInitialization);
            }

            if (InstallerExitRequested) return Success();
            string lastSuccessVersion = PlayerPrefs.GetString(LAST_SUCCESSFUL_HOST_VERSION_KEY, "");
            bool hasCachedVersion = !string.IsNullOrEmpty(lastSuccessVersion);

            SetStatus("正在初始化资源系统...");

            var remoteServices = new RemoteServices(_resourceServer);
            var parameters = new HostPlayModeParameters();
            parameters.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
            parameters.CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices);

            var operation = _package.InitializeAsync(parameters);
            await operation.WaitAsync();

            BootOperationResult hostFailure = BootOperationResult.Failed(BootFailure.Create(
                BootFailureCode.HostInitializationFailed,
                BootFailureStage.ResourceInitialization,
                BootRetryScope.ResourceInitialization,
                DiagnosticFields(("operationStatus", operation.Status.ToString()),
                    ("resourcePackage", PACKAGE_NAME), ("operationError", operation.Error))));

            if (operation.Status == EOperationStatus.Succeed)
            {
                if (_serverChangeRequested) return hostFailure;
                SetStatus("正在获取资源版本...");
                var versionOp = _package.RequestPackageVersionAsync(true, REMOTE_VERSION_TIMEOUT_SECONDS);
                await versionOp.WaitAsync();
                if (_serverChangeRequested) return hostFailure;

                if (versionOp.Status == EOperationStatus.Succeed)
                {
                    string remoteVersion = versionOp.PackageVersion;
                    _detectedHostVersion = remoteVersion ?? string.Empty;
                    SetStatus("正在加载资源清单...");
                    var manifestOp = _package.UpdatePackageManifestAsync(
                        remoteVersion,
                        REMOTE_MANIFEST_TIMEOUT_SECONDS);
                    await manifestOp.WaitAsync();
                    if (_serverChangeRequested) return hostFailure;

                    if (manifestOp.Status == EOperationStatus.Succeed)
                    {
                        RefreshVersion();
                        BootOperationResult downloadResult = await DownloadResources(remoteVersion);
                        if (downloadResult.Succeeded)
                            return downloadResult;
                        hostFailure = downloadResult;
                    }
                    else
                    {
                        hostFailure = Fail(BootFailureCode.PackageManifestUpdateFailed,
                            BootFailureStage.ResourceManifest, BootRetryScope.ResourceInitialization,
                            ("resourceVersion", remoteVersion), ("operationError", manifestOp.Error));
                    }
                }
                else
                {
                    hostFailure = Fail(BootFailureCode.PackageVersionRequestFailed,
                        BootFailureStage.ResourceVersion, BootRetryScope.ResourceInitialization,
                        ("operationError", versionOp.Error));
                }
            }

            if (_serverChangeRequested) return hostFailure;
            if (hasCachedVersion)
            {
                SetStatus($"在线资源不可用，正在切换到缓存版本 {lastSuccessVersion}...");
                BootOperationResult cachedResult = await FallbackToCachedVersion(lastSuccessVersion);
                if (cachedResult.Succeeded)
                    return cachedResult;
            }

            if (_serverChangeRequested) return hostFailure;
            SetStatus("在线资源不可用，正在切换到内置资源...");
            BootOperationResult builtinResult = await FallbackToBuiltin();
            return ResolveHostFallbackResult(builtinResult, hostFailure);
        }

        /// <summary>完整内置资源成功时继续启动；本地无法启动时恢复本轮在线失败，供玩家修复网络后重试。</summary>
        private BootOperationResult ResolveHostFallbackResult(
            BootOperationResult builtinResult, BootOperationResult hostFailure)
        {
            if (builtinResult.Succeeded)
                return builtinResult;

            Debug.LogWarning($"[ResourceBoot] 内置回退不可用：{builtinResult.Failure.Code}；保留在线失败：{hostFailure.Failure.Code}");
            _lastError = hostFailure.Failure.Code.ToString();
            return hostFailure;
        }

#if UNITY_INCLUDE_TESTS
        private async UniTask<BootOperationResult> InitializeFromTestRawOutcome()
        {
            string outcome = await TestRawInitializationOutcome();
            switch (outcome)
            {
                case "installer-exit":
                    InstallerExitRequested = true;
                    return Success();
                case "success":
                    _isReady = true;
                    return Success();
                case "resource-download-failed":
                    return Fail(
                        BootFailureCode.ResourceDownloadFailed,
                        BootFailureStage.ResourceDownload,
                        BootRetryScope.ResourceInitialization,
                        ("resourcePackage", PACKAGE_NAME));
                case "host-final-builtin-invalid":
                    return Fail(
                        BootFailureCode.BuiltinResourceInvalid,
                        BootFailureStage.ResourceInitialization,
                        BootRetryScope.None,
                        ("resourcePackage", PACKAGE_NAME),
                        ("operationStatus", "Failed"));
                default:
                    return Fail(
                        BootFailureCode.HostInitializationFailed,
                        BootFailureStage.ResourceInitialization,
                        BootRetryScope.ResourceInitialization,
                        ("resourcePackage", PACKAGE_NAME),
                        ("operationStatus", outcome ?? "null"));
            }
        }
#endif

        private async UniTask<BootOperationResult> DownloadResources(string version)
        {
            SetStatus("正在检查资源...");

            _downloader = CreateStartupResourceDownloader();
            if (_downloader.TotalDownloadCount == 0)
            {
                BootOperationResult availabilityResult = ValidateStartupResources();
                if (!availabilityResult.Succeeded)
                    return availabilityResult;

                _pendingHostVersion = version;
                _pendingResourceSource = BootResourceSource.HostLatest;
                SetStatus("资源已是最新版本");
                RefreshVersion();
                return Success();
            }

            _updateView.RenderDownloading("正在下载资源...");
            _updateView.SetProgress(0f);
            _updateView.SetSize(0, (ulong)_downloader.TotalDownloadBytes);
            _updateView.SetFileCount(0, _downloader.TotalDownloadCount);
            _updateView.SetDownloadEstimate(0, null);
            _lastDownloadTime = Time.realtimeSinceStartup;
            _lastDownloadBytes = 0;
            _smoothedDownloadBytesPerSecond = 0d;

            _downloader.DownloadUpdateCallback = OnDownloadUpdate;
            _downloader.DownloadErrorCallback = OnDownloadError;
            _downloader.BeginDownload();

            await _downloader.WaitAsync();

            if (_downloader.Status != EOperationStatus.Succeed)
            {
                return Fail(
                    BootFailureCode.ResourceDownloadFailed,
                    BootFailureStage.ResourceDownload,
                    BootRetryScope.ResourceInitialization,
                    ("operationStatus", _downloader.Status.ToString()),
                    ("resourceVersion", version),
                    ("operationError", _downloader.Error));
            }

            BootOperationResult validationResult = ValidateStartupResources();
            if (!validationResult.Succeeded)
                return validationResult;

            _pendingHostVersion = version;
            _pendingResourceSource = BootResourceSource.HostLatest;
            _updateView.RenderBusy("资源下载完成");
            RefreshVersion();
            return Success();
        }

        private async UniTask<BootOperationResult> FallbackToCachedVersion(string version)
        {
            await ResetPackageAsync();

            var remoteServices = new RemoteServices(_resourceServer);
            var parameters = new HostPlayModeParameters();
            parameters.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
            parameters.CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices);

            var operation = _package.InitializeAsync(parameters);
            await operation.WaitAsync();
            if (operation.Status != EOperationStatus.Succeed)
                return Fail(BootFailureCode.HostInitializationFailed,
                    BootFailureStage.ResourceInitialization, BootRetryScope.ResourceInitialization,
                    ("resourceStage", "缓存初始化"), ("operationError", operation.Error));

            var manifestOp = _package.UpdatePackageManifestAsync(
                version,
                REMOTE_MANIFEST_TIMEOUT_SECONDS);
            await manifestOp.WaitAsync();
            if (manifestOp.Status != EOperationStatus.Succeed)
                return Fail(BootFailureCode.PackageManifestUpdateFailed,
                    BootFailureStage.ResourceManifest, BootRetryScope.ResourceInitialization,
                    ("resourceStage", "缓存清单"), ("operationError", manifestOp.Error));

            _downloader = CreateStartupResourceDownloader();
            if (_downloader.TotalDownloadCount > 0)
            {
                SetStatus($"缓存版本不完整，缺少 {_downloader.TotalDownloadCount} 个文件");
                return Fail(BootFailureCode.ResourceDownloadFailed,
                    BootFailureStage.ResourceDownload, BootRetryScope.ResourceInitialization,
                    ("resourceStage", "缓存资源"), ("missingFileCount", _downloader.TotalDownloadCount.ToString()));
            }

            BootOperationResult validationResult = ValidateStartupResources();
            if (!validationResult.Succeeded)
                return validationResult;

            _pendingHostVersion = version;
            _pendingResourceSource = BootResourceSource.HostCached;
            _detectedHostVersion = _detectedHostVersion ?? string.Empty;
            SetStatus($"已使用缓存版本 {version}");
            RefreshVersion();
            return Success();
        }

        private async UniTask<BootOperationResult> FallbackToBuiltin()
        {
            _actualPlayMode = ResourcePlayMode.OfflinePlayMode;
            await ResetPackageAsync();

            var parameters = new OfflinePlayModeParameters();
            parameters.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();

            var operation = _package.InitializeAsync(parameters);
            await operation.WaitAsync();

            if (operation.Status != EOperationStatus.Succeed)
            {
                return Fail(
                    BootFailureCode.BuiltinResourceInvalid,
                    BootFailureStage.ResourceInitialization,
                    BootRetryScope.None,
                    ("operationStatus", operation.Status.ToString()),
                    ("resourcePackage", PACKAGE_NAME));
            }

            BootOperationResult manifestResult = await EnsurePackageManifestAsync("内置资源");
            if (!manifestResult.Succeeded)
                return manifestResult;

            _pendingResourceSource = BootResourceSource.BuiltinFallback;
            SetStatus("已使用内置资源");
            RefreshVersion();
            return Success();
        }

        public void CommitSuccessfulHostVersion()
        {
            if (string.IsNullOrEmpty(_pendingHostVersion))
                return;

            PlayerPrefs.SetString(LAST_SUCCESSFUL_HOST_VERSION_KEY, _pendingHostVersion);
            PlayerPrefs.Save();
            _pendingHostVersion = null;
        }

        private BootOperationResult ValidateRequiredLocations()
        {
            BootOperationResult availabilityResult = ValidateStartupResources();
            if (!availabilityResult.Succeeded)
                return availabilityResult;

            if (_actualPlayMode != ResourcePlayMode.EditorSimulateMode)
                return ValidateAotDlls();

            return Success();
        }

        /// <summary>
        /// 在任何加载前确认必需地址、启动标签资源及其依赖能由当前文件系统提供。
        /// 地址存在仅表示清单记录存在；离线空目录的查询异常必须转换为结构化失败。
        /// 可选资源不参与启动完整性判定。
        /// </summary>
        private BootOperationResult ValidateStartupResources()
        {
            foreach (var address in GetRequiredAddresses())
            {
                if (!_package.CheckLocationValid(address))
                {
                    return Fail(
                        address.Equals("Game.dll.bytes", StringComparison.OrdinalIgnoreCase)
                            ? BootFailureCode.GameDllMissing
                            : BootFailureCode.RequiredResourceMissing,
                        BootFailureStage.ResourceValidation,
                        BootRetryScope.None,
                        ("resourceAddress", address));
                }
            }

            if (_actualPlayMode == ResourcePlayMode.EditorSimulateMode)
                return Success();

            var checkedPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string address in GetRequiredAddresses())
            {
                BootOperationResult result = ValidateAvailableAsset(_package.GetAssetInfo(address), checkedPaths);
                if (!result.Succeeded)
                    return result;
            }

            foreach (AssetInfo asset in _package.GetAssetInfos(StartupResourceTags))
            {
                BootOperationResult result = ValidateAvailableAsset(asset, checkedPaths);
                if (!result.Succeeded)
                    return result;
            }

            return Success();
        }

        /// <summary>只查询资源归属及依赖，不创建加载句柄；缺失归属与待下载文件都不能视为启动就绪。</summary>
        private BootOperationResult ValidateAvailableAsset(AssetInfo asset, HashSet<string> checkedPaths)
        {
            if (!checkedPaths.Add(asset.AssetPath))
                return Success();

            string exceptionType = string.Empty;
            try
            {
                if (!_package.IsNeedDownloadFromRemote(asset))
                    return Success();
            }
            catch (Exception exception)
            {
                // YooAsset 离线模式在 Bundle 没有文件系统归属时直接抛异常，不能继续尝试同步加载。
                exceptionType = exception.GetType().FullName;
            }

            return Fail(
                _actualPlayMode == ResourcePlayMode.OfflinePlayMode
                    ? BootFailureCode.BuiltinResourceInvalid
                    : BootFailureCode.ResourceDownloadFailed,
                BootFailureStage.ResourceValidation,
                _actualPlayMode == ResourcePlayMode.OfflinePlayMode
                    ? BootRetryScope.None
                    : BootRetryScope.ResourceInitialization,
                ("resourceAddress", asset.Address), ("exceptionType", exceptionType));
        }

        private BootOperationResult FailAotValidation(BootFailureCode code, string dllName)
        {
            return Fail(code, BootFailureStage.AotList, BootRetryScope.None, ("dllName", dllName));
        }

        private BootOperationResult ValidateAotDlls()
        {
            const string aotListAddress = "HybridCLRAssemblyList.bytes";
            if (!_package.CheckLocationValid(aotListAddress))
                return FailAotValidation(BootFailureCode.AotListInvalid, aotListAddress);

            var handle = _package.LoadAssetSync<TextAsset>(aotListAddress);
            try
            {
                if (handle.Status != EOperationStatus.Succeed)
                    return FailAotValidation(BootFailureCode.AotListInvalid, aotListAddress);

                var textAsset = handle.AssetObject as TextAsset;
                if (textAsset == null)
                    return FailAotValidation(BootFailureCode.AotListInvalid, aotListAddress);

                var lines = textAsset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int validCount = 0;

                foreach (var line in lines)
                {
                    string dllName = line.Trim();
                    if (string.IsNullOrEmpty(dllName) || dllName.StartsWith("//"))
                        continue;

                    if (dllName.Equals("Game.dll", StringComparison.OrdinalIgnoreCase) ||
                        dllName.Equals("Game", StringComparison.OrdinalIgnoreCase))
                        return FailAotValidation(BootFailureCode.AotListInvalid, "Game.dll");

                    if (!dllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        dllName += ".dll";

                    if (!seen.Add(dllName))
                        return FailAotValidation(BootFailureCode.AotListInvalid, dllName);

                    string dllAddress = ToDllBytesAddress(dllName);
                    if (!_package.CheckLocationValid(dllAddress))
                        return Fail(BootFailureCode.AotDllMissing, BootFailureStage.AotList, BootRetryScope.None, ("dllName", dllName));

                    validCount++;
                }

                if (validCount == 0)
                    return FailAotValidation(BootFailureCode.AotListInvalid, aotListAddress);
                return Success();
            }
            finally
            {
                handle.Release();
            }
        }

        /// <summary>
        /// 枚举街机启动前必须存在且已就绪的核心地址；已移除的业务界面不再作为启动依赖。
        /// 缺地址或缺 Bundle 时由启动校验返回结构化失败，不延迟到游戏界面加载时报错。
        /// </summary>
        private IEnumerable<string> GetRequiredAddresses()
        {
            yield return "Scenes/Main";
            yield return "tbitem.bytes";
            yield return "Game.dll.bytes";
            yield return "HybridCLRAssemblyList.bytes";
        }

        private static string ToDllBytesAddress(string dllName)
        {
            if (dllName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                return dllName;
            return $"{dllName}.bytes";
        }

        private void OnDownloadUpdate(DownloadUpdateData data)
        {
            float progress = data.Progress;
            _updateView.SetProgress(progress);
            _updateView.SetFileCount(data.CurrentDownloadCount, data.TotalDownloadCount);
            _updateView.SetSize((ulong)data.CurrentDownloadBytes, (ulong)data.TotalDownloadBytes);

            float currentTime = Time.realtimeSinceStartup;
            float deltaTime = currentTime - _lastDownloadTime;
            if (deltaTime >= 0.5f)
            {
                long bytesDiff = data.CurrentDownloadBytes - _lastDownloadBytes;
                long bytesPerSecond = deltaTime > 0 ? (long)(bytesDiff / deltaTime) : 0;
                if (bytesPerSecond > 0)
                {
                    _smoothedDownloadBytesPerSecond = _smoothedDownloadBytesPerSecond <= 0d
                        ? bytesPerSecond
                        : _smoothedDownloadBytesPerSecond +
                          (bytesPerSecond - _smoothedDownloadBytesPerSecond) * DOWNLOAD_SPEED_SMOOTHING;
                }

                long remainingBytes = Math.Max(0L, data.TotalDownloadBytes - data.CurrentDownloadBytes);
                double? remainingSeconds = _smoothedDownloadBytesPerSecond > 0d
                    ? remainingBytes / _smoothedDownloadBytesPerSecond
                    : (double?)null;
                _updateView.SetDownloadEstimate(
                    (ulong)Math.Max(0d, _smoothedDownloadBytesPerSecond),
                    remainingSeconds);

                _lastDownloadBytes = data.CurrentDownloadBytes;
                _lastDownloadTime = currentTime;
            }
        }

        /// <summary>
        /// 创建启动阶段下载器；最新版本下载与缓存完整性检查必须使用同一组标签。
        /// </summary>
        private ResourceDownloaderOperation CreateStartupResourceDownloader()
        {
            return _package.CreateResourceDownloader(StartupResourceTags, MAX_DOWNLOAD_COUNT, 3);
        }

        private void OnDownloadError(DownloadErrorData data)
        {
            Debug.LogError($"[ResourceBoot] 下载错误：{data.FileName}，底层错误：{data.ErrorInfo}");
        }

        private void SetStatus(string status)
        {
            _updateView.SetStatus(status);
        }

        public void Configure(HotUpdateView updateView)
        {
            _updateView = updateView != null
                ? updateView
                : throw new ArgumentNullException(nameof(updateView),
                    "ResourceBoot 必须配置 HotUpdateView 后才能启动。");
            if (_apkUpdate == null) _apkUpdate = new ApkUpdateFlow(_updateView);
            if (_windowsUpdate == null) _windowsUpdate = new WindowsUpdateFlow(_updateView);
        }

        private class RemoteServices : IRemoteServices
        {
            private readonly string _remoteURL;

            public RemoteServices(string remoteURL)
            {
                _remoteURL = remoteURL;
            }

            public string GetRemoteMainURL(string fileName)
            {
                return $"{_remoteURL}/{fileName}";
            }

            public string GetRemoteFallbackURL(string fileName)
            {
                return $"{_remoteURL}/{fileName}";
            }
        }
    }
}
