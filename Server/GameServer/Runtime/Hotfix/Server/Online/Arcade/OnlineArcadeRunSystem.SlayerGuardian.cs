using System;
using System.Linq;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>收币结束生成唯一独立Guardian；不套用普通墓园Boss或主线奖励。</summary>
        private static void SpawnSlayerGuardian(OnlineArcadeBattleRuntime battle)
        {
            var c=ServerConfig.Instance.SlayerChallenge;var definition=ServerConfig.Instance.Tables.TbEnemy.Get(c.BossEnemyId);
            long id=battle.NextEnemyRuntimeId++;int health=checked(c.BossHealthPerPlayer*battle.SlayerChallenge.Members.Count);
            battle.SlayerGuardian=new ArcadeSlayerGuardianState();battle.SlayerGuardianEnemyId=id;
            battle.Enemies.Add(id,new OnlineArcadeEnemyRuntime{RuntimeId=id,EnemyConfigId=c.BossEnemyId,ContentBossKind=1,
                CurrentHealth=health,MaxHealth=health,Damage=c.BossContactDamage,MoveSpeed=c.BossMoveSpeed,
                PositionX=0,PositionZ=0,AttackKind=definition.AttackKind,AttackRange=definition.AttackRange,
                AttackIntervalSeconds=definition.AttackCooldownSeconds});
        }
        /// <summary>同源三技能通过既有伤害入口执行，保留无敌、冲刺、护盾及第四次失败语义。</summary>
        private static bool TickSlayerGuardian(OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime battle,
            OnlineArcadeEnemyRuntime enemy,OnlineArcadeBattlePlayerRuntime target,float seconds)
        {
            if(battle.SlayerGuardian==null||enemy.RuntimeId!=battle.SlayerGuardianEnemyId)return false;
            var c=ServerConfig.Instance.SlayerChallenge;var rule=ServerConfig.Instance.ArcadeRule;
            var targets=battle.Players.Values.Where(p=>p.Alive).OrderBy(p=>p.PlayerId).Select(p=>new ArcadeSlayerGuardianTarget(p.PlayerId,p.PositionX,p.PositionZ)).ToArray();
            float Obstacle(float ax,float ay,float bx,float by)
            {
                float nearest=1;foreach(var obstacle in EffectiveObstacles(battle))
                    if(TryGetSegmentObstacleEntry(ax,ay,bx,by,rule.PlayerRadius,obstacle,out float entry))nearest=Math.Min(nearest,entry);
                return nearest;
            }
            var hits=ArcadeSlayerGuardianCombat.Tick(battle.SlayerGuardian,c,seconds,enemy.PositionX,enemy.PositionZ,
                target.PositionX,target.PositionZ,targets,rule.PlayerRadius,Obstacle);
            foreach(var hit in hits)
                if(battle.Players.TryGetValue(hit.Id,out var player))actor.DamagePlayerCore(battle,player,hit.Damage,
                    hit.Lethal?SlayerMartHitKind.LethalMechanism:SlayerMartHitKind.Ordinary);
            battle.ContentAreas.Clear();battle.ContentAreas.AddRange(battle.SlayerGuardian.Snapshots);
            enemy.ContentWarning=battle.SlayerGuardian.Snapshots.Any(p=>p.Warning);
            enemy.AttackCooldownSeconds=Math.Max(0,enemy.AttackCooldownSeconds-seconds);
            if(!enemy.ContentWarning&&target.Alive&&battle.SlayerChallenge.Phase==ArcadeSlayerChallengePhase.Boss)
                {
                MoveEnemy(enemy,target.PositionX,target.PositionZ,seconds,rule,EffectiveObstacles(battle),null);
                float radius=rule.PlayerRadius+rule.EnemyContactRadius;
                if(enemy.AttackCooldownSeconds<=0&&DistanceSquared(enemy.PositionX,enemy.PositionZ,target.PositionX,target.PositionZ)<=radius*radius)
                {actor.DamagePlayer(battle,target,c.BossContactDamage);enemy.AttackCooldownSeconds=enemy.AttackIntervalSeconds;}
            }
            return true;
        }
        /// <summary>胜利仅开启购物，不开放普通场出口或直接提交挑战返回。</summary>
        private static bool SlayerGuardianDefeated(OnlineArcadeBattleRuntime battle,OnlineArcadeEnemyRuntime enemy)
        {
            if(battle.SlayerGuardian==null||enemy.RuntimeId!=battle.SlayerGuardianEnemyId)return false;
            battle.SlayerChallenge=ArcadeSlayerChallengeRules.BossDefeated(battle.SlayerChallenge,ServerConfig.Instance.SlayerChallenge);
            battle.ChallengeSeconds=(float)battle.SlayerChallenge.RemainingSeconds;
            battle.SlayerGuardian=null;battle.ContentAreas.Clear();battle.Projectiles.Clear();battle.PurpleRayArcs.Clear();battle.AwaitingExit=false;BeginSlayerChallengeShop(battle);return true;
        }
    }
}
