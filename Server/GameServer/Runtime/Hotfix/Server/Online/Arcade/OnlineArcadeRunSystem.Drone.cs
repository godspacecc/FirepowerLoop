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
            if(tier<3)powerup.ExtraDrone=null;
            else if(powerup.ExtraDrone==null&&state.Phase==ArcadeDronePhase.Following&&state.RemainingSeconds>0)
                powerup.ExtraDrone=new ArcadeDroneState{RemainingSeconds=state.RemainingSeconds,FireCooldownSeconds=state.FireCooldownSeconds,
                    X=player.PositionX-tuning.OffsetX,Z=player.PositionZ+tuning.OffsetZ};
            var targets=battle.Enemies.Values.Where(e=>e.CurrentHealth>0).Select(e=>new ArcadeDroneTarget(e.RuntimeId,e.PositionX,e.PositionZ)).ToArray();
            bool canAct=player.Alive&&!player.InTreasureRoom&&player.RespawnSeconds<=0&&!battle.AwaitingExit&&!battle.WaveClearAwarded;
            var step=self.StepDroneUnit(battle,player,powerup,state,targets,tier,seconds,canAct,1,0);
            if(powerup.ExtraDrone!=null)self.StepDroneUnit(battle,player,powerup,powerup.ExtraDrone,targets,tier,seconds,canAct,-1,step.TargetId);
            powerup.PositionX=state.X;powerup.PositionZ=state.Z;
            powerup.RemainingSeconds=state.RemainingSeconds;
            powerup.CooldownSeconds=state.FireCooldownSeconds;
            if(state.Phase==ArcadeDronePhase.Expired&&(powerup.ExtraDrone==null||powerup.ExtraDrone.Phase==ArcadeDronePhase.Expired))player.Powerups.Remove(ArcadePowerupBehavior.Drone);
            return true;
        }
        /// <summary>权威逐架裁决原攻击与归属，额外机优先不同目标；不复制玩家武器。</summary>
        private static ArcadeDroneStep StepDroneUnit(this OnlineArcadeRunActor self,OnlineArcadeBattleRuntime battle,
            OnlineArcadeBattlePlayerRuntime player,OnlineArcadePowerupRuntime powerup,ArcadeDroneState state,
            ArcadeDroneTarget[] targets,int tier,float seconds,bool canAct,int sign,long excluded)
        {
            var tuning=ServerConfig.Instance.Drone;
            var step=ArcadeDroneRules.Step(state,tuning,seconds,player.PositionX,player.PositionZ,tier,canAct,
                battle.Projectiles.Count<ServerConfig.Instance.ArcadeRule.MaxProjectiles,targets,sign,excluded);
            if(step.Shot||state.Phase==ArcadeDronePhase.Ramming){state.DirectionX=step.DirectionX;state.DirectionZ=step.DirectionZ;}
            if(sign==1&&state.Phase==ArcadeDronePhase.Ramming)
            {powerup.ShotDirectionX=step.DirectionX;powerup.ShotDirectionZ=step.DirectionZ;}
            if(step.Shot)
            {
                if(sign==1){powerup.ShotDirectionX=step.DirectionX;powerup.ShotDirectionZ=step.DirectionZ;}
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
            return step;
        }
    }
}
