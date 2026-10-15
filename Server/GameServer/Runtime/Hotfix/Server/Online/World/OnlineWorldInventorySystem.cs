using System;
using System.Collections.Generic;
using System.Linq;

namespace ET.Server
{
    [FriendOf(typeof(OnlineWorldComponent))]
    public static class OnlineWorldInventorySystem
    {
        private const float SearchDistance = 1.5f;

        public static OnlineInventoryRecord CopyAndValidateInventory(this OnlineWorldComponent self, OnlineInventoryInfo message, long playerId)
        {
            OnlineInventoryRecord value = OnlineInventoryRecordHelper.FromMessage(message);
            OnlineAccountComponentSystem.ValidateInventory(value, $"WorldEnter PlayerId={playerId}");
            return OnlineInventoryRecordHelper.Clone(value);
        }

        public static OnlineInventoryInfo InventoryMessage(OnlineInventoryRecord value) => OnlineInventoryRecordHelper.ToMessage(value);

        public static WorldCorpseInfo CorpseMessage(OnlineWorldCorpse corpse)
        {
            WorldCorpseInfo value = WorldCorpseInfo.Create();
            value.CorpseId = corpse.CorpseId; value.SourceKind = corpse.SourceKind; value.SourceUnitId = corpse.SourceUnitId;
            value.SourcePlayerId = corpse.SourcePlayerId; value.SourceConfigId = corpse.SourceConfigId; value.WorldId = corpse.WorldId;
            value.MapId = corpse.MapId; value.PositionX = corpse.PositionX; value.PositionY = corpse.PositionY; value.PositionZ = corpse.PositionZ;
            value.YawDegrees = corpse.YawDegrees; value.ExpiresAtServerTime = corpse.ExpiresAtServerTime;
            value.HasContents = corpse.Slots.Count > 0; value.StateRevision = corpse.StateRevision;
            return value;
        }

        public static WorldCorpseContentsInfo CorpseContentsMessage(OnlineWorldCorpse corpse)
        {
            WorldCorpseContentsInfo value = WorldCorpseContentsInfo.Create(); value.Corpse = CorpseMessage(corpse); value.Capacity = corpse.Capacity;
            foreach (OnlineItemSlotRecord source in corpse.Slots.OrderBy(x => x.SlotIndex))
            {
                OnlineItemSlotInfo slot = OnlineItemSlotInfo.Create(); slot.SlotIndex = source.SlotIndex; slot.ItemId = source.ItemId; slot.Amount = source.Amount; slot.LoadedAmmo = source.LoadedAmmo; value.Slots.Add(slot);
            }
            return value;
        }

        public static (OnlineInventoryInfo Inventory, WorldCorpseContentsInfo Corpse) OpenCorpse(this OnlineWorldComponent self, P2W_OpenWorldCorpse request)
        {
            OnlineWorldMapState state = RequireMember(self, request.PlayerId, request.WorldId, request.MapId);
            OnlineWorldUnit unit = RequireActiveUnit(state, request.PlayerId, request.UnitId, request.LifecycleRevision);
            OnlineWorldCorpse corpse = RequireSearchableCorpse(state, unit, request.CorpseId);
            return (InventoryMessage(state.PlayerInventories[request.PlayerId]), CorpseContentsMessage(corpse));
        }

        public static (int Error, bool Accepted, OnlineInventoryInfo Inventory, bool HasCorpse, WorldCorpseContentsInfo Corpse) Operate(
            this OnlineWorldComponent self, P2W_OperateWorldInventory request)
        {
            OnlineWorldMapState state = RequireMember(self, request.PlayerId, request.WorldId, request.MapId);
            OnlineWorldUnit unit = RequireActiveUnit(state, request.PlayerId, request.UnitId, request.LifecycleRevision);
            if (unit.IsLeaving) throw new RpcException(ErrorCode.ERR_OnlineWorldStateConflict, "world leave is prepared");
            if (string.IsNullOrWhiteSpace(request.OperationId)) throw new RpcException(ErrorCode.ERR_OnlineWorldOperationInvalid, "OperationId is required");
            if (request.InventorySchemaVersion != 1) throw new RpcException(ErrorCode.ERR_OnlineWorldOperationInvalid, "inventory schema mismatch");
            string fingerprint = Fingerprint(request);
            Dictionary<string, OnlineWorldInventoryOperationRecord> operations = GetOperations(state, request.PlayerId);
            if (operations.TryGetValue(request.OperationId, out OnlineWorldInventoryOperationRecord old))
            {
                if (!string.Equals(old.Fingerprint, fingerprint, StringComparison.Ordinal)) throw new RpcException(ErrorCode.ERR_OnlineWorldOperationConflict, "OperationId candidate conflict");
                state.Corpses.TryGetValue(old.CorpseId, out OnlineWorldCorpse currentCorpse);
                return (old.Error, old.Accepted, InventoryMessage(state.PlayerInventories[request.PlayerId]), currentCorpse != null, currentCorpse == null ? null : CorpseContentsMessage(currentCorpse));
            }

            OnlineInventoryRecord current = state.PlayerInventories[request.PlayerId];
            OnlineWorldCorpse corpse = null;
            if (request.CorpseId != 0) corpse = RequireSearchableCorpse(state, unit, request.CorpseId);
            if (request.ExpectedInventoryRevision != current.Revision || request.CorpseId != 0 && request.ExpectedCorpseRevision != corpse.StateRevision)
                return RecordResult(operations, request, ErrorCode.ERR_OnlineWorldInventoryRevisionConflict, false, current, corpse);

            OnlineInventoryRecord candidate = OnlineInventoryRecordHelper.Clone(current);
            List<OnlineItemSlotRecord> corpseSlots = corpse?.Slots.Select(CloneSlot).ToList();
            int error = ApplyOperation(request, candidate, corpse, corpseSlots);
            if (error != ErrorCode.ERR_Success) return RecordResult(operations, request, error, false, current, corpse);
            OnlineAccountComponentSystem.ValidateInventory(candidate, $"WorldOperation PlayerId={request.PlayerId}");
            // 重复拿出/收起不推进库存版本或推送；OperationId 重放仍由记录入口统一处理。
            if (request.ActionKind is 9 or 10 && OnlineInventoryRecordHelper.Fingerprint(candidate, false, 0, "") == OnlineInventoryRecordHelper.Fingerprint(current, false, 0, ""))
                return RecordResult(operations, request, ErrorCode.ERR_Success, true, current, corpse);
            candidate.Revision++;
            state.PlayerInventories[request.PlayerId] = candidate;
            bool corpseChanged = corpse != null;
            if (corpseChanged)
            {
                corpse.Slots.Clear(); corpse.Slots.AddRange(corpseSlots); corpse.StateRevision++;
                if (corpse.Slots.Count == 0)
                {
                    state.Corpses.Remove(corpse.CorpseId); state.WorldRevision++; self.BroadcastCorpse(state, null, corpse.CorpseId); corpse = null;
                }
                else { state.WorldRevision++; self.BroadcastCorpse(state, corpse, 0); }
            }
            if(!OnlineInventoryRecordHelper.ClothingItems(candidate).SequenceEqual(OnlineInventoryRecordHelper.ClothingItems(current)))
            {
                unit.TorsoItemId=candidate.TorsoItemId;unit.LegsItemId=candidate.LegsItemId;unit.HeadItemId=candidate.HeadItemId;unit.HandsItemId=candidate.HandsItemId;unit.FeetItemId=candidate.FeetItemId;unit.BackItemId=candidate.BackItemId;unit.ArmorItemId=candidate.ArmorItemId;
                unit.StateRevision++;state.WorldRevision++;BroadcastClothing(self,state,unit);
            }
            self.PushInventory(state, request.PlayerId, candidate);
            return RecordResult(operations, request, ErrorCode.ERR_Success, true, candidate, corpse);
        }

        private static (int Error, bool Accepted, OnlineInventoryInfo Inventory, bool HasCorpse, WorldCorpseContentsInfo Corpse) RecordResult(
            Dictionary<string, OnlineWorldInventoryOperationRecord> operations, P2W_OperateWorldInventory request, int error, bool accepted,
            OnlineInventoryRecord inventory, OnlineWorldCorpse corpse)
        {
            operations[request.OperationId] = new OnlineWorldInventoryOperationRecord
            {
                Fingerprint = Fingerprint(request), Error = error, Accepted = accepted, CorpseId = request.CorpseId,
                HasCorpse = corpse != null, CompletedAt = TimeInfo.Instance.ServerNow()
            };
            TrimOperations(operations);
            return (error, accepted, InventoryMessage(inventory), corpse != null, corpse == null ? null : CorpseContentsMessage(corpse));
        }

        /// <summary>只修改操作候选库存；成功由 Operate 发布，失败丢弃候选，幂等重放由外层操作记录负责。</summary>
        private static int ApplyOperation(P2W_OperateWorldInventory request, OnlineInventoryRecord inventory, OnlineWorldCorpse corpse, List<OnlineItemSlotRecord> corpseSlots)
        {
            if (request.ActionKind == 1)
            {
                if (request.SourceContainerKind is < 1 or > 2 || request.TargetContainerKind is < 1 or > 2) return ErrorCode.ERR_OnlineWorldOperationInvalid;
                List<OnlineItemSlotRecord> source = request.SourceContainerKind == 1 ? inventory.Slots : corpseSlots;
                List<OnlineItemSlotRecord> target = request.TargetContainerKind == 1 ? inventory.Slots : corpseSlots;
                if (source == null || target == null) return ErrorCode.ERR_OnlineWorldCorpseNotFound;
                int sourceCapacity = request.SourceContainerKind == 1 ? inventory.Capacity : corpse.Capacity;
                int targetCapacity = request.TargetContainerKind == 1 ? inventory.Capacity : corpse.Capacity;
                if (request.SourceSlotIndex < 0 || request.SourceSlotIndex >= sourceCapacity || request.TargetSlotIndex < 0 || request.TargetSlotIndex >= targetCapacity)
                    return ErrorCode.ERR_OnlineWorldOperationInvalid;
                OnlineItemSlotRecord item = source.FirstOrDefault(x => x.SlotIndex == request.SourceSlotIndex);
                if (item == null) return ErrorCode.ERR_OnlineWorldOperationInvalid;
                OnlineItemSlotRecord occupied = target.FirstOrDefault(x => x.SlotIndex == request.TargetSlotIndex);
                if (ReferenceEquals(source, target) && request.SourceSlotIndex == request.TargetSlotIndex) return ErrorCode.ERR_Success;
                if (occupied != null)
                {
                    Item config = ServerConfig.Instance.GetItem(item.ItemId);
                    if (occupied.ItemId != item.ItemId || occupied.Amount + item.Amount > config.MaxStackAmount) return ErrorCode.ERR_OnlineWorldInventoryCapacity;
                    occupied.Amount += item.Amount; source.Remove(item);
                }
                else { source.Remove(item); target.Add(new OnlineItemSlotRecord { SlotIndex = request.TargetSlotIndex, ItemId = item.ItemId, Amount = item.Amount, LoadedAmmo = item.LoadedAmmo }); }
                if (request.SourceContainerKind == 1 && inventory.Equipment.SourceSlotIndex == request.SourceSlotIndex && inventory.Equipment.ItemId == item.ItemId)
                {
                    if (request.TargetContainerKind == 1) inventory.Equipment.SourceSlotIndex = request.TargetSlotIndex;
                    else inventory.Equipment = new OnlineEquipmentRecord();
                }
                return ErrorCode.ERR_Success;
            }
            // 穿脱只修改候选；满包替换可回填原格，卸下必须有背包空格。
            if(request.ActionKind==12)
            {
                if(request.SourceContainerKind!=1)return ErrorCode.ERR_OnlineWorldOperationInvalid;
                var source=inventory.Slots.FirstOrDefault(x=>x.SlotIndex==request.SourceSlotIndex);
                if(source==null||source.Amount!=1||source.ItemId!=request.ExpectedItemId)return ErrorCode.ERR_OnlineWorldOperationInvalid;
                var item=ServerConfig.Instance.GetItem(source.ItemId);
                if(item.ItemType!=ItemType.Clothing||(int)item.WearSlot is < 1 or > 7)return ErrorCode.ERR_OnlineWorldOperationInvalid;
                int old=OnlineInventoryRecordHelper.GetClothing(inventory,item.WearSlot);
                inventory.Slots.Remove(source);
                if(old!=0)inventory.Slots.Add(new OnlineItemSlotRecord{SlotIndex=source.SlotIndex,ItemId=old,Amount=1});
                OnlineInventoryRecordHelper.SetClothing(inventory,item.WearSlot,source.ItemId);
                return ErrorCode.ERR_Success;
            }
            if(request.ActionKind==13)
            {
                var slot=(ClothingSlot)request.TargetSlotIndex;
                if((int)slot is < 1 or > 7)return ErrorCode.ERR_OnlineWorldOperationInvalid;
                int old=OnlineInventoryRecordHelper.GetClothing(inventory,slot);
                if(old==0)return ErrorCode.ERR_OnlineWorldOperationInvalid;
                if(!TryAddItem(inventory.Slots,inventory.Capacity,old,1))return ErrorCode.ERR_OnlineWorldInventoryCapacity;
                OnlineInventoryRecordHelper.SetClothing(inventory,slot,0);
                return ErrorCode.ERR_Success;
            }
            if (request.ActionKind == 9)
            {
                if (request.SourceContainerKind != 1) return ErrorCode.ERR_OnlineWorldOperationInvalid;
                var source = inventory.Slots.FirstOrDefault(x => x.SlotIndex == request.SourceSlotIndex);
                if (source == null || source.Amount != 1 || source.ItemId != request.ExpectedItemId || ServerConfig.Instance.GetItem(source.ItemId).ItemType != ItemType.Weapon || ServerConfig.Instance.GetWeapon(source.ItemId).WeaponKind is not (WeaponKind.Melee or WeaponKind.Projectile)) return ErrorCode.ERR_OnlineWorldOperationInvalid;
                inventory.Equipment.ItemId = source.ItemId;
                inventory.Equipment.SourceSlotIndex = source.SlotIndex;
                return ErrorCode.ERR_Success;
            }
            if (request.ActionKind == 10)
            { inventory.Equipment = new OnlineEquipmentRecord(); return ErrorCode.ERR_Success; }
            if (request.ActionKind == 11)
            {
                var held = OnlineInventoryRecordHelper.HeldStack(inventory);
                if (held == null || ServerConfig.Instance.GetWeapon(held.ItemId).WeaponKind != WeaponKind.Projectile || held.ItemId != request.ExpectedItemId || held.SlotIndex != request.SourceSlotIndex) return ErrorCode.ERR_OnlineWorldOperationInvalid;
                var weapon = ServerConfig.Instance.GetWeapon(held.ItemId);
                int missing = weapon.MagazineCapacity - held.LoadedAmmo;
                if (missing <= 0) return ErrorCode.ERR_OnlineWorldOperationInvalid;
                var ammoSlots = inventory.Slots.Where(x => x.ItemId == weapon.AmmoItemId).OrderBy(x => x.SlotIndex).ToArray();
                int amount = Math.Min(weapon.ReloadWholeMagazine ? missing : 1, ammoSlots.Sum(x => x.Amount)), remaining = amount;
                if (amount <= 0) return ErrorCode.ERR_OnlineWorldOperationInvalid;
                foreach (var ammo in ammoSlots)
                { int take=Math.Min(remaining,ammo.Amount); ammo.Amount-=take;remaining-=take;if(ammo.Amount==0)inventory.Slots.Remove(ammo);if(remaining==0)break; }
                held.LoadedAmmo += amount;
                return ErrorCode.ERR_Success;
            }
            return ErrorCode.ERR_OnlineWorldOperationInvalid;
        }

        private static bool TryAddItem(List<OnlineItemSlotRecord> slots, int capacity, int itemId, int amount)
        {
            Item item = ServerConfig.Instance.GetItem(itemId);
            foreach (OnlineItemSlotRecord slot in slots.Where(x => x.ItemId == itemId && x.Amount < item.MaxStackAmount).OrderBy(x => x.SlotIndex))
            {
                int accepted = Math.Min(item.MaxStackAmount - slot.Amount, amount);
                slot.Amount += accepted;
                amount -= accepted;
                if (amount == 0) return true;
            }
            for (int slotIndex = 0; slotIndex < capacity && amount > 0; slotIndex++)
            {
                if (slots.Any(x => x.SlotIndex == slotIndex)) continue;
                int accepted = Math.Min(item.MaxStackAmount, amount);
                slots.Add(new OnlineItemSlotRecord { SlotIndex = slotIndex, ItemId = itemId, Amount = accepted });
                amount -= accepted;
            }
            return amount == 0;
        }

        public static void CreateEnemyCorpse(this OnlineWorldComponent self, OnlineWorldMapState state, OnlineWorldEnemy enemy, long now)
        {
            List<(int ItemId, int Amount)> drops = new();
            AddLoot(drops, enemy.Config.GuaranteedWeaponLootConfigId); AddLoot(drops, enemy.Config.GuaranteedAmmoLootConfigId); AddLoot(drops, enemy.Config.LootConfigId);
            if (drops.Count == 0) return;
            OnlineWorldCorpse corpse = NewCorpse(self, AllocateId(self), 1, enemy.UnitId, 0, enemy.EnemyConfigId, state.MapId,
                enemy.PositionX, enemy.PositionY, enemy.PositionZ, enemy.YawDegrees, now, drops);
            state.Corpses.Add(corpse.CorpseId, corpse); state.WorldRevision++; self.BroadcastCorpse(state, corpse, 0);
        }

        public static void CreateProjectileRecovery(this OnlineWorldComponent self, OnlineWorldMapState state,
            string lootConfigId, float x, float y, float z, float yaw, long now)
        {
            if (string.IsNullOrWhiteSpace(lootConfigId)) return;
            LootConfig loot = ServerConfig.Instance.GetLoot(lootConfigId);
            OnlineWorldCorpse recovery = NewCorpse(self, AllocateId(self), 3, 0, 0, lootConfigId, state.MapId,
                x, y, z, yaw, now, new[] { (loot.ItemId, loot.Amount) });
            state.Corpses.Add(recovery.CorpseId, recovery);
            state.WorldRevision++;
            self.BroadcastCorpse(state, recovery, 0);
        }

        public static void TransitionPlayerDeath(this OnlineWorldComponent self, OnlineWorldMapState state, OnlineWorldUnit unit, long now, bool respawn)
        {
            self.ReleaseOwnerProjectiles(state,unit.UnitId);
            OnlineInventoryRecord inventory = state.PlayerInventories[unit.PlayerId];
            List<(int ItemId, int Amount)> items = inventory.Slots.OrderBy(x => x.SlotIndex).Select(x => (x.ItemId, x.Amount)).ToList();
            foreach(var pair in OnlineInventoryRecordHelper.ClothingItems(inventory))
                if(pair.Item1>0)items.Add((pair.Item1,1));
            // 武器已在库存中；只把每个实例的已装弹药回填掉落一次。
            foreach (var slot in inventory.Slots.Where(x => x.LoadedAmmo > 0)) items.Add((ServerConfig.Instance.GetWeapon(slot.ItemId).AmmoItemId, slot.LoadedAmmo));
            OnlineWorldCorpse corpse = items.Count == 0 ? null : NewCorpse(self, unit.UnitId, 2, unit.UnitId, unit.PlayerId, unit.PlayerConfigId,
                state.MapId, unit.PositionX, unit.PositionY, unit.PositionZ, unit.YawDegrees, now, items);
            OnlineInventoryRecord empty = OnlineInventoryRecordHelper.Empty(ServerConfig.Instance.OnlineWorldRule.BagCapacity); empty.Revision = inventory.Revision + 1;
            state.PlayerInventories[unit.PlayerId] = empty;
            state.Units.Remove(unit.UnitId); state.PlayerUnits.Remove(unit.PlayerId);
            if (respawn) state.PendingRespawns[unit.PlayerId] = new OnlineWorldPendingRespawn
            {
                PlayerId = unit.PlayerId, PreviousUnitId = unit.UnitId, RespawnAtServerTime = now + self.PlayerRespawnMilliseconds,
                DisplayName = unit.DisplayName, PlayerConfigId = unit.PlayerConfigId, PlayerActorId = unit.PlayerActorId,
                ConnectionState = unit.ConnectionState
            };
            else { self.PlayerMapIds.Remove(unit.PlayerId); state.PlayerInventories.Remove(unit.PlayerId); }
            if (corpse != null) state.Corpses.Add(corpse.CorpseId, corpse);
            state.WorldRevision++; self.BroadcastPlayerLeft(state, unit.UnitId); if (corpse != null) self.BroadcastCorpse(state, corpse, 0);
            self.PushSelfUnit(state, unit.PlayerId, unit.PlayerActorId, unit.UnitId, 0); self.PushInventory(state, unit.PlayerId, empty, unit.PlayerActorId);
            self.PushCheckpoint(state, unit.PlayerId, unit.PlayerActorId, "death:" + unit.UnitId, empty);
            self.RecomputeNavigationAfterMembershipChange(state, "player-death");
        }

        public static void RespawnPendingPlayers(this OnlineWorldComponent self, OnlineWorldMapState state, long now)
        {
            foreach (OnlineWorldPendingRespawn pending in state.PendingRespawns.Values.Where(x => now >= x.RespawnAtServerTime).ToArray())
            {
                if (!self.PlayerMapIds.ContainsKey(pending.PlayerId)) { state.PendingRespawns.Remove(pending.PlayerId); continue; }
                long unitId = AllocateId(self); OnlineWorldUnit unit = new()
                {
                    PlayerId=pending.PlayerId,UnitId=unitId,DisplayName=pending.DisplayName,WorldId=self.WorldId,MapId=state.MapId,
                    PlayerConfigId=pending.PlayerConfigId,PositionX=state.PlayerSpawn.PositionX,PositionY=state.PlayerSpawn.PositionY,PositionZ=state.PlayerSpawn.PositionZ,
                    YawDegrees=state.PlayerSpawn.RotationY,ConnectionState=pending.ConnectionState,StateRevision=1,LifecycleRevision=1,
                    CurrentHP=state.PlayerConfig.MaxHP,MaxHP=state.PlayerConfig.MaxHP,PlayerActorId=pending.PlayerActorId
                };
                state.Units.Add(unitId, unit); state.PlayerUnits[pending.PlayerId] = unitId; state.PendingRespawns.Remove(pending.PlayerId);
                state.WorldRevision++; self.PlayerRespawnCount++; self.BroadcastPlayerEntered(state, unit); self.PushSelfUnit(state, pending.PlayerId, pending.PlayerActorId, 0, unitId);
                self.RecomputeNavigationAfterMembershipChange(state, "player-respawn");
            }
        }

        public static bool SetConnectionIncludingPendingRespawn(this OnlineWorldComponent self, P2W_SetWorldConnection request)
        {
            if (request.WorldId == self.WorldId && request.UnitId == 0 &&
                self.PlayerMapIds.TryGetValue(request.PlayerId, out string indexedMap) &&
                string.Equals(indexedMap, request.MapId, StringComparison.Ordinal) &&
                self.MapStates.TryGetValue(request.MapId, out OnlineWorldMapState state) &&
                state.PendingRespawns.TryGetValue(request.PlayerId, out OnlineWorldPendingRespawn pending))
            {
                pending.PlayerActorId = request.PlayerActorId;
                pending.ConnectionState = request.ConnectionState == (int)OnlineWorldConnectionState.DisconnectedGrace
                    ? OnlineWorldConnectionState.DisconnectedGrace
                    : OnlineWorldConnectionState.Online;
                return true;
            }

            if (request.WorldId != self.WorldId ||
                !self.PlayerMapIds.TryGetValue(request.PlayerId, out string activeMap) ||
                !string.Equals(activeMap, request.MapId, StringComparison.Ordinal) ||
                !self.MapStates.TryGetValue(request.MapId, out OnlineWorldMapState activeState) ||
                !activeState.PlayerUnits.TryGetValue(request.PlayerId, out long activeUnitId) ||
                activeUnitId != request.UnitId ||
                !activeState.Units.TryGetValue(activeUnitId, out OnlineWorldUnit unit)) return false;
            OnlineWorldConnectionState next=request.ConnectionState==(int)OnlineWorldConnectionState.DisconnectedGrace?OnlineWorldConnectionState.DisconnectedGrace:OnlineWorldConnectionState.Online;
            bool reconnected=unit.ConnectionState==OnlineWorldConnectionState.DisconnectedGrace&&next==OnlineWorldConnectionState.Online;
            unit.PlayerActorId=request.PlayerActorId;unit.ConnectionState=next;if(reconnected)unit.NavigationEligible=true;
            self.RecomputeNavigationAfterMembershipChange(activeState,next==OnlineWorldConnectionState.DisconnectedGrace?"disconnect":reconnected?"reconnect":"online");
            return true;
        }

        public static OnlineWorldLeaveRecord PrepareLeave(this OnlineWorldComponent self, long playerId, long worldId, string mapId, long unitId, string operationId)
        {
            OnlineWorldMapState state = RequireMember(self, playerId, worldId, mapId); string key = playerId + ":" + operationId;
            if (state.LeaveRecords.TryGetValue(key, out OnlineWorldLeaveRecord old)) return old;
            if (string.IsNullOrWhiteSpace(operationId)) throw new RpcException(ErrorCode.ERR_OnlineWorldOperationInvalid, "OperationId is required");
            if (state.PlayerUnits.TryGetValue(playerId, out long activeId))
            {
                if (activeId != unitId || !state.Units.TryGetValue(activeId, out OnlineWorldUnit unit)) throw new RpcException(ErrorCode.ERR_OnlineWorldStateConflict, "world identity conflict");
                unit.IsLeaving = true;
            }
            else if (!state.PendingRespawns.ContainsKey(playerId) || unitId != 0) throw new RpcException(ErrorCode.ERR_OnlineWorldStateConflict, "world identity conflict");
            OnlineWorldLeaveRecord record = new() { PlayerId=playerId,UnitId=unitId,OperationId=operationId,Prepared=true,Inventory=OnlineInventoryRecordHelper.Clone(state.PlayerInventories[playerId]) };
            state.LeaveRecords.Add(key, record); return record;
        }

        public static bool CommitLeave(this OnlineWorldComponent self, long playerId, long worldId, string mapId, string operationId)
        {
            OnlineWorldMapState state = self.MapStates.TryGetValue(mapId, out OnlineWorldMapState found) ? found : throw new RpcException(ErrorCode.ERR_OnlineWorldOperationInvalid, "invalid map");
            string key = playerId + ":" + operationId;
            if (!state.LeaveRecords.TryGetValue(key, out OnlineWorldLeaveRecord record)) return !self.PlayerMapIds.ContainsKey(playerId);
            if (record.Completed) return true;
            if (record.Aborted) throw new RpcException(ErrorCode.ERR_OnlineWorldOperationConflict, "leave was aborted");
            if (state.PlayerUnits.TryGetValue(playerId, out long unitId)) { self.ReleaseOwnerProjectiles(state,unitId); state.Units.Remove(unitId); state.PlayerUnits.Remove(playerId); state.WorldRevision++; self.BroadcastPlayerLeft(state, unitId); }
            state.PendingRespawns.Remove(playerId); state.PlayerInventories.Remove(playerId); self.PlayerMapIds.Remove(playerId); record.Completed = true;
            self.RecomputeNavigationAfterMembershipChange(state, "leave-commit"); return true;
        }

        public static bool AbortLeave(this OnlineWorldComponent self, long playerId, long worldId, string mapId, string operationId)
        {
            OnlineWorldMapState state = RequireMember(self, playerId, worldId, mapId); string key = playerId + ":" + operationId;
            if (!state.LeaveRecords.TryGetValue(key, out OnlineWorldLeaveRecord record)) return true;
            if (record.Completed) throw new RpcException(ErrorCode.ERR_OnlineWorldOperationConflict, "leave already committed");
            record.Aborted = true; record.Prepared = false; if (state.PlayerUnits.TryGetValue(playerId, out long id) && state.Units.TryGetValue(id, out OnlineWorldUnit unit)) unit.IsLeaving=false; return true;
        }

        public static void CleanupCorpses(this OnlineWorldComponent self, OnlineWorldMapState state, long now)
        {
            foreach (OnlineWorldCorpse corpse in state.Corpses.Values.Where(x => now >= x.ExpiresAtServerTime).ToArray())
            { state.Corpses.Remove(corpse.CorpseId); state.WorldRevision++; self.BroadcastCorpse(state, null, corpse.CorpseId); }
        }

        private static OnlineWorldCorpse NewCorpse(OnlineWorldComponent self,long corpseId,int kind,long sourceUnit,long playerId,string configId,string mapId,float x,float y,float z,float yaw,long now,IEnumerable<(int ItemId,int Amount)> items)
        {
            OnlineWorldCorpse corpse = new(){CorpseId=corpseId,SourceKind=kind,SourceUnitId=sourceUnit,SourcePlayerId=playerId,SourceConfigId=configId,WorldId=self.WorldId,MapId=mapId,PositionX=x,PositionY=y,PositionZ=z,YawDegrees=yaw,ExpiresAtServerTime=now+self.CorpseLifetimeMilliseconds,StateRevision=1};
            foreach ((int itemId, int amount) in items) AddStacked(corpse.Slots, itemId, amount);
            corpse.Capacity = corpse.Slots.Count; return corpse;
        }

        private static void AddLoot(List<(int ItemId,int Amount)> drops,string id){if(string.IsNullOrWhiteSpace(id))return;LootConfig loot=ServerConfig.Instance.GetLoot(id);drops.Add((loot.ItemId,loot.Amount));}
        private static void AddStacked(List<OnlineItemSlotRecord> slots,int itemId,int amount){if(itemId==0||amount<=0)return;int max=ServerConfig.Instance.GetItem(itemId).MaxStackAmount;while(amount>0){int take=Math.Min(max,amount);slots.Add(new OnlineItemSlotRecord{SlotIndex=slots.Count,ItemId=itemId,Amount=take});amount-=take;}}
        private static OnlineItemSlotRecord CloneSlot(OnlineItemSlotRecord x)=>new(){SlotIndex=x.SlotIndex,ItemId=x.ItemId,Amount=x.Amount,LoadedAmmo=x.LoadedAmmo};
        private static string Fingerprint(P2W_OperateWorldInventory x)=>$"{x.ExpectedInventoryRevision}|{x.CorpseId}|{x.ExpectedCorpseRevision}|{x.ActionKind}|{x.SourceContainerKind}|{x.SourceSlotIndex}|{x.TargetContainerKind}|{x.TargetSlotIndex}|{x.LifecycleRevision}|{x.InventorySchemaVersion}|{x.ExpectedItemId}";
        private static Dictionary<string,OnlineWorldInventoryOperationRecord> GetOperations(OnlineWorldMapState state,long playerId){if(!state.InventoryOperations.TryGetValue(playerId,out var value)){value=new Dictionary<string,OnlineWorldInventoryOperationRecord>(StringComparer.Ordinal);state.InventoryOperations.Add(playerId,value);}return value;}
        private static void TrimOperations(Dictionary<string,OnlineWorldInventoryOperationRecord> operations){long now=TimeInfo.Instance.ServerNow();if(operations.Count<=128)return;foreach(string key in operations.Where(x=>now-x.Value.CompletedAt>=600000).OrderBy(x=>x.Value.CompletedAt).Take(operations.Count-128).Select(x=>x.Key).ToArray())operations.Remove(key);}
        private static OnlineWorldMapState RequireMember(OnlineWorldComponent self,long playerId,long worldId,string mapId){if(worldId!=self.WorldId||string.IsNullOrWhiteSpace(mapId)||!self.MapStates.TryGetValue(mapId,out OnlineWorldMapState state)||!self.PlayerMapIds.TryGetValue(playerId,out string indexed)||!string.Equals(indexed,mapId,StringComparison.Ordinal))throw new RpcException(ErrorCode.ERR_OnlineNotInWorld,"player is not in world");return state;}
        private static OnlineWorldUnit RequireActiveUnit(OnlineWorldMapState state,long playerId,long unitId,long lifecycle){if(unitId==0||!state.PlayerUnits.TryGetValue(playerId,out long indexed)||indexed!=unitId||!state.Units.TryGetValue(unitId,out OnlineWorldUnit unit)||unit.IsDead||unit.LifecycleRevision!=lifecycle)throw new RpcException(ErrorCode.ERR_OnlineWorldSearchInvalid,"world search identity invalid");return unit;}
        private static OnlineWorldCorpse RequireSearchableCorpse(OnlineWorldMapState state,OnlineWorldUnit unit,long corpseId){if(!state.Corpses.TryGetValue(corpseId,out OnlineWorldCorpse corpse)||TimeInfo.Instance.ServerNow()>=corpse.ExpiresAtServerTime)throw new RpcException(ErrorCode.ERR_OnlineWorldCorpseNotFound,"corpse not found");float dx=unit.PositionX-corpse.PositionX,dz=unit.PositionZ-corpse.PositionZ;if(dx*dx+dz*dz>SearchDistance*SearchDistance)throw new RpcException(ErrorCode.ERR_OnlineWorldSearchInvalid,"corpse is out of range");return corpse;}

        private static IEnumerable<OnlineWorldUnit> Targets(OnlineWorldMapState state)=>state.Units.Values.Where(x=>x.ConnectionState==OnlineWorldConnectionState.Online&&x.PlayerActorId!=default);
        public static void BroadcastCorpse(this OnlineWorldComponent self,OnlineWorldMapState state,OnlineWorldCorpse corpse,long removed){foreach(OnlineWorldUnit t in Targets(state)){W2P_WorldCorpseChanged m=W2P_WorldCorpseChanged.Create();m.PlayerId=t.PlayerId;m.WorldId=self.WorldId;m.MapId=state.MapId;m.WorldRevision=state.WorldRevision;m.HasCorpse=corpse!=null;m.Corpse=corpse==null?null:CorpseMessage(corpse);m.RemovedCorpseId=removed;self.Root().GetComponent<MessageSender>().Send(t.PlayerActorId,m);}}
        public static void PushInventory(this OnlineWorldComponent self,OnlineWorldMapState state,long playerId,OnlineInventoryRecord inventory,ActorId actorId=default){if(actorId==default&&state.PlayerUnits.TryGetValue(playerId,out long id)&&state.Units.TryGetValue(id,out OnlineWorldUnit unit))actorId=unit.PlayerActorId;if(actorId==default)return;W2P_WorldSelfInventoryChanged m=W2P_WorldSelfInventoryChanged.Create();m.PlayerId=playerId;m.WorldId=self.WorldId;m.MapId=state.MapId;m.SelfInventory=InventoryMessage(inventory);self.Root().GetComponent<MessageSender>().Send(actorId,m);}
        private static void PushCheckpoint(this OnlineWorldComponent self,OnlineWorldMapState state,long playerId,ActorId actorId,string operationId,OnlineInventoryRecord inventory){if(actorId==default)return;W2P_WorldInventoryCheckpoint m=W2P_WorldInventoryCheckpoint.Create();m.PlayerId=playerId;m.WorldId=self.WorldId;m.MapId=state.MapId;m.OperationId=operationId;m.OnlineInventory=InventoryMessage(inventory);self.Root().GetComponent<MessageSender>().Send(actorId,m);}
        private static void PushSelfUnit(this OnlineWorldComponent self,OnlineWorldMapState state,long playerId,ActorId actorId,long previous,long current){if(actorId==default)return;W2P_WorldSelfUnitChanged m=W2P_WorldSelfUnitChanged.Create();m.PlayerId=playerId;m.WorldId=self.WorldId;m.MapId=state.MapId;m.PreviousUnitId=previous;m.SelfUnitId=current;m.WorldRevision=state.WorldRevision;self.Root().GetComponent<MessageSender>().Send(actorId,m);}
        private static void BroadcastPlayerLeft(this OnlineWorldComponent self,OnlineWorldMapState state,long id){foreach(OnlineWorldUnit t in Targets(state)){W2P_WorldPlayerLeft m=W2P_WorldPlayerLeft.Create();m.PlayerId=t.PlayerId;m.WorldId=self.WorldId;m.MapId=state.MapId;m.WorldRevision=state.WorldRevision;m.UnitId=id;self.Root().GetComponent<MessageSender>().Send(t.PlayerActorId,m);}}
        private static void BroadcastPlayerEntered(this OnlineWorldComponent self,OnlineWorldMapState state,OnlineWorldUnit entered){foreach(OnlineWorldUnit t in Targets(state).Where(x=>x.UnitId!=entered.UnitId)){W2P_WorldPlayerEntered m=W2P_WorldPlayerEntered.Create();m.PlayerId=t.PlayerId;m.WorldRevision=state.WorldRevision;m.Player=PlayerMessage(entered);self.Root().GetComponent<MessageSender>().Send(t.PlayerActorId,m);}}
        public static void RecomputeNavigationAfterMembershipChange(this OnlineWorldComponent self,OnlineWorldMapState state,string reason)
        {
            if(state.Enemies.Count==0){state.NavigationExecutorUnitId=0;state.LastAcceptedNavigationReportSequence=0;return;}
            List<long> eligible=state.Units.Values.Where(x=>x.NavigationEligible&&x.ConnectionState==OnlineWorldConnectionState.Online&&x.PlayerActorId!=default&&!x.IsLeaving).Select(x=>x.UnitId).OrderBy(x=>x).ToList();
            long next=eligible.Count==0?0:eligible[0];if(next==state.NavigationExecutorUnitId)return;
            state.NavigationExecutorUnitId=next;state.NavigationLeaseRevision++;state.LastAcceptedNavigationReportSequence=0;state.LastNavigationHeartbeatAt=TimeInfo.Instance.ServerNow();state.WorldRevision++;self.LeaseTransferCount++;
            foreach(OnlineWorldUnit target in Targets(state)){W2P_WorldNavigationLeaseChanged message=W2P_WorldNavigationLeaseChanged.Create();message.PlayerId=target.PlayerId;message.WorldId=self.WorldId;message.MapId=state.MapId;message.WorldRevision=state.WorldRevision;message.NavigationExecutorUnitId=next;message.NavigationLeaseRevision=state.NavigationLeaseRevision;message.ServerTime=state.LastNavigationHeartbeatAt;self.Root().GetComponent<MessageSender>().Send(target.PlayerActorId,message);}
            Log.Info($"online world navigation lease worldId={self.WorldId} mapId={state.MapId} executor={next} revision={state.NavigationLeaseRevision} reason={reason}");
        }
        private static long AllocateId(OnlineWorldComponent self){if(self.NextUnitId<1||self.NextUnitId>int.MaxValue)throw new InvalidOperationException("Online World UnitId exhausted.");return self.NextUnitId++;}
        /// <summary>沿用玩家完整状态推送，避免外观另建可靠性或重连协议。</summary>
        private static void BroadcastClothing(OnlineWorldComponent self,OnlineWorldMapState state,OnlineWorldUnit unit)
        {
            foreach(var target in Targets(state).Where(x=>x.UnitId!=unit.UnitId))
            {
                var message=W2P_WorldPlayerTransformChanged.Create();message.PlayerId=target.PlayerId;
                message.WorldId=self.WorldId;message.MapId=state.MapId;message.WorldRevision=state.WorldRevision;
                message.Player=PlayerMessage(unit);self.Root().GetComponent<MessageSender>().Send(target.PlayerActorId,message);
            }
        }
        private static WorldPlayerInfo PlayerMessage(OnlineWorldUnit p){WorldPlayerInfo i=WorldPlayerInfo.Create();i.TorsoItemId=p.TorsoItemId;i.LegsItemId=p.LegsItemId;i.HeadItemId=p.HeadItemId;i.HandsItemId=p.HandsItemId;i.FeetItemId=p.FeetItemId;i.BackItemId=p.BackItemId;i.ArmorItemId=p.ArmorItemId;i.PlayerId=p.PlayerId;i.UnitId=p.UnitId;i.DisplayName=p.DisplayName;i.WorldId=p.WorldId;i.MapId=p.MapId;i.PlayerConfigId=p.PlayerConfigId;i.PositionX=p.PositionX;i.PositionY=p.PositionY;i.PositionZ=p.PositionZ;i.YawDegrees=p.YawDegrees;i.ConnectionState=(int)p.ConnectionState;i.StateRevision=p.StateRevision;i.CurrentHP=p.CurrentHP;i.MaxHP=p.MaxHP;i.IsDead=p.IsDead;i.RespawnAtServerTime=p.RespawnAtServerTime;i.LastAcceptedAttackSequence=p.LastAcceptedAttackSequence;i.LifecycleRevision=p.LifecycleRevision;return i;}
    }
}
