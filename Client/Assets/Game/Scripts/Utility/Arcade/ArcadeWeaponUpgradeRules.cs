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
        public ArcadeWeaponUpgradeTuning(float minimum,float move,string red,string purple)
        {MinimumPowerSeconds=minimum;PurpleMoveSpeedPercent=move;RedPowerupId=red;PurplePowerupId=purple;}
        public static ArcadeWeaponUpgradeTuning FromConfig(UpgradeConfig row)=>row==null?null:new ArcadeWeaponUpgradeTuning(row.MinimumPowerSeconds,row.PurpleMoveSpeedPercent,row.RedPowerupId,row.PurplePowerupId);
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
            !string.IsNullOrWhiteSpace(t.RedPowerupId)&&!string.IsNullOrWhiteSpace(t.PurplePowerupId)&&t.RedPowerupId!=t.PurplePowerupId;
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
