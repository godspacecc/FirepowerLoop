using System;
using System.Collections.Generic;

namespace FirepowerLoop.Arcade
{
    /// <summary>金币宝箱的权威阶段；Ready之前所有金币和金块都不能领取。</summary>
    public enum ArcadeRoundRewardPhase { Falling,Impact,Bursting,Ready,Expired }

    /// <summary>配置提供的宝箱时序、数量和散落范围；数值不从视觉实例读取。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeRoundRewardTuning
    {
        public readonly float FallSeconds,ImpactSeconds,ScatterSeconds,GroundSeconds,PickupRadius;
        public readonly float ScatterBaseRadius,ScatterRadiusStep;
        /// <summary>中央传送门出现时长与接触半径，同样由配置提供。</summary>
        public readonly float PortalAppearSeconds,PortalRadius,PortalX,PortalZ;
        public readonly int CoinCount,BarCount,CoinValue,BarValue,ScatterRadiusLevels;
        public float PresentationSeconds=>FallSeconds+ImpactSeconds+ScatterSeconds;
        public ArcadeRoundRewardTuning(float fall,float impact,float scatter,float ground,float pickupRadius,
            int coins,int bars,int coinValue,int barValue,float baseRadius,float radiusStep,int radiusLevels,float portalAppear=0,float portalRadius=0,float portalX=0,float portalZ=0)
        {FallSeconds=fall;ImpactSeconds=impact;ScatterSeconds=scatter;GroundSeconds=ground;PickupRadius=pickupRadius;
            CoinCount=coins;BarCount=bars;CoinValue=coinValue;BarValue=barValue;ScatterBaseRadius=baseRadius;ScatterRadiusStep=radiusStep;ScatterRadiusLevels=radiusLevels;PortalAppearSeconds=portalAppear;PortalRadius=portalRadius;PortalX=portalX;PortalZ=portalZ;}
        /// <summary>从双端生成的权威配置构造，不在表现层补数值。</summary>
        public static ArcadeRoundRewardTuning FromConfig(
#if DOTNET
            ET.ArcadeRoundRewardConfig c
#else
            global::ArcadeRoundRewardConfig c
#endif
        )=>c==null?null:new ArcadeRoundRewardTuning(c.FallSeconds,c.ImpactSeconds,c.ScatterSeconds,c.GroundSeconds,c.PickupRadius,c.CoinCount,c.BarCount,c.CoinValue,c.BarValue,c.ScatterBaseRadius,c.ScatterRadiusStep,c.ScatterRadiusLevels,c.PortalAppearSeconds,c.PortalRadius,c.PortalX,c.PortalZ);
    }

    /// <summary>每件奖励的稳定身份和最终地面位置，由权威规则生成。</summary>
    public readonly struct ArcadeRoundRewardItem
    {
        public readonly int Id,Value;
        public readonly bool GoldBar;
        public readonly float X,Z;
        public ArcadeRoundRewardItem(int id,bool bar,int value,float x,float z){Id=id;GoldBar=bar;Value=value;X=x;Z=z;}
    }

    /// <summary>仅当前轮拥有的奖励现场；离开即丢弃，不能写成自动领取的钱包。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeRoundRewardState
    {
        public int RoundIdentity;
        public float X,Z,ElapsedSeconds,GroundRemainingSeconds;
        public ArcadeRoundRewardPhase Phase;
        public readonly List<ArcadeRoundRewardItem> Items=new List<ArcadeRoundRewardItem>();
        public readonly HashSet<int> Collected=new HashSet<int>();
    }

    /// <summary>双端共用的每轮一次宝箱及领取时序；不冻结移动、不自动补领。</summary>
    public static class ArcadeRoundRewardRules
    {
        public static bool IsValid(ArcadeRoundRewardTuning t)=>t!=null&&Positive(t.FallSeconds)&&NonNegative(t.ImpactSeconds)&&
            Positive(t.ScatterSeconds)&&Positive(t.GroundSeconds)&&Positive(t.PickupRadius)&&
            t.CoinCount>0&&t.BarCount>0&&t.CoinCount<=128&&t.BarCount<=128&&t.CoinCount+t.BarCount<=128&&t.CoinValue>0&&t.BarValue>0&&
            Finite(t.PortalX)&&Finite(t.PortalZ)&&Positive(t.PresentationSeconds)&&Positive(t.ScatterBaseRadius)&&NonNegative(t.ScatterRadiusStep)&&t.ScatterRadiusLevels>0&&t.ScatterRadiusLevels<=128&&
            Positive(t.ScatterBaseRadius+t.ScatterRadiusStep*(t.ScatterRadiusLevels-1));

        /// <summary>重复清场通知不重置同轮箱子；不同轮才创建新的现场。</summary>
        public static bool TryStart(ref ArcadeRoundRewardState state,ArcadeRoundRewardTuning tuning,int roundIdentity,float x,float z)
        {
            if(!IsValid(tuning)||roundIdentity<=0||!Finite(x)||!Finite(z)||state!=null&&state.RoundIdentity==roundIdentity)return false;
            var next=new ArcadeRoundRewardState{RoundIdentity=roundIdentity,X=x,Z=z,GroundRemainingSeconds=tuning.GroundSeconds};
            int count=tuning.CoinCount+tuning.BarCount;
            for(int i=0;i<count;i++)
            {
                bool bar=i*tuning.BarCount%count<tuning.BarCount;
                float angle=i*2.399963f,radius=tuning.ScatterBaseRadius+i%tuning.ScatterRadiusLevels*tuning.ScatterRadiusStep;
                next.Items.Add(new ArcadeRoundRewardItem(i,bar,bar?tuning.BarValue:tuning.CoinValue,
                    x+(float)Math.Cos(angle)*radius,z+(float)Math.Sin(angle)*radius));
            }
            state=next;return true;
        }

        /// <summary>只消耗实际活动秒；跨过动画结束的大步长只扣余下地面停留时间。</summary>
        public static void Advance(ArcadeRoundRewardState state,ArcadeRoundRewardTuning tuning,float seconds)
        {
            if(state==null||!Finite(seconds)||seconds<=0||state.Phase==ArcadeRoundRewardPhase.Expired)return;
            if(!IsValid(tuning))throw new ArgumentException("金币宝箱配置无效",nameof(tuning));
            double elapsed=(double)state.ElapsedSeconds+seconds;
            state.ElapsedSeconds=(float)Math.Min(elapsed,(double)tuning.PresentationSeconds+tuning.GroundSeconds);
            state.GroundRemainingSeconds=(float)Math.Max(0,tuning.GroundSeconds-Math.Max(0,elapsed-tuning.PresentationSeconds));
            state.Phase=state.GroundRemainingSeconds<=0?ArcadeRoundRewardPhase.Expired:
                elapsed<tuning.FallSeconds?ArcadeRoundRewardPhase.Falling:
                elapsed<tuning.FallSeconds+tuning.ImpactSeconds?ArcadeRoundRewardPhase.Impact:
                elapsed<tuning.PresentationSeconds?ArcadeRoundRewardPhase.Bursting:ArcadeRoundRewardPhase.Ready;
        }

        /// <summary>权威按玩家位置裁决，完整特效前和已领取身份都拒绝。</summary>
        public static bool TryCollect(ArcadeRoundRewardState state,ArcadeRoundRewardTuning tuning,int itemId,
            float playerX,float playerZ,bool alive,out ArcadeRoundRewardItem item)
        {
            item=default;
            if(state==null||!IsValid(tuning)||!alive||!Finite(playerX)||!Finite(playerZ)||
                state.Phase!=ArcadeRoundRewardPhase.Ready||state.Collected.Contains(itemId))return false;
            foreach(var candidate in state.Items)
            {
                if(candidate.Id!=itemId)continue;
                double dx=(double)playerX-candidate.X,dz=(double)playerZ-candidate.Z;
                if(dx*dx+dz*dz>(double)tuning.PickupRadius*tuning.PickupRadius)return false;
                state.Collected.Add(itemId);item=candidate;
                if(state.Collected.Count==state.Items.Count)state.Phase=ArcadeRoundRewardPhase.Expired;
                return true;
            }
            return false;
        }

        /// <summary>从目标点向外寻找可走地面，双端同顺序；仅在清场创建时计算。</summary>
        public static ArcadeRegionPoint FindGround(float x,float z,float radius,float halfWidth,float halfHeight,Func<float,float,bool> blocked)
        {
            bool Clear(float px,float pz)=>Math.Abs(px)<=halfWidth-radius&&Math.Abs(pz)<=halfHeight-radius&&!blocked(px,pz);
            if(Clear(x,z))return new ArcadeRegionPoint(x,z);
            for(float distance=.5f;distance<=Math.Max(halfWidth,halfHeight)*2;distance+=.5f)
                for(int i=0;i<32;i++){float a=i*(float)Math.PI/16,px=x+(float)Math.Cos(a)*distance,pz=z+(float)Math.Sin(a)*distance;if(Clear(px,pz))return new ArcadeRegionPoint(px,pz);}
            throw new InvalidOperationException("地图缺少奖励或传送门可用地面");
        }
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        private static bool Positive(float v)=>Finite(v)&&v>0;
        private static bool NonNegative(float v)=>Finite(v)&&v>=0;
    }
    /// <summary>中央门的活动秒与已离开门口的玩家身份，不写存档。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeStagePortalState
    {
        public float X,Z,ElapsedSeconds;
        public readonly HashSet<long> ArmedPlayers=new HashSet<long>();
    }
    /// <summary>门出现后主动走入才推进；原本站在门口的玩家可先自由拾取奖励。</summary>
    public static class ArcadeStagePortalRules
    {
        public static void Advance(ArcadeStagePortalState state,float seconds)
        {if(state!=null&&seconds>0&&!float.IsNaN(seconds)&&!float.IsInfinity(seconds))state.ElapsedSeconds=(float)Math.Min(float.MaxValue,(double)state.ElapsedSeconds+seconds);}
        public static bool TryContact(ArcadeStagePortalState state,ArcadeRoundRewardTuning tuning,long player,float x,float z,bool alive)
        {
            if(state==null||tuning==null||!alive||float.IsNaN(x)||float.IsNaN(z)||float.IsInfinity(x)||float.IsInfinity(z))return false;
            double dx=(double)x-state.X,dz=(double)z-state.Z;
            if(dx*dx+dz*dz>(double)tuning.PortalRadius*tuning.PortalRadius){state.ArmedPlayers.Add(player);return false;}
            return state.ElapsedSeconds>=tuning.PortalAppearSeconds&&state.ArmedPlayers.Contains(player);
        }
    }

}
