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
        /// <summary>原活动帧推进能力，购物和加载冻结；普通宝藏房的既有时钟保留。</summary>
        private static bool SlayerMartClockActive(ArcadeCombatRuntime runtime)
            => runtime.PlayerHealth>0&&runtime.RespawnRemaining<=0&&!runtime.IslandTransitionPending&&!runtime.IslandRoomTransitionPending&&
                !runtime.CompletionAwaitingRetry&&!runtime.CompletionSubmitted&&
                !(runtime.SlayerMart?.Ready==true&&!runtime.SlayerMart.Closing);
        /// <summary>统一输入适配入口；成功后才发布HUD，不消费其他能力库存。</summary>
        public bool TryUseFieldUpgrade()
        {
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
