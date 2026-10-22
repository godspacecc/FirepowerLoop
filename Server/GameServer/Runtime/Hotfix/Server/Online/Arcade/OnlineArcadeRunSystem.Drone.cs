using System.Linq;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>按玩家独立推进无人机并归属击杀；阶段只由服务器决定，断线快照不会重复爆炸。</summary>
        private static bool TickDrone(this OnlineArcadeRunActor self,OnlineArcadeBattleRuntime battle,
            OnlineArcadeBattlePlayerRuntime player,OnlineArcadePowerupRuntime powerup,float seconds)
        {
            var tuning=ServerConfig.Instance.Drone;
            var state=powerup.Drone??(powerup.Drone=new ArcadeDroneState{RemainingSeconds=powerup.RemainingSeconds,
                FireCooldownSeconds=powerup.CooldownSeconds,X=player.PositionX+tuning.OffsetX,Z=player.PositionZ+tuning.OffsetZ});
            int tier=TryGetPowerup(player,ArcadePowerupBehavior.WeaponSkull,out var skull)?(int)skull.Magnitude:1;
            powerup.DronePowerTier=tier;
            var targets=battle.Enemies.Values.Where(e=>e.CurrentHealth>0).Select(e=>new ArcadeDroneTarget(e.RuntimeId,e.PositionX,e.PositionZ)).ToArray();
            var step=ArcadeDroneRules.Step(state,tuning,seconds,player.PositionX,player.PositionZ,tier,
                player.Alive&&!player.InTreasureRoom&&player.RespawnSeconds<=0&&!battle.AwaitingExit&&!battle.WaveClearAwarded,
                battle.Projectiles.Count<ServerConfig.Instance.ArcadeRule.MaxProjectiles,targets);
            powerup.PositionX=state.X;powerup.PositionZ=state.Z;
            powerup.RemainingSeconds=state.RemainingSeconds;
            powerup.CooldownSeconds=state.FireCooldownSeconds;
            if(state.Phase==ArcadeDronePhase.Ramming)
            {powerup.ShotDirectionX=step.DirectionX;powerup.ShotDirectionZ=step.DirectionZ;}
            if(step.Shot)
            {
                powerup.ShotDirectionX=step.DirectionX;powerup.ShotDirectionZ=step.DirectionZ;
                long id=battle.NextProjectileRuntimeId++;
                battle.Projectiles.Add(id,new OnlineArcadeProjectileRuntime{RuntimeId=id,OwnerPlayerId=player.PlayerId,
                    PositionX=state.X,PositionZ=state.Z,VelocityX=step.DirectionX*tuning.ProjectileSpeed,
                    VelocityZ=step.DirectionZ*tuning.ProjectileSpeed,Damage=step.ShotDamage,RemainingSeconds=tuning.ProjectileLifetimeSeconds,
                    Behavior=ArcadeWeaponBehavior.Direct,RemainingHits=1,SourceWeaponId=powerup.ProductId,WeaponPowerLevel=tier-1});
            }
            if(step.Exploded)
            {
                // 无人机在玩家计时阶段结算，先建立当帧敌人快照，不能依赖稍后敌人更新留下的旧列表。
                battle.EnemyIterationIds.Clear();battle.EnemyIterationIds.AddRange(battle.Enemies.Keys);battle.EnemyIterationIds.Sort();
                self.DamageEnemiesInExplosionRadius(battle,state.X,state.Z,tuning.ExplosionRadius,step.ExplosionDamage,player.PlayerId);
            }
            if(state.Phase==ArcadeDronePhase.Expired)player.Powerups.Remove(ArcadePowerupBehavior.Drone);
            return true;
        }
    }
}
