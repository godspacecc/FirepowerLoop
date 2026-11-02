using System;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game
{
    public sealed partial class SurvivorSystem
    {
        /// <summary>本局粘液瓶及粘液区独立预算；不占步枪弹丸预算。</summary>
        private const int SlimeBottleFlightBudget=2, SlimeBottleZoneBudget=4;
        private readonly List<SlimeBottleFlight> slimeBottleFlights=new List<SlimeBottleFlight>();
        private readonly List<SlimeBottleZone> slimeBottleZones=new List<SlimeBottleZone>();
        private readonly List<int> slimeBottleCandidates=new List<int>();
        private float slimeBottleClock;
        /// <summary>投出时冻结目标落点及武器数值，升级不追溯已投出的瓶子。</summary>
        private sealed class SlimeBottleFlight { public int Id; public Vector2 Origin,Target; public float Age; public SurvivorWeaponLevelDefinition Level; }
        /// <summary>粘液区仅持有领域位置、计时及有限生命周期投掷快照。</summary>
        private sealed class SlimeBottleZone { public int Id; public Vector2 Position; public float Age,SlowFraction; public SurvivorWeaponLevelDefinition Level; }
        /// <summary>暂停由主Tick入口阻断；冷却固定，不继承步枪射速、弹头或弹夹。</summary>
        private void TickSlimeBottle(float dt)
        {
            slimeBottleClock=Mathf.Max(0,slimeBottleClock-dt);
            if((Model.Loadout?.LevelOf("SlimeBottle")??0)>0)Model.MolotovCooldownRemaining=slimeBottleClock;

            for(int i=slimeBottleZones.Count-1;i>=0;i--)AdvanceSlimeBottleZone(slimeBottleZones[i],dt);
            for(int i=slimeBottleFlights.Count-1;i>=0;i--)
            {
                var f=slimeBottleFlights[i];f.Age+=dt;float duration=f.Level.ProjectileLifetimeSeconds;
                if(f.Age>=duration)
                {
                    view.SetSurvivorSlimeBottle(f.Id,f.Target,0,0,false);slimeBottleFlights.RemoveAt(i);
                    if(slimeBottleZones.Count<SlimeBottleZoneBudget){var z=new SlimeBottleZone{Id=f.Id,Position=f.Target,Level=f.Level,SlowFraction=SlimeStrength(f.Level.Level)};slimeBottleZones.Add(z);AdvanceSlimeBottleZone(z,f.Age-duration);}
                }
                else ProjectSlimeBottle(f);
            }
            int number=Model.Loadout?.LevelOf("SlimeBottle")??0;
            if(number==0||slimeBottleClock>0||slimeBottleFlights.Count>=SlimeBottleFlightBudget||slimeBottleZones.Count+slimeBottleFlights.Count>=SlimeBottleZoneBudget)return;
            EnsureHordeSpatial();var level=config.WeaponLevel("SlimeBottle",number);
            float range=Mathf.Min(CurrentAimRange,level.Range),nearest=range*range;ArcadeEnemyRuntime target=null;
            enemySpatial.QueryCircle(player.x,player.y,range,slimeBottleCandidates);
            foreach(int id in slimeBottleCandidates)
            {
                if(!enemies.TryGetValue(id,out var e)||e.Health<=0||!view.IsArenaTargetVisible(e.Position)||Blocked(player,e.Position)||!CanAimGroundBody(e,player,.75f))continue;
                float distance=(e.Position-player).sqrMagnitude;
                if(distance>nearest||(distance==nearest&&target!=null&&id>target.Id))continue;
                nearest=distance;target=e;
            }
            if(target==null)return;
            var flight=new SlimeBottleFlight{Id=++identity,Origin=player,Target=target.Position,Level=level};slimeBottleFlights.Add(flight);slimeBottleClock=level.FireIntervalSeconds;
            Model.MolotovCooldownRemaining=Model.MolotovCooldownDuration=slimeBottleClock;
            ProjectSlimeBottle(flight);
        }
        /// <summary>抛物线只投影显示，落点由领域冻结，不产生物理碰撞伤害。</summary>
        private void ProjectSlimeBottle(SlimeBottleFlight f)
        {float t=f.Age/f.Level.ProjectileLifetimeSeconds;view.SetSurvivorSlimeBottle(f.Id,Vector2.Lerp(f.Origin,f.Target,t),Mathf.Lerp(1,.12f,t)+Mathf.Sin(t*Mathf.PI)*1.5f,10+t*400,true);}
        /// <summary>仅推进区域年龄，不进入伤害、击杀或掉落事务。</summary>
        private void AdvanceSlimeBottleZone(SlimeBottleZone z,float dt)
        {
            z.Age=Mathf.Min(z.Level.AreaDurationSeconds,z.Age+dt);
            bool active=z.Age<z.Level.AreaDurationSeconds;
            view.SetSurvivorSlimeZone(z.Id,z.Position,z.Level.AreaRadius,z.Age,active);
            if(!active)slimeBottleZones.Remove(z);
        }
        /// <summary>创建区域时冻结权威配置强度，避免每个敌人逐帧查询配置。</summary>
        private float SlimeStrength(int level)
        {foreach(var u in config.Upgrades)if(u.Kind=="SlimeBottle")return u.StatusStrength(level);return 0;}
        /// <summary>卡片读取真实粘液减速等级，使用与区域相同的权威配置。</summary>
        public bool TryGetSlimeUpgradeStrength(int id,out float current,out float next)
        {current=next=0;if(!TryGetWeaponUpgrade(id,out var kind,out var level,out _,out _)||kind!="SlimeBottle")return false;current=level==0?0:SlimeStrength(level);next=SlimeStrength(level+1);return true;}
        /// <summary>按实时位置读取区域强度，离开立即恢复；多个区域只取最强。</summary>
        private float SlimeMovementFraction(ArcadeEnemyRuntime enemy)
        {
            float strength=0;
            foreach(var z in slimeBottleZones)
                if(z.Age<z.Level.AreaDurationSeconds&&(enemy.Position-z.Position).sqrMagnitude<=z.Level.AreaRadius*z.Level.AreaRadius&&!Blocked(z.Position,enemy.Position)&&CanAimGroundBody(enemy,z.Position,.75f))
                    strength=Mathf.Max(strength,z.SlowFraction);
            return strength;
        }
        /// <summary>退出、重开和销毁明确回收在途瓶子与粘液区。</summary>
        private void ClearSlimeBottle()
        {
            foreach(var f in slimeBottleFlights)view?.SetSurvivorSlimeBottle(f.Id,f.Target,0,0,false);
            foreach(var z in slimeBottleZones)view?.SetSurvivorSlimeZone(z.Id,z.Position,z.Level.AreaRadius,z.Age,false);
            slimeBottleFlights.Clear();slimeBottleZones.Clear();slimeBottleClock=0;
            Model.MolotovCooldownRemaining=Model.MolotovCooldownDuration=0;

        }
    }
}
