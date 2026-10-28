using System;
using System.Globalization;
using System.Linq;
using Cysharp.Threading.Tasks;
using QFramework;
using TMPro;

namespace Game
{
    /// <summary>按需呈现单人进度和结果；开局及双人入口统一由主菜单提供。</summary>
    public sealed class ArcadeResumeUICtrl : BaseUICtrl
    {
        private ArcadeRunSaveData current;
        // 无进行中存档时隐藏所有本局操作；不再提供新局或联机分流。
        private bool selectionMode;
        /// <summary>仅联机沿用再次点击提交放弃意图，单人使用独立确认框。</summary>
        private bool awaitingAbandonConfirmation;
        private bool operationPending;
        /// <summary>单人点击继续后等待商店完成的本局身份；关闭窗口或换局即失效。</summary>
        private string continueAfterScoreShopRunId;
        /// <summary>双人准备与结果由服务器快照驱动，不读取单人存档。</summary>
        private bool onlineMode;
        /// <summary>上一次默认联机说明；语言切换只替换默认文案，不覆盖操作错误或放弃确认。</summary>
        private string shownOnlineHint;
        /// <summary>入口请求版本；显示、隐藏或卸载使上一轮资源准备失效，同一 View 重开也不复用旧请求。</summary>
        private int operationGeneration;
        private ArcadeResumeUI UI => BaseUI as ArcadeResumeUI;

        protected override string Path() => "Arcade/ArcadeResume";
        public override UILayer Layer() => UILayer.Dialog;
        public override bool BlocksGame => true;

        /// <summary>用已校验的存档预填隐藏窗口，不恢复领域状态；可见窗口仍由正式显示及领域事件刷新。</summary>
        internal void PrepareMenuContent(ArcadeRunSaveData snapshot)
        {
            if (BaseUI == null || IsShow()) return;
            onlineMode = false;
            selectionMode = snapshot == null;
            Render(snapshot);
        }

        /// <summary>主菜单按需打开已有进度或结果；无存档时不再显示模式选择。</summary>
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
            error = Loc.T("ui.arcade.no_pending_run");
            return false;
        }

        /// <summary>初始安全节点落盘后切换正式街机地图；任一步失败保留节点并返回主界面供重试。</summary>
        internal static async UniTask EnterNewRunAsync(ArcadeRunSystem system)
        {
            var flow = Game.Interface.GetSystem<ArcadeSceneFlowSystem>();
            var mapResult = await flow.EnterArenaMapAsync();
            if (mapResult.Success && TryEnterArena(system, out var error))
            {
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
            UI.BtnClose.onClick.AddListener(CloseFromMenu);
            UI.BtnContinue.onClick.AddListener(ChooseContinue);
            UI.BtnSettle.onClick.AddListener(() => SettleAsync().Forget());
            UI.BtnAbandon.onClick.AddListener(RequestAbandon);
            this.RegisterEvent<ArcadeRunChangedEvent>(OnRunChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineArcadeRunChangedEvent>(_ => RefreshOnline()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ => { if (onlineMode) RefreshOnline(); else Render(current); }).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            operationGeneration++;
            continueAfterScoreShopRunId = null;
            awaitingAbandonConfirmation = false;
            operationPending = false;
            UI.BtnContinue.interactable = true;
            UI.BtnSettle.interactable = true;
            UI.BtnAbandon.interactable = true;
            // 场内安全节点没有后方菜单，不能只关闭唯一的恢复入口。
            RefreshCloseButton();
            onlineMode = this.GetSystem<OnlineArcadeRunSystem>().Snapshot.HasRun;
            if (onlineMode)
            {
                shownOnlineHint = null;
                Set(UI.TextMessage, string.Empty);
                RefreshOnline();
                return;
            }
            ArcadeRunSaveData snapshot = this.GetModel<ArcadeRunModel>().Current;
            selectionMode = snapshot == null;
            Render(snapshot);
        }

        /// <summary>关闭使尚未提交的资源准备失效；已提交的地图事务仍由原流程负责收尾。</summary>
        protected override void OnHide()
        {
            operationGeneration++;
            continueAfterScoreShopRunId = null;
            awaitingAbandonConfirmation = false;
        }

        protected override void OnUnload()
        {
            operationGeneration++;
            continueAfterScoreShopRunId = null;
            operationPending = false;
        }

        private void OnRunChanged(ArcadeRunChangedEvent evt)
        {
            if (onlineMode) return;
            if (!IsShow()) return;
            if (evt.Phase is ArcadeRunPhase.SafeCheckpoint or ArcadeRunPhase.Victory or ArcadeRunPhase.Result) Render(evt.Snapshot);
            else if (evt.Phase == ArcadeRunPhase.None) Hide();
            if (continueAfterScoreShopRunId == null) return;
            if (evt.Phase != ArcadeRunPhase.SafeCheckpoint || evt.Snapshot?.RunId != continueAfterScoreShopRunId || evt.Snapshot.HasResult)
            {
                continueAfterScoreShopRunId = null;
                return;
            }
            if (evt.Snapshot.ScoreShop?.Completed == true)
            {
                // 成功落盘后才接续原点击；先消费意图，避免重复事件或购买回调再次加载。
                continueAfterScoreShopRunId = null;
                operationPending = true;
                UI.BtnContinue.interactable = false;
                ContinueAfterScoreShopAsync(evt.Snapshot.RunId, operationGeneration).Forget();
            }
        }

        /// <summary>等安全节点事件派发结束再进关，避免旧事件覆盖新HUD；等待期间仍校验窗口与本局身份。</summary>
        private async UniTask ContinueAfterScoreShopAsync(string runId, int generation)
        {
            await UniTask.Yield();
            if (!IsShow() || generation != operationGeneration) return;
            operationPending = false;
            var model = this.GetModel<ArcadeRunModel>();
            var snapshot = model.Current;
            if (model.Phase != ArcadeRunPhase.SafeCheckpoint || snapshot?.RunId != runId ||
                snapshot.HasResult || snapshot.ScoreShop?.Completed != true)
            {
                Render(snapshot);
                return;
            }
            Render(snapshot);
            ChooseContinue();
        }

        /// <summary>打开界面、存档事件与语言切换时刷新展示；不改变布局或街机领域数据。</summary>
        private void Render(ArcadeRunSaveData snapshot)
        {
            if (UI == null) return;
            RefreshCloseButton();
            UI.ResumeDetails.SetActive(!selectionMode);
            UI.SoloStats.SetActive(!selectionMode);
            UI.OnlinePlayers.SetActive(false);
            Set(UI.TextPlayerNames, string.Empty);
            Set(UI.TextPlayerScores, string.Empty);
            Set(UI.TextPlayerStates, string.Empty);
            UI.BtnOnline.gameObject.SetActive(false);
            UI.BtnSettle.interactable = !operationPending;
            UI.BtnAbandon.interactable = !operationPending;
            UI.BtnContinue.gameObject.SetActive(!selectionMode && snapshot != null && !snapshot.HasResult);
            UI.BtnSettle.gameObject.SetActive(snapshot?.HasResult == true);
            UI.BtnAbandon.gameObject.SetActive(!selectionMode && snapshot?.HasResult != true);
            UI.DialogTitle.text = Loc.T(snapshot?.HasResult == true ? "ui.arcade.dialog.result" : "ui.arcade.dialog.progress");
            if (selectionMode)
            {
                current = null;
                Set(UI.TextSummary, string.Empty);
                Set(UI.TextRewards, string.Empty);
                Set(UI.TextMessage, string.Empty);
                Set(UI.TextArenaCount, string.Empty);
                Set(UI.TextLives, string.Empty);
                Set(UI.TextGold, string.Empty);
                SetButton(UI.BtnContinue, Loc.T("ui.arcade.resume"));
                return;
            }
            if (snapshot == null) return;
            current = snapshot.Clone();
            // 浏览进度仅展示存档，商店选择属于点击继续后的流程。
            UI.BtnContinue.interactable = !operationPending;
            SetButton(UI.BtnContinue, Loc.T("ui.arcade.resume"));
            Set(UI.TextSummary, current.HasResult ? Loc.T("ui.arcade.result." + (current.Victory ? "victory" : current.Outcome))
                : Loc.T("ui.arcade.details.continuable"));
            Set(UI.TextRewards, FormatDetailScore(current.Score));
            Set(UI.TextArenaCount, current.CompletedArenaCount.ToString("D2", CultureInfo.InvariantCulture));
            Set(UI.TextLives, current.RemainingLives.ToString("D2", CultureInfo.InvariantCulture));
            Set(UI.TextGold, FormatDetailScore(current.RunGold));
            Set(UI.TextMessage, current.HasResult ? string.Empty : current.RoundCheckpoint is { } round
                ? Loc.T("ui.arcade.island.resume", round.RoundNumber, Loc.T("ui.arcade.theme." + round.ArenaConfigNumber))
                : current.IslandRoundCheckpoint is { } checkpoint
                ? Loc.T("ui.arcade.island.resume", checkpoint.RoundNumber, Loc.T(ArcadeIslandLayout.GetNameKey(checkpoint.RoundNumber)))
                : Loc.T("ui.arcade.details.resume_hint"));
        }

        /// <summary>点击开始/继续时检查已由 Boot 准备的资源并提交街机流程；旧请求不得影响新窗口。</summary>
        private void ChooseContinue()
        {
            if (operationPending) return;
            if (!onlineMode && current?.ScoreShop is {Completed:false})
            {
                continueAfterScoreShopRunId = current.RunId;
                ShowScoreShopIfNeeded(current.ScoreShop);
                return;
            }
            if (ShowScoreShopIfNeeded(onlineMode ? this.GetSystem<OnlineArcadeRunSystem>().GetScoreShopState() : current?.ScoreShop)) return;
            int generation = operationGeneration;
            var online = this.GetSystem<OnlineArcadeRunSystem>().Snapshot;
            bool challenge = onlineMode
                ? ShouldShowChallengeButton(online.ChallengeId, online.ChallengeAttempted)
                : current != null && ShouldShowChallengeButton(current.Checkpoint?.ChallengeId, current.Checkpoint?.ChallengeAttempted == true);
            if (!challenge) { ContinueRunAsync().Forget(); return; }
            bool Current() => IsShow() && generation == operationGeneration;
            // 使用正式通知按钮展示挑战选择，避免把旧联机入口的静态本地化按钮变为动态控件。
            Notify.ShowMessage(Loc.T("ui.arcade.dialog.progress"), Loc.T("ui.arcade.challenge.enter"),
                new System.Collections.Generic.List<MessageButtonConfig> {
                    new MessageButtonConfig { ButtonText = onlineMode ? Loc.T(LocalPlayer(online).Ready ? "ui.arcade.online.cancel_ready" : "ui.arcade.online.ready") : Loc.T("ui.arcade.resume"),
                        Role = MessageButtonRole.Confirm, OnClick = () => { if (Current()) ContinueRunAsync().Forget(); } },
                    new MessageButtonConfig { ButtonText = onlineMode ? Loc.T(LocalPlayer(online).ChallengeReady ? "ui.arcade.challenge.cancel_ready" : "ui.arcade.challenge.ready") : Loc.T("ui.arcade.challenge.enter"),
                        Role = MessageButtonRole.Confirm, OnClick = () => { if (Current()) StartChallengeAsync().Forget(); } }
                });
        }

        private async UniTask ContinueRunAsync()
        {
            if (onlineMode) { await SetOnlineReadyAsync(false); return; }
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
                    Set(UI.TextMessage, Loc.T("ui.arcade.no_pending_run"));
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

        /// <summary>先释放现场，再确认已落盘结果；失败保留当前结果界面与重试入口。</summary>
        private async UniTask SettleAsync()
        {
            if (operationPending) return;
            operationPending = true;
            int generation = operationGeneration;
            string runId = current?.RunId;
            try
            {
                if (onlineMode)
                {
                    var system = this.GetSystem<OnlineArcadeRunSystem>();
                    long revision = system.Snapshot.Revision;
                    long onlineRunId = system.Snapshot.RunId;
                    var result = await system.ConfirmSettlementAsync();
                    if (!result.Success && system.Snapshot.HasRun && system.Snapshot.RunId == onlineRunId && system.Snapshot.Revision > revision &&
                        system.Snapshot.Phase == OnlineArcadeRunPhase.Victory && !LocalPlayer(system.Snapshot).SettlementConfirmed)
                        result = await system.ConfirmSettlementAsync();
                    if (!result.Success && IsShow() && generation == operationGeneration) Set(UI.TextMessage, ArcadeUiFormatter.OnlineResult(result.Code));
                    return;
                }
                var menu = await this.GetSystem<ArcadeSceneFlowSystem>().ReturnToMainMenuAsync();
                if (!menu.Success) { if (IsShow() && generation == operationGeneration) Set(UI.TextMessage, menu.Error); return; }
                if (this.GetModel<ArcadeRunModel>().Current?.RunId != runId) return;
                if (!this.GetSystem<ArcadeRunSystem>().TrySettle(out _, out var error))
                {
                    UIHelper.ShowUI<ArcadeResumeUICtrl>();
                    Set(UI.TextMessage, error);
                    return;
                }
                Hide();
                UIHelper.ShowUI<StartUICtrl>();
            }
            finally { FinishOperation(generation); }
        }

        /// <summary>单人使用明确的确认框；确认只对当前窗口与原本局有效，取消不修改存档。</summary>
        private void RequestAbandon()
        {
            if (operationPending) return;
            if (onlineMode) { AbandonAsync().Forget(); return; }
            var snapshot = this.GetModel<ArcadeRunModel>().Current;
            if (snapshot == null || snapshot.HasResult) return;
            var owner = BaseUI;
            int generation = operationGeneration;
            Notify.ShowMessage(Loc.T("ui.arcade.abandon"), Loc.T("ui.arcade.abandon.body"),
                new System.Collections.Generic.List<MessageButtonConfig> {
                    new MessageButtonConfig { ButtonText = Loc.T("ui.arcade.abandon.confirm"), Role = MessageButtonRole.Confirm,
                        OnClick = () => {
                            var currentRun = this.GetModel<ArcadeRunModel>().Current;
                            if (IsShow() && ReferenceEquals(owner, BaseUI) && generation == operationGeneration &&
                                !onlineMode && !operationPending && currentRun?.RunId == snapshot.RunId && !currentRun.HasResult)
                                AbandonSinglePlayerAsync(snapshot.RunId).Forget();
                        } },
                    new MessageButtonConfig { ButtonText = Texts.CommonCancel, Role = MessageButtonRole.Cancel }
                });
        }

        /// <summary>先释放地图，成功后才清除已确认放弃的单人局；失败保留原进度与放弃重试入口。</summary>
        private async UniTask AbandonSinglePlayerAsync(string runId)
        {
            if (operationPending) return;
            operationPending = true;
            int generation = operationGeneration;
            UI.BtnContinue.interactable = false;
            UI.BtnSettle.interactable = false;
            UI.BtnAbandon.interactable = false;
            RefreshCloseButton();
            try
            {
                var menu = await this.GetSystem<ArcadeSceneFlowSystem>().ReturnToMainMenuAsync();
                if (!menu.Success)
                {
                    if (IsShow() && generation == operationGeneration) Set(UI.TextMessage, menu.Error);
                    return;
                }
                // 场景事务会主动隐藏本窗口；此后只核验本局身份，不把旧确认应用到新存档。
                if (this.GetModel<ArcadeRunModel>().Current?.RunId != runId) return;
                if (!this.GetSystem<ArcadeRunSystem>().TryAbandonAndClear(out var error))
                {
                    UIHelper.ShowUI<ArcadeResumeUICtrl>();
                    Set(UI.TextMessage, error);
                    return;
                }
                Notify.ShowFloatTips(Loc.T("ui.arcade.result.abandoned"));
            }
            finally { FinishOperation(generation); }
        }

        /// <summary>双人仍沿用二次点击及双方权威确认，不调用单人清档事务。</summary>
        private async UniTask AbandonAsync()
        {
            if (operationPending) return;
            if (!awaitingAbandonConfirmation)
            {
                awaitingAbandonConfirmation = true;
                Set(UI.TextMessage, Loc.T("ui.arcade.abandon_confirm"));
                return;
            }
            operationPending = true;
            int generation = operationGeneration;
            try
            {
                if (onlineMode)
                {
                    var result = await this.GetSystem<OnlineArcadeRunSystem>().ConfirmAbandonAsync();
                    if (!result.Success && IsShow() && generation == operationGeneration) Set(UI.TextMessage, ArcadeUiFormatter.OnlineResult(result.Code));
                    return;
                }
            }
            finally { FinishOperation(generation); }
        }

        /// <summary>挑战选择复用正式通知流程，不新增 View 字段或运行时布局。</summary>
        private async UniTask StartChallengeAsync()
        {
            if (onlineMode) { await SetOnlineReadyAsync(true); return; }
            if (operationPending) return;
            operationPending = true;
            int generation = operationGeneration;
            bool Current() => IsShow() && generation == operationGeneration;
            try
            {
                var preparation = this.GetUtility<IGameplayResourcePreparation>();
                if (preparation != null && !await preparation.PrepareAsync(MapIds.Arcade, Current)) return;
                if (!Current()) return;
                var map = await this.GetSystem<ArcadeSceneFlowSystem>().EnterArenaMapAsync();
                if (!Current()) return;
                RefreshCloseButton();
                if (!map.Success) { Set(UI.TextMessage, map.Error); return; }
                if (!this.GetSystem<ArcadeRunSystem>().TryStartChallenge(out _, out var error))
                { Set(UI.TextMessage, error); return; }
                if (this.GetModel<ArcadeRunModel>().Phase != ArcadeRunPhase.Challenge)
                { Set(UI.TextMessage, this.GetSystem<ArcadeCombatSystem>().LastEntryError); return; }
                Hide(); UIHelper.ShowUI<ArcadeHudUICtrl>();
            }
            finally { if (generation == operationGeneration) operationPending = false; }
        }

        /// <summary>按当前登录身份读取服务器个人快照，不创建第二份权威状态。</summary>
        private OnlineArcadePlayerSnapshot LocalPlayer(OnlineArcadeRunClientSnapshot snapshot) =>
            snapshot.Players.FirstOrDefault(p => p.PlayerId == this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId);

        /// <summary>挑战机会只在未尝试的有效恢复节点显示。</summary>
        internal static bool ShouldShowChallengeButton(string challengeId, bool attempted) =>
            !string.IsNullOrWhiteSpace(challengeId) && !attempted;

        /// <summary>每次权威变更重绘双方准备与结果状态；交易列表不再参与阶段判断。</summary>
        internal static bool CanReadyOnline(OnlineArcadePlayerSnapshot player,bool idle)=>idle&&player.CanReadyForArena;

        private void RefreshOnline()
        {
            if (!onlineMode || !IsShow()) return;
            RefreshCloseButton();
            var snapshot = this.GetSystem<OnlineArcadeRunSystem>().Snapshot;
            if (!snapshot.HasRun) { Hide(); return; }
            if (snapshot.Phase is OnlineArcadeRunPhase.Arena or OnlineArcadeRunPhase.Challenge)
            { Hide(); UIHelper.ShowUI<ArcadeHudUICtrl>(); return; }
            var self = LocalPlayer(snapshot);
            if (self.PlayerId == 0) return;
            bool victory = snapshot.Phase == OnlineArcadeRunPhase.Victory;
            bool idle = snapshot.OperationState == OnlineArcadeOperationState.Idle && self.SettlementState == 0;
            UI.ResumeDetails.SetActive(true);
            UI.DialogTitle.text = Loc.T(victory ? "ui.arcade.dialog.result" : "ui.arcade.dialog.progress");
            UI.BtnContinue.gameObject.SetActive(!victory);
            bool choosingScoreShop=ShowScoreShopIfNeeded(this.GetSystem<OnlineArcadeRunSystem>().GetScoreShopState());
            UI.BtnContinue.interactable = CanReadyOnline(self,idle) && !choosingScoreShop;
            SetButton(UI.BtnContinue, Loc.T(self.Ready ? "ui.arcade.online.cancel_ready" : "ui.arcade.online.ready"));
            UI.BtnSettle.gameObject.SetActive(victory);
            UI.BtnSettle.interactable = snapshot.OperationState == OnlineArcadeOperationState.Idle;
            UI.BtnAbandon.gameObject.SetActive(!victory);
            UI.BtnAbandon.interactable = idle;
            UI.BtnOnline.gameObject.SetActive(false);
            RenderOnlineDetails(snapshot, victory);
        }

        /// <summary>安全节点呈现本人未完成的整关商店；显示不抽取商品，也不提交跳过。</summary>
        private bool ShowScoreShopIfNeeded(FirepowerLoop.Arcade.ArcadeScoreShopState shop)
        {
            if(shop is not {Completed:false})return false;
            if(IsShow() && UIHelper.GetUICtrl<ArcadeScoreShopUICtrl>()?.IsShow()!=true)UIHelper.ShowUI<ArcadeScoreShopUICtrl>();
            return true;
        }

        /// <summary>复用详情框展示双方权威分数与准备/结果确认状态；与单人指标互斥，不改变联机规则。</summary>
        private void RenderOnlineDetails(OnlineArcadeRunClientSnapshot snapshot, bool victory)
        {
            UI.SoloStats.SetActive(false);
            UI.OnlinePlayers.SetActive(true);
            Set(UI.TextArenaCount, string.Empty);
            Set(UI.TextLives, string.Empty);
            Set(UI.TextGold, string.Empty);
            Set(UI.TextSummary, Loc.T(victory ? "ui.arcade.result.victory" : "ui.arcade.details.coop"));
            Set(UI.TextRewards, FormatDetailScore(snapshot.Players.Sum(p => p.Score)));
            Set(UI.TextPlayerNames, string.Join("\n", snapshot.Players.Select(p => p.DisplayName.Replace("\r", " ").Replace("\n", " "))));
            Set(UI.TextPlayerScores, string.Join("\n", snapshot.Players.Select(p => FormatDetailScore(p.Score))));
            Set(UI.TextPlayerStates, string.Join("\n", snapshot.Players.Select(p => Loc.T(victory
                ? p.SettlementConfirmed ? "ui.arcade.details.confirmed" : "ui.arcade.details.pending_confirmation"
                : p.Ready || p.ChallengeReady ? "ui.arcade.online.ready" : "ui.arcade.online.not_ready"))));
            var hint = victory ? string.Empty : Loc.T("ui.arcade.details.coop_hint");
            if (string.IsNullOrEmpty(UI.TextMessage.text) || UI.TextMessage.text == shownOnlineHint)
                Set(UI.TextMessage, hint);
            shownOnlineHint = hint;
        }

        /// <summary>详情分数使用稳定的千位分组；完整数值始终来自当前快照。</summary>
        private static string FormatDetailScore(long score) => score.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>资源与地图准备成功后才提交准备意图；取消准备不重复加载。</summary>
        private async UniTask SetOnlineReadyAsync(bool challenge)
        {
            if (operationPending) return;
            operationPending = true;
            int generation = operationGeneration;
            var system = this.GetSystem<OnlineArcadeRunSystem>();
            var snapshot = system.Snapshot;
            var self = LocalPlayer(snapshot);
            bool ready = !(challenge ? self.ChallengeReady : self.Ready);
            bool Current() => IsShow() && generation == operationGeneration && system.Snapshot.RunId == snapshot.RunId && system.Snapshot.Revision == snapshot.Revision;
            try
            {
                if (ready)
                {
                    var preparation = this.GetUtility<IGameplayResourcePreparation>();
                    if (preparation != null && !await preparation.PrepareAsync(MapIds.Arcade, Current)) return;
                    if (!Current()) return;
                    var map = await this.GetSystem<ArcadeSceneFlowSystem>().EnterArenaMapAsync();
                    if (!Current()) return;
                    RefreshCloseButton();
                    if (!map.Success) { Set(UI.TextMessage, map.Error); return; }
                }
                var result = challenge ? await system.SetChallengeReadyAsync(ready) : await system.SetReadyAsync(ready);
                if (!result.Success && IsShow() && generation == operationGeneration) Set(UI.TextMessage, ArcadeUiFormatter.OnlineResult(result.Code));
            }
            finally { if (generation == operationGeneration) operationPending = false; }
        }

        /// <summary>旧操作结束不得解除重开界面的等待状态；单人按钮不继承在线禁用状态。</summary>
        private void FinishOperation(int generation)
        {
            if (generation != operationGeneration) return;
            operationPending = false;
            if (!IsShow()) return;
            RefreshCloseButton();
            if (onlineMode) RefreshOnline();
            else
            {
                UI.BtnContinue.interactable = true;
                UI.BtnSettle.interactable = true;
                UI.BtnAbandon.interactable = true;
            }
        }

        /// <summary>地图加载后实时校验关闭条件，不能依赖打开界面时的地图快照。</summary>
        private void CloseFromMenu()
        {
            if (!operationPending && this.GetModel<MapSceneModel>().CurrentMapId != MapIds.Arcade) Hide();
            else RefreshCloseButton();
        }

        /// <summary>仅菜单浏览状态允许关闭，资源准备与场内恢复必须保留操作入口。</summary>
        private void RefreshCloseButton() => UI.BtnClose.gameObject.SetActive(
            !operationPending && this.GetModel<MapSceneModel>().CurrentMapId != MapIds.Arcade);

        private static void SetButton(UnityEngine.UI.Button button, string value) =>
            Set(button?.GetComponentInChildren<TextMeshProUGUI>(true), value);

        private static void Set(TextMeshProUGUI target, string value) { if (target != null) target.text = value ?? string.Empty; }
    }
}
