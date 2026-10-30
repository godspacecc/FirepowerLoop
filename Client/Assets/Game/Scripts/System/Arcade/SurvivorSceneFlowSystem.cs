using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    /// <summary>幸存者入口、退出与界面协调；不调用闯关存档事务。</summary>
    public sealed class SurvivorSceneFlowSystem : AbstractSystem
    {
        /// <summary>串行化开局和退出，生命周期结束取消所有未完成入口。</summary>
        private bool busy;
        /// <summary>同一场地故障只展示一次；返回失败时可重新提供退出操作。</summary>
        private string shownBattleError;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        protected override void OnInit()
        {
            this.RegisterEvent<SurvivorChangedEvent>(_ => RefreshPresentation());
        }

        protected override void OnDeinit()
        {
            lifetime.Cancel();
            lifetime.Dispose();
        }

        /// <summary>全部配置先校验，地图加载后才创建新局，失败释放本次现场。</summary>
        public async UniTask<ArcadeSceneFlowResult> StartAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested || lifetime.IsCancellationRequested)
                return ArcadeSceneFlowResult.Fail("幸存者开局已取消");
            if (busy) return ArcadeSceneFlowResult.Fail("幸存者场景正在切换");
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetSurvivor(out _, out var error))
                return ArcadeSceneFlowResult.Fail(error);
            if (this.GetModel<SurvivorModel>().Snapshot.Active)
                return ArcadeSceneFlowResult.Fail("请先结束当前幸存者局");
            var system = this.GetSystem<SurvivorSystem>();
            if (!system.TryPrepare(out error)) return ArcadeSceneFlowResult.Fail(error);
            busy = true;
            shownBattleError = null;
            bool entered = false;
            LoadingUICtrl loading=null;
            int loadingRequest=0;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            try
            {
                // 同时覆盖菜单入口和结算后的再来一局；接管入口令牌，防止旧回调提前隐藏。
                loading=UIHelper.ShowUI<LoadingUICtrl>();
                loadingRequest=loading.BeginSurvivorEntryPreparation();
                var result = await this.GetSystem<ArcadeSceneFlowSystem>().EnterArenaMapAsync(linked.Token);
                if (!result.Success) return result;
                entered = true;
                linked.Token.ThrowIfCancellationRequested();
                if (!system.TryStart(out error)) throw new InvalidOperationException(error);
                // 场景句柄就绪并不等于农场资源就绪；错误仍属于这次入场事务。
                var view = this.GetUtility<IArcadeCombatViewPort>();
                float deadline = UnityEngine.Time.realtimeSinceStartup + 30f;
                while (!view.IsRoundLayoutReady)
                {
                    if (!string.IsNullOrEmpty(view.RoundLayoutLoadError))
                        throw new InvalidOperationException(view.RoundLayoutLoadError);
                    if (UnityEngine.Time.realtimeSinceStartup >= deadline)
                        throw new TimeoutException("草地战场准备超时，请重试");
                    await UniTask.NextFrame(linked.Token);
                }
                system.ConfirmBattlePrepared();
                if (!this.GetSystem<AppStateSystem>().TryTransition(AppState.Playing))
                    throw new InvalidOperationException("幸存者战场无法进入可操作状态");
                UIHelper.ShowUI<SurvivorHudUICtrl>();
                return ArcadeSceneFlowResult.Ok();
            }
            catch (Exception exception)
            {
                system.Stop();
                if (entered && !lifetime.IsCancellationRequested)
                    await this.GetSystem<ArcadeSceneFlowSystem>().ReturnToMainMenuAsync(showFailureDialog: false);
                return ArcadeSceneFlowResult.Fail(exception is OperationCanceledException ? "幸存者开局已取消" : exception.Message);
            }
            finally { loading?.EndEntryPreparation(loadingRequest);busy = false; }
        }

        /// <summary>保存结果成功后才释放现场；失败保留已结束结果供再次退出重试。</summary>
        public async UniTask<ArcadeSceneFlowResult> ReturnToMenuAsync(CancellationToken cancellationToken = default)
        {
            if (busy) return ArcadeSceneFlowResult.Fail("幸存者场景正在切换");
            var system = this.GetSystem<SurvivorSystem>();
            if (this.GetModel<SurvivorModel>().Snapshot.Active && !system.TryFinish(out var error))
                return ArcadeSceneFlowResult.Fail(error);
            busy = true;
            try
            {
                var result = await this.GetSystem<ArcadeSceneFlowSystem>().ReturnToMainMenuAsync(cancellationToken, showFailureDialog: false);
                if (result.Success)
                {
                    system.Stop();
                    UIHelper.HideUI<SurvivorUpgradeUICtrl>();
                    UIHelper.HideUI<SurvivorHudUICtrl>();
                    UIHelper.HideUI<SurvivorResultUICtrl>();
                }
                return result;
            }
            finally { busy = false; }
        }

        /// <summary>先完成原局保存和释放，再走同一开局事务，防止复用旧敌人及强化。</summary>
        public async UniTask<ArcadeSceneFlowResult> RestartAsync(CancellationToken cancellationToken = default)
        {
            var result = await ReturnToMenuAsync(cancellationToken);
            return result.Success ? await StartAsync(cancellationToken) : result;
        }

        /// <summary>只打开已采用的升级窗口，领域状态决定候选与暂停。</summary>
        private void RefreshPresentation()
        {
            var model = this.GetModel<SurvivorModel>();
            var error = this.GetSystem<SurvivorSystem>().LastError;
            if (model.Snapshot.Active && !model.Snapshot.Finished && !busy && !string.IsNullOrEmpty(error))
            {
                ShowBattleFailure(error);
                return;
            }
            if (model.Snapshot.Active && model.Snapshot.Finished && !busy)
            {
                UIHelper.HideUI<SurvivorUpgradeUICtrl>();
                UIHelper.HideUI<SurvivorHudUICtrl>();
                if (UIHelper.GetUICtrl<SurvivorResultUICtrl>()?.IsShow() != true)
                    UIHelper.ShowUI<SurvivorResultUICtrl>();
                return;
            }
            if (model.Snapshot.Active && model.UpgradeChoices.Length == 3 &&
                UIHelper.GetUICtrl<SurvivorUpgradeUICtrl>()?.IsShow() != true)
                UIHelper.ShowUI<SurvivorUpgradeUICtrl>();
        }

        /// <summary>复用现有错误弹窗；损坏现场提供返回入口，不允许继续静默暂停。</summary>
        private void ShowBattleFailure(string error)
        {
            if (shownBattleError == error) return;
            shownBattleError = error;
            Log.Error("[SurvivorSceneFlowSystem] 战场失效：" + error);
            Notify.ShowMessage(Texts.SaveErrorLoadFailed, error,
                new System.Collections.Generic.List<MessageButtonConfig> {
                    new MessageButtonConfig { ButtonText = Texts.CommonConfirm,
                        Role = MessageButtonRole.Confirm, OnClick = () => ReturnFromFailedBattleAsync().Forget() }
                });
        }

        /// <summary>故障退出只卸载无效现场；不伪造死亡、放弃结果或触碰闯关存档。</summary>
        private async UniTask ReturnFromFailedBattleAsync()
        {
            if (busy) return;
            busy = true;
            try
            {
                var result = await this.GetSystem<ArcadeSceneFlowSystem>().ReturnToMainMenuAsync(showFailureDialog: false);
                if (!result.Success) { shownBattleError = null; ShowBattleFailure(result.Error); return; }
                this.GetSystem<SurvivorSystem>().Stop();
                UIHelper.HideUI<SurvivorUpgradeUICtrl>();
                UIHelper.HideUI<SurvivorHudUICtrl>();
            }
            catch (Exception exception) { shownBattleError = null; ShowBattleFailure(exception.Message); }
            finally { busy = false; }
        }
    }
}
