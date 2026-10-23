using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game
{
    public sealed partial class EnemyView
    {
        /// <summary>缓存本实例骨架及上一帧叠加，回池显式复位。</summary>
        private readonly ArcadeZombieGrabVisual meleeVisual=new ArcadeZombieGrabVisual();
        /// <summary>权威阶段仅采样；本地只推进取消后的撤手过渡。</summary>
        private float meleeDuration,meleeRemaining,meleeRecovery,meleeCancelRemaining,meleeCancelPose;
        /// <summary>已接管的空手抓挠禁止命中后旧Attack触发器重新起手。</summary>
        private bool meleeAuthorityEnabled;
        private const float MeleeCancelSeconds=.2f;

        /// <summary>只消费本帧权威阶段；首次快照和重连直接采样当前位置，不补播旧攻击。</summary>
        public void SetMeleeAttack(float duration,float remaining,float recovery,Vector3 facing)
        {
            if(animationDead||herdImpact.Active){ResetMeleeAttack();return;}
            if(!meleeVisual.Bind(transform,animator))return;
            remaining=Mathf.Max(0,remaining);recovery=Mathf.Max(0,recovery);
            if(meleeRemaining>0 && remaining<=0 && recovery<=0)
            {meleeCancelPose=Mathf.Max(0,meleeDuration-meleeRemaining);meleeCancelRemaining=MeleeCancelSeconds;}
            if(remaining>0||recovery>0)
            {
                meleeAuthorityEnabled=true;meleeCancelRemaining=0;
                facing.y=0;if(facing.sqrMagnitude>.0001f)transform.rotation=Quaternion.LookRotation(facing.normalized,transform.up);
                if(animator!=null)animator.ResetTrigger(AttackAnimatorParameter);
            }
            meleeDuration=Mathf.Max(0,duration);meleeRemaining=remaining;meleeRecovery=recovery;
        }
        private void TickMeleeAttack(float seconds)
        {meleeVisual.Restore();meleeCancelRemaining=Mathf.Max(0,meleeCancelRemaining-Mathf.Max(0,seconds));}
        private void ApplyMeleeAttack()
        {
            if(animationDead||herdImpact.Active){meleeVisual.Restore();return;}
            if(meleeRemaining>0)meleeVisual.Apply(Mathf.Max(0,meleeDuration-meleeRemaining));
            else if(meleeRecovery>0)meleeVisual.Apply(meleeDuration+ArcadeMeleeRules.RecoverySeconds-meleeRecovery);
            else if(meleeCancelRemaining>0)meleeVisual.Apply(meleeCancelPose,1-meleeCancelRemaining/MeleeCancelSeconds);
            else meleeVisual.Restore();
        }
        private void ResetMeleeAttack()
        {
            meleeVisual.Clear();meleeDuration=meleeRemaining=meleeRecovery=meleeCancelRemaining=meleeCancelPose=0;
            meleeAuthorityEnabled=false;
        }
    }
}
