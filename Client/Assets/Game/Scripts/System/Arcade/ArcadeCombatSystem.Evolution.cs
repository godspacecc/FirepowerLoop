using System;
using System.Linq;
using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>进化弹体沿真实剩余寿命抛射；地火只读取玩法区域，不自行延长伤害。</summary>
        private void SyncWeaponEvolutionVisuals(ArcadeCombatRuntime runtime)
        {
            var t=catalog.Rule.WeaponUpgrade.Evolution;
            foreach(var area in runtime.WeaponBurnAreas)
                view.SetWeaponBurnArea(area.Id,new Vector2(area.X,area.Z),t.FlameGroundRadius,area.RemainingSeconds);
            foreach(var p in runtime.Projectiles.Values)
            {
                if(p.EvolutionHitRadius>0)view.SetEvolvedPiercing(p.Id,p.Position,p.Velocity,p.EvolutionHitRadius);
                else if(IsPurpleEvolution(p,"grenade"))
                {
                    float duration=p.EvolutionClusterChild?t.ClusterSeconds:catalog.Weapons.First(w=>w.Id==p.SourceWeaponId).ProjectileLifetimeSeconds;
                    view.SetEvolvedGrenade(p.Id,p.Position,p.Velocity,p.EvolutionClusterChild,1-p.RemainingSeconds/duration);
                }
            }
        }

        /// <summary>实际引爆时选择已采用的集束表现；其他弹种沿用原爆炸反馈。</summary>
        private void PlayWeaponProjectileExplosion(ArcadeProjectileRuntime p)
        {
            if(IsPurpleEvolution(p,"grenade"))view?.PlayEvolvedGrenadeExplosion(p.Position,p.ExplosionRadius,p.EvolutionClusterChild);
            else view?.PlayProjectileExplosion(p.Position,p.ExplosionRadius);
        }

        /// <summary>只使用发射时固定的玩家武器身份与紫级，换枪不改变在途特技。</summary>
        private static bool IsPurpleEvolution(ArcadeProjectileRuntime p,string suffix)=>!p.Hostile&&
            ArcadeWeaponEvolutionRules.IsPurple(p.SourceWeaponId,p.WeaponPowerLevel,p.FromPlayerWeapon,"arcade.weapon."+suffix);

        /// <summary>引信终点前仍按扫掠路径处理首个墙、桶、箱或敌人，不能因长帧到期跳过碰撞。</summary>
        private bool TickPurpleGrenade(ArcadeCombatRuntime r,ArcadeProjectileRuntime p,float seconds)
        {
            if(!IsPurpleEvolution(p,"grenade"))return false;
            int id=p.Id;float active=Mathf.Min(seconds,Mathf.Max(0,p.RemainingSeconds));
            var from=p.Position;var to=from+p.Velocity*active;float first=1;bool hit=false;int crateId=-1;
            foreach(var wall in ProjectileObstaclesFor(r))
                if(TryGetSegmentObstacleEntry(from,to,0,wall,out float entry)&&entry<=first){first=entry;hit=true;}
            foreach(var crate in r.Crates.Values)
                if(crate.Health>0&&TryGroundBarrelEntry(from,to,new Vector2(crate.Definition.X,crate.Definition.Y),crate.Definition.Radius+ProjectileHitRadius,out float entry)&&entry<first)
                {first=entry;hit=true;crateId=crate.Definition.Id;}
            foreach(var enemy in r.Enemies.Values)
                if(enemy.Health>0&&enemy.DockSeaEntry.CanBeAttacked&&TryGroundBarrelEntry(from,to,enemy.Position,catalog.Rule.EnemyContactRadius+ProjectileHitRadius,out float entry)&&entry<first)
                {first=entry;hit=true;crateId=-1;}
            var end=Vector2.Lerp(from,to,first);p.Position=end;
            if(active>0&&TryHitGroundBarrel(r,p,from,end)){RemoveProjectile(r,id);return true;}
            p.Position=end;p.RemainingSeconds=Mathf.Max(0,p.RemainingSeconds-active);
            if(hit||p.RemainingSeconds<=0)
            {
                if(crateId>=0)DamageCrate(r,crateId,p.Damage);
                PlayWeaponProjectileExplosion(p);DamageEnemiesInProjectileExplosion(r,p);RemoveProjectile(r,id);
            }
            return true;
        }

        /// <summary>击杀贯穿和宽束按整段弹路排序命中，长帧不能越过前排或先伤墙后敌人。</summary>
        private bool TickPurpleLine(ArcadeCombatRuntime r,ArcadeProjectileRuntime p,float seconds)
        {
            bool basic=IsPurpleEvolution(p,"basic");
            if(!basic&&!IsPurpleEvolution(p,"piercing"))return false;
            if(seconds<=0||float.IsNaN(seconds)||float.IsInfinity(seconds))return true;
            float active=Mathf.Min(seconds,Mathf.Max(0,p.RemainingSeconds));
            if(active<=0){RemoveProjectile(r,p.Id);return true;}
            var from=p.Position;var to=from+p.Velocity*active;float block=1;int crateId=-1;bool blocked=false;
            foreach(var wall in ProjectileObstaclesFor(r))
                if(TryGetSegmentObstacleEntry(from,to,p.EvolutionHitRadius,wall,out float entry)&&entry<=block){block=entry;blocked=true;}
            foreach(var crate in r.Crates.Values)
                if(crate.Health>0&&TryGroundBarrelEntry(from,to,new Vector2(crate.Definition.X,crate.Definition.Y),crate.Definition.Radius+ProjectileHitRadius+p.EvolutionHitRadius,out float entry)&&(!blocked||entry<block))
                {block=entry;blocked=true;crateId=crate.Definition.Id;}
            var end=Vector2.Lerp(from,to,block);p.Position=end;
            if(TryHitGroundBarrel(r,p,from,end)){RemoveProjectile(r,p.Id);return true;}
            float radius=catalog.Rule.EnemyContactRadius+ProjectileHitRadius+p.EvolutionHitRadius;
            var hits=r.Enemies.Values.Where(e=>e.Health>0&&e.DockSeaEntry.CanBeAttacked&&!p.HitEnemyIds.Contains(e.Id))
                .Select(e=>new {Enemy=e,Entry=TryGroundBarrelEntry(from,to,e.Position,radius,out float f)?f:float.PositiveInfinity})
                .Where(h=>h.Entry<=block&&(!blocked||h.Entry<block)).OrderBy(h=>h.Entry).ThenBy(h=>h.Enemy.Id).ToArray();
            foreach(var hit in hits)
            {
                var enemy=hit.Enemy;if(!r.Enemies.ContainsKey(enemy.Id)||!TryRegisterProjectileHit(p,enemy.Id))continue;
                p.Position=Vector2.Lerp(from,to,hit.Entry);
                if(TryDamageCarrierBarrel(r,enemy,p.Damage)){RemoveProjectile(r,p.Id);return true;}
                DamageEnemy(enemy,p.Damage);bool killed=enemy.Health<=0;
                if(killed)KillEnemy(r,enemy);
                if(r.CompletionSubmitted||catalog==null)return true;
                if((basic&&!killed)||p.RemainingHits<=0){RemoveProjectile(r,p.Id);return true;}
                if(TryHitGroundBarrel(r,p,p.Position,end)){RemoveProjectile(r,p.Id);return true;}
            }
            p.Position=end;p.RemainingSeconds-=active;
            if(blocked)
            {if(crateId>=0)DamageCrate(r,crateId,p.Damage);else view?.PlayProjectileSurfaceImpact(end,p.Velocity);RemoveProjectile(r,p.Id);}
            else if(p.RemainingSeconds<=0)RemoveProjectile(r,p.Id);
            return true;
        }

        /// <summary>震退周边存活敌人；每次齐射对同一敌人最多一次，仍受正式步行碰撞约束。</summary>
        private void PushPurpleShotgun(ArcadeCombatRuntime r,ArcadeProjectileRuntime p,Vector2 center)
        {
            if(!IsPurpleEvolution(p,"spread"))return;
            var t=catalog.Rule.WeaponUpgrade.Evolution;
            foreach(var enemy in r.Enemies.Values)
            {
                if(enemy.StageEnemy.Phase>0||enemy.DockSeaEntry.Active||enemy.Health<=0||p.EvolutionPushedEnemyIds==null||p.EvolutionPushedEnemyIds.Contains(enemy.Id)||
                    (enemy.Position-center).sqrMagnitude>t.ShotgunPushRadius*t.ShotgunPushRadius)continue;
                p.EvolutionPushedEnemyIds.Add(enemy.Id);
                var direction=enemy.Position-p.SourcePosition;
                if(direction.sqrMagnitude<.00001f)direction=p.Velocity;
                var displaced=enemy.Position+direction.normalized*t.ShotgunPushDistance;
                enemy.Position=ResolveEnemyPosition(enemy.Position,displaced,catalog.Rule.EnemyContactRadius,
                    ArenaHalfWidth,ArenaHalfHeight,ObstaclesFor(r),IslandFor(r),arena.Opening.EnemySeaApproachDistance);
            }
        }
        /// <summary>紫火焰在到达的位置留下有限残火；相邻区域合并，避免高射速无限叠伤。</summary>
        private void AddWeaponBurnArea(ArcadeCombatRuntime r,ArcadeProjectileRuntime p)
        {
            if(!IsPurpleEvolution(p,"flamethrower"))return;
            var t=catalog.Rule.WeaponUpgrade.Evolution;
            if(Mathf.Abs(p.Position.x)>ArenaHalfWidth||Mathf.Abs(p.Position.y)>ArenaHalfHeight||
                IslandFor(r) is {} island&&!island.ContainsCircle(p.Position.x,p.Position.y,0))return;
            var area=r.WeaponBurnAreas.FirstOrDefault(a=>(new Vector2(a.X,a.Z)-p.Position).sqrMagnitude<=t.FlameGroundRadius*t.FlameGroundRadius);
            if(area==null)
            {
                if(r.WeaponBurnAreas.Count>=t.FlameGroundMaxAreas)return;
                area=new ArcadeWeaponBurnArea{Id=r.NextEntityId++,X=p.Position.x,Z=p.Position.y,TickRemainingSeconds=t.FlameGroundTickSeconds};
                r.WeaponBurnAreas.Add(area);
            }
            area.RemainingSeconds=t.FlameGroundSeconds;
            area.Damage=Math.Max(area.Damage,ArcadeWeaponEvolutionRules.DerivedDamage(p.Damage,t.FlameGroundDamageRatio));
        }
        /// <summary>残火只伤敌人；重叠区域同一帧取最大有效跳伤，死亡沿用原奖励入口。</summary>
        private void TickWeaponBurnAreas(ArcadeCombatRuntime r,float seconds)
        {
            if(r.WeaponBurnAreas.Count==0){foreach(var enemy in r.Enemies.Values)enemy.EvolutionBurnContactSeconds=0;return;}
            var t=catalog.Rule.WeaponUpgrade.Evolution;
            foreach(var enemy in r.Enemies.Values.ToArray())
            {
                int damage=ArcadeWeaponEvolutionRules.BurningDamage(r.WeaponBurnAreas,0,enemy.Position.x,enemy.Position.y,
                    ref enemy.EvolutionBurnContactSeconds,seconds,t);
                if(damage<=0)continue;
                if(TryDamageCarrierBarrel(r,enemy,damage))continue;
                DamageEnemyFromFlame(enemy,damage);
                if(enemy.Health<=0)KillEnemy(r,enemy);
                if(r.CompletionSubmitted||catalog==null)return;
            }
            foreach(var area in r.WeaponBurnAreas)area.RemainingSeconds=Math.Max(0,area.RemainingSeconds-seconds);
            r.WeaponBurnAreas.RemoveAll(a=>a.RemainingSeconds<=0);
        }
        /// <summary>首爆生成子榴弹，子代不能再次分裂；沿用弹丸预算、原武器归属与爆炸入口。</summary>
        private void SpawnPurpleCluster(ArcadeCombatRuntime r,ArcadeProjectileRuntime parent)
        {
            // Boss死亡、购物或离场会先清空原弹丸；旧爆炸回调不得在已清理现场派生。
            if(!r.Projectiles.TryGetValue(parent.Id,out var current)||!ReferenceEquals(current,parent))return;
            if(!IsPurpleEvolution(parent,"grenade")||parent.EvolutionClusterChild)return;
            parent.EvolutionClusterChild=true;
            var t=catalog.Rule.WeaponUpgrade.Evolution;
            for(int i=0;i<t.ClusterCount;i++)
            {
                var direction=Rotate(Vector2.up,i*360f/t.ClusterCount);
                var shot=SpawnProjectile(r,false,parent.Position,direction*t.ClusterSpeed,
                    ArcadeWeaponEvolutionRules.DerivedDamage(parent.Damage,t.ClusterDamageRatio),t.ClusterSeconds,
                    global::ArcadeWeaponBehavior.TimedExplosive,1,t.ClusterRadius,parent.ProjectileVisualResourceName,
                    true,parent.SourceWeaponId,parent.WeaponPowerLevel);
                if(shot==null)break;
                shot.EvolutionClusterChild=true;shot.SourcePosition=parent.SourcePosition;
            }
        }
    }
}
