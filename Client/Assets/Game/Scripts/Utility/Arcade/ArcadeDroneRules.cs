using System;
using System.Collections.Generic;
#if DOTNET
using DroneConfig = ET.ArcadeDroneConfig;
#else
using DroneConfig = global::ArcadeDroneConfig;
#endif

namespace FirepowerLoop.Arcade
{
    /// <summary>临时无人机阶段；爆炸只在进入爆炸阶段时结算一次。</summary>
    public enum ArcadeDronePhase { Following=0, Ramming=1, Exploding=2, Expired=3 }

    /// <summary>从Config读取的无人机调参，双端不自行复制一组默认值。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeDroneTuning
    {
        public readonly float DurationSeconds,Range,OffsetX,OffsetZ,FireIntervalSeconds,ProjectileSpeed,ProjectileLifetimeSeconds;
        public readonly float RamWindowSeconds,RamSpeed,ExplosionRadius,ExplosionVisualSeconds;
        public readonly int Damage,ExplosionDamage;
        public ArcadeDroneTuning(float duration,float range,float offsetX,float offsetZ,int damage,float interval,
            float projectileSpeed,float projectileLifetime,float ramWindow,float ramSpeed,int explosionDamage,float explosionRadius,float explosionVisual)
        {DurationSeconds=duration;Range=range;OffsetX=offsetX;OffsetZ=offsetZ;Damage=damage;FireIntervalSeconds=interval;
            ProjectileSpeed=projectileSpeed;ProjectileLifetimeSeconds=projectileLifetime;RamWindowSeconds=ramWindow;RamSpeed=ramSpeed;
            ExplosionDamage=explosionDamage;ExplosionRadius=explosionRadius;ExplosionVisualSeconds=explosionVisual;}
        /// <summary>从双端同源生成表构造调参。</summary>
        public static ArcadeDroneTuning FromConfig(DroneConfig r)=>r==null?null:new ArcadeDroneTuning(
            r.DurationSeconds,r.Range,r.OffsetX,r.OffsetZ,r.Damage,r.FireIntervalSeconds,r.ProjectileSpeed,
            r.ProjectileLifetimeSeconds,r.RamWindowSeconds,r.RamSpeed,r.ExplosionDamage,r.ExplosionRadius,r.ExplosionVisualSeconds);
    }
    /// <summary>本场瞬时状态，由单人Model或权威服务端持有，不写入安全节点。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeDroneState
    {
        public ArcadeDronePhase Phase;
        public float RemainingSeconds,FireCooldownSeconds,VisualRemainingSeconds,X,Z;
        public long TargetId;
    }
    /// <summary>当前可攻击目标的只读位置；调用方只传入仍存活的敌人。</summary>
    public readonly struct ArcadeDroneTarget
    {
        public readonly long Id;public readonly float X,Z;
        public ArcadeDroneTarget(long id,float x,float z){Id=id;X=x;Z=z;}
    }
    /// <summary>本次规则步唯一产生的射击／爆炸事实，伤害由原战斗入口落实。</summary>
    public readonly struct ArcadeDroneStep
    {
        public readonly bool Shot,Exploded;
        public readonly long TargetId;
        public readonly int ShotDamage,ExplosionDamage;
        public readonly float DirectionX,DirectionZ;
        public ArcadeDroneStep(bool shot,bool exploded,long target,int shotDamage,int explosionDamage,float dx,float dz)
        {Shot=shot;Exploded=exploded;TargetId=target;ShotDamage=shotDamage;ExplosionDamage=explosionDamage;DirectionX=dx;DirectionZ=dz;}
    }
    /// <summary>双端共享无人机时效、选敌及一次性自毁规则；不加载资源或修改玩家手持武器。</summary>
    public static class ArcadeDroneRules
    {
        /// <summary>零步长、无效步长和已结束实例不产生战斗事实；外观阶段不重复结算伤害。</summary>
        public static ArcadeDroneStep Step(ArcadeDroneState state,ArcadeDroneTuning tuning,float deltaTime,
            float ownerX,float ownerZ,int powerTier,bool canAct,bool canFire,IReadOnlyList<ArcadeDroneTarget> targets)
        {
            if(state==null)throw new ArgumentNullException(nameof(state));
            if(!Finite(deltaTime)||deltaTime<=0||state.Phase==ArcadeDronePhase.Expired||!Finite(ownerX)||!Finite(ownerZ))return default;
            if(!IsValid(tuning))throw new ArgumentException("无人机调参无效",nameof(tuning));
            if(state.Phase==ArcadeDronePhase.Exploding)
            {
                state.VisualRemainingSeconds=Math.Max(0,state.VisualRemainingSeconds-deltaTime);
                if(state.VisualRemainingSeconds<=0)Expire(state);
                return default;
            }
            float previousLifetime=state.RemainingSeconds;
            if(!Finite(previousLifetime)||previousLifetime<=0){Expire(state);return default;}
            state.RemainingSeconds=Math.Max(0,previousLifetime-deltaTime);
            state.FireCooldownSeconds=Math.Max(0,state.FireCooldownSeconds-deltaTime);
            powerTier=Math.Max(1,Math.Min(3,powerTier));
            bool beganRam=false;
            if(state.Phase==ArcadeDronePhase.Following)
            {
                state.X=ownerX+tuning.OffsetX;state.Z=ownerZ+tuning.OffsetZ;
                var target=canAct?Nearest(state,targets,tuning.Range):default;
                if(target.Id>0&&state.RemainingSeconds<=tuning.RamWindowSeconds)
                {state.Phase=ArcadeDronePhase.Ramming;state.TargetId=target.Id;beganRam=true;}
                else
                {
                    if(state.RemainingSeconds<=0)Expire(state);
                    else if(target.Id>0&&canFire&&state.FireCooldownSeconds<=0)
                    {
                        Direction(state,target,out float dx,out float dz,out _);
                        state.FireCooldownSeconds=tuning.FireIntervalSeconds/powerTier;
                        return new ArcadeDroneStep(true,false,target.Id,Damage(tuning.Damage,powerTier),0,dx,dz);
                    }
                    return default;
                }
            }
            if(!canAct||!Find(targets,state.TargetId,out var locked)){Expire(state);return default;}
            Direction(state,locked,out float directionX,out float directionZ,out double distance);
            // 跨过临期窗口的大步长只使用窗口内活动时间，不能追溯用整帧时长瞬移撞击。
            float active=Math.Min(deltaTime,previousLifetime);
            if(beganRam)active=Math.Max(0,active-Math.Max(0,previousLifetime-tuning.RamWindowSeconds));
            double travel=tuning.RamSpeed*active;
            if(distance<=travel)
            {
                state.X=locked.X;state.Z=locked.Z;state.Phase=ArcadeDronePhase.Exploding;
                state.RemainingSeconds=0;state.VisualRemainingSeconds=tuning.ExplosionVisualSeconds;
                return new ArcadeDroneStep(false,true,locked.Id,0,Damage(tuning.ExplosionDamage,powerTier),directionX,directionZ);
            }
            state.X+=directionX*(float)travel;state.Z+=directionZ*(float)travel;
            if(state.RemainingSeconds<=0)Expire(state);
            return new ArcadeDroneStep(false,false,locked.Id,0,0,directionX,directionZ);
        }

        /// <summary>双端加载配置时共用完整校验，避免生成表合法但运行时进入永不结束阶段。</summary>
        public static bool IsValid(ArcadeDroneTuning t)=>t!=null&&Positive(t.DurationSeconds)&&Positive(t.Range)&&
            Finite(t.OffsetX)&&Finite(t.OffsetZ)&&t.Damage>0&&Positive(t.FireIntervalSeconds)&&Positive(t.ProjectileSpeed)&&
            Positive(t.ProjectileLifetimeSeconds)&&Positive(t.RamWindowSeconds)&&t.RamWindowSeconds<t.DurationSeconds&&
            Positive(t.RamSpeed)&&t.ExplosionDamage>0&&Positive(t.ExplosionRadius)&&Positive(t.ExplosionVisualSeconds);

        /// <summary>距离相同按稳定身份选敌，避免字典枚举顺序影响双人结果。</summary>
        private static ArcadeDroneTarget Nearest(ArcadeDroneState state,IReadOnlyList<ArcadeDroneTarget> targets,float range)
        {
            ArcadeDroneTarget nearest=default;double best=(double)range*range;
            if(targets==null)return nearest;
            foreach(var target in targets)
            {
                if(target.Id<=0||!Finite(target.X)||!Finite(target.Z))continue;
                double dx=(double)target.X-state.X,dz=(double)target.Z-state.Z,distance=dx*dx+dz*dz;
                if(distance>best||distance==best&&nearest.Id>0&&target.Id>=nearest.Id)continue;
                nearest=target;best=distance;
            }
            return nearest;
        }
        private static bool Find(IReadOnlyList<ArcadeDroneTarget> targets,long id,out ArcadeDroneTarget found)
        {
            if(targets!=null)foreach(var target in targets)if(target.Id==id&&Finite(target.X)&&Finite(target.Z)){found=target;return true;}
            found=default;return false;
        }
        private static void Direction(ArcadeDroneState state,ArcadeDroneTarget target,out float x,out float z,out double distance)
        {
            double dx=(double)target.X-state.X,dz=(double)target.Z-state.Z;distance=Math.Sqrt(dx*dx+dz*dz);
            if(distance<=.000001){x=0;z=1;}else{x=(float)(dx/distance);z=(float)(dz/distance);}
        }
        private static int Damage(int value,int tier)=>(int)Math.Min(int.MaxValue,(long)value*tier);
        private static void Expire(ArcadeDroneState state){state.Phase=ArcadeDronePhase.Expired;state.RemainingSeconds=0;state.VisualRemainingSeconds=0;}
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        private static bool Positive(float value)=>Finite(value)&&value>0;
    }
}
