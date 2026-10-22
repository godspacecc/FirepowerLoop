using System;
using System.Linq;
using FirepowerLoop.Arcade;
using UnityEngine;
using QFramework;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>只由活动战斗帧扣除电弧余量；不借领域时钟推进，旧主题同样可用。</summary>
        private static void TickPurpleRayArcs(ArcadeCombatRuntime r,float seconds)
        {
            if(float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<=0)return;
            for(int i=r.PurpleRayArcs.Count-1;i>=0;i--)
                if((r.PurpleRayArcs[i].RemainingSeconds-=seconds)<=0)r.PurpleRayArcs.RemoveAt(i);
        }

        /// <summary>寿命内逐段找最近命中；反射保留本帧剩余路程，只有紫级玩家射线进入。</summary>
        private bool TickPurpleRay(ArcadeCombatRuntime r,ArcadeProjectileRuntime p,float seconds)
        {
            if(p.Hostile||!p.FromPlayerWeapon||p.SourceWeaponId!="arcade.weapon.raygun"||p.WeaponPowerLevel<2)return false;
            if(float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<=0)return true;
            float active=Mathf.Min(seconds,Mathf.Max(0,p.RemainingSeconds)),remaining=active;
            if(remaining<=0){RemoveProjectile(r,p.Id);return true;}
            for(int step=0;step<=catalog.Rule.WeaponUpgrade.PurpleRayReflections&&remaining>0;step++)
            {
                var from=p.Position;var to=from+p.Velocity*remaining;float first=1;int kind=0,crateId=-1;ArcadeEnemyRuntime target=null;
                Vector2 wallCenter=Vector2.zero,wallHalf=new Vector2(ArenaHalfWidth,ArenaHalfHeight);
                foreach(var wall in ObstaclesFor(r))
                    if(TryGetSegmentObstacleEntry(from,to,0,wall,out float entry)&&entry<=first)
                    {first=entry;kind=1;wallCenter=wall.Center;wallHalf=wall.HalfExtents;}
                if(IslandFor(r)==null&&ArcadeWeaponUpgradeRules.TryArenaExit(from.x,from.y,to.x,to.y,ArenaHalfWidth,ArenaHalfHeight,out float edge)&&edge<=first)
                {first=edge;kind=1;wallCenter=Vector2.zero;wallHalf=new Vector2(ArenaHalfWidth,ArenaHalfHeight);}
                foreach(var enemy in r.Enemies.Values.OrderBy(e=>e.Id))
                    if(enemy.Health>0&&TryGroundBarrelEntry(from,to,enemy.Position,catalog.Rule.EnemyContactRadius+ProjectileHitRadius,out float entry)&&(kind==0||entry<first))
                    {first=entry;kind=2;target=enemy;}
                foreach(var crate in r.Crates.Values.OrderBy(c=>c.Definition.Id))
                    if(crate.Health>0&&TryGroundBarrelEntry(from,to,new Vector2(crate.Definition.X,crate.Definition.Y),crate.Definition.Radius+ProjectileHitRadius,out float entry)&&(kind==0||entry<first))
                    {first=entry;kind=3;crateId=crate.Definition.Id;}
                var hit=Vector2.Lerp(from,to,first);p.Position=hit;
                if(TryHitGroundBarrel(r,p,from,hit)){RemoveProjectile(r,p.Id);return true;}
                p.Position=hit;
                if(kind==2){HitPurpleRay(r,p,target);RemoveProjectile(r,p.Id);return true;}
                if(kind==3)
                {DamageCrate(r,crateId,p.Damage);view?.PlayProjectileExplosion(hit,p.ExplosionRadius);DamageEnemiesInProjectileExplosion(r,p);RemoveProjectile(r,p.Id);return true;}
                if(kind==0)break;
                if(p.RemainingReflections<=0){view?.PlayProjectileSurfaceImpact(hit,p.Velocity);RemoveProjectile(r,p.Id);return true;}
                p.RemainingReflections--;float vx=p.Velocity.x,vz=p.Velocity.y;
                ArcadeWeaponUpgradeRules.Reflect(hit.x,hit.y,wallCenter.x,wallCenter.y,wallHalf.x,wallHalf.y,ref vx,ref vz);
                p.Velocity=new Vector2(vx,vz);p.Position+=p.Velocity.normalized*.001f;remaining*=1-first;
            }
            p.RemainingSeconds=Mathf.Max(0,p.RemainingSeconds-active);
            if(p.RemainingSeconds<=0)RemoveProjectile(r,p.Id);
            return true;
        }

        /// <summary>首敌与稳定排序的额外目标只结算一次；电弧只投影已实际选中的连接。</summary>
        private void HitPurpleRay(ArcadeCombatRuntime r,ArcadeProjectileRuntime p,ArcadeEnemyRuntime primary)
        {
            var tuning=catalog.Rule.WeaponUpgrade;var origin=primary.Position;int damage=p.Damage;
            var others=r.Enemies.Values.Where(e=>e.Health>0&&e.Id!=primary.Id&&(e.Position-origin).sqrMagnitude<=tuning.PurpleRayChainRange*tuning.PurpleRayChainRange)
                .OrderBy(e=>(e.Position-origin).sqrMagnitude).ThenBy(e=>e.Id).Take(tuning.PurpleRayChainCount).ToArray();
            foreach(var enemy in new[]{primary}.Concat(others))
            {
                if(!r.Enemies.ContainsKey(enemy.Id)||enemy.Health<=0||!p.HitEnemyIds.Add(enemy.Id))continue;
                if(enemy!=primary)
                {
                    while(r.PurpleRayArcs.Count>=catalog.Rule.MaxProjectiles*2)r.PurpleRayArcs.RemoveAt(0);
                    r.PurpleRayArcs.Add(new ArcadeRayArcState(r.NextEntityId++,origin.x,origin.y,enemy.Position.x,enemy.Position.y));
                }
                DamageEnemy(enemy,damage);
                if(enemy.Health<=0)KillEnemy(r,enemy);
                if(r.CompletionSubmitted||this.GetModel<ArcadeCombatModel>().Runtime!=r||catalog==null)return;
            }
        }
    }
}
