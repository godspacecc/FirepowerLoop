using System;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game
{
    public sealed partial class SurvivorSystem
    {
        /// <summary>本局燃烧弹及火区独立预算；不占步枪弹丸预算。</summary>
        private const int MolotovFlightBudget=2, MolotovZoneBudget=4;
        /// <summary>每秒四次范围结算，余数按火区与敌人保存，避免低伤害截断。</summary>
        private const float MolotovDamageStep=.25f;
        private readonly List<MolotovFlight> molotovFlights=new List<MolotovFlight>();
        private readonly List<MolotovZone> molotovZones=new List<MolotovZone>();
        private readonly List<int> molotovCandidates=new List<int>();
        private float molotovClock;
        /// <summary>投出时冻结目标落点及武器数值，升级不追溯已投出的瓶子。</summary>
        private sealed class MolotovFlight { public int Id; public Vector2 Origin,Target; public float Age; public SurvivorWeaponLevelDefinition Level; }
        /// <summary>火区仅持有领域位置、计时及有限生命周期伤害余数。</summary>
        private sealed class MolotovZone { public int Id; public Vector2 Position; public float Age,Settled; public SurvivorWeaponLevelDefinition Level; public readonly Dictionary<int,float> Fractions=new Dictionary<int,float>(); }
        /// <summary>暂停由主Tick入口阻断；冷却固定，不继承步枪射速、弹头或弹夹。</summary>
        private void TickMolotov(float dt)
        {
            molotovClock=Mathf.Max(0,molotovClock-dt);
            Model.MolotovCooldownRemaining=molotovClock;
            for(int i=molotovZones.Count-1;i>=0;i--)AdvanceMolotovZone(molotovZones[i],dt);
            for(int i=molotovFlights.Count-1;i>=0;i--)
            {
                var f=molotovFlights[i];f.Age+=dt;float duration=f.Level.ProjectileLifetimeSeconds;
                if(f.Age>=duration)
                {
                    view.SetSurvivorMolotov(f.Id,f.Target,0,0,false);molotovFlights.RemoveAt(i);
                    if(molotovZones.Count<MolotovZoneBudget){var z=new MolotovZone{Id=f.Id,Position=f.Target,Level=f.Level};molotovZones.Add(z);AdvanceMolotovZone(z,f.Age-duration);}
                }
                else ProjectMolotov(f);
            }
            int number=Model.Loadout?.LevelOf("Molotov")??0;
            if(number==0||molotovClock>0||molotovFlights.Count>=MolotovFlightBudget||molotovZones.Count+molotovFlights.Count>=MolotovZoneBudget)return;
            EnsureHordeSpatial();var level=config.WeaponLevel("Molotov",number);
            float range=Mathf.Min(CurrentAimRange,level.Range),nearest=range*range;ArcadeEnemyRuntime target=null;
            enemySpatial.QueryCircle(player.x,player.y,range,molotovCandidates);
            foreach(int id in molotovCandidates)
            {
                if(!enemies.TryGetValue(id,out var e)||e.Health<=0||!view.IsArenaTargetVisible(e.Position)||Blocked(player,e.Position)||!CanAimGroundBody(e,player,.75f))continue;
                float distance=(e.Position-player).sqrMagnitude;
                if(distance>nearest||(distance==nearest&&target!=null&&id>target.Id))continue;
                nearest=distance;target=e;
            }
            if(target==null)return;
            var flight=new MolotovFlight{Id=++identity,Origin=player,Target=target.Position,Level=level};molotovFlights.Add(flight);molotovClock=level.FireIntervalSeconds;
            Model.MolotovCooldownRemaining=Model.MolotovCooldownDuration=molotovClock;ProjectMolotov(flight);
        }
        /// <summary>抛物线只投影显示，落点由领域冻结，不产生物理碰撞伤害。</summary>
        private void ProjectMolotov(MolotovFlight f)
        {float t=f.Age/f.Level.ProjectileLifetimeSeconds;view.SetSurvivorMolotov(f.Id,Vector2.Lerp(f.Origin,f.Target,t),.8f*(1-t)+4*t*(1-t),t*540,true);}
        /// <summary>只查询火区邻近网格，所有击杀统一走既有死亡事务。</summary>
        private void AdvanceMolotovZone(MolotovZone z,float dt)
        {
            z.Age=Mathf.Min(z.Level.AreaDurationSeconds,z.Age+dt);
            EnsureHordeSpatial();
            while(z.Settled+MolotovDamageStep<=z.Age+.00001f)
            {
                z.Settled+=MolotovDamageStep;enemySpatial.QueryCircle(z.Position.x,z.Position.y,z.Level.AreaRadius,molotovCandidates);
                foreach(int id in molotovCandidates)
                {
                    if(!enemies.TryGetValue(id,out var e)||e.Health<=0||(e.Position-z.Position).sqrMagnitude>z.Level.AreaRadius*z.Level.AreaRadius||Blocked(z.Position,e.Position)||!CanAimGroundBody(e,z.Position,.75f))continue;
                    z.Fractions.TryGetValue(id,out float fraction);float amount=fraction+z.Level.Damage*MolotovDamageStep;int damage=(int)amount;
                    ApplyModifierDamage(e,damage);if(enemies.ContainsKey(id))z.Fractions[id]=amount-damage;else z.Fractions.Remove(id);
                }
            }
            bool active=z.Age<z.Level.AreaDurationSeconds;
            view.SetSurvivorFireZone(z.Id,z.Position,z.Level.AreaRadius,z.Age,active);
            if(!active)molotovZones.Remove(z);
        }
        /// <summary>退出、重开和销毁明确回收在途瓶子与火区。</summary>
        private void ClearMolotov()
        {
            foreach(var f in molotovFlights)view?.SetSurvivorMolotov(f.Id,f.Target,0,0,false);
            foreach(var z in molotovZones)view?.SetSurvivorFireZone(z.Id,z.Position,z.Level.AreaRadius,z.Age,false);
            molotovFlights.Clear();molotovZones.Clear();molotovClock=0;
            Model.MolotovCooldownRemaining=Model.MolotovCooldownDuration=0;
        }
    }
}
