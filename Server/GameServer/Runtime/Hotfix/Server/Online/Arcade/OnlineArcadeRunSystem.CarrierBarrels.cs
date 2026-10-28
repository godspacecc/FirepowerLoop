using System;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>权威武器命中先扣背桶，破桶后只触发一次既有范围紫爆并保留击杀归属。</summary>
        private static bool TryDamageCarrierBarrel(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime b,OnlineArcadeEnemyRuntime enemy,int damage,long ownerPlayerId)
        {
            if (!enemy.DockSeaEntry.CanBeAttacked) return false;
            if(enemy.SpecialKind!=1||enemy.CurrentHealth<=0||damage<=0||!RoundContentActive(b))return false;
            int health=enemy.CarriedBarrelHealth>0?enemy.CarriedBarrelHealth:b.RoundLayout.BarrelHealth;
            enemy.CarriedBarrelHealth=Math.Max(0,health-damage);
            if(enemy.CarriedBarrelHealth>0)return true;
            float x=enemy.PositionX,y=enemy.PositionZ;enemy.SpecialKind=0;enemy.ContentWarning=false;
            var tuning=ContentTuning(b);
            AddContentZone(b,ArcadeRoundContentRules.BarrelExplosionKind,x,y,x,y,tuning.EventRadius,0,tuning.SkillWarning,0,false,false);
            DamageContentExplosionEnemies(actor,b,x,y,tuning.EventRadius,tuning.EventDamage,ownerPlayerId);
            return true;
        }
    }
}
