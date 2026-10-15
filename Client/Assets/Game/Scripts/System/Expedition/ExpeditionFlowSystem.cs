using Cysharp.Threading.Tasks;
using QFramework;
using System;

namespace Game
{
    /// <summary>
    /// 表示远征进入、成功返回或失败返回的流程结果和错误信息。
    /// </summary>
    public readonly struct ExpeditionFlowResult
    {
        private ExpeditionFlowResult(bool success, string error)
        {
            Success = success;
            Error = error;
        }

        public bool Success { get; }
        public string Error { get; }

        public static ExpeditionFlowResult Ok()
        {
            return new ExpeditionFlowResult(true, string.Empty);
        }

        public static ExpeditionFlowResult Fail(string error)
        {
            return new ExpeditionFlowResult(false, error ?? string.Empty);
        }
    }

    /// <summary>
    /// 编排远征进入、返回与进入失败后的基地恢复事务；不生成地形、修复导航或决定界面展示。
    /// </summary>
    public sealed class ExpeditionFlowSystem : AbstractSystem
    {
        private bool busy;
        /// <summary>进入事务丢失基地后保留的运行态快照；恢复成功或明确离开后释放，不写入存档。</summary>
        private SaveRuntimeSnapshot pendingEntryRecovery;
        private string entryFailure;

        /// <summary>进入失败且基地尚未恢复时为真；界面据此提供恢复与离开入口。</summary>
        public bool HasPendingEntryRecovery => pendingEntryRecovery != null;

        /// <summary>重试失败进入事务的基地恢复；重复请求受流程忙状态保护，不重新进入远征。</summary>
        public async UniTask<ExpeditionFlowResult> RetryEntryRecoveryAsync()
        {
            if (busy) return ExpeditionFlowResult.Fail("远征流程正忙");
            if (!HasPendingEntryRecovery) return ExpeditionFlowResult.Ok();
            busy = true;
            try { return await RestoreEntryBaseAsync(); }
            finally { busy = false; }
        }

        /// <summary>离开进入失败现场；仅清理运行地图并暂停，不删除基地或副本文件。清理失败仍允许重试。</summary>
        public async UniTask<ExpeditionFlowResult> LeaveFailedEntryAsync()
        {
            if (busy) return ExpeditionFlowResult.Fail("远征流程正忙");
            if (!HasPendingEntryRecovery) return ExpeditionFlowResult.Fail("没有待恢复的进入事务");
            busy = true;
            try
            {
                var unload = await this.GetSystem<MapSceneSystem>().UnloadCurrentMapAsync();
                if (!unload.Success) return ExpeditionFlowResult.Fail($"{entryFailure}；离开失败现场失败：{unload.Error}");
                if (!this.GetSystem<SaveSystem>().RestoreRuntimeSnapshot(pendingEntryRecovery, out var error))
                    return ExpeditionFlowResult.Fail($"{entryFailure}；运行态恢复失败：{error}");
                this.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(AppState.Paused);
                pendingEntryRecovery = null;
                entryFailure = null;
                return ExpeditionFlowResult.Ok();
            }
            finally { busy = false; }
        }

        /// <summary>先恢复存档分区再加载基地；任何失败保留原快照与进入错误，供用户显式重试。</summary>
        private async UniTask<ExpeditionFlowResult> RestoreEntryBaseAsync()
        {
            var snapshot = pendingEntryRecovery;
            try
            {
                if (!this.GetSystem<SaveSystem>().RestoreRuntimeSnapshot(snapshot, out var error))
                    return ExpeditionFlowResult.Fail($"{entryFailure}；运行态恢复失败：{error}");
                var load = await this.GetSystem<MapSceneSystem>().LoadBaseMapAsync(snapshot.CurrentBase);
                if (!load.Success) return ExpeditionFlowResult.Fail($"{entryFailure}；基地恢复失败：{load.Error}");
                pendingEntryRecovery = null;
                entryFailure = null;
                return ExpeditionFlowResult.Ok();
            }
            catch (Exception exception)
            {
                return ExpeditionFlowResult.Fail($"{entryFailure}；基地恢复异常：{exception.Message}");
            }
        }

        protected override void OnInit()
        {
        }

        public UniTask<ExpeditionFlowResult> EnterSurvivalAsync(string mapId)
        {
            return EnterExpeditionAsync(mapId, MapKind.Survival);
        }

        public UniTask<ExpeditionFlowResult> EnterTaskAsync(string mapId)
        {
            return EnterExpeditionAsync(mapId, MapKind.Task);
        }

        /// <summary>从固定副本文件恢复候选世界；任何地图候选失败均回滚运行态并保留文件。</summary>
        public async UniTask<ExpeditionFlowResult> ResumePendingExpeditionAsync()
        {
            if (busy) return ExpeditionFlowResult.Fail("远征流程正忙");
            var save = this.GetSystem<SaveSystem>();
            var pending = save.InspectExpedition();
            if (pending.Status == SaveFileStatus.Missing) return ExpeditionFlowResult.Fail("没有待恢复的远征");
            if (!pending.IsValid) return ExpeditionFlowResult.Fail(pending.Error ?? "副本存档损坏");
            var model = this.GetModel<ExpeditionModel>();
            busy = true;
            try
            {
                var preparation = this.GetUtility<IGameplayResourcePreparation>();
                if (preparation != null && !await preparation.PrepareAsync(pending.Data.MapId,
                    () => !model.IsActive && save.InspectExpedition().Data?.ExpeditionId == pending.Data.ExpeditionId))
                    return ExpeditionFlowResult.Fail("远征资源未就绪");
                // 分区先通过正式提交入口恢复；若地图候选恢复失败，下面会完整回滚该运行态。
                var before = save.CaptureRuntimeSnapshot();
                if (!save.CommitExpeditionRuntime(pending.Data, out var commitError))
                    return ExpeditionFlowResult.Fail(commitError);
                if (!model.TryRestoreSave(pending.Data, out var restoreError))
                {
                    save.RestoreRuntimeSnapshot(before, out _);
                    return ExpeditionFlowResult.Fail(restoreError);
                }
                if (!this.GetSystem<ExpeditionTaskSystem>().TryBindRestoredExpedition(pending.Data, out var taskError))
                {
                    save.RestoreRuntimeSnapshot(before, out _);
                    return ExpeditionFlowResult.Fail(taskError);
                }
                var result = await this.GetSystem<MapSceneSystem>().LoadMapAsync(pending.Data.MapId, pending.Data);
                if (!result.Success)
                {
                    model.Clear();
                    save.RestoreRuntimeSnapshot(before, out var rollbackError);
                    this.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(AppState.Paused);
                    return ExpeditionFlowResult.Fail(string.IsNullOrWhiteSpace(rollbackError)
                        ? result.Error
                        : $"{result.Error}；恢复副本运行态失败：{rollbackError}");
                }
                this.SendEvent(new ExpeditionChangedEvent());
                return ExpeditionFlowResult.Ok();
            }
            finally { busy = false; }
        }

        /// <summary>旧探索可按原规则离线结算；新版河谷必须继续探索完成实体撤离，拒绝时不改写文件。</summary>
        public UniTask<ExpeditionFlowResult> SettlePendingWithoutLoadingAsync()
        {
            return SettlePendingWithoutLoadingInternal();
        }

        private async UniTask<ExpeditionFlowResult> SettlePendingWithoutLoadingInternal()
        {
            if (busy) return ExpeditionFlowResult.Fail("远征流程正忙");
            busy = true;
            try
            {
            var save = this.GetSystem<SaveSystem>();
            var expedition = save.InspectExpedition();
            if (!expedition.IsValid) return ExpeditionFlowResult.Fail(expedition.Error ?? "副本存档损坏");
            if (expedition.Data.ValleyLayout?.GenerationVersion == 3)
                return ExpeditionFlowResult.Fail("请继续河谷探索，到达实体撤离点并完成读条");
            var baseRead = save.InspectBase();
            BaseSaveData baseData;
            if (baseRead.Status == SaveFileStatus.Valid)
            {
                baseData = baseRead.Data;
            }
            else if (baseRead.Status == SaveFileStatus.Missing)
            {
                if (!save.TryCreateBaseCandidate(out baseData, out var candidateError))
                    return ExpeditionFlowResult.Fail(candidateError ?? "无法建立基地候选");
            }
            else
            {
                return ExpeditionFlowResult.Fail(baseRead.Error ?? "基地存档损坏");
            }

            if (!save.ApplyExpeditionSettlement(baseData, expedition.Data, false, out _, out var settlementError))
                return ExpeditionFlowResult.Fail(settlementError);
            if (!save.SaveBase(baseData, out var baseError)) return ExpeditionFlowResult.Fail(baseError);
            if (!save.DeleteExpeditionFiles(out var deleteError)) return ExpeditionFlowResult.Fail(deleteError);
            this.GetModel<ExpeditionModel>().Clear();
            this.GetSystem<ExpeditionTaskSystem>()?.Clear();
            this.GetModel<SaveModel>().SetBase(baseData, false);
            return ExpeditionFlowResult.Ok();
            }
            finally
            {
                busy = false;
            }
        }

        public async UniTask<ExpeditionFlowResult> EnterExpeditionAsync(string mapId)
        {
            if (!MapCatalog.TryGet(mapId, out var definition))
            {
                return ExpeditionFlowResult.Fail($"未知远征地图：{mapId}");
            }

            if (definition.Kind != MapKind.Survival && definition.Kind != MapKind.Task)
            {
                return ExpeditionFlowResult.Fail($"地图不是远征地图：{mapId}");
            }

            return await EnterExpeditionAsync(mapId, definition.Kind);
        }

        /// <summary>暂停菜单仅能结束当前离线任务或生存远征；包含和家村，街机由独立流程负责。</summary>
        public bool CanReturnFromPause()
        {
            var map = this.GetModel<MapSceneModel>();
            var expedition = this.GetModel<ExpeditionModel>();
            return !busy && this.GetModel<AppStateModel>().CurrentState.Value == AppState.Paused &&
                   expedition.ValleyLayout?.GenerationVersion != 3 &&
                   map.Status.Value == MapLoadStatus.Loaded && map.CurrentMode == MapPreparationMode.OfflineFull &&
                   expedition.IsActive && expedition.Status != ExpeditionStatus.Returning &&
                   !string.IsNullOrWhiteSpace(expedition.CurrentExpeditionId) &&
                   map.CurrentMapId == expedition.CurrentMapId &&
                   MapCatalog.TryGet(map.CurrentMapId, out var definition) &&
                   (definition.Kind == MapKind.Survival || definition.Kind == MapKind.Task);
        }

        /// <summary>
        /// 确认后核对当趟身份与提示过的返程资格；达标正常结算，未达标复用失败清空事务。
        /// 过期、并发或重复请求直接拒绝，不切换地图、不修改远征或物资。
        /// </summary>
        /// <param name="mapId">打开确认框时的稳定地图 ID。</param>
        /// <param name="expeditionId">打开确认框时的当趟远征 ID。</param>
        /// <param name="qualified">确认框承诺的正常结算资格；状态变化后必须重新确认。</param>
        public UniTask<ExpeditionFlowResult> ReturnFromPauseAsync(string mapId, string expeditionId, bool qualified)
        {
            var expedition = this.GetModel<ExpeditionModel>();
            if (!CanReturnFromPause() || expedition.CurrentMapId != mapId ||
                expedition.CurrentExpeditionId != expeditionId || expedition.CanReturn != qualified)
                return UniTask.FromResult(ExpeditionFlowResult.Fail("返回营地确认已过期或当前状态不允许返回"));
            return qualified ? ReturnToBaseAsync() : FailExpeditionAndReturnToBaseAsync();
        }

        public async UniTask<ExpeditionFlowResult> ReturnToBaseAsync()
        {
            if(this.GetModel<ExpeditionModel>().ValleyLayout?.GenerationVersion==3 &&
                this.GetSystem<ExtractionSystem>()?.HasCompletedReturnAuthorization!=true)
                return ExpeditionFlowResult.Fail("须到达实体撤离点并完成读条");
            if (busy)
            {
                return ExpeditionFlowResult.Fail("远征流程正忙");
            }

            var expedition = this.GetModel<ExpeditionModel>();
            if (!expedition.CanReturn)
            {
                return ExpeditionFlowResult.Fail("尚未满足返回条件");
            }

            var save = this.GetSystem<SaveSystem>();
            var beforeReturnRuntime = save.CaptureRuntimeSnapshot();
            busy = true;
            expedition.BeginReturning();
            this.SendEvent(new ExpeditionChangedEvent());
            try
            {
                var expeditionData = expedition.CaptureSave(expedition.CurrentExpeditionId ?? Guid.NewGuid().ToString("N"), DateTime.UtcNow);
                // 运行态可短暂处于 Returning，但落盘候选必须可在重启后继续返回。
                expeditionData.Status = ExpeditionStatus.ReturnQualified;
                if (!save.CaptureExpeditionSections(expeditionData, out var captureError))
                { expedition.RestoreReturnQualified(); return ExpeditionFlowResult.Fail(captureError); }
                if (!save.SaveExpedition(expeditionData, out var expeditionSaveError))
                { expedition.RestoreReturnQualified(); return ExpeditionFlowResult.Fail(expeditionSaveError); }
                var baseRead = save.InspectBase();
                string settlementError = null, baseSaveError = null, commitError = null, deleteError = null;
                var settled = baseRead.Status == SaveFileStatus.Valid && save.ApplyExpeditionSettlement(baseRead.Data, expeditionData, false, out _, out settlementError);
                var baseSaved = settled && save.SaveBase(baseRead.Data, out baseSaveError);
                if (!baseSaved)
                { expedition.RestoreReturnQualified(); return ExpeditionFlowResult.Fail(settlementError ?? baseSaveError ?? deleteError ?? baseRead.Error ?? "基地结算失败"); }

                if (!save.CommitBaseRuntime(baseRead.Data, out commitError))
                {
                    expedition.RestoreReturnQualified();
                    return ExpeditionFlowResult.Fail(commitError);
                }

                var deleted = save.DeleteExpeditionFiles(out deleteError);
                if (!deleted)
                {
                    var restored = save.RestoreRuntimeSnapshot(beforeReturnRuntime, out var restoreError);
                    expedition.RestoreReturnQualified();
                    var error = deleteError ?? "删除副本存档失败";
                    if (!restored)
                    {
                        error = $"{error}；恢复副本运行态失败：{restoreError}";
                    }
                    return ExpeditionFlowResult.Fail(error);
                }

                // 基地结算和副本清理已完成；之后地图加载失败不能回到已无文件的副本态。
                expedition.Clear();
                this.GetSystem<ExpeditionTaskSystem>()?.Clear();
                var load = await this.GetSystem<MapSceneSystem>().LoadBaseMapAsync(baseRead.Data);
                if (!load.Success)
                {
                    this.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(AppState.Paused);
                    UIHelper.ShowUI<StartUICtrl>();
                    this.SendEvent(new ExpeditionChangedEvent());
                    return ExpeditionFlowResult.Fail(load.Error);
                }

                this.SendEvent(new ExpeditionChangedEvent());
                CivilisationSettlementUICtrl.TryShowPending();
                return ExpeditionFlowResult.Ok();
            }
            finally
            {
                busy = false;
            }
        }

        public async UniTask<ExpeditionFlowResult> FailExpeditionAndReturnToBaseAsync()
        {
            if (busy)
            {
                return ExpeditionFlowResult.Fail("远征流程正忙");
            }

            var expedition = this.GetModel<ExpeditionModel>();
            if (!expedition.IsActive)
            {
                return ExpeditionFlowResult.Fail("当前没有远征");
            }

            busy = true;
            try
            {
                expedition.MarkDefeated();
                var save = this.GetSystem<SaveSystem>();
                var expeditionData = expedition.CaptureSave(expedition.CurrentExpeditionId ?? Guid.NewGuid().ToString("N"), DateTime.UtcNow);
                if (!save.CaptureExpeditionSections(expeditionData, out var captureError)) return ExpeditionFlowResult.Fail(captureError);
                if (!save.SaveExpedition(expeditionData, out var expeditionSaveError)) return ExpeditionFlowResult.Fail(expeditionSaveError);
                var baseRead = save.InspectBase();
                string settlementError = null, baseSaveError = null, deleteError = null;
                var alreadySettled = false;
                var settled = baseRead.Status == SaveFileStatus.Valid && save.ApplyExpeditionSettlement(baseRead.Data, expeditionData, true, out alreadySettled, out settlementError);
                var beforeDeathRuntime = save.CaptureRuntimeSnapshot();
                // 死亡结果必须在删除副本文件前一次性落到基地 DTO，不能在删除后依赖 Flush 补写。
                var inventory = this.GetSystem<InventorySystem>();
                this.GetSystem<WeaponEquipmentSystem>().ClearForRiskLoss();
                this.GetSystem<ClothingEquipmentSystem>().ClearForRiskLoss();
                inventory.ClearPlayerBag();
                this.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.BaseSafe);
                var capturedBase = settled && (alreadySettled || save.CaptureBaseSections(baseRead.Data, out baseSaveError));
                var baseSaved = capturedBase && (alreadySettled || save.SaveBase(baseRead.Data, out baseSaveError));
                if (!baseSaved)
                {
                    // 基地写入失败时副本文件仍可重试，恢复死亡前运行态以免持久态与内存分裂。
                    save.RestoreRuntimeSnapshot(beforeDeathRuntime, out _);
                    return ExpeditionFlowResult.Fail(settlementError ?? baseSaveError ?? baseRead.Error ?? "基地结算失败");
                }
                var deleted = baseSaved && save.DeleteExpeditionFiles(out deleteError);
                if (!deleted)
                    return ExpeditionFlowResult.Fail(settlementError ?? baseSaveError ?? deleteError ?? baseRead.Error ?? "基地结算失败");
                expedition.Clear();
                this.GetSystem<ExpeditionTaskSystem>()?.Clear();
                if (!save.CommitBaseRuntime(baseRead.Data, out var commitError))
                    return ExpeditionFlowResult.Fail(commitError);
                var load = await this.GetSystem<MapSceneSystem>().LoadBaseMapAsync(baseRead.Data);
                if (!load.Success)
                {
                    this.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(AppState.Paused);
                    UIHelper.ShowUI<StartUICtrl>();
                    return ExpeditionFlowResult.Fail(load.Error);
                }

                this.SendEvent(new ExpeditionChangedEvent());
                return ExpeditionFlowResult.Ok();
            }
            finally
            {
                busy = false;
            }
        }

        private async UniTask<ExpeditionFlowResult> EnterExpeditionAsync(
            string mapId,
            MapKind expectedKind)
        {
            if (HasPendingEntryRecovery)
                return ExpeditionFlowResult.Fail("上次进入失败尚未恢复，请先重试恢复基地或返回主菜单");
            if (busy)
            {
                return ExpeditionFlowResult.Fail("远征流程正忙");
            }

            if (!MapCatalog.TryGet(mapId, out var definition) ||
                definition.Kind != expectedKind)
            {
                return ExpeditionFlowResult.Fail($"未知{expectedKind}地图：{mapId}");
            }

            if(!this.GetSystem<CarryLoadSystem>().TryCheckDeparture(mapId,out var preparationError))
                return ExpeditionFlowResult.Fail(preparationError);

            if (!definition.UnlockedByDefault &&
                !this.GetModel<BaseMapProgressModel>().IsMapUnlocked(mapId))
            {
                return ExpeditionFlowResult.Fail($"地图尚未解锁：{mapId}");
            }

            // 直接领域调用也必须在改变风险状态前拒绝未准备的可选地图。
            var resources = this.GetSystem<OptionalGameplayResourceSystem>();
            if (mapId != MapIds.CivilisationValley && resources != null &&
                resources.QueryMap(mapId).State != OptionalGameplayResourceState.Ready)
                return ExpeditionFlowResult.Fail("地图资源尚未就绪，请先完成资源下载");

            // 配置读取必须早于风险状态、地图和候选文件变更；成功空集合是合法远征。
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetExpeditionTasks(mapId, out var taskDefinitions, out var taskConfigError))
            {
                return ExpeditionFlowResult.Fail(taskConfigError);
            }
            ValleyGenerationProfile valleyProfile=null;
            if(mapId==MapIds.CivilisationValley&&!this.GetUtility<IRuntimeConfigProvider>().TryGetValleyGenerationProfile(mapId,out valleyProfile,out var valleyError))
                return ExpeditionFlowResult.Fail(valleyError);

            var inventory = this.GetSystem<InventorySystem>();
            if (inventory.RiskState == PlayerBagRiskState.RecoveryRequired)
            {
                if (!inventory.TryRetryRecovery(out var recoveryError))
                {
                    return ExpeditionFlowResult.Fail(recoveryError);
                }
            }

            if (inventory.RiskState != PlayerBagRiskState.BaseSafe)
            {
                return ExpeditionFlowResult.Fail("背包风险状态不允许进入远征");
            }

            if (this.GetModel<ExpeditionModel>().IsActive)
            {
                return ExpeditionFlowResult.Fail("当前已有远征");
            }

            if (this.GetModel<CombatModel>().PlayerHP.Value <= 0)
            {
                return ExpeditionFlowResult.Fail("生命尚未恢复，无法进入远征");
            }

            var save = this.GetSystem<SaveSystem>();
            if (save.InspectExpedition().Status != SaveFileStatus.Missing)
            {
                return ExpeditionFlowResult.Fail("已有待处理副本文件，不能创建新副本");
            }

            busy = true;
            var beforeBag = inventory.CapturePlayerBagSnapshot();
            var beforeRuntime = save.CaptureRuntimeSnapshot();
            var mapSystem = this.GetSystem<MapSceneSystem>();
            var startedFromBase = this.GetModel<MapSceneModel>().CurrentMapId == MapIds.Base &&
                                  mapSystem.HasRecoverableCurrentWorld();
            var candidateFileCreated = false;
            try
            {
                if (!inventory.TrySetPlayerBagRiskState(PlayerBagRiskState.EnteringExpedition, "entering expedition", true, out var enteringSaveError))
                {
                    inventory.RestorePlayerBagState(beforeBag, PlayerBagRiskState.BaseSafe);
                    return ExpeditionFlowResult.Fail(enteringSaveError);
                }

                var load = valleyProfile==null
                    ? await mapSystem.LoadMapAsync(mapId)
                    : await mapSystem.LoadNewValleyMapAsync(valleyProfile,Guid.NewGuid().GetHashCode());
                if (!load.Success)
                {
                    inventory.RestorePlayerBagState(beforeBag, PlayerBagRiskState.BaseSafe);
                    if (!inventory.TrySetPlayerBagRiskState(PlayerBagRiskState.BaseSafe, "enter expedition rollback", true, out var rollbackSaveError))
                    {
                        inventory.MarkRecoveryRequired(PlayerBagRecoveryKind.EnterRollback, "enter expedition rollback recovery required");
                        return ExpeditionFlowResult.Fail($"运行时操作失败：{load.Error}; {rollbackSaveError}");
                    }

                    // 准备失败仍保留旧基地时不重复加载；提交失败已释放基地时才执行恢复事务。
                    if (startedFromBase && !mapSystem.HasRecoverableCurrentWorld())
                    {
                        pendingEntryRecovery = beforeRuntime;
                        entryFailure = load.Error;
                        var recovery = await RestoreEntryBaseAsync();
                        if (!recovery.Success) return recovery;
                    }
                    return ExpeditionFlowResult.Fail(load.Error);
                }

                // 异步加载期间库存可能被其它入口改变；创建远征身份和文件前再次核对。
                if(!this.GetSystem<CarryLoadSystem>().TryCheckDeparture(mapId,out var loadedPreparationError))
                {
                    pendingEntryRecovery=beforeRuntime;
                    entryFailure=loadedPreparationError;
                    var recovery=await RestoreEntryBaseAsync();
                    return recovery.Success?ExpeditionFlowResult.Fail(loadedPreparationError):recovery;
                }
                var expedition = this.GetSystem<ExpeditionSystem>();
                var expeditionId = Guid.NewGuid().ToString("N");
                if (!expedition.TryBeginConfiguredExpedition(mapId, expeditionId, taskDefinitions, out var taskBeginError))
                {
                    await this.GetSystem<MapSceneSystem>().LoadBaseMapAsync(this.GetModel<SaveModel>().CurrentBase);
                    inventory.RestorePlayerBagState(beforeBag, PlayerBagRiskState.BaseSafe);
                    save.RestoreRuntimeSnapshot(beforeRuntime, out _);
                    return ExpeditionFlowResult.Fail(taskBeginError);
                }
                // 新趟开始才清空文明探索记录；恢复已有远征时保留存档中的工艺与调查点。
                if (mapId == MapIds.CivilisationValley) this.GetModel<CivilisationExpeditionModel>().Reset();
                var expeditionModel = this.GetModel<ExpeditionModel>();
                if(this.GetModel<MapSceneModel>().CurrentHandle is GeneratedValleyContentHandle generated)
                    expeditionModel.InstallValleyLayout(generated.Layout);
                if(expeditionModel.ValleyLayout?.GenerationVersion==3)
                {expeditionModel.QualifyReturn();this.GetModel<MapExplorationModel>()?.ForgetMap(mapId);}
                var expeditionData = expeditionModel.CaptureSave(expeditionId, DateTime.UtcNow);
                if (!save.CaptureExpeditionSections(expeditionData, out var captureError))
                {
                    await this.GetSystem<MapSceneSystem>().LoadBaseMapAsync(this.GetModel<SaveModel>().CurrentBase);
                    inventory.RestorePlayerBagState(beforeBag, PlayerBagRiskState.BaseSafe);
                    save.RestoreRuntimeSnapshot(beforeRuntime, out _);
                    return ExpeditionFlowResult.Fail(captureError);
                }
                if (!save.SaveExpedition(expeditionData, out var expeditionSaveError))
                {
                    await this.GetSystem<MapSceneSystem>().LoadBaseMapAsync(this.GetModel<SaveModel>().CurrentBase);
                    inventory.RestorePlayerBagState(beforeBag, PlayerBagRiskState.BaseSafe);
                    save.RestoreRuntimeSnapshot(beforeRuntime, out _);
                    return ExpeditionFlowResult.Fail(expeditionSaveError);
                }
                candidateFileCreated = true;
                this.GetModel<SaveModel>().SetExpedition(expeditionData);

                if (inventory.TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "expedition entered", true, out var inSaveError))
                {
                    this.SendEvent(new ExpeditionChangedEvent());
                    return ExpeditionFlowResult.Ok();
                }

                string cleanupError = null;
                var cleanupSucceeded = !candidateFileCreated || TryDiscardNewExpeditionCandidate(save, beforeRuntime, beforeBag, out cleanupError);
                if (!cleanupSucceeded)
                {
                    // The candidate file is still present (or its runtime rollback could not be
                    // restored).  Do not load the base map here: presenting that map as a
                    // completed return would hide a recoverable, unresolved expedition.
                    this.GetModel<ExpeditionModel>().Clear();
                    this.GetSystem<ExpeditionTaskSystem>()?.Clear();
                    inventory.MarkRecoveryRequired(PlayerBagRecoveryKind.EnterRollback, "enter expedition recovery required");
                    return ExpeditionFlowResult.Fail($"{inSaveError}；候选副本清理失败：{cleanupError}");
                }

                var baseLoad = await this.GetSystem<MapSceneSystem>().LoadBaseMapAsync(this.GetModel<SaveModel>().CurrentBase);
                if (baseLoad.Success)
                {
                    return ExpeditionFlowResult.Fail("进入远征已回滚：" + inSaveError);
                }

                inventory.MarkRecoveryRequired(PlayerBagRecoveryKind.EnterRollback, "enter expedition recovery required");
                var combinedError = string.IsNullOrWhiteSpace(cleanupError) ? inSaveError : $"{inSaveError}；候选副本清理失败：{cleanupError}";
                if (!baseLoad.Success) combinedError += $"；基地地图恢复失败：{baseLoad.Error}";
                return ExpeditionFlowResult.Fail(combinedError);
            }
            finally
            {
                busy = false;
            }
        }

        private bool TryDiscardNewExpeditionCandidate(
            SaveSystem save,
            SaveRuntimeSnapshot beforeRuntime,
            ItemContainerSnapshot beforeBag,
            out string error)
        {
            error = null;
            if (!save.DeleteExpeditionFiles(out var deleteError))
            {
                // There must not be an in-memory expedition which claims a failed candidate
                // was accepted.  Keep the files for recovery and report the deletion error.
                this.GetModel<ExpeditionModel>().Clear();
                error = deleteError;
                return false;
            }

            this.GetModel<ExpeditionModel>().Clear();
            this.GetSystem<ExpeditionTaskSystem>()?.Clear();
            if (!save.RestoreRuntimeSnapshot(beforeRuntime, out var restoreError))
            {
                error = restoreError;
                return false;
            }

            this.GetSystem<InventorySystem>().RestorePlayerBagState(beforeBag, PlayerBagRiskState.BaseSafe);
            return true;
        }
    }
}
