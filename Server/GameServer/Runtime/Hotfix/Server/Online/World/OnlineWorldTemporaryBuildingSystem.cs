using System;
using System.Collections.Generic;
using System.Linq;

namespace ET.Server
{
    [FriendOf(typeof(OnlineWorldComponent))]
    public static class OnlineWorldTemporaryBuildingSystem
    {
        public const int Build = 1;
        public const int Destroy = 2;
        public const int Reload = 3;
        public const int Fire = 4;

        public static (int Error, bool Accepted, long WorldRevision, WorldTemporaryBuildingInfo Building, int RemovedRuntimeId, OnlineInventoryInfo Inventory) Operate(
            this OnlineWorldComponent self,
            P2W_OperateWorldTemporaryBuilding request)
        {
            OnlineWorldMapState state = RequireActor(self, request, out OnlineWorldUnit actor);
            return request.ActionKind switch
            {
                Build => BuildOne(self, state, actor, request),
                Destroy => DestroyOne(self, state, actor, request),
                Reload => ReloadOne(self, state, actor, request),
                Fire => FireOne(self, state, actor, request),
                _ => Failure(ErrorCode.ERR_OnlineWorldOperationInvalid, state)
            };
        }

        public static WorldTemporaryBuildingInfo Message(OnlineWorldTemporaryBuilding value)
        {
            if (value == null) return null;
            WorldTemporaryBuildingInfo message = WorldTemporaryBuildingInfo.Create();
            message.RuntimeId = value.RuntimeId;
            message.ConfigId = value.ConfigId;
            message.WorldId = value.WorldId;
            message.MapId = value.MapId;
            message.PositionX = value.PositionX;
            message.PositionY = value.PositionY;
            message.PositionZ = value.PositionZ;
            message.YawDegrees = value.YawDegrees;
            message.LoadedAmmo = value.LoadedAmmo;
            message.NextShootAtServerTime = value.NextShootAtServerTime;
            message.StateRevision = value.StateRevision;
            return message;
        }

        private static (int, bool, long, WorldTemporaryBuildingInfo, int, OnlineInventoryInfo) BuildOne(
            OnlineWorldComponent self, OnlineWorldMapState state, OnlineWorldUnit actor, P2W_OperateWorldTemporaryBuilding request)
        {
            if (!Finite(request.PositionX, request.PositionY, request.PositionZ, request.YawDegrees) || string.IsNullOrWhiteSpace(request.ConfigId))
                return Failure(ErrorCode.ERR_OnlineWorldOperationInvalid, state);
            TempBuildingConfig config;
            try { config = ServerConfig.Instance.GetTemporaryBuilding(request.ConfigId); }
            catch { return Failure(ErrorCode.ERR_OnlineWorldOperationInvalid, state); }
            // 基地摆件仅属于离线存档，服务端在库存候选和世界分配前拒绝绕过客户端目录的请求。
            // 基地摆件仅属于离线存档，在库存候选和世界分配前拒绝绕过客户端目录的请求。
            if (config.BaseOnly) return Failure(ErrorCode.ERR_OnlineWorldOperationInvalid, state);
            if (!WithinBuildingOperationRange(actor, request.PositionX, request.PositionY, request.PositionZ, config.FootprintRadius))
                return Failure(ErrorCode.ERR_OnlineWorldOperationInvalid, state);
            foreach (OnlineWorldTemporaryBuilding other in state.TemporaryBuildings.Values)
            {
                TempBuildingConfig otherConfig = ServerConfig.Instance.GetTemporaryBuilding(other.ConfigId);
                float radius = config.FootprintRadius + otherConfig.FootprintRadius;
                if (DistanceSquared(other.PositionX, other.PositionZ, request.PositionX, request.PositionZ) < radius * radius)
                    return Failure(ErrorCode.ERR_OnlineWorldOperationInvalid, state);
            }
            if (!TryInventory(state, actor.PlayerId, request.ExpectedInventoryRevision, out OnlineInventoryRecord inventory))
                return Failure(ErrorCode.ERR_OnlineWorldStateConflict, state);
            OnlineInventoryRecord candidate = OnlineInventoryRecordHelper.Clone(inventory);
            foreach (TempBuildingMaterialConfig material in config.Materials)
                if (!TryRemove(candidate, material.ItemId, material.Amount)) return Failure(ErrorCode.ERR_OnlineWorldOperationInvalid, state);
            candidate.Revision++;
            int runtimeId = state.NextTemporaryBuildingId++;
            OnlineWorldTemporaryBuilding building = new()
            {
                RuntimeId = runtimeId, ConfigId = config.Id, WorldId = self.WorldId, MapId = state.MapId,
                PositionX = request.PositionX, PositionY = request.PositionY, PositionZ = request.PositionZ,
                YawDegrees = NormalizeYaw(request.YawDegrees), StateRevision = 1
            };
            state.PlayerInventories[actor.PlayerId] = candidate;
            state.TemporaryBuildings.Add(runtimeId, building);
            state.WorldRevision++;
            self.PushInventory(state, actor.PlayerId, candidate);
            self.BroadcastTemporaryBuilding(state, building, 0);
            return Success(state, building, 0, candidate);
        }

        private static (int, bool, long, WorldTemporaryBuildingInfo, int, OnlineInventoryInfo) DestroyOne(
            OnlineWorldComponent self, OnlineWorldMapState state, OnlineWorldUnit actor, P2W_OperateWorldTemporaryBuilding request)
        {
            if (!TryBuilding(state, actor, request, out OnlineWorldTemporaryBuilding building, out TempBuildingConfig config))
                return Failure(ErrorCode.ERR_OnlineWorldStateConflict, state);
            if (!WithinBuildingOperationRange(actor, building.PositionX, building.PositionY, building.PositionZ, config.FootprintRadius))
                return Failure(ErrorCode.ERR_OnlineWorldOperationInvalid, state);
            if (!TryInventory(state, actor.PlayerId, request.ExpectedInventoryRevision, out OnlineInventoryRecord inventory))
                return Failure(ErrorCode.ERR_OnlineWorldStateConflict, state);
            OnlineInventoryRecord candidate = OnlineInventoryRecordHelper.Clone(inventory);
            foreach (TempBuildingMaterialConfig material in config.Materials)
                if (!TryAdd(candidate, material.ItemId, material.Amount)) return Failure(ErrorCode.ERR_OnlineWorldInventoryCapacity, state);
            if (building.LoadedAmmo > 0 && ServerConfig.Instance.TryGetTemporaryBuildingAttack(building.ConfigId, out TempBuildingManualAttackConfig attack) &&
                !TryAdd(candidate, attack.AmmoItemId, building.LoadedAmmo)) return Failure(ErrorCode.ERR_OnlineWorldInventoryCapacity, state);
            candidate.Revision++;
            state.PlayerInventories[actor.PlayerId] = candidate;
            state.TemporaryBuildings.Remove(building.RuntimeId);
            state.WorldRevision++;
            self.PushInventory(state, actor.PlayerId, candidate);
            self.BroadcastTemporaryBuilding(state, null, building.RuntimeId);
            return Success(state, null, building.RuntimeId, candidate);
        }

        /// <summary>建造和拆除共用玩家距离；占地圆只补偿水平距离，高低差参与三维边缘距离判定。</summary>
        private static bool WithinBuildingOperationRange(OnlineWorldUnit actor, float x, float y, float z, float footprintRadius)
        {
            float range = ServerConfig.Instance.GetPlayer(actor.PlayerConfigId).BuildingOperateDistance;
            double dx = (double)actor.PositionX - x, dz = (double)actor.PositionZ - z, dy = (double)actor.PositionY - y;
            double edgeDistance = Math.Max(0d, Math.Sqrt(dx * dx + dz * dz) - footprintRadius);
            return range > 0f && edgeDistance * edgeDistance + dy * dy <= (double)range * range + 0.00001d;
        }

        private static (int, bool, long, WorldTemporaryBuildingInfo, int, OnlineInventoryInfo) ReloadOne(
            OnlineWorldComponent self, OnlineWorldMapState state, OnlineWorldUnit actor, P2W_OperateWorldTemporaryBuilding request)
        {
            if (!TryBuilding(state, actor, request, out OnlineWorldTemporaryBuilding building, out _) ||
                !ServerConfig.Instance.TryGetTemporaryBuildingAttack(building.ConfigId, out TempBuildingManualAttackConfig attack))
                return Failure(ErrorCode.ERR_OnlineWorldStateConflict, state);
            if (DistanceSquared(actor.PositionX, actor.PositionZ, building.PositionX, building.PositionZ) > attack.InteractDistance * attack.InteractDistance)
                return Failure(ErrorCode.ERR_OnlineWorldOperationInvalid, state);
            if (!TryInventory(state, actor.PlayerId, request.ExpectedInventoryRevision, out OnlineInventoryRecord inventory))
                return Failure(ErrorCode.ERR_OnlineWorldStateConflict, state);
            int requested = Math.Min(attack.MagazineCapacity - building.LoadedAmmo, Total(inventory, attack.AmmoItemId));
            if (requested <= 0) return Failure(ErrorCode.ERR_OnlineWorldOperationInvalid, state);
            OnlineInventoryRecord candidate = OnlineInventoryRecordHelper.Clone(inventory);
            if (!TryRemove(candidate, attack.AmmoItemId, requested)) return Failure(ErrorCode.ERR_OnlineWorldOperationInvalid, state);
            candidate.Revision++;
            building.LoadedAmmo += requested;
            building.StateRevision++;
            state.PlayerInventories[actor.PlayerId] = candidate;
            state.WorldRevision++;
            self.PushInventory(state, actor.PlayerId, candidate);
            self.BroadcastTemporaryBuilding(state, building, 0);
            return Success(state, building, 0, candidate);
        }

        private static (int, bool, long, WorldTemporaryBuildingInfo, int, OnlineInventoryInfo) FireOne(
            OnlineWorldComponent self, OnlineWorldMapState state, OnlineWorldUnit actor, P2W_OperateWorldTemporaryBuilding request)
        {
            if (!TryBuilding(state, actor, request, out OnlineWorldTemporaryBuilding building, out _) ||
                !ServerConfig.Instance.TryGetTemporaryBuildingAttack(building.ConfigId, out TempBuildingManualAttackConfig attack) ||
                !Finite(request.AimX, request.AimZ))
                return Failure(ErrorCode.ERR_OnlineWorldStateConflict, state);
            if (DistanceSquared(actor.PositionX, actor.PositionZ, building.PositionX, building.PositionZ) > attack.InteractDistance * attack.InteractDistance)
                return Failure(ErrorCode.ERR_OnlineWorldOperationInvalid, state);
            float length = (float)Math.Sqrt(request.AimX * request.AimX + request.AimZ * request.AimZ);
            long now = TimeInfo.Instance.ServerNow();
            if (length < 0.001f || building.LoadedAmmo < attack.AmmoAmount || now < building.NextShootAtServerTime)
                return Failure(ErrorCode.ERR_OnlineWorldOperationInvalid, state);
            float aimX = request.AimX / length, aimZ = request.AimZ / length;
            ProjectileConfig projectile = ServerConfig.Instance.GetProjectile(attack.ProjectileConfigId);
            OnlineWorldEnemy target = state.Enemies.Values
                .Where(x => !x.IsDead && InProjectilePath(building.PositionX, building.PositionZ, aimX, aimZ, x.PositionX, x.PositionZ, projectile.MaxTravelDistance))
                .OrderBy(x => ForwardDistance(building.PositionX, building.PositionZ, aimX, aimZ, x.PositionX, x.PositionZ)).ThenBy(x => x.UnitId).FirstOrDefault();
            building.LoadedAmmo -= attack.AmmoAmount;
            building.NextShootAtServerTime = now + (long)(attack.ShootCooldownSeconds * 1000f);
            building.YawDegrees = (float)(Math.Atan2(aimX, aimZ) * 180.0 / Math.PI);
            building.StateRevision++;
            if (target != null) self.DamageEnemy(state, target, attack.Damage, now);
            state.WorldRevision++;
            self.BroadcastTemporaryBuilding(state, building, 0);
            self.BroadcastCombat(state, 4, building.RuntimeId, 0, Array.Empty<OnlineWorldUnit>(), target == null ? Array.Empty<OnlineWorldEnemy>() : new[] { target });
            if (target == null)
            {
                self.CreateProjectileRecovery(state, projectile.RecoveryLootConfigId,
                    building.PositionX + aimX * projectile.MaxTravelDistance,
                    building.PositionY,
                    building.PositionZ + aimZ * projectile.MaxTravelDistance,
                    building.YawDegrees,
                    now);
            }
            if (target?.IsDead == true) self.RecomputeLeaseForInventory(state, "temporary-building-enemy-death");
            return Success(state, building, 0, state.PlayerInventories[actor.PlayerId]);
        }

        private static OnlineWorldMapState RequireActor(OnlineWorldComponent self, P2W_OperateWorldTemporaryBuilding request, out OnlineWorldUnit actor)
        {
            actor = null;
            if (request.WorldId != self.WorldId || !self.MapStates.TryGetValue(request.MapId ?? string.Empty, out OnlineWorldMapState state) ||
                !self.PlayerMapIds.TryGetValue(request.PlayerId, out string indexedMap) || !string.Equals(indexedMap, request.MapId, StringComparison.Ordinal) ||
                !state.PlayerUnits.TryGetValue(request.PlayerId, out long unitId) || unitId != request.UnitId || !state.Units.TryGetValue(unitId, out actor) ||
                actor.IsDead || actor.IsLeaving || actor.ConnectionState != OnlineWorldConnectionState.Online || actor.LifecycleRevision != request.LifecycleRevision)
                throw new RpcException(ErrorCode.ERR_OnlineWorldStateConflict, "world temporary building identity conflict");
            return state;
        }

        private static bool TryBuilding(OnlineWorldMapState state, OnlineWorldUnit actor, P2W_OperateWorldTemporaryBuilding request,
            out OnlineWorldTemporaryBuilding building, out TempBuildingConfig config)
        {
            config = null;
            if (!state.TemporaryBuildings.TryGetValue(request.RuntimeId, out building) || building.StateRevision != request.ExpectedBuildingRevision) return false;
            try { config = ServerConfig.Instance.GetTemporaryBuilding(building.ConfigId); return true; }
            catch { return false; }
        }

        private static bool TryInventory(OnlineWorldMapState state, long playerId, long expectedRevision, out OnlineInventoryRecord inventory)
        {
            return state.PlayerInventories.TryGetValue(playerId, out inventory) && inventory.Revision == expectedRevision;
        }

        private static int Total(OnlineInventoryRecord inventory, int itemId) => inventory.Slots.Where(x => x.ItemId == itemId).Sum(x => x.Amount);

        private static bool TryRemove(OnlineInventoryRecord inventory, int itemId, int amount)
        {
            if (amount <= 0 || Total(inventory, itemId) < amount) return false;
            foreach (OnlineItemSlotRecord slot in inventory.Slots.Where(x => x.ItemId == itemId).OrderBy(x => x.SlotIndex).ToList())
            {
                int removed = Math.Min(slot.Amount, amount); slot.Amount -= removed; amount -= removed;
                if (slot.Amount == 0) inventory.Slots.Remove(slot);
                if (amount == 0) return true;
            }
            return false;
        }

        private static bool TryAdd(OnlineInventoryRecord inventory, int itemId, int amount)
        {
            Item item = ServerConfig.Instance.GetItem(itemId);
            foreach (OnlineItemSlotRecord slot in inventory.Slots.Where(x => x.ItemId == itemId && x.Amount < item.MaxStackAmount).OrderBy(x => x.SlotIndex))
            {
                int accepted = Math.Min(item.MaxStackAmount - slot.Amount, amount); slot.Amount += accepted; amount -= accepted;
                if (amount == 0) return true;
            }
            for (int index = 0; index < inventory.Capacity && amount > 0; index++)
            {
                if (inventory.Slots.Any(x => x.SlotIndex == index)) continue;
                int accepted = Math.Min(item.MaxStackAmount, amount);
                inventory.Slots.Add(new OnlineItemSlotRecord { SlotIndex = index, ItemId = itemId, Amount = accepted });
                amount -= accepted;
            }
            return amount == 0;
        }

        private static void BroadcastTemporaryBuilding(this OnlineWorldComponent self, OnlineWorldMapState state, OnlineWorldTemporaryBuilding building, int removedRuntimeId)
        {
            foreach (OnlineWorldUnit target in state.Units.Values.Where(x => x.ConnectionState == OnlineWorldConnectionState.Online && x.PlayerActorId != default))
            {
                W2P_WorldTemporaryBuildingChanged message = W2P_WorldTemporaryBuildingChanged.Create();
                message.PlayerId = target.PlayerId; message.WorldId = self.WorldId; message.MapId = state.MapId; message.WorldRevision = state.WorldRevision; message.ServerTime = TimeInfo.Instance.ServerNow();
                message.HasBuilding = building != null; message.Building = Message(building); message.RemovedRuntimeId = removedRuntimeId;
                self.Root().GetComponent<MessageSender>().Send(target.PlayerActorId, message);
            }
        }

        private static (int, bool, long, WorldTemporaryBuildingInfo, int, OnlineInventoryInfo) Success(OnlineWorldMapState state, OnlineWorldTemporaryBuilding building, int removed, OnlineInventoryRecord inventory)
            => (ErrorCode.ERR_Success, true, state.WorldRevision, Message(building), removed, OnlineWorldInventorySystem.InventoryMessage(inventory));

        private static (int, bool, long, WorldTemporaryBuildingInfo, int, OnlineInventoryInfo) Failure(int error, OnlineWorldMapState state)
            => (error, false, state.WorldRevision, null, 0, null);

        private static bool Finite(params float[] values) { foreach (float value in values) if (float.IsNaN(value) || float.IsInfinity(value)) return false; return true; }
        private static float DistanceSquared(float ax, float az, float bx, float bz) { float x = bx - ax, z = bz - az; return x * x + z * z; }
        private static float ForwardDistance(float ox, float oz, float ax, float az, float tx, float tz) => ax * (tx - ox) + az * (tz - oz);
        private static bool InProjectilePath(float ox, float oz, float ax, float az, float tx, float tz, float range)
        { float dx = tx - ox, dz = tz - oz, forward = ax * dx + az * dz; if (forward < 0 || forward > range) return false; return dx * dx + dz * dz - forward * forward <= 0.75f * 0.75f; }
        private static float NormalizeYaw(float yaw) { yaw %= 360f; return yaw < 0f ? yaw + 360f : yaw; }
    }
}
