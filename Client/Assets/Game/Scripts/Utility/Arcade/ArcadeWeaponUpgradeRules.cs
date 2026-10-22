using System;
#if DOTNET
using UpgradeConfig=ET.ArcadeWeaponUpgradeConfig;
#else
using UpgradeConfig=global::ArcadeWeaponUpgradeConfig;
#endif

namespace FirepowerLoop.Arcade
{
    /// <summary>配置映射后的不可变升级参数；两端只读取唯一配置源。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeWeaponUpgradeTuning
    {
        public readonly float MinimumPowerSeconds,PurpleMoveSpeedPercent;
        public readonly string RedPowerupId,PurplePowerupId;
        /// <summary>紫火箭数量、扇角、限幅转向与选敌半径；均为项目调参。</summary>
        public readonly int PurpleRocketCount;
        public readonly float PurpleRocketSpreadDegrees,PurpleRocketTurnRadians,PurpleRocketRange;
        /// <summary>已采用的紫射线反射次数、额外目标数和连接半径。</summary>
        public readonly int PurpleRayReflections,PurpleRayChainCount;
        public readonly float PurpleRayChainRange;
        public ArcadeWeaponUpgradeTuning(float minimum,float move,string red,string purple,int rocketCount,float rocketSpread,float rocketTurn,float rocketRange,int rayReflections,int rayCount,float rayRange)
        {MinimumPowerSeconds=minimum;PurpleMoveSpeedPercent=move;RedPowerupId=red;PurplePowerupId=purple;PurpleRocketCount=rocketCount;PurpleRocketSpreadDegrees=rocketSpread;PurpleRocketTurnRadians=rocketTurn;PurpleRocketRange=rocketRange;PurpleRayReflections=rayReflections;PurpleRayChainCount=rayCount;PurpleRayChainRange=rayRange;}
        public static ArcadeWeaponUpgradeTuning FromConfig(UpgradeConfig row)=>row==null?null:new ArcadeWeaponUpgradeTuning(row.MinimumPowerSeconds,row.PurpleMoveSpeedPercent,row.RedPowerupId,row.PurplePowerupId,row.PurpleRocketCount,row.PurpleRocketSpreadDegrees,row.PurpleRocketTurnRadians,row.PurpleRocketRange,row.PurpleRayReflections,row.PurpleRayChainCount,row.PurpleRayChainRange);
    }
    /// <summary>同枪和骷髅共用等级、补时与移动规则；不写武器持有或持久化状态。</summary>
    public static class ArcadeWeaponUpgradeRules
    {
        /// <summary>零为普通，一为红，二为紫；失效强化不参与升级。</summary>
        public static int Level(float magnitude,float seconds)=>seconds>0&&Finite(seconds)&&Finite(magnitude)?Math.Max(0,Math.Min(2,(int)Math.Round(magnitude)-1)):0;
        public static int RepeatedLevel(int current)=>Math.Min(2,Math.Max(0,current)+1);
        /// <summary>两种时钟只补足不缩短；强化最低秒数由调用方传入配置值，武器时钟不套此下限。</summary>
        public static float Refill(float current,float grant)=>Math.Max(Finite(current)?Math.Max(0,current):0,Finite(grant)?Math.Max(0,grant):0);
        public static float PowerSeconds(float current,float grant,ArcadeWeaponUpgradeTuning tuning)=>Refill(current,Refill(grant,tuning.MinimumPowerSeconds));
        public static float MovementMultiplier(int level,ArcadeWeaponUpgradeTuning tuning)=>level>=2?1+tuning.PurpleMoveSpeedPercent:1;
        public static bool Valid(ArcadeWeaponUpgradeTuning t)=>t!=null&&Finite(t.MinimumPowerSeconds)&&t.MinimumPowerSeconds>0&&Finite(t.PurpleMoveSpeedPercent)&&t.PurpleMoveSpeedPercent>=0&&t.PurpleMoveSpeedPercent<=1&&
            !string.IsNullOrWhiteSpace(t.RedPowerupId)&&!string.IsNullOrWhiteSpace(t.PurplePowerupId)&&t.RedPowerupId!=t.PurplePowerupId&&t.PurpleRocketCount==3&&Finite(t.PurpleRocketSpreadDegrees)&&t.PurpleRocketSpreadDegrees>=0&&t.PurpleRocketSpreadDegrees<=90&&
            Finite(t.PurpleRocketTurnRadians)&&t.PurpleRocketTurnRadians>0&&Finite(t.PurpleRocketRange)&&t.PurpleRocketRange>0&&t.PurpleRayReflections==2&&t.PurpleRayChainCount==2&&Finite(t.PurpleRayChainRange)&&t.PurpleRayChainRange>0;
        /// <summary>三枚合计保留原强化单发预算，余数按发射顺序分配，不逐枚重新套紫级倍率。</summary>
        public static int SplitVolleyDamage(int total,int count,int index)=>count>0&&index>=0&&index<count?total/count+(index<total%count?1:0):0;
        /// <summary>非海岛弹道的外缘首次接触比例；海岛海面不使用行走边界裁断飞行。</summary>
        public static bool TryArenaExit(float ax,float az,float bx,float bz,float halfWidth,float halfHeight,out float entry)
        {
            entry=1;bool hit=false;
            if(Math.Abs(bx)>halfWidth&&bx!=ax){entry=Math.Min(entry,((bx>0?halfWidth:-halfWidth)-ax)/(bx-ax));hit=true;}
            if(Math.Abs(bz)>halfHeight&&bz!=az){entry=Math.Min(entry,((bz>0?halfHeight:-halfHeight)-az)/(bz-az));hit=true;}
            entry=Math.Max(0,entry);return hit;
        }
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);

        /// <summary>矩形首次接触后按最近面反射；直角同时翻转两轴，不逐轴重复消耗次数。</summary>
        public static void Reflect(float x,float z,float cx,float cz,float hx,float hz,ref float vx,ref float vz)
        {
            float dx=Math.Abs(Math.Abs(x-cx)-hx),dz=Math.Abs(Math.Abs(z-cz)-hz);
            if(dx<=dz+.0001f)vx=-vx;
            if(dz<=dx+.0001f)vz=-vz;
        }
    }

    /// <summary>权威拥有的短时电弧，仅保存只读线段和活动余量，不参与危险或伤害判定。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeRayArcState
    {
        public const int Kind=30;
        public readonly ArcadeContentAreaSnapshot Snapshot;
        public float RemainingSeconds;
        public ArcadeRayArcState(int id,float x,float z,float endX,float endZ)
        {Snapshot=new ArcadeContentAreaSnapshot(id,Kind,x,z,.07f,false,endX,endZ,true);RemainingSeconds=.18f;}
    }
}
