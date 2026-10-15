using UnityEngine;

namespace Game
{
    /// <summary>接收角色 Animator 自带的表现事件；不负责移动判定、战斗伤害或音效特效。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class CharacterAnimationEventReceiver : MonoBehaviour
    {
        /// <summary>左脚落地时由动画调用；当前仅消除未接收事件的错误。</summary>
        public void FootL()
        {
        }

        /// <summary>右脚落地时由动画调用；当前仅消除未接收事件的错误。</summary>
        public void FootR()
        {
        }

        /// <summary>接收双手剑攻击片段的命中标记；实际伤害由战斗系统按距离和冷却判定。</summary>
        public void Hit()
        {
        }
    }
}
