using System;

namespace FirepowerLoop.Arcade
{
    /// <summary>明确失命的机关跳过新增护甲，普通伤害共用挡伤入口。</summary>
    public enum SlayerMartHitKind { Ordinary, LethalMechanism }
    /// <summary>命中裁决来源，宿主据此消费旧护盾或显示反馈。</summary>
    public enum SlayerMartBlockResult { None, Protected, Shield, Plate }
    /// <summary>精通允许的伤害来源；不能从最终伤害反推来源。</summary>
    public enum SlayerMartDamageSource { Weapon, Chicken, Sentry, Bomb, Barrel, Orb, Saw, Gloves, Storm }
    /// <summary>双端同源的个人能力规则；参数由正式配置提供，不直接修改角色HP。</summary>
    public static class ArcadeSlayerMartAbilityRules
    {
        /// <summary>优先级固定为保护、旧护盾、护甲；失命机关跳过新增护甲。</summary>
        public static SlayerMartBlockResult TryBlockHit(SlayerMartHitKind kind,ArcadeSlayerMartAbilityState state,bool protectedHit,bool shield)
        {
            if(protectedHit)return SlayerMartBlockResult.Protected;
            if(shield)return SlayerMartBlockResult.Shield;
            if(kind==SlayerMartHitKind.LethalMechanism||state==null||state.Plates<=0)return SlayerMartBlockResult.None;
            state.Plates--;return SlayerMartBlockResult.Plate;
        }
        /// <summary>在旧属性结果之上一次取整；不同来源精通互不串用。</summary>
        public static int ResolveDamage(int baseDamage,SlayerMartDamageSource source,ArcadeSlayerMartAbilityState state,Func<string,float> parameter)
        {
            double scale=1;
            if(Owns(state,12)&&(source==SlayerMartDamageSource.Weapon||source==SlayerMartDamageSource.Chicken))scale=parameter("mastery.deadOperator");
            if(Owns(state,23)&&source==SlayerMartDamageSource.Sentry)scale=parameter("mastery.sentry");
            return (int)Math.Min(int.MaxValue,Math.Max(0,Math.Round(baseDamage*scale,MidpointRounding.ToEven)));
        }
        /// <summary>合法本人击杀充能；活动期、风暴和无归属清场不积攒。</summary>
        public static void RegisterKill(ArcadeSlayerMartAbilityState state,bool attributed,bool stormKill,Func<string,float> parameter)
        {
            if(!Owns(state,16)||!attributed||stormKill||state.StormRemaining>0)return;
            int maximum=(int)parameter("storm.kills");
            if(state.StormCharge<maximum)state.StormCharge++;
        }
        /// <summary>宿主验证可操作阶段后才能消费唯一满充。</summary>
        public static bool TryActivate(ArcadeSlayerMartAbilityState state,bool canUse,Func<string,float> parameter)
        {
            if(!canUse||!Owns(state,16)||!IsValid(state,parameter)||state.StormRemaining>0||state.StormCharge<(int)parameter("storm.kills"))return false;
            state.StormCharge=0;state.StormRemaining=parameter("storm.duration");state.StormTickElapsed=0;return true;
        }
        /// <summary>只推进活动时钟；首个完整周期才伤害，长帧不丢末尾周期。</summary>
        public static int AdvanceStorm(ArcadeSlayerMartAbilityState state,double seconds,bool active,Func<string,float> parameter)
        {
            if(!IsValid(state,parameter))throw new InvalidOperationException("电击风暴恢复状态无效");
            if(state==null||!active||state.StormRemaining<=0||seconds<=0||double.IsNaN(seconds)||double.IsInfinity(seconds))return 0;
            double step=Math.Min(seconds,state.StormRemaining),interval=parameter("storm.interval");
            state.StormRemaining=Math.Max(0,state.StormRemaining-step);state.StormTickElapsed+=step;
            int ticks=(int)Math.Floor((state.StormTickElapsed+1e-9)/interval);
            state.StormTickElapsed=Math.Max(0,state.StormTickElapsed-ticks*interval);
            if(state.StormRemaining==0)state.StormTickElapsed=0;
            return ticks;
        }
        /// <summary>控制直接根据当前位置求值，离圈立即移除，不改写原速度。</summary>
        public static float StormSpeedScale(bool inRange,bool boss,bool elite,Func<string,float> parameter)
            => !inRange||boss?1:elite?parameter("storm.eliteSpeedScale"):0;
        /// <summary>死亡保留背心、精通和未使用充能；清除板与活动风暴。</summary>
        public static void OnDeath(ArcadeSlayerMartAbilityState state)
        {if(state==null)return;state.Plates=0;state.StormRemaining=0;state.StormTickElapsed=0;}
        /// <summary>只在真正复起时生成一次保护，恢复剩余量不再乘。</summary>
        public static float RespawnProtection(float contactSeconds,ArcadeSlayerMartAbilityState state,Func<string,float> parameter)
            => contactSeconds*2*(Owns(state,21)?parameter("mastery.respawn"):1);
        /// <summary>只在新实例生成或正常刷新时计算时长，购买精通不回填已过去时间。</summary>
        public static float MasteryDuration(float seconds,SlayerMartEffect mastery,ArcadeSlayerMartAbilityState state,Func<string,float> parameter)
        {
            if(!Owns(state,(int)mastery))return seconds;
            string key=mastery==SlayerMartEffect.MagnetMastery?"mastery.magnet":
                mastery==SlayerMartEffect.GlovesMastery?"mastery.gloves":mastery==SlayerMartEffect.ChickenMastery?"mastery.chicken":
                mastery==SlayerMartEffect.BarrelMastery?"mastery.barrel":null;
            return key==null?seconds:seconds*parameter(key);
        }
        /// <summary>活动半径从原始配置求值，重复帧不会把上次结果再次相乘。</summary>
        public static float MasteryRadius(float radius,SlayerMartEffect mastery,ArcadeSlayerMartAbilityState state,Func<string,float> parameter)
        {
            if(!Owns(state,(int)mastery))return radius;
            string key=mastery==SlayerMartEffect.MagnetMastery?"mastery.magnet":
                mastery==SlayerMartEffect.DecoyMastery?"mastery.decoy":mastery==SlayerMartEffect.SawMastery?"mastery.sawDistance":null;
            return key==null?radius:radius*parameter(key);
        }
        /// <summary>爆桶轨道速度独立于触发次数和命中冷却。</summary>
        public static float MasteryOrbitSpeed(float speed,ArcadeSlayerMartAbilityState state,Func<string,float> parameter)
            => Owns(state,22)?speed*parameter("mastery.barrel"):speed;
        /// <summary>恢复时依正式配置校验上界；文件层只检查不依赖配置的结构。</summary>
        public static bool IsValid(ArcadeSlayerMartAbilityState state,Func<string,float> parameter)
        {
            if(state==null)return true;
            if(state.StormCharge<0||state.StormCharge>parameter("storm.kills")||
                double.IsNaN(state.StormRemaining)||double.IsInfinity(state.StormRemaining)||state.StormRemaining<0||state.StormRemaining>parameter("storm.duration")||
                double.IsNaN(state.StormTickElapsed)||double.IsInfinity(state.StormTickElapsed)||state.StormTickElapsed<0||state.StormTickElapsed>=parameter("storm.interval"))return false;
            return (Owns(state,16)||state.StormCharge==0&&state.StormRemaining==0)&&
                (state.StormRemaining>0||state.StormTickElapsed==0);
        }
        /// <summary>固定商品身份与源表效果编号一致；不使用本地化名称判断能力。</summary>
        private static bool Owns(ArcadeSlayerMartAbilityState state,int effect)
            => state?.Owned?.Contains("slayermart.item."+effect.ToString("00",System.Globalization.CultureInfo.InvariantCulture))==true;
    }
}
