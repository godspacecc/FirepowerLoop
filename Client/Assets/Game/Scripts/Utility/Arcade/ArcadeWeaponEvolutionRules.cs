using System;
using System.Collections.Generic;
#if DOTNET
using UpgradeConfig=ET.ArcadeWeaponUpgradeConfig;
#else
using UpgradeConfig=global::ArcadeWeaponUpgradeConfig;
#endif
namespace FirepowerLoop.Arcade
{
    /// <summary>紫级六枪进化参数，只从正式升级表读取，双端共享。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeWeaponEvolutionTuning
    {
        public readonly int RapidPeriod;
        public readonly float RapidSpreadDegrees;
        public readonly float ShotgunPushRadius;
        public readonly float ShotgunPushDistance;
        public readonly float FlameGroundSeconds;
        public readonly float FlameGroundTickSeconds;
        public readonly float FlameGroundRadius;
        public readonly float FlameGroundDamageRatio;
        public readonly int FlameGroundMaxAreas;
        public readonly int PiercingPeriod;
        public readonly float PiercingRadius;
        public readonly int PiercingHits;
        public readonly int ClusterCount;
        public readonly float ClusterSeconds;
        public readonly float ClusterRadius;
        public readonly float ClusterDamageRatio;
        public readonly float ClusterSpeed;
        public readonly float ContinuousResetSeconds;
        public ArcadeWeaponEvolutionTuning(UpgradeConfig r)
        {
            RapidPeriod=r.PurpleRapidPeriod;
            RapidSpreadDegrees=r.PurpleRapidSpreadDegrees;
            ShotgunPushRadius=r.PurpleShotgunPushRadius;
            ShotgunPushDistance=r.PurpleShotgunPushDistance;
            FlameGroundSeconds=r.PurpleFlameGroundSeconds;
            FlameGroundTickSeconds=r.PurpleFlameGroundTickSeconds;
            FlameGroundRadius=r.PurpleFlameGroundRadius;
            FlameGroundDamageRatio=r.PurpleFlameGroundDamageRatio;
            FlameGroundMaxAreas=r.PurpleFlameGroundMaxAreas;
            PiercingPeriod=r.PurplePiercingPeriod;
            PiercingRadius=r.PurplePiercingRadius;
            PiercingHits=r.PurplePiercingHits;
            ClusterCount=r.PurpleClusterCount;
            ClusterSeconds=r.PurpleClusterSeconds;
            ClusterRadius=r.PurpleClusterRadius;
            ClusterDamageRatio=r.PurpleClusterDamageRatio;
            ClusterSpeed=r.PurpleClusterSpeed;
            ContinuousResetSeconds=r.PurpleContinuousResetSeconds;
        }
        /// <summary>限定有限正数与派生上限，避免无限子弹或零间隔循环。</summary>
        public bool Valid=>RapidPeriod>0&&RapidPeriod<=256&&Positive(RapidSpreadDegrees)&&Positive(ShotgunPushRadius)&&Positive(ShotgunPushDistance)&&Positive(FlameGroundSeconds)&&Positive(FlameGroundTickSeconds)&&Positive(FlameGroundRadius)&&Positive(FlameGroundDamageRatio)&&FlameGroundMaxAreas>0&&FlameGroundMaxAreas<=256&&PiercingPeriod>0&&PiercingPeriod<=256&&Positive(PiercingRadius)&&PiercingHits>0&&PiercingHits<=256&&ClusterCount>0&&ClusterCount<=256&&Positive(ClusterSeconds)&&Positive(ClusterRadius)&&Positive(ClusterDamageRatio)&&Positive(ClusterSpeed)&&Positive(ContinuousResetSeconds);
        private static bool Positive(float v)=>v>0&&!float.IsNaN(v)&&!float.IsInfinity(v);
    }
    /// <summary>仅成功射击推进的连续开火相位，不保存到轮前节点。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeWeaponEvolutionState
    {
        public string WeaponId;
        public int Level,Shots;
        public float IdleSeconds;
        public int Next(string weapon,int level,ArcadeWeaponEvolutionTuning t)=>level<2?1:
            WeaponId!=weapon||Level!=level||IdleSeconds>t.ContinuousResetSeconds?1:Shots+1;
        public void Commit(string weapon,int level,int shot){WeaponId=weapon;Level=level;Shots=shot;IdleSeconds=0;}
        public void Clear(){WeaponId=null;Shots=Level=0;IdleSeconds=0;}
    }
    /// <summary>当前地图有限残火，邻近火团合并，不按弹丸重叠叠伤。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeWeaponBurnArea
    {
        public int Id,Damage;
        public long OwnerId;
        public float X,Z,RemainingSeconds,TickRemainingSeconds;
        public int Step(float seconds,float interval)
        {
            float active=Math.Min(Math.Max(0,seconds),RemainingSeconds);
            RemainingSeconds=Math.Max(0,RemainingSeconds-seconds);TickRemainingSeconds-=active;
            int ticks=0;
            while(TickRemainingSeconds<=0&&active>0){ticks++;TickRemainingSeconds+=interval;}
            return ticks;
        }
    }
    /// <summary>能力仅依据发射快照，支援与敌弹不能获得紫级武器特技。</summary>
    public static class ArcadeWeaponEvolutionRules
    {
        /// <summary>每个敌人与玩家来源共用接触时钟；spatiallyValidated 仅接受调用方已逐区域三维验证的集合，不再用根节点重筛。</summary>
        public static int BurningDamage(IReadOnlyList<ArcadeWeaponBurnArea> areas,long owner,float x,float z,ref float contact,float seconds,ArcadeWeaponEvolutionTuning t,bool spatiallyValidated=false)
        {
            float active=0;double radiusSquared=t.FlameGroundRadius*t.FlameGroundRadius;
            foreach(var area in areas)
                if(area.OwnerId==owner&&(spatiallyValidated||(area.X-x)*(area.X-x)+(area.Z-z)*(area.Z-z)<=radiusSquared))
                    active=Math.Max(active,Math.Min(seconds,area.RemainingSeconds));
            if(active<=0){contact=0;return 0;}
            float prior=contact;contact+=active;int ticks=(int)Math.Floor((contact+.000001f)/t.FlameGroundTickSeconds);long total=0;
            for(int i=1;i<=ticks;i++)
            {
                float time=i*t.FlameGroundTickSeconds-prior;int damage=0;
                foreach(var area in areas)
                    if(area.OwnerId==owner&&area.RemainingSeconds+.000001f>=time&&
                        (spatiallyValidated||(area.X-x)*(area.X-x)+(area.Z-z)*(area.Z-z)<=radiusSquared))damage=Math.Max(damage,area.Damage);
                total+=damage;
            }
            // 长帧后半段若火已熄灭，也存在真实空窗；下一团火不能继承前半帧的接触蓄积。
            contact=active+ .000001f<seconds?0:Math.Max(0,contact-ticks*t.FlameGroundTickSeconds);
            return (int)Math.Min(int.MaxValue,total);
        }
        public static bool IsPurple(string weapon,int level,bool playerWeapon,string expected)=>playerWeapon&&level>=2&&weapon==expected;
        public static bool Periodic(int shot,int period)=>shot>0&&period>0&&shot%period==0;
        public static int DerivedDamage(int damage,float ratio)=>(int)Math.Max(1,Math.Min(int.MaxValue,Math.Round(damage*(double)ratio)));
    }
}
