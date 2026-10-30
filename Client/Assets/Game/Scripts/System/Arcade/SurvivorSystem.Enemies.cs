using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine;
namespace Game
{
    public sealed partial class SurvivorSystem
    {
        /// <summary>本局敌池、抽取随机流及只读依赖；不修改共用闯关配置。</summary>
        private SurvivorEnemyRoster enemyRoster;
        private System.Random enemyRandom=new System.Random();
        private readonly Dictionary<string,EnemyConfigDefinition> enemyConfigs=new();
        private bool PrepareEnemyRoster(out string error)
        {
            var provider=this.GetUtility<IRuntimeConfigProvider>();
            if(!provider.TryGetSurvivorEnemyRoster(out var roster,out error))return false;
            var configs=new Dictionary<string,EnemyConfigDefinition>();
            foreach(var entry in roster.Entries)
            {
                if(!provider.TryGetEnemy(entry.EnemyId,out var definition,out error))return false;
                configs[entry.EnemyId]=definition;
            }
            enemyRoster=roster;enemyConfigs.Clear();
            foreach(var pair in configs)enemyConfigs.Add(pair.Key,pair.Value);
            return true;
        }

        /// <summary>沿既有画面外通道入场；新增僵尸追到抓挠距离，前摇与收招时停步。</summary>
        private void TickEnemies(float dt)
        {
            foreach(var e in enemies.Values)
            {
                if(FirepowerLoop.Arcade.ArcadeOutsideEntryRules.Advance(ref e.OutsideEntry,dt,e.Speed,out var entryPosition))
                {e.Position=new Vector2(entryPosition.X,entryPosition.Y);continue;}
                bool configured=enemyConfigs.TryGetValue(e.ConfigId??string.Empty,out var definition)&&e.ConfigId!=config.EnemyId;
                float distance=Vector2.Distance(e.Position,player);
                if(!configured)e.Position=navigation.Move(e.Position,player,e.Speed*dt);
                else if(!e.MeleeWindupActive&&e.MeleeRecoveryRemaining<=0)
                {
                    float approach=FirepowerLoop.Arcade.ArcadeMeleeRules.ApproachSeconds(distance,definition.AttackRange,e.Speed,dt,e.MeleeRecoveryRemaining);
                    e.Position=navigation.Move(e.Position,player,e.Speed*approach);
                }
                if(configured)TickMeleeEnemy(e,dt);
                else if(hurtClock<=0&&Vector2.Distance(e.Position,player)<=catalog.Rule.PlayerRadius+catalog.Rule.EnemyContactRadius)
                {Model.Health=Math.Max(0,Model.Health-e.Damage);hurtClock=catalog.Rule.ContactInvulnerabilitySeconds;e.AttackSequence++;}
            }
        }

        /// <summary>新增近战敌人按共用配置起手，离开范围取消；命中后采用既有收招停步。</summary>
        private void TickMeleeEnemy(ArcadeEnemyRuntime enemy,float dt)
        {
            var definition=enemyConfigs[enemy.ConfigId];
            enemy.AttackCooldown=Math.Max(0,enemy.AttackCooldown-dt);
            enemy.MeleeRecoveryRemaining=Math.Max(0,enemy.MeleeRecoveryRemaining-dt);
            // 与闯关相同的边界容差，避免恰好追到攻击距离时因浮点舍入永远取消起手。
            if(enemy.OutsideEntry.Active||Vector2.Distance(enemy.Position,player)>definition.AttackRange+.0001f)
            {enemy.MeleeWindupActive=false;enemy.MeleeWindupRemaining=0;return;}
            if(enemy.MeleeRecoveryRemaining>0||enemy.AttackCooldown>0)return;
            if(!enemy.MeleeWindupActive)
            {enemy.MeleeWindupActive=true;enemy.MeleeWindupSeconds=definition.TelegraphSeconds;enemy.MeleeWindupRemaining=definition.TelegraphSeconds;return;}
            enemy.MeleeWindupRemaining=Math.Max(0,enemy.MeleeWindupRemaining-dt);
            if(enemy.MeleeWindupRemaining>0)return;
            enemy.MeleeWindupActive=false;enemy.MeleeRecoveryRemaining=FirepowerLoop.Arcade.ArcadeMeleeRules.RecoverySeconds;
            enemy.AttackCooldown=definition.AttackCooldownSeconds;enemy.AttackSequence++;
            if(hurtClock>0)return;
            Model.Health=Math.Max(0,Model.Health-enemy.Damage);hurtClock=catalog.Rule.ContactInvulnerabilitySeconds;
        }

    }
}
