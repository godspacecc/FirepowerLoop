using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// Handles loot registration, collection rules, map ownership, and inventory submission.
    /// </summary>
    public sealed class LootSystem : AbstractSystem
    {
        public const float CollectRange = 1.25f;
        private const float CollectRangeSqr = CollectRange * CollectRange;

        protected override void OnInit()
        {
        }

        public bool TryRegisterLoot(int unitId, LootConfigDefinition config, out string error)
        {
            if (unitId <= 0)
            {
                error = "掉落物注册失败：UnitId 必须大于 0";
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.Id))
            {
                error = "掉落物注册失败：缺少配置 Id";
                return false;
            }

            if (config.ItemId <= 0)
            {
                error = "掉落物注册失败：ItemId 必须大于 0";
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.PrefabAddress))
            {
                error = "掉落物注册失败：缺少 PrefabAddress";
                return false;
            }

            if (config.Amount <= 0)
            {
                error = "掉落物注册失败：Amount 必须大于 0";
                return false;
            }

            if (!this.GetSystem<UnitSystem>().TryGet(unitId, out var unit) ||
                unit.UnitType != UnitType.Loot ||
                unit.ConfigId != config.Id)
            {
                error = "掉落物注册失败：缺少匹配的 Loot Unit";
                return false;
            }

            if (!unit.IsActive)
            {
                error = "掉落物注册失败：Loot Unit 必须处于激活状态";
                return false;
            }

            var snapshot = new LootSnapshot(
                unit.UnitId,
                unit.MapInstanceId,
                config.Id,
                config.ItemId,
                config.Amount,
                unit.Position,
                false);

            if (!this.GetModel<LootModel>().TryAdd(snapshot))
            {
                error = $"掉落物注册失败：UnitId={unitId} 已存在";
                return false;
            }

            this.SendEvent(new LootCreatedEvent(unitId, unit.MapInstanceId, config.Id, config.ItemId, config.Amount));
            error = null;
            return true;
        }

        /// <summary>创建或恢复事务中绑定火把余量；不允许对非火把或多件掉落附加实例状态。</summary>
        internal bool TrySetTorchRemaining(int unitId, float? seconds)
        {
            if (!this.GetModel<LootModel>().TryGet(unitId,out var loot) || loot.Amount != 1 ||
                !this.GetUtility<IRuntimeConfigProvider>().TryGetItem(loot.ItemId,out var item,out _) || item.ActionKind != global::ItemActionKind.ToggleTorch ||
                seconds.HasValue && (float.IsNaN(seconds.Value) || float.IsInfinity(seconds.Value) || seconds.Value<0f || seconds.Value>item.BurnSeconds)) return false;
            this.GetModel<LootModel>().Set(loot.WithTorchRemaining(seconds)); return true;
        }

        /// <summary>掉落/恢复事务提交来源，基地带入物品不会因重新拾取转成当地物资。</summary>
        internal bool TrySetOrigin(int unitId,string id,int acquired)
        {
            var model=this.GetModel<LootModel>();if(!model.TryGet(unitId,out var loot)||acquired<0||acquired>loot.Amount||acquired>0&&string.IsNullOrEmpty(id))return false;
            model.Set(loot.WithOrigin(id,acquired));return true;
        }
        public bool TryRemoveLoot(int unitId)
        {
            return this.GetModel<LootModel>().Remove(unitId);
        }

        public bool TryCollect(int lootUnitId, int collectorUnitId)
        {
            var model = this.GetModel<LootModel>();
            var inventory = this.GetSystem<InventorySystem>();
            if (!model.TryGet(lootUnitId, out var loot) ||
                loot.Collected ||
                loot.Amount <= 0 ||
                !this.GetSystem<UnitSystem>().TryGet(lootUnitId, out var lootUnit) ||
                !this.GetSystem<UnitSystem>().TryGet(collectorUnitId, out var collector) ||
                lootUnit.UnitType != UnitType.Loot ||
                collector.UnitType != UnitType.Player ||
                collectorUnitId != PlayerModel.LocalPlayerUnitId ||
                lootUnit.MapInstanceId != collector.MapInstanceId ||
                lootUnit.MapInstanceId != loot.MapInstanceId ||
                lootUnit.ConfigId != loot.ConfigId ||
                !lootUnit.IsActive ||
                !collector.IsActive ||
                (lootUnit.Position - collector.Position).sqrMagnitude > CollectRangeSqr ||
                !inventory.CanCollectWorldLootForCurrentPlayer())
            {
                return false;
            }

            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetItem(loot.ItemId, out _, out _))
            {
                return false;
            }

            var bagSnapshot = inventory.CapturePlayerBagSnapshot();
            var lootSnapshot = model.CaptureSnapshot(lootUnitId);
            var unitSnapshot = lootUnit;
            try
            {
                var provenance=loot.OriginKnown?new ItemStack(loot.ItemId,loot.Amount){OriginExpeditionId=loot.OriginExpeditionId,AcquiredAmount=loot.AcquiredAmount}:null;
                if (!inventory.TryPreviewAddPlayerBagItem(loot.ItemId, loot.Amount, out var acceptedAmount, out _,provenance) ||
                    acceptedAmount <= 0)
                {
                    inventory.RestorePlayerBagSnapshot(bagSnapshot);
                    return false;
                }

                if (loot.TorchRemainingSeconds.HasValue)
                {
                    var bag=this.GetModel<PlayerBagModel>().Container;
                    foreach(var pair in bag.Slots)
                        if (pair.Value.ItemId==loot.ItemId && !bagSnapshot.Slots.ContainsKey(pair.Key))
                            pair.Value.TorchRemainingSeconds=loot.TorchRemainingSeconds;
                }
                var remainingAmount = loot.Amount - acceptedAmount;
                if (remainingAmount > 0)
                {
                    model.Set(loot.WithRemainingAmount(remainingAmount));
                }
                else
                {
                    model.Set(loot.WithCollected());
                    if (!this.GetSystem<UnitSystem>().Remove(lootUnitId, UnitRemovedReason.Explicit))
                    {
                        inventory.RestorePlayerBagSnapshot(bagSnapshot);
                        model.RestoreSnapshot(lootSnapshot);
                        return false;
                    }
                }

                inventory.CommitPlayerBagChanged("loot collected");
                SafeSendLootCollected(new LootCollectedEvent(
                    lootUnitId,
                    collectorUnitId,
                    loot.ConfigId,
                    loot.ItemId,
                    acceptedAmount,
                    remainingAmount));
                return true;
            }
            catch
            {
                inventory.RestorePlayerBagSnapshot(bagSnapshot);
                model.RestoreSnapshot(lootSnapshot);
                this.GetSystem<UnitSystem>().TryRestoreRemovedSnapshot(unitSnapshot);
                return false;
            }
        }

        private void SafeSendLootCollected(LootCollectedEvent evt)
        {
            try
            {
                this.SendEvent(evt);
            }
            catch (System.Exception e)
            {
                Log.Error($"[LootSystem] LootCollectedEvent 监听器 失败: {e.Message}");
            }
        }
    }
}
