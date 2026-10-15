using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    public enum OnlineWorldOperationState{Idle,Entering,Leaving,Restoring}
    public enum OnlineWorldResultCode{None,Success,NotLoggedIn,NotInWorld,OperationInvalid,StateConflict,RoomLeaveFailed,ServiceUnavailable,Disconnected,Timeout,MapLoadFailed,BaseSaveFailed,OperationInProgress,UnknownFailure}
    public enum OnlineWorldConnectionState{Online=1,DisconnectedGrace=2}
    public enum OnlineWorldCombatActionKind{None=0,PlayerMelee=1,EnemyMelee=2,PlayerProjectile=3,TemporaryBuildingProjectile=4}
    public enum OnlineTemporaryBuildingActionKind{Build=1,Destroy=2,Reload=3,Fire=4}

    public readonly struct OnlineWorldPlayerSnapshot
    {
        public OnlineWorldPlayerSnapshot(long playerId,long unitId,string displayName,long worldId,string mapId,string playerConfigId,Vector3 localPosition,float yawDegrees,OnlineWorldConnectionState connectionState,long stateRevision):this(playerId,unitId,displayName,worldId,mapId,playerConfigId,localPosition,yawDegrees,connectionState,stateRevision,0,0,false,0,0,1){}
        public OnlineWorldPlayerSnapshot(long playerId,long unitId,string displayName,long worldId,string mapId,string playerConfigId,Vector3 localPosition,float yawDegrees,OnlineWorldConnectionState connectionState,long stateRevision,int currentHP,int maxHP,bool isDead,long respawnAtServerTime,long lastAcceptedAttackSequence,long lifecycleRevision,int torsoItemId=0,int legsItemId=0,int headItemId=0,int handsItemId=0,int feetItemId=0,int backItemId=0,int armorItemId=0)
        {TorsoItemId=torsoItemId;LegsItemId=legsItemId;HeadItemId=headItemId;HandsItemId=handsItemId;FeetItemId=feetItemId;BackItemId=backItemId;ArmorItemId=armorItemId;PlayerId=playerId;UnitId=unitId;DisplayName=displayName??string.Empty;WorldId=worldId;MapId=mapId??string.Empty;PlayerConfigId=playerConfigId??string.Empty;LocalPosition=localPosition;YawDegrees=yawDegrees;ConnectionState=connectionState;StateRevision=stateRevision;CurrentHP=currentHP;MaxHP=maxHP;IsDead=isDead;RespawnAtServerTime=respawnAtServerTime;LastAcceptedAttackSequence=lastAcceptedAttackSequence;LifecycleRevision=lifecycleRevision;}
        public int TorsoItemId{get;}public int LegsItemId{get;} public int HeadItemId{get;} public int HandsItemId{get;} public int FeetItemId{get;} public int BackItemId{get;} public int ArmorItemId{get;}
        /// <summary>固定枚举顺序的独立副本，供外观与装备页消费。</summary>
        public int[] ClothingItems()=>new[]{TorsoItemId,LegsItemId,HeadItemId,HandsItemId,FeetItemId,BackItemId,ArmorItemId};
        public long PlayerId{get;}public long UnitId{get;}public string DisplayName{get;}public long WorldId{get;}public string MapId{get;}public string PlayerConfigId{get;}public Vector3 LocalPosition{get;}public float YawDegrees{get;}public OnlineWorldConnectionState ConnectionState{get;}public long StateRevision{get;}public int CurrentHP{get;}public int MaxHP{get;}public bool IsDead{get;}public long RespawnAtServerTime{get;}public long LastAcceptedAttackSequence{get;}public long LifecycleRevision{get;}
    }

    public readonly struct OnlineWorldEnemySnapshot
    {
        public OnlineWorldEnemySnapshot(long unitId,long worldId,string mapId,string configId,Vector3 spawn,float spawnYaw,Vector3 position,float yaw,long target,int hp,int maxHp,bool dead,long respawn,long state,long lifecycle){UnitId=unitId;WorldId=worldId;MapId=mapId??string.Empty;EnemyConfigId=configId??string.Empty;SpawnLocalPosition=spawn;SpawnYawDegrees=spawnYaw;LocalPosition=position;YawDegrees=yaw;TargetUnitId=target;CurrentHP=hp;MaxHP=maxHp;IsDead=dead;RespawnAtServerTime=respawn;StateRevision=state;LifecycleRevision=lifecycle;}
        public long UnitId{get;}public long WorldId{get;}public string MapId{get;}public string EnemyConfigId{get;}public Vector3 SpawnLocalPosition{get;}public float SpawnYawDegrees{get;}public Vector3 LocalPosition{get;}public float YawDegrees{get;}public long TargetUnitId{get;}public int CurrentHP{get;}public int MaxHP{get;}public bool IsDead{get;}public long RespawnAtServerTime{get;}public long StateRevision{get;}public long LifecycleRevision{get;}
    }
    public readonly struct OnlineWorldEnemyTransformSnapshot{public OnlineWorldEnemyTransformSnapshot(long id,Vector3 position,float yaw,long state,long lifecycle){UnitId=id;LocalPosition=position;YawDegrees=yaw;StateRevision=state;LifecycleRevision=lifecycle;}public long UnitId{get;}public Vector3 LocalPosition{get;}public float YawDegrees{get;}public long StateRevision{get;}public long LifecycleRevision{get;}}
    public readonly struct OnlineWorldTemporaryBuildingSnapshot
    {
        public OnlineWorldTemporaryBuildingSnapshot(int runtimeId,string configId,long worldId,string mapId,Vector3 position,float yawDegrees,int loadedAmmo,long nextShootAtServerTime,long stateRevision){RuntimeId=runtimeId;ConfigId=configId??string.Empty;WorldId=worldId;MapId=mapId??string.Empty;Position=position;YawDegrees=yawDegrees;LoadedAmmo=loadedAmmo;NextShootAtServerTime=nextShootAtServerTime;StateRevision=stateRevision;}
        public int RuntimeId{get;}public string ConfigId{get;}public long WorldId{get;}public string MapId{get;}public Vector3 Position{get;}public float YawDegrees{get;}public int LoadedAmmo{get;}public long NextShootAtServerTime{get;}public long StateRevision{get;}
    }
    public readonly struct OnlineWorldEnemyNavigationSample{public OnlineWorldEnemyNavigationSample(long id,long lifecycle,Vector3 position,float yaw){UnitId=id;LifecycleRevision=lifecycle;LocalPosition=position;YawDegrees=yaw;}public long UnitId{get;}public long LifecycleRevision{get;}public Vector3 LocalPosition{get;}public float YawDegrees{get;}}
    /// <summary>已经应用的权威战斗表现通知；保留本次目标值快照，死亡目标移出活动列表后仍可反馈命中。</summary>
    public readonly struct OnlineWorldCombatPresentationEvent
    {
        public OnlineWorldCombatPresentationEvent(long sequence, OnlineWorldCombatActionKind kind, long actor,
            int weapon, IReadOnlyList<long> targets, IReadOnlyList<OnlineWorldEnemySnapshot> targetEnemies = null,
            IReadOnlyList<OnlineWorldPlayerSnapshot> targetPlayers = null)
        {
            CombatSequence = sequence; ActionKind = kind; ActorUnitId = actor; WeaponItemId = weapon;
            TargetUnitIds = targets == null ? Array.Empty<long>() : new List<long>(targets).AsReadOnly();
            TargetEnemies = targetEnemies == null ? Array.Empty<OnlineWorldEnemySnapshot>() : new List<OnlineWorldEnemySnapshot>(targetEnemies).AsReadOnly();
            TargetPlayers = targetPlayers == null ? Array.Empty<OnlineWorldPlayerSnapshot>() : new List<OnlineWorldPlayerSnapshot>(targetPlayers).AsReadOnly();
        }
        public long CombatSequence { get; }
        public OnlineWorldCombatActionKind ActionKind { get; }
        public long ActorUnitId { get; }
        public int WeaponItemId { get; }
        public IReadOnlyList<long> TargetUnitIds { get; }
        public IReadOnlyList<OnlineWorldEnemySnapshot> TargetEnemies { get; }
        public IReadOnlyList<OnlineWorldPlayerSnapshot> TargetPlayers { get; }
    }

    public sealed class OnlineWorldSnapshot
    {
        public OnlineWorldSnapshot(bool hasWorld,long worldId,string mapId,long selfUnitId,long worldRevision,IReadOnlyList<OnlineWorldPlayerSnapshot> players,bool connectionLost,OnlineWorldOperationState operationState,OnlineWorldResultCode lastResultCode):this(hasWorld,worldId,mapId,selfUnitId,worldRevision,players,Array.Empty<OnlineWorldEnemySnapshot>(),0,0,0,connectionLost,operationState,lastResultCode){}
        public OnlineWorldSnapshot(bool hasWorld,long worldId,string mapId,long selfUnitId,long worldRevision,IReadOnlyList<OnlineWorldPlayerSnapshot> players,IReadOnlyList<OnlineWorldEnemySnapshot> enemies,long executor,long lease,long serverTime,bool connectionLost,OnlineWorldOperationState operationState,OnlineWorldResultCode lastResultCode,OnlineInventorySnapshot selfInventory=default,IReadOnlyList<OnlineWorldCorpseSnapshot> corpses=null,IReadOnlyList<OnlineWorldTemporaryBuildingSnapshot> temporaryBuildings=null,IReadOnlyList<OnlineWorldProjectileSnapshot> projectiles=null,string collisionContentHash=null,bool isProjectileFullSnapshot=false){HasWorld=hasWorld;WorldId=worldId;MapId=mapId??string.Empty;SelfUnitId=selfUnitId;WorldRevision=worldRevision;Players=Copy(players);Enemies=Copy(enemies);NavigationExecutorUnitId=executor;NavigationLeaseRevision=lease;ServerTime=serverTime;ConnectionLost=connectionLost;OperationState=operationState;LastResultCode=lastResultCode;SelfInventory=selfInventory;Corpses=Copy(corpses);TemporaryBuildings=Copy(temporaryBuildings);Projectiles=Copy(projectiles);CollisionContentHash=collisionContentHash??string.Empty;IsProjectileFullSnapshot=isProjectileFullSnapshot;}
        public bool HasWorld{get;}public long WorldId{get;}public string MapId{get;}public long SelfUnitId{get;}public long WorldRevision{get;}public IReadOnlyList<OnlineWorldPlayerSnapshot> Players{get;}public IReadOnlyList<OnlineWorldEnemySnapshot> Enemies{get;}public long NavigationExecutorUnitId{get;}public long NavigationLeaseRevision{get;}public long ServerTime{get;}public bool ConnectionLost{get;}public OnlineWorldOperationState OperationState{get;}public OnlineWorldResultCode LastResultCode{get;}public OnlineInventorySnapshot SelfInventory{get;}public IReadOnlyList<OnlineWorldCorpseSnapshot> Corpses{get;}public IReadOnlyList<OnlineWorldTemporaryBuildingSnapshot> TemporaryBuildings{get;}
        public IReadOnlyList<OnlineWorldProjectileSnapshot> Projectiles{get;}public string CollisionContentHash{get;}public bool IsProjectileFullSnapshot{get;}
        public OnlineWorldSnapshot WithProjectiles(IReadOnlyList<OnlineWorldProjectileSnapshot> values,long revision,long serverTime,string hash)=>new(HasWorld,WorldId,MapId,SelfUnitId,revision,Players,Enemies,NavigationExecutorUnitId,NavigationLeaseRevision,serverTime,ConnectionLost,OperationState,LastResultCode,SelfInventory,Corpses,TemporaryBuildings,values,hash);
        public static OnlineWorldSnapshot Empty(OnlineWorldResultCode code=OnlineWorldResultCode.None)=>new(false,0,string.Empty,0,0,Array.Empty<OnlineWorldPlayerSnapshot>(),Array.Empty<OnlineWorldEnemySnapshot>(),0,0,0,false,OnlineWorldOperationState.Idle,code);
        public OnlineWorldSnapshot WithOperation(OnlineWorldOperationState state,OnlineWorldResultCode code)=>new(HasWorld,WorldId,MapId,SelfUnitId,WorldRevision,Players,Enemies,NavigationExecutorUnitId,NavigationLeaseRevision,ServerTime,ConnectionLost,state,code,SelfInventory,Corpses,TemporaryBuildings,Projectiles,CollisionContentHash);
        public OnlineWorldSnapshot WithConnectionLost(bool lost)=>new(HasWorld,WorldId,MapId,SelfUnitId,WorldRevision,Players,Enemies,NavigationExecutorUnitId,NavigationLeaseRevision,ServerTime,lost,OperationState,LastResultCode,SelfInventory,Corpses,TemporaryBuildings,Projectiles,CollisionContentHash);
        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T>s){if(s==null||s.Count==0)return Array.Empty<T>();T[]a=new T[s.Count];for(int i=0;i<a.Length;i++)a[i]=s[i];return Array.AsReadOnly(a);}
    }
    public readonly struct OnlineWorldOperationResult{public OnlineWorldOperationResult(OnlineWorldResultCode code,OnlineWorldSnapshot snapshot){Code=code;Snapshot=snapshot;}public OnlineWorldResultCode Code{get;}public OnlineWorldSnapshot Snapshot{get;}public bool Success=>Code==OnlineWorldResultCode.Success;}
    public readonly struct OnlineWorldChangedEvent{public OnlineWorldChangedEvent(OnlineWorldSnapshot snapshot){Snapshot=snapshot;}public OnlineWorldSnapshot Snapshot{get;}}
    public sealed class OnlineWorldModel:AbstractModel{public BindableProperty<OnlineWorldSnapshot> Snapshot{get;}=new(OnlineWorldSnapshot.Empty());public BindableProperty<OnlineWorldCorpseContentsSnapshot?> OpenCorpse{get;}=new(null);protected override void OnInit(){}public void Publish(OnlineWorldSnapshot snapshot){Snapshot.Value=snapshot;if(snapshot==null||!snapshot.HasWorld)OpenCorpse.Value=null;this.SendEvent(new OnlineWorldChangedEvent(snapshot));}public void PublishOpenCorpse(OnlineWorldCorpseContentsSnapshot? corpse){OpenCorpse.Value=corpse;this.SendEvent(new OnlineWorldInventoryChangedEvent(Snapshot.Value.SelfInventory.Revision,corpse?.Corpse.CorpseId??0,corpse?.Corpse.StateRevision??0));}}
}
