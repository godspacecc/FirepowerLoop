using UnityEngine;

namespace Game
{
    /// <summary>
    /// 指定一个正式控制器的三个可替换槽位及空手动作。
    /// 只描述 Animator 接线，不拥有控制器资产或角色实例。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Character Animation Profile")]
    public sealed class CharacterAnimationProfile : ScriptableObject
    {
        [SerializeField] private RuntimeAnimatorController baseController;
        [SerializeField] private AnimationClip idleSlot;
        [SerializeField] private AnimationClip moveSlot;
        [SerializeField] private AnimationClip attackSlot;
        [SerializeField] private CharacterAnimationSet unarmedAnimations = new CharacterAnimationSet();
        /// <summary>徒手向前加速时使用的循环片段；未配置时沿用前进走路。</summary>
        [SerializeField] private AnimationClip unarmedRunForward;
        /// <summary>步枪换弹与收尾拉栓片段；只决定表现顺序，不改变领域计时。</summary>
        [SerializeField] private CharacterAnimationDefinition rifleReload;
        [SerializeField] private CharacterAnimationDefinition riflePullBolt;

        public RuntimeAnimatorController BaseController => baseController;
        public AnimationClip IdleSlot => idleSlot;
        public AnimationClip MoveSlot => moveSlot;
        public AnimationClip AttackSlot => attackSlot;
        public CharacterAnimationSet UnarmedAnimations => unarmedAnimations;
        public AnimationClip UnarmedRunForward => unarmedRunForward;
        public CharacterAnimationDefinition RifleReload => rifleReload;
        public CharacterAnimationDefinition RiflePullBolt => riflePullBolt;
    }
}
