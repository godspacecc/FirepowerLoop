using System;
using System.Collections.Generic;
using UnityEngine;
namespace Game
{
    /// <summary>完整只读附魔快照；原生盾另由原盾端口投影，不得混用。</summary>
    public readonly struct SurvivorEnchantSnapshot
    {
        public readonly int HealthStacks,SpeedStacks,ShieldRemaining,BaseMaxHealth;
        public readonly float BaseSpeed;
        public SurvivorEnchantSnapshot(int health,int speed,int shield,int baseHealth,float baseSpeed)
        {HealthStacks=health;SpeedStacks=speed;ShieldRemaining=shield;BaseMaxHealth=baseHealth;BaseSpeed=baseSpeed;}
    }
    public sealed partial class SurvivorSystem
    {
        /// <summary>每个出生实体的基础值与专用三类附魔；仅本局有效。</summary>
        private sealed class EnchantState
        {
            public int BaseHealth,HealthStacks,SpeedStacks,Shield;
            public float BaseSpeed;
            public double PulseElapsed;
        }
        private readonly Dictionary<int,EnchantState> enchantments=new();
        /// <summary>只在实体出生或首次进入规则时捕获，完整投影不写回基础值。</summary>
        private EnchantState EnchantStateFor(ArcadeEnemyRuntime enemy)
        {
            if(!enchantments.TryGetValue(enemy.Id,out var state))
                enchantments.Add(enemy.Id,state=new EnchantState{BaseHealth=enemy.MaxHealth,BaseSpeed=enemy.Speed});
            return state;
        }
        /// <summary>未来表现只消费不可变副本，不能修改层数或当前盾量。</summary>
        public bool TryGetEnchantSnapshot(int id,out SurvivorEnchantSnapshot snapshot)
        {
            snapshot=default;if(!enemies.ContainsKey(id)||!enchantments.TryGetValue(id,out var s))return false;
            snapshot=new SurvivorEnchantSnapshot(s.HealthStacks,s.SpeedStacks,s.Shield,s.BaseHealth,s.BaseSpeed);return true;
        }
        /// <summary>普通近战与附魔支持者共用移动规则，附魔不接管抓挠。</summary>
        private static bool IsEnchantKind(string kind)=>kind=="EnchantHealth"||kind=="EnchantSpeed"||kind=="EnchantShield";
        /// <summary>活动主循环中推进周期；已爬出实体按距离授予，不做视线判定。</summary>
        private void AdvanceEnchantments(float dt)
        {
            if(missionMode||dt<=0||float.IsNaN(dt)||float.IsInfinity(dt))return;
            foreach(var source in enemies.Values)
            {
                var shape=Shape(source.ConfigId);
                if(source.Health<=0||source.GroundEntry.Active||shape==null||!IsEnchantKind(shape.SpecialKind))continue;
                var state=EnchantStateFor(source);state.PulseElapsed+=dt;
                if(state.PulseElapsed<shape.EnchantIntervalSeconds)continue;
                // 大步长只保留有效授予次数；护盾在同一规则步无伤害穿插，额外周期不会带来更多补充。
                double pulses=Math.Floor(state.PulseElapsed/shape.EnchantIntervalSeconds);
                state.PulseElapsed%=shape.EnchantIntervalSeconds;
                int grants=(int)Math.Min(shape.EnchantMaxStacks,pulses);
                if(shape.SpecialKind=="EnchantShield")grants=(int)Math.Min((long)Math.Ceiling((double)shape.EnchantShieldCap/shape.EnchantShieldGain),pulses);
                foreach(var target in enemies.Values)
                {
                    if(target.Id==source.Id||target.Health<=0||target.GroundEntry.Active||(target.Position-source.Position).sqrMagnitude>(double)shape.EnchantRadius*shape.EnchantRadius)continue;
                    GrantEnchantment(target,shape,grants);
                }
                view.PlaySurvivorEnchantPulse(source.Id,shape.SpecialKind,source.Position,shape.EnchantRadius);
            }
        }
        /// <summary>线性计算、整型饱和，生命只补本次新增上限；同帧多源逐次封顶。</summary>
        private void GrantEnchantment(ArcadeEnemyRuntime enemy,SurvivorEnemyDefinition shape,int grants)
        {
            var s=EnchantStateFor(enemy);
            if(shape.SpecialKind=="EnchantHealth")
            {
                int stacks=(int)Math.Min(shape.EnchantMaxStacks,(long)s.HealthStacks+grants);
                if(stacks>s.HealthStacks)
                {
                    int maximum=(int)Math.Min(int.MaxValue,Math.Round(s.BaseHealth*(1d+stacks*(double)shape.EnchantHealthFraction),MidpointRounding.AwayFromZero));
                    int delta=Math.Max(0,maximum-enemy.MaxHealth);enemy.MaxHealth=maximum;
                    enemy.Health=(int)Math.Min(maximum,(long)enemy.Health+delta);s.HealthStacks=stacks;
                }
            }
            else if(shape.SpecialKind=="EnchantSpeed")
            {
                s.SpeedStacks=(int)Math.Min(shape.EnchantMaxStacks,(long)s.SpeedStacks+grants);
                enemy.Speed=(float)Math.Min(float.MaxValue,s.BaseSpeed*(1d+s.SpeedStacks*(double)shape.EnchantSpeedFraction));
            }
            else s.Shield=(int)Math.Min(shape.EnchantShieldCap,(long)s.Shield+(long)grants*shape.EnchantShieldGain);
            ProjectEnchantment(enemy);
        }
        /// <summary>消耗附魔池优先于原生盾；破盾攻击全部吸收，绝不溢出下一池。</summary>
        private bool AbsorbEnchantShield(ArcadeEnemyRuntime enemy,int damage)
        {
            if(!enchantments.TryGetValue(enemy.Id,out var s)||s.Shield<=0)return false;
            s.Shield=Math.Max(0,s.Shield-damage);ProjectEnchantment(enemy);return true;
        }
        /// <summary>金币仍使用出生强度；死亡移除状态前读取。</summary>
        private int EnchantBirthHealth(ArcadeEnemyRuntime enemy)=>enchantments.TryGetValue(enemy.Id,out var s)?s.BaseHealth:enemy.MaxHealth;
        /// <summary>近场恢复时只投影完整快照，不授予效果或补盾。</summary>
        private void ProjectEnchantment(ArcadeEnemyRuntime enemy)
        {if(TryGetEnchantSnapshot(enemy.Id,out var snapshot))view.SetSurvivorEnemyEnchantment(enemy.Id,snapshot);}
    }
}
