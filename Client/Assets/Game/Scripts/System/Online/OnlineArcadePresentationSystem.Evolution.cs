using System.Linq;
using UnityEngine;
namespace Game
{
    public sealed partial class OnlineArcadePresentationSystem
    {
        /// <summary>与支援共用本帧资源同步事务；只投影所在区域的权威残火和进化弹体。</summary>
        private void SyncWeaponEvolutionVisuals(OnlineArcadeBattleSnapshot battle,bool inRoom)
        {
            if(inRoom)return;
            foreach(var area in battle.ContentAreas)
                if(area.Kind==31)view.SetWeaponBurnArea(area.Id,new Vector2(area.X,area.Y),area.Radius,area.RemainingSeconds);
            foreach(var p in battle.Projectiles)
            {
                if(p.OwnerPlayerId<=0)continue;
                int id=MapId(projectileIds,p.RuntimeId);var position=new Vector2(p.PositionX,p.PositionZ);var direction=new Vector2(p.VelocityX,p.VelocityZ);
                if(p.EvolutionHitRadius>0)view.SetEvolvedPiercing(id,position,direction,p.EvolutionHitRadius);
                else if(p.SourceWeaponId=="arcade.weapon.grenade"&&p.WeaponPowerLevel==2)
                {
                    float duration=p.EvolutionClusterChild?catalog.Rule.WeaponUpgrade.Evolution.ClusterSeconds:
                        catalog.Weapons.First(w=>w.Id==p.SourceWeaponId).ProjectileLifetimeSeconds;
                    view.SetEvolvedGrenade(id,position,direction,p.EvolutionClusterChild,1-p.RemainingSeconds/duration);
                }
            }
        }
    }
}
