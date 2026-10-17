using System;
using System.Linq;
using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>普通锯／桶只投影其真实接触体，避免同时残留旧中心模型。</summary>
        private bool SyncSlayerMartOrdinaryOrbitVisual(ArcadeCombatRuntime runtime,ArcadePowerupRuntime powerup)
        {
            var state=SlayerMartAbility(runtime);var behavior=powerup.Definition.Behavior;
            if(state==null||slayerMartConfig==null||(behavior!=global::ArcadePowerupBehavior.Saw&&behavior!=global::ArcadePowerupBehavior.Barrel))return false;
            var kind=behavior==global::ArcadePowerupBehavior.Saw?SlayerMartSupportKind.Saw:SlayerMartSupportKind.Barrel;
            if(powerup.RemainingSeconds<=0||state.Supports.Any(p=>p.Kind==kind&&p.RemainingSeconds>0))return true;
            SyncSlayerMartBodyVisual(runtime,state,ArcadeSlayerMartSupportRules.OrdinaryBody(state,kind,powerup.RemainingSeconds),powerup.Definition);
            return true;
        }
        /// <summary>每帧只发出活动实例的只读位置，缺资源不改变能力或交付账本。</summary>
        private void SyncSlayerMartSupportVisuals(ArcadeCombatRuntime runtime)
        {
            var state=SlayerMartAbility(runtime);if(state==null||slayerMartConfig==null)return;
            foreach(var item in state.Supports.Where(p=>p.RemainingSeconds>0))
            {
                var definition=catalog.Powerups.FirstOrDefault(p=>p.Id==item.ProductId);
                if(string.IsNullOrEmpty(definition.Id))continue;
                SyncSlayerMartBodyVisual(runtime,state,item,definition);
            }
        }
        /// <summary>几何和视觉共用Bodies，两个锯片各自有身份且保留同一逻辑命中组。</summary>
        private void SyncSlayerMartBodyVisual(ArcadeCombatRuntime runtime,ArcadeSlayerMartPlayer state,ArcadeSlayerMartEffectInstance item,ArcadePowerupDefinition definition)
        {
            var bodies=ArcadeSlayerMartSupportRules.Bodies(state,item,runtime.PlayerPosition.x,runtime.PlayerPosition.y,slayerMartConfig.Supports,slayerMartConfig.Parameter);
            for(int index=0;index<bodies.Length;index++)view.SetSlayerMartSupport(state.PlayerId,item.InstanceId,index,definition,
                new Vector2(bodies[index].X,bodies[index].Y),new Vector2(item.ShotDirectionX,item.ShotDirectionY));
        }
        /// <summary>普通槽复用真实轨道和共享目标周期；购买同类活动时只结算购买实例。</summary>
        private bool TickSlayerMartOrdinaryOrbit(ArcadeCombatRuntime runtime,ArcadePowerupRuntime powerup)
        {
            var state=SlayerMartAbility(runtime);var behavior=powerup.Definition.Behavior;
            if(state==null||slayerMartConfig==null||(behavior!=global::ArcadePowerupBehavior.Saw&&behavior!=global::ArcadePowerupBehavior.Barrel))return false;
            var kind=behavior==global::ArcadePowerupBehavior.Saw?SlayerMartSupportKind.Saw:SlayerMartSupportKind.Barrel;
            if(!SlayerMartClockActive(runtime)||state.Supports.Any(p=>p.Kind==kind&&p.RemainingSeconds>0))return true;
            var item=ArcadeSlayerMartSupportRules.OrdinaryBody(state,kind,powerup.RemainingSeconds);
            if(kind==SlayerMartSupportKind.Barrel)
            {
                if(powerup.Charges<=0||powerup.CooldownSeconds>0)return true;
                var body=ArcadeSlayerMartSupportRules.Bodies(state,item,runtime.PlayerPosition.x,runtime.PlayerPosition.y,slayerMartConfig.Supports,slayerMartConfig.Parameter)[0];
                var origin=new Vector2(body.X,body.Y);float radius=slayerMartConfig.Parameter("support.contactRadius")+catalog.Rule.EnemyContactRadius;
                if(!HasEnemyInRadius(runtime,origin,radius))return true;
                DamageEnemiesInRadius(runtime,origin,powerup.Definition.Radius,powerup.Definition.Damage);
                powerup.Charges--;powerup.CooldownSeconds=slayerMartConfig.Supports.Get(kind).Interval;
                if(powerup.Charges==0)powerup.RemainingSeconds=0;return true;
            }
            foreach(var enemy in runtime.Enemies.Values.ToArray())
            {
                string target=runtime.ArenaNumber+":"+runtime.WaveIndex+":"+runtime.IsChallenge+":"+enemy.Id;
                if(!ArcadeSlayerMartSupportRules.TryContact(state,item,target,enemy.Position.x,enemy.Position.y,catalog.Rule.EnemyContactRadius,
                    runtime.PlayerPosition.x,runtime.PlayerPosition.y,slayerMartConfig.Supports,slayerMartConfig.Parameter))continue;
                DamageEnemy(enemy,powerup.Definition.Damage);if(enemy.Health<=0)KillEnemy(runtime,enemy);
            }
            return true;
        }
        /// <summary>原普通鸡和购买实例只占一套总容量，不用商店打开次数重算奖励。</summary>
        private void SyncSlayerMartCapacity(ArcadeCombatRuntime runtime)
        {
            var state=SlayerMartAbility(runtime);if(state==null)return;
            state.Chickens=state.Supports.Count(p=>p.Kind==SlayerMartSupportKind.Chicken&&p.RemainingSeconds>0)+
                (TryGetPowerup(runtime,global::ArcadePowerupBehavior.Chicken,out _)?1:0);
        }
        /// <summary>新版死亡清除正式普通支援槽，保留原非支援效果的既定行为。</summary>
        private void ClearSlayerMartOrdinarySupports(ArcadeCombatRuntime runtime)
        {
            if(runtime.SlayerMartRulesVersion!=1)return;
            foreach(var kind in runtime.Powerups.Keys.Where(IsSlayerMartSupportSlot).ToArray())runtime.Powerups.Remove(kind);
        }
        private static bool IsSlayerMartSupportSlot(global::ArcadePowerupBehavior kind)=>kind==global::ArcadePowerupBehavior.Chicken||kind==global::ArcadePowerupBehavior.ElectricOrb||
            kind==global::ArcadePowerupBehavior.Saw||kind==global::ArcadePowerupBehavior.Barrel||kind==global::ArcadePowerupBehavior.Sentry||kind==global::ArcadePowerupBehavior.Decoy;
        /// <summary>原活动帧推进购买支援，显示失败不重赠、不重置次数或冷却。</summary>
        private void TickSlayerMartSupports(ArcadeCombatRuntime runtime,float seconds)
        {
            var state=SlayerMartAbility(runtime);if(state==null||slayerMartConfig==null||!SlayerMartClockActive(runtime))return;
            ArcadeSlayerMartSupportRules.Advance(state,seconds,true,slayerMartConfig.Supports,slayerMartConfig.Parameter);SyncSlayerMartCapacity(runtime);
            if(runtime.InTreasureRoom||runtime.AwaitingExit)return;
            foreach(var item in state.Supports.ToArray())
            {
                var definition=slayerMartConfig.Supports.Get(item.Kind);
                var bodies=ArcadeSlayerMartSupportRules.Bodies(state,item,runtime.PlayerPosition.x,runtime.PlayerPosition.y,slayerMartConfig.Supports,slayerMartConfig.Parameter);
                var origin=new Vector2(bodies[0].X,bodies[0].Y);
                if(item.Kind==SlayerMartSupportKind.Chicken)
                {
                    if(runtime.Projectiles.Count>=catalog.Rule.MaxProjectiles)continue;
                    var target=runtime.Enemies.Values.OrderBy(e=>(e.Position-origin).sqrMagnitude).ThenBy(e=>e.Id).FirstOrDefault();
                    if(target==null||!ArcadeSlayerMartSupportRules.TryShoot(item,slayerMartConfig.Supports))continue;
                    var offset=target.Position-origin;var direction=offset.sqrMagnitude<.000001f?Vector2.up:offset.normalized;
                    int id=runtime.NextEntityId;
                    SpawnProjectile(runtime,false,origin,direction*definition.ProjectileSpeed,
                        SlayerMartDamage(runtime,definition.Damage,SlayerMartDamageSource.Chicken),SupportProjectileLifetimeSeconds);
                    if(runtime.Projectiles.TryGetValue(id,out var projectile))projectile.SupportInstanceId=item.InstanceId;
                    item.ShotDirectionX=direction.x;item.ShotDirectionY=direction.y;continue;
                }
                if(item.Kind==SlayerMartSupportKind.Barrel)
                {
                    float radius=slayerMartConfig.Parameter("support.contactRadius")+catalog.Rule.EnemyContactRadius;
                    bool touching=runtime.Enemies.Values.Any(e=>(e.Position-origin).sqrMagnitude<=radius*radius);
                    if(ArcadeSlayerMartSupportRules.TryTriggerBarrel(item,touching,slayerMartConfig.Supports))
                        DamageEnemiesInRadius(runtime,origin,definition.Radius,definition.Damage);
                    continue;
                }
                foreach(var enemy in runtime.Enemies.Values.ToArray())
                {
                    string targetKey=runtime.ArenaNumber+":"+runtime.WaveIndex+":"+runtime.IsChallenge+":"+enemy.Id;
                    if(!ArcadeSlayerMartSupportRules.TryContact(state,item,targetKey,enemy.Position.x,enemy.Position.y,catalog.Rule.EnemyContactRadius,
                        runtime.PlayerPosition.x,runtime.PlayerPosition.y,slayerMartConfig.Supports,slayerMartConfig.Parameter))continue;
                    DamageEnemy(enemy,definition.Damage);
                    if(enemy.Health<=0){KillEnemy(runtime,enemy);continue;}
                    if(item.Kind!=SlayerMartSupportKind.Orb||enemy.ContentBossKind>0||enemy.IsBossPresentation||!string.IsNullOrEmpty(enemy.BossId)||enemy.Speed<=0)continue;
                    var offset=enemy.Position-runtime.PlayerPosition;var direction=offset.sqrMagnitude<.000001f?Vector2.up:offset.normalized;
                    var next=enemy.Position+direction*slayerMartConfig.Parameter("support.orbKnockback");
                    if(!enemy.EnteringIsland||IslandFor(runtime)!=null)
                        next=ResolvePosition(next,catalog.Rule.EnemyContactRadius,catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight,ObstaclesFor(runtime),IslandFor(runtime));
                    enemy.Position=next;
                }
            }
        }
    }
}
