using System.Collections.Generic;

namespace ET.Server
{
    public enum OnlineWorldConnectionState { Online = 1, DisconnectedGrace = 2 }

    [EnableClass]
    public sealed class OnlineWorldUnit
    {
        public long PlayerId, UnitId, WorldId, StateRevision, RespawnAtServerTime, LastAcceptedAttackSequence, LifecycleRevision, NextMeleeAttackAt;
        /// <summary>提交库存后同步的外观快照，初见与重连均随玩家消息发送。</summary>
        public int TorsoItemId, LegsItemId, HeadItemId, HandsItemId, FeetItemId, BackItemId, ArmorItemId;
        public string DisplayName, MapId, PlayerConfigId;
        public float PositionX, PositionY, PositionZ, YawDegrees;
        public int CurrentHP, MaxHP;
        public bool IsDead, NavigationEligible = true;
        public bool IsLeaving;
        public OnlineWorldConnectionState ConnectionState;
        public ActorId PlayerActorId;
    }

    [EnableClass]
    public sealed class OnlineWorldEnemy
    {
        public long UnitId, WorldId, TargetUnitId, StateRevision, LifecycleRevision, NextAttackAt, LastNavigationAt;
        public string MapId, EnemyConfigId;
        public float SpawnPositionX, SpawnPositionY, SpawnPositionZ, SpawnYawDegrees, PositionX, PositionY, PositionZ, YawDegrees;
        public int CurrentHP, MaxHP;
        public bool IsDead;
        public EnemyConfig Config;
    }

    [EnableClass]
    public sealed class OnlineWorldTemporaryBuilding
    {
        public int RuntimeId;
        public string ConfigId, MapId;
        public long WorldId, NextShootAtServerTime, StateRevision;
        public float PositionX, PositionY, PositionZ, YawDegrees;
        public int LoadedAmmo;
    }

    [EnableClass]
    public sealed class OnlineWorldMapState
    {
        public string MapId;
        public PlayerConfig PlayerConfig;
        public MapSpawnConfig PlayerSpawn;
        public long NextSpawnSequence, WorldRevision, CombatSequence;
        public int NextTemporaryBuildingId = 1;
        public long NavigationExecutorUnitId, NavigationLeaseRevision, LastNavigationHeartbeatAt, LastAcceptedNavigationReportSequence;
        public readonly Dictionary<long, OnlineWorldUnit> Units = new();
        public readonly Dictionary<long, long> PlayerUnits = new();
        public readonly Dictionary<long, OnlineWorldEnemy> Enemies = new();
        /// <summary>当前地图中由服务器推进的玩家弓箭，运行身份由 World 单调分配。</summary>
        public readonly Dictionary<long, OnlineWorldProjectileRuntime> Projectiles = new();
        public Game.WorldProjectileCollisionData CollisionData;
        public Game.WorldProjectileCollisionQuery CollisionQuery;
        public readonly Dictionary<int, OnlineWorldTemporaryBuilding> TemporaryBuildings = new();
        public readonly Dictionary<long, OnlineInventoryRecord> PlayerInventories = new();
        public readonly Dictionary<long, OnlineWorldCorpse> Corpses = new();
        public readonly Dictionary<long, OnlineWorldPendingRespawn> PendingRespawns = new();
        public readonly Dictionary<string, OnlineWorldLeaveRecord> LeaveRecords = new(System.StringComparer.Ordinal);
        public readonly Dictionary<long, Dictionary<string, OnlineWorldInventoryOperationRecord>> InventoryOperations = new();
    }

    [ComponentOf(typeof(Scene))]
    public class OnlineWorldComponent: Entity, IAwake, IDestroy
    {
        public const long MainWorldId = 1;
        public const string MainMapId = "map.another-day.survival-sample";
        public const string MainPlayerConfigId = "player.graybox.default";
        public long WorldId = MainWorldId, NextUnitId = 1;
        public long NextProjectileRuntimeId = 1;
        public readonly OnlineWorldCollisionCatalog CollisionCatalog = new();
        public int FailedLeaveCount, TickGeneration;
        public int TickMilliseconds, PlayerRespawnMilliseconds, NavigationHeartbeatMilliseconds, NavigationLeaseTimeoutMilliseconds;
        public int CorpseLifetimeMilliseconds, DropLeaveCommitResponseCount;
        public long LeaseTransferCount, AcceptedAttackCount, AcceptedAttackWhiffCount, RejectedAttackCount, RejectedAttackIdentityCount, RejectedAttackLifecycleCount, RejectedAttackSequenceCount, RejectedAttackCooldownCount, RejectedAttackInputCount, PlayerDeathCount, PlayerRespawnCount, EnemyDeathCount;
        public long RejectedOldLeaseCount, RejectedNavigationSequenceCount, RejectedNavigationFloatCount, RejectedNavigationRangeCount;
        public long LastInvalidInputLogAt;
        public int InvalidInputSinceLastLog;
        public readonly Dictionary<string, OnlineWorldMapState> MapStates = new(System.StringComparer.Ordinal);
        public readonly Dictionary<long, string> PlayerMapIds = new();
    }

    public static class OnlineWorldSnapshotHelper
    {
        public static WorldProjectileInfo Clone(WorldProjectileInfo s)
        {if(s==null)return null;var t=WorldProjectileInfo.Create();t.RuntimeId=s.RuntimeId;t.OwnerUnitId=s.OwnerUnitId;t.OwnerLifecycleRevision=s.OwnerLifecycleRevision;t.AttackSequence=s.AttackSequence;t.ProjectileConfigId=s.ProjectileConfigId;t.PositionX=s.PositionX;t.PositionY=s.PositionY;t.PositionZ=s.PositionZ;t.DirectionX=s.DirectionX;t.DirectionY=s.DirectionY;t.DirectionZ=s.DirectionZ;t.Speed=s.Speed;t.RemainingDistance=s.RemainingDistance;t.SpawnServerTime=s.SpawnServerTime;t.StateRevision=s.StateRevision;t.SampleServerTime=s.SampleServerTime;return t;}
        public static WorldProjectileTermination Clone(WorldProjectileTermination s)
        {if(s==null)return null;var t=WorldProjectileTermination.Create();t.RuntimeId=s.RuntimeId;t.StateRevision=s.StateRevision;t.PositionX=s.PositionX;t.PositionY=s.PositionY;t.PositionZ=s.PositionZ;t.Reason=s.Reason;return t;}
        public static WorldPlayerInfo Clone(WorldPlayerInfo s)
        {
            if (s == null) return null; WorldPlayerInfo t = WorldPlayerInfo.Create();
            t.TorsoItemId=s.TorsoItemId;t.LegsItemId=s.LegsItemId;t.HeadItemId=s.HeadItemId;t.HandsItemId=s.HandsItemId;t.FeetItemId=s.FeetItemId;t.BackItemId=s.BackItemId;t.ArmorItemId=s.ArmorItemId;t.PlayerId=s.PlayerId;t.UnitId=s.UnitId;t.DisplayName=s.DisplayName;t.WorldId=s.WorldId;t.MapId=s.MapId;t.PlayerConfigId=s.PlayerConfigId;t.PositionX=s.PositionX;t.PositionY=s.PositionY;t.PositionZ=s.PositionZ;t.YawDegrees=s.YawDegrees;t.ConnectionState=s.ConnectionState;t.StateRevision=s.StateRevision;t.CurrentHP=s.CurrentHP;t.MaxHP=s.MaxHP;t.IsDead=s.IsDead;t.RespawnAtServerTime=s.RespawnAtServerTime;t.LastAcceptedAttackSequence=s.LastAcceptedAttackSequence;t.LifecycleRevision=s.LifecycleRevision;return t;
        }
        public static WorldEnemyInfo Clone(WorldEnemyInfo s)
        {
            if (s == null) return null; WorldEnemyInfo t=WorldEnemyInfo.Create();t.UnitId=s.UnitId;t.WorldId=s.WorldId;t.MapId=s.MapId;t.EnemyConfigId=s.EnemyConfigId;t.SpawnPositionX=s.SpawnPositionX;t.SpawnPositionY=s.SpawnPositionY;t.SpawnPositionZ=s.SpawnPositionZ;t.SpawnYawDegrees=s.SpawnYawDegrees;t.PositionX=s.PositionX;t.PositionY=s.PositionY;t.PositionZ=s.PositionZ;t.YawDegrees=s.YawDegrees;t.TargetUnitId=s.TargetUnitId;t.CurrentHP=s.CurrentHP;t.MaxHP=s.MaxHP;t.IsDead=s.IsDead;t.RespawnAtServerTime=s.RespawnAtServerTime;t.StateRevision=s.StateRevision;t.LifecycleRevision=s.LifecycleRevision;return t;
        }
        public static WorldEnemyTransformInfo Clone(WorldEnemyTransformInfo s)
        { if(s==null)return null;WorldEnemyTransformInfo t=WorldEnemyTransformInfo.Create();t.UnitId=s.UnitId;t.PositionX=s.PositionX;t.PositionY=s.PositionY;t.PositionZ=s.PositionZ;t.YawDegrees=s.YawDegrees;t.StateRevision=s.StateRevision;t.LifecycleRevision=s.LifecycleRevision;return t; }
        public static OnlineWorldSnapshot Clone(OnlineWorldSnapshot s)
        {
            if(s==null)return null;OnlineWorldSnapshot t=OnlineWorldSnapshot.Create();t.WorldId=s.WorldId;t.MapId=s.MapId;t.WorldRevision=s.WorldRevision;t.SelfUnitId=s.SelfUnitId;t.NavigationExecutorUnitId=s.NavigationExecutorUnitId;t.NavigationLeaseRevision=s.NavigationLeaseRevision;t.ServerTime=s.ServerTime;t.SelfInventory=Clone(s.SelfInventory);t.CollisionContentHash=s.CollisionContentHash;foreach(var x in s.Projectiles)t.Projectiles.Add(Clone(x));foreach(WorldPlayerInfo p in s.Players)t.Players.Add(Clone(p));foreach(WorldEnemyInfo e in s.Enemies)t.Enemies.Add(Clone(e));foreach(WorldCorpseInfo c in s.Corpses)t.Corpses.Add(Clone(c));foreach(WorldTemporaryBuildingInfo b in s.TemporaryBuildings)t.TemporaryBuildings.Add(Clone(b));return t;
        }
        public static OnlineItemSlotInfo Clone(OnlineItemSlotInfo s){if(s==null)return null;OnlineItemSlotInfo t=OnlineItemSlotInfo.Create();t.SlotIndex=s.SlotIndex;t.ItemId=s.ItemId;t.Amount=s.Amount;t.LoadedAmmo=s.LoadedAmmo;return t;}
        public static OnlineEquipmentInfo Clone(OnlineEquipmentInfo s){if(s==null)return null;OnlineEquipmentInfo t=OnlineEquipmentInfo.Create();t.MeleeWeaponItemId=s.MeleeWeaponItemId;t.RangedWeaponItemId=s.RangedWeaponItemId;t.ActiveSlot=s.ActiveSlot;t.RangedLoadedAmmo=s.RangedLoadedAmmo;t.SchemaVersion=s.SchemaVersion;t.SourceSlotIndex=s.SourceSlotIndex;t.ItemId=s.ItemId;return t;}
        public static OnlineInventoryInfo Clone(OnlineInventoryInfo s){if(s==null)return null;OnlineInventoryInfo t=OnlineInventoryInfo.Create();t.TorsoItemId=s.TorsoItemId;t.LegsItemId=s.LegsItemId;t.HeadItemId=s.HeadItemId;t.HandsItemId=s.HandsItemId;t.FeetItemId=s.FeetItemId;t.BackItemId=s.BackItemId;t.ArmorItemId=s.ArmorItemId;t.Capacity=s.Capacity;t.MigrationExtraCapacity=s.MigrationExtraCapacity;t.Revision=s.Revision;t.Equipment=Clone(s.Equipment);foreach(OnlineItemSlotInfo x in s.Slots)t.Slots.Add(Clone(x));return t;}
        public static WorldCorpseInfo Clone(WorldCorpseInfo s){if(s==null)return null;WorldCorpseInfo t=WorldCorpseInfo.Create();t.CorpseId=s.CorpseId;t.SourceKind=s.SourceKind;t.SourceUnitId=s.SourceUnitId;t.SourcePlayerId=s.SourcePlayerId;t.SourceConfigId=s.SourceConfigId;t.WorldId=s.WorldId;t.MapId=s.MapId;t.PositionX=s.PositionX;t.PositionY=s.PositionY;t.PositionZ=s.PositionZ;t.YawDegrees=s.YawDegrees;t.ExpiresAtServerTime=s.ExpiresAtServerTime;t.HasContents=s.HasContents;t.StateRevision=s.StateRevision;return t;}
        public static WorldCorpseContentsInfo Clone(WorldCorpseContentsInfo s){if(s==null)return null;WorldCorpseContentsInfo t=WorldCorpseContentsInfo.Create();t.Corpse=Clone(s.Corpse);t.Capacity=s.Capacity;foreach(OnlineItemSlotInfo x in s.Slots)t.Slots.Add(Clone(x));return t;}
        public static WorldTemporaryBuildingInfo Clone(WorldTemporaryBuildingInfo s){if(s==null)return null;WorldTemporaryBuildingInfo t=WorldTemporaryBuildingInfo.Create();t.RuntimeId=s.RuntimeId;t.ConfigId=s.ConfigId;t.WorldId=s.WorldId;t.MapId=s.MapId;t.PositionX=s.PositionX;t.PositionY=s.PositionY;t.PositionZ=s.PositionZ;t.YawDegrees=s.YawDegrees;t.LoadedAmmo=s.LoadedAmmo;t.NextShootAtServerTime=s.NextShootAtServerTime;t.StateRevision=s.StateRevision;return t;}
    }
}
