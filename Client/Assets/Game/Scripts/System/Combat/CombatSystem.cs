using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 管理战斗生命变化、玩家攻击、基地恢复和死亡事件，生命数据统一写入 CombatModel。
    /// </summary>
    public sealed class CombatSystem : AbstractSystem
    {
        private float baseRecoveryPerSecond;
        private float recoveryAccumulator;
        private bool playerDeathPublished;
        private ICharacterMotorPort playerPort;
        private int playerUnitId;
        private long playerMapInstanceId;

        public int PlayerHP => this.GetModel<CombatModel>().PlayerHP.Value;
        public int PlayerMaxHP => this.GetModel<CombatModel>().PlayerMaxHP;

        protected override void OnInit()
        {
            var combat = this.GetModel<CombatModel>();
            var registered = this.GetSystem<SaveSystem>()
                .RegisterSection(combat, out var error);
            if (!registered)
            {
                Log.Error($"[CombatSystem] 战斗存档分区注册失败：{error}");
            }

            this.RegisterEvent<PlayerReadyEvent>(OnPlayerReady);
            LifeCycle.RegisterUpdate(OnUpdate);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(OnUpdate);
            playerPort = null;
        }

        public bool TryGetCombatant(int unitId, out CombatantSnapshot snapshot)
        {
            return this.GetModel<CombatModel>().TryGetCombatant(unitId, out snapshot);
        }

        public bool TryRegisterEnemy(int unitId, EnemyConfigDefinition config, out string error)
        {
            if (config.MaxHP <= 0 || config.AttackDamage < 0)
            {
                error = "敌人战斗注册失败：配置数值无效";
                return false;
            }

            if (!this.GetSystem<UnitSystem>().TryGet(unitId, out var unit))
            {
                error = "敌人战斗注册失败：Unit 不存在";
                return false;
            }

            if (unit.UnitType != UnitType.Enemy)
            {
                error = "敌人战斗注册失败：Unit 类型不是 Enemy";
                return false;
            }

            if (!unit.IsActive)
            {
                error = "敌人战斗注册失败：Unit 未激活";
                return false;
            }

            if (unit.ConfigId != config.Id)
            {
                error = "敌人战斗注册失败：ConfigId 不匹配";
                return false;
            }

            var snapshot = new CombatantSnapshot(
                unitId,
                CombatFaction.Enemy,
                config.MaxHP,
                config.MaxHP,
                config.AttackDamage,
                PlayerModel.LocalPlayerUnitId,
                false);
            if (!this.GetModel<CombatModel>().TryAddCombatant(snapshot))
            {
                error = "敌人战斗注册失败：Combatant 已存在";
                return false;
            }

            error = null;
            return true;
        }

        public bool TryDamage(int attackerUnitId, int targetUnitId)
        {
            var combat = this.GetModel<CombatModel>();
            return combat.TryGetCombatant(attackerUnitId, out var attacker) &&
                   TryDamage(attackerUnitId, targetUnitId, attacker.AttackDamage);
        }

        public bool TryDamage(int attackerUnitId, int targetUnitId, int damage)
        {
            var combat = this.GetModel<CombatModel>();
            if (attackerUnitId == targetUnitId ||
                damage <= 0 ||
                !this.GetSystem<UnitSystem>().TryGet(attackerUnitId, out var attackerUnit) ||
                !this.GetSystem<UnitSystem>().TryGet(targetUnitId, out var targetUnit) ||
                !attackerUnit.IsActive ||
                !targetUnit.IsActive ||
                attackerUnit.MapInstanceId <= 0 ||
                attackerUnit.MapInstanceId != targetUnit.MapInstanceId ||
                !combat.TryGetCombatant(attackerUnitId, out var attacker) ||
                !combat.TryGetCombatant(targetUnitId, out var before) ||
                attacker.Faction == before.Faction ||
                attacker.IsDead ||
                before.IsDead)
            {
                return false;
            }

            this.SendEvent(new CombatDamageRequestedEvent(attackerUnitId, targetUnitId, damage));
            if (!combat.TrySetCombatantHealth(targetUnitId, before.HP - damage, out var after))
            {
                return false;
            }

            var applied = before.HP - after.HP;
            this.SendEvent(new CombatDamageAppliedEvent(attackerUnitId, targetUnitId, applied));
            this.SendEvent(new CombatHealthChangedEvent(
                targetUnitId,
                after.Faction,
                after.HP,
                after.MaxHP));
            if (after.Faction == CombatFaction.Player)
            {
                PublishPlayerTargetSideEffects(before, after);
            }

            if (!after.IsDead)
            {
                return true;
            }

            this.SendEvent(new CombatantDiedEvent(
                targetUnitId,
                attackerUnitId));
            return true;
        }

        public bool TryRemoveCombatant(int unitId)
        {
            return this.GetModel<CombatModel>().RemoveCombatant(unitId);
        }

        public bool TryRestorePlayerHealth(int requestedValue, out int appliedValue)
        {
            appliedValue = 0;
            var combat = this.GetModel<CombatModel>();
            if (requestedValue <= 0 ||
                combat.PlayerHP.Value <= 0 ||
                combat.PlayerMaxHP <= 0 ||
                combat.PlayerHP.Value >= combat.PlayerMaxHP)
            {
                return false;
            }

            var before = combat.PlayerHP.Value;
            var target = Mathf.Min(combat.PlayerMaxHP, before + requestedValue);
            if (target <= before)
            {
                return false;
            }

            ApplyPlayerHealth(target);
            appliedValue = combat.PlayerHP.Value - before;
            return appliedValue > 0;
        }

        /// <summary>休息先提交生命候选，不发布领域事件；准备系统负责保存或恢复快照。</summary>
        internal void PrepareBaseRestHealth()=>this.GetModel<CombatModel>().SetPlayerHP(PlayerMaxHP);
        /// <summary>休息保存成功后恢复存活表现并发布健康状态，监听异常不撤销已落盘的事务。</summary>
        internal void CompleteBaseRestHealth()
        {
            playerDeathPublished=false;recoveryAccumulator=0;playerPort?.SetAlivePresentation(true);
            this.SendEvent(new CombatHealthChangedEvent(PlayerModel.LocalPlayerUnitId,CombatFaction.Player,PlayerHP,PlayerMaxHP));
            this.SendEvent(new ExpeditionChangedEvent());
        }
        public bool TrySetPlayerHP(int value, out int before, out int after, out string error)
        {
            error = null;
            var combat = this.GetModel<CombatModel>();
            before = combat.PlayerHP.Value;
            after = before;
            if (value < 0 || value > combat.PlayerMaxHP)
            {
                error = "生命值超出范围";
                return false;
            }

            ApplyPlayerHealth(value);
            after = combat.PlayerHP.Value;
            return true;
        }

        public bool TryApplyPlayerMaxHP(int value, out int before, out int after, out string error)
        {
            error = null;
            before = this.GetModel<CombatModel>().PlayerMaxHP;
            after = before;
            if (value <= 0)
            {
                error = "最大生命值 必须为正数";
                return false;
            }

            var combat = this.GetModel<CombatModel>();
            var hpBefore = combat.PlayerHP.Value;
            var changed = combat.ApplyPlayerMaxHP(value);
            after = combat.PlayerMaxHP;
            if (!changed)
            {
                return true;
            }

            this.SendEvent(new CombatHealthChangedEvent(
                PlayerModel.LocalPlayerUnitId,
                CombatFaction.Player,
                combat.PlayerHP.Value,
                combat.PlayerMaxHP));
            this.SendEvent(new ExpeditionChangedEvent());
            this.GetSystem<SaveSystem>().MarkDirty("player maxhp changed");
            if (hpBefore > 0 && combat.PlayerHP.Value == 0 && !playerDeathPublished)
            {
                playerDeathPublished = true;
                this.SendEvent(new CombatantDiedEvent(
                    PlayerModel.LocalPlayerUnitId,
                    0));
            }

            return true;
        }

        public bool TryApplyPlayerEnvironmentalDamage(
            int requestedValue,
            out int appliedValue)
        {
            appliedValue = 0;
            var combat = this.GetModel<CombatModel>();
            if (requestedValue <= 0 ||
                combat.PlayerMaxHP <= 0 ||
                combat.PlayerHP.Value <= 0 ||
                !combat.TryGetCombatant(PlayerModel.LocalPlayerUnitId, out var before) ||
                before.Faction != CombatFaction.Player ||
                before.IsDead)
            {
                return false;
            }

            var target = Mathf.Max(0, before.HP - requestedValue);
            if (!combat.TrySetCombatantHealth(PlayerModel.LocalPlayerUnitId, target, out var after))
            {
                return false;
            }

            appliedValue = before.HP - after.HP;
            if (appliedValue <= 0)
            {
                return false;
            }

            this.SendEvent(new CombatHealthChangedEvent(
                PlayerModel.LocalPlayerUnitId,
                CombatFaction.Player,
                after.HP,
                after.MaxHP));
            playerPort?.PlayHurtFeedback();
            if (after.IsDead)
            {
                playerPort?.SetAlivePresentation(false);
            }
            this.SendEvent(new ExpeditionChangedEvent());
            this.GetSystem<SaveSystem>().MarkDirty("player health changed");
            if (after.HP > 0)
            {
                playerDeathPublished = false;
                return true;
            }

            if (!playerDeathPublished)
            {
                playerDeathPublished = true;
                this.SendEvent(new CombatantDiedEvent(
                    PlayerModel.LocalPlayerUnitId,
                    0));
            }

            return true;
        }

        public void TickBaseRecovery(
            float deltaTime,
            AppState appState,
            string currentMapId)
        {
            if (deltaTime <= 0f ||
                appState != AppState.Playing ||
                currentMapId != MapIds.Base ||
                PlayerMaxHP <= 0 ||
                baseRecoveryPerSecond <= 0f ||
                PlayerHP >= PlayerMaxHP)
            {
                recoveryAccumulator = 0f;
                return;
            }

            recoveryAccumulator += deltaTime * baseRecoveryPerSecond;
            var recovered = Mathf.FloorToInt(recoveryAccumulator);
            if (recovered <= 0)
            {
                return;
            }

            recoveryAccumulator -= recovered;
            ApplyPlayerHealth(PlayerHP + recovered);
        }

        private void ApplyPlayerHealth(int value)
        {
            var combat = this.GetModel<CombatModel>();
            var wasDead = combat.PlayerHP.Value <= 0;
            if (!combat.SetPlayerHP(value))
            {
                return;
            }

            this.SendEvent(new CombatHealthChangedEvent(
                PlayerModel.LocalPlayerUnitId,
                CombatFaction.Player,
                combat.PlayerHP.Value,
                combat.PlayerMaxHP));
            this.SendEvent(new ExpeditionChangedEvent());
            this.GetSystem<SaveSystem>().MarkDirty("player health changed");

            if (combat.PlayerHP.Value > 0)
            {
                playerDeathPublished = false;
                if (wasDead)
                {
                    playerPort?.SetAlivePresentation(true);
                }
            }

            if (combat.PlayerHP.Value == 0 && !playerDeathPublished && !wasDead)
            {
                playerDeathPublished = true;
                this.SendEvent(new CombatantDiedEvent(
                    PlayerModel.LocalPlayerUnitId,
                    0));
            }
        }

        private void OnPlayerReady(PlayerReadyEvent evt)
        {
            playerUnitId = evt.UnitId;
            playerMapInstanceId = evt.MapInstanceId;
            if (this.GetSystem<UnitSystem>().TryGet(playerUnitId, out var snapshot))
            {
                RefreshPlayerConfig(snapshot.ConfigId, true);
            }

            this.GetUtility<IPlayerRuntimePortProvider>().TryGetMotorPort(playerUnitId, playerMapInstanceId, out playerPort);
            var combat = this.GetModel<CombatModel>();
            playerDeathPublished = combat.PlayerHP.Value <= 0;
        }

        private void OnUpdate()
        {
            var deltaTime = Time.deltaTime;
            TickBaseRecovery(
                deltaTime,
                this.GetModel<AppStateModel>().CurrentState.Value,
                this.GetModel<MapSceneModel>().CurrentMapId);

            if (this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing ||
                playerPort == null)
            {
                return;
            }

            playerPort.TickCombatFeedback(deltaTime);
        }

        private void PublishPlayerTargetSideEffects(CombatantSnapshot before, CombatantSnapshot after)
        {
            if (before.HP == after.HP)
            {
                return;
            }

            playerPort?.PlayHurtFeedback();
            if (after.IsDead)
            {
                playerPort?.SetAlivePresentation(false);
            }
            this.SendEvent(new ExpeditionChangedEvent());
            this.GetSystem<SaveSystem>().MarkDirty("player health changed");
            if (after.HP > 0)
            {
                playerDeathPublished = false;
            }
        }

        private void RefreshPlayerConfig(string configId, bool preserveHP)
        {
            var error = string.Empty;
            if (string.IsNullOrWhiteSpace(configId) ||
                !this.GetUtility<IRuntimeConfigProvider>().TryGetPlayer(configId, out var config, out error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Log.Error($"[CombatSystem] 玩家战斗配置读取失败：{error}");
                }

                return;
            }

            if (this.GetModel<PlayerAttributeModel>().MaxHPOverride.HasValue)
            {
                config = new PlayerConfigDefinition(
                    config.Id,
                    config.PrefabAddress,
                    this.GetModel<PlayerAttributeModel>().MaxHPOverride.Value,
                    config.AttackDamage,
                    config.AttackRange,
                    config.AttackCooldownSeconds,
                    config.MoveSpeed,
                    config.TurnSpeed,
                    config.BaseRecoveryPerSecond);
            }

            this.GetModel<CombatModel>().ApplyPlayerConfig(config, preserveHP);
            baseRecoveryPerSecond = config.BaseRecoveryPerSecond;
        }
    }
}
