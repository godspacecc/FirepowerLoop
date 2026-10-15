using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>只显示当前手持火把的火焰与照明；燃烧资格、余量和存档由手持领域管理。</summary>
    public sealed class HandheldTorchView : MonoBehaviour
    {
        [SerializeField] private GameObject burningRoot;
        /// <summary>动画采样后抬起主手，保持火焰在身体前方；只修改这一帧的骨骼表现。</summary>
        public void ApplyGrip(Animator animator, Quaternion gripRotation)
        {
            if (animator == null || !animator.isHuman) return;
            var upper=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var lower=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
            var chest=animator.GetBoneTransform(HumanBodyBones.Chest);
            if (upper==null || lower==null || hand==null || chest==null) return;
            var forward=animator.transform.forward;var right=animator.transform.right;
            var target=chest.position+forward*.35f+right*.28f-Vector3.up*.12f;
            var a=Vector3.Distance(upper.position,lower.position);var b=Vector3.Distance(lower.position,hand.position);
            var offset=target-upper.position;
            if (offset.sqrMagnitude<.000001f || a<=0f || b<=0f) return;
            var distance=Mathf.Clamp(offset.magnitude,.0001f,a+b-.0001f);var direction=offset.normalized;
            var bend=Vector3.ProjectOnPlane(-Vector3.up+right*.35f,direction).normalized;
            var along=(a*a-b*b+distance*distance)/(2f*distance);
            var elbow=upper.position+direction*along+bend*Mathf.Sqrt(Mathf.Max(0f,a*a-along*along));
            upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,elbow-upper.position)*upper.rotation;
            lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,target-lower.position)*lower.rotation;
            hand.rotation=Quaternion.LookRotation(forward,Vector3.up)*Quaternion.Inverse(gripRotation);
        }
        private void OnEnable()
        {
            if (burningRoot != null) burningRoot.SetActive(false);
            if (!Application.isPlaying) return;
        }
        private void OnDisable()
        {
            if (burningRoot != null) burningRoot.SetActive(false);
        }
    }
}
