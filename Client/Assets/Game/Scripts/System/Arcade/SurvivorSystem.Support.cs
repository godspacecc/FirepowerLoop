using System.Collections.Generic;
using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    public sealed partial class SurvivorSystem
    {
        /// <summary>落点预警和垂直下降共用领域时钟，暂停时表现不自行推进。</summary>
        public const float MissileWarningSeconds=.75f, MissileDescentSeconds=.9f;
        private const int MissileSampleBudget=12;
        private readonly List<int> missileTargets=new List<int>(), missileNeighbors=new List<int>();
        private float missileRetry, missileAge;
        private int missileSearchOffset;
        /// <summary>锁定后冻结落点及等级，玩家移动、目标离屏或升级都不改变该次空袭。</summary>
        private Vector2 missileImpact;
        private SurvivorWeaponLevelDefinition armedMissile;

        /// <summary>只在活动战斗帧搜索敌群，成功锁定才进入独立冷却。</summary>
        private void TickSupport(float dt)
        {
            Model.RocketClock=Mathf.Max(0,Model.RocketClock-dt);
            missileRetry-=dt;
            if(armedMissile!=null)
            {
                missileAge+=dt;
                if(missileAge>=MissileWarningSeconds+MissileDescentSeconds)ExplodeMissile();
                else view.SetMissileStrike(missileImpact,armedMissile.AreaRadius,missileAge,true);
            }
            int level=Model.Loadout?.LevelOf("RocketSupport")??0;
            if(level==0 || armedMissile!=null || Model.RocketClock>0 || missileRetry>0)return;
            missileRetry=.2f;
            var definition=config.WeaponLevel("RocketSupport",level);
            if(!TryLockMissile(definition,out missileImpact))return;
            armedMissile=definition;
            missileAge=0;
            Model.RocketClock=Model.RocketInterval=definition.FireIntervalSeconds;
            view.SetMissileStrike(missileImpact,definition.AreaRadius,0,true);
        }

        /// <summary>在可见局部敌群中轮换有限候选，按实际爆炸可波及的身体数评分。</summary>
        private bool TryLockMissile(SurvivorWeaponLevelDefinition definition,out Vector2 impact)
        {
            impact=default;
            EnsureHordeSpatial();
            float lockRange=Mathf.Min(CurrentAimRange,definition.Range);
            enemySpatial.QueryCircle(player.x,player.y,lockRange,missileTargets);
            int bestCount=1,sampled=0;
            float bestDistance=float.PositiveInfinity;
            for(int i=0;i<missileTargets.Count;i++)
            {
                int index=(missileSearchOffset+i)%missileTargets.Count;
                if(!enemies.TryGetValue(missileTargets[index],out var candidate) || candidate.Health<=0 ||
                    !view.IsArenaTargetVisible(candidate.Position) || !CanAimGroundBody(candidate,candidate.Position,.75f))continue;
                if(sampled++>=MissileSampleBudget){missileSearchOffset=index;break;}
                var center=candidate.Position;
                enemySpatial.QueryCircle(center.x,center.y,definition.AreaRadius,missileNeighbors);
                int count=0;
                foreach(int id in missileNeighbors)
                    if(MissileCanHit(id,center,out var neighbor) && (neighbor.Position-player).sqrMagnitude<=lockRange*lockRange &&
                        view.IsArenaTargetVisible(neighbor.Position))count++;
                float distance=(center-player).sqrMagnitude;
                if(count<2 || count<bestCount || count==bestCount && distance>=bestDistance)continue;
                impact=center;bestCount=count;bestDistance=distance;
            }
            return bestCount>=2;
        }

        /// <summary>爆炸只检查落点到身体的遮挡，玩家与敌群之间的墙不会阻挡垂直空袭。</summary>
        private bool MissileCanHit(int id,Vector2 center,out ArcadeEnemyRuntime enemy)
        {
            return enemies.TryGetValue(id,out enemy) && enemy.Health>0 &&
                !MissileBlastBlocked(center,enemy.Position) && CanAimGroundBody(enemy,center,.75f);
        }

        /// <summary>连续线段检测爆炸波与实体墙体的相交，薄墙也能挡住范围伤害。</summary>
        private bool MissileBlastBlocked(Vector2 center,Vector2 target)
        {
            foreach(var obstacle in obstacles)
            {
                if(!obstacle.BlocksProjectiles)continue;
                var wall=new ArcadeLayoutObstacle(obstacle.Center.x,obstacle.Center.y,obstacle.HalfExtents.x,obstacle.HalfExtents.y);
                if(ArcadeOutsideRouteRules.Intersects(center.x,center.y,target.x,target.y,wall,0))return true;
            }
            return false;
        }

        /// <summary>按爆炸瞬间的实际位置结算，离开标记范围的敌人能够避开导弹。</summary>
        private void ExplodeMissile()
        {
            var level=armedMissile;
            armedMissile=null;
            view.SetMissileStrike(missileImpact,level.AreaRadius,MissileWarningSeconds+MissileDescentSeconds,false);
            EnsureHordeSpatial();
            enemySpatial.QueryCircle(missileImpact.x,missileImpact.y,level.AreaRadius,missileNeighbors);
            foreach(int id in missileNeighbors)
                if(MissileCanHit(id,missileImpact,out var enemy))ApplyModifierDamage(enemy,level.Damage,missileImpact);
            view.PlayProjectileExplosion(missileImpact,level.AreaRadius);
        }

        /// <summary>退出时取消已预警的空袭，清空锁点、搜索轮次及道具冷却。</summary>
        private void ClearSupport()
        {
            if(armedMissile!=null)view?.SetMissileStrike(missileImpact,armedMissile.AreaRadius,missileAge,false);
            armedMissile=null;missileImpact=default;
            missileAge=missileRetry=0;missileSearchOffset=0;
            Model.RocketClock=Model.RocketInterval=0;
        }
    }
}
