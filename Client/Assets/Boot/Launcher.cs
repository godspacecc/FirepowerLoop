using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Boot.UI;
using YooAsset;

namespace Boot
{
    public enum BootMainStartupStage
    {
        InitializingGame,
        LoadingGameConfig,
        PreparingStartUI
    }

    /// <summary>
    /// Boot 启动链入口，负责把必需 UI、资源初始化和热更新 DLL 加载串接到主场景前。
    /// </summary>
    [RequireComponent(typeof(ResourceBoot))]
    public class Launcher : MonoBehaviour
    {
        [Header("界面引用")]
        [SerializeField] private HotUpdateView _updateView;
        [SerializeField] private Canvas _updateCanvas;

        private static Launcher current;
        private ResourceBoot _resourceBoot;
        /// <summary>仅在真实启动时创建，跨主场景加载保留；就绪或销毁时释放 Unity 日志订阅。</summary>
        private BootStartupLogStore _startupLog;
        private bool _isStarting;
        private bool _viewEventsBound;
        private bool _destroying;
        private BootFailure _currentFailure;
        /// <summary>地址编辑只在程序集加载前开放；保存后等待旧资源操作结束，不并发重建包。</summary>
        private bool _resourceServerAvailable = true;
        private bool _resourceServerEditing;
        private bool _resourceServerRestartRequested;
        private UniTaskCompletionSource _resourceServerEditGate;

        public bool CanEditResourceServer => !ClientReleasePolicy.OfflineSinglePlayer && _resourceServerAvailable && !_resourceServerRestartRequested &&
            (!_resourceBoot || !_resourceBoot.IsApkUpdateActive);
        public event Action<bool> ResourceServerAvailabilityChanged;
        private StartupTerminalState _terminalState = StartupTerminalState.Running;

#if UNITY_INCLUDE_TESTS
        public static Func<ResourcePackage> TestPackageResolver { get; set; }
        public static Func<UniTask<bool>> TestMainSceneLoader { get; set; }
        public static Action<string> TestClipboardWriter { get; set; }
        public static Func<BootFailure, bool> TestBootErrorLogWriter { get; set; }
        public static Action TestQuitPort { get; set; }

        public static void ResetTestPorts()
        {
            TestPackageResolver = null;
            TestMainSceneLoader = null;
            TestClipboardWriter = null;
            TestBootErrorLogWriter = null;
            TestQuitPort = null;
            DiagnosticReportSender.EndBoot();
            current = null;
        }

        public void InitializeForTests()
        {
            _updateView.InitializeForTests();
            InitializeOwner();
        }

        public UniTask StartForTestsAsync()
        {
            return RunStartupAsync();
        }
#endif

        private enum StartupTerminalState
        {
            Running,
            InstallerExitPending,
            ReadyPendingRelease,
            Released,
            Failed
        }

        public static bool TryReportMainStage(BootMainStartupStage stage)
        {
            if (!Enum.IsDefined(typeof(BootMainStartupStage), stage))
            {
                return false;
            }

            var owner = current;
            if (!owner || owner._terminalState != StartupTerminalState.Running)
            {
                return false;
            }

            owner.SetMainStage(stage);
            return true;
        }

        public static bool TryReportMainReady()
        {
            var owner = current;
            if (!owner || owner._terminalState != StartupTerminalState.Running)
            {
                return false;
            }

            owner._terminalState = StartupTerminalState.ReadyPendingRelease;
            owner._startupLog?.RecordStage("Main:Ready");
            owner._startupLog?.Dispose();
            DiagnosticReportSender.MainReady();
            owner.ReleaseAfterNextFrameAsync().Forget();
            return true;
        }

        public static bool TryReportMainFailure()
        {
            return TryReportMainFailure(null);
        }

        public static bool TryReportMainFailure(IReadOnlyDictionary<string, string> diagnosticFields)
        {
            var owner = current;
            if (!owner || owner._terminalState != StartupTerminalState.Running)
            {
                return false;
            }

            owner._terminalState = StartupTerminalState.Failed;
            owner._updateView.gameObject.SetActive(true);
            owner.ShowFailure(BootFailure.Create(
                BootFailureCode.MainInitializationFailed,
                BootFailureStage.MainInitialization,
                BootRetryScope.None,
                diagnosticFields));
            return true;
        }

        private void Awake()
        {
            if (!current)
            {
                _startupLog = new BootStartupLogStore(Application.persistentDataPath, Application.version,
                    Application.platform.ToString());
                _startupLog.RecordStage("Boot:Awake; UnityVersion=" + Application.unityVersion);
                UpdatePackageCache.CleanCurrentInstallation();
            }
            InitializeOwner();
        }

        /// <summary>由 Boot 场景加载器报告真实进度；没有当前启动所有者时忽略，不改变启动状态。</summary>
        public static void RecordStartupStage(string stage)
        {
            if (current) current._startupLog?.RecordStage(stage);
        }

        private void InitializeOwner()
        {
            ValidateRequiredReferences();
            if (current && current != this)
            {
                Debug.LogWarning(
                    $"[Launcher] Boot 覆盖层所有者重复：当前={GetHierarchyPath(current.transform)}，拒绝覆盖={GetHierarchyPath(transform)}");
                if (_updateCanvas)
                {
                    _updateCanvas.gameObject.SetActive(false);
                    DestroyOwnedObject(_updateCanvas.gameObject);
                }

                enabled = false;
                return;
            }

            current = this;
            DiagnosticReportSender.BeginBoot();
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
                DontDestroyOnLoad(_updateCanvas.gameObject);
            }

            _resourceBoot = GetComponent<ResourceBoot>();
            if (!_resourceBoot)
            {
                throw MissingReference(nameof(_resourceBoot));
            }
            _resourceBoot.ApkUpdateActivityChanged -= OnApkUpdateActivityChanged;
            _resourceBoot.ApkUpdateActivityChanged += OnApkUpdateActivityChanged;
        }

        private void Start()
        {
            RunStartupAsync().Forget();
        }

        private void Update()
        {
            DiagnosticReportSender.TickBoot();
        }

        private void OnDestroy()
        {
            _startupLog?.RecordStage("Boot:Destroyed");
            _startupLog?.Dispose();
            if (_resourceBoot) _resourceBoot.ApkUpdateActivityChanged -= OnApkUpdateActivityChanged;
            _destroying = true;
            _resourceServerEditGate?.TrySetCanceled();
            if (current != this)
            {
                return;
            }

            current = null;
            DiagnosticReportSender.EndBoot();
            if (_viewEventsBound)
            {
                _updateView.OnRetryClicked -= OnRetryClicked;
                _updateView.OnCopyDetailsClicked -= OnCopyDetailsClicked;
                _updateView.OnQuitClicked -= OnQuitClicked;
                _viewEventsBound = false;
            }
        }

        private void OnApkUpdateActivityChanged() => ResourceServerAvailabilityChanged?.Invoke(CanEditResourceServer);

        private async UniTask RunStartupAsync()
        {
            if (_isStarting)
            {
                Debug.LogWarning("[Launcher] 启动流程正在执行，请勿重复触发。");
                return;
            }

            _isStarting = true;

            try
            {
                _currentFailure = null;
                _startupLog?.RecordStage("Resources:Started");
                _updateView.gameObject.SetActive(true);
                _updateView.RenderBusy("正在初始化资源...");

                _resourceBoot.Configure(_updateView);
                BindViewEvents();

                bool restartResources = _resourceServerRestartRequested;
                _resourceServerRestartRequested = false;
                BootOperationResult resourceResult;
                do
                {
                    _resourceServerAvailable = true;
                    ResourceServerAvailabilityChanged?.Invoke(CanEditResourceServer);
                    resourceResult = restartResources
                        ? await _resourceBoot.ReinitializeForRetryAsync()
                        : await _resourceBoot.InitializeAsync();
                    if (!this || _destroying) return;
                    // 编辑期间不消费旧结果；保存只提交下一轮初始化意图。
                    if (_resourceServerEditing)
                        await _resourceServerEditGate.Task;
                    if (!this || _destroying) return;
                    restartResources = _resourceServerRestartRequested;
                    _resourceServerRestartRequested = false;
                } while (restartResources);
                if (_resourceBoot.InstallerExitRequested)
                {
                    _terminalState = StartupTerminalState.InstallerExitPending;
                    _resourceServerAvailable = false;
                    ResourceServerAvailabilityChanged?.Invoke(false);
                    _updateView.RenderBusy("正在退出游戏并打开安装器……");
                    OnQuitClicked();
                    return;
                }
                if (!RequireResult(resourceResult, BootFailureStage.ResourceInitialization, out resourceResult))
                {
                    return;
                }

                if (!resourceResult.Succeeded)
                {
                    ShowFailure(resourceResult.Failure);
                    return;
                }

                _resourceServerAvailable = false;
                ResourceServerAvailabilityChanged?.Invoke(false);
                var package = ResolveDefaultPackage();
                _startupLog?.RecordStage("Resources:Completed; ResourceVersion=" + package?.GetPackageVersion());
                _startupLog?.RecordStage("Assemblies:Started");
                BootOperationResult codeResult = await CodeLoader.LoadAsync(package);
                if (!RequireResult(codeResult, BootFailureStage.GameAssembly, out codeResult))
                {
                    return;
                }

                if (!codeResult.Succeeded)
                {
                    ShowFailure(codeResult.Failure);
                    return;
                }

                _resourceBoot.CommitSuccessfulHostVersion();
                _startupLog?.RecordStage("Assemblies:Completed");
                _updateView.RenderNormal("正在进入游戏……");

                bool sceneLoaded = await GetMainSceneLoader()();
                _startupLog?.RecordStage("MainScene:Result=" + sceneLoaded);
                if (!sceneLoaded)
                {
                    ShowFailure(BootFailure.Create(
                        BootFailureCode.MainSceneLoadFailed,
                        BootFailureStage.MainScene,
                        BootRetryScope.MainScene,
                        DiagnosticFields(("sceneAddress", "Scenes/Main"))));
                    return;
                }
            }
            catch (OperationCanceledException) when (!this || _destroying)
            {
                // 场景所有者销毁后，地址编辑等待不得继续访问已销毁的 UI。
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ShowFailure(BootFailure.Unexpected(e, BootFailureStage.Unexpected));
            }
            finally
            {
                _isStarting = false;
            }
        }

        private async UniTask RetryMainSceneAsync()
        {
            if (_isStarting)
            {
                return;
            }

            _isStarting = true;
            _currentFailure = null;
            _startupLog?.RecordStage("MainScene:Retry");
            _updateView.RenderBusy("正在重新进入游戏……");

            try
            {
                bool sceneLoaded = await GetMainSceneLoader()();
                if (!sceneLoaded)
                {
                    ShowFailure(BootFailure.Create(
                        BootFailureCode.MainSceneLoadFailed,
                        BootFailureStage.MainScene,
                        BootRetryScope.MainScene,
                        DiagnosticFields(("sceneAddress", "Scenes/Main"))));
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ShowFailure(BootFailure.Unexpected(e, BootFailureStage.MainScene));
            }
            finally
            {
                _isStarting = false;
            }
        }

        private void ShowFailure(BootFailure failure)
        {
            _currentFailure = failure ?? BootFailure.Create(
                BootFailureCode.UnexpectedException,
                BootFailureStage.Unexpected);
            _startupLog?.RecordStage("Startup:Failed\n" + _currentFailure.CopyDetails);
            PersistBootFailure(_currentFailure);
            DiagnosticReportSender.RecordBoot(_currentFailure);
            Debug.LogWarning($"[Launcher] 启动失败：{_currentFailure.Code}");
            _updateView.gameObject.SetActive(true);
            _updateView.RenderFailure(_currentFailure);
        }

        /// <summary>在显示失败页前保存最近一次支持信息；持久化失败不得改变原失败语义。</summary>
        private static void PersistBootFailure(BootFailure failure)
        {
#if UNITY_INCLUDE_TESTS
            if (TestBootErrorLogWriter != null)
            {
                TestBootErrorLogWriter(failure);
                return;
            }
#endif

            if (!BootErrorLogStore.TryWrite(failure, out _, out string diagnostic))
            {
                Debug.LogWarning($"[Launcher] Boot 错误日志写入失败：{diagnostic ?? "unknown"}");
            }
        }

        public void OnRetryClicked()
        {
            if (_isStarting || _resourceServerEditing || _currentFailure == null)
            {
                return;
            }

            switch (_currentFailure.RetryScope)
            {
                case BootRetryScope.ResourceInitialization:
                    RunStartupAsync().Forget();
                    break;
                case BootRetryScope.MainScene:
                    RetryMainSceneAsync().Forget();
                    break;
            }
        }

        /// <summary>地址输入打开时阻止资源结果进入程序集阶段；重复打开或晚于资源阶段返回 false。</summary>
        public bool TryBeginResourceServerEdit()
        {
            if (!CanEditResourceServer || _resourceServerEditing)
                return false;
            _resourceServerEditing = true;
            _resourceServerEditGate = new UniTaskCompletionSource();
            return true;
        }

        /// <summary>关闭输入后恢复旧流程，或丢弃旧结果并串行重新初始化；仅接受当前编辑会话一次提交。</summary>
        public void FinishResourceServerEdit(bool restart)
        {
            if (!_resourceServerEditing)
                return;
            _resourceServerEditing = false;
            if (restart)
            {
                _resourceServerRestartRequested = true;
                _resourceBoot.RequestServerChange();
                _updateView.RenderBusy("地址已保存，正在重新连接资源服务器...");
                ResourceServerAvailabilityChanged?.Invoke(false);
            }
            _resourceServerEditGate.TrySetResult();
            if (restart && !_isStarting)
                RunStartupAsync().Forget();
        }

        private void OnCopyDetailsClicked()
        {
            if (_currentFailure == null)
            {
                return;
            }

#if UNITY_INCLUDE_TESTS
            if (TestClipboardWriter != null)
            {
                TestClipboardWriter(_currentFailure.CopyDetails);
            }
            else
#endif
            {
                GUIUtility.systemCopyBuffer = _currentFailure.CopyDetails;
            }

            _updateView.ShowCopiedNotice();
        }

        private void OnQuitClicked()
        {
#if UNITY_INCLUDE_TESTS
            if (TestQuitPort != null)
            {
                TestQuitPort();
                return;
            }
#endif

            Application.Quit();
        }

        private void BindViewEvents()
        {
            if (_viewEventsBound)
            {
                return;
            }

            _updateView.OnRetryClicked += OnRetryClicked;
            _updateView.OnCopyDetailsClicked += OnCopyDetailsClicked;
            _updateView.OnQuitClicked += OnQuitClicked;
            _viewEventsBound = true;
        }

        private bool RequireResult(
            BootOperationResult result,
            BootFailureStage nullStage,
            out BootOperationResult safeResult)
        {
            if (result != null)
            {
                safeResult = result;
                return true;
            }

            safeResult = BootOperationResult.Failed(BootFailure.Create(
                BootFailureCode.UnexpectedException,
                nullStage));
            ShowFailure(safeResult.Failure);
            return false;
        }

        private static Func<UniTask<bool>> GetMainSceneLoader()
        {
#if UNITY_INCLUDE_TESTS
            return TestMainSceneLoader ?? (() => ResourceSceneLoader.LoadMainAsync());
#else
            return () => ResourceSceneLoader.LoadMainAsync();
#endif
        }

        private static ResourcePackage ResolveDefaultPackage()
        {
#if UNITY_INCLUDE_TESTS
            if (TestPackageResolver != null)
            {
                return TestPackageResolver();
            }
#endif

            return YooAssets.TryGetPackage("DefaultPackage");
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

        private void ValidateRequiredReferences()
        {
            RequireAssigned(_updateView, nameof(_updateView));
            RequireAssigned(_updateCanvas, nameof(_updateCanvas));
            if (_updateCanvas.transform.parent)
            {
                throw MissingReference(nameof(_updateCanvas));
            }

            if (!_updateView.transform.IsChildOf(_updateCanvas.transform))
            {
                throw MissingReference(nameof(_updateView));
            }
        }

        private void RequireAssigned(UnityEngine.Object value, string fieldName)
        {
            if (value)
            {
                return;
            }

            throw MissingReference(fieldName);
        }

        private InvalidOperationException MissingReference(string fieldName)
        {
            return new InvalidOperationException(
                $"Boot 必需引用缺失：{GetHierarchyPath(transform)}，组件字段：{GetType().Name}.{fieldName}");
        }

        private static string GetHierarchyPath(Transform target)
        {
            string path = target.name;
            while (target.parent)
            {
                target = target.parent;
                path = $"{target.name}/{path}";
            }

            return path;
        }

        private void SetMainStage(BootMainStartupStage stage)
        {
            _startupLog?.RecordStage("Main:" + stage);
            switch (stage)
            {
                case BootMainStartupStage.InitializingGame:
                    _updateView.RenderNormal("正在初始化游戏……");
                    break;
                case BootMainStartupStage.LoadingGameConfig:
                    _updateView.RenderNormal("正在加载游戏配置……");
                    break;
                case BootMainStartupStage.PreparingStartUI:
                    _updateView.RenderNormal("正在准备开始界面……");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
            }
        }

        private async UniTaskVoid ReleaseAfterNextFrameAsync()
        {
            var owner = this;
            await UniTask.NextFrame();
            if (current != owner || _terminalState != StartupTerminalState.ReadyPendingRelease)
            {
                return;
            }

            _terminalState = StartupTerminalState.Released;
            if (_updateCanvas)
            {
                DestroyOwnedObject(_updateCanvas.gameObject);
            }

            if (this)
            {
                DestroyOwnedObject(gameObject);
            }
        }

        private static void DestroyOwnedObject(GameObject target)
        {
            if (!target)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
                return;
            }

            DestroyImmediate(target);
        }
    }
}
