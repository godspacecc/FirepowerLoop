using System;
using System.Linq;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>活动权威帧推进短时电弧；即使没有在途弹，也能到期移除并刷新快照。</summary>
        private static bool TickPurpleRayArcs(OnlineArcadeBattleRuntime b,float seconds)
        {
            if(!float.IsFinite(seconds)||seconds<=0||b.PurpleRayArcs.Count==0)return false;
            for(int i=b.PurpleRayArcs.Count-1;i>=0;i--)
                if((b.PurpleRayArcs[i].RemainingSeconds-=seconds)<=0)b.PurpleRayArcs.RemoveAt(i);
            return true;
        }

        /// <summary>紫级射线只扫掠最近实体命中；空旷外缘不截断，实体命中不反弹。</summary>
        private static bool TickPurpleRay(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime b,OnlineArcadeProjectileRuntime p,float seconds)
        {
            if(p.OwnerPlayerId<=0||!p.FromPlayerWeapon||p.SourceWeaponId!="arcade.weapon.raygun"||p.WeaponPowerLevel<2)return false;
            if(!float.IsFinite(seconds)||seconds<=0)return true;
            float active=Math.Min(seconds,Math.Max(0,p.RemainingSeconds));
            if(active<=0){b.Projectiles.Remove(p.RuntimeId);return true;}
            var rule=ServerConfig.Instance.ArcadeRule;
            float ax=p.PositionX,az=p.PositionZ,bx=ax+p.VelocityX*active,bz=az+p.VelocityZ*active,first=1;
            int kind=0,crateId=-1;OnlineArcadeEnemyRuntime target=null;
            foreach(var wall in ProjectileObstaclesFor(b))
                if(TryGetSegmentObstacleEntry(ax,az,bx,bz,0,wall,out float entry)&&entry<=first)
                {first=entry;kind=1;}
            foreach(var enemy in b.Enemies.Values.OrderBy(e=>e.RuntimeId))
                if(enemy.CurrentHealth>0&&enemy.CanBeAttacked&&TryEnemySweep(enemy,ax,az,bx,bz,rule.EnemyContactRadius+ProjectileHitRadius,p.EvolutionHitRadius,out float entry)&&(kind==0||entry<first))
                {first=entry;kind=2;target=enemy;}
            foreach(var crate in b.Crates.Values.OrderBy(c=>c.Definition.Id))
                if(crate.Health>0&&GroundCircleEntry(ax,az,bx,bz,crate.Definition.X,crate.Definition.Y,crate.Definition.Radius+ProjectileHitRadius,out float entry)&&(kind==0||entry<first))
                {first=entry;kind=3;crateId=crate.Definition.Id;}
            float x=ax+(bx-ax)*first,z=az+(bz-az)*first;p.PositionX=x;p.PositionZ=z;
            if(actor.TryHitGroundBarrel(b,p,ax,az,x,z))
            {actor.DamageEnemiesInProjectileExplosion(b,p);b.Projectiles.Remove(p.RuntimeId);return true;}
            p.PositionX=x;p.PositionZ=z;
            if(kind==2){actor.HitPurpleRay(b,p,target);b.Projectiles.Remove(p.RuntimeId);return true;}
            if(kind==3){actor.DamageCrate(b,crateId,p.Damage);actor.DamageEnemiesInProjectileExplosion(b,p);b.Projectiles.Remove(p.RuntimeId);return true;}
            if(kind==1){RecordWeaponFeedback(b,p,2,p.PositionX,p.PositionZ);b.Projectiles.Remove(p.RuntimeId);return true;}

            p.RemainingSeconds=Math.Max(0,p.RemainingSeconds-active);
            if(p.RemainingSeconds<=0)b.Projectiles.Remove(p.RuntimeId);
            return true;
        }

        /// <summary>首敌加最多两个四米内目标，稳定排序且各一次；伤害归属保留给击杀入口。</summary>
        private static void HitPurpleRay(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime b,OnlineArcadeProjectileRuntime p,OnlineArcadeEnemyRuntime primary)
        {
            var tuning=ServerConfig.Instance.WeaponUpgrade;float x=primary.PositionX,z=primary.PositionZ;
            var others=b.Enemies.Values.Where(e=>e.CurrentHealth>0&&CanAimAtEnemy(e,x,z)&&e.RuntimeId!=primary.RuntimeId&&DistanceSquared(x,z,e.PositionX,e.PositionZ)<=tuning.PurpleRayChainRange*tuning.PurpleRayChainRange)
                .OrderBy(e=>DistanceSquared(x,z,e.PositionX,e.PositionZ)).ThenBy(e=>e.RuntimeId).Take(tuning.PurpleRayChainCount).ToArray();
            foreach(var enemy in new[]{primary}.Concat(others))
            {
                if(!b.Enemies.ContainsKey(enemy.RuntimeId)||enemy.CurrentHealth<=0||!p.HitEnemyIds.Add(enemy.RuntimeId))continue;
                if(enemy!=primary)
                {
                    while(b.PurpleRayArcs.Count>=ServerConfig.Instance.ArcadeRule.MaxProjectiles*2)b.PurpleRayArcs.RemoveAt(0);
                    b.PurpleRayArcs.Add(new ArcadeRayArcState((int)b.NextProjectileRuntimeId++,x,z,enemy.PositionX,enemy.PositionZ));
                }
                enemy.LastDamagePlayerId=p.OwnerPlayerId;
                if(actor.TryDamageCarrierBarrel(b,enemy,p.Damage,p.OwnerPlayerId))continue;
                DamageEnemy(b,enemy,p.Damage);
                if(enemy.CurrentHealth<=0)actor.KillEnemy(b,enemy);
                if(actor.Battle!=b)return;
            }
        }
    }
}
