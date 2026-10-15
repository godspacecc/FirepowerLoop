using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 五人物共用的武器外观与 Humanoid 动作目录。只描述表现资源和握点，
    /// 不保存伤害、弹药、冷却或装备权威状态。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Character Weapon Catalog")]
    public sealed class CharacterWeaponCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            /// <summary>只覆盖本武器指定动作的挂点姿态；未列出的动作沿用默认挂点。</summary>
            [Serializable]
            public sealed class AnimationPoseOverride
            {
                [Tooltip("从本武器 Animations 中选择要单独调整的动作")]
                [SerializeField] private CharacterAnimationDefinition definition;
                [Tooltip("启用后，此动作使用下方位置；否则沿用本武器默认位置")]
                [SerializeField] private bool overridePosition;
                [Tooltip("该动作下武器相对主手骨骼的位置，单位：米")]
                [SerializeField] private Vector3 localPosition;
                [Tooltip("启用后，此动作使用下方角度；否则沿用本武器默认角度")]
                [SerializeField] private bool overrideRotation;
                [Tooltip("该动作下武器相对主手骨骼的欧拉角，单位：度")]
                [SerializeField] private Vector3 localEulerAngles;
                [Tooltip("启用后，此动作使用下方缩放；否则沿用本武器默认缩放")]
                [SerializeField] private bool overrideScale;
                [Tooltip("该动作下武器相对主手骨骼的缩放；三个分量都要大于 0")]
                [SerializeField] private Vector3 localScale = Vector3.one;

                public CharacterAnimationDefinition Definition => definition;
                public bool OverridePosition => overridePosition;
                public Vector3 LocalPosition => localPosition;
                public bool OverrideRotation => overrideRotation;
                public Quaternion LocalRotation => Quaternion.Euler(localEulerAngles);
                public bool OverrideScale => overrideScale;
                public Vector3 LocalScale => localScale;
            }

            [SerializeField] private string id;
            [SerializeField] private int worldItemId;
            [SerializeField] private string displayName;
            [SerializeField] private GameObject visualSource;
            [SerializeField] private Mesh mesh;
            [SerializeField] private Material material;
            [SerializeField] private HumanBodyBones mainHand = HumanBodyBones.RightHand;
            [SerializeField] private Vector3 handLocalPosition;
            [SerializeField] private Vector3 handLocalEulerAngles;
            [SerializeField] private Vector3 handLocalScale = Vector3.one;
            [Tooltip("可选：每项指定一个动作，勾选要覆盖的位置、旋转或缩放；未勾选时沿用默认姿态")]
            [SerializeField] private AnimationPoseOverride[] animationPoseOverrides =
                Array.Empty<AnimationPoseOverride>();
            [SerializeField] private Vector3 projectileFirePointLocalPosition;
            [SerializeField] private bool hasOffhandGrip;
            [SerializeField] private Vector3 offhandGripLocalPosition;
            [SerializeField] private CharacterAnimationDefinition.Categories category;
            [SerializeField] private bool isProjectile;
            [SerializeField] private bool muzzleFlash;
            [SerializeField] private CharacterAnimationSet animations = new CharacterAnimationSet();
            /// <summary>可选连招表现控制器与按段排序的片段；不储存伤害、冷却或输入缓存。</summary>
            [SerializeField] private RuntimeAnimatorController meleeComboController;
            [SerializeField] private CharacterAnimationDefinition[] meleeCombo = Array.Empty<CharacterAnimationDefinition>();
            public RuntimeAnimatorController MeleeComboController => meleeComboController;
            public CharacterAnimationDefinition[] MeleeCombo => meleeCombo;

            public string Id => id;
            public int WorldItemId => worldItemId;
            public string DisplayName => displayName;
            public GameObject VisualSource => visualSource;
            public Mesh Mesh => mesh;
            public Material Material => material;
            public HumanBodyBones MainHand => mainHand;
            public Vector3 HandLocalPosition => handLocalPosition;
            public Quaternion HandLocalRotation => Quaternion.Euler(handLocalEulerAngles);
            public Vector3 HandLocalScale => handLocalScale;
            public AnimationPoseOverride[] AnimationPoseOverrides => animationPoseOverrides;
            /// <summary>投射武器枪口在共享模型根节点的局部米坐标；近战武器不用。</summary>
            public Vector3 ProjectileFirePointLocalPosition => projectileFirePointLocalPosition;
            public bool HasOffhandGrip => hasOffhandGrip;
            public Vector3 OffhandGripLocalPosition => offhandGripLocalPosition;
            public CharacterAnimationDefinition.Categories Category => category;
            public bool IsProjectile => isProjectile;
            public bool MuzzleFlash => muzzleFlash;
            public CharacterAnimationSet Animations => animations;
            public bool HasVisual => visualSource != null || mesh != null && material != null;

            /// <summary>按动作资源身份解析本武器姿态；未配置覆盖时使用原有默认值。</summary>
            public void GetPose(CharacterAnimationDefinition definition, out Vector3 position,
                out Quaternion rotation, out Vector3 scale)
            {
                position = handLocalPosition;
                rotation = HandLocalRotation;
                scale = handLocalScale;
                if (definition == null || animationPoseOverrides == null) return;
                foreach (var pose in animationPoseOverrides)
                {
                    if (pose == null || pose.Definition != definition) continue;
                    if (pose.OverridePosition) position = pose.LocalPosition;
                    if (pose.OverrideRotation) rotation = pose.LocalRotation;
                    if (pose.OverrideScale) scale = pose.LocalScale;
                    return;
                }
            }

            /// <summary>正式 Animator 用实际片段查找动作，复用造型库的同一组覆盖值。</summary>
            public void GetPose(AnimationClip clip, out Vector3 position,
                out Quaternion rotation, out Vector3 scale)
            {
                GetPose(FindAnimation(clip), out position, out rotation, out scale);
            }

            /// <summary>用实际片段解析动作身份，供正式采样和本机覆盖共同使用。</summary>
            public CharacterAnimationDefinition FindAnimation(AnimationClip clip)
            {
                CharacterAnimationDefinition match = null;
                if (clip != null && animations != null)
                {
                    if (animations.Idle?.Clip == clip) match = animations.Idle;
                    else if (animations.MoveForward?.Clip == clip) match = animations.MoveForward;
                    else if (animations.MoveBackward?.Clip == clip) match = animations.MoveBackward;
                    else if (animations.MoveLeft?.Clip == clip) match = animations.MoveLeft;
                    else if (animations.MoveRight?.Clip == clip) match = animations.MoveRight;
                    else if (animations.Attack?.Clip == clip) match = animations.Attack;
                    if (match == null && animations.Extras != null)
                        foreach (var definition in animations.Extras)
                            if (definition?.Clip == clip)
                            {
                                match = definition;
                                break;
                            }
                }
                if (match == null && clip != null && meleeCombo != null)
                    foreach (var definition in meleeCombo)
                        if (definition?.Clip == clip) return definition;
                return match;
            }

            /// <summary>按动作资产名查找持久化身份；空身份表示默认姿态。</summary>
            public CharacterAnimationDefinition FindAnimation(string identity)
            {
                if (string.IsNullOrEmpty(identity) || animations == null) return null;
                var definitions = new[] { animations.Idle, animations.MoveForward, animations.MoveBackward,
                    animations.MoveLeft, animations.MoveRight, animations.Attack };
                foreach (var definition in definitions)
                    if (definition != null && definition.name == identity) return definition;
                if (animations.Extras != null)
                    foreach (var definition in animations.Extras)
                        if (definition != null && definition.name == identity) return definition;
                if (meleeCombo != null)
                    foreach (var definition in meleeCombo)
                        if (definition != null && definition.name == identity) return definition;
                return null;
            }
        }

        [SerializeField] private Entry[] weapons = Array.Empty<Entry>();

        public int Count => weapons?.Length ?? 0;
        public Entry Get(int index) => index >= 0 && index < Count ? weapons[index] : null;

        /// <summary>检查目录稳定身份；0 是可重复的纯预览物品 ID，正数只能映射一次。</summary>
        public bool ValidateIdentities(out string error)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var items = new HashSet<int>();
            for (var index = 0; index < Count; index++)
            {
                var entry = weapons[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id))
                {
                    error = $"共享武器目录第 {index} 项的 ID 为空或重复";
                    return false;
                }
                if (entry.WorldItemId < 0 || entry.WorldItemId > 0 && !items.Add(entry.WorldItemId))
                {
                    error = $"共享武器目录 {entry.Id} 的普通物品 ID 非法或重复";
                    return false;
                }
            }
            error = null;
            return true;
        }

        /// <summary>资源身份必须唯一；未知武器返回空值，不回退到某个角色的默认武器。</summary>
        public Entry Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (!ValidateIdentities(out _)) return null;
            foreach (var weapon in weapons)
                if (weapon != null && string.Equals(weapon.Id, id, StringComparison.Ordinal)) return weapon;
            return null;
        }

        /// <summary>普通地图装备仍以 Luban 物品 ID 为权威，只有显式映射的武器参与表现。</summary>
        public Entry FindByWorldItemId(int itemId)
        {
            if (itemId <= 0) return null;
            if (!ValidateIdentities(out _)) return null;
            foreach (var weapon in weapons)
                if (weapon != null && weapon.WorldItemId == itemId) return weapon;
            return null;
        }
    }
}
