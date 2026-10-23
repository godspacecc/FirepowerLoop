using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game
{
    /// <summary>只叠加到模型子根的腾空和倒姿；不写碰撞体、生命、时间或移动权限。</summary>
    public sealed class ArcadeHerdImpactVisual
    {
        private Transform model;private Vector3 position;private Quaternion rotation;private bool applied;
        private Animator frozenAnimator;private float animationSpeed;
        /// <summary>腾空与倒地时保留当前身体姿态，恢复时还原原动画速度，避免空中继续跑步。</summary>
        public void SetAnimation(Animator animator,bool active)
        {
            if(active&&animator!=null)
            {
                if(frozenAnimator==animator)return;
                SetAnimation(null,false);frozenAnimator=animator;animationSpeed=animator.speed;animator.speed=0;
            }
            else if(frozenAnimator!=null){frozenAnimator.speed=animationSpeed;frozenAnimator=null;}
        }
        /// <summary>撤销上一帧叠加，保证复用与重复采样不累积模型偏移。</summary>
        public void Restore()
        {
            if(applied&&model!=null){model.localPosition=position;model.localRotation=rotation;}
            applied=false;
        }
        /// <summary>将权威进度投影到模型子根；尸体只追加尚未完成的飞行路程。</summary>
        public void Apply(Transform target,ArcadeHerdImpactState state,Vector3 worldDirection,bool dead=false,float corpseTravel=0)
        {
            Restore();if(target==null||(!state.Active&&!dead))return;
            model=target;position=target.localPosition;rotation=target.localRotation;
            ArcadeHerdImpactRules.Sample(state.Remaining,dead,out float height,out float tilt);
            var direction=worldDirection.sqrMagnitude>.001f?worldDirection.normalized:Vector3.forward;
            var offset=Vector3.up*height+direction*corpseTravel;
            var axis=Vector3.Cross(Vector3.up,direction);
            if(target.parent!=null){offset=target.parent.InverseTransformVector(offset);axis=target.parent.InverseTransformDirection(axis);}
            target.localPosition=position+offset;target.localRotation=Quaternion.AngleAxis(tilt,axis)*rotation;applied=true;
        }
    }
}
