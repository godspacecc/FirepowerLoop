using System;
using System.Collections.Generic;
using System.Linq;
using Game;
namespace ET.Server
{
    public static partial class OnlineWorldComponentSystem
    {
        private static bool EnsureCollisionData(this OnlineWorldComponent self,OnlineWorldMapState state)
        {
            if(state.CollisionData!=null)return true;
            if(!self.CollisionCatalog.TryGet(state.MapId,out var data,out _))return false;
            state.CollisionData=data;state.CollisionQuery=new WorldProjectileCollisionQuery(data.Shapes);return true;
        }
        /// <summary>完成所有几何及资源验证后原子接受一支箭；失败不消费弹药、不推进序号。</summary>
        private static bool TryAcceptProjectile(this OnlineWorldComponent self,OnlineWorldMapState state,OnlineWorldUnit actor,P2W_WorldMeleeAttack request,WeaponConfig weapon,OnlineInventoryRecord inventory,long now)
        {
            if(!request.HasProjectileOrigin||!self.EnsureCollisionData(state)||request.CollisionContentHash!=state.CollisionData.ContentHash)return false;
            var origin=new WorldCollisionVector(request.OriginX,request.OriginY,request.OriginZ);var direction=new WorldCollisionVector(request.AimX,request.AimY,request.AimZ);
            var position=new WorldCollisionVector(actor.PositionX,actor.PositionY,actor.PositionZ);
            if(!origin.IsFinite||!direction.IsFinite||float.IsInfinity(direction.Length)||direction.Length<1e-6f||(origin-position).Length>state.CollisionData.MaxOriginDistance)return false;
            var body=state.CollisionData.Profiles.FirstOrDefault(p=>p.ConfigId==actor.PlayerConfigId)?.Shapes.FirstOrDefault();
            var shape=state.CollisionData.Profiles.FirstOrDefault(p=>p.ConfigId==weapon.ProjectileConfigId)?.Shapes.FirstOrDefault();
            if(body==null||shape==null||shape.Kind!=WorldCollisionShapeKind.Box)return false;
            if(state.Enemies.Values.Any(e=>!e.IsDead&&!state.CollisionData.Profiles.Any(p=>p.ConfigId==e.EnemyConfigId)))return false;
            // 从权威身体中心到枪口校验遮挡，相机越过墙不能把箭直接生在墙外。
            var point=new WorldProjectileShape{Kind=WorldCollisionShapeKind.Sphere,Radius=0};
            if(state.CollisionQuery.TrySweep(position+body.Center,origin,point,out _))return false;
            ProjectileConfig config;try{config=ServerConfig.Instance.GetProjectile(weapon.ProjectileConfigId);}catch{return false;}
            if(!Finite(config.MoveSpeed,config.MaxTravelDistance)||config.MoveSpeed<=0||config.MaxTravelDistance<=0||self.NextProjectileRuntimeId<=0||self.NextProjectileRuntimeId==long.MaxValue)return false;
            direction=direction.Normalized;
            var arrow=new OnlineWorldProjectileRuntime{RuntimeId=self.NextProjectileRuntimeId,OwnerUnitId=actor.UnitId,OwnerLifecycleRevision=actor.LifecycleRevision,AttackSequence=request.AttackSequence,WeaponItemId=request.WeaponItemId,Damage=weapon.AttackDamage,ProjectileConfigId=config.Id,RecoveryLootConfigId=config.RecoveryLootConfigId,Position=origin,Direction=direction,Shape=Orient(shape,direction),Speed=config.MoveSpeed,RemainingDistance=config.MaxTravelDistance,SpawnServerTime=now,SampleServerTime=now,StateRevision=1};
            state.Projectiles.Add(arrow.RuntimeId,arrow);self.NextProjectileRuntimeId++;
            OnlineInventoryRecordHelper.HeldStack(inventory).LoadedAmmo--;inventory.Revision++;actor.LastAcceptedAttackSequence=request.AttackSequence;actor.NextMeleeAttackAt=now+(long)(weapon.AttackCooldownSeconds*1000);actor.StateRevision++;
            if(request.AimX*request.AimX+request.AimZ*request.AimZ>1e-6f)actor.YawDegrees=(float)(Math.Atan2(request.AimX,request.AimZ)*180/Math.PI);
            self.AcceptedAttackCount++;state.WorldRevision++;self.PushInventory(state,actor.PlayerId,inventory);
            self.BroadcastCombat(state,3,actor.UnitId,request.WeaponItemId,new[]{actor},Array.Empty<OnlineWorldEnemy>());
            self.BroadcastProjectiles(state,new[]{arrow},Array.Empty<WorldProjectileTermination>(),now);return true;
        }
        /// <summary>按服务器实际时间对完整移动段扫掠；命中、射程耗尽、来源失效都只终止一次。</summary>
        public static void TickProjectiles(this OnlineWorldComponent self,OnlineWorldMapState state,long now)
        {
            if(state.Projectiles.Count==0)return;
            var changes=new List<OnlineWorldProjectileRuntime>();var ends=new List<WorldProjectileTermination>();
            foreach(var arrow in state.Projectiles.Values.OrderBy(p=>p.RuntimeId).ToArray())
            {
                if(!state.Units.TryGetValue(arrow.OwnerUnitId,out var owner)||owner.IsDead||owner.IsLeaving||owner.LifecycleRevision!=arrow.OwnerLifecycleRevision)
                {Finish(state,arrow,4,ends);continue;}
                if(now<=arrow.SampleServerTime)continue;
                float distance=Math.Min(arrow.RemainingDistance,arrow.Speed*((now-arrow.SampleServerTime)/1000f));var from=arrow.Position;var to=from+arrow.Direction*distance;
                float nearest=float.PositiveInfinity;OnlineWorldEnemy target=null;
                if(state.CollisionQuery.TrySweep(from,to,arrow.Shape,out var staticHit))nearest=staticHit.Distance;
                foreach(var enemy in state.Enemies.Values.Where(e=>!e.IsDead).OrderBy(e=>e.UnitId))
                {
                    var profile=state.CollisionData.Profiles.FirstOrDefault(p=>p.ConfigId==enemy.EnemyConfigId);
                    if(profile==null)continue;
                    var enemyPosition=new WorldCollisionVector(enemy.PositionX,enemy.PositionY,enemy.PositionZ);
                    var enemyShapes=profile.Shapes.Select(s=>RotateYaw(s,enemy.YawDegrees,enemyPosition)).ToArray();
                    var query=new WorldProjectileCollisionQuery(enemyShapes);
                    if(query.TrySweep(from,to,arrow.Shape,out var hit)&&hit.Distance<nearest-.00002f){nearest=hit.Distance;target=enemy;}
                }
                arrow.Position=float.IsPositiveInfinity(nearest)?to:from+arrow.Direction*nearest;arrow.RemainingDistance=Math.Max(0,arrow.RemainingDistance-distance);arrow.SampleServerTime=now;arrow.StateRevision++;
                if(target!=null)
                {
                    Finish(state,arrow,1,ends);self.DamageEnemy(state,target,arrow.Damage,now);state.WorldRevision++;
                    self.BroadcastCombat(state,0,arrow.OwnerUnitId,arrow.WeaponItemId,Array.Empty<OnlineWorldUnit>(),new[]{target});if(target.IsDead)self.RecomputeLease(state,"projectile-enemy-death");
                }
                else if(!float.IsPositiveInfinity(nearest)||arrow.RemainingDistance<=1e-6f)
                {
                    Finish(state,arrow,float.IsPositiveInfinity(nearest)?3:2,ends);self.AcceptedAttackWhiffCount++;
                    if(!string.IsNullOrWhiteSpace(arrow.RecoveryLootConfigId)&&state.CollisionQuery.TryGround(arrow.Position,out var ground))
                        self.CreateProjectileRecovery(state,arrow.RecoveryLootConfigId,ground.X,ground.Y,ground.Z,(float)(Math.Atan2(arrow.Direction.X,arrow.Direction.Z)*180/Math.PI),now);
                    else if(!string.IsNullOrWhiteSpace(arrow.RecoveryLootConfigId))Log.Warning($"[WorldProjectile] 无可用回收表面 map={state.MapId} runtimeId={arrow.RuntimeId}");
                }
                else changes.Add(arrow);
            }
            if(changes.Count==0&&ends.Count==0)return;state.WorldRevision++;self.BroadcastProjectiles(state,changes,ends,now);
        }
        private static void Finish(OnlineWorldMapState state,OnlineWorldProjectileRuntime arrow,int reason,List<WorldProjectileTermination> ends)
        {
            if(!state.Projectiles.Remove(arrow.RuntimeId))return;
            var t=WorldProjectileTermination.Create();t.RuntimeId=arrow.RuntimeId;t.StateRevision=arrow.StateRevision+1;t.PositionX=arrow.Position.X;t.PositionY=arrow.Position.Y;t.PositionZ=arrow.Position.Z;t.Reason=reason;ends.Add(t);
        }
        private static WorldProjectileShape Orient(WorldProjectileShape s,WorldCollisionVector forward)
        {
            var up=Math.Abs(forward.Y)>.999f?new WorldCollisionVector(0,0,1):new WorldCollisionVector(0,1,0);
            var right=WorldCollisionVector.Cross(up,forward).Normalized;up=WorldCollisionVector.Cross(forward,right).Normalized;
            WorldCollisionVector R(WorldCollisionVector p)=>right*p.X+up*p.Y+forward*p.Z;
            return new WorldProjectileShape{Id=s.Id,Kind=s.Kind,Center=R(s.Center),AxisX=R(s.AxisX),AxisY=R(s.AxisY),AxisZ=R(s.AxisZ),A=R(s.A),B=R(s.B),C=R(s.C),Radius=s.Radius};
        }
        private static WorldProjectileShape RotateYaw(WorldProjectileShape s,float yaw,WorldCollisionVector position)
        {
            float radians=yaw*(float)Math.PI/180;var forward=new WorldCollisionVector((float)Math.Sin(radians),0,(float)Math.Cos(radians));var r=Orient(s,forward);r.Id=s.Id;r.Center+=position;r.A+=position;r.B+=position;r.C+=position;return r;
        }
    }
}

namespace ET.Server
{
    /// <summary>仅复制及转发弹体权威结果，供世界推进与库存离图清理单向调用，不计算命中。</summary>
    [FriendOf(typeof(OnlineWorldComponent))]
    public static class OnlineWorldProjectileMessages
    {
        public static void ReleaseOwnerProjectiles(this OnlineWorldComponent self,OnlineWorldMapState state,long ownerId)
        {
            var ends=new List<WorldProjectileTermination>();foreach(var arrow in state.Projectiles.Values.Where(a=>a.OwnerUnitId==ownerId).ToArray()){
                if(!state.Projectiles.Remove(arrow.RuntimeId))continue;
                var t=WorldProjectileTermination.Create();t.RuntimeId=arrow.RuntimeId;t.StateRevision=arrow.StateRevision+1;t.PositionX=arrow.Position.X;t.PositionY=arrow.Position.Y;t.PositionZ=arrow.Position.Z;t.Reason=4;ends.Add(t);
            }
            if(ends.Count==0)return;state.WorldRevision++;self.BroadcastProjectiles(state,Array.Empty<OnlineWorldProjectileRuntime>(),ends,TimeInfo.Instance.ServerNow());
        }
        public static WorldProjectileInfo ProjectileMessage(OnlineWorldProjectileRuntime a)
        {
            var p=WorldProjectileInfo.Create();p.RuntimeId=a.RuntimeId;p.OwnerUnitId=a.OwnerUnitId;p.OwnerLifecycleRevision=a.OwnerLifecycleRevision;p.AttackSequence=a.AttackSequence;p.ProjectileConfigId=a.ProjectileConfigId;p.PositionX=a.Position.X;p.PositionY=a.Position.Y;p.PositionZ=a.Position.Z;p.DirectionX=a.Direction.X;p.DirectionY=a.Direction.Y;p.DirectionZ=a.Direction.Z;p.Speed=a.Speed;p.RemainingDistance=a.RemainingDistance;p.SpawnServerTime=a.SpawnServerTime;p.SampleServerTime=a.SampleServerTime;p.StateRevision=a.StateRevision;return p;
        }
        public static void BroadcastProjectiles(this OnlineWorldComponent self,OnlineWorldMapState state,IEnumerable<OnlineWorldProjectileRuntime> arrows,IReadOnlyList<WorldProjectileTermination> ends,long now)
        {
            var list=arrows.ToArray();foreach(var recipient in state.Units.Values.Where(x=>x.ConnectionState==OnlineWorldConnectionState.Online&&x.PlayerActorId!=default))
            {
                var m=W2P_WorldProjectilesChanged.Create();m.PlayerId=recipient.PlayerId;m.WorldId=self.WorldId;m.MapId=state.MapId;m.WorldRevision=state.WorldRevision;m.ServerTime=now;
                foreach(var a in list)m.Projectiles.Add(ProjectileMessage(a));foreach(var e in ends)m.Terminations.Add(OnlineWorldSnapshotHelper.Clone(e));self.Root().GetComponent<MessageSender>().Send(recipient.PlayerActorId,m);
            }
        }
    }
}
