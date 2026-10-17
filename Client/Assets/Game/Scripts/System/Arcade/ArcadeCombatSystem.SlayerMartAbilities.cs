using System;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>正式配置由入场事务提供；测试使用同一生成表适配。</summary>
        private ArcadeSlayerMartConfiguration slayerMartConfig;
        /// <summary>单人固定原始成员1，不把商店余额当作另一份能力状态。</summary>
        private ArcadeSlayerMartPlayer SlayerMartAbility(ArcadeCombatRuntime runtime)
            => runtime.SlayerMartRulesVersion==1?runtime.SlayerMart?.Players.FirstOrDefault(p=>p.PlayerId==1):null;
        /// <summary>普通强化只映射对应精通，价格和倍率仍来自正式配置。</summary>
        private static SlayerMartEffect SlayerMartMastery(global::ArcadePowerupBehavior behavior)
            => behavior switch
            {
                global::ArcadePowerupBehavior.Magnet=>SlayerMartEffect.MagnetMastery,
                global::ArcadePowerupBehavior.Decoy=>SlayerMartEffect.DecoyMastery,
                global::ArcadePowerupBehavior.Gloves=>SlayerMartEffect.GlovesMastery,
                global::ArcadePowerupBehavior.Chicken=>SlayerMartEffect.ChickenMastery,
                global::ArcadePowerupBehavior.Barrel=>SlayerMartEffect.BarrelMastery,
                global::ArcadePowerupBehavior.Saw=>SlayerMartEffect.SawMastery,
                _=>0
            };
        /// <summary>原始半径每次只乘一次，已有实例购买精通后立即使用新半径。</summary>
        private float SlayerMartRadius(ArcadeCombatRuntime runtime,ArcadePowerupRuntime powerup)
            => slayerMartConfig==null?powerup.Definition.Radius:ArcadeSlayerMartAbilityRules.MasteryRadius(powerup.Definition.Radius,
                SlayerMartMastery(powerup.Definition.Behavior),SlayerMartAbility(runtime),slayerMartConfig.Parameter);
        /// <summary>发射时叠到旧属性结果之上，命中时不再重复乘精通。</summary>
        private int SlayerMartDamage(ArcadeCombatRuntime runtime,int damage,SlayerMartDamageSource source)
            => slayerMartConfig==null?damage:ArcadeSlayerMartAbilityRules.ResolveDamage(damage,source,SlayerMartAbility(runtime),slayerMartConfig.Parameter);
        /// <summary>原活动帧推进能力，购物和加载冻结；普通宝藏房的既有时钟保留。</summary>
        private static bool SlayerMartClockActive(ArcadeCombatRuntime runtime)
            => runtime.PlayerHealth>0&&runtime.RespawnRemaining<=0&&!runtime.IslandTransitionPending&&!runtime.IslandRoomTransitionPending&&
                !runtime.CompletionAwaitingRetry&&!runtime.CompletionSubmitted&&
                !(runtime.SlayerMart?.Ready==true&&!runtime.SlayerMart.Closing);
        /// <summary>统一输入适配入口；成功后才发布HUD，不消费其他能力库存。</summary>
        public bool TryUseFieldUpgrade()
        {
            var app=this.GetModel<AppStateModel>();
            if(app==null||app.CurrentState.Value!=AppState.Playing||UIHelper.HasUIOpen)return false;
            var runtime=this.GetModel<ArcadeCombatModel>().Runtime;
            if(runtime==null||!UseSlayerMartUpgrade(runtime))return false;
            PublishHud(runtime);return true;
        }
        /// <summary>可控、非菜单且处于当前战斗时才消耗满充。</summary>
        private bool UseSlayerMartUpgrade(ArcadeCombatRuntime runtime)
            => slayerMartConfig!=null&&ArcadeSlayerMartAbilityRules.TryActivate(SlayerMartAbility(runtime),
                SlayerMartClockActive(runtime)&&!runtime.InTreasureRoom&&!runtime.AwaitingExit&&SlayerMartAbility(runtime)?.MenuOpen!=true,slayerMartConfig.Parameter);
        /// <summary>伤害仍进入正式敌人护甲/特殊规则入口；结算作用域屏蔽风暴自身充能。</summary>
        private void TickSlayerMartStorm(ArcadeCombatRuntime runtime,float seconds)
        {
            var state=SlayerMartAbility(runtime);
            if(slayerMartConfig==null||state==null||!SlayerMartClockActive(runtime))return;
            int ticks=ArcadeSlayerMartAbilityRules.AdvanceStorm(state,seconds,true,slayerMartConfig.Parameter);
            if(runtime.InTreasureRoom||ticks==0)return;
            float radius=slayerMartConfig.Parameter("storm.radius");
            bool previous=runtime.SlayerMartStormDamage;runtime.SlayerMartStormDamage=true;
            try
            {
                for(int tick=0;tick<ticks;tick++)foreach(var enemy in runtime.Enemies.Values.ToArray())
                {
                    if((enemy.Position-runtime.PlayerPosition).sqrMagnitude>radius*radius)continue;
                    DamageEnemy(enemy,(int)slayerMartConfig.Parameter("storm.damage"));
                    if(enemy.Health<=0)KillEnemy(runtime,enemy);
                }
            }
            finally{runtime.SlayerMartStormDamage=previous;}
        }
        /// <summary>实时按圆内位置聚合控制，离圈或到期不遗留眩晕与速度改写。</summary>
        private float SlayerMartStormSpeed(ArcadeCombatRuntime runtime,ArcadeEnemyRuntime enemy)
        {
            var state=SlayerMartAbility(runtime);if(slayerMartConfig==null||state==null)return 1;
            float radius=slayerMartConfig.Parameter("storm.radius");
            bool inside=runtime.PlayerHealth>0&&!runtime.InTreasureRoom&&state.StormRemaining>0&&
                (enemy.Position-runtime.PlayerPosition).sqrMagnitude<=radius*radius;
            return ArcadeSlayerMartAbilityRules.StormSpeedScale(inside,enemy.ContentBossKind>0||enemy.IsBossPresentation||!string.IsNullOrEmpty(enemy.BossId),enemy.ContentSpecialKind>0,slayerMartConfig.Parameter);
        }
    }
}
