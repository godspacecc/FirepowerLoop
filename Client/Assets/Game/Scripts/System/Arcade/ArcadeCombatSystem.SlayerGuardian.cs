using System;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>独立Guardian只用正式敌人外观与专用参数，不继承主线Boss行为。</summary>
        private void SpawnSlayerGuardian(ArcadeCombatRuntime runtime)
        {
            var c=slayerChallengeConfig;
            if(!this.GetUtility<IRuntimeConfigProvider>().TryGetEnemy(c.BossEnemyId,out var definition,out var error))throw new InvalidOperationException(error);
            var enemy=runtimePool.RentEnemy();enemy.Id=runtime.NextEntityId++;enemy.ConfigId=c.BossEnemyId;
            enemy.MaxHealth=enemy.Health=checked(c.BossHealthPerPlayer*runtime.SlayerChallenge.Members.Count);
            enemy.Damage=c.BossContactDamage;enemy.Speed=c.BossMoveSpeed;enemy.Position=Vector2.zero;
            enemy.AttackKind=definition.AttackKind;enemy.AttackInterval=definition.AttackCooldownSeconds;enemy.ContentBossKind=1;enemy.IsBossPresentation=true;
            runtime.SlayerGuardian=new ArcadeSlayerGuardianState();runtime.SlayerGuardianEnemyId=enemy.Id;runtime.ContentBossEnemyId=enemy.Id;
            runtime.Enemies.Add(enemy.Id,enemy);
        }
        /// <summary>三技能同服务端共用规则，所有伤害交回现有保护与倒地入口。</summary>
        private bool TickSlayerGuardian(ArcadeCombatRuntime runtime,ArcadeEnemyRuntime enemy,float seconds)
        {
            if(runtime.SlayerGuardian==null||enemy.Id!=runtime.SlayerGuardianEnemyId)return false;
            var targets=runtime.PlayerHealth>0?new[]{new ArcadeSlayerGuardianTarget(1,runtime.PlayerPosition.x,runtime.PlayerPosition.y)}:Array.Empty<ArcadeSlayerGuardianTarget>();
            float Obstacle(float ax,float ay,float bx,float by)
            {
                float fraction=1;foreach(var obstacle in ObstaclesFor(runtime))
                    if(TryGetSegmentObstacleEntry(new Vector2(ax,ay),new Vector2(bx,by),catalog.Rule.PlayerRadius,obstacle,out float entry))fraction=Math.Min(fraction,entry);
                return fraction;
            }
            var hits=ArcadeSlayerGuardianCombat.Tick(runtime.SlayerGuardian,slayerChallengeConfig,seconds,enemy.Position.x,enemy.Position.y,
                runtime.PlayerPosition.x,runtime.PlayerPosition.y,targets,catalog.Rule.PlayerRadius,Obstacle);
            foreach(var hit in hits)DamagePlayerCore(runtime,hit.Damage,hit.Lethal?SlayerMartHitKind.LethalMechanism:SlayerMartHitKind.Ordinary);
            runtime.ContentAreaSnapshots.Clear();runtime.ContentAreaSnapshots.AddRange(runtime.SlayerGuardian.Snapshots);
            enemy.ContentWarning=runtime.SlayerGuardian.Snapshots.Any(p=>p.Warning);
            if(!enemy.ContentWarning&&runtime.PlayerHealth>0&&runtime.SlayerChallenge.Phase==ArcadeSlayerChallengePhase.Boss)
                {
                MoveEnemyTowards(runtime,enemy,runtime.PlayerPosition,seconds);
                float radius=catalog.Rule.PlayerRadius+catalog.Rule.EnemyContactRadius;
                if(enemy.AttackCooldown<=0&&(enemy.Position-runtime.PlayerPosition).sqrMagnitude<=radius*radius)
                {DamagePlayer(runtime,slayerChallengeConfig.BossContactDamage);enemy.AttackCooldown=enemy.AttackInterval;}
            }
            return true;
        }
        /// <summary>实际Boss死亡只开启独立购物阶段，不开放主线出口。</summary>
        private bool SlayerGuardianDefeated(ArcadeCombatRuntime runtime,ArcadeEnemyRuntime enemy)
        {
            if(runtime.SlayerGuardian==null||enemy.Id!=runtime.SlayerGuardianEnemyId)return false;
            runtime.SlayerChallenge=ArcadeSlayerChallengeRules.BossDefeated(runtime.SlayerChallenge,slayerChallengeConfig);
            runtime.ChallengeSeconds=(float)runtime.SlayerChallenge.RemainingSeconds;runtime.SlayerGuardian=null;
            runtime.ContentAreas.Clear();runtime.ContentAreaSnapshots.Clear();
            foreach(int id in runtime.Projectiles.Keys.ToArray())RemoveProjectile(runtime,id);
            view?.RemoveEnemy(enemy.Id,true);runtimePool.Return(enemy);runtime.AwaitingExit=false;BeginSlayerChallengeShop(runtime);return true;
        }
    }
}
