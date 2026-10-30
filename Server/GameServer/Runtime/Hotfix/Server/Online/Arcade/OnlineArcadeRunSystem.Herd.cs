using System;
using System.Linq;
using FirepowerLoop.Arcade;
namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>双人各自用真实地图阻挡比较两侧落点，中心接触优先场内，不改敌人的原直线击飞。</summary>
        private static bool BeginPlayerHerdImpact(OnlineArcadeBattleRuntime b,OnlineArcadeBattlePlayerRuntime p,ArcadeHerdState cow)
        {
            if(p.HerdImpact.Active)return false;
            ArcadeHerdImpactRules.PlayerDirection(cow.DirectionX,cow.DirectionZ,1,out float px,out float pz);
            ArcadeHerdImpactRules.PlayerDirection(cow.DirectionX,cow.DirectionZ,-1,out float nx,out float nz);
            float positiveX=p.PositionX+px*ArcadeHerdImpactRules.Distance,positiveZ=p.PositionZ+pz*ArcadeHerdImpactRules.Distance;
            float negativeX=p.PositionX+nx*ArcadeHerdImpactRules.Distance,negativeZ=p.PositionZ+nz*ArcadeHerdImpactRules.Distance;
            var obstacles=EffectiveObstacles(b);var island=IslandFor(b);float radius=ServerConfig.Instance.ArcadeRule.PlayerRadius;
            ResolveBattlePosition(b,ref positiveX,ref positiveZ,p.PositionX,p.PositionZ,radius,obstacles,island);
            ResolveBattlePosition(b,ref negativeX,ref negativeZ,p.PositionX,p.PositionZ,radius,obstacles,island);
            float positiveClearance=Math.Abs((positiveX-cow.X)*-cow.DirectionZ+(positiveZ-cow.Z)*cow.DirectionX);
            float negativeClearance=Math.Abs((negativeX-cow.X)*-cow.DirectionZ+(negativeZ-cow.Z)*cow.DirectionX);
            int side=ArcadeHerdImpactRules.PlayerSide(positiveClearance,negativeClearance,p.PositionX*cow.DirectionZ-p.PositionZ*cow.DirectionX);
            return ArcadeHerdImpactRules.Begin(ref p.HerdImpact,side>0?px:nx,side>0?pz:nz);
        }
        /// <summary>服务端只在活动步推进飞行；位移仍经过原地图阻挡，落地禁用由同一状态控制。</summary>
        private static void TickHerdImpacts(OnlineArcadeBattleRuntime b,float seconds)
        {
            foreach(var p in b.Players.Values)
            {
                if(!p.Alive||p.InTreasureRoom){p.HerdImpact=default;continue;}
                float travel=ArcadeHerdImpactRules.Step(ref p.HerdImpact,seconds);
                if(travel<=0)continue;
                float x=p.PositionX+p.HerdImpact.DirectionX*travel,z=p.PositionZ+p.HerdImpact.DirectionZ*travel;
                ResolveBattlePosition(b,ref x,ref z,p.PositionX,p.PositionZ,ServerConfig.Instance.ArcadeRule.PlayerRadius,EffectiveObstacles(b),IslandFor(b));
                p.PositionX=x;p.PositionZ=z;
            }
            foreach(var e in b.Enemies.Values)
            {
                float travel=ArcadeHerdImpactRules.Step(ref e.HerdImpact,seconds);
                if(travel<=0||e.DockSeaEntry.Active||e.OutsideEntry.Active)continue;
                float x=e.PositionX+e.HerdImpact.DirectionX*travel,z=e.PositionZ+e.HerdImpact.DirectionZ*travel;
                ResolveBattlePosition(b,ref x,ref z,e.PositionX,e.PositionZ,ServerConfig.Instance.ArcadeRule.EnemyContactRadius,EffectiveObstacles(b),IslandFor(b));
                e.PositionX=x;e.PositionZ=z;
            }
        }
        /// <summary>全员当前地图准备成功后才推进；同一头牛对每个玩家及敌人至多接触一次。</summary>
        private static void TickHerd(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime b,float seconds)
        {
            var stage=EarlyStage(b);if(stage==null||stage.HerdCount<=0)return;
            TickHerdImpacts(b,seconds);
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
                    {
                        int before=p.CurrentHealth;
                        float contactX=p.PositionX,contactZ=p.PositionZ;
                        actor.ContentDamagePlayer(b,p,ArcadeRoundContentRules.Damage(p.MaxHealth,stage.HerdPlayerDamage));
                        if(p.CurrentHealth<before)
                        {p.HerdContactSequence++;p.HerdContactX=contactX;p.HerdContactZ=contactZ;p.HerdContactElapsedSeconds=b.HazardElapsedSeconds;}
                        if(p.Alive&&p.CurrentHealth<before&&BeginPlayerHerdImpact(b,p,cow))
                        {p.Firing=false;p.DashSeconds=0;}
                    }
                if(!RoundContentActive(b))return;
                foreach(var enemy in b.Enemies.Values.ToArray())
                {
                    // 三维牛身扫掠先于接触去重；入场状态本身不排除受伤。
                    if(enemy.DockSeaEntry.Active&&!TryEnemySweep(enemy,sweep.FromX,sweep.FromZ,sweep.ToX,sweep.ToZ,0,sweep.Radius,out _))continue;
                    if(!b.Enemies.ContainsKey(enemy.RuntimeId)||!ArcadeHerdRules.TryContact(cow,sweep,b.RoundGeneration,enemy.RuntimeId,enemy.PositionX,enemy.PositionZ,ServerConfig.Instance.ArcadeRule.EnemyContactRadius,enemy.CurrentHealth>0,enemy.DockSeaEntry.Active))continue;
                    DamageEnemy(b,enemy,ArcadeRoundContentRules.Damage(enemy.MaxHealth,stage.HerdEnemyDamage));
                    if(enemy.CurrentHealth<=0)actor.KillEnemyWithReaction(b,enemy,true,false,enemy.PositionX-cow.DirectionX,enemy.PositionZ-cow.DirectionZ,ArcadeDeathImpactRules.Herd);
                    else if(ArcadeHerdImpactRules.Begin(ref enemy.HerdImpact,cow.DirectionX,cow.DirectionZ))CancelMeleeWindup(enemy,true);
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
