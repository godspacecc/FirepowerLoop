using System;
using System.Collections.Generic;

namespace FirepowerLoop.Arcade
{
    /// <summary>单人和服务器共用的牛撞击飞瞬时状态；零值表示未受击，不写存档。</summary>
    public struct ArcadeHerdImpactState
    {
        /// <summary>剩余活动秒数及已归一化的平面冲撞方向。</summary>
        public float Remaining, DirectionX, DirectionZ;
        public bool Active => Remaining > 0;
    }
    /// <summary>v2动作时间轴及v3玩家侧飞；水平位移由战斗权威裁决，View只采样高度和倾斜。</summary>
    public static class ArcadeHerdImpactRules
    {
        /// <summary>飞行、落地停顿与起身阶段使用统一活动时间。</summary>
        public const float FlightSeconds=.95f, StunSeconds=.5f, RecoverySeconds=.25f;
        /// <summary>总禁用时长、无遮挡时前飞距离与最高抛升基准。</summary>
        public const float TotalSeconds=FlightSeconds+StunSeconds+RecoverySeconds, Distance=3f, Height=2.5f;
        /// <summary>只接受有效方向，已在击飞中的目标不会被后续牛反复重新起飞。</summary>
        public static bool Begin(ref ArcadeHerdImpactState state,float dx,float dz)
        {
            if(state.Active||float.IsNaN(dx)||float.IsNaN(dz)||float.IsInfinity(dx)||float.IsInfinity(dz))return false;
            float length=(float)Math.Sqrt(dx*dx+dz*dz);if(length<.001f||float.IsInfinity(length))return false;
            state=new ArcadeHerdImpactState{Remaining=TotalSeconds,DirectionX=dx/length,DirectionZ=dz/length};return true;
        }
        /// <summary>已采用的侧飞角度：三米行程分为前移1.2米、侧移约2.75米，敌人仍用原方向。</summary>
        public static void PlayerDirection(float dx,float dz,int side,out float x,out float z)
        {
            float lateral=(float)Math.Sqrt(.84f)*(side>=0?1:-1);
            x=.4f*dx-lateral*dz;z=.4f*dz+lateral*dx;
        }
        /// <summary>比较经过地图阻挡后的落点距牛道距离；两侧相同时朝场内，避免中心接触随机甩出场外。</summary>
        public static int PlayerSide(float positiveClearance,float negativeClearance,float towardCenter)
            => Math.Abs(positiveClearance-negativeClearance)>.01f
                ? (positiveClearance>negativeClearance?1:-1) : (towardCenter>=0?1:-1);
        /// <summary>只返回本活动步的水平路程；落地后的0.5秒不再滑动，暂停不消耗状态。</summary>
        public static float Step(ref ArcadeHerdImpactState state,float seconds)
        {
            if(!state.Active||seconds<=0||float.IsNaN(seconds)||float.IsInfinity(seconds))return 0;
            float before=Progress(state.Remaining);state.Remaining=Math.Max(0,state.Remaining-seconds);
            // 消除固定帧累加的浮点尾差，0.5秒落地停顿不会额外多留一帧。
            if(Math.Abs(state.Remaining)<.00001f)state.Remaining=0;
            else if(Math.Abs(state.Remaining-RecoverySeconds)<.00001f)state.Remaining=RecoverySeconds;
            else if(Math.Abs(state.Remaining-StunSeconds-RecoverySeconds)<.00001f)state.Remaining=StunSeconds+RecoverySeconds;
            return (Progress(state.Remaining)-before)*Distance;
        }
        private static float Progress(float remaining)=>Math.Max(0,Math.Min(1,(TotalSeconds-remaining)/FlightSeconds));
        /// <summary>存活者落地保持倒姿后起身；死亡者停在落地倒姿，绝不复活。</summary>
        public static void Sample(float remaining,bool dead,out float height,out float tilt)
        {
            float flight=Progress(remaining),recovery=dead?0:Math.Max(0,Math.Min(1,(TotalSeconds-remaining-FlightSeconds-StunSeconds)/RecoverySeconds));
            height=Height*4*flight*(1-flight)+.12f*flight*(1-recovery);
            tilt=85*Math.Min(1,flight*3)*(1-recovery*recovery*(3-2*recovery));
            if(!dead&&remaining<=0)height=tilt=0;
        }
    }
    /// <summary>牛群单次冲撞的活动阶段；起势仅给身体动作，不产生接触伤害。</summary>
    public enum ArcadeHerdPhase { Windup, Charging, Expired }
    /// <summary>由关卡配置提供的起势秒数、速度、最大行程和实体半径；不存美术尺寸。</summary>
    public readonly struct ArcadeHerdTuning
    {
        public readonly float WindupSeconds,Speed,Distance,Radius;
        public ArcadeHerdTuning(float windup,float speed,float distance,float radius)
        {WindupSeconds=windup;Speed=speed;Distance=distance;Radius=radius;}
    }
    /// <summary>地图拥有的一头牛；切轮直接丢弃，不保存半次冲撞。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeHerdState
    {
        public long MapInstanceId,Id;
        public ArcadeHerdPhase Phase;
        public float X,Z,DirectionX,DirectionZ;
        public double ElapsedSeconds,Travelled;
        internal long StepVersion;
        /// <summary>每次冲撞的目标身份：玩家负数，敌人正数，避免两种身份碰撞。</summary>
        internal readonly HashSet<long> Contacts=new HashSet<long>();
    }
    /// <summary>单个活动步的完整扫掠；包含地图、冲撞及步骤身份，不能在暂停或切图后重复使用。</summary>
    public readonly struct ArcadeHerdSweep
    {
        public readonly long MapInstanceId,ChargeId,Version;
        public readonly float FromX,FromZ,ToX,ToZ,Radius;
        public bool Active=>Version>0;
        public ArcadeHerdSweep(long map,long charge,long version,float ax,float az,float bx,float bz,float radius)
        {MapInstanceId=map;ChargeId=charge;Version=version;FromX=ax;FromZ=az;ToX=bx;ToZ=bz;Radius=radius;}
    }
    /// <summary>地图到View的只读牛姿态；阶段秒数由权威活动时钟提供，View不能自行推进。</summary>
    public readonly struct ArcadeHerdSnapshot
    {
        public readonly long MapInstanceId,Id;
        public readonly ArcadeHerdPhase Phase;
        public readonly float X,Z,DirectionX,DirectionZ;
        public readonly double PhaseSeconds;
        public ArcadeHerdSnapshot(long map,long id,ArcadeHerdPhase phase,float x,float z,float dx,float dz,double seconds)
        {MapInstanceId=map;Id=id;Phase=phase;X=x;Z=z;DirectionX=dx;DirectionZ=dz;PhaseSeconds=seconds;}
    }
    /// <summary>双端同源的锁向、活动时间及扫掠规则；不写生命、掉落或保护状态。</summary>
    public static class ArcadeHerdRules
    {
        /// <summary>双端共用固定东西向通路；牛沿行进方向前后排成一队，往返交替而地形不随机。</summary>
        public static void StartBatch(List<ArcadeHerdState> cows,ArcadeEarlyStageParameters stage,long map,int batch)
        {
            float direction=(batch%2==1)?1:-1;
            // 行程覆盖整队长度，使头牛在入口外起步、尾牛在出口外回收。
            float entry=(stage.Herd.Distance-(stage.HerdCount-1)*stage.HerdSpacing)*.5f;
            for(int i=0;i<stage.HerdCount;i++)
                if(TryStart(map,(long)batch*stage.HerdCount+i+1,-direction*(entry+i*stage.HerdSpacing),
                    -stage.PortalY,direction,0,stage.Herd,out var cow))cows.Add(cow);
        }
        /// <summary>只读姿态使用阶段年龄，切换冲撞时从零开始动作。</summary>
        public static ArcadeHerdSnapshot Snapshot(ArcadeHerdState cow,ArcadeHerdTuning tuning)=>new(cow.MapInstanceId,cow.Id,cow.Phase,cow.X,cow.Z,cow.DirectionX,cow.DirectionZ,
            cow.Phase==ArcadeHerdPhase.Windup?cow.ElapsedSeconds:Math.Max(0,cow.ElapsedSeconds-tuning.WindupSeconds));
        /// <summary>生成时一次锁定朝向，之后不根据玩家位置转弯。</summary>
        public static bool TryStart(long map,long id,float x,float z,float directionX,float directionZ,ArcadeHerdTuning tuning,out ArcadeHerdState state)
        {
            state=null;
            if(map<=0||id<=0||!Valid(tuning)||!Finite(x)||!Finite(z)||!Finite(directionX)||!Finite(directionZ))return false;
            double length=Math.Sqrt((double)directionX*directionX+(double)directionZ*directionZ);
            if(length<=.000001)return false;
            state=new ArcadeHerdState{MapInstanceId=map,Id=id,X=x,Z=z,
                DirectionX=(float)(directionX/length),DirectionZ=(float)(directionZ/length),Phase=ArcadeHerdPhase.Windup};
            return true;
        }
        /// <summary>只推进活动秒；跨起势边界仅消费冲撞部分，大步长仍返回完整末段以防穿透目标。</summary>
        public static ArcadeHerdSweep Step(ArcadeHerdState state,ArcadeHerdTuning tuning,float seconds,bool active)
        {
            if(state==null)return default;
            // 每次调用都使旧扫掠失效，暂停帧不能重放上一帧接触。
            if(state.StepVersion==long.MaxValue){Cancel(state);return default;}
            state.StepVersion++;
            if(!active||!Finite(seconds)||seconds<=0||state.MapInstanceId<=0||state.Phase==ArcadeHerdPhase.Expired)return default;
            if(!Valid(tuning)||!Finite(state.X)||!Finite(state.Z)||!Finite(state.DirectionX)||!Finite(state.DirectionZ))
            {Cancel(state);return default;}
            double before=Math.Max(0,state.ElapsedSeconds-tuning.WindupSeconds);
            state.ElapsedSeconds+=seconds;
            if(state.ElapsedSeconds<tuning.WindupSeconds)return default;
            state.Phase=ArcadeHerdPhase.Charging;
            double chargeSeconds=Math.Max(0,state.ElapsedSeconds-tuning.WindupSeconds)-before;
            double travel=Math.Min(Math.Max(0,tuning.Distance-state.Travelled),chargeSeconds*tuning.Speed);
            if(travel<=0)return default;
            float ax=state.X,az=state.Z;
            float bx=(float)(ax+state.DirectionX*travel),bz=(float)(az+state.DirectionZ*travel);
            if(!Finite(bx)||!Finite(bz)){Cancel(state);return default;}
            state.X=bx;state.Z=bz;state.Travelled+=travel;
            if(state.Travelled>=tuning.Distance)state.Phase=ArcadeHerdPhase.Expired;
            return new ArcadeHerdSweep(state.MapInstanceId,state.Id,state.StepVersion,ax,az,bx,bz,tuning.Radius);
        }
        /// <summary>扫掠胶囊包含端帽，单次冲撞每目标只登记一次；真实伤害仍走战斗系统保护与死亡结算。</summary>
        public static bool TryContact(ArcadeHerdState state,ArcadeHerdSweep sweep,long currentMap,long targetId,float x,float z,float radius,bool alive)
        {
            if(state==null||!alive||targetId==0||currentMap<=0||!sweep.Active||state.MapInstanceId!=currentMap||
                sweep.MapInstanceId!=currentMap||sweep.ChargeId!=state.Id||sweep.Version!=state.StepVersion)return false;
            return ArcadeRoundContentRules.TouchesSegment(x,z,radius,sweep.FromX,sweep.FromZ,sweep.ToX,sweep.ToZ,sweep.Radius)&&state.Contacts.Add(targetId);
        }
        /// <summary>切图或拥有者释放立即使已有扫掠失效，不把半次冲撞带到新地图。</summary>
        public static void Cancel(ArcadeHerdState state)
        {if(state==null)return;state.Phase=ArcadeHerdPhase.Expired;state.MapInstanceId=0;state.StepVersion=0;}
        /// <summary>配置适配层和生成入口共用，拒绝无限行程或倒退等坏值。</summary>
        public static bool Valid(ArcadeHerdTuning t)=>Finite(t.WindupSeconds)&&t.WindupSeconds>=0&&
            Finite(t.Speed)&&t.Speed>0&&Finite(t.Distance)&&t.Distance>0&&Finite(t.Radius)&&t.Radius>0;
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    }
}
