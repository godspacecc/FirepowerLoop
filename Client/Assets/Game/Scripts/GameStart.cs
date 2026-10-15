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
                UIHelper.ShowUI<MobileJoystickUICtrl>();
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

        private void OnApplicationQuit()
        {
            try
            {
                var architecture = Game.Interface;
                try
                {
                    if (architecture.GetModel<SaveModel>().CurrentDomain == OfflineSaveDomain.Base &&
                        !architecture.GetSystem<PlayerBasePoseSystem>().TryCaptureCurrentBasePoseForExit(out _, out var error))
                    {
                        Log.Error("[GameStart] 退出前基地姿态捕获失败：" + error);
                    }
                }
                catch (System.Exception exception)
                {
                    Log.Error("[GameStart] 退出前基地姿态捕获异常：" + exception);
                }

                try
                {
                    var save = architecture.GetSystem<SaveSystem>();
                    // 正常退出保存精确时刻，包括最近一次自动保存后不足一个游戏分钟的变化。
                    if (architecture.GetModel<SaveModel>().CurrentDomain == OfflineSaveDomain.Base)
                        save.MarkDirty("base day-night before application quit");
                    if (!save.Flush(out var error))
                    {
                        Log.Error("[GameStart] 退出前存档刷新失败：" + error);
                    }
                }
                catch (System.Exception exception)
                {
                    Log.Error("[GameStart] 退出前存档刷新异常：" + exception);
                }

                try
                {
                    architecture.Deinit();
                }
                catch (System.Exception exception)
                {
                    Log.Error("[GameStart] 退出架构反初始化异常：" + exception);
                }
            }
            finally
            {
                LifeCycle.Quit();
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            LifeCycle.ApplicationPause(pauseStatus);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            LifeCycle.ApplicationFocus(hasFocus);
        }

        private static async UniTaskVoid ReturnPendingToBaseAsync()
        {
            await ExecutePendingReturnDecisionAsync(
                async data =>
                {
                    var start = UIHelper.ShowUI<StartUICtrl>();
                    return start != null && await start.EnterBaseFromStartupAsync(data);
                },
                () =>
                {
                    UIHelper.ShowUI<PlayerConditionHudUICtrl>();
                    UIHelper.ShowUI<GameplayGuideHudUICtrl>();
                },
                ShowPendingReturnRetry);
        }

        /// <summary>启动页拒绝继续副本的受控结算入口。</summary>
        internal static UniTask<ExpeditionFlowResult> SettlePendingForStartupAsync()
        {
            return Game.Interface.GetSystem<ExpeditionFlowSystem>().SettlePendingWithoutLoadingAsync();
        }

        /// <summary>“返回基地”按钮的生产决策：结算、重读基地和受控进入依次完成后才开放运行态 HUD。</summary>
        internal static async UniTask<bool> ExecutePendingReturnDecisionAsync(
            System.Func<BaseSaveData, UniTask<bool>> requestBaseEntry,
            System.Action requestBaseOpened,
            System.Action requestSettlementRetry)
        {
            var result = await SettlePendingForStartupAsync();
            if (!result.Success)
            {
                Log.Error($"[GameStart] 结算待恢复副本失败：{result.Error}");
                requestSettlementRetry?.Invoke();
                return false;
            }

            var baseRead = Game.Interface.GetSystem<SaveSystem>().PrepareBase();
            if (!baseRead.IsValid)
            {
                Log.Error($"[GameStart] 结算后读取基地失败：{baseRead.Error}");
                UIHelper.ShowUI<StartUICtrl>();
                return false;
            }

            if (requestBaseEntry == null || !await requestBaseEntry(baseRead.Data))
            {
                Log.Error("[GameStart] 结算后进入基地失败：受控基地进入未完成");
                return false;
            }

            requestBaseOpened?.Invoke();
            return true;
        }

        private static async UniTaskVoid EnterBaseWithCorruptedExpeditionAsync()
        {
            await ExecuteCorruptedExpeditionBaseDecisionAsync(
                async data =>
                {
                    var start = UIHelper.ShowUI<StartUICtrl>();
                    return start != null && await start.EnterBaseFromStartupAsync(data);
                },
                () =>
                {
                    UIHelper.ShowUI<PlayerConditionHudUICtrl>();
                    UIHelper.ShowUI<GameplayGuideHudUICtrl>();
                },
                ShowCorruptedExpeditionBaseRetry);
        }

        /// <summary>损坏副本“进入基地”按钮的生产决策：准备、受控进入、成功显示与错误重试只在此处编排。</summary>
        internal static async UniTask<bool> ExecuteCorruptedExpeditionBaseDecisionAsync(
            System.Func<BaseSaveData, UniTask<bool>> requestBaseEntry,
            System.Action requestBaseOpened,
            System.Action requestErrorRetry)
        {
            var baseRead = Game.Interface.GetSystem<SaveSystem>().PrepareBase();
            if (!baseRead.IsValid)
            {
                Log.Error($"[GameStart] 损坏副本进入基地失败：{baseRead.Error}");
                requestErrorRetry?.Invoke();
                return false;
            }

            if (requestBaseEntry == null || !await requestBaseEntry(baseRead.Data))
            {
                Log.Error("[GameStart] 损坏副本进入基地失败：受控基地进入未完成");
                requestErrorRetry?.Invoke();
                return false;
            }

            requestBaseOpened?.Invoke();
            return true;
        }

        private static void ShowPendingReturnRetry()
        {
            Notify.ShowMessage(Texts.OfflineExpeditionPendingTitle, Texts.SaveErrorLoadFailed,
                new List<MessageButtonConfig>
                {
                    new MessageButtonConfig { ButtonText = Texts.CommonConfirm, Role = MessageButtonRole.Confirm, OnClick = () => ReturnPendingToBaseAsync().Forget() }
                });
        }

        private static void ShowCorruptedExpeditionBaseRetry()
        {
            Notify.ShowMessage(Texts.OfflineExpeditionPendingTitle, Texts.SaveErrorLoadFailed,
                new List<MessageButtonConfig>
                {
                    new MessageButtonConfig { ButtonText = Texts.CommonConfirm, Role = MessageButtonRole.Confirm, OnClick = () => EnterBaseWithCorruptedExpeditionAsync().Forget() }
                });
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
