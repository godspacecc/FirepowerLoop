using System;
using System.Linq;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>没有未占用且远离玩家的墓位时保留现有队列，下一活动帧重试，不额外发怪。</summary>
        private static bool NextStageSpawnAvailable(OnlineArcadeBattleRuntime b)
        {
            if(b.RoundLayout==null||b.SlayerChallenge!=null||b.RoundBossEncounter||b.RoundLayout.SpecialKind!=ArcadeNextStageEnemyRules.GraveKind)return true;
            float ratio=b.RoundLayout.SpecialRatio;
            if((b.SpawnedInWave+1)*ratio<Math.Floor(b.SpawnedInWave*ratio)+1)return true;
            return TryNextGrave(b,out _,out _);
        }

        /// <summary>墓土只作表现起点；真实碰撞体从墓沿外的安全落脚点开始，现身前禁止追击。</summary>
        private static void InitializeNextStageEnemy(OnlineArcadeBattleRuntime b,OnlineArcadeEnemyRuntime e)
        {
            if(e.SpecialKind==ArcadeNextStageEnemyRules.GraveKind)
            {
                if(!TryNextGrave(b,out float x,out float z))throw new InvalidOperationException("墓位在出生前失效");
                e.StageEnemy=new ArcadeStageEnemyState{Phase=1,OriginX=x,OriginY=z-ArcadeNextStageEnemyRules.GraveOriginOffset};
                e.PositionX=x;e.PositionZ=z-ArcadeNextStageEnemyRules.GraveLandingOffset;
            }
            else if(e.SpecialKind==ArcadeNextStageEnemyRules.ShadowKind)
                e.StageEnemy=new ArcadeStageEnemyState{Phase=2,RevivesRemaining=1,OriginX=e.PositionX,OriginY=e.PositionZ};
            e.HazardPreviousX=e.PositionX;e.HazardPreviousZ=e.PositionZ;
        }

        /// <summary>优先选择离存活玩家更远的墓位；起身期间独占墓位，落脚点也不能与其他敌人重叠。</summary>
        private static bool TryNextGrave(OnlineArcadeBattleRuntime b,out float x,out float z)
        {
            x=z=0;float best=-1;
            for(int i=0;i<ArcadeNextStageEnemyRules.TombCount;i++)
            {
                ArcadeNextStageEnemyRules.GraveTomb(i,out float tx,out float tz);
                if(b.Enemies.Values.Any(e=>e.StageEnemy.Phase==1&&DistanceSquared(tx,tz-ArcadeNextStageEnemyRules.GraveOriginOffset,e.StageEnemy.OriginX,e.StageEnemy.OriginY)<.01f))continue;
                if(!NextStageLandingSafe(b,tx,tz-ArcadeNextStageEnemyRules.GraveLandingOffset,0))continue;
                float distance=b.Players.Values.Where(p=>p.Alive&&!p.InTreasureRoom).Select(p=>Math.Min(DistanceSquared(tx,tz-ArcadeNextStageEnemyRules.GraveOriginOffset,p.PositionX,p.PositionZ),DistanceSquared(tx,tz-ArcadeNextStageEnemyRules.GraveLandingOffset,p.PositionX,p.PositionZ))).DefaultIfEmpty(float.MaxValue).Min();
                if(distance<ContentTuning(b).EventRadius*ContentTuning(b).EventRadius||distance<=best)continue;
                best=distance;x=tx;z=tz;
            }
            return best>=0;
        }

        /// <summary>阶段年龄只随真实活动战斗推进；完成现身的这一帧仍不移动、不攻击。</summary>
        private static bool TickNextStageEnemy(OnlineArcadeBattleRuntime b,OnlineArcadeEnemyRuntime e,float seconds)
        {
            if(e.StageEnemy.Phase==0)return false;
            CancelMeleeWindup(e,true);
            if(RoundContentActive(b)&&RoundPlayersReady(b))ArcadeNextStageEnemyRules.Advance(ref e.StageEnemy,seconds,ContentTuning(b));
            return true;
        }

        /// <summary>测试清场与终局剩余敌人清理不触发重生，也不生成玩家击杀奖励。</summary>
        private static void RemoveEnemyForCleanup(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime b,OnlineArcadeEnemyRuntime e)
            =>actor.ResolveEnemyDeath(b,e,true,false,e.PositionX,e.PositionZ,0,true);

        /// <summary>首次击倒只消费一次换位，不进入死亡事实、计分或掉落；清理调用始终直接删除。</summary>
        private static bool PreserveNextStageEnemy(OnlineArcadeBattleRuntime b,OnlineArcadeEnemyRuntime e,bool forceRemove)
        {
            if(forceRemove)return false;
            if(e.StageEnemy.Phase!=0){e.CurrentHealth=e.MaxHealth;return true;}
            if(e.SpecialKind!=ArcadeNextStageEnemyRules.ShadowKind||e.StageEnemy.RevivesRemaining<=0)return false;
            float oldX=e.PositionX,oldZ=e.PositionZ;
            if(!TryShadowLanding(b,e,out float x,out float z))return true;
            e.StageEnemy=new ArcadeStageEnemyState{Phase=3,RevivesRemaining=0,OriginX=oldX,OriginY=oldZ};
            e.PositionX=x;e.PositionZ=z;e.HazardPreviousX=x;e.HazardPreviousZ=z;e.CurrentHealth=e.MaxHealth;
            e.FlameHitRemainingSeconds=0;e.LastDamageWasFlame=false;CancelMeleeWindup(e,true);
            return true;
        }

        /// <summary>蓝影从击倒处移至可行走的空位，避开所有存活玩家及现有敌人，不把安全点推进墙内。</summary>
        private static bool TryShadowLanding(OnlineArcadeBattleRuntime b,OnlineArcadeEnemyRuntime e,out float x,out float z)
        {
            for(int ring=1;ring<=12;ring++)for(int angle=0;angle<24;angle++)
            {
                float radians=(angle+(e.RuntimeId%24))*MathF.PI/12;
                float tx=e.PositionX+MathF.Cos(radians)*ring*1.5f,tz=e.PositionZ+MathF.Sin(radians)*ring*1.5f;
                if(NextStageLandingSafe(b,tx,tz,e.RuntimeId)){x=tx;z=tz;return true;}
            }
            // 圆周被墙截断时扫描场内网格，保持相同距离与障碍裁决。
            for(float tx=-ArenaHalfWidth(b)+2;tx<ArenaHalfWidth(b)-2;tx+=1.5f)
                for(float tz=-ArenaHalfHeight(b)+2;tz<ArenaHalfHeight(b)-2;tz+=1.5f)
                    if(NextStageLandingSafe(b,tx,tz,e.RuntimeId)){x=tx;z=tz;return true;}
            x=e.PositionX;z=e.PositionZ;return false;
        }

        /// <summary>落脚点须同时满足场内边界、完整可见障碍、玩家避让及敌人间距。</summary>
        private static bool NextStageLandingSafe(OnlineArcadeBattleRuntime b,float x,float z,long except)
        {
            float radius=ServerConfig.Instance.ArcadeRule.EnemyContactRadius,distance=ContentTuning(b).EventRadius;
            return Math.Abs(x)<ArenaHalfWidth(b)-radius&&Math.Abs(z)<ArenaHalfHeight(b)-radius&&
                !InsideObstacle(x,z,radius,EffectiveObstacles(b))&&
                b.Players.Values.All(p=>!p.Alive||p.InTreasureRoom||DistanceSquared(x,z,p.PositionX,p.PositionZ)>=distance*distance)&&
                b.Enemies.Values.All(e=>e.RuntimeId==except||DistanceSquared(x,z,e.PositionX,e.PositionZ)>radius*radius*4);
        }
    }
}
