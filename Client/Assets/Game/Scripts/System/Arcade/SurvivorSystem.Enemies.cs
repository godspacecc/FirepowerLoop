using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <summary>同体型共用距离场，较胖敌人不能沿普通体型的窄缝穿墙。</summary>
        private readonly Dictionary<float,SurvivorNavigation> sizedNavigation=new();
        /// <summary>仅查询本局只读敌池中的已采用比例。</summary>
        private SurvivorEnemyDefinition Shape(string configId)=>enemyRoster?.Entries.FirstOrDefault(x=>x.EnemyId==configId);
        /// <summary>圆形命中及行走半径保守覆盖水平较宽的一轴。</summary>
        private float EnemyRadius(ArcadeEnemyRuntime enemy)=>catalog.Rule.EnemyContactRadius*(Shape(enemy.ConfigId)?.BodyRadiusMultiplier??1);
        /// <summary>抓挠范围随水平身体比例同步，保持手臂表现与规则相配。</summary>
        private float MeleeReach(ArcadeEnemyRuntime enemy,EnemyConfigDefinition definition)=>definition.AttackRange*(Shape(enemy.ConfigId)?.BodyRadiusMultiplier??1);
        /// <summary>入场时预建不同占地的静态通路，避免后期首次混入大体型时卡一帧。</summary>
        private void PrepareSizedNavigation()
        {
            sizedNavigation.Clear();
            foreach(var shape in enemyRoster.Entries)
            {
                float radius=catalog.Rule.EnemyContactRadius*shape.BodyRadiusMultiplier;
                if(!Mathf.Approximately(radius,catalog.Rule.EnemyContactRadius)&&!sizedNavigation.ContainsKey(radius))
                    sizedNavigation.Add(radius,new SurvivorNavigation(HalfWidth,HalfHeight,radius,obstacles));
            }
        }
        /// <summary>相同占地复用距离场；测试或晚到实体可按需补齐，但不逐敌人重算。</summary>
        private SurvivorNavigation NavigationFor(ArcadeEnemyRuntime enemy)
        {
            float radius=EnemyRadius(enemy);
            if(Mathf.Approximately(radius,catalog.Rule.EnemyContactRadius))return navigation;
            if(!sizedNavigation.TryGetValue(radius,out var field))
            {field=new SurvivorNavigation(HalfWidth,HalfHeight,radius,obstacles);field.SetTarget(player);sizedNavigation.Add(radius,field);}
            return field;
        }
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
            enemyRoster=roster;enemyConfigs.Clear();sizedNavigation.Clear();
            foreach(var pair in configs)enemyConfigs.Add(pair.Key,pair.Value);
            PrepareGroundHitPoses();
            return true;
        }

        /// <summary>爬出阶段锁定路径；站稳后追到抓挠距离，前摇与收招时停步。</summary>
        private void TickEnemies(float dt)
        {
            // 每种体型每帧只更新一次目标格，不随敌人数量重复找格或重算。
            foreach(var field in sizedNavigation.Values)field.SetTarget(player);
            foreach(var e in enemies.Values)
            {
                if(SurvivorGroundEntryRules.Advance(ref e.GroundEntry,dt))
                {e.Position=SurvivorGroundEntryRules.Position(e.GroundEntry);continue;}
                bool configured=enemyConfigs.TryGetValue(e.ConfigId??string.Empty,out var definition)&&e.ConfigId!=config.EnemyId;
                float distance=Vector2.Distance(e.Position,player);
                if(!configured)e.Position=navigation.Move(e.Position,player,e.Speed*dt);
                else if(!e.MeleeWindupActive&&e.MeleeRecoveryRemaining<=0)
                {
                    float approach=FirepowerLoop.Arcade.ArcadeMeleeRules.ApproachSeconds(distance,MeleeReach(e,definition),e.Speed,dt,e.MeleeRecoveryRemaining);
                    e.Position=NavigationFor(e).Move(e.Position,player,e.Speed*approach);
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
            if(enemy.OutsideEntry.Active||Vector2.Distance(enemy.Position,player)>MeleeReach(enemy,definition)+.0001f)
            {enemy.MeleeWindupActive=false;enemy.MeleeWindupRemaining=0;return;}
            if(enemy.MeleeRecoveryRemaining>0||enemy.AttackCooldown>0)return;
            if(!enemy.MeleeWindupActive)
            {enemy.MeleeWindupActive=true;enemy.MeleeWindupSeconds=definition.TelegraphSeconds;enemy.MeleeWindupRemaining=definition.TelegraphSeconds;return;}
            enemy.MeleeWindupRemaining=Math.Max(0,enemy.MeleeWindupRemaining-dt);
            if(enemy.MeleeWindupRemaining>0)return;
            enemy.MeleeWindupActive=false;enemy.MeleeRecoveryRemaining=FirepowerLoop.Arcade.ArcadeMeleeRules.RecoverySeconds;
            enemy.AttackCooldown=Shape(enemy.ConfigId)?.AttackCooldownSeconds??definition.AttackCooldownSeconds;enemy.AttackSequence++;
            if(hurtClock>0)return;
            Model.Health=Math.Max(0,Model.Health-enemy.Damage);hurtClock=catalog.Rule.ContactInvulnerabilitySeconds;
        }

    }
}
