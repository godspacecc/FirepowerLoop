using System;
using System.Linq;
using FirepowerLoop.Arcade;
namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>全员当前地图准备成功后才推进；同一头牛对每个玩家及敌人至多接触一次。</summary>
        private static void TickHerd(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime b,float seconds)
        {
            var stage=EarlyStage(b);if(stage==null||stage.HerdCount<=0)return;
            if(!b.HerdInitialized){b.HerdInitialized=true;b.HerdUntilNext=stage.HerdFirstDelay;}
            b.HerdUntilNext-=seconds;
            if(b.HerdUntilNext<=0)
            {b.HerdUntilNext=stage.HerdPeriod;ArcadeHerdRules.StartBatch(b.HerdCows,stage,b.RoundGeneration,++b.HerdBatch);return;}
            // 一次推进完整单排，再处理可能进入复活等待的伤害事务。
            var steps=b.HerdCows.Select(c=>(Cow:c,Sweep:ArcadeHerdRules.Step(c,stage.Herd,seconds,true))).ToArray();
            foreach(var step in steps)
            {
                var cow=step.Cow;var sweep=step.Sweep;
                foreach(var p in b.Players.Values.OrderBy(p=>p.PlayerId))
                    if(ArcadeHerdRules.TryContact(cow,sweep,b.RoundGeneration,-p.PlayerId,p.PositionX,p.PositionZ,ServerConfig.Instance.ArcadeRule.PlayerRadius,p.Alive&&!p.InTreasureRoom))
                        actor.ContentDamagePlayer(b,p,ArcadeRoundContentRules.Damage(p.MaxHealth,stage.HerdPlayerDamage));
                if(!RoundContentActive(b))return;
                foreach(var enemy in b.Enemies.Values.ToArray())
                {
                    if(!b.Enemies.ContainsKey(enemy.RuntimeId)||!ArcadeHerdRules.TryContact(cow,sweep,b.RoundGeneration,enemy.RuntimeId,enemy.PositionX,enemy.PositionZ,ServerConfig.Instance.ArcadeRule.EnemyContactRadius,enemy.CurrentHealth>0))continue;
                    DamageEnemy(b,enemy,ArcadeRoundContentRules.Damage(enemy.MaxHealth,stage.HerdEnemyDamage));
                    if(enemy.CurrentHealth<=0)actor.KillEnemyWithImpact(b,enemy,true,false,cow.X,cow.Z);
                    if(!RoundContentActive(b))return;
                }
                if(cow.Phase==ArcadeHerdPhase.Expired){ArcadeHerdRules.Cancel(cow);b.HerdCows.Remove(cow);}
            }
        }
        /// <summary>快照和缓存分别拥有消息副本，不能共享对象池中的可变消息。</summary>
        private static void AppendHerdSnapshot(OnlineArcadeBattleRuntime source,OnlineArcadeBattleInfo target)
        {
            var stage=EarlyStage(source);if(stage==null||stage.HerdCount<=0)return;
            foreach(var cow in source.HerdCows)
            {
                var pose=ArcadeHerdRules.Snapshot(cow,stage.Herd);var item=OnlineArcadeHerdInfo.Create();
                item.MapInstanceId=pose.MapInstanceId;item.Id=pose.Id;item.Phase=(int)pose.Phase;item.X=pose.X;item.Z=pose.Z;
                item.DirectionX=pose.DirectionX;item.DirectionZ=pose.DirectionZ;item.PhaseSeconds=pose.PhaseSeconds;target.Herds.Add(item);
            }
        }
        private static void CloneHerdSnapshot(OnlineArcadeBattleInfo source,OnlineArcadeBattleInfo target)
        {
            foreach(var pose in source.Herds)
            {var item=OnlineArcadeHerdInfo.Create();item.MapInstanceId=pose.MapInstanceId;item.Id=pose.Id;item.Phase=pose.Phase;item.X=pose.X;item.Z=pose.Z;item.DirectionX=pose.DirectionX;item.DirectionZ=pose.DirectionZ;item.PhaseSeconds=pose.PhaseSeconds;target.Herds.Add(item);}
        }
    }
}
