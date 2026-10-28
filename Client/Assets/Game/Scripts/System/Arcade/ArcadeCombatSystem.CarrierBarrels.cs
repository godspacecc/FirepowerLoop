using System;
using FirepowerLoop.Arcade;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>武器先消耗背桶耐久；破桶先清身份再结算原有紫爆，溢出弹伤不穿给载体。</summary>
        private bool TryDamageCarrierBarrel(ArcadeCombatRuntime r,ArcadeEnemyRuntime enemy,int damage)
        {
            if (!enemy.DockSeaEntry.CanBeAttacked) return false;
            if(enemy.ContentSpecialKind!=1||enemy.Health<=0||damage<=0||!RoundContentActive(r))return false;
            int health=enemy.CarriedBarrelHealth>0?enemy.CarriedBarrelHealth:RoundLayout(r).BarrelHealth;
            enemy.CarriedBarrelHealth=Math.Max(0,health-damage);
            if(enemy.CarriedBarrelHealth>0)return true;
            var position=enemy.Position;enemy.ContentSpecialKind=0;enemy.ContentWarning=false;
            var tuning=ContentTuning(r);
            AddContentArea(r,ArcadeRoundContentRules.BarrelExplosionKind,position,position,tuning.EventRadius,0,tuning.SkillWarning,0,false,false);
            DamageContentExplosionEnemies(r,position,tuning.EventRadius,tuning.EventDamage);
            return true;
        }
    }
}
