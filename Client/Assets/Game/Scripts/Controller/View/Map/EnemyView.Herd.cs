using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game
{
    public sealed partial class EnemyView
    {
        private readonly ArcadeHerdImpactVisual herdVisual=new ArcadeHerdImpactVisual();
        private ArcadeHerdImpactState herdImpact;private Vector3 herdDirection;
        /// <summary>存活敌人只投影本帧权威牛撞状态；死亡后由尸体拥有者消费死亡类别。</summary>
        public void SetHerdImpact(ArcadeHerdImpactState state,Vector3 direction)
        {herdImpact=state;herdDirection=direction;herdVisual.SetAnimation(animator,state.Active&&!animationDead);if(!state.Active)herdVisual.Restore();}
        private void ApplyHerdImpact(){if(!animationDead)herdVisual.Apply(animator!=null?animator.transform:null,herdImpact,herdDirection);}
        private void ResetHerdImpact(){herdVisual.SetAnimation(null,false);herdVisual.Restore();herdImpact=default;}
    }
}
