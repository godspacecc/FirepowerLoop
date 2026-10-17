using System;
using System.Linq;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>普通支援槽与已购实例共用总鸡容量，纯交易读取前也复用此同步。</summary>
        private static void SyncSlayerMartCapacity(OnlineArcadeBattleRuntime battle,OnlineArcadeBattlePlayerRuntime owner)
        {
            var state=SlayerMartAbility(battle,owner.PlayerId);if(state==null)return;
            state.Chickens=state.Supports.Count(p=>p.Kind==SlayerMartSupportKind.Chicken&&p.RemainingSeconds>0)+
                (TryGetPowerup(owner,ArcadePowerupBehavior.Chicken,out _)?1:0);
        }
        /// <summary>仅新版局死亡清除限时支援；磁铁等原有非支援普通效果维持原合同。</summary>
        private static void ClearSlayerMartOrdinarySupports(OnlineArcadeBattleRuntime battle,OnlineArcadeBattlePlayerRuntime player)
        {
            if(battle.SlayerMartRulesVersion!=1)return;
            foreach(var kind in player.Powerups.Keys.Where(IsSlayerMartSupportSlot).ToArray())player.Powerups.Remove(kind);
        }
        private static bool IsSlayerMartSupportSlot(ArcadePowerupBehavior kind)=>kind==ArcadePowerupBehavior.Chicken||kind==ArcadePowerupBehavior.ElectricOrb||
            kind==ArcadePowerupBehavior.Saw||kind==ArcadePowerupBehavior.Barrel||kind==ArcadePowerupBehavior.Sentry||kind==ArcadePowerupBehavior.Decoy;
        /// <summary>权威活动帧推进实例；资源表现与网络投影不参与扣款、冷却或伤害。</summary>
        private static bool TickSlayerMartSupports(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime battle,float seconds)
        {
            if(battle.SlayerMartRulesVersion!=1||battle.SlayerMart==null||!SlayerMartClockActive(battle)||actor.DisconnectedAtByPlayer.Count>0)return false;
            var config=ServerConfig.Instance.SlayerMart;bool changed=false;
            foreach(var owner in battle.Players.Values.OrderBy(p=>p.PlayerId))
            {
                var state=SlayerMartAbility(battle,owner.PlayerId);if(state==null)continue;
                changed|=state.Supports.Count>0&&seconds>0;
                ArcadeSlayerMartSupportRules.Advance(state,seconds,true,config.Supports,config.Parameter);SyncSlayerMartCapacity(battle,owner);
                if(!owner.Alive||owner.InTreasureRoom||battle.AwaitingExit)continue;
                foreach(var item in state.Supports.ToArray())
                {
                    var definition=config.Supports.Get(item.Kind);
                    var bodies=ArcadeSlayerMartSupportRules.Bodies(state,item,owner.PositionX,owner.PositionZ,config.Supports,config.Parameter);
                    if(item.Kind==SlayerMartSupportKind.Chicken)
                    {
                        if(battle.Projectiles.Count>=ServerConfig.Instance.ArcadeRule.MaxProjectiles)continue;
                        var target=battle.Enemies.Values.OrderBy(e=>DistanceSquared(bodies[0].X,bodies[0].Y,e.PositionX,e.PositionZ)).ThenBy(e=>e.RuntimeId).FirstOrDefault();
                        if(target==null||!ArcadeSlayerMartSupportRules.TryShoot(item,config.Supports))continue;
                        float dx=target.PositionX-bodies[0].X,dz=target.PositionZ-bodies[0].Y,length=Math.Max(.001f,MathF.Sqrt(dx*dx+dz*dz));
                        if(dx==0&&dz==0){dz=1;length=1;}
                        long id=battle.NextProjectileRuntimeId++;
                        battle.Projectiles.Add(id,new OnlineArcadeProjectileRuntime{RuntimeId=id,OwnerPlayerId=owner.PlayerId,SupportInstanceId=item.InstanceId,
                            PositionX=bodies[0].X,PositionZ=bodies[0].Y,VelocityX=dx/length*definition.ProjectileSpeed,VelocityZ=dz/length*definition.ProjectileSpeed,
                            Damage=SlayerMartDamage(battle,owner.PlayerId,definition.Damage,SlayerMartDamageSource.Chicken),RemainingSeconds=SupportProjectileLifetimeSeconds,
                            Behavior=ArcadeWeaponBehavior.Direct,RemainingHits=1});
                        item.ShotDirectionX=dx/length;item.ShotDirectionY=dz/length;changed=true;continue;
                    }
                    if(item.Kind==SlayerMartSupportKind.Barrel)
                    {
                        float radius=config.Parameter("support.contactRadius")+ServerConfig.Instance.ArcadeRule.EnemyContactRadius;
                        bool touching=battle.Enemies.Values.Any(e=>DistanceSquared(bodies[0].X,bodies[0].Y,e.PositionX,e.PositionZ)<=radius*radius);
                        if(!ArcadeSlayerMartSupportRules.TryTriggerBarrel(item,touching,config.Supports))continue;
                        battle.EnemyIterationIds.Clear();battle.EnemyIterationIds.AddRange(battle.Enemies.Keys.OrderBy(p=>p));
                        actor.DamageEnemiesInRadius(battle,bodies[0].X,bodies[0].Y,definition.Radius,definition.Damage,owner.PlayerId);changed=true;continue;
                    }
                    foreach(var enemy in battle.Enemies.Values.ToArray())
                    {
                        string targetKey=battle.ArenaNumber+":"+battle.WaveIndex+":"+battle.IsChallenge+":"+enemy.RuntimeId;
                        if(!ArcadeSlayerMartSupportRules.TryContact(state,item,targetKey,enemy.PositionX,enemy.PositionZ,ServerConfig.Instance.ArcadeRule.EnemyContactRadius,
                            owner.PositionX,owner.PositionZ,config.Supports,config.Parameter))continue;
                        enemy.LastDamagePlayerId=owner.PlayerId;DamageEnemy(battle,enemy,definition.Damage);changed=true;
                        if(enemy.CurrentHealth<=0){actor.KillEnemy(battle,enemy);continue;}
                        if(item.Kind!=SlayerMartSupportKind.Orb||enemy.ContentBossKind>0||!string.IsNullOrEmpty(enemy.BossId)||enemy.MoveSpeed<=0)continue;
                        float dx=enemy.PositionX-owner.PositionX,dz=enemy.PositionZ-owner.PositionZ,length=Math.Max(.001f,MathF.Sqrt(dx*dx+dz*dz));
                        if(dx==0&&dz==0){dz=1;length=1;}
                        float push=config.Parameter("support.orbKnockback");
                        float nextX=enemy.PositionX+dx/length*push,nextZ=enemy.PositionZ+dz/length*push;
                        if(!enemy.EnteringIsland||IslandFor(battle)!=null)
                            ResolvePosition(ref nextX,ref nextZ,enemy.PositionX,enemy.PositionZ,ServerConfig.Instance.ArcadeRule.EnemyContactRadius,
                                ServerConfig.Instance.ArcadeRule,EffectiveObstacles(battle),IslandFor(battle));
                        enemy.PositionX=nextX;enemy.PositionZ=nextZ;
                    }
                }
            }
            return changed;
        }
    }
}
