using QFramework;
using Boot;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace Game
{
    /// <summary>
    /// Game 热更新程序集的主场景入口，负责在资源和配置就绪后触发设置加载与启动界面。
    /// </summary>
    public class GameStart : MonoBehaviour
    {
        private bool mainStartupFailed;

        private void Awake()
        {
            Launcher.TryReportMainStage(BootMainStartupStage.InitializingGame);
            try
            {
                _ = Game.Interface;
            }
            catch (System.Exception exception)
            {
                mainStartupFailed = true;
                ReportMainStartupFailure("Awake 架构初始化异常", exception);
            }
        }

        private void Start()
        {
            if (mainStartupFailed)
            {
                return;
            }

            try
            {
                var package = YooAssets.TryGetPackage("DefaultPackage");
                if (package == null)
                {
                    ReportMainStartupFailure("DefaultPackage 为空，游戏启动已停止。");
                    return;
                }

                ResourceLoader.Use(new YooAssetResourceLoader(package));
                LocalizationLanguages.ResetCache();
                Game.Interface.GetUtility<LocalizationUtility>().InitializeConfiguredTables();
                if (!BootResourceStartupState.TryGet(out var startupInfo))
                {
                    ReportMainStartupFailure("缺少 Boot 资源启动信息。");
                    return;
                }

                Game.Interface.GetSystem<ResourceUpdateSystem>()
                    .InitializeStartupInfo(startupInfo);
                MissingCharCollector.Init();
                Launcher.TryReportMainStage(BootMainStartupStage.LoadingGameConfig);
                if (!DataLoader.Init())
                {
                    ReportMainStartupFailure("DataLoader.Init 返回 false。");
                    return;
                }

                Game.Interface.GetSystem<SettingsSystem>().LoadAndApply();
                MobileRenderScaleRuntime.ApplyForCurrentPlatform();
                Launcher.TryReportMainStage(BootMainStartupStage.PreparingStartUI);
                UIHelper.ShowUI<TestCmdButtonUICtrl>();
                UIHelper.ShowUI<FloatUICtrl>();
                StartUICtrl startCtrl = UIHelper.ShowUI<StartUICtrl>();
                if (startCtrl != null && startCtrl.IsShow())
                {
                    Launcher.TryReportMainReady();
                    return;
                }

                ReportMainStartupFailure("StartUICtrl 未能显示。");
            }
            catch (System.Exception exception)
            {
                ReportMainStartupFailure("Start 初始化异常", exception);
            }
        }

        private void Update()
        {
            LifeCycle.Update();
        }

        private void FixedUpdate()
        {
            LifeCycle.FixedUpdate();
        }

        private void LateUpdate()
        {
            LifeCycle.LateUpdate();
        }

        /// <summary>街机安全节点在事务内落盘；退出时仅关闭本工程的架构和生命周期。</summary>
        private void OnApplicationQuit()
        {
            try { Game.Interface.Deinit(); }
            catch (System.Exception exception) { Log.Error("[GameStart] 退出清理失败：" + exception); }
            finally { LifeCycle.Quit(); }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            LifeCycle.ApplicationPause(pauseStatus);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            LifeCycle.ApplicationFocus(hasFocus);
        }

        private void ReportMainStartupFailure(string message, System.Exception exception = null)
        {
            mainStartupFailed = true;
            if (exception == null)
            {
                Log.Error($"[GameStart] 启动失败：{message}");
            }
            else
            {
                Log.Error($"[GameStart] 启动失败：{message}\n{exception}");
            }

            Launcher.TryReportMainFailure(BuildMainFailureDiagnostics(exception));
        }

        private static IReadOnlyDictionary<string, string> BuildMainFailureDiagnostics(System.Exception exception)
        {
            var fields = new Dictionary<string, string>();

            if (exception != null)
            {
                fields["exceptionType"] = exception.GetType().FullName;
                fields["hResult"] = exception.HResult.ToString();
            }

            return fields;
        }
    }
}
