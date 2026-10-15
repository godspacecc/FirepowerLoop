using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    public readonly struct EnemyCorpseContentsRequestedEvent
    {
        public EnemyCorpseContentsRequestedEvent(long mapInstanceId, int sourceEnemyUnitId, Vector3 position, Quaternion rotation, IReadOnlyList<LootDropBatchEntry> entries)
        {
            MapInstanceId = mapInstanceId;
            SourceEnemyUnitId = sourceEnemyUnitId;
            Position = position;
            Rotation = rotation;
            Entries = entries;
        }

        public long MapInstanceId { get; }
        public int SourceEnemyUnitId { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public IReadOnlyList<LootDropBatchEntry> Entries { get; }
    }

    public readonly struct SearchContainerChangedEvent
    {
        public SearchContainerChangedEvent(int containerId) { ContainerId = containerId; }
        public int ContainerId { get; }
    }

    public readonly struct SearchSlotSnapshot
    {
        public SearchSlotSnapshot(int slotIndex, int itemId, int amount)
        {
            SlotIndex = slotIndex;
            ItemId = itemId;
            Amount = amount;
        }

        public int SlotIndex { get; }
        public int ItemId { get; }
        public int Amount { get; }
    }

    public sealed class SearchContainerSnapshot
    {
        public SearchContainerSnapshot(int containerId, long mapInstanceId, int capacity, IReadOnlyList<SearchSlotSnapshot> slots)
        {
            ContainerId = containerId;
            MapInstanceId = mapInstanceId;
            Capacity = capacity;
            Slots = slots;
        }

        public int ContainerId { get; }
        public long MapInstanceId { get; }
        public int Capacity { get; }
        public IReadOnlyList<SearchSlotSnapshot> Slots { get; }
    }

    public sealed class SearchContainerSystem : AbstractSystem
    {
        public const float SearchDurationSeconds = 1f;
        public const float SearchDistance = 1.5f;

        private int pendingContainerId;
        private long pendingActionId;
        private int openContainerId;

        public bool IsSearching => pendingContainerId > 0;
        public int OpenContainerId => openContainerId;

        protected override void OnInit()
        {
            this.RegisterEvent<EnemyCorpseContentsRequestedEvent>(OnContentsRequested);
            // 地图候选开始时仍可能需要回滚到旧地图，不能提前清空活动容器。
            this.RegisterEvent<MapLoadingStartedEvent>(_ => { CancelSearch(); openContainerId = 0; });
            this.RegisterEvent<AppStateChangedEvent>(OnAppStateChanged);
            this.RegisterEvent<ExpeditionChangedEvent>(_ => ValidateStateOrCancel());
            this.RegisterEvent<CombatDamageAppliedEvent>(OnDamageApplied);
            this.RegisterEvent<UnitRemovedEvent>(OnUnitRemoved);
            this.RegisterEvent<PlayerActionEndedEvent>(OnPlayerActionEnded);
            LifeCycle.RegisterUpdate(OnUpdate);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(OnUpdate);
            ClearAll();
        }

        public bool TryBeginSearch(int containerId, out string error)
        {
            if (UIHelper.HasUIOpen)
            {
                error = "已打开的 UI 阻止搜索";
                return false;
            }

            this.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.Replaced);
            pendingContainerId = 0;
            pendingActionId = 0;

            if (openContainerId > 0)
            {
                error = "搜索容器已打开";
                return false;
            }

            if (!ValidateSearchTarget(containerId, true, out error))
            {
                return false;
            }

            pendingContainerId = containerId;
            if (!this.GetSystem<PlayerActionSystem>().TryBegin(new PlayerActionRequest(PlayerActionKind.Search, SearchDurationSeconds, string.Empty), out pendingActionId, out error))
            {
                pendingContainerId = 0;
                return false;
            }
            return true;
        }

        public void CancelSearch()
        {
            if (pendingActionId > 0) this.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.OwnerCanceled);
            pendingContainerId = 0;
            pendingActionId = 0;
        }

        public void CloseOpenSession(int containerId)
        {
            if (containerId <= 0 || openContainerId == containerId)
            {
                openContainerId = 0;
            }
        }

        public bool IsCurrentSession(int containerId)
        {
            return containerId > 0 && openContainerId == containerId &&
                   this.GetModel<SearchContainerModel>().TryGet(containerId, out _);
        }

        public bool CanSearch(int containerId)
        {
            return !IsSearching && openContainerId == 0 && ValidateSearchTarget(containerId, true, out _);
        }

        public bool TryGetSnapshot(int containerId, out SearchContainerSnapshot snapshot)
        {
            snapshot = null;
            if (!this.GetModel<SearchContainerModel>().TryGet(containerId, out var record))
            {
                return false;
            }

            var slots = new List<SearchSlotSnapshot>(record.Container.Slots.Count);
            foreach (var pair in record.Container.Slots)
            {
                slots.Add(new SearchSlotSnapshot(pair.Key, pair.Value.ItemId, pair.Value.Amount));
            }

            slots.Sort((left, right) => left.SlotIndex.CompareTo(right.SlotIndex));
            snapshot = new SearchContainerSnapshot(record.ContainerId, record.MapInstanceId, record.Container.Capacity, slots.AsReadOnly());
            return true;
        }

        /// <summary>仅构造搜索容器候选；调用方必须在地图事务提交时统一注册。</summary>
        internal bool TryPrepareSnapshot(ExpeditionSearchContainerSaveData snapshot, long mapInstanceId, out SearchContainerRecord record, out string error)
        {
            record = null;
            error = null;
            if (snapshot == null || snapshot.Pose == null || snapshot.RuntimeId <= 0)
            {
                error = "搜索容器恢复失败：快照实体无效";
                return false;
            }
            var id = snapshot.RuntimeId;
            var position = new Vector3(snapshot.Pose.X, snapshot.Pose.Y, snapshot.Pose.Z);
            var capacity = snapshot.Capacity;
            var sourceEnemyId = snapshot.SourceEnemyRuntimeId;
            if (capacity <= 0 || sourceEnemyId <= 0)
            {
                error = "搜索容器恢复失败：容器字段无效";
                return false;
            }
            var rawSlots = new JArray();
            foreach (var slot in snapshot.Slots ?? new List<ItemSlotSaveData>())
            {
                rawSlots.Add(new JObject { [InventorySaveContracts.IndexKey] = slot.Index, [InventorySaveContracts.ItemIdKey] = slot.ItemId, [InventorySaveContracts.AmountKey] = slot.Amount,["loadedAmmo"]=slot.LoadedAmmo,["torchRemainingSeconds"]=slot.TorchRemainingSeconds.HasValue?new JValue(slot.TorchRemainingSeconds.Value):JValue.CreateNull(),["originExpeditionId"]=slot.OriginExpeditionId,["acquiredAmount"]=slot.AcquiredAmount,["loadedAcquiredAmount"]=slot.LoadedAcquiredAmount });
            }
            if (!InventorySaveContracts.ValidateSlots(rawSlots, capacity, this.GetUtility<IRuntimeConfigProvider>(), out var slots, out error)) return false;
            var container = InventorySaveContracts.RestoreContainer(capacity, slots);
            record = new SearchContainerRecord(id, mapInstanceId, sourceEnemyId, position, container);
            return true;
        }

        /// <summary>提交一组已准备候选；Model 会在发现重复 ID 时保持不变。</summary>
        internal bool CommitPrepared(IReadOnlyList<SearchContainerRecord> candidates, out string error)
        {
            if (!this.GetModel<SearchContainerModel>().TryRegisterRange(candidates))
            {
                error = "搜索容器恢复失败：容器 ID 重复";
                return false;
            }

            error = null;
            return true;
        }

        public void ReleaseMap(long mapInstanceId)
        {
            if (mapInstanceId <= 0) return;
            this.GetModel<SearchContainerModel>().RemoveByMapInstance(mapInstanceId);
            if (pendingContainerId > 0 || openContainerId > 0)
            {
                CancelSearch();
                openContainerId = 0;
            }
        }

        public bool TryGetPendingProgress(int containerId, out float progress01)
        {
            progress01 = 0f;
            if (containerId <= 0 || pendingContainerId != containerId)
            {
                return false;
            }

            if (!this.GetSystem<PlayerActionSystem>().TryGetSnapshot(out var snapshot) || snapshot.ActionId != pendingActionId || snapshot.Kind != PlayerActionKind.Search) return false;
            progress01 = snapshot.Progress01;
            return true;
        }

        internal bool TryResolveMutableContainer(int containerId, out ItemContainer container)
        {
            container = null;
            if (!this.GetModel<SearchContainerModel>().TryGet(containerId, out var record))
            {
                return false;
            }

            container = record.Container;
            return true;
        }

        private void OnContentsRequested(EnemyCorpseContentsRequestedEvent evt)
        {
            if (this.GetModel<SearchContainerModel>().TryGet(evt.SourceEnemyUnitId, out _))
            {
                return;
            }

            if (TryBuildCandidate(evt, out var record, out _))
            {
                if (this.GetModel<SearchContainerModel>().TryRegister(record))
                {
                    this.SendEvent(new SearchContainerChangedEvent(record.ContainerId));
                    return;
                }
            }

            this.SendEvent(new LootDropBatchRequestedEvent(evt.MapInstanceId, evt.SourceEnemyUnitId, evt.Position, evt.Rotation, evt.Entries));
        }

        private bool TryBuildCandidate(EnemyCorpseContentsRequestedEvent evt, out SearchContainerRecord record, out string error)
        {
            record = null;
            error = null;
            if (evt.MapInstanceId <= 0 || evt.SourceEnemyUnitId <= 0 || evt.Entries == null || evt.Entries.Count == 0)
            {
                error = "尸体请求无效";
                return false;
            }

            var resolved = new List<(int itemId, int amount, int maxStack)>(evt.Entries.Count);
            var requiredSlots = 0;
            for (var i = 0; i < evt.Entries.Count; i++)
            {
                var entry = evt.Entries[i];
                if (string.IsNullOrWhiteSpace(entry.LootConfigId) || entry.AmountOverride < 0 ||
                    !this.GetUtility<IRuntimeConfigProvider>().TryGetLoot(entry.LootConfigId, out var loot, out error) ||
                    !this.GetUtility<IRuntimeConfigProvider>().TryGetItem(loot.ItemId, out var item, out error))
                {
                    return false;
                }

                var amount = entry.AmountOverride > 0 ? entry.AmountOverride : loot.Amount;
                if (amount <= 0 || item.MaxStackAmount <= 0)
                {
                    error = "尸体物品数量或堆叠上限无效";
                    return false;
                }

                requiredSlots += Mathf.CeilToInt((float)amount / item.MaxStackAmount);
                resolved.Add((loot.ItemId, amount, item.MaxStackAmount));
            }

            var candidate = new ItemContainer(Mathf.Max(6, requiredSlots));
            for (var i = 0; i < resolved.Count; i++)
            {
                var item = resolved[i];
                if (!candidate.TryAdd(item.itemId, item.amount, item.maxStack, out var accepted, out error,this.GetSystem<InventorySystem>().CreateAcquisition(item.itemId,item.amount)) || accepted != item.amount)
                {
                    error = error ?? "尸体容器无法接收全部物品";
                    return false;
                }
            }

            record = new SearchContainerRecord(evt.SourceEnemyUnitId, evt.MapInstanceId, evt.SourceEnemyUnitId, evt.Position, candidate);
            error = null;
            return true;
        }

        private void OnUpdate()
        {
            if (!IsSearching)
            {
                return;
            }

            if (!ValidateSearchTarget(pendingContainerId, true, out _))
            {
                CancelSearch();
                return;
            }

            if (UIHelper.HasUIOpen)
            {
                CancelSearch();
                return;
            }

        }

        private void OnPlayerActionEnded(PlayerActionEndedEvent evt)
        {
            if (evt.Kind != PlayerActionKind.Search || evt.ActionId != pendingActionId) return;
            var completedContainerId = pendingContainerId;
            pendingContainerId = 0;
            pendingActionId = 0;
            if (!evt.Completed || UIHelper.HasUIOpen || !ValidateSearchTarget(completedContainerId, true, out _)) return;
            openContainerId = completedContainerId;
            if (!SearchContainerUICtrl.TryShow(completedContainerId, out _)) openContainerId = 0;
        }

        private bool ValidateSearchTarget(int containerId, bool requireContents, out string error)
        {
            if (!this.GetModel<SearchContainerModel>().TryGet(containerId, out var record))
            {
                error = "搜索容器 不存在";
                return false;
            }

            var expedition = this.GetModel<ExpeditionModel>();
            if (this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing ||
                !string.Equals(this.GetModel<MapSceneModel>().CurrentMapId, expedition.CurrentMapId, System.StringComparison.Ordinal) ||
                this.GetModel<CombatModel>().PlayerHP.Value <= 0 ||
                this.GetModel<PlayerBagModel>().RiskState != PlayerBagRiskState.InExpedition ||
                expedition.Status != ExpeditionStatus.Playing && expedition.Status != ExpeditionStatus.ReturnQualified ||
                !this.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var player) ||
                !player.IsActive || player.MapInstanceId != record.MapInstanceId ||
                !this.GetSystem<UnitSystem>().TryGet(record.SourceEnemyUnitId, out var corpse) ||
                !corpse.IsActive || corpse.MapInstanceId != record.MapInstanceId ||
                (player.Position - record.Position).sqrMagnitude > SearchDistance * SearchDistance ||
                requireContents && record.Container.Slots.Count == 0)
            {
                error = "搜索条件未满足";
                return false;
            }

            error = null;
            return true;
        }

        private void OnDamageApplied(CombatDamageAppliedEvent evt)
        {
            if (evt.TargetUnitId == PlayerModel.LocalPlayerUnitId && evt.Damage > 0)
            {
                CancelSearch();
            }
        }

        private void OnAppStateChanged(AppStateChangedEvent evt)
        {
            if (evt.Current != AppState.Playing)
            {
                CancelSearch();
                openContainerId = 0;
            }
        }

        private void ValidateStateOrCancel()
        {
            var status = this.GetModel<ExpeditionModel>().Status;
            if (status != ExpeditionStatus.Playing && status != ExpeditionStatus.ReturnQualified)
            {
                CancelSearch();
                openContainerId = 0;
            }
        }

        private void OnUnitRemoved(UnitRemovedEvent evt)
        {
            var id = evt.Snapshot.UnitId;
            if (id == PlayerModel.LocalPlayerUnitId)
            {
                CancelSearch();
                openContainerId = 0;
                return;
            }

            if (this.GetModel<SearchContainerModel>().Remove(id))
            {
                if (pendingContainerId == id) CancelSearch();
                if (openContainerId == id) openContainerId = 0;
                this.SendEvent(new SearchContainerChangedEvent(id));
            }
        }

        private void ClearAll()
        {
            CancelSearch();
            openContainerId = 0;
            this.GetModel<SearchContainerModel>().Clear();
        }
    }
}
