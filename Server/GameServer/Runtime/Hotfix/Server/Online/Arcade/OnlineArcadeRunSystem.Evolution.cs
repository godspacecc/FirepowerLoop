using System;
using System.Linq;
using FirepowerLoop.Arcade;
namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>支援与敌弹不进化；在途武器弹仅看生成档位。</summary>
        private static bool IsPurpleEvolution(OnlineArcadeProjectileRuntime p,string suffix)=>p.OwnerPlayerId!=0&&
            ArcadeWeaponEvolutionRules.IsPurple(p.SourceWeaponId,p.WeaponPowerLevel,p.FromPlayerWeapon,"arcade.weapon."+suffix);

        /// <summary>先裁决引信内有效路径的最近实体，再处理到期；长帧与短帧保持同一爆点。</summary>
        private static bool TickPurpleGrenade(this OnlineArcadeRunActor self,OnlineArcadeBattleRuntime b,OnlineArcadeProjectileRuntime p,float seconds)
        {
            if(!IsPurpleEvolution(p,"grenade"))return false;
            long id=p.RuntimeId;float active=Math.Min(seconds,Math.Max(0,p.RemainingSeconds));
            float ax=p.PositionX,az=p.PositionZ,bx=ax+p.VelocityX*active,bz=az+p.VelocityZ*active,first=1;
            bool hit=false;int crateId=-1;
            foreach(var wall in ProjectileObstaclesFor(b))
                if(TryGetSegmentObstacleEntry(ax,az,bx,bz,0,wall,out float entry)&&entry<=first){first=entry;hit=true;}
            foreach(var crate in b.Crates.Values)
                if(crate.Health>0&&GroundCircleEntry(ax,az,bx,bz,crate.Definition.X,crate.Definition.Y,crate.Definition.Radius+ProjectileHitRadius,out float entry)&&entry<first)
                {first=entry;hit=true;crateId=crate.Definition.Id;}
            foreach(var enemy in b.Enemies.Values)
                if(enemy.CurrentHealth>0&&GroundCircleEntry(ax,az,bx,bz,enemy.PositionX,enemy.PositionZ,ServerConfig.Instance.ArcadeRule.EnemyContactRadius+ProjectileHitRadius,out float entry)&&entry<first)
                {first=entry;hit=true;crateId=-1;}
            float endX=ax+(bx-ax)*first,endZ=az+(bz-az)*first;p.PositionX=endX;p.PositionZ=endZ;
            if(active>0&&self.TryHitGroundBarrel(b,p,ax,az,endX,endZ))
            {
                if(b.Projectiles.ContainsKey(id))self.DamageEnemiesInProjectileExplosion(b,p);
                b.Projectiles.Remove(id);return true;
            }
            p.PositionX=endX;p.PositionZ=endZ;p.RemainingSeconds=Math.Max(0,p.RemainingSeconds-active);
            if(hit||p.RemainingSeconds<=0)
            {
                if(crateId>=0)self.DamageCrate(b,crateId,p.Damage);
                self.DamageEnemiesInProjectileExplosion(b,p);b.Projectiles.Remove(id);
            }
            return true;
        }
        /// <summary>按真实扫掠距离处理前排击杀贯穿及宽束，障碍优先，命中身份去重。</summary>
        private static bool TickPurpleLine(this OnlineArcadeRunActor self,OnlineArcadeBattleRuntime b,OnlineArcadeProjectileRuntime p,float seconds)
        {
            bool basic=IsPurpleEvolution(p,"basic");if(!basic&&!IsPurpleEvolution(p,"piercing"))return false;
            if(seconds<=0||float.IsNaN(seconds)||float.IsInfinity(seconds))return true;
            float active=Math.Min(seconds,Math.Max(0,p.RemainingSeconds));if(active<=0){b.Projectiles.Remove(p.RuntimeId);return true;}
            float fromX=p.PositionX,fromZ=p.PositionZ,toX=fromX+p.VelocityX*active,toZ=fromZ+p.VelocityZ*active,block=1;
            bool blocked=false;int crateId=-1;
            foreach(var wall in ProjectileObstaclesFor(b))
                if(TryGetSegmentObstacleEntry(fromX,fromZ,toX,toZ,p.EvolutionHitRadius,wall,out float entry)&&entry<=block){block=entry;blocked=true;}
            foreach(var crate in b.Crates.Values)
                if(crate.Health>0&&GroundCircleEntry(fromX,fromZ,toX,toZ,crate.Definition.X,crate.Definition.Y,crate.Definition.Radius+ProjectileHitRadius+p.EvolutionHitRadius,out float entry)&&(!blocked||entry<block))
                {block=entry;blocked=true;crateId=crate.Definition.Id;}
            p.PositionX=fromX+(toX-fromX)*block;p.PositionZ=fromZ+(toZ-fromZ)*block;
            if(self.TryHitGroundBarrel(b,p,fromX,fromZ,p.PositionX,p.PositionZ)){b.Projectiles.Remove(p.RuntimeId);return true;}
            float radius=ServerConfig.Instance.ArcadeRule.EnemyContactRadius+ProjectileHitRadius+p.EvolutionHitRadius;
            var hits=b.Enemies.Values.Where(e=>e.CurrentHealth>0&&!p.HitEnemyIds.Contains(e.RuntimeId))
                .Select(e=>new {Enemy=e,Entry=GroundCircleEntry(fromX,fromZ,toX,toZ,e.PositionX,e.PositionZ,radius,out float f)?f:float.PositiveInfinity})
                .Where(h=>h.Entry<=block&&(!blocked||h.Entry<block)).OrderBy(h=>h.Entry).ThenBy(h=>h.Enemy.RuntimeId).ToArray();
            foreach(var hit in hits)
            {
                var enemy=hit.Enemy;if(!b.Enemies.ContainsKey(enemy.RuntimeId)||!TryRegisterProjectileHit(p,enemy.RuntimeId))continue;
                p.PositionX=fromX+(toX-fromX)*hit.Entry;p.PositionZ=fromZ+(toZ-fromZ)*hit.Entry;
                enemy.LastDamagePlayerId=p.OwnerPlayerId;DamageEnemy(b,enemy,p.Damage);bool killed=enemy.CurrentHealth<=0;
                if(killed)self.KillEnemy(b,enemy);
                if((basic&&!killed)||p.RemainingHits<=0){b.Projectiles.Remove(p.RuntimeId);return true;}
                float endX=fromX+(toX-fromX)*block,endZ=fromZ+(toZ-fromZ)*block;
                if(self.TryHitGroundBarrel(b,p,p.PositionX,p.PositionZ,endX,endZ)){b.Projectiles.Remove(p.RuntimeId);return true;}
            }
            p.PositionX=fromX+(toX-fromX)*block;p.PositionZ=fromZ+(toZ-fromZ)*block;p.RemainingSeconds-=active;
            if(blocked){if(crateId>=0)self.DamageCrate(b,crateId,p.Damage);b.Projectiles.Remove(p.RuntimeId);}
            else if(p.RemainingSeconds<=0)b.Projectiles.Remove(p.RuntimeId);
            return true;
        }
        /// <summary>每次齐射对周围敌人最多震退一次，使用正式敌人步行约束。</summary>
        private static void PushPurpleShotgun(OnlineArcadeBattleRuntime b,OnlineArcadeProjectileRuntime p,float cx,float cz)
        {
            if(!IsPurpleEvolution(p,"spread"))return;
            var t=ServerConfig.Instance.WeaponUpgrade.Evolution;var rule=ServerConfig.Instance.ArcadeRule;
            foreach(var enemy in b.Enemies.Values)
            {
                if(enemy.CurrentHealth<=0||p.EvolutionPushedEnemyIds==null||p.EvolutionPushedEnemyIds.Contains(enemy.RuntimeId)||DistanceSquared(cx,cz,enemy.PositionX,enemy.PositionZ)>t.ShotgunPushRadius*t.ShotgunPushRadius)continue;
                p.EvolutionPushedEnemyIds.Add(enemy.RuntimeId);
                float dx=enemy.PositionX-p.SourcePositionX,dz=enemy.PositionZ-p.SourcePositionZ;
                float length=MathF.Sqrt(dx*dx+dz*dz);if(length<.00001f){dx=p.VelocityX;dz=p.VelocityZ;length=MathF.Sqrt(dx*dx+dz*dz);}
                if(length<.00001f)continue;
                float x=enemy.PositionX+dx/length*t.ShotgunPushDistance,z=enemy.PositionZ+dz/length*t.ShotgunPushDistance;
                ResolveBattleEnemyPosition(b,ref x,ref z,enemy.PositionX,enemy.PositionZ,rule.EnemyContactRadius,rule,EffectiveObstacles(b),IslandFor(b),ResolveBattleArena(b).EnemySeaApproachDistance);
                enemy.PositionX=x;enemy.PositionZ=z;
            }
        }
        /// <summary>相同玩家邻近火团合并，并沿用生成时伤害预算，范围外或水面不留火。</summary>
        private static void AddWeaponBurnArea(OnlineArcadeBattleRuntime b,OnlineArcadeProjectileRuntime p)
        {
            if(!IsPurpleEvolution(p,"flamethrower"))return;
            var t=ServerConfig.Instance.WeaponUpgrade.Evolution;
            if(Math.Abs(p.PositionX)>ArenaHalfWidth(b)||Math.Abs(p.PositionZ)>ArenaHalfHeight(b)||IslandFor(b) is {} island&&!island.ContainsCircle(p.PositionX,p.PositionZ,0))return;
            var area=b.WeaponBurnAreas.FirstOrDefault(a=>a.OwnerId==p.OwnerPlayerId&&DistanceSquared(a.X,a.Z,p.PositionX,p.PositionZ)<=t.FlameGroundRadius*t.FlameGroundRadius);
            if(area==null)
            {
                if(b.WeaponBurnAreas.Count>=t.FlameGroundMaxAreas)return;
                area=new ArcadeWeaponBurnArea{Id=(int)b.NextProjectileRuntimeId++,OwnerId=p.OwnerPlayerId,X=p.PositionX,Z=p.PositionZ,TickRemainingSeconds=t.FlameGroundTickSeconds};b.WeaponBurnAreas.Add(area);
            }
            area.RemainingSeconds=t.FlameGroundSeconds;area.Damage=Math.Max(area.Damage,ArcadeWeaponEvolutionRules.DerivedDamage(p.Damage,t.FlameGroundDamageRatio));
        }
        /// <summary>每名玩家的重叠火带取最大跳伤，击杀仍归属对应玩家；无弹丸时仍推进有限残火。</summary>
        private static bool TickWeaponBurnAreas(this OnlineArcadeRunActor self,OnlineArcadeBattleRuntime b,float seconds)
        {
            if(b.WeaponBurnAreas.Count==0){foreach(var enemy in b.Enemies.Values)enemy.EvolutionBurnContactSeconds.Clear();return false;}
            var t=ServerConfig.Instance.WeaponUpgrade.Evolution;var owners=b.WeaponBurnAreas.Select(a=>a.OwnerId).Distinct().ToArray();
            foreach(var enemy in b.Enemies.Values.ToArray())
            {
                foreach(long oldOwner in enemy.EvolutionBurnContactSeconds.Keys.Where(o=>!owners.Contains(o)).ToArray())enemy.EvolutionBurnContactSeconds.Remove(oldOwner);
                foreach(long owner in owners)
                {
                    enemy.EvolutionBurnContactSeconds.TryGetValue(owner,out float contact);
                    int damage=ArcadeWeaponEvolutionRules.BurningDamage(b.WeaponBurnAreas,owner,enemy.PositionX,enemy.PositionZ,ref contact,seconds,t);
                    enemy.EvolutionBurnContactSeconds[owner]=contact;
                    if(damage<=0)continue;enemy.LastDamagePlayerId=owner;DamageEnemyFromFlame(b,enemy,damage);
                    if(enemy.CurrentHealth<=0){self.KillEnemy(b,enemy);break;}
                }
            }
            foreach(var area in b.WeaponBurnAreas)area.RemainingSeconds=Math.Max(0,area.RemainingSeconds-seconds);
            b.WeaponBurnAreas.RemoveAll(a=>a.RemainingSeconds<=0);return true;
        }
        /// <summary>子榴弹继承来源与强化快照，使用原容量和爆炸入口，禁止递归分裂。</summary>
        private static void SpawnPurpleCluster(OnlineArcadeBattleRuntime b,OnlineArcadeProjectileRuntime parent)
        {
            // Boss击杀可在范围伤害中清弹并切换购物/出口阶段，旧父弹不能重新生成子弹。
            if(!b.Projectiles.TryGetValue(parent.RuntimeId,out var current)||!ReferenceEquals(current,parent))return;
            if(!IsPurpleEvolution(parent,"grenade")||parent.EvolutionClusterChild)return;
            parent.EvolutionClusterChild=true;var t=ServerConfig.Instance.WeaponUpgrade.Evolution;
            for(int i=0;i<t.ClusterCount&&b.Projectiles.Count<ServerConfig.Instance.ArcadeRule.MaxProjectiles;i++)
            {
                Rotate(0,1,i*360f/t.ClusterCount,out float dx,out float dz);long id=b.NextProjectileRuntimeId++;
                b.Projectiles.Add(id,new OnlineArcadeProjectileRuntime{RuntimeId=id,OwnerPlayerId=parent.OwnerPlayerId,FromPlayerWeapon=true,
                    SourceWeaponId=parent.SourceWeaponId,WeaponPowerLevel=parent.WeaponPowerLevel,SourcePositionX=parent.SourcePositionX,SourcePositionZ=parent.SourcePositionZ,
                    PositionX=parent.PositionX,PositionZ=parent.PositionZ,VelocityX=dx*t.ClusterSpeed,VelocityZ=dz*t.ClusterSpeed,
                    Damage=ArcadeWeaponEvolutionRules.DerivedDamage(parent.Damage,t.ClusterDamageRatio),RemainingSeconds=t.ClusterSeconds,
                    Behavior=ArcadeWeaponBehavior.TimedExplosive,RemainingHits=1,ExplosionRadius=t.ClusterRadius,EvolutionClusterChild=true});
            }
        }
    }
}
