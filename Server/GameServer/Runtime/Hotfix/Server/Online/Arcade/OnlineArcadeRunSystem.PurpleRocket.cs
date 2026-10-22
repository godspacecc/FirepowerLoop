using System;
using System.Linq;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>与单人相同的距离/身份顺序，目标失效才重新选敌。</summary>
        private static OnlineArcadeEnemyRuntime[] RocketTargets(OnlineArcadeBattleRuntime b,float x,float z)=>b.Enemies.Values
            .Where(e=>e.CurrentHealth>0&&DistanceSquared(x,z,e.PositionX,e.PositionZ)<=ServerConfig.Instance.WeaponUpgrade.PurpleRocketRange*ServerConfig.Instance.WeaponUpgrade.PurpleRocketRange)
            .OrderBy(e=>DistanceSquared(x,z,e.PositionX,e.PositionZ)).ThenBy(e=>e.RuntimeId).ToArray();

        /// <summary>紫火箭完整扫掠；不复用敌方追玩家的追踪入口，寿命耗尽无爆炸。</summary>
        private static bool TickPurpleRocket(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime b,OnlineArcadeProjectileRuntime p,float seconds)
        {
            if(p.OwnerPlayerId<=0||!p.FromPlayerWeapon||p.SourceWeaponId!="arcade.weapon.rocket"||p.WeaponPowerLevel<2)return false;
            if(!float.IsFinite(seconds)||seconds<=0)return true;
            float active=Math.Min(seconds,Math.Max(0,p.RemainingSeconds));
            if(active<=0){b.Projectiles.Remove(p.RuntimeId);return true;}
            var tuning=ServerConfig.Instance.WeaponUpgrade;var rule=ServerConfig.Instance.ArcadeRule;
            if(!b.Enemies.TryGetValue(p.TrackingEnemyId,out var target)||target.CurrentHealth<=0||
                DistanceSquared(p.PositionX,p.PositionZ,target.PositionX,target.PositionZ)>tuning.PurpleRocketRange*tuning.PurpleRocketRange)
            {target=RocketTargets(b,p.PositionX,p.PositionZ).FirstOrDefault();p.TrackingEnemyId=target?.RuntimeId??0;}
            if(target!=null)ArcadeRoundContentRules.SteerTowards(p.VelocityX,p.VelocityZ,target.PositionX-p.PositionX,target.PositionZ-p.PositionZ,
                tuning.PurpleRocketTurnRadians,active,out p.VelocityX,out p.VelocityZ);
            float ax=p.PositionX,az=p.PositionZ,bx=ax+p.VelocityX*active,bz=az+p.VelocityZ*active,first=1;bool hit=false;int crateId=-1;
            foreach(var wall in EffectiveObstacles(b))
                if(TryGetSegmentObstacleEntry(ax,az,bx,bz,0,wall,out float entry)&&entry<=first){first=entry;hit=true;}
            if(IslandFor(b)==null&&ArcadeWeaponUpgradeRules.TryArenaExit(ax,az,bx,bz,rule.ArenaHalfWidth,rule.ArenaHalfHeight,out float edge)&&edge<=first)
            {first=edge;hit=true;}
            foreach(var enemy in b.Enemies.Values.OrderBy(e=>e.RuntimeId))
                if(enemy.CurrentHealth>0&&GroundCircleEntry(ax,az,bx,bz,enemy.PositionX,enemy.PositionZ,rule.EnemyContactRadius+ProjectileHitRadius,out float entry)&&(!hit||entry<first))
                {first=entry;hit=true;crateId=-1;}
            foreach(var crate in b.Crates.Values.OrderBy(c=>c.Definition.Id))
            {
                var c=crate.Definition;
                if(crate.Health>0&&GroundCircleEntry(ax,az,bx,bz,c.X,c.Y,c.Radius+ProjectileHitRadius,out float entry)&&(!hit||entry<first))
                {first=entry;hit=true;crateId=c.Id;}
            }
            p.PositionX=ax+(bx-ax)*first;p.PositionZ=az+(bz-az)*first;
            if(actor.TryHitGroundBarrel(b,p,ax,az,p.PositionX,p.PositionZ))
            {actor.DamageEnemiesInProjectileExplosion(b,p);b.Projectiles.Remove(p.RuntimeId);return true;}
            if(hit)
            {
                if(crateId>=0)actor.DamageCrate(b,crateId,p.Damage);
                actor.DamageEnemiesInProjectileExplosion(b,p);b.Projectiles.Remove(p.RuntimeId);return true;
            }
            p.RemainingSeconds=Math.Max(0,p.RemainingSeconds-active);
            if(p.RemainingSeconds<=0)b.Projectiles.Remove(p.RuntimeId);
            return true;
        }
    }
}
