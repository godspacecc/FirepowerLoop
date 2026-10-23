using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>仅在诱饵／环绕桶原有真实触发点增加已采用的普通爆心，原伤害和次数仍由调用方管理。</summary>
        private void DamageEnemiesFromSupportExplosion(ArcadeCombatRuntime runtime,Vector2 center,float radius,int damage)
        {
            if(radius>0)view?.PlayProjectileExplosion(center,radius);
            DamageEnemiesInExplosionRadius(runtime,center,radius,damage);
        }
    }
}
