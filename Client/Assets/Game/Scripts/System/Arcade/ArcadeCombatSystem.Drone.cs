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
            var targets=runtime.Enemies.Values.Where(e=>e.Health>0).Select(e=>new ArcadeDroneTarget(e.Id,e.Position.x,e.Position.y)).ToArray();
            var step=ArcadeDroneRules.Step(state,tuning,deltaTime,runtime.PlayerPosition.x,runtime.PlayerPosition.y,tier,
                runtime.PlayerHealth>0&&runtime.RespawnRemaining<=0&&!runtime.InTreasureRoom&&!runtime.AwaitingExit&&!runtime.IslandRoundAwaitingExit&&!runtime.WaveClearAwarded,
                runtime.Projectiles.Count<catalog.Rule.MaxProjectiles,targets);
            powerup.Position=new Vector2(state.X,state.Z);
            powerup.RemainingSeconds=state.RemainingSeconds;
            powerup.CooldownSeconds=state.FireCooldownSeconds;
            if(state.Phase==ArcadeDronePhase.Ramming)
                powerup.ShotDirection=new Vector2(step.DirectionX,step.DirectionZ);
            if(step.Shot)
            {
                powerup.ShotDirection=new Vector2(step.DirectionX,step.DirectionZ);
                SpawnProjectile(runtime,false,powerup.Position,powerup.ShotDirection*tuning.ProjectileSpeed,
                    step.ShotDamage,tuning.ProjectileLifetimeSeconds,projectileVisualResourceName:ArcadeWeaponPowerVisual.WithLevel(catalog.Weapons.First(w=>w.Id==catalog.Rule.BaseWeaponId).ProjectileVisualResourceName,tier-1),sourceWeaponId:powerup.ProductId);
            }
            if(step.Exploded)DamageEnemiesInExplosionRadius(runtime,powerup.Position,tuning.ExplosionRadius,step.ExplosionDamage);
            if(state.Phase==ArcadeDronePhase.Expired)runtime.Powerups.Remove(global::ArcadePowerupBehavior.Drone);
        }
    }
}
