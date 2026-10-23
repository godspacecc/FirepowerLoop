using System.Linq;
using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>无人机沿用现有弹丸与爆炸入口；当前骷髅倍率即时同步，不拥有第二份强化计时。</summary>
        private void TickDrone(ArcadeCombatRuntime runtime,float deltaTime)
        {
            if(!runtime.Powerups.TryGetValue(global::ArcadePowerupBehavior.Drone,out var powerup))return;
            var tuning=catalog.Rule.Drone;
            var state=powerup.Drone??(powerup.Drone=new ArcadeDroneState{RemainingSeconds=powerup.RemainingSeconds,
                FireCooldownSeconds=powerup.CooldownSeconds,X=runtime.PlayerPosition.x+tuning.OffsetX,Z=runtime.PlayerPosition.y+tuning.OffsetZ});
            int tier=TryGetPowerup(runtime,global::ArcadePowerupBehavior.WeaponSkull,out var skull)?Mathf.RoundToInt(skull.Definition.Magnitude):1;
            powerup.DronePowerTier=tier;
            if(tier<3)powerup.ExtraDrone=null;
            else if(powerup.ExtraDrone==null&&state.Phase==ArcadeDronePhase.Following&&state.RemainingSeconds>0)
                powerup.ExtraDrone=new ArcadeDroneState{RemainingSeconds=state.RemainingSeconds,FireCooldownSeconds=state.FireCooldownSeconds,
                    X=runtime.PlayerPosition.x-tuning.OffsetX,Z=runtime.PlayerPosition.y+tuning.OffsetZ};
            var targets=runtime.Enemies.Values.Where(e=>e.Health>0).Select(e=>new ArcadeDroneTarget(e.Id,e.Position.x,e.Position.y)).ToArray();
            bool canAct=runtime.PlayerHealth>0&&runtime.RespawnRemaining<=0&&!runtime.InTreasureRoom&&!runtime.AwaitingExit&&!runtime.IslandRoundAwaitingExit&&!runtime.WaveClearAwarded;
            var step=StepDroneUnit(runtime,powerup,state,targets,tier,deltaTime,canAct,1,0);
            if(powerup.ExtraDrone!=null)
                StepDroneUnit(runtime,powerup,powerup.ExtraDrone,targets,tier,deltaTime,canAct,-1,step.TargetId);
            powerup.Position=new Vector2(state.X,state.Z);
            powerup.RemainingSeconds=state.RemainingSeconds;
            powerup.CooldownSeconds=state.FireCooldownSeconds;
            if(state.Phase==ArcadeDronePhase.Expired&&(powerup.ExtraDrone==null||powerup.ExtraDrone.Phase==ArcadeDronePhase.Expired))runtime.Powerups.Remove(global::ArcadePowerupBehavior.Drone);
        }
        /// <summary>两架各用原无人机攻击，不复制玩家弹种；次机优先其他目标，共用容量。</summary>
        private ArcadeDroneStep StepDroneUnit(ArcadeCombatRuntime runtime,ArcadePowerupRuntime powerup,ArcadeDroneState state,
            ArcadeDroneTarget[] targets,int tier,float seconds,bool canAct,int sign,long excluded)
        {
            var tuning=catalog.Rule.Drone;
            var step=ArcadeDroneRules.Step(state,tuning,seconds,runtime.PlayerPosition.x,runtime.PlayerPosition.y,tier,canAct,
                runtime.Projectiles.Count<catalog.Rule.MaxProjectiles,targets,sign,excluded);
            if(step.Shot||state.Phase==ArcadeDronePhase.Ramming){state.DirectionX=step.DirectionX;state.DirectionZ=step.DirectionZ;}
            var position=new Vector2(state.X,state.Z);var direction=new Vector2(step.DirectionX,step.DirectionZ);
            if(sign==1&&(step.Shot||state.Phase==ArcadeDronePhase.Ramming))powerup.ShotDirection=direction;
            if(step.Shot)SpawnProjectile(runtime,false,position,direction*tuning.ProjectileSpeed,step.ShotDamage,tuning.ProjectileLifetimeSeconds,
                projectileVisualResourceName:ArcadeWeaponPowerVisual.WithLevel(catalog.Weapons.First(w=>w.Id==catalog.Rule.BaseWeaponId).ProjectileVisualResourceName,tier-1),sourceWeaponId:powerup.ProductId);
            if(step.Exploded)DamageEnemiesInExplosionRadius(runtime,position,tuning.ExplosionRadius,step.ExplosionDamage);
            return step;
        }
    }
}
