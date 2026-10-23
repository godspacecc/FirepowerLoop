using System;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;
namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>同一布局再次进场也使用新代次，旧扫掠不能命中新现场。</summary>
        private long herdGeneration;
        private void InitializeHerd(ArcadeCombatRuntime r)
        {
            ClearHerd(r);r.HerdMapId=checked(++herdGeneration);r.HerdBatch=0;r.HerdUntilNext=EarlyStage?.HerdFirstDelay??0;
        }
        private static void ClearHerd(ArcadeCombatRuntime r)
        {foreach(var cow in r.HerdCows)ArcadeHerdRules.Cancel(cow);r.HerdCows.Clear();r.HerdMapId=0;r.HerdImpact=default;foreach(var enemy in r.Enemies.Values)enemy.HerdImpact=default;}
        /// <summary>活动步推进击飞，水平仍经过地图障碍；View仅投影腾空高度，不移动碰撞体。</summary>
        private void TickHerdImpacts(ArcadeCombatRuntime r,float dt)
        {
            if(r.PlayerHealth<=0)r.HerdImpact=default;
            float travel=ArcadeHerdImpactRules.Step(ref r.HerdImpact,dt);
            if(travel>0)r.PlayerPosition=MoveHerdTarget(r,r.PlayerPosition,new Vector2(r.HerdImpact.DirectionX,r.HerdImpact.DirectionZ)*travel,catalog.Rule.PlayerRadius);
            foreach(var enemy in r.Enemies.Values)
            {
                travel=ArcadeHerdImpactRules.Step(ref enemy.HerdImpact,dt);
                if(travel>0)enemy.Position=MoveHerdTarget(r,enemy.Position,new Vector2(enemy.HerdImpact.DirectionX,enemy.HerdImpact.DirectionZ)*travel,catalog.Rule.EnemyContactRadius);
            }
        }
        /// <summary>短步推进平面落点，长帧击飞也不能穿过场内墙或离开地图。</summary>
        private Vector2 MoveHerdTarget(ArcadeCombatRuntime r,Vector2 position,Vector2 delta,float radius)
        {
            int steps=Mathf.Max(1,Mathf.CeilToInt(delta.magnitude/.2f));
            for(int i=0;i<steps;i++)position=ResolvePosition(position+delta/steps,radius,ArenaHalfWidth,ArenaHalfHeight,ObstaclesFor(r),IslandFor(r));
            return position;
        }
        /// <summary>用真实短步阻挡预估两侧落点，选离开牛道的一边；只锁定方向，不瞬移或追加保护。</summary>
        private bool BeginPlayerHerdImpact(ArcadeCombatRuntime r,ArcadeHerdState cow)
        {
            if(r.HerdImpact.Active)return false;
            ArcadeHerdImpactRules.PlayerDirection(cow.DirectionX,cow.DirectionZ,1,out float px,out float pz);
            ArcadeHerdImpactRules.PlayerDirection(cow.DirectionX,cow.DirectionZ,-1,out float nx,out float nz);
            var positive=new Vector2(px,pz);var negative=new Vector2(nx,nz);
            var positiveEnd=MoveHerdTarget(r,r.PlayerPosition,positive*ArcadeHerdImpactRules.Distance,catalog.Rule.PlayerRadius);
            var negativeEnd=MoveHerdTarget(r,r.PlayerPosition,negative*ArcadeHerdImpactRules.Distance,catalog.Rule.PlayerRadius);
            var lateral=new Vector2(-cow.DirectionZ,cow.DirectionX);var origin=new Vector2(cow.X,cow.Z);
            int side=ArcadeHerdImpactRules.PlayerSide(Mathf.Abs(Vector2.Dot(positiveEnd-origin,lateral)),
                Mathf.Abs(Vector2.Dot(negativeEnd-origin,lateral)),-Vector2.Dot(r.PlayerPosition,lateral));
            var direction=side>0?positive:negative;
            return ArcadeHerdImpactRules.Begin(ref r.HerdImpact,direction.x,direction.y);
        }
        /// <summary>活动循环生成固定通路的一排牛；不追踪玩家、不显示方向提示。</summary>
        private void TickHerd(ArcadeCombatRuntime r,float dt)
        {
            var stage=EarlyStage;if(stage==null||stage.HerdCount<=0)return;
            TickHerdImpacts(r,dt);
            r.HerdUntilNext-=dt;
            if(r.HerdUntilNext<=0)
            {
                r.HerdUntilNext=stage.HerdPeriod;
                ArcadeHerdRules.StartBatch(r.HerdCows,stage,r.HerdMapId,++r.HerdBatch);
                return;
            }
            // 先推进整排，死亡或结算导致的中途冻结不能让后半排漏走本帧。
            var steps=r.HerdCows.Select(c=>(Cow:c,Sweep:ArcadeHerdRules.Step(c,stage.Herd,dt,true))).ToArray();
            foreach(var step in steps)
            {
                var cow=step.Cow;var sweep=step.Sweep;
                if(ArcadeHerdRules.TryContact(cow,sweep,r.HerdMapId,-1,r.PlayerPosition.x,r.PlayerPosition.y,catalog.Rule.PlayerRadius,r.PlayerHealth>0))
                {
                    int before=r.PlayerHealth;
                    DamageContentPlayer(r,ArcadeRoundContentRules.Damage(r.PlayerMaxHealth,stage.HerdPlayerDamage));
                    if(r.PlayerHealth>0&&r.PlayerHealth<before&&BeginPlayerHerdImpact(r,cow))
                    {r.Firing=false;r.DashRemaining=0;}
                }
                if(this.GetModel<ArcadeCombatModel>().Runtime!=r||catalog==null||!RoundContentActive(r))return;
                foreach(var enemy in r.Enemies.Values.ToArray())
                {
                    if(!r.Enemies.ContainsKey(enemy.Id)||!ArcadeHerdRules.TryContact(cow,sweep,r.HerdMapId,enemy.Id,enemy.Position.x,enemy.Position.y,catalog.Rule.EnemyContactRadius,enemy.Health>0))continue;
                    DamageEnemy(enemy,ArcadeRoundContentRules.Damage(enemy.MaxHealth,stage.HerdEnemyDamage));
                    if(enemy.Health<=0)KillEnemyWithReaction(r,enemy,true,false,enemy.Position-new UnityEngine.Vector2(cow.DirectionX,cow.DirectionZ),ArcadeDeathImpactRules.Herd);
                    else if(ArcadeHerdImpactRules.Begin(ref enemy.HerdImpact,cow.DirectionX,cow.DirectionZ))CancelMeleeWindup(enemy);
                    if(this.GetModel<ArcadeCombatModel>().Runtime!=r||catalog==null||!RoundContentActive(r))return;
                }
                if(cow.Phase==ArcadeHerdPhase.Expired){ArcadeHerdRules.Cancel(cow);r.HerdCows.Remove(cow);}
            }
        }
    }
}
