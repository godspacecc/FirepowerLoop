using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game
{
    public sealed partial class PlayerView
    {
        private readonly ArcadeHerdImpactVisual herdVisual=new ArcadeHerdImpactVisual();
        private ArcadeHerdImpactState herdImpact;private Vector3 herdDirection;
        /// <summary>只保留当前权威姿态；加载、暂停或重连不在View内重新计时。</summary>
        public void SetHerdImpact(ArcadeHerdImpactState state,Vector3 worldDirection)
        {herdImpact=state;herdDirection=worldDirection;herdVisual.SetAnimation(animator,state.Active);if(!state.Active)herdVisual.Restore();}
        private bool ApplyHerdImpact()
        {
            if(!presentationAlive||!herdImpact.Active){herdVisual.SetAnimation(null,false);herdVisual.Restore();return false;}
            herdVisual.Apply(animator!=null?animator.transform:null,herdImpact,herdDirection);return true;
        }
        private void ResetHerdImpact(){herdVisual.SetAnimation(null,false);herdVisual.Restore();herdImpact=default;}
    }
}
