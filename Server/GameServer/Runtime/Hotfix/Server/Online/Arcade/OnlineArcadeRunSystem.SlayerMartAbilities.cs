using System;
using System.Linq;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>依原始成员身份读取个人能力，不从队友或客户端快照借用。</summary>
        private static ArcadeSlayerMartPlayer SlayerMartAbility(OnlineArcadeBattleRuntime battle,long playerId)
            => battle?.SlayerMartRulesVersion==1?battle.SlayerMart?.Players.FirstOrDefault(p=>p.PlayerId==playerId):null;
        /// <summary>普通道具与个人精通的固定映射，不修改正式掉落基础数值。</summary>
        private static SlayerMartEffect SlayerMartMastery(ArcadePowerupBehavior behavior)
            => behavior switch
            {
                ArcadePowerupBehavior.Magnet=>SlayerMartEffect.MagnetMastery,
                ArcadePowerupBehavior.Decoy=>SlayerMartEffect.DecoyMastery,
                ArcadePowerupBehavior.Gloves=>SlayerMartEffect.GlovesMastery,
                ArcadePowerupBehavior.Chicken=>SlayerMartEffect.ChickenMastery,
                ArcadePowerupBehavior.Barrel=>SlayerMartEffect.BarrelMastery,
                ArcadePowerupBehavior.Saw=>SlayerMartEffect.SawMastery,
                _=>0
            };
        /// <summary>从基础半径按当前拥有状态求值；买精通后已有实例立即使用新几何。</summary>
        private static float SlayerMartRadius(OnlineArcadeBattleRuntime battle,long owner,OnlineArcadePowerupRuntime powerup)
            => ArcadeSlayerMartAbilityRules.MasteryRadius(powerup.Radius,SlayerMartMastery(powerup.Behavior),SlayerMartAbility(battle,owner),ServerConfig.Instance.SlayerMart.Parameter);
        /// <summary>伤害只在发射时求值一次，在途弹丸保持已确定的数值。</summary>
        private static int SlayerMartDamage(OnlineArcadeBattleRuntime battle,long owner,int damage,SlayerMartDamageSource source)
            => ArcadeSlayerMartAbilityRules.ResolveDamage(damage,source,SlayerMartAbility(battle,owner),ServerConfig.Instance.SlayerMart.Parameter);
        /// <summary>购物、加载与轮前候选冻结战斗能力；普通宝藏房继续原活动计时。</summary>
        private static bool SlayerMartClockActive(OnlineArcadeBattleRuntime battle)
            => !battle.RoundTransitionPending&&!battle.ArenaCompletionReady&&!battle.AllPlayersDefeated&&
                RoundPlayersReady(battle)&&!battle.Players.Values.Any(p=>p.RespawnSeconds>0)&&
                !(battle.SlayerMart?.Ready==true&&!battle.SlayerMart.Closing);
        /// <summary>权威输入仅在正式可控阶段消耗满充，不改炸弹和冲刺库存。</summary>
        private static bool UseSlayerMartUpgrade(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime battle,OnlineArcadeBattlePlayerRuntime player)
        {
            var state=SlayerMartAbility(battle,player.PlayerId);
            bool allowed=SlayerMartClockActive(battle)&&actor.DisconnectedAtByPlayer.Count==0&&player.Alive&&!player.InTreasureRoom&&
                !battle.AwaitingExit&&player.RespawnSeconds<=0&&state?.MenuOpen!=true;
            return ArcadeSlayerMartAbilityRules.TryActivate(state,allowed,ServerConfig.Instance.SlayerMart.Parameter);
        }
        /// <summary>每个归属独立结算完整周期；真实伤害共用护甲/特殊敌人入口，风暴击杀不自充。</summary>
        private static bool TickSlayerMartStorm(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime battle,float seconds)
        {
            if(battle.SlayerMartRulesVersion!=1||battle.SlayerMart==null||!SlayerMartClockActive(battle))return false;
            var config=ServerConfig.Instance.SlayerMart;bool changed=false;
            foreach(var player in battle.Players.Values)
            {
                var state=SlayerMartAbility(battle,player.PlayerId);if(state==null||state.StormRemaining<=0)continue;
                int ticks=ArcadeSlayerMartAbilityRules.AdvanceStorm(state,seconds,true,config.Parameter);changed|=seconds>0;
                if(!player.Alive||player.InTreasureRoom||ticks==0)continue;
                float radius=config.Parameter("storm.radius");
                bool previous=battle.SlayerMartStormDamage;battle.SlayerMartStormDamage=true;
                try
                {
                    for(int tick=0;tick<ticks;tick++)foreach(var enemy in battle.Enemies.Values.ToArray())
                    {
                        if(!EnemyInHitSphere(enemy,player.PositionX,player.PositionZ,radius))continue;
                        enemy.LastDamagePlayerId=player.PlayerId;
                        DamageEnemy(battle,enemy,(int)config.Parameter("storm.damage"));
                        if(enemy.CurrentHealth<=0)actor.KillEnemy(battle,enemy);
                    }
                }
                finally{battle.SlayerMartStormDamage=previous;}
            }
            return changed;
        }
        /// <summary>控制源按最强聚合；离圈、死亡和到期立即解除，不修改原始移速。</summary>
        private static float SlayerMartStormSpeed(OnlineArcadeBattleRuntime battle,OnlineArcadeEnemyRuntime enemy)
        {
            if(battle.SlayerMartRulesVersion!=1||battle.SlayerMart==null)return 1;
            var config=ServerConfig.Instance.SlayerMart;float radius=config.Parameter("storm.radius"),scale=1;
            foreach(var owner in battle.Players.Values)
            {
                var state=SlayerMartAbility(battle,owner.PlayerId);
                bool inside=owner.Alive&&!owner.InTreasureRoom&&state?.StormRemaining>0&&
                    DistanceSquared(owner.PositionX,owner.PositionZ,enemy.PositionX,enemy.PositionZ)<=radius*radius;
                scale=Math.Min(scale,ArcadeSlayerMartAbilityRules.StormSpeedScale(inside,enemy.ContentBossKind>0||!string.IsNullOrEmpty(enemy.BossId),enemy.SpecialKind>0,config.Parameter));
            }
            return scale;
        }
    }
}
