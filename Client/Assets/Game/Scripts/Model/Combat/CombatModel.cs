using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 统一保存玩家与敌人的战斗生命状态，并继续兼容既有玩家生命存档分区。
    /// </summary>
    public sealed class CombatModel : AbstractModel, IScopedSaveSectionProvider
    {
        private const string HPKey = "hp";
        private int playerMaxHealth;
        private int playerAttackDamage;
        /// <summary>配置到达前暂存存档血量；null 表示没有已保存的血量，0 表示明确死亡。</summary>
        private int? pendingRestoredPlayerHealth;
        private bool pendingFullHealthOnConfig;
        private readonly Dictionary<int, CombatantSnapshot> combatants =
            new Dictionary<int, CombatantSnapshot>();

        public string SectionId => "another-day.player-health";
        public SaveSectionScope Scope => SaveSectionScope.SharedPlayer;
        public int PlayerMaxHP => playerMaxHealth;
        public int PlayerAttackDamage => playerAttackDamage;
        public BindableProperty<int> PlayerHP { get; } =
            new BindableProperty<int>(0);
        public IReadOnlyDictionary<int, CombatantSnapshot> Combatants => combatants;

        protected override void OnInit()
        {
        }

        public void ApplyPlayerConfig(PlayerConfigDefinition config, bool preserveHP)
        {
            playerMaxHealth = Mathf.Max(1, config.MaxHP);
            playerAttackDamage = Mathf.Max(0, config.AttackDamage);
            if (pendingRestoredPlayerHealth.HasValue)
            {
                SetPlayerHPSilently(pendingRestoredPlayerHealth.Value);
                pendingRestoredPlayerHealth = null;
            }
            else if (pendingFullHealthOnConfig)
            {
                SetPlayerHPSilently(playerMaxHealth);
                pendingFullHealthOnConfig = false;
            }
            else if (!preserveHP)
            {
                SetPlayerHPSilently(playerMaxHealth);
            }
            else
            {
                SetPlayerHPSilently(PlayerHP.Value);
            }

            UpsertPlayerSnapshot();
        }

        public bool ApplyPlayerMaxHP(int value)
        {
            var next = Mathf.Max(1, value);
            if (playerMaxHealth == next)
            {
                return false;
            }

            playerMaxHealth = next;
            SetPlayerHPSilently(PlayerHP.Value);
            UpsertPlayerSnapshot();
            return true;
        }

        public bool SetPlayerHP(int value)
        {
            var changed = SetPlayerHPSilently(value);
            if (playerMaxHealth > 0)
            {
                UpsertPlayerSnapshot();
            }

            return changed;
        }

        public bool RegisterOrResetCombatant(
            int unitId,
            CombatFaction faction,
            int maxHP,
            int attackDamage,
            int targetUnitId = 0)
        {
            var safeMaximum = Mathf.Max(1, maxHP);
            combatants[unitId] = new CombatantSnapshot(
                unitId,
                faction,
                safeMaximum,
                safeMaximum,
                Mathf.Max(0, attackDamage),
                Mathf.Max(0, targetUnitId),
                false);
            return true;
        }

        internal bool TryAddCombatant(CombatantSnapshot snapshot)
        {
            if (combatants.ContainsKey(snapshot.UnitId))
            {
                return false;
            }

            combatants.Add(snapshot.UnitId, snapshot);
            return true;
        }

        public bool RemoveCombatant(int unitId)
        {
            return combatants.Remove(unitId);
        }

        public bool TryGetCombatant(int unitId, out CombatantSnapshot snapshot)
        {
            return combatants.TryGetValue(unitId, out snapshot);
        }

        public bool TrySetCombatantHealth(int unitId, int value, out CombatantSnapshot snapshot)
        {
            if (!combatants.TryGetValue(unitId, out snapshot))
            {
                return false;
            }

            var current = Mathf.Clamp(value, 0, snapshot.MaxHP);
            if (current == snapshot.HP)
            {
                return false;
            }

            snapshot = new CombatantSnapshot(
                snapshot.UnitId,
                snapshot.Faction,
                current,
                snapshot.MaxHP,
                snapshot.AttackDamage,
                snapshot.TargetUnitId,
                current <= 0);
            combatants[unitId] = snapshot;
            if (snapshot.Faction == CombatFaction.Player)
            {
                SetPlayerHPSilently(current);
            }

            return true;
        }

        public bool Validate(JToken data, out string error)
        {
            error = string.Empty;
            if (data == null)
            {
                return true;
            }

            var value = data[HPKey];
            if (value == null || value.Type == JTokenType.Integer)
            {
                return true;
            }

            error = "玩家生命存档字段无效";
            return false;
        }

        /// <summary>未取得角色配置且没有已恢复血量时不保存 hp；配置到达后按满血初始化，明确保存的零血量仍保留。</summary>
        public JToken Capture()
        {
            if (playerMaxHealth <= 0 && !pendingRestoredPlayerHealth.HasValue)
            {
                return new JObject();
            }

            return JObject.FromObject(new
            {
                hp = playerMaxHealth <= 0 ? pendingRestoredPlayerHealth.Value : PlayerHP.Value
            });
        }

        public void Restore(JToken data)
        {
            var value = data?[HPKey];
            if (value == null)
            {
                pendingRestoredPlayerHealth = null;
                if (playerMaxHealth > 0)
                {
                    pendingFullHealthOnConfig = false;
                    SetPlayerHPSilently(playerMaxHealth);
                }
                else
                {
                    pendingFullHealthOnConfig = true;
                    SetPlayerHPSilently(0);
                }
            }
            else
            {
                var restored = value.Value<int>();
                pendingRestoredPlayerHealth = restored;
                pendingFullHealthOnConfig = false;
                SetPlayerHPSilently(restored);
            }

            if (playerMaxHealth > 0)
            {
                UpsertPlayerSnapshot();
            }

            var architecture = ((IBelongToArchitecture)this).GetArchitecture();
            architecture?.SendEvent(new CombatHealthChangedEvent(
                PlayerModel.LocalPlayerUnitId,
                CombatFaction.Player,
                PlayerHP.Value,
                PlayerMaxHP));
        }

        public void Reset()
        {
            pendingRestoredPlayerHealth = null;
            pendingFullHealthOnConfig = playerMaxHealth <= 0;
            SetPlayerHPSilently(playerMaxHealth > 0 ? playerMaxHealth : 0);
            if (playerMaxHealth > 0)
            {
                UpsertPlayerSnapshot();
            }
            else
            {
                combatants.Remove(PlayerModel.LocalPlayerUnitId);
            }
        }

        private bool SetPlayerHPSilently(int value)
        {
            if (playerMaxHealth <= 0)
            {
                if (PlayerHP.Value == value)
                {
                    return false;
                }

                PlayerHP.Value = value;
                return true;
            }

            var clamped = Mathf.Clamp(value, 0, PlayerMaxHP);
            if (PlayerHP.Value == clamped)
            {
                return false;
            }

            PlayerHP.Value = clamped;
            return true;
        }

        private void UpsertPlayerSnapshot()
        {
            combatants[PlayerModel.LocalPlayerUnitId] = new CombatantSnapshot(
                PlayerModel.LocalPlayerUnitId,
                CombatFaction.Player,
                PlayerHP.Value,
                PlayerMaxHP,
                playerAttackDamage,
                0,
                PlayerHP.Value <= 0);
        }
    }
}
