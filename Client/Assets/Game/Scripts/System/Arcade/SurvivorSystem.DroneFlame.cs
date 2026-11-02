using System.Collections.Generic;
using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    public sealed partial class SurvivorSystem
    {
        /// <summary>局部采样间隔及未满一点的伤害余数，暂停时都不推进。</summary>
        private float droneFlameElapsed, droneFlameFraction;
        private bool droneFlameEmitting;
        private readonly List<int> droneFlameCandidates=new List<int>();
        private readonly List<ArcadeEnemyRuntime> droneFlameTargets=new List<ArcadeEnemyRuntime>();

        /// <summary>改装后的无人机升级卡展示当前与下一级实际喷火每秒伤害。</summary>
        public bool TryGetDroneFlameUpgradeDamage(int id,out float currentDps,out float nextDps)
        {
            currentDps=nextDps=0;
            if(!Model.DroneFlameUpgraded || !TryGetWeaponUpgrade(id,out var kind,out _,out var current,out var next) ||
                kind!="Drone" || current==null)return false;
            currentDps=DroneFlameDps(current,Model.DroneFlameUpgrade);
            nextDps=DroneFlameDps(next,Model.DroneFlameUpgrade);
            return true;
        }

        /// <summary>卡片及伤害采样共用本局改装倍率和无人机独立等级。</summary>
        private static float DroneFlameDps(SurvivorWeaponLevelDefinition drone,SurvivorUpgradeDefinition upgrade)
            =>drone.Damage*upgrade.EffectDamage(1);

        /// <summary>每个固定采样周期只作一次局部索引查询，由合法的最近身体确定锥形朝向。</summary>
        private void TickDroneFlame(float dt,SurvivorWeaponLevelDefinition drone,SurvivorUpgradeDefinition upgrade)
        {
            droneFlameElapsed+=dt;
            float interval=upgrade.ActivationDelaySeconds;
            if(droneFlameElapsed<interval)return;
            droneFlameElapsed-=interval;
            float range=upgrade.EffectRadius(1),visibleRange=CurrentAimRange;
            enemySpatial.QueryCircle(dronePosition.x,dronePosition.y,range,droneFlameCandidates);
            droneFlameTargets.Clear();
            ArcadeEnemyRuntime target=null;
            float nearest=float.PositiveInfinity;
            foreach(int id in droneFlameCandidates)
            {
                if(!enemies.TryGetValue(id,out var enemy) || enemy.Health<=0 ||
                    (enemy.Position-player).sqrMagnitude>visibleRange*visibleRange || !view.IsArenaTargetVisible(enemy.Position) ||
                    ProjectileWallBetween(dronePosition,enemy.Position) || !CanAimGroundBody(enemy,dronePosition,droneVisual.VisualOffset.y))continue;
                droneFlameTargets.Add(enemy);
                float distance=(enemy.Position-dronePosition).sqrMagnitude;
                if(distance<nearest){nearest=distance;target=enemy;}
            }
            droneFlameEmitting=target!=null;
            if(!droneFlameEmitting){droneFlameFraction=0;return;}
            if(nearest>0)droneFacing=(target.Position-dronePosition).normalized;
            float amount=droneFlameFraction+DroneFlameDps(drone,upgrade)*interval;
            int damage=Mathf.FloorToInt(amount);
            droneFlameFraction=amount-damage;
            float cosine=Mathf.Cos(.5f*upgrade.EffectAngleDegrees*Mathf.Deg2Rad);
            foreach(var enemy in droneFlameTargets)
            {
                var offset=enemy.Position-dronePosition;
                if(Vector2.Dot(droneFacing,offset)>=offset.magnitude*cosine)
                    ApplyModifierDamage(enemy,damage,dronePosition);
            }
        }

        /// <summary>退出清理改装、命中缓存和喷嘴，重开时仍从普通无人机开始。</summary>
        private void ClearDroneFlame()
        {
            if(Model.DroneFlameUpgraded)view?.SetDroneFlame(false,dronePosition,droneFacing,false,0);
            Model.DroneFlameUpgrade=null;
            droneFlameElapsed=droneFlameFraction=0;
            droneFlameEmitting=false;
            droneFlameTargets.Clear();droneFlameCandidates.Clear();
        }
    }
}
