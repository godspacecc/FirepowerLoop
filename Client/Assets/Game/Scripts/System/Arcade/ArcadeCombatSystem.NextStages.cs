using System;
using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>先检查空墓位再消费通道调度，等待期间不丢失敌人预算。</summary>
        private bool NextStageSpawnReady(ArcadeCombatRuntime r)
        {
            var layout=RoundLayout(r);
            if(layout?.SpecialKind!=ArcadeNextStageEnemyRules.GraveKind)return true;
            int sequence=r.ContentSpawnSequence;
            return Math.Floor((sequence+1)*layout.SpecialRatio)<=Math.Floor(sequence*layout.SpecialRatio)||TryFindGrave(r,null,out _);
        }

        /// <summary>仅选择当前空闲且远离玩家的固定墓穴，不把出生点放在墙体或玩家脚下。</summary>
        private bool TryFindGrave(ArcadeCombatRuntime r,ArcadeEnemyRuntime e,out Vector2 tomb)
        {
            int start=r.ContentSpawnSequence%ArcadeNextStageEnemyRules.TombCount;
            for(int n=0;n<ArcadeNextStageEnemyRules.TombCount;n++)
            {
                int i=(start+n)%ArcadeNextStageEnemyRules.TombCount;
                ArcadeNextStageEnemyRules.GraveTomb(i,out float x,out float z);
                bool occupied=false;
                foreach(var other in r.Enemies.Values)
                    if(other!=e&&other.StageEnemy.Phase==1&&Math.Abs(other.StageEnemy.OriginX-x)<.01f&&
                        Math.Abs(other.StageEnemy.OriginY-(z-ArcadeNextStageEnemyRules.GraveOriginOffset))<.01f){occupied=true;break;}
                if(occupied)continue;
                var landing=new Vector2(x,z-ArcadeNextStageEnemyRules.GraveLandingOffset);
                if(!RoundLayout(r).IsWalkable(landing.x,landing.y,catalog.Rule.EnemyContactRadius,ArenaHalfWidth,ArenaHalfHeight)||
                    Vector2.Distance(r.PlayerPosition,new Vector2(x,z-ArcadeNextStageEnemyRules.GraveOriginOffset))<ContentTuning(r).EventRadius||
                    !NextStageLandingSafe(r,landing,e))continue;
                tomb=new Vector2(x,z);return true;
            }
            tomb=default;return false;
        }

        /// <summary>墓穴替换原预算中的部分普通敌人，其他敌人仍沿本轮通道入场。</summary>
        private void InitializeNextStageEnemy(ArcadeCombatRuntime r,ArcadeEnemyRuntime e)
        {
            if(e.ContentSpecialKind==ArcadeNextStageEnemyRules.GraveKind&&TryFindGrave(r,e,out var tomb))
            {
                e.Position=tomb-Vector2.up*ArcadeNextStageEnemyRules.GraveLandingOffset;
                e.StageEnemy=new ArcadeStageEnemyState{Phase=1,OriginX=tomb.x,OriginY=tomb.y-ArcadeNextStageEnemyRules.GraveOriginOffset};
            }
            else if(e.ContentSpecialKind==ArcadeNextStageEnemyRules.ShadowKind)
                e.StageEnemy=new ArcadeStageEnemyState{Phase=2,RevivesRemaining=1,OriginX=e.Position.x,OriginY=e.Position.y};
        }

        /// <summary>首次打倒只消费一次换位，不产生死亡、分数、掉落或清场。</summary>
        private bool KeepNextStageEnemyAlive(ArcadeCombatRuntime r,ArcadeEnemyRuntime e)
        {
            if(e.StageEnemy.Phase!=0){e.Health=e.MaxHealth;return true;}
            if(e.ContentSpecialKind!=ArcadeNextStageEnemyRules.ShadowKind||e.StageEnemy.RevivesRemaining<=0)return false;
            var origin=e.Position;
            if(!TryShadowLanding(r,e,out var best)){e.Health=e.MaxHealth;return true;}
            e.Position=best;e.Health=e.MaxHealth;e.AttackCooldown=0;
            e.ContentStunUntil=0;e.FlameHitRemainingSeconds=0;e.LastDamageWasFlame=false;
            e.HerdImpact=default;CancelMeleeWindup(e);
            e.StageEnemy=new ArcadeStageEnemyState{Phase=3,RevivesRemaining=0,OriginX=origin.x,OriginY=origin.y};
            return true;
        }

        /// <summary>蓝影优先在附近可走空位落地，靠墙时扫描场内，保持与联机相同的安全约束。</summary>
        private bool TryShadowLanding(ArcadeCombatRuntime r,ArcadeEnemyRuntime e,out Vector2 landing)
        {
            for(int ring=1;ring<=12;ring++)for(int angle=0;angle<24;angle++)
            {
                float radians=(angle+e.Id%24)*Mathf.PI/12;
                var point=e.Position+new Vector2(Mathf.Cos(radians),Mathf.Sin(radians))*ring*1.5f;
                if(NextStageLandingSafe(r,point,e)){landing=point;return true;}
            }
            for(float x=-ArenaHalfWidth+2;x<ArenaHalfWidth-2;x+=1.5f)
                for(float y=-ArenaHalfHeight+2;y<ArenaHalfHeight-2;y+=1.5f)
                    if(NextStageLandingSafe(r,new Vector2(x,y),e)){landing=new Vector2(x,y);return true;}
            landing=e.Position;return false;
        }

        /// <summary>完整实体半径须避开地标、地图边缘、玩家和其他敌人。</summary>
        private bool NextStageLandingSafe(ArcadeCombatRuntime r,Vector2 point,ArcadeEnemyRuntime except)
        {
            float radius=catalog.Rule.EnemyContactRadius;
            if(!RoundLayout(r).IsWalkable(point.x,point.y,radius,ArenaHalfWidth,ArenaHalfHeight)||
                Vector2.Distance(r.PlayerPosition,point)<ContentTuning(r).EventRadius)return false;
            foreach(var other in r.Enemies.Values)
                if(other!=except&&Vector2.Distance(point,other.Position)<=radius*2)return false;
            return true;
        }
    }
}
