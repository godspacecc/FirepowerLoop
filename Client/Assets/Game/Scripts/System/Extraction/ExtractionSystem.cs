using System;
using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    /// <summary>
    /// 撤离资格、当前唯一撤离尝试、持续动作结束结果与既有返回事务之间的业务边界。
    /// 计时更新由 PlayerActionSystem 负责，View 表现由 Controller 负责；本系统不创建第二套任务结算或存档流程。
    /// </summary>
    public sealed class ExtractionSystem : AbstractSystem
    {
        public const float ExtractionDurationSeconds = 5f;

        // 仅在当前运行会话保存唯一尝试。四个身份字段用于拒绝重复开始并隔离晚到事件，
        // 不进入 ExtractionModel 或存档；ObservedHealth 是伤害比较基线，治疗后随当前生命上移。
        private ActiveExtractionAttempt activeAttempt;
        /// <summary>仅在读条完成并重新验证后、同一次返回事务内有效；不写入存档。</summary>
        private ActiveExtractionAttempt completedReturn;
        internal bool HasCompletedReturnAuthorization => completedReturn!=null &&
            TryValidateIdentity(completedReturn.ExtractionUnitId,completedReturn.PlayerUnitId,out var exit,out _,out _) &&
            exit.MapInstanceId==completedReturn.MapInstanceId && exit.PlayerInside && exit.Available &&
            this.GetModel<CombatModel>().PlayerHP.Value>0 && this.GetSystem<PlayerMoveSystem>()?.IsSwimming!=true;

        protected override void OnInit()
        {
            this.RegisterEvent<PlayerActionEndedEvent>(OnPlayerActionEnded);
            this.RegisterEvent<CombatHealthChangedEvent>(OnCombatHealthChanged);
        }

        protected override void OnDeinit() => CancelAttempt(PlayerActionCancelReason.PlayerUnavailable);

        public bool TryRegisterExtraction(int unitId, bool available, out string error)
        {
            if (unitId <= 0 || !this.GetSystem<UnitSystem>().TryGet(unitId, out var unit) ||
                unit.UnitType != UnitType.ExtractionPoint || !unit.IsActive ||
                string.IsNullOrWhiteSpace(unit.ConfigId))
            {
                error = "撤离点注册失败：Unit 无效";
                return false;
            }

            var model = this.GetModel<ExtractionModel>();
            if (model.TryGet(unitId, out _))
            {
                error = "撤离点注册失败：记录已存在";
                return false;
            }

            model.Set(new ExtractionSnapshot(unit.UnitId, unit.MapInstanceId, available, false));
            this.SendEvent(new ExtractionCreatedEvent(unit.UnitId, unit.MapInstanceId, available));
            error = null;
            return true;
        }

        public bool TryRemoveExtraction(int unitId)
        {
            var model = this.GetModel<ExtractionModel>();
            if (!model.TryGet(unitId, out var snapshot)) return false;
            if (activeAttempt != null && activeAttempt.ExtractionUnitId == unitId)
                CancelAttempt(PlayerActionCancelReason.PlayerUnavailable);
            if (!model.Remove(unitId)) return false;
            this.SendEvent(new ExtractionRemovedEvent(unitId, snapshot.MapInstanceId));
            return true;
        }

        public bool TrySetAvailable(int unitId, bool available)
        {
            var model = this.GetModel<ExtractionModel>();
            if (!model.TryGet(unitId, out var snapshot)) return false;
            if (snapshot.Available == available) return true;
            model.Set(snapshot.WithAvailable(available));
            if (!available && activeAttempt != null && activeAttempt.ExtractionUnitId == unitId)
                CancelAttempt(PlayerActionCancelReason.ContextInvalid);
            this.SendEvent(new ExtractionAvailabilityChangedEvent(unitId, available));
            return true;
        }

        public bool TrySetPlayerInRange(int extractionUnitId, int playerUnitId, bool inside, out string error)
        {
            if (!TryValidateIdentity(extractionUnitId, playerUnitId, out var extraction, out _, out error))
                return false;
            if (extraction.PlayerInside != inside)
                this.GetModel<ExtractionModel>().Set(extraction.WithPlayerInside(inside));
            if (!inside && activeAttempt != null && activeAttempt.ExtractionUnitId == extractionUnitId &&
                activeAttempt.PlayerUnitId == playerUnitId)
                CancelAttempt(PlayerActionCancelReason.LeftRange);
            error = null;
            return true;
        }

        public bool TryBeginExtraction(int extractionUnitId, int playerUnitId, out long actionId, out string error)
        {
            actionId = 0;
            var actions = this.GetSystem<PlayerActionSystem>();
            if (activeAttempt != null ||
                actions.TryGetSnapshot(out var current) && current.Kind == PlayerActionKind.Extraction)
            {
                error = "已有撤离动作正在进行";
                return false;
            }

            if (!TryValidateBegin(extractionUnitId, playerUnitId, out var extraction, out _, out error))
                return false;
            var health = this.GetModel<CombatModel>().PlayerHP.Value;
            if (!actions.TryBegin(new PlayerActionRequest(PlayerActionKind.Extraction,
                    ExtractionDurationSeconds, string.Empty), out actionId, out error))
                return false;

            activeAttempt = new ActiveExtractionAttempt(extractionUnitId, playerUnitId,
                extraction.MapInstanceId, actionId, health);
            this.SendEvent(new ExtractionCountdownStartedEvent(extractionUnitId, playerUnitId,
                extraction.MapInstanceId, actionId, ExtractionDurationSeconds));
            return true;
        }

        public async UniTask<ExpeditionFlowResult> TryReturnToBaseAsync(int extractionUnitId, int playerUnitId)
        {
            if(this.GetModel<ExpeditionModel>().ValleyLayout?.GenerationVersion==3 &&
                (completedReturn==null || completedReturn.ExtractionUnitId!=extractionUnitId || completedReturn.PlayerUnitId!=playerUnitId || !HasCompletedReturnAuthorization))
                return ExpeditionFlowResult.Fail("须到达实体撤离点并完成读条");
            if (!TryValidateIdentity(extractionUnitId, playerUnitId, out var extraction, out _, out var error))
                return ExpeditionFlowResult.Fail(error);
            if (!extraction.Available) return ExpeditionFlowResult.Fail("撤离点暂不可用");
            if (extraction.Returning) return ExpeditionFlowResult.Fail("撤离点正在返回中");
            var model = this.GetModel<ExtractionModel>();
            model.Set(extraction.WithReturning(true));
            try
            {
                this.SendEvent(new ExtractionReturnStartedEvent(extractionUnitId, playerUnitId));
                var result = await this.GetSystem<ExpeditionFlowSystem>().ReturnToBaseAsync();
                if (!result.Success && model.TryGet(extractionUnitId, out var failed))
                    model.Set(failed.WithReturning(false));
                return result;
            }
            catch (Exception e)
            {
                if (model.TryGet(extractionUnitId, out var failed)) model.Set(failed.WithReturning(false));
                return ExpeditionFlowResult.Fail($"撤离返回异常：{e.Message}");
            }
        }

        private bool TryValidateIdentity(int extractionUnitId, int playerUnitId,
            out ExtractionSnapshot extraction, out UnitSnapshot player, out string error)
        {
            extraction = default;
            player = default;
            if (!this.GetModel<ExtractionModel>().TryGet(extractionUnitId, out extraction))
            { error = "撤离点不存在"; return false; }
            var units = this.GetSystem<UnitSystem>();
            if (!units.TryGet(extractionUnitId, out var exitUnit) ||
                exitUnit.UnitType != UnitType.ExtractionPoint || !exitUnit.IsActive ||
                exitUnit.MapInstanceId != extraction.MapInstanceId)
            { error = "撤离点 Unit 无效"; return false; }
            if (playerUnitId != PlayerModel.LocalPlayerUnitId || !units.TryGet(playerUnitId, out player) ||
                player.UnitType != UnitType.Player || !player.IsActive ||
                player.MapInstanceId != extraction.MapInstanceId)
            { error = "本地玩家 Unit 无效或不在同一地图"; return false; }
            error = null;
            return true;
        }

        private bool TryValidateBegin(int extractionUnitId, int playerUnitId,
            out ExtractionSnapshot extraction, out UnitSnapshot player, out string error)
        {
            if (!TryValidateIdentity(extractionUnitId, playerUnitId, out extraction, out player, out error))
                return false;
            if (!extraction.Available || extraction.Returning || !extraction.PlayerInside)
            { error = "撤离点当前不可交互"; return false; }
            if(this.GetSystem<PlayerMoveSystem>()?.IsSwimming==true) {error="游泳时不能撤离";return false;}
            var expedition = this.GetModel<ExpeditionModel>();
            if (this.GetModel<CombatModel>().PlayerHP.Value <= 0 ||
                expedition.Status == ExpeditionStatus.Defeated || !expedition.CanReturn ||
                this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing)
            { error = "当前状态不允许撤离"; return false; }
            error = null;
            return true;
        }

        private void OnPlayerActionEnded(PlayerActionEndedEvent evt)
        {
            var attempt = activeAttempt;
            if (attempt == null || evt.Kind != PlayerActionKind.Extraction || evt.ActionId != attempt.ActionId)
                return;

            // 先认领并释放唯一尝试的占用，再复核完成时刻的身份、资格与范围。
            // 只有复核通过才会异步进入既有返回事务，晚到或失效事件不能启动返回。
            activeAttempt = null;
            if (!evt.Completed)
            {
                PublishCanceled(attempt, evt.CancelReason);
                return;
            }
            if (!TryValidateBegin(attempt.ExtractionUnitId, attempt.PlayerUnitId,
                    out _, out _, out _))
            {
                PublishCanceled(attempt, PlayerActionCancelReason.ContextInvalid);
                return;
            }
            CompleteExtractionAsync(attempt).Forget();
        }

        private async UniTaskVoid CompleteExtractionAsync(ActiveExtractionAttempt attempt)
        {
            // 返回失败沿用 TryReturnToBaseAsync 的 Returning 复位，并只通知表现恢复；
            // 这里不建立第二条结算、保存或地图切换路径。
            ExpeditionFlowResult result;
            completedReturn=attempt;
            try {result=await TryReturnToBaseAsync(attempt.ExtractionUnitId, attempt.PlayerUnitId);}
            finally {completedReturn=null;}
            if (result.Success) return;
            Log.Error($"[ExtractionSystem] 返回基地失败：{result.Error}");
            if (this.GetModel<ExtractionModel>().TryGet(attempt.ExtractionUnitId, out var current) &&
                current.MapInstanceId == attempt.MapInstanceId)
                this.SendEvent(new ExtractionReturnFailedEvent(attempt.ExtractionUnitId,
                    attempt.PlayerUnitId, attempt.MapInstanceId));
        }

        private void OnCombatHealthChanged(CombatHealthChangedEvent evt)
        {
            var attempt = activeAttempt;
            // 生命归零由随后发布的 CombatantDiedEvent 经 PlayerActionSystem 统一取消，避免再显示受伤提示。
            // 正生命下降才按 Damaged 取消；治疗上移比较基线，数值不变不会取消。
            if (attempt == null || evt.UnitId != attempt.PlayerUnitId || evt.Faction != CombatFaction.Player ||
                evt.Current <= 0) return;
            if (evt.Current < attempt.ObservedHealth)
            {
                CancelAttempt(PlayerActionCancelReason.Damaged);
                return;
            }
            attempt.ObservedHealth = evt.Current;
        }

        private void CancelAttempt(PlayerActionCancelReason reason)
        {
            var attempt = activeAttempt;
            if (attempt == null) return;
            var actions = this.GetSystem<PlayerActionSystem>();
            if (actions.TryGetSnapshot(out var action) && action.Kind == PlayerActionKind.Extraction &&
                action.ActionId == attempt.ActionId)
            {
                actions.CancelActive(reason);
                return;
            }
            activeAttempt = null;
            PublishCanceled(attempt, reason);
        }

        private void PublishCanceled(ActiveExtractionAttempt attempt, PlayerActionCancelReason reason)
        {
            this.SendEvent(new ExtractionCountdownCanceledEvent(attempt.ExtractionUnitId,
                attempt.PlayerUnitId, attempt.MapInstanceId, attempt.ActionId, reason));
        }

        /// <summary>
        /// 当前运行会话中的唯一撤离尝试。身份与 ActionId 只用于事件归属和晚到隔离；
        /// ObservedHealth 随治疗上移，作为本次尝试判断后续实际生命下降的基线。
        /// </summary>
        private sealed class ActiveExtractionAttempt
        {
            public ActiveExtractionAttempt(int extractionUnitId, int playerUnitId, long mapInstanceId,
                long actionId, int observedHealth)
            {
                ExtractionUnitId = extractionUnitId; PlayerUnitId = playerUnitId;
                MapInstanceId = mapInstanceId; ActionId = actionId; ObservedHealth = observedHealth;
            }
            public int ExtractionUnitId { get; }
            public int PlayerUnitId { get; }
            public long MapInstanceId { get; }
            public long ActionId { get; }
            public int ObservedHealth { get; set; }
        }
    }
}
