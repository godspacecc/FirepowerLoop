using System;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 单个 Humanoid 片段的表现身份与适用范围。只描述预览和握持条件，
    /// 不决定攻击、移动或装备的玩法规则。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Character Animation Definition")]
    public sealed class CharacterAnimationDefinition : ScriptableObject
    {
        [Flags]
        public enum Categories
        {
            None = 0,
            Unarmed = 1 << 0,
            OneHandMelee = 1 << 1,
            TwoHandMelee = 1 << 2,
            Bow = 1 << 3,
            Pistol = 1 << 4,
            Rifle = 1 << 5,
            Launcher = 1 << 6,
            Throwing = 1 << 7
        }

        [SerializeField] private AnimationClip clip;
        [SerializeField] private string displayName;
        [SerializeField] private Categories compatibleCategories;
        [SerializeField] private HumanBodyBones[] requiredBones = Array.Empty<HumanBodyBones>();
        [SerializeField] private bool allowOffhandGrip;
        /// <summary>单次攻击的默认表现秒数；0 沿用原速，显式街机间隔优先，不参与冷却或命中。</summary>
        [SerializeField, Min(0f)] private float singleAttackDurationSeconds;

        public AnimationClip Clip => clip;
        public string DisplayName => displayName;
        public Categories CompatibleCategories => compatibleCategories;
        public HumanBodyBones[] RequiredBones => requiredBones;
        public bool AllowOffhandGrip => allowOffhandGrip;
        public float SingleAttackDurationSeconds => singleAttackDurationSeconds;
    }

    /// <summary>一组基础动作与额外预览动作；集合本身不驱动 Animator 状态机。</summary>
    [Serializable]
    public sealed class CharacterAnimationSet
    {
        [SerializeField] private CharacterAnimationDefinition idle;
        [SerializeField] private CharacterAnimationDefinition moveForward;
        [SerializeField] private CharacterAnimationDefinition moveBackward;
        [SerializeField] private CharacterAnimationDefinition moveLeft;
        [SerializeField] private CharacterAnimationDefinition moveRight;
        [SerializeField] private CharacterAnimationDefinition attack;
        [SerializeField] private CharacterAnimationDefinition[] extras = Array.Empty<CharacterAnimationDefinition>();

        public CharacterAnimationDefinition Idle => idle;
        public CharacterAnimationDefinition MoveForward => moveForward;
        public CharacterAnimationDefinition MoveBackward => moveBackward;
        public CharacterAnimationDefinition MoveLeft => moveLeft;
        public CharacterAnimationDefinition MoveRight => moveRight;
        public CharacterAnimationDefinition Attack => attack;
        public CharacterAnimationDefinition[] Extras => extras;
    }
}
