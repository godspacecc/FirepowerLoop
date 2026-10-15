using System;
using Cysharp.Threading.Tasks;
using QFramework;
using TMPro;

namespace Game
{
    /// <summary>作为主界面的街机本局入口：统一承载单人新局、单人恢复和双人联机分流；不直接持有联机房间状态。</summary>
    public sealed class ArcadeResumeUICtrl : BaseUICtrl
    {
        private ArcadeRunSaveData current;
        // 无本地进行中存档时只显示模式选择；恢复资料和破坏性操作由同一状态控制。
        private bool selectionMode;
        private bool awaitingAbandonConfirmation;
        private bool operationPending;
        /// <summary>入口请求版本；显示、隐藏或卸载使上一轮资源准备失效，同一 View 重开也不复用旧请求。</summary>
        private int operationGeneration;
        private ArcadeResumeUI UI => BaseUI as ArcadeResumeUI;

        protected override string Path() => "Arcade/ArcadeResume";
        public override UILayer Layer() => UILayer.Dialog;
        public override bool BlocksGame => true;

        /// <summary>主界面调用点：校验本地存档后打开正式选择界面，不在入口回调里隐式启动新局。</summary>
        public static bool TryOpenEntry(out string error)
        {
            var system = Game.Interface.GetSystem<ArcadeRunSystem>();
            var pending = system.InspectPending();
            if (pending.IsValid)
            {
                if (!system.TryResume(out _, out error)) return false;
                UIHelper.ShowUI<ArcadeResumeUICtrl>();
                return true;
            }
            if (pending.Status != SaveFileStatus.Missing)
            {
                error = string.IsNullOrWhiteSpace(pending.Error) ? Loc.T("ui.arcade.save_corrupted") : pending.Error;
                return false;
            }
            UIHelper.ShowUI<ArcadeResumeUICtrl>();
            error = null;
            return true;
        }

        /// <summary>初始安全节点落盘后切换正式街机地图；任一步失败保留节点并返回主界面供重试。</summary>
        private static async UniTaskVoid EnterNewRunAsync(ArcadeRunSystem system)
        {
            var flow = Game.Interface.GetSystem<ArcadeSceneFlowSystem>();
            var mapResult = await flow.EnterArenaMapAsync();
            if (mapResult.Success && TryEnterArena(system, out var error))
            {
                Game.Interface.GetSystem<AudioSystem>().PlayUiSound("ArcadeUiConfirm");
                UIHelper.HideUI<StartUICtrl>();
                UIHelper.ShowUI<ArcadeHudUICtrl>();
                return;
            }
            // 已落盘的新局也属于玩家进度；失败只返回菜单，绝不自动放弃或覆盖。
            await flow.ReturnToMainMenuAsync();
            ReportNewRunFailure(mapResult.Success ? Game.Interface.GetSystem<ArcadeCombatSystem>().LastEntryError : mapResult.Error);
        }

        internal static string RollbackNewRun(ArcadeRunSystem system, string entryError)
        {
            // 兼容旧调用名称；失败只能报告，不能删除已经落盘的本局。
            return entryError;
        }

        private static void ReportNewRunFailure(string entryError)
        {
            Log.Error($"[ArcadeResumeUICtrl] 首次进入街机失败：{entryError}");
            Notify.ShowFloatTips(string.IsNullOrWhiteSpace(entryError) ? Loc.T("ui.arcade.error.open") : entryError);
        }

        protected override void OnInit()
        {
            UI.BtnClose.onClick.AddListener(Hide);
            UI.BtnContinue.onClick.AddListener(() => ContinueRunAsync().Forget());
            UI.BtnOnline.onClick.AddListener(OpenOnline);
            UI.BtnSettle.onClick.AddListener(Settle);
            UI.BtnAbandon.onClick.AddListener(Abandon);
            this.RegisterEvent<ArcadeRunChangedEvent>(OnRunChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ => Render(current)).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            operationGeneration++;
            awaitingAbandonConfirmation = false;
            operationPending = false;
            UI.BtnContinue.interactable = true;
            ArcadeRunSaveData snapshot = this.GetModel<ArcadeRunModel>().Current;
            selectionMode = snapshot == null;
            Render(snapshot);
        }

        /// <summary>关闭使尚未提交的资源准备失效；已提交的地图事务仍由原流程负责收尾。</summary>
        protected override void OnHide()
        {
            operationGeneration++;
            awaitingAbandonConfirmation = false;
        }

        protected override void OnUnload()
        {
            operationGeneration++;
            operationPending = false;
        }

        private void OnRunChanged(ArcadeRunChangedEvent evt)
        {
            if (!IsShow()) return;
            if (evt.Phase is ArcadeRunPhase.SafeShop or ArcadeRunPhase.Victory or ArcadeRunPhase.Result) Render(evt.Snapshot);
            else if (evt.Phase == ArcadeRunPhase.None) Hide();
        }

        /// <summary>打开界面、存档事件与语言切换时刷新展示；不改变布局或街机领域数据。</summary>
        private void Render(ArcadeRunSaveData snapshot)
        {
            if (UI == null) return;
            UI.ResumeDetails.SetActive(!selectionMode);
            UI.BtnOnline.gameObject.SetActive(true);
            UI.BtnContinue.gameObject.SetActive(selectionMode || snapshot?.HasResult != true);
            UI.BtnSettle.gameObject.SetActive(snapshot?.HasResult == true);
            UI.BtnAbandon.gameObject.SetActive(!selectionMode && snapshot?.HasResult != true);
            if (selectionMode)
            {
                current = null;
                Set(UI.TextSummary, string.Empty);
                Set(UI.TextRewards, string.Empty);
                Set(UI.TextMessage, string.Empty);
                SetButton(UI.BtnContinue, Loc.T("ui.arcade.entry.start"));
                return;
            }
            if (snapshot == null) return;
            current = snapshot.Clone();
            SetButton(UI.BtnContinue, Loc.T("ui.arcade.resume"));
            Set(UI.TextSummary, current.HasResult ? Loc.T("ui.arcade.result." + current.Outcome) : Loc.T("ui.arcade.resume.summary", current.CompletedArenaCount, current.RemainingLives, current.RunGold));
            Set(UI.TextRewards, Loc.T("ui.arcade.hud.score", current.Score));
            Set(UI.TextMessage, string.Empty);
        }

        /// <summary>点击开始/继续时检查已由 Boot 准备的资源并提交街机流程；旧请求不得影响新窗口。</summary>
        private async UniTask ContinueRunAsync()
        {
            if (operationPending) return;
            operationPending = true;
            UI.BtnContinue.interactable = false;
            var owner = BaseUI;
            int generation = ++operationGeneration;
            bool IsCurrent() => generation == operationGeneration && owner != null &&
                ReferenceEquals(owner, BaseUI) && IsShow();
            try
            {
                var preparation = this.GetUtility<IGameplayResourcePreparation>();
                if (preparation != null && !await preparation.PrepareAsync(MapIds.Arcade, IsCurrent)) return;
                // 资源端口完成后再次校验；关闭再重开同一实例不代表旧请求重新获得提交权。
                if (!IsCurrent()) return;
                if (selectionMode)
                {
                    ArcadeRunSystem system = this.GetSystem<ArcadeRunSystem>();
                    if (!system.TryStartConfiguredRun(out _, out string startError))
                    {
                        Set(UI.TextMessage, startError);
                        return;
                    }
                    Hide();
                    EnterNewRunAsync(system).Forget();
                    return;
                }
                var mapResult = await this.GetSystem<ArcadeSceneFlowSystem>().EnterArenaMapAsync();
                if (!mapResult.Success)
                {
                    if (IsCurrent()) Set(UI.TextMessage, mapResult.Error);
                    return;
                }
                if (!TryEnterArena(this.GetSystem<ArcadeRunSystem>(), out var error))
                {
                    await this.GetSystem<ArcadeSceneFlowSystem>().ReturnToMainMenuAsync();
                    if (IsCurrent()) Set(UI.TextMessage, error);
                    return;
                }
                // 地图与战斗都已进入成功状态后才播放确认音，失败回滚不产生成功反馈。
                this.GetSystem<AudioSystem>().PlayUiSound("ArcadeUiConfirm");
                if (IsCurrent()) Hide();
                UIHelper.HideUI<StartUICtrl>();
                UIHelper.ShowUI<ArcadeHudUICtrl>();
            }
            catch (Exception exception)
            {
                Log.Error($"[ArcadeResumeUICtrl] 街机入口操作失败：{exception}");
                if (IsCurrent()) Set(UI.TextMessage, Loc.T("ui.arcade.error.open"));
            }
            finally
            {
                // 仅当前可见请求恢复交互；旧回调不能清除重开窗口中新请求的等待状态。
                if (IsCurrent())
                {
                    operationPending = false;
                    UI.BtnContinue.interactable = true;
                }
            }
        }

        /// <summary>切换到联机身份或双人房间入口；本地未结束局保持落盘，不在此处清理。</summary>
        private void OpenOnline()
        {
            Hide();
            if (!this.GetModel<OnlineAccountModel>().Snapshot.Value.IsAuthenticated)
            {
                Notify.ShowFloatTips(Loc.T("ui.online.error.not_logged_in"));
                OnlineLoginUICtrl.ShowForArcadeLobby();
                return;
            }
            OnlineRoomUICtrl.ShowArcadeLobby();
        }

        /// <summary>进入竞技场并确认同步战斗初始化没有把运行态回退到安全节点。</summary>
        private static bool TryEnterArena(ArcadeRunSystem system, out string error)
        {
            if (!system.TryContinue(out _, out error)) return false;
            if (Game.Interface.GetModel<ArcadeRunModel>().Phase == ArcadeRunPhase.Arena)
            {
                error = null;
                return true;
            }

            error = Game.Interface.GetSystem<ArcadeCombatSystem>()?.LastEntryError;
            if (string.IsNullOrWhiteSpace(error)) error = "街机战斗初始化失败";
            return false;
        }

        private void Settle()
        {
            if (!this.GetSystem<ArcadeRunSystem>().TrySettle(out _, out var error))
            {
                Set(UI.TextMessage, error);
                return;
            }
            Hide();
            UIHelper.ShowUI<StartUICtrl>();
        }

        private void Abandon()
        {
            if (!awaitingAbandonConfirmation)
            {
                awaitingAbandonConfirmation = true;
                Set(UI.TextMessage, Loc.T("ui.arcade.abandon_confirm"));
                return;
            }
            if (!this.GetSystem<ArcadeRunSystem>().TryAbandon(out var error))
            {
                Set(UI.TextMessage, error);
                return;
            }
            Hide();
            UIHelper.ShowUI<StartUICtrl>();
        }

        private static void SetButton(UnityEngine.UI.Button button, string value) =>
            Set(button?.GetComponentInChildren<TextMeshProUGUI>(true), value);

        private static void Set(TextMeshProUGUI target, string value) { if (target != null) target.text = value ?? string.Empty; }
    }
}
