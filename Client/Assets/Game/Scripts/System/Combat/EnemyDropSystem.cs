using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 保证每个敌人死亡事件只请求一次掉落，并在地图切换后重置去重状态。
    /// </summary>
    public sealed class EnemyDropRule
    {
        private readonly HashSet<int> droppedEnemyIds = new HashSet<int>();

        public bool TryHandle(
            CombatantDiedEvent evt,
            bool canCreateDrop,
            long mapInstanceId,
            IReadOnlyList<LootDropBatchEntry> entries,
            Vector3 position,
            Quaternion rotation,
            out LootDropBatchRequestedEvent request)
        {
            request = default;
            if (!canCreateDrop ||
                mapInstanceId <= 0 ||
                entries == null ||
                entries.Count == 0 ||
                !droppedEnemyIds.Add(evt.UnitId))
            {
                return false;
            }

            request = new LootDropBatchRequestedEvent(mapInstanceId, evt.UnitId, position, rotation, entries);
            return true;
        }

        public bool TryHandle(
            CombatantDiedEvent evt,
            bool canCreateDrop,
            long mapInstanceId,
            string lootConfigId,
            Vector3 position,
            Quaternion rotation,
            out LootDropRequestedEvent request)
        {
            request = default;
            if (!canCreateDrop ||
                mapInstanceId <= 0 ||
                string.IsNullOrWhiteSpace(lootConfigId) ||
                !droppedEnemyIds.Add(evt.UnitId))
            {
                return false;
            }

            request = new LootDropRequestedEvent(mapInstanceId, lootConfigId, position, rotation);
            return true;
        }

        public void Reset()
        {
            droppedEnemyIds.Clear();
        }
    }

    /// <summary>
    /// 监听敌人死亡并按敌人 UnitId 去重发布掉落请求，不创建 Unity Prefab。
    /// </summary>
    public sealed class EnemyDropSystem : AbstractSystem
    {
        private EnemyDropRule rule;

        protected override void OnInit()
        {
            rule = new EnemyDropRule();
            this.RegisterEvent<CombatantDiedEvent>(OnCombatantDied);
            this.RegisterEvent<MapLoadingCompletedEvent>(OnMapLoadingCompleted);
        }

        private void OnCombatantDied(CombatantDiedEvent evt)
        {
            if (!this.GetSystem<UnitSystem>().TryGet(evt.UnitId, out var unit) ||
                unit.UnitType != UnitType.Enemy)
            {
                return;
            }

            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetEnemy(unit.ConfigId, out var enemyConfig, out _) ||
                string.IsNullOrWhiteSpace(enemyConfig.GuaranteedWeaponLootConfigId))
            {
                return;
            }

            var entries = BuildEntries(evt.UnitId, enemyConfig);
            if (rule.TryHandle(
                evt,
                this.GetSystem<InventorySystem>().CanUseExpeditionBagItems(),
                unit.MapInstanceId,
                entries,
                unit.Position,
                unit.Rotation,
                out var request))
            {
                this.SendEvent(new EnemyCorpseContentsRequestedEvent(
                    request.MapInstanceId,
                    request.SourceEnemyUnitId,
                    request.Position,
                    request.Rotation,
                    request.Entries));
            }
        }

        private IReadOnlyList<LootDropBatchEntry> BuildEntries(int unitId, EnemyConfigDefinition enemyConfig)
        {
            var lootIds = new List<(string id, int amount)>(3)
            {
                (enemyConfig.GuaranteedWeaponLootConfigId, 0)
            };

            if (!string.IsNullOrWhiteSpace(enemyConfig.GuaranteedAmmoLootConfigId) &&
                this.GetSystem<EnemySystem>().TryGetRemainingAmmo(unitId, out var remainingAmmo) &&
                remainingAmmo > 0)
            {
                lootIds.Add((enemyConfig.GuaranteedAmmoLootConfigId, remainingAmmo));
            }

            if (!string.IsNullOrWhiteSpace(enemyConfig.LootConfigId))
            {
                lootIds.Add((enemyConfig.LootConfigId, 0));
            }

            var entries = new List<LootDropBatchEntry>(lootIds.Count);
            for (var i = 0; i < lootIds.Count; i++)
            {
                var offset = new Vector3((i - (lootIds.Count - 1) / 2f) * 0.6f, 0f, 0f);
                entries.Add(new LootDropBatchEntry(lootIds[i].id, lootIds[i].amount, offset));
            }

            return entries;
        }

        private void OnMapLoadingCompleted(MapLoadingCompletedEvent evt)
        {
            rule.Reset();
        }
    }
}
