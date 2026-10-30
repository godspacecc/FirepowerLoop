using System;
using System.Linq;
using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>发射时按距离/稳定身份分配目标；目标失效后才重新选敌，不追玩家。</summary>
        private ArcadeEnemyRuntime[] RocketTargets(ArcadeCombatRuntime r,Vector2 position)=>r.Enemies.Values
            .Where(e=>e.Health>0&&e.CanBeAttacked&&(e.Position-position).sqrMagnitude<=catalog.Rule.WeaponUpgrade.PurpleRocketRange*catalog.Rule.WeaponUpgrade.PurpleRocketRange)
            .OrderBy(e=>(e.Position-position).sqrMagnitude).ThenBy(e=>e.Id).ToArray();

        /// <summary>紫火箭拥有独立扫掠命中，长帧不能越过敌人/墙/箱；寿命只允许实际余下飞行时间。</summary>
        private bool TickPurpleRocket(ArcadeCombatRuntime r,ArcadeProjectileRuntime p,float seconds)
        {
            if(p.Hostile||!p.FromPlayerWeapon||p.SourceWeaponId!="arcade.weapon.rocket"||p.WeaponPowerLevel<2)return false;
            if(float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<=0)return true;
            float active=Mathf.Min(seconds,Mathf.Max(0,p.RemainingSeconds));
            if(active<=0){RemoveProjectile(r,p.Id);return true;}
            var tuning=catalog.Rule.WeaponUpgrade;
            if(!r.Enemies.TryGetValue(p.TrackingEnemyId,out var target)||target.Health<=0||!target.CanBeAttacked||
                (target.Position-p.Position).sqrMagnitude>tuning.PurpleRocketRange*tuning.PurpleRocketRange)
            {target=RocketTargets(r,p.Position).FirstOrDefault();p.TrackingEnemyId=target?.Id??0;}
            if(target!=null)
            {
                var offset=target.Position-p.Position;
                ArcadeRoundContentRules.SteerTowards(p.Velocity.x,p.Velocity.y,offset.x,offset.y,tuning.PurpleRocketTurnRadians,active,out var x,out var z);
                p.Velocity=new Vector2(x,z);
            }
            var from=p.Position;var to=from+p.Velocity*active;float first=1;bool hit=false;int crateId=-1;
            foreach(var wall in ProjectileObstaclesFor(r))
                if(TryGetSegmentObstacleEntry(from,to,0,wall,out float entry)&&entry<=first){first=entry;hit=true;}
            foreach(var enemy in r.Enemies.Values.OrderBy(e=>e.Id))
                if(enemy.Health>0&&enemy.CanBeAttacked&&TryGroundBarrelEntry(from,to,enemy.Position,catalog.Rule.EnemyContactRadius+ProjectileHitRadius,out float entry)&&(!hit||entry<first))
                {first=entry;hit=true;crateId=-1;}
            foreach(var crate in r.Crates.Values.OrderBy(c=>c.Definition.Id))
            {
                var c=crate.Definition;
                if(crate.Health>0&&TryGroundBarrelEntry(from,to,new Vector2(c.X,c.Y),c.Radius+ProjectileHitRadius,out float entry)&&(!hit||entry<first))
                {first=entry;hit=true;crateId=c.Id;}
            }
            p.Position=Vector2.Lerp(from,to,first);
            // 复用已验证的桶优先级、耐久与爆炸结算，不重复实现吞物或死亡奖励。
            if(TryHitGroundBarrel(r,p,from,p.Position)){RemoveProjectile(r,p.Id);return true;}
            if(hit)
            {
                if(crateId>=0)DamageCrate(r,crateId,p.Damage);
                view?.PlayProjectileExplosion(p.Position,p.ExplosionRadius);DamageEnemiesInProjectileExplosion(r,p);
                RemoveProjectile(r,p.Id);return true;
            }
            p.RemainingSeconds=Mathf.Max(0,p.RemainingSeconds-active);
            if(p.RemainingSeconds<=0)RemoveProjectile(r,p.Id);
            return true;
        }
    }
}
