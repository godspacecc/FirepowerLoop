using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    public enum InventoryContainerKind
    {
        PlayerBag = 0,
        Warehouse = 1,
        Search = 2,
        /// <summary>玩家固定四格口袋；与 PlayerBag 共享状态许可和保存归属。</summary>
        Pocket = 3
    }

    public readonly struct InventoryChangedEvent
    {
    }

    /// <summary>本机从搜索容器成功收取到背包后发布；只表示已经完成的转移，不驱动奖励或存档。</summary>
    public readonly struct SearchItemCollectedEvent { }

    public readonly struct PlayerItemUseFinishedEvent
    {
        public PlayerItemUseFinishedEvent(long actionId, bool succeeded, int appliedValue, string error)
        {
            ActionId = actionId;
            Succeeded = succeeded;
            AppliedValue = appliedValue;
            Error = error;
        }

        public long ActionId { get; }
        public bool Succeeded { get; }
        public int AppliedValue { get; }
        public string Error { get; }
    }

    public readonly struct InventorySlotRef
    {
        public InventorySlotRef(InventoryContainerKind containerKind, int slotIndex, long runtimeContainerId = 0)
        {
            ContainerKind = containerKind;
            SlotIndex = slotIndex;
            RuntimeContainerId = runtimeContainerId;
        }

        public InventoryContainerKind ContainerKind { get; }
        public int SlotIndex { get; }
        public long RuntimeContainerId { get; }
    }

    public readonly struct WarehouseExpansionQuote
    {
        public WarehouseExpansionQuote(
            int currentCapacity,
            int maximumCapacity,
            int metalPartsOwned,
            int metalPartsRequired,
            int clothOwned,
            int clothRequired,
            bool stateAllowed)
        {
            CurrentCapacity = currentCapacity;
            MaximumCapacity = maximumCapacity;
            MetalPartsOwned = metalPartsOwned;
            MetalPartsRequired = metalPartsRequired;
            ClothOwned = clothOwned;
            ClothRequired = clothRequired;
            StateAllowed = stateAllowed;
        }

        public int CurrentCapacity { get; }
        public int MaximumCapacity { get; }
        public int MetalPartsOwned { get; }
        public int MetalPartsRequired { get; }
        public int ClothOwned { get; }
        public int ClothRequired { get; }
        public bool StateAllowed { get; }
        public bool IsAtMaximum => CurrentCapacity >= MaximumCapacity;
        public bool HasRequiredMaterials =>
            MetalPartsOwned >= MetalPartsRequired &&
            ClothOwned >= ClothRequired;
        public bool CanExpand => StateAllowed && !IsAtMaximum && HasRequiredMaterials;
    }

    public enum PlayerBagRecoveryKind
    {
        None = 0,
        EnterRollback = 1,
        SuccessfulReturn = 2,
        DeathClear = 3,
        UnsafeSaveLoad = 4
    }

    public sealed class InventorySystem : AbstractSystem
    {
        private PlayerBagRecoveryKind pendingRecoveryKind;
        private Func<string> craftAfterMaterialsRemovedTestHook;
        private long pendingUseActionId;
        private int pendingUseSlotIndex = -1;
        private int pendingUseItemId;
        /// <summary>持续使用开始时的来源容器；结束时仍需核验格号与物品身份。</summary>
        private InventoryContainerKind pendingUseContainerKind = InventoryContainerKind.PlayerBag;

        protected override void OnInit()
        {
            this.RegisterEvent<PlayerActionEndedEvent>(OnPlayerActionEnded);
            var save = this.GetSystem<SaveSystem>();
            if (!save.RegisterSection(this.GetModel<PlayerBagModel>(), out var bagError))
            {
                Log.Error($"[InventorySystem] 注册玩家背包保存分区失败：{bagError}");
            }

            if (!save.RegisterSection(this.GetModel<WarehouseModel>(), out var warehouseError))
            {
                Log.Error($"[InventorySystem] 注册仓库保存分区失败：{warehouseError}");
            }
        }

        public PlayerBagRiskState RiskState => this.GetModel<PlayerBagModel>().RiskState;

        /// <summary>查询背包与口袋的合计数量；不包含仓库，不改变库存或保存状态。</summary>
        public int GetCarriedItemAmount(int itemId)
        {
            var model = this.GetModel<PlayerBagModel>();
            return model.Container.GetTotalAmount(itemId) + model.Pocket.GetTotalAmount(itemId);
        }

        /// <summary>供装填等外层事务扣除随身物品；背包优先、口袋补足，失败恢复两容器，由调用者提交事件与保存。</summary>
        internal bool TryRemoveCarriedItemAmount(int itemId, int amount, out string error)
        {
            var model = this.GetModel<PlayerBagModel>();
            return TryRemoveAcrossContainers(new[] { model.Container, model.Pocket }, itemId, amount, out error);
        }

        public bool TrySetPlayerBagRiskState(PlayerBagRiskState riskState, string reason, bool flush, out string error)
        {
            error = null;
            var bag = this.GetModel<PlayerBagModel>();
            bag.SetRiskState(riskState);
            MarkInventoryChanged(reason);
            if (!flush || this.GetSystem<SaveSystem>().Flush(out error))
            {
                return true;
            }

            return false;
        }

        public ItemContainerSnapshot CapturePlayerBagSnapshot()
        {
            return this.GetModel<PlayerBagModel>().Container.CaptureSnapshot();
        }

        /// <summary>从当前背包快照计算完整加入候选；失败不修改实时背包。</summary>
        internal bool TryBuildPlayerBagAddCandidate(int itemId, int amount, out ItemContainerSnapshot candidate, out string error)
        {
            candidate = null;
            if (amount <= 0)
            {
                error = "数量 必须为正数";
                return false;
            }

            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out var item, out error)) return false;
            var current = this.GetModel<PlayerBagModel>().Container;
            var working = new ItemContainer(current.Capacity);
            working.RestoreSnapshot(current.CaptureSnapshot());
            if (!working.TryAdd(itemId, amount, item.MaxStackAmount, out var accepted, out error, CreateAcquisition(itemId,amount)) || accepted != amount)
            {
                candidate = null;
                error = "玩家背包空间不足";
                return false;
            }

            candidate = working.CaptureSnapshot();
            error = null;
            return true;
        }

        /// <summary>从当前背包快照跨格计算完整移除候选；失败不留下部分变化。</summary>
        internal bool TryBuildPlayerBagRemoveCandidate(int itemId, int amount, out ItemContainerSnapshot candidate, out string error)
        {
            candidate = null;
            if (amount <= 0)
            {
                error = "数量 必须为正数";
                return false;
            }

            var current = this.GetModel<PlayerBagModel>().Container;
            if (current.GetTotalAmount(itemId) < amount)
            {
                error = "玩家背包物品不足";
                return false;
            }

            var working = new ItemContainer(current.Capacity);
            working.RestoreSnapshot(current.CaptureSnapshot());
            if (!TryRemoveItemAmount(working, itemId, amount, out error)) return false;
            candidate = working.CaptureSnapshot();
            error = null;
            return true;
        }

        internal int GetPlayerBagOwnedAmount(int itemId)
        {
            return this.GetModel<PlayerBagModel>().Container.GetTotalAmount(itemId);
        }

        internal int CalculatePlayerBagAcceptableAmount(int itemId, int requestedAmount)
        {
            if (requestedAmount <= 0 || !this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out var item, out _)) return 0;
            return this.GetModel<PlayerBagModel>().Container.CalculateAcceptableAmount(itemId, requestedAmount, item.MaxStackAmount);
        }

        /// <summary>无通知应用系统自产候选；异常被收敛为失败，供原子事务决定回滚。</summary>
        internal bool TryApplyPlayerBagCandidate(ItemContainerSnapshot candidate)
        {
            if (candidate == null) return false;
            try
            {
                this.GetModel<PlayerBagModel>().RestoreContainer(candidate);
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[InventorySystem] 应用背包候选失败：{e.Message}");
                return false;
            }
        }

        /// <summary>无通知恢复系统自产旧快照；该入口不保存、不发事件且不向事务抛异常。</summary>
        internal void RestorePlayerBagTransactionSnapshot(ItemContainerSnapshot snapshot)
        {
            if (snapshot == null) return;
            try
            {
                this.GetModel<PlayerBagModel>().RestoreContainer(snapshot);
            }
            catch (Exception e)
            {
                Log.Error($"[InventorySystem] 恢复背包事务快照失败：{e.Message}");
            }
        }

        /// <summary>交易持久化成功或回滚完成后，仅发布一次与最终内存一致的背包刷新。</summary>
        internal void PublishPlayerBagChanged()
        {
            try
            {
                this.SendEvent(new InventoryChangedEvent());
            }
            catch (Exception e)
            {
                Log.Error($"[InventorySystem] InventoryChangedEvent 监听器失败：{e.Message}");
            }
        }

        public void RestorePlayerBagSnapshot(ItemContainerSnapshot snapshot)
        {
            this.GetModel<PlayerBagModel>().RestoreContainer(snapshot);
        }

        public void RestorePlayerBagState(ItemContainerSnapshot snapshot, PlayerBagRiskState riskState)
        {
            var bag = this.GetModel<PlayerBagModel>();
            bag.RestoreContainer(snapshot);
            bag.SetRiskState(riskState);
        }

        public void ClearPlayerBag()
        {
            this.GetModel<PlayerBagModel>().ClearContainer();
        }

        public void MarkRecoveryRequired(PlayerBagRecoveryKind kind, string reason)
        {
            pendingRecoveryKind = kind;
            this.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.RecoveryRequired);
            MarkInventoryChanged(reason);
        }

        public bool TryRetryRecovery(out string error)
        {
            error = null;
            if (this.GetModel<PlayerBagModel>().RiskState != PlayerBagRiskState.RecoveryRequired)
            {
                error = "玩家背包 恢复 不需要";
                return false;
            }

            if (pendingRecoveryKind == PlayerBagRecoveryKind.None)
            {
                error = "缺少玩家背包恢复结果";
                return false;
            }

            this.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.BaseSafe);
            MarkInventoryChanged("player bag recovery retry");
            if (!this.GetSystem<SaveSystem>().Flush(out error))
            {
                this.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.RecoveryRequired);
                return false;
            }

            pendingRecoveryKind = PlayerBagRecoveryKind.None;
            this.GetModel<ExpeditionModel>().Clear();
            this.SendEvent(new ExpeditionChangedEvent());
            return true;
        }

        public bool FlushPlayerBagAsBaseSafe(string reason, out string error)
        {
            this.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.BaseSafe);
            MarkInventoryChanged(reason);
            return this.GetSystem<SaveSystem>().Flush(out error);
        }

        public bool TryPreviewAddPlayerBagItem(int itemId, int amount, out int acceptedAmount, out string error, ItemStack provenance=null)
        {
            acceptedAmount = 0;
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out var item, out error))
            {
                return false;
            }

            var bag = this.GetModel<PlayerBagModel>().Container;
            acceptedAmount = bag.CalculateAcceptableAmount(itemId, amount, item.MaxStackAmount);
            if (acceptedAmount <= 0)
            {
                error = "玩家背包不能接收该物品";
                return false;
            }

            return bag.TryAdd(itemId, acceptedAmount, item.MaxStackAmount, out acceptedAmount, out error, provenance??CreateAcquisition(itemId,amount));
        }

        /// <summary>当地新增实物记录当前远征身份；基地购买/生产仍是基地来源。</summary>
        internal ItemStack CreateAcquisition(int itemId,int amount)
        {
            var stack=new ItemStack(itemId,amount);var expedition=this.GetModel<ExpeditionModel>();
            if(this.GetModel<SaveModel>().CurrentDomain==OfflineSaveDomain.Expedition&&expedition.IsActive)stack.MarkExploration(expedition.CurrentExpeditionId,amount);
            return stack;
        }
        internal int GetAcquiredAmount(int itemId)
        {var bag=this.GetModel<PlayerBagModel>();return new[]{bag.Container,bag.Pocket}.Sum(c=>c.Slots.Values.Where(s=>s.ItemId==itemId).Sum(s=>s.AcquiredAmount));}

        public void CommitPlayerBagChanged(string reason)
        {
            MarkInventoryChanged(reason);
        }

        /// <summary>当前本地玩家是否可把同一张有效离线地图中的世界掉落放入玩家背包。</summary>
        public bool CanCollectWorldLootForCurrentPlayer()
        {
            var map = this.GetModel<MapSceneModel>().CurrentMapId;
            var save = this.GetModel<SaveModel>();
            var bag = this.GetModel<PlayerBagModel>();
            if (this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing ||
                this.GetModel<CombatModel>().PlayerHP.Value <= 0 ||
                !this.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var player) ||
                !player.IsActive || player.MapInstanceId <= 0)
            {
                return false;
            }

            if (save.CurrentDomain == OfflineSaveDomain.Base)
            {
                return string.Equals(map, MapIds.Base, StringComparison.Ordinal) &&
                       bag.RiskState == PlayerBagRiskState.BaseSafe;
            }

            var expedition = this.GetModel<ExpeditionModel>();
            return save.CurrentDomain == OfflineSaveDomain.Expedition &&
                   bag.RiskState == PlayerBagRiskState.InExpedition &&
                   (expedition.Status == ExpeditionStatus.Playing || expedition.Status == ExpeditionStatus.ReturnQualified) &&
                   string.Equals(map, expedition.CurrentMapId, StringComparison.Ordinal);
        }

        public bool CanUseExpeditionBagItems()
        {
            var expedition = this.GetModel<ExpeditionModel>();
            return this.GetModel<PlayerBagModel>().RiskState == PlayerBagRiskState.InExpedition &&
                   this.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing &&
                   (expedition.Status == ExpeditionStatus.Playing || expedition.Status == ExpeditionStatus.ReturnQualified) &&
                   this.GetModel<CombatModel>().PlayerHP.Value > 0;
        }

        public bool CanUseBaseOrExpeditionPowerItems()
        {
            return this.GetModel<CombatModel>().PlayerHP.Value > 0 &&
                   (CanUseBasePowerItems() || CanUseExpeditionBagItems());
        }

        private bool TryCommitPlayerBagItem(int slotIndex, out int appliedValue, out string error)
        {
            return TryCommitCarriedItem(new InventorySlotRef(InventoryContainerKind.PlayerBag, slotIndex), out appliedValue, out error);
        }

        /// <summary>持续使用完成后原子提交来源扣除与效果；失败恢复运行时和来源容器。</summary>
        private bool TryCommitCarriedItem(InventorySlotRef source, out int appliedValue, out string error)
        {
            appliedValue = 0;
            var slotIndex = source.SlotIndex;
            var bag = GetContainer(source.ContainerKind);
            if (!bag.TryGet(slotIndex, out var stack))
            {
                error = "源槽位为空";
                return false;
            }

            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetItem(stack.ItemId, out var item, out error))
            {
                return false;
            }

            if (item.ItemType != global::ItemType.Consumable ||
                item.UseValue <= 0)
            {
                error = "该物品不是受支持的消耗品";
                return false;
            }

            if (!CanUsePlayerBagConsumable(item.UseEffect, out error))
            {
                return false;
            }

            var bagSnapshot = bag.CaptureSnapshot();
            var runtimeSnapshot = this.GetSystem<SaveSystem>().CaptureRuntimeSnapshot();
            var success = false;
            try
            {
                if (!TryApplyPlayerBagConsumable(item, out appliedValue))
                {
                    error = GetConsumableNoApplyError(item.UseEffect);
                    appliedValue = 0;
                    return false;
                }

                if (!bag.TryRemove(slotIndex, 1, out _, out error))
                {
                    return false;
                }

                MarkInventoryChanged("player bag item used");
                success = true;
                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = $"物品使用失败：{e.Message}";
                return false;
            }
            finally
            {
                if (!success)
                {
                    var runtimeRestored = this.GetSystem<SaveSystem>().RestoreRuntimeSnapshot(runtimeSnapshot, out var runtimeError);
                    var bagRestored = true;
                    var bagError = string.Empty;
                    try
                    {
                        bag.RestoreSnapshot(bagSnapshot);
                    }
                    catch (Exception e)
                    {
                        bagRestored = false;
                        bagError = e.Message;
                    }

                    if (!runtimeRestored || !bagRestored)
                    {
                        error = $"{error}；运行时回滚{(runtimeRestored ? "成功" : "失败：" + runtimeError)}；背包回滚{(bagRestored ? "成功" : "失败：" + bagError)}";
                    }
                }
            }
        }

        public bool TryBeginUsePlayerBagItem(int slotIndex, out string error)
        {
            return TryBeginUseCarriedItem(new InventorySlotRef(InventoryContainerKind.PlayerBag, slotIndex), out error);
        }

        /// <summary>从背包或口袋发起原有持续使用；容器身份随操作保存，不把物品临时搬到背包。</summary>
        public bool TryBeginUseCarriedItem(InventorySlotRef source, out string error)
        {
            if (!IsCarriedContainer(source.ContainerKind) || !ValidateSlotRef(source, out error))
            {
                error = "物品来源不是随身容器";
                return false;
            }
            var slotIndex = source.SlotIndex;
            this.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.Replaced);
            pendingUseActionId = 0;
            pendingUseSlotIndex = -1;
            pendingUseItemId = 0;
            var bag = GetContainer(source.ContainerKind);
            if (!bag.TryGet(slotIndex, out var stack))
            {
                error = "源槽位为空";
                return false;
            }

            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetItem(stack.ItemId, out var item, out error) ||
                item.ItemType != global::ItemType.Consumable || item.UseValue <= 0 || item.UseDurationSeconds <= 0f)
            {
                error = error ?? "该物品不是受支持的消耗品";
                return false;
            }

            if (!CanUsePlayerBagConsumable(item.UseEffect, out error)) return false;
            if (!this.GetSystem<PlayerActionSystem>().TryBegin(new PlayerActionRequest(PlayerActionKind.UseItem, item.UseDurationSeconds, item.IconAddress), out var actionId, out error)) return false;
            pendingUseActionId = actionId;
            pendingUseSlotIndex = slotIndex;
            pendingUseContainerKind = source.ContainerKind;
            pendingUseItemId = stack.ItemId;
            return true;
        }

        private void OnPlayerActionEnded(PlayerActionEndedEvent evt)
        {
            if (evt.Kind != PlayerActionKind.UseItem || evt.ActionId != pendingUseActionId) return;
            var slotIndex = pendingUseSlotIndex;
            var source = new InventorySlotRef(pendingUseContainerKind, slotIndex);
            var itemId = pendingUseItemId;
            pendingUseActionId = 0;
            pendingUseSlotIndex = -1;
            pendingUseItemId = 0;
            if (!evt.Completed)
            {
                this.SendEvent(new PlayerItemUseFinishedEvent(evt.ActionId, false, 0, "物品使用已取消"));
                return;
            }

            var bag = GetContainer(source.ContainerKind);
            if (!bag.TryGet(slotIndex, out var stack) || stack.ItemId != itemId)
            {
                this.SendEvent(new PlayerItemUseFinishedEvent(evt.ActionId, false, 0, "来源槽位已变化"));
                return;
            }

            var success = TryCommitCarriedItem(source, out var appliedValue, out var error);
            this.SendEvent(new PlayerItemUseFinishedEvent(evt.ActionId, success, appliedValue, error));
        }

        private bool CanUsePlayerBagConsumable(global::ItemUseEffect effect, out string error)
        {
            switch (effect)
            {
                case global::ItemUseEffect.RestoreHealth:
                    if (CanUseExpeditionBagItems())
                    {
                        error = null;
                        return true;
                    }

                    error = "不允许使用玩家背包物品";
                    return false;
                case global::ItemUseEffect.RestorePower:
                    if (CanUseBaseOrExpeditionPowerItems())
                    {
                        error = null;
                        return true;
                    }

                    error = "不允许使用玩家体力物品";
                    return false;
                case global::ItemUseEffect.RestoreWater:
                    if (CanUseBaseOrExpeditionPowerItems())
                    {
                        error = null;
                        return true;
                    }

                    error = "不允许使用玩家水分物品";
                    return false;
                default:
                    error = "该物品不是受支持的消耗品";
                    return false;
            }
        }

        private bool TryApplyPlayerBagConsumable(ItemDefinition item, out int appliedValue)
        {
            switch (item.UseEffect)
            {
                case global::ItemUseEffect.RestoreHealth:
                    return this.GetSystem<CombatSystem>().TryRestorePlayerHealth(item.UseValue, out appliedValue);
                case global::ItemUseEffect.RestorePower:
                    var applied = 0f;
                    var success = this.GetSystem<PlayerConditionSystem>().TryRestorePower(item.UseValue, out applied);
                    appliedValue = Mathf.RoundToInt(applied);
                    return success;
                case global::ItemUseEffect.RestoreWater:
                    var appliedWater = 0f;
                    var waterSuccess = this.GetSystem<PlayerConditionSystem>().TryRestoreWater(item.UseValue, out appliedWater);
                    appliedValue = Mathf.RoundToInt(appliedWater);
                    return waterSuccess;
                default:
                    appliedValue = 0;
                    return false;
            }
        }

        private static string GetConsumableNoApplyError(global::ItemUseEffect effect)
        {
            switch (effect)
            {
                case global::ItemUseEffect.RestoreHealth:
                    return "玩家生命值已满或不可用";
                case global::ItemUseEffect.RestorePower:
                    return "玩家能量已满或不可用";
                case global::ItemUseEffect.RestoreWater:
                    return "玩家水分已满或不可用";
                default:
                    return "物品使用未产生效果";
            }
        }

        public bool TryAddWarehouseItem(int itemId, int amount, out int acceptedAmount)
        {
            acceptedAmount = 0;
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out var item, out _) ||
                amount <= 0)
            {
                return false;
            }

            var result = this.GetModel<WarehouseModel>().Container.TryAdd(
                itemId,
                amount,
                item.MaxStackAmount,
                out acceptedAmount,
                out _);
            if (result)
            {
                MarkInventoryChanged("warehouse item added");
            }

            return result;
        }

        public bool TrySpendWarehouseItem(int itemId, int amount)
        {
            if (amount <= 0)
            {
                return true;
            }

            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out _, out _))
            {
                return false;
            }

            var warehouse = this.GetModel<WarehouseModel>().Container;
            if (warehouse.GetTotalAmount(itemId) < amount)
            {
                return false;
            }

            var snapshot = warehouse.CaptureSnapshot();
            var remaining = amount;
            foreach (var pair in warehouse.Slots.OrderBy(pair => pair.Key).ToList())
            {
                if (pair.Value.ItemId != itemId)
                {
                    continue;
                }

                var remove = Math.Min(remaining, pair.Value.Amount);
                if (!warehouse.TryRemove(pair.Key, remove, out _, out _))
                {
                    warehouse.RestoreSnapshot(snapshot);
                    return false;
                }

                remaining -= remove;
                if (remaining == 0)
                {
                    MarkInventoryChanged("warehouse item spent");
                    return true;
                }
            }

            warehouse.RestoreSnapshot(snapshot);
            return false;
        }

        public bool TryGetWarehouseExpansionQuote(
            out WarehouseExpansionQuote quote,
            out string error)
        {
            quote = default;
            var configs = this.GetUtility<IRuntimeConfigProvider>();
            if (!configs.TryGetItem(ItemIds.MetalParts, out _, out error) ||
                !configs.TryGetItem(ItemIds.Cloth, out _, out error))
            {
                return false;
            }

            var warehouse = this.GetModel<WarehouseModel>().Container;
            var bag = this.GetModel<PlayerBagModel>().Container;
            var stateAllowed = CanModifyWarehouse(out _);
            quote = new WarehouseExpansionQuote(
                warehouse.Capacity,
                InventoryDefaults.WarehouseMaximumCapacity,
                warehouse.GetTotalAmount(ItemIds.MetalParts) + GetCarriedItemAmount(ItemIds.MetalParts),
                InventoryDefaults.WarehouseExpansionMetalPartsCost,
                warehouse.GetTotalAmount(ItemIds.Cloth) + GetCarriedItemAmount(ItemIds.Cloth),
                InventoryDefaults.WarehouseExpansionClothCost,
                stateAllowed);
            error = null;
            return true;
        }

        public bool TryGetCraftInventoryQuote(
            CraftRecipeDefinition recipe,
            out CraftInventoryQuote quote,
            out string error)
        {
            return TryGetCraftInventoryQuote(recipe, 1, out quote, out error);
        }

        public bool TryGetCraftInventoryQuote(
            CraftRecipeDefinition recipe,
            int batchCount,
            out CraftInventoryQuote quote,
            out string error)
        {
            quote = default;
            if (!TryValidateCraftRecipeDefinition(recipe, out var configReason, out error))
            {
                quote = new CraftInventoryQuote(
                    Array.Empty<CraftMaterialQuote>(),
                    false,
                    false,
                    0,
                    0,
                    0,
                    0,
                    configReason);
                return true;
            }

            if (!TryBuildCraftAmounts(recipe, batchCount, out var materials, out var outputAmount, out var amountReason, out error))
            {
                quote = new CraftInventoryQuote(
                    Array.Empty<CraftMaterialQuote>(),
                    false,
                    false,
                    0,
                    0,
                    0,
                    0,
                    amountReason);
                return true;
            }

            SimulateCraftStorage(recipe.OutputItemId, outputAmount, out var bagAccepted, out var warehouseAccepted, out var droppedAmount, out _);
            if (!CanModifyWarehouse(out error))
            {
                quote = new CraftInventoryQuote(
                    materials,
                    false,
                    droppedAmount == 0,
                    outputAmount,
                    bagAccepted,
                    warehouseAccepted,
                    droppedAmount,
                    CraftFailureReason.StateNotAllowed);
                return true;
            }

            if (!TrySimulateCraft(recipe, batchCount, out var reason, out error))
            {
                quote = new CraftInventoryQuote(
                    materials,
                    true,
                    droppedAmount == 0,
                    outputAmount,
                    bagAccepted,
                    warehouseAccepted,
                    droppedAmount,
                    reason);
                return true;
            }

            quote = new CraftInventoryQuote(
                materials,
                true,
                droppedAmount == 0,
                outputAmount,
                bagAccepted,
                warehouseAccepted,
                droppedAmount,
                CraftFailureReason.None);
            error = null;
            return true;
        }

        public bool TryCommitCraftInventory(
            CraftRecipeDefinition recipe,
            out CraftFailureReason reason,
            out string error)
        {
            return TryCommitCraftInventory(recipe, 1, null, out _, out reason, out error);
        }

        public bool TryCommitCraftInventory(
            CraftRecipeDefinition recipe,
            int batchCount,
            ICraftOverflowDropPort overflowDropPort,
            out CraftCommitResult result,
            out CraftFailureReason reason,
            out string error)
        {
            result = default;
            reason = CraftFailureReason.InventoryOperationFailed;
            if (!TryValidateCraftRecipeDefinition(recipe, out reason, out error))
            {
                return false;
            }

            if (!TryBuildCraftAmounts(recipe, batchCount, out var materials, out var outputAmount, out reason, out error))
            {
                return false;
            }

            if (!CanModifyWarehouse(out error))
            {
                reason = CraftFailureReason.StateNotAllowed;
                return false;
            }

            if (!TrySimulateCraft(recipe, batchCount, out reason, out error))
            {
                return false;
            }

            var bag = this.GetModel<PlayerBagModel>().Container;
            var warehouse = this.GetModel<WarehouseModel>().Container;
            var bagSnapshot = bag.CaptureSnapshot();
            var warehouseSnapshot = warehouse.CaptureSnapshot();
            var pocket = this.GetModel<PlayerBagModel>().Pocket;
            var pocketSnapshot = pocket.CaptureSnapshot();
            var dropReceipt = CraftOverflowDropReceipt.Empty;
            var success = false;
            try
            {
                foreach (var material in materials.OrderBy(material => material.ItemId))
                {
                    if (!TryRemoveAcrossContainers(
                            new[] { bag, pocket, warehouse },
                            material.ItemId,
                            material.RequiredAmount,
                            out error))
                    {
                        reason = CraftFailureReason.InventoryOperationFailed;
                        return false;
                    }
                }

                var hookError = craftAfterMaterialsRemovedTestHook?.Invoke();
                if (!string.IsNullOrEmpty(hookError))
                {
                    error = hookError;
                    reason = CraftFailureReason.InventoryOperationFailed;
                    return false;
                }

                if (!TryAddAcrossBagThenWarehouse(
                        bag,
                        warehouse,
                        recipe.OutputItemId,
                        outputAmount,
                        out var storedInBag,
                        out var storedInWarehouse,
                        out var droppedAmount,
                        out error))
                {
                    reason = CraftFailureReason.InventoryOperationFailed;
                    return false;
                }

                if (droppedAmount > 0)
                {
                    if (overflowDropPort == null ||
                        !overflowDropPort.TryCreateCraftOverflowDrop(
                            recipe.OutputItemId,
                            droppedAmount,
                            out dropReceipt,
                            out error))
                    {
                        reason = CraftFailureReason.DropCreationFailed;
                        return false;
                    }
                }

                MarkInventoryChanged("craft completed");
                success = true;
                result = new CraftCommitResult(
                    recipe.Id,
                    recipe.OutputItemId,
                    outputAmount,
                    storedInBag,
                    storedInWarehouse,
                    droppedAmount);
                reason = CraftFailureReason.None;
                error = null;
                return true;
            }
            catch (Exception e)
            {
                reason = CraftFailureReason.InventoryOperationFailed;
                error = e.Message;
                return false;
            }
            finally
            {
                if (!success)
                {
                    overflowDropPort?.RollbackCraftOverflowDrop(dropReceipt);
                    bag.RestoreSnapshot(bagSnapshot);
                    warehouse.RestoreSnapshot(warehouseSnapshot);
                    pocket.RestoreSnapshot(pocketSnapshot);
                }
            }
        }

        /// <summary>兼容已有调用；批量材料消耗统一采用随身库存规则。</summary>
        public bool TrySpendPlayerBagItems(IReadOnlyList<ItemCostDefinition> costs, out string error) =>
            TrySpendCarriedItems(costs, out error);

        /// <summary>校验全部材料后按物品 ID、背包、口袋、格号顺序扣除；任一失败恢复两容器，不发布成功事件。</summary>
        public bool TrySpendCarriedItems(IReadOnlyList<ItemCostDefinition> costs, out string error) =>
            TrySpendCarriedItems(costs, true, out error);

        /// <summary>建造外层事务可延迟发布；只有表现和领域对象都成功后才提交库存变化。</summary>
        internal bool TrySpendCarriedItems(IReadOnlyList<ItemCostDefinition> costs, bool publishChange, out string error)
        {
            if (!CanModifyPlayerBag(out error))
            {
                return false;
            }

            if (costs == null || costs.Count == 0)
            {
                error = "物品消耗为空";
                return false;
            }

            var normalized = new Dictionary<int, int>();
            var configs = this.GetUtility<IRuntimeConfigProvider>();
            foreach (var cost in costs)
            {
                if (cost.ItemId <= 0)
                {
                    error = "物品消耗的 ItemId 必须为正数";
                    return false;
                }

                if (cost.Amount <= 0)
                {
                    error = "物品消耗的 Amount 必须为正数";
                    return false;
                }

                if (!configs.TryGetItem(cost.ItemId, out _, out error))
                {
                    return false;
                }

                normalized.TryGetValue(cost.ItemId, out var previous);
                if (cost.Amount > int.MaxValue - previous)
                { error = "物品消耗数量溢出"; return false; }
                normalized[cost.ItemId] = previous + cost.Amount;
            }

            var bag = this.GetModel<PlayerBagModel>().Container;
            foreach (var pair in normalized)
            {
                if (GetCarriedItemAmount(pair.Key) < pair.Value)
                {
                    error = "随身物品不足";
                    return false;
                }
            }

            var snapshot = bag.CaptureSnapshot();
            var pocket = this.GetModel<PlayerBagModel>().Pocket;
            var pocketSnapshot = pocket.CaptureSnapshot();
            foreach (var pair in normalized.OrderBy(pair => pair.Key))
            {
                if (!TryRemoveCarriedItemAmount(pair.Key, pair.Value, out error))
                {
                    bag.RestoreSnapshot(snapshot);
                    pocket.RestoreSnapshot(pocketSnapshot);
                    return false;
                }
            }

            if (publishChange) MarkInventoryChanged("carried items spent");
            error = null;
            return true;
        }

        public bool TryDropPlayerBagStack(
            int slotIndex,
            IPlayerInventoryDropPort dropPort,
            out string error)
        {
            return TryDropCarriedStack(new InventorySlotRef(InventoryContainerKind.PlayerBag, slotIndex), dropPort, out error);
        }

        /// <summary>丢弃背包或口袋整堆；世界创建失败时恢复实际来源，保持旧丢弃语义。</summary>
        public bool TryDropCarriedStack(InventorySlotRef source, IPlayerInventoryDropPort dropPort, out string error)
        {
            if (!IsCarriedContainer(source.ContainerKind) || !ValidateSlotRef(source, out error))
            {
                error = "物品来源不是随身容器";
                return false;
            }
            var slotIndex = source.SlotIndex;
            if (!CanModifyPlayerBag(out error))
            {
                return false;
            }

            if (dropPort == null)
            {
                error = "玩家背包丢弃端口不可用";
                return false;
            }

            var bag = GetContainer(source.ContainerKind);
            if (slotIndex < 0 || slotIndex >= bag.Capacity ||
                !bag.TryGet(slotIndex, out var stack) ||
                stack.ItemId <= 0 || stack.Amount <= 0)
            {
                error = "玩家背包丢弃格子无有效物品";
                return false;
            }

            var itemId = stack.ItemId;
            var amount = stack.Amount;
            var snapshot = bag.CaptureSnapshot();
            var receipt = PlayerInventoryDropReceipt.Empty;
            var success = false;
            try
            {
                // 快照后的整堆扣除仍是未发布的事务中间状态。
                if (!bag.TryRemove(slotIndex, amount, out var removed, out error) ||
                    removed == null || removed.ItemId != itemId || removed.Amount != amount)
                {
                    error = string.IsNullOrWhiteSpace(error)
                        ? "玩家背包丢弃扣除结果与原整堆不一致"
                        : error;
                    return false;
                }

                // 世界掉落端口只承载物品数量，已装弹药回填刚腾出的随身空间，避免静默丢失。
                if (removed.LoadedAmmo > 0)
                {
                    if (!this.GetUtility<IRuntimeConfigProvider>().TryGetWeapon(itemId, out var weapon, out error) ||
                        !bag.TryAdd(weapon.AmmoItemId, removed.LoadedAmmo, ResolveMaxStack(weapon.AmmoItemId), out var returned, out error,new ItemStack(weapon.AmmoItemId,removed.LoadedAmmo){OriginExpeditionId=removed.OriginExpeditionId,AcquiredAmount=removed.LoadedAcquiredAmount}) || returned != removed.LoadedAmmo)
                        return false;
                }
                if (!dropPort.TryCreatePlayerInventoryDrop(itemId, amount, out receipt, out error))
                {
                    error = string.IsNullOrWhiteSpace(error) ? "玩家背包丢弃世界创建失败" : error;
                    return false;
                }

                if (removed.TorchRemainingSeconds.HasValue)
                {
                    foreach(var id in receipt.LootUnitIds)
                        if (!this.GetSystem<LootSystem>().TrySetTorchRemaining(id,removed.TorchRemainingSeconds))
                        { error="火把掉落状态提交失败"; return false; }
                }
                // 世界创建完整成功后，才允许库存变化只发布一次。
                var originOffset=0;
                foreach(var id in receipt.LootUnitIds)
                {
                    if(!this.GetModel<LootModel>().TryGet(id,out var dropped)||!this.GetSystem<LootSystem>().TrySetOrigin(id,removed.OriginExpeditionId,removed.AcquiredSlice(originOffset,dropped.Amount)))
                    {error="掉落来源提交失败";return false;}
                    originOffset+=dropped.Amount;
                }
                MarkInventoryChanged("player bag stack dropped");
                success = true;
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = "玩家背包丢弃失败：" + exception.Message;
                return false;
            }
            finally
            {
                if (!success)
                {
                    // 失败清理必须保证世界回滚异常也无法阻止背包快照恢复。
                    try
                    {
                        if (receipt.LootUnitIds != null && receipt.LootUnitIds.Count > 0)
                        {
                            dropPort.RollbackPlayerInventoryDrop(receipt);
                        }
                    }
                    catch (Exception rollbackException)
                    {
                        Log.Error($"[InventorySystem] 玩家背包丢弃世界回滚失败：{rollbackException.Message}");
                    }

                    bag.RestoreSnapshot(snapshot);
                }
            }
        }

        public bool TryExpandWarehouse(out string error)
        {
            if (!TryGetWarehouseExpansionQuote(out var quote, out error))
            {
                return false;
            }

            if (!quote.StateAllowed)
            {
                CanModifyWarehouse(out error);
                return false;
            }

            if (quote.IsAtMaximum)
            {
                error = "仓库已达到最大容量";
                return false;
            }

            if (!quote.HasRequiredMaterials)
            {
                error = "仓库扩容材料不足";
                return false;
            }

            var warehouse = this.GetModel<WarehouseModel>().Container;
            var bag = this.GetModel<PlayerBagModel>().Container;
            var warehouseSnapshot = warehouse.CaptureSnapshot();
            var bagSnapshot = bag.CaptureSnapshot();
            var pocket = this.GetModel<PlayerBagModel>().Pocket;
            var pocketSnapshot = pocket.CaptureSnapshot();
            var success = false;
            try
            {
                if (!TryRemoveAcrossContainers(
                        new[] { warehouse, bag, pocket },
                        ItemIds.MetalParts,
                        InventoryDefaults.WarehouseExpansionMetalPartsCost,
                        out error) ||
                    !TryRemoveAcrossContainers(
                        new[] { warehouse, bag, pocket },
                        ItemIds.Cloth,
                        InventoryDefaults.WarehouseExpansionClothCost,
                        out error) ||
                    !warehouse.TryExpandCapacity(quote.CurrentCapacity + 1, out error))
                {
                    return false;
                }

                MarkInventoryChanged("warehouse expanded");
                success = true;
                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
            finally
            {
                if (!success)
                {
                    warehouse.RestoreSnapshot(warehouseSnapshot);
                    bag.RestoreSnapshot(bagSnapshot);
                    pocket.RestoreSnapshot(pocketSnapshot);
                }
            }
        }

        public bool TryTransfer(
            InventoryContainerKind sourceKind,
            int sourceIndex,
            InventoryContainerKind targetKind,
            int targetIndex,
            out string error)
        {
            return TryTransfer(
                new InventorySlotRef(sourceKind, sourceIndex),
                new InventorySlotRef(targetKind, targetIndex),
                out error);
        }

        /// <summary>按指定数量在随身容器间或基地背包与仓库间转移；先检查完整接收能力，失败不交换或部分搬动。</summary>
        public bool TryMoveCarriedStack(InventorySlotRef source, InventoryContainerKind targetKind, int amount, out string error)
        {
            var warehouseTransfer = source.ContainerKind == InventoryContainerKind.Warehouse && targetKind == InventoryContainerKind.PlayerBag ||
                                    source.ContainerKind == InventoryContainerKind.PlayerBag && targetKind == InventoryContainerKind.Warehouse;
            if ((!warehouseTransfer && (!IsCarriedContainer(source.ContainerKind) || !IsCarriedContainer(targetKind))) ||
                source.ContainerKind == targetKind || !ValidateSlotRef(source, out error) ||
                !(warehouseTransfer ? CanModifyWarehouse(out error) : CanModifyPlayerBag(out error)))
            {
                error = "当前状态或随身转移目标无效";
                return false;
            }

            var from = GetContainer(source.ContainerKind);
            var to = GetContainer(targetKind);
            if (!from.TryGet(source.SlotIndex, out var stack) || amount <= 0 || amount > stack.Amount)
            {
                error = "转移数量或来源物品无效";
                return false;
            }
            var maxStack = ResolveMaxStack(stack.ItemId);
            if (maxStack <= 0 || to.CalculateAcceptableAmount(stack.ItemId, amount, maxStack) < amount)
            {
                error = "目标存储区空间不足";
                return false;
            }

            // 不可堆叠武器整件移动必须保留库存实例身份和已装弹药。
            if (maxStack == 1 && amount == 1)
            {
                var destination = Enumerable.Range(0, to.Capacity).First(i => !to.Slots.ContainsKey(i));
                return TryTransfer(source, new InventorySlotRef(targetKind, destination), out error);
            }

            var fromBefore = from.CaptureSnapshot();
            var toBefore = to.CaptureSnapshot();
            var committed = false;
            try
            {
                if (!to.TryAdd(stack.ItemId, amount, maxStack, out var accepted, out error,stack) || accepted != amount ||
                    !from.TryRemove(source.SlotIndex, amount, out _, out error)) return false;
                committed = true;
                if (amount == fromBefore.Slots[source.SlotIndex].Amount && (stack.ItemId == ItemIds.ShortSword || stack.ItemId == ItemIds.Bow))
                {
                    var added = to.Slots.First(p => !toBefore.Slots.ContainsKey(p.Key) && p.Value.ItemId == stack.ItemId);
                    this.GetSystem<WeaponEquipmentSystem>().FollowTransfer(source, new InventorySlotRef(targetKind, added.Key));
                }
                MarkInventoryChanged("carried item moved");
                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = "随身物品转移失败：" + e.Message;
                return false;
            }
            finally
            {
                if (!committed)
                {
                    from.RestoreSnapshot(fromBefore);
                    to.RestoreSnapshot(toBefore);
                }
            }
        }

        public bool TryTransfer(InventorySlotRef source, InventorySlotRef target, out string error)
        {
            if (!ValidateSlotRef(source, out error) || !ValidateSlotRef(target, out error) ||
                !CanModifySlotRefs(source, target, out error))
            {
                return false;
            }

            if (source.ContainerKind == target.ContainerKind &&
                source.RuntimeContainerId == target.RuntimeContainerId &&
                source.SlotIndex == target.SlotIndex)
            {
                error = null;
                return true;
            }

            if (!TryGetContainer(source, out var sourceContainer) || !TryGetContainer(target, out var targetContainer))
            {
                error = "库存容器 不可用";
                return false;
            }

            var held = this.GetModel<WeaponEquipmentModel>();
            var heldAtTarget = held.SourceIsCurrent(target);
            var result = ItemContainer.TryTransfer(
                sourceContainer,
                source.SlotIndex,
                targetContainer,
                target.SlotIndex,
                ResolveMaxStack,
                out error);
            if (!result)
            {
                return false;
            }

            if (heldAtTarget) this.GetSystem<WeaponEquipmentSystem>().FollowTransfer(target, source);
            else this.GetSystem<WeaponEquipmentSystem>().FollowTransfer(source, target);
            if (IsCarriedContainer(source.ContainerKind) || IsCarriedContainer(target.ContainerKind))
            {
                MarkInventoryChanged("inventory transferred");
            }

            if (source.ContainerKind == InventoryContainerKind.Search || target.ContainerKind == InventoryContainerKind.Search)
            {
                var id = source.ContainerKind == InventoryContainerKind.Search
                    ? source.RuntimeContainerId
                    : target.RuntimeContainerId;
                if(id>0&&id<=int.MaxValue)this.SendEvent(new SearchContainerChangedEvent((int)id));
            }

            if (source.ContainerKind == InventoryContainerKind.Search && target.ContainerKind == InventoryContainerKind.PlayerBag)
                this.SendEvent(new SearchItemCollectedEvent());

            return true;
        }

        public bool TrySplitHalf(
            InventoryContainerKind containerKind,
            int sourceIndex,
            int targetIndex,
            out string error)
        {
            if (!CanModifyContainers(containerKind, containerKind, out error))
            {
                return false;
            }

            if (sourceIndex == targetIndex)
            {
                error = "源槽位与目标槽位相同";
                return false;
            }

            var container = GetContainer(containerKind);
            var snapshot = container.CaptureSnapshot();
            if (!container.TrySplitHalf(sourceIndex, targetIndex, out error))
            {
                container.RestoreSnapshot(snapshot);
                return false;
            }

            MarkInventoryChanged("inventory split");
            return true;
        }

        private ItemContainer GetContainer(InventoryContainerKind kind)
        {
            return kind == InventoryContainerKind.Pocket ? this.GetModel<PlayerBagModel>().Pocket : kind == InventoryContainerKind.PlayerBag
                ? this.GetModel<PlayerBagModel>().Container
                : this.GetModel<WarehouseModel>().Container;
        }

        internal bool TryGetContainer(InventorySlotRef slotRef, out ItemContainer container)
        {
            if (slotRef.ContainerKind == InventoryContainerKind.Pocket)
            {
                container = this.GetModel<PlayerBagModel>().Pocket;
                return true;
            }
            if (slotRef.ContainerKind == InventoryContainerKind.PlayerBag)
            {
                container = this.GetModel<PlayerBagModel>().Container;
                return true;
            }

            if (slotRef.ContainerKind == InventoryContainerKind.Warehouse)
            {
                container = this.GetModel<WarehouseModel>().Container;
                return true;
            }

            container = null;
            return slotRef.RuntimeContainerId>0&&slotRef.RuntimeContainerId<=int.MaxValue&&this.GetSystem<SearchContainerSystem>()
                .TryResolveMutableContainer((int)slotRef.RuntimeContainerId, out container);
        }

        internal bool TryResolveEquipmentSource(InventorySlotRef source, out ItemContainer container, out string error)
        {
            container = null;
            var bagTarget = new InventorySlotRef(InventoryContainerKind.PlayerBag, 0);
            if (!ValidateSlotRef(source, out error) ||
                !CanModifySlotRefs(source, bagTarget, out error) ||
                !TryGetContainer(source, out container))
            {
                error ??= "装备来源容器不可用";
                return false;
            }

            error = null;
            return true;
        }

        private static bool ValidateSlotRef(InventorySlotRef slotRef, out string error)
        {
            var validKind = IsKnownContainer(slotRef.ContainerKind);
            var validRuntimeId = slotRef.ContainerKind == InventoryContainerKind.Search
                ? slotRef.RuntimeContainerId > 0
                : slotRef.RuntimeContainerId == 0;
            if (!validKind || !validRuntimeId || slotRef.SlotIndex < 0)
            {
                error = "库存槽位引用无效";
                return false;
            }

            error = null;
            return true;
        }

        private bool CanModifySlotRefs(InventorySlotRef source, InventorySlotRef target, out string error)
        {
            var sourceSearch = source.ContainerKind == InventoryContainerKind.Search;
            var targetSearch = target.ContainerKind == InventoryContainerKind.Search;
            if (!sourceSearch && !targetSearch)
            {
                return CanModifyContainers(source.ContainerKind, target.ContainerKind, out error);
            }

            if (source.ContainerKind == InventoryContainerKind.Warehouse || target.ContainerKind == InventoryContainerKind.Warehouse ||
                sourceSearch && targetSearch && source.RuntimeContainerId != target.RuntimeContainerId)
            {
                error = "不允许搜索容器组合操作";
                return false;
            }

            var containerId = sourceSearch ? source.RuntimeContainerId : target.RuntimeContainerId;
            if (containerId<=0||containerId>int.MaxValue||!this.GetSystem<SearchContainerSystem>().IsCurrentSession((int)containerId))
            {
                error = "搜索容器会话未激活";
                return false;
            }

            if (!CanModifyPlayerBag(out error))
            {
                return false;
            }

            error = null;
            return true;
        }

        private int ResolveMaxStack(int itemId)
        {
            return this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out var item, out _)
                ? item.MaxStackAmount
                : 0;
        }

        private static bool TryRemoveItemAmount(
            ItemContainer container,
            int itemId,
            int amount,
            out string error)
        {
            error = null;
            if (amount == 0)
            {
                return true;
            }

            var remaining = amount;
            foreach (var pair in container.Slots.OrderBy(pair => pair.Key).ToList())
            {
                if (pair.Value.ItemId != itemId)
                {
                    continue;
                }

                var remove = Math.Min(remaining, pair.Value.Amount);
                if (!container.TryRemove(pair.Key, remove, out _, out error))
                {
                    return false;
                }

                remaining -= remove;
                if (remaining == 0)
                {
                    return true;
                }
            }

            error = "容器材料不足";
            return false;
        }

        /// <summary>按调用者指定的容器优先级扣除；先校验合计数量，失败时恢复所有参与容器。</summary>
        private static bool TryRemoveAcrossContainers(ItemContainer[] containers, int itemId, int amount, out string error)
        {
            error = null;
            if (amount < 0 || containers.Sum(c => (long)c.GetTotalAmount(itemId)) < amount)
            { error = "容器物品不足或消耗数量无效"; return false; }
            if (amount == 0) return true;
            var snapshots = containers.Select(c => c.CaptureSnapshot()).ToArray();
            var remaining = amount;
            for (var i = 0; i < containers.Length && remaining > 0; i++)
            {
                var take = Math.Min(remaining, containers[i].GetTotalAmount(itemId));
                if (!TryRemoveItemAmount(containers[i], itemId, take, out error))
                {
                    for (var j = 0; j < containers.Length; j++) containers[j].RestoreSnapshot(snapshots[j]);
                    return false;
                }
                remaining -= take;
            }
            return true;
        }

        private IReadOnlyList<CraftMaterialQuote> BuildCraftMaterialQuotes(CraftRecipeDefinition recipe, int batchCount)
        {
            var warehouse = this.GetModel<WarehouseModel>().Container;
            return recipe.Materials
                .OrderBy(material => material.ItemId)
                .Select(material => new CraftMaterialQuote(
                    material.ItemId,
                    checked(material.Amount * batchCount),
                    GetCarriedItemAmount(material.ItemId),
                    warehouse.GetTotalAmount(material.ItemId)))
                .ToArray();
        }

        private bool TryBuildCraftAmounts(
            CraftRecipeDefinition recipe,
            int batchCount,
            out IReadOnlyList<CraftMaterialQuote> materials,
            out int outputAmount,
            out CraftFailureReason reason,
            out string error)
        {
            materials = Array.Empty<CraftMaterialQuote>();
            outputAmount = 0;
            if (batchCount < 1 || batchCount > 99)
            {
                reason = CraftFailureReason.InvalidBatchCount;
                error = "制作批次数量必须介于 1 和 99";
                return false;
            }

            try
            {
                outputAmount = checked(recipe.OutputAmount * batchCount);
                materials = BuildCraftMaterialQuotes(recipe, batchCount);
            }
            catch (OverflowException)
            {
                reason = CraftFailureReason.ConfigUnavailable;
                error = $"制作配方[{recipe.Id}] 数量溢出";
                return false;
            }

            reason = CraftFailureReason.None;
            error = null;
            return true;
        }

        private bool TryValidateCraftRecipeDefinition(
            CraftRecipeDefinition recipe,
            out CraftFailureReason reason,
            out string error)
        {
            reason = CraftFailureReason.ConfigUnavailable;
            if (recipe == null)
            {
                error = "制作配方为空";
                return false;
            }

            if (string.IsNullOrWhiteSpace(recipe.Id))
            {
                error = "制作配方 Id 为空";
                return false;
            }

            var configs = this.GetUtility<IRuntimeConfigProvider>();
            if (recipe.OutputItemId <= 0)
            {
                error = $"制作配方[{recipe.Id}].OutputItemId 必须为正数";
                return false;
            }

            if (!configs.TryGetItem(recipe.OutputItemId, out _, out var itemError))
            {
                error = $"制作配方[{recipe.Id}].OutputItemId 无效：{itemError}";
                return false;
            }

            if (recipe.OutputAmount <= 0)
            {
                error = $"制作配方[{recipe.Id}].OutputAmount 必须为正数";
                return false;
            }

            if (recipe.Materials == null || recipe.Materials.Count == 0)
            {
                error = $"制作配方[{recipe.Id}].Materials 为空";
                return false;
            }

            var materialIds = new HashSet<int>();
            foreach (var material in recipe.Materials)
            {
                if (material.ItemId <= 0)
                {
                    error = $"制作配方[{recipe.Id}].Materials.ItemId 必须为正数";
                    return false;
                }

                if (material.Amount <= 0)
                {
                    error = $"制作配方[{recipe.Id}].Materials.Amount 必须为正数";
                    return false;
                }

                if (!materialIds.Add(material.ItemId))
                {
                    error = $"制作配方[{recipe.Id}].Materials.ItemId 重复";
                    return false;
                }

                if (!configs.TryGetItem(material.ItemId, out _, out itemError))
                {
                    error = $"制作配方[{recipe.Id}].Materials.ItemId 无效：{itemError}";
                    return false;
                }
            }

            reason = CraftFailureReason.None;
            error = null;
            return true;
        }

        private bool TrySimulateCraft(CraftRecipeDefinition recipe, int batchCount, out CraftFailureReason reason, out string error)
        {
            reason = CraftFailureReason.InventoryOperationFailed;
            var bag = new ItemContainer(this.GetModel<PlayerBagModel>().Container.Capacity);
            var warehouse = new ItemContainer(this.GetModel<WarehouseModel>().Container.Capacity);
            bag.RestoreSnapshot(this.GetModel<PlayerBagModel>().Container.CaptureSnapshot());
            warehouse.RestoreSnapshot(this.GetModel<WarehouseModel>().Container.CaptureSnapshot());

            var pocket = new ItemContainer(this.GetModel<PlayerBagModel>().Pocket.Capacity);
            pocket.RestoreSnapshot(this.GetModel<PlayerBagModel>().Pocket.CaptureSnapshot());

            if (!TryBuildCraftAmounts(recipe, batchCount, out var materials, out _, out reason, out error))
            {
                return false;
            }

            foreach (var material in materials.OrderBy(material => material.ItemId))
            {
                if ((long)bag.GetTotalAmount(material.ItemId) + pocket.GetTotalAmount(material.ItemId) + warehouse.GetTotalAmount(material.ItemId) < material.RequiredAmount)
                {
                    reason = CraftFailureReason.MaterialsInsufficient;
                    error = "制作材料不足";
                    return false;
                }

                if (!TryRemoveAcrossContainers(new[] { bag, pocket, warehouse }, material.ItemId, material.RequiredAmount, out error))
                {
                    reason = CraftFailureReason.MaterialsInsufficient;
                    return false;
                }
            }

            reason = CraftFailureReason.None;
            error = null;
            return true;
        }

        private bool TryAddAcrossBagThenWarehouse(
            ItemContainer bag,
            ItemContainer warehouse,
            int itemId,
            int amount,
            out int bagAccepted,
            out int warehouseAccepted,
            out int remaining,
            out string error)
        {
            bagAccepted = 0;
            warehouseAccepted = 0;
            remaining = amount;
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out var item, out error))
            {
                return false;
            }

            if (remaining > 0 && bag.TryAdd(itemId, remaining, item.MaxStackAmount, out bagAccepted, out error))
            {
                remaining -= bagAccepted;
            }

            if (remaining > 0 && warehouse.TryAdd(itemId, remaining, item.MaxStackAmount, out warehouseAccepted, out error))
            {
                remaining -= warehouseAccepted;
            }

            error = null;
            return true;
        }

        private void SimulateCraftStorage(
            int itemId,
            int amount,
            out int bagAccepted,
            out int warehouseAccepted,
            out int remaining,
            out string error)
        {
            bagAccepted = 0;
            warehouseAccepted = 0;
            remaining = amount;
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out var item, out error))
            {
                return;
            }

            var bag = new ItemContainer(this.GetModel<PlayerBagModel>().Container.Capacity);
            var warehouse = new ItemContainer(this.GetModel<WarehouseModel>().Container.Capacity);
            bag.RestoreSnapshot(this.GetModel<PlayerBagModel>().Container.CaptureSnapshot());
            warehouse.RestoreSnapshot(this.GetModel<WarehouseModel>().Container.CaptureSnapshot());

            bagAccepted = bag.CalculateAcceptableAmount(itemId, remaining, item.MaxStackAmount);
            remaining -= bagAccepted;
            warehouseAccepted = warehouse.CalculateAcceptableAmount(itemId, remaining, item.MaxStackAmount);
            remaining -= warehouseAccepted;
            error = null;
        }

        private bool CanModifyContainers(
            InventoryContainerKind sourceKind,
            InventoryContainerKind targetKind,
            out string error)
        {
            if (!IsKnownContainer(sourceKind) || !IsKnownContainer(targetKind))
            {
                error = "库存容器类型无效";
                return false;
            }

            if (sourceKind == InventoryContainerKind.Search || targetKind == InventoryContainerKind.Search)
            {
                error = "搜索容器需要运行时容器 Id";
                return false;
            }

            var involvesWarehouse = sourceKind == InventoryContainerKind.Warehouse ||
                                    targetKind == InventoryContainerKind.Warehouse;
            if (involvesWarehouse)
            {
                return CanModifyWarehouse(out error);
            }

            return CanModifyPlayerBag(out error);
        }

        /// <summary>校验随身背包事务条件；显式测试指令可允许暂停，风险状态和远征结算条件仍需满足。</summary>
        /// <param name="allowPaused">仅测试指令传 true；普通玩法保持游戏中状态校验。</param>
        public bool CanModifyPlayerBag(out string error, bool allowPaused = false)
        {
            if (CanModifyBaseBag(allowPaused) || CanModifyExpeditionBag(allowPaused))
            {
                error = null;
                return true;
            }

            error = "当前状态不允许玩家背包操作";
            return false;
        }

        private bool CanModifyWarehouse(out string error)
        {
            if (this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing)
            {
                error = "仓库操作需要处于游戏中状态";
                return false;
            }

            if (!string.Equals(this.GetModel<MapSceneModel>().CurrentMapId, MapIds.Base, StringComparison.Ordinal))
            {
                error = "仓库操作需要位于基地地图";
                return false;
            }

            if (this.GetModel<PlayerBagModel>().RiskState != PlayerBagRiskState.BaseSafe)
            {
                error = "仓库操作需要玩家背包处于安全状态";
                return false;
            }

            error = null;
            return true;
        }

        private bool CanModifyBaseBag(bool allowPaused = false)
        {
            var state = this.GetModel<AppStateModel>().CurrentState.Value;
            return (state == AppState.Playing || allowPaused && state == AppState.Paused) &&
                   string.Equals(this.GetModel<MapSceneModel>().CurrentMapId, MapIds.Base, StringComparison.Ordinal) &&
                   this.GetModel<PlayerBagModel>().RiskState == PlayerBagRiskState.BaseSafe;
        }

        private bool CanUseBasePowerItems()
        {
            return CanModifyBaseBag() &&
                   !this.GetModel<ExpeditionModel>().IsActive;
        }

        private bool CanModifyExpeditionBag(bool allowPaused = false)
        {
            var expedition = this.GetModel<ExpeditionModel>();
            var state = this.GetModel<AppStateModel>().CurrentState.Value;
            return (state == AppState.Playing || allowPaused && state == AppState.Paused) &&
                   this.GetModel<PlayerBagModel>().RiskState == PlayerBagRiskState.InExpedition &&
                   (expedition.Status == ExpeditionStatus.Playing ||
                    expedition.Status == ExpeditionStatus.ReturnQualified) &&
                   this.GetModel<CombatModel>().PlayerHP.Value > 0;
        }

        private static bool IsKnownContainer(InventoryContainerKind kind)
        {
            return kind == InventoryContainerKind.PlayerBag ||
                   kind == InventoryContainerKind.Pocket ||
                   kind == InventoryContainerKind.Warehouse ||
                   kind == InventoryContainerKind.Search;
        }

        /// <summary>背包与口袋都是玩家随身容器；仓库和搜索容器沿用各自许可。</summary>
        public static bool IsCarriedContainer(InventoryContainerKind kind) =>
            kind == InventoryContainerKind.PlayerBag || kind == InventoryContainerKind.Pocket;

        private void MarkInventoryChanged(string reason)
        {
            this.GetSystem<WeaponEquipmentSystem>().ValidateSourceAfterInventoryMutation();
            this.GetSystem<SaveSystem>().MarkDirty(reason);
            try
            {
                this.SendEvent(new InventoryChangedEvent());
            }
            catch (Exception e)
            {
                Log.Error($"[InventorySystem] InventoryChangedEvent 监听器失败：{e.Message}");
            }
        }
    }
}
