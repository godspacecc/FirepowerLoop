using QFramework;
using UnityEngine;

namespace Game
{
    public sealed class PlayerConditionSystem : AbstractSystem
    {
        private int lastPowerDisplayValue;
        private int lastWaterDisplayValue;
        private float powerDepletionDamageAccumulator;
        private float waterDepletionDamageAccumulator;
        private bool sprintRestartThresholdRequired;
        private bool waitingForSprintRestartPress;
        private bool previousSprintHeld;
        private int observedRuntimeResetVersion;
        private ICharacterMotorPort playerPort;
        private int playerUnitId;
        private long playerMapInstanceId;

        protected override void OnInit()
        {
            if (!this.GetSystem<SaveSystem>()
                    .RegisterSection(this.GetModel<PlayerConditionModel>(), out var error))
            {
                Log.Error($"[PlayerConditionSystem] 玩家状态存档分区注册失败：{error}");
            }

            lastPowerDisplayValue = PowerDisplayValue();
            lastWaterDisplayValue = WaterDisplayValue();
            observedRuntimeResetVersion = this.GetModel<PlayerConditionModel>().RuntimeResetVersion;
            this.RegisterEvent<PlayerReadyEvent>(OnPlayerReady);
            this.RegisterEvent<UnitChangedEvent>(OnUnitChanged);
            this.RegisterEvent<UnitRemovedEvent>(OnUnitRemoved);
            this.RegisterEvent<PlayerConditionChangedEvent>(_ => RefreshTiliRing(false));
            LifeCycle.RegisterUpdate(OnUpdate);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(OnUpdate);
            ClearPlayerPort();
        }

        public void TickWorld(float realDeltaSeconds, AppState appState, string currentMapId)
        {
            if (!IsFinitePositive(realDeltaSeconds) || appState != AppState.Playing)
            {
                return;
            }

            SyncRuntimeResetIfNeeded();
            if (string.Equals(currentMapId, MapIds.Base, System.StringComparison.Ordinal))
            {
                AdvanceBase(realDeltaSeconds);
                return;
            }

            if (!MapCatalog.TryGet(currentMapId, out var map) ||
                map.Kind != MapKind.Survival &&
                map.Kind != MapKind.Task)
            {
                return;
            }

            var expedition = this.GetModel<ExpeditionModel>();
            if (!string.Equals(expedition.CurrentMapId, currentMapId, System.StringComparison.Ordinal) ||
                expedition.Status != ExpeditionStatus.Playing &&
                expedition.Status != ExpeditionStatus.ReturnQualified)
            {
                return;
            }

            AdvanceExploration(realDeltaSeconds * PlayerConditionRules.GameMinutesPerRealSecond, out _);
        }

        /// <summary>
        /// 响应显式时间推进指令，在游戏中或暂停时复用基地恢复与远征生存结算。
        /// 不恢复自动计时或切换应用状态；加载中及非活动远征拒绝推进，死亡沿用正式失败流程。
        /// </summary>
        /// <param name="gameMinutes">要推进的游戏分钟，必须为有限正数。</param>
        /// <param name="result">实际结算结果；远征死亡时推进量可能小于请求值。</param>
        /// <param name="error">校验失败原因；成功时为空。</param>
        public bool TryAdvanceGameMinutes(
            float gameMinutes,
            out PlayerConditionAdvanceResult result,
            out string error)
        {
            result = default;
            if (!IsFinitePositive(gameMinutes))
            {
                error = "游戏分钟 必须为正数";
                return false;
            }

            var appState = this.GetModel<AppStateModel>().CurrentState.Value;
            if (appState != AppState.Playing && appState != AppState.Paused)
            {
                error = "推进需要处于游戏中或暂停状态";
                return false;
            }

            SyncRuntimeResetIfNeeded();
            var mapId = this.GetModel<MapSceneModel>().CurrentMapId;
            if (string.Equals(mapId, MapIds.Base, System.StringComparison.Ordinal))
            {
                var realSeconds = gameMinutes / PlayerConditionRules.GameMinutesPerRealSecond;
                result = AdvanceBase(realSeconds);
                error = null;
                return true;
            }

            if (!MapCatalog.TryGet(mapId, out var map) ||
                map.Kind != MapKind.Survival &&
                map.Kind != MapKind.Task)
            {
                error = "推进需要基地或活动远征地图";
                return false;
            }

            var expedition = this.GetModel<ExpeditionModel>();
            if (!string.Equals(expedition.CurrentMapId, mapId, System.StringComparison.Ordinal) ||
                expedition.Status != ExpeditionStatus.Playing &&
                expedition.Status != ExpeditionStatus.ReturnQualified)
            {
                error = "推进需要活动远征";
                return false;
            }

            result = AdvanceExploration(gameMinutes, out _);
            error = null;
            return true;
        }

        public bool TryRestorePower(float requestedValue, out float appliedValue)
        {
            appliedValue = 0f;
            if (!IsFinitePositive(requestedValue))
            {
                return false;
            }

            SyncRuntimeResetIfNeeded();
            var model = this.GetModel<PlayerConditionModel>();
            if (model.Power >= model.MaxPower)
            {
                return false;
            }

            var before = model.Power;
            if (!model.SetPower(before + requestedValue))
            {
                return false;
            }

            appliedValue = model.Power - before;
            PublishConditionChanged(powerMayDirty: true, waterMayDirty: false, temperatureMayDirty: false, tiliMayDirty: false);
            return appliedValue > 0f;
        }

        public bool TryRestoreWater(float requestedValue, out float appliedValue)
        {
            appliedValue = 0f;
            if (!IsFinitePositive(requestedValue))
            {
                return false;
            }

            SyncRuntimeResetIfNeeded();
            var model = this.GetModel<PlayerConditionModel>();
            if (model.Water >= model.MaxWater)
            {
                return false;
            }

            var before = model.Water;
            if (!model.SetWater(before + requestedValue))
            {
                return false;
            }

            appliedValue = model.Water - before;
            PublishConditionChanged(powerMayDirty: false, waterMayDirty: true, temperatureMayDirty: false, tiliMayDirty: false);
            return appliedValue > 0f;
        }

        public bool TrySetPower(float value, out float before, out float after, out string error)
        {
            before = this.GetModel<PlayerConditionModel>().Power;
            after = before;
            if (!IsFiniteNonNegative(value) || value > PlayerConditionRules.MaxPower)
            {
                error = "体力 超出范围";
                return false;
            }

            SyncRuntimeResetIfNeeded();
            powerDepletionDamageAccumulator = 0f;
            var model = this.GetModel<PlayerConditionModel>();
            var changed = model.SetPower(value);
            after = model.Power;
            if (changed)
            {
                PublishConditionChanged(powerMayDirty: true, waterMayDirty: false, temperatureMayDirty: false, tiliMayDirty: false);
            }

            error = null;
            return true;
        }

        public bool TrySetTili(float value, out float before, out float after, out string error)
        {
            before = this.GetModel<PlayerConditionModel>().Tili;
            after = before;
            if (!IsFiniteNonNegative(value) || value > PlayerConditionRules.MaxTili)
            {
                error = "体力 超出范围";
                return false;
            }

            SyncRuntimeResetIfNeeded();
            var model = this.GetModel<PlayerConditionModel>();
            var changed = model.SetTili(value);
            if (value <= 0f)
            {
                changed |= model.SetSprintReleaseRequired(true);
                sprintRestartThresholdRequired = true;
                waitingForSprintRestartPress = false;
            }
            else
            {
                changed |= model.SetSprintReleaseRequired(false);
                sprintRestartThresholdRequired = value < PlayerConditionRules.SprintRestartThreshold;
                waitingForSprintRestartPress = false;
            }

            after = model.Tili;
            if (changed)
            {
                PublishConditionChanged(powerMayDirty: false, waterMayDirty: false, temperatureMayDirty: false, tiliMayDirty: true);
                RefreshTiliRing(false);
            }

            error = null;
            return true;
        }

        public bool TrySetWater(float value, out float before, out float after, out string error)
        {
            var model = this.GetModel<PlayerConditionModel>();
            before = model.Water;
            after = before;
            if (!IsFiniteNonNegative(value) || value > model.MaxWater)
            {
                error = "水分 超出范围";
                return false;
            }

            SyncRuntimeResetIfNeeded();
            waterDepletionDamageAccumulator = 0f;
            var changed = model.SetWater(value);
            after = model.Water;
            if (changed)
            {
                PublishConditionChanged(powerMayDirty: false, waterMayDirty: true, temperatureMayDirty: false, tiliMayDirty: false);
            }

            error = null;
            return true;
        }

        public bool TrySetMaxWater(float value, out float before, out float after, out string error)
        {
            var model = this.GetModel<PlayerConditionModel>();
            before = model.MaxWater;
            after = before;
            if (!IsFinitePositive(value))
            {
                error = "最大水分 必须为正数";
                return false;
            }

            SyncRuntimeResetIfNeeded();
            waterDepletionDamageAccumulator = 0f;
            var changed = model.SetMaxWater(value);
            after = model.MaxWater;
            if (changed)
            {
                PublishConditionChanged(powerMayDirty: false, waterMayDirty: true, temperatureMayDirty: false, tiliMayDirty: false);
            }

            error = null;
            return true;
        }

        public bool TrySetTemperature(float value, out float before, out float after, out string error)
        {
            var model = this.GetModel<PlayerConditionModel>();
            before = model.Temperature;
            after = before;
            if (!IsFinite(value) ||
                value < PlayerConditionRules.MinTemperature ||
                value > PlayerConditionRules.MaxTemperature)
            {
                error = "温度 超出范围";
                return false;
            }

            SyncRuntimeResetIfNeeded();
            var changed = model.SetTemperature(value);
            after = model.Temperature;
            if (changed)
            {
                PublishConditionChanged(powerMayDirty: false, waterMayDirty: false, temperatureMayDirty: true, tiliMayDirty: false);
            }

            error = null;
            return true;
        }

        /// <summary>判断新的加速按下意图是否可开始；耗尽后继续复用体力恢复阈值，不消耗体力。</summary>
        public bool CanStartSprint
        {
            get
            {
                var model = this.GetModel<PlayerConditionModel>();
                return model.Tili > 0f && !model.SprintReleaseRequired &&
                    (!(waitingForSprintRestartPress || sprintRestartThresholdRequired) ||
                     model.Tili >= PlayerConditionRules.SprintRestartThreshold);
            }
        }

        public SprintEvaluation TickTili(
            float realDeltaSeconds,
            bool hasMoveInput,
            bool sprintHeld)
        {
            if (!IsFinitePositive(realDeltaSeconds))
            {
                return new SprintEvaluation(false, 1f);
            }

            SyncRuntimeResetIfNeeded();
            var model = this.GetModel<PlayerConditionModel>();
            var changed = false;
            var sprintPressedThisFrame = sprintHeld && !previousSprintHeld;
            if (!sprintHeld && model.SprintReleaseRequired)
            {
                changed |= model.SetSprintReleaseRequired(false);
                waitingForSprintRestartPress = true;
            }

            var canSprint = hasMoveInput &&
                            sprintHeld &&
                            model.Tili > 0f &&
                            !model.SprintReleaseRequired &&
                            (!waitingForSprintRestartPress ||
                             sprintPressedThisFrame &&
                             model.Tili >= PlayerConditionRules.SprintRestartThreshold) &&
                            (!sprintRestartThresholdRequired ||
                             model.Tili >= PlayerConditionRules.SprintRestartThreshold);
            if (canSprint)
            {
                sprintRestartThresholdRequired = false;
                waitingForSprintRestartPress = false;
                var load=this.GetSystem<CarryLoadSystem>();
                var cost=load.AppliesInCurrentMap?load.Current.Sprint:1;
                changed |= model.SetTili(model.Tili - PlayerConditionRules.SprintTiliCostPerRealSecond * (float)cost * realDeltaSeconds);
                if (model.Tili <= 0f)
                {
                    changed |= model.SetSprintReleaseRequired(true);
                    sprintRestartThresholdRequired = true;
                    waitingForSprintRestartPress = false;
                }

                if (changed)
                {
                    PublishConditionChanged(powerMayDirty: false, waterMayDirty: false, temperatureMayDirty: false, tiliMayDirty: true);
                }

                previousSprintHeld = sprintHeld;
                RefreshTiliRing(true);
                return new SprintEvaluation(true, PlayerConditionRules.SprintSpeedMultiplier);
            }

            if (!(model.SprintReleaseRequired && sprintHeld))
            {
                var recovery = hasMoveInput
                    ? PlayerConditionRules.WalkingTiliRecoveryPerRealSecond
                    : PlayerConditionRules.StandingTiliRecoveryPerRealSecond;
                changed |= model.SetTili(model.Tili + recovery * realDeltaSeconds);
            }

            if (changed)
            {
                PublishConditionChanged(powerMayDirty: false, waterMayDirty: false, temperatureMayDirty: false, tiliMayDirty: true);
            }

            previousSprintHeld = sprintHeld;
            RefreshTiliRing(false);
            return new SprintEvaluation(false, 1f);
        }

        private void OnPlayerReady(PlayerReadyEvent evt)
        {
            playerUnitId = evt.UnitId;
            playerMapInstanceId = evt.MapInstanceId;
            if (!this.GetUtility<IPlayerRuntimePortProvider>().TryGetMotorPort(playerUnitId, playerMapInstanceId, out playerPort))
            {
                playerPort = null;
                Log.Error("[PlayerConditionSystem] 玩家体力环端口未绑定");
                return;
            }

            RefreshTiliRing(false);
        }

        private void OnUnitChanged(UnitChangedEvent evt)
        {
            if (evt.Current.UnitId == playerUnitId &&
                evt.Current.MapInstanceId == playerMapInstanceId &&
                (evt.ChangeMask & UnitChangeMask.Active) != 0 &&
                !evt.Current.IsActive)
            {
                ClearPlayerPort();
            }
        }

        private void OnUnitRemoved(UnitRemovedEvent evt)
        {
            if (evt.Snapshot.UnitId == playerUnitId && evt.Snapshot.MapInstanceId == playerMapInstanceId)
            {
                ClearPlayerPort();
            }
        }

        private void RefreshTiliRing(bool isSprinting)
        {
            if (playerPort == null || !playerPort.IsValid)
            {
                return;
            }

            var model = this.GetModel<PlayerConditionModel>();
            playerPort.SetTiliRing(
                model.Tili,
                model.MaxTili,
                isSprinting,
                model.SprintReleaseRequired || model.Tili <= 0f);
        }

        private void ClearPlayerPort()
        {
            if (playerPort != null && playerPort.IsValid)
            {
                playerPort.SetTiliRing(
                    PlayerConditionRules.MaxTili,
                    PlayerConditionRules.MaxTili,
                    false,
                    false);
            }

            playerPort = null;
            playerUnitId = 0;
            playerMapInstanceId = 0;
        }

        private void SyncRuntimeResetIfNeeded()
        {
            var model = this.GetModel<PlayerConditionModel>();
            if (observedRuntimeResetVersion == model.RuntimeResetVersion)
            {
                return;
            }

            powerDepletionDamageAccumulator = 0f;
            waterDepletionDamageAccumulator = 0f;
            sprintRestartThresholdRequired = false;
            waitingForSprintRestartPress = false;
            previousSprintHeld = false;
            lastPowerDisplayValue = PowerDisplayValue();
            lastWaterDisplayValue = WaterDisplayValue();
            observedRuntimeResetVersion = model.RuntimeResetVersion;
        }

        private PlayerConditionAdvanceResult AdvanceExploration(float gameMinutes, out bool deathClipped)
        {
            deathClipped = false;
            var model = this.GetModel<PlayerConditionModel>();
            var beforePower = model.Power;
            var beforeWater = model.Water;
            var currentPower = beforePower;
            var currentWater = beforeWater;
            var remaining = gameMinutes;
            var advanced = 0f;
            var powerDepletionDamage = 0;
            var waterDepletionDamage = 0;
            var progressCommitted = false;

            void CommitProgress()
            {
                if (progressCommitted)
                {
                    return;
                }

                this.GetSystem<ExpeditionSystem>().AdvanceGameMinutes(advanced);
                model.SetPower(currentPower);
                model.SetWater(currentWater);
                PublishConditionChanged(
                    powerMayDirty: !Mathf.Approximately(beforePower, model.Power),
                    waterMayDirty: !Mathf.Approximately(beforeWater, model.Water),
                    temperatureMayDirty: false,
                    tiliMayDirty: false);
                progressCommitted = true;
            }

            while (remaining > 0.0001f && this.GetModel<CombatModel>().PlayerHP.Value > 0)
            {
                var next = remaining;
                if (currentPower > 0f)
                {
                    next = Mathf.Min(next, currentPower / PlayerConditionRules.PowerDrainPerGameMinute);
                }

                if (currentWater > 0f)
                {
                    next = Mathf.Min(next, currentWater / PlayerConditionRules.WaterDrainPerGameMinute);
                }

                if (currentPower <= 0f)
                {
                    next = Mathf.Min(next, SecondsUntilIntegerDamage(
                        powerDepletionDamageAccumulator,
                        PlayerConditionRules.PowerDepletionDamagePerRealSecond));
                }

                if (currentWater <= 0f)
                {
                    next = Mathf.Min(next, SecondsUntilIntegerDamage(
                        waterDepletionDamageAccumulator,
                        PlayerConditionRules.WaterDepletionDamagePerRealSecond));
                }

                if (next <= 0.0001f)
                {
                    next = Mathf.Min(remaining, 0.0001f);
                }

                var powerWasDepleted = currentPower <= 0f;
                var waterWasDepleted = currentWater <= 0f;
                currentPower = Mathf.Max(0f, currentPower - next * PlayerConditionRules.PowerDrainPerGameMinute);
                currentWater = Mathf.Max(0f, currentWater - next * PlayerConditionRules.WaterDrainPerGameMinute);
                if (powerWasDepleted)
                {
                    powerDepletionDamageAccumulator += next * PlayerConditionRules.PowerDepletionDamagePerRealSecond;
                }

                if (waterWasDepleted)
                {
                    waterDepletionDamageAccumulator += next * PlayerConditionRules.WaterDepletionDamagePerRealSecond;
                }

                remaining -= next;
                advanced += next;

                if (WillAccumulatedDamageKillPlayer(powerDepletionDamageAccumulator))
                {
                    CommitProgress();
                }

                powerDepletionDamage += ApplyAccumulatedDamage(ref powerDepletionDamageAccumulator);
                if (this.GetModel<CombatModel>().PlayerHP.Value <= 0)
                {
                    deathClipped = remaining > 0.0001f;
                    break;
                }

                if (WillAccumulatedDamageKillPlayer(waterDepletionDamageAccumulator))
                {
                    CommitProgress();
                }

                waterDepletionDamage += ApplyAccumulatedDamage(ref waterDepletionDamageAccumulator);
                if (this.GetModel<CombatModel>().PlayerHP.Value <= 0)
                {
                    deathClipped = remaining > 0.0001f;
                    break;
                }
            }

            CommitProgress();
            return new PlayerConditionAdvanceResult(
                advanced > 0f,
                advanced,
                model.Power - beforePower,
                model.Water - beforeWater,
                powerDepletionDamage,
                waterDepletionDamage);
        }

        /// <summary>基地休息提交身体候选；保存与失败回滚由准备系统统一负责，不模拟整夜消耗。</summary>
        internal void RestoreForBaseRest()
        {
            var model=this.GetModel<PlayerConditionModel>();
            model.SetPower(model.MaxPower);model.SetWater(model.MaxWater);model.SetTili(model.MaxTili);
            model.SetTemperature(PlayerConditionRules.NormalTemperature);model.SetSprintReleaseRequired(true);
            sprintRestartThresholdRequired=false;waitingForSprintRestartPress=false;previousSprintHeld=true;
        }
        /// <summary>休息失败时恢复运行期冲刺保护；存档恢复已负责持久属性。</summary>
        internal System.Action CaptureRestRuntimeRollback()
        {
            bool threshold=sprintRestartThresholdRequired,waiting=waitingForSprintRestartPress,held=previousSprintHeld,release=this.GetModel<PlayerConditionModel>().SprintReleaseRequired;
            return ()=>{sprintRestartThresholdRequired=threshold;waitingForSprintRestartPress=waiting;previousSprintHeld=held;var model=this.GetModel<PlayerConditionModel>();model.SetSprintReleaseRequired(release);observedRuntimeResetVersion=model.RuntimeResetVersion;};
        }

        private PlayerConditionAdvanceResult AdvanceBase(float realSeconds)
        {
            var model = this.GetModel<PlayerConditionModel>();
            var alive = this.GetModel<CombatModel>().PlayerHP.Value > 0;
            var beforePower = model.Power;
            var beforeWater = model.Water;
            var beforeTemperature = model.Temperature;
            var changed = false;
            if (alive)
            {
                changed |= model.SetPower(model.Power + realSeconds * PlayerConditionRules.BasePowerRecoveryPerRealSecond);
                changed |= model.SetWater(model.Water + realSeconds * PlayerConditionRules.BaseWaterRecoveryPerRealSecond);
                var temperatureDelta = PlayerConditionRules.NormalTemperature - model.Temperature;
                if (!Mathf.Approximately(temperatureDelta, 0f))
                {
                    var step = Mathf.Sign(temperatureDelta) *
                               Mathf.Min(Mathf.Abs(temperatureDelta), realSeconds * PlayerConditionRules.BaseTemperatureRecoveryPerRealSecond);
                    changed |= model.SetTemperature(model.Temperature + step);
                }
            }

            if (changed)
            {
                PublishConditionChanged(
                    powerMayDirty: !Mathf.Approximately(beforePower, model.Power),
                    waterMayDirty: !Mathf.Approximately(beforeWater, model.Water),
                    temperatureMayDirty: !Mathf.Approximately(beforeTemperature, model.Temperature),
                    tiliMayDirty: false);
            }

            return new PlayerConditionAdvanceResult(
                changed,
                0f,
                model.Power - beforePower,
                model.Water - beforeWater,
                0,
                0);
        }

        private static float SecondsUntilIntegerDamage(float accumulator, float damagePerSecond)
        {
            if (damagePerSecond <= 0f)
            {
                return float.PositiveInfinity;
            }

            var fractional = accumulator - Mathf.Floor(accumulator);
            return Mathf.Max(0.0001f, (1f - fractional) / damagePerSecond);
        }

        private int ApplyAccumulatedDamage(ref float accumulator)
        {
            var damage = Mathf.FloorToInt(accumulator + 0.01f);
            if (damage <= 0)
            {
                return 0;
            }

            accumulator -= damage;
            return this.GetSystem<CombatSystem>()
                .TryApplyPlayerEnvironmentalDamage(damage, out var applied)
                ? applied
                : 0;
        }

        private bool WillAccumulatedDamageKillPlayer(float accumulator)
        {
            var damage = Mathf.FloorToInt(accumulator + 0.01f);
            return damage > 0 && damage >= this.GetModel<CombatModel>().PlayerHP.Value;
        }

        private void PublishConditionChanged(bool powerMayDirty, bool waterMayDirty, bool temperatureMayDirty, bool tiliMayDirty)
        {
            if (powerMayDirty)
            {
                var display = PowerDisplayValue();
                if (display != lastPowerDisplayValue)
                {
                    lastPowerDisplayValue = display;
                    this.GetSystem<SaveSystem>().MarkDirty("player power changed");
                }
            }

            if (waterMayDirty)
            {
                var display = WaterDisplayValue();
                if (display != lastWaterDisplayValue)
                {
                    lastWaterDisplayValue = display;
                }

                this.GetSystem<SaveSystem>().MarkDirty("player water changed");
            }

            if (temperatureMayDirty)
            {
                this.GetSystem<SaveSystem>().MarkDirty("player temperature changed");
            }

            if (tiliMayDirty)
            {
                this.GetSystem<SaveSystem>().MarkDirty("player tili changed");
            }

            this.SendEvent(new PlayerConditionChangedEvent());
        }

        private int PowerDisplayValue()
        {
            return Mathf.FloorToInt(this.GetModel<PlayerConditionModel>().Power);
        }

        private int WaterDisplayValue()
        {
            return Mathf.FloorToInt(this.GetModel<PlayerConditionModel>().Water);
        }

        private void OnUpdate()
        {
            TickWorld(
                Time.deltaTime,
                this.GetModel<AppStateModel>().CurrentState.Value,
                this.GetModel<MapSceneModel>().CurrentMapId);
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value) &&
                   value > 0f;
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return IsFinite(value) &&
                   value >= 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
