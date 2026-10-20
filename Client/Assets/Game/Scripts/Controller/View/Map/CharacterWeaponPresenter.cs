using System.Collections.Generic;
using UnityEngine;
using QFramework;

namespace Game
{
    /// <summary>
    /// 将目录中的武器挂到当前 Humanoid 骨架，并在动画采样后约束副手。
    /// 只处理可见物及骨骼，不决定角色是否拥有武器或攻击是否命中。
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class CharacterWeaponPresenter : MonoBehaviour
    {
        [SerializeField] private CharacterWeaponCatalog catalog;
        [SerializeField] private Animator animator;
        [SerializeField] private CharacterAnimationProfile profile;
        private RuntimeAnimatorController baseAnimatorController;
        /// <summary>首次绑定前的 Animator 控制器；null 也可能是已保存的合法原值。</summary>
        private RuntimeAnimatorController originalAnimatorController;
        private bool originalAnimatorControllerSaved;
        private AnimatorOverrideController weaponAnimatorController;
        private CharacterWeaponCatalog.Entry current;
        private GameObject visual;
        private BowVisualView bowVisual;
        private HandheldTorchView torchVisual;
        /// <summary>当前喷火器喷嘴的短程火焰，随手持模型创建和销毁，不持有伤害状态。</summary>
        private ParticleSystem flamethrowerMuzzle;
        /// <summary>最近一次有效的水平世界位移，单位为米；切换武器后用于立即选对方向。</summary>
        private Vector3 lastHorizontalMotion;
        /// <summary>由移动领域结算传入的实际加速状态；不判断输入或消耗体力。</summary>
        private bool isSprinting;
        private bool manualPoseSampling;
        private WeaponPoseSettingsSystem poseSettings;
        /// <summary>当前换弹阶段，仅供握持姿态解析；动作结束或武器替换时清空。</summary>
        private CharacterAnimationDefinition reloadDefinition;
        /// <summary>共享枪械独立上身层；基础层继续负责行走和近战全身动作。</summary>
        private const string GunFireLayer = "Shared Gun Upper Body";
        private float gunFireRemainingSeconds;

        /// <summary>仅枪械与发射器使用上身射击；弓和投掷动作保留原有完整动作。</summary>
        public bool UsesUpperBodyGunFire => current != null && current.IsProjectile &&
            (current.Category & (CharacterAnimationDefinition.Categories.Pistol |
                                 CharacterAnimationDefinition.Categories.Rifle |
                                 CharacterAnimationDefinition.Categories.Launcher)) != 0;

        /// <summary>每次权威开火只重启上身片段，不触发基础层 Attack，也不改变玩法冷却。</summary>
        public void PlayUpperBodyGunFire()
        {
            if (!UsesUpperBodyGunFire || animator == null || reloadDefinition != null) return;
            BeginArcadeGunKick();
            int layer = animator.GetLayerIndex(GunFireLayer);
            if (layer < 0) return;
            gunFireRemainingSeconds = current.Animations.Attack.Clip.length;
            animator.SetLayerWeight(layer, 1f);
            animator.Play(GunFireLayer + ".Fire", layer, 0f);
        }

        /// <summary>复用本机及远端既有表现帧，动作结束后撤销覆盖，恢复完整移动姿态。</summary>
        public void TickUpperBodyGunFire(float deltaSeconds)
        {
            gunKickRemainingSeconds = Mathf.Max(0f, gunKickRemainingSeconds - Mathf.Max(0f, deltaSeconds));
            if (gunFireRemainingSeconds <= 0f) return;
            gunFireRemainingSeconds = Mathf.Max(0f, gunFireRemainingSeconds - deltaSeconds);
            if (gunFireRemainingSeconds <= 0f) ClearUpperBodyGunFire();
        }

        /// <summary>切装、禁用、受击与死亡共同撤销上身射击，避免旧武器姿态残留。</summary>
        public void ClearUpperBodyGunFire()
        {
            gunKickRemainingSeconds = 0f;
            gunFireRemainingSeconds = 0f;
            // 不可见候选和池化禁用阶段只清本地计时，Animator尚未准备时不查询层。
            if (animator == null || !animator.isActiveAndEnabled || !animator.isInitialized) return;
            int layer = animator.GetLayerIndex(GunFireLayer);
            if (layer < 0) return;
            animator.SetLayerWeight(layer, 0f);
            if (animator.isActiveAndEnabled) animator.Play(GunFireLayer + ".Empty", layer, 0f);
        }

        /// <summary>按照权威换弹进度采样上半身，两段片段按原始时长比例分配现有计时。</summary>
        public void ShowReload(float progress01)
        {
            if (current == null ||
                current.Category != CharacterAnimationDefinition.Categories.Rifle ||
                profile == null || !IsAnimationCompatible(profile.RifleReload, out _) ||
                !IsAnimationCompatible(profile.RiflePullBolt, out _))
            {
                ClearReload();
                return;
            }
            var layer = animator.GetLayerIndex("RifleReload");
            if (layer < 0) return;
            ClearUpperBodyGunFire();
            var reload = profile.RifleReload;
            var bolt = profile.RiflePullBolt;
            var time = Mathf.Clamp01(progress01) * (reload.Clip.length + bolt.Clip.length);
            var isBolt = time >= reload.Clip.length;
            reloadDefinition = isBolt ? bolt : reload;
            animator.SetLayerWeight(layer, 1f);
            animator.Play(isBolt ? "RifleReload.PullBolt" : "RifleReload.Reload", layer,
                Mathf.Clamp01((isBolt ? time - reload.Clip.length : time) / reloadDefinition.Clip.length));
        }

        /// <summary>取消、完成、失活或切换武器时撤销表现层，不写入弹药或动作状态。</summary>
        public void ClearReload()
        {
            reloadDefinition = null;
            if (animator == null || !animator.isActiveAndEnabled || !animator.isInitialized) return;
            var layer = animator.GetLayerIndex("RifleReload");
            if (layer >= 0) animator.SetLayerWeight(layer, 0f);
        }

        private void OnDisable()
        {
            ClearReload();
            ClearUpperBodyGunFire();
        }

        /// <summary>预览和独立测试可注入同一姿态系统；正式实例在运行时使用游戏架构服务。</summary>
        public void ConfigurePoseSettings(WeaponPoseSettingsSystem settings) => poseSettings = settings;

        /// <summary>采样时统一解析资源和本机覆盖；编辑器独立资源检查保持资源值。</summary>
        private void ResolvePose(CharacterAnimationDefinition definition, out Vector3 position,
            out Quaternion rotation, out Vector3 scale)
        {
            if (poseSettings == null && Application.isPlaying)
                poseSettings = Game.Interface.GetSystem<WeaponPoseSettingsSystem>();
            if (poseSettings != null) poseSettings.Resolve(current, definition, out position, out rotation, out scale);
            else current.GetPose(definition, out position, out rotation, out scale);
        }

        public GameObject Visual => visual;
        public CharacterWeaponCatalog.Entry Current => current;
        public CharacterWeaponCatalog Catalog => catalog;
        public Animator BodyAnimator => animator;
        public CharacterAnimationProfile Profile => profile;
        /// <summary>最近一次绑定失败的原因；成功切换后清空。</summary>
        public string LastError { get; private set; }

        private void Awake()
        {
            SaveOriginalAnimatorControllerOnce();
        }

        /// <summary>隐藏实例可先于 Awake 绑定武器，因此首次改写 Animator 前只保存一次原值。</summary>
        private void SaveOriginalAnimatorControllerOnce()
        {
            if (originalAnimatorControllerSaved) return;
            originalAnimatorController = animator != null ? animator.runtimeAnimatorController : null;
            originalAnimatorControllerSaved = true;
        }

        /// <summary>只从当前有效投射武器的模型计算枪口；空手和失败不暴露旧位置。</summary>
        public bool TryGetProjectileOrigin(out Vector3 origin)
        {
            origin = default;
            if (current == null || !current.IsProjectile || visual == null) return false;
            if (!manualPoseSampling) ApplyCurrentAnimationPose();
            origin = visual.transform.TransformPoint(current.ProjectileFirePointLocalPosition);
            return true;
        }

        /// <summary>正式战斗按稳定 ID 选武器；未知身份清空旧外观并报告失败。</summary>
        public bool SetWeaponId(string id)
        {
            SaveOriginalAnimatorControllerOnce();
            if (string.IsNullOrEmpty(id)) return SetWeapon(null);
            if (catalog != null && !catalog.ValidateIdentities(out var catalogError))
            {
                SetWeapon(null);
                LastError = catalogError;
                return false;
            }
            var entry = catalog != null ? catalog.Find(id) : null;
            if (entry != null) return SetWeapon(entry);
            SetWeapon(null);
            LastError = $"共享目录未找到武器 ID：{id}";
            return false;
        }

        /// <summary>切换外观时销毁旧实例；缺少骨骼或资源时保持无武器并返回失败。</summary>
        public bool SetWeapon(CharacterWeaponCatalog.Entry entry)
        {
            SaveOriginalAnimatorControllerOnce();
            LastError = null;
            if (entry != null && ReferenceEquals(current, entry) && visual != null)
            {
                if (animator != null && animator.runtimeAnimatorController != weaponAnimatorController)
                    animator.runtimeAnimatorController = weaponAnimatorController;
                return true;
            }
            if (entry == null)
            {
                ClearCurrentVisual();
                RestoreAnimatorController();
                if (weaponAnimatorController != null) return true;
                LastError = "空手动作 Profile 不可用，已恢复基础控制器";
                return false;
            }
            if (!ValidateEntry(entry, out var validationError))
                return FailSwitch(validationError);
            var hand = animator.GetBoneTransform(entry.MainHand);
            GameObject candidate = null;
            AnimatorOverrideController replacement = null;
            try
            {
                // 在不可见父节点中准备模型，任何实例化回调都不能看到半成品武器。
                candidate = new GameObject("HeldWeapon_" + entry.Id);
                candidate.SetActive(false);
                candidate.transform.SetParent(hand, false);
                candidate.transform.localPosition = entry.HandLocalPosition;
                candidate.transform.localRotation = entry.HandLocalRotation;
                candidate.transform.localScale = entry.HandLocalScale;
                GameObject content;
                if (entry.VisualSource != null)
                    content = Instantiate(entry.VisualSource, candidate.transform);
                else
                {
                    content = new GameObject(entry.Id, typeof(MeshFilter), typeof(MeshRenderer));
                    content.transform.SetParent(candidate.transform, false);
                    content.GetComponent<MeshFilter>().sharedMesh = entry.Mesh;
                    content.GetComponent<MeshRenderer>().sharedMaterial = entry.Material;
                }
                content.transform.localPosition = Vector3.zero;
                content.transform.localRotation = Quaternion.identity;
                content.transform.localScale = Vector3.one;
                content.SetActive(true);
                foreach (var child in candidate.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = gameObject.layer;
                foreach (var nestedAnimator in candidate.GetComponentsInChildren<Animator>(true))
                    nestedAnimator.enabled = false;
                foreach (var collider in candidate.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
                replacement = CreateAnimatorController(entry);
                if (replacement == null) throw new System.InvalidOperationException("Profile 槽位替换失败");

                ClearCurrentVisual();
                animator.runtimeAnimatorController = replacement;
                ReleaseWeaponAnimatorController();
                weaponAnimatorController = replacement;
                replacement = null;
                current = entry;
                visual = candidate;
                bowVisual = visual.GetComponentInChildren<BowVisualView>(true);
                torchVisual = visual.GetComponentInChildren<HandheldTorchView>(true);
                flamethrowerMuzzle = entry.Id=="arcade.weapon.flamethrower" ? visual.GetComponentInChildren<ParticleSystem>(true) : null;
                candidate = null;
                visual.SetActive(true);
                SetMovementDirection(lastHorizontalMotion);
                return true;
            }
            catch (System.Exception exception)
            {
                DestroyOwned(candidate);
                if (replacement != null) DestroyOwned(replacement);
                return FailSwitch($"武器 {entry.Id} 创建失败：{exception.Message}");
            }
        }

        /// <summary>造型库预览期间同步当前武器的局部姿态；数值未变时不触碰动画或实例。</summary>
        public bool RefreshCurrentPose(CharacterWeaponCatalog.Entry entry)
        {
            if (entry == null || current == null || visual == null || entry.Id != current.Id ||
                !Finite(entry.HandLocalPosition) || !Finite(entry.HandLocalRotation) ||
                !Finite(entry.HandLocalScale) || entry.HandLocalScale.x <= 0f ||
                entry.HandLocalScale.y <= 0f || entry.HandLocalScale.z <= 0f) return false;
            current = entry;
            var root = visual.transform;
            if (root.localPosition != entry.HandLocalPosition) root.localPosition = entry.HandLocalPosition;
            if (root.localRotation != entry.HandLocalRotation) root.localRotation = entry.HandLocalRotation;
            if (root.localScale != entry.HandLocalScale) root.localScale = entry.HandLocalScale;
            return true;
        }

        private bool FailSwitch(string error)
        {
            ClearCurrentVisual();
            RestoreAnimatorController();
            LastError = error;
            return false;
        }

        private void ClearCurrentVisual()
        {
            ClearUpperBodyGunFire();
            GetComponent<PlayerView>()?.ResetWeaponAttackPresentation(switchingWeapon: true);
            ClearReload();
            current = null;
            bowVisual?.Dispose();
            bowVisual = null;
            torchVisual = null;
            flamethrowerMuzzle = null;
            if (visual == null) return;
            visual.SetActive(false);
            var old = visual;
            visual = null;
            DestroyOwned(old);
        }

        private static void DestroyOwned(Object owned)
        {
            if (owned == null) return;
            if (Application.isPlaying) Destroy(owned);
            else DestroyImmediate(owned);
        }

        /// <summary>权威开火反馈触发喷嘴近端火焰；远端角色复用同一表现入口。</summary>
        public void PlayWeaponMuzzleEffect()
        {
            if(flamethrowerMuzzle==null)return;
            flamethrowerMuzzle.Play(false);
            // 只重置发射窗口，保留已喷出的世界空间火焰；停火后窗口自动结束。
            flamethrowerMuzzle.time=0;
            flamethrowerMuzzle.Emit(2);
        }

        /// <summary>发布候选前检查模型、数值、Avatar、槽位和所有动作的显式兼容性。</summary>
        private bool ValidateEntry(CharacterWeaponCatalog.Entry entry, out string error)
        {
            error = null;
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || !entry.HasVisual)
                error = "武器身份或模型缺失";
            else if (animator == null || animator.avatar == null || !animator.avatar.isValid ||
                     !animator.avatar.isHuman || !animator.isHuman)
                error = $"武器 {entry.Id} 的主体 Humanoid Animator/Avatar 不可用";
            else if (entry.MainHand != HumanBodyBones.LeftHand && entry.MainHand != HumanBodyBones.RightHand)
                error = $"武器 {entry.Id} 的主手必须为左手或右手";
            else if (animator.GetBoneTransform(entry.MainHand) == null)
                error = $"武器 {entry.Id} 缺少主手骨骼";
            else if (!Finite(entry.HandLocalPosition) || !Finite(entry.ProjectileFirePointLocalPosition) ||
                     !Finite(entry.OffhandGripLocalPosition) ||
                     !Finite(entry.HandLocalScale) || entry.HandLocalScale.x <= 0f ||
                     entry.HandLocalScale.y <= 0f || entry.HandLocalScale.z <= 0f ||
                     !Finite(entry.HandLocalRotation))
                error = $"武器 {entry.Id} 的挂载、枪口或缩放数值非法";
            else if (!ValidateAnimationPoses(entry))
                error = $"武器 {entry.Id} 的动画姿态覆盖存在重复动作或非法数值";
            else if (profile == null || profile.BaseController == null || profile.IdleSlot == null ||
                     profile.MoveSlot == null || profile.AttackSlot == null ||
                     profile.IdleSlot == profile.MoveSlot || profile.IdleSlot == profile.AttackSlot ||
                     profile.MoveSlot == profile.AttackSlot)
                error = $"武器 {entry.Id} 的动画 Profile 或槽位非法";
            else if (!ValidateProfileSlots())
                error = $"武器 {entry.Id} 的控制器缺少 Profile 槽位";
            else if (entry.Category == CharacterAnimationDefinition.Categories.None || entry.Animations == null)
                error = $"武器 {entry.Id} 缺少类别或动作集合";
            else
            {
                var set = entry.Animations;
                foreach (var definition in new[]
                         {
                             set.Idle, set.MoveForward, set.MoveBackward,
                             set.MoveLeft, set.MoveRight, set.Attack
                         })
                    if (!ValidateDefinition(definition, entry.Category, out error))
                    {
                        error = $"武器 {entry.Id} 的动作不可用：{error}";
                        break;
                    }
                if (error == null && set.Extras != null)
                    foreach (var definition in set.Extras)
                        if (!ValidateDefinition(definition, entry.Category, out error))
                        {
                            error = $"武器 {entry.Id} 的额外动作不可用：{error}";
                            break;
                        }
                if (error == null && entry.HasOffhandGrip)
                {
                    var opposite = entry.MainHand == HumanBodyBones.LeftHand
                        ? new[] { HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand }
                        : new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand };
                    foreach (var bone in opposite)
                        if (animator.GetBoneTransform(bone) == null)
                        {
                            error = $"武器 {entry.Id} 缺少副手骨骼 {bone}";
                            break;
                        }
                }
            }
            return error == null;
        }

        private bool ValidateProfileSlots()
        {
            var clips = new HashSet<AnimationClip>(profile.BaseController.animationClips);
            return clips.Contains(profile.IdleSlot) && clips.Contains(profile.MoveSlot) &&
                   clips.Contains(profile.AttackSlot);
        }

        /// <summary>覆盖项以动作资源为唯一键；拒绝无效缩放，避免运行中只在某个动作失效。</summary>
        private static bool ValidateAnimationPoses(CharacterWeaponCatalog.Entry entry)
        {
            var seen = new HashSet<CharacterAnimationDefinition>();
            if (entry.AnimationPoseOverrides == null) return true;
            foreach (var pose in entry.AnimationPoseOverrides)
                if (pose == null || pose.Definition == null || !seen.Add(pose.Definition) ||
                    (pose.OverridePosition && !Finite(pose.LocalPosition)) ||
                    (pose.OverrideRotation && !Finite(pose.LocalRotation)) ||
                    (pose.OverrideScale && (!Finite(pose.LocalScale) || pose.LocalScale.x <= 0f ||
                                            pose.LocalScale.y <= 0f || pose.LocalScale.z <= 0f)))
                    return false;
            return true;
        }

        private bool ValidateDefinition(CharacterAnimationDefinition definition,
            CharacterAnimationDefinition.Categories category, out string error)
        {
            error = null;
            if (definition == null || definition.Clip == null || !definition.Clip.humanMotion ||
                definition.Clip.length <= 0f)
                error = "片段缺失、时长为零或不是 Humanoid";
            else if ((definition.CompatibleCategories & category) == 0)
                error = $"{definition.name} 与类别 {category} 不兼容";
            else if (definition.RequiredBones != null)
                foreach (var bone in definition.RequiredBones)
                    if (animator.GetBoneTransform(bone) == null)
                    {
                        error = $"{definition.name} 缺少骨骼 {bone}";
                        break;
                    }
            return error == null;
        }

        /// <summary>造型库按当前骨架与显式类别判断片段，不使用动作名或玩法获取规则。</summary>
        public bool IsAnimationCompatible(CharacterAnimationDefinition definition, out string error)
        {
            if (animator == null || animator.avatar == null || !animator.avatar.isValid ||
                !animator.avatar.isHuman || !animator.isHuman)
            {
                error = "当前模型不是有效 Humanoid";
                return false;
            }
            return ValidateDefinition(definition, current != null
                ? current.Category : CharacterAnimationDefinition.Categories.Unarmed, out error);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) &&
                                                       Finite(value.z) && Finite(value.w);

        /// <summary>实际加速状态变化后重选片段；相同状态不重复刷新控制器。</summary>
        public void SetSprinting(bool value)
        {
            if (isSprinting == value) return;
            isSprinting = value;
            SetMovementDirection(lastHorizontalMotion);
        }

        /// <summary>位移和角色朝向均确定后调用；徒手向前加速使用跑步，停止时由 MoveSpeed 控制待机。</summary>
        public void SetMovementDirection(Vector3 worldDisplacement)
        {
            lastHorizontalMotion = new Vector3(worldDisplacement.x, 0f, worldDisplacement.z);
            if (profile == null || weaponAnimatorController == null) return;
            var actions = current != null ? current.Animations : profile.UnarmedAnimations;
            if (actions == null) return;
            var local = transform.InverseTransformDirection(lastHorizontalMotion);
            var movement = actions.MoveForward;
            if (lastHorizontalMotion.sqrMagnitude > 0.000001f)
            {
                movement = Mathf.Abs(local.z) >= Mathf.Abs(local.x)
                    ? local.z >= 0f ? actions.MoveForward : actions.MoveBackward
                    : local.x >= 0f ? actions.MoveRight : actions.MoveLeft;
            }
            var clip = movement?.Clip;
            if (current == null && isSprinting && local.z > 0f &&
                Mathf.Abs(local.z) >= Mathf.Abs(local.x) && profile.UnarmedRunForward != null)
                clip = profile.UnarmedRunForward;
            if (clip != null && profile.MoveSlot != null && weaponAnimatorController[profile.MoveSlot] != clip)
                weaponAnimatorController[profile.MoveSlot] = clip;
        }

        /// <summary>按 Profile 的槽位引用替换动作，不从原片段名称推断状态机。</summary>
        private AnimatorOverrideController CreateAnimatorController(CharacterWeaponCatalog.Entry entry)
        {
            if (profile == null || profile.BaseController == null || profile.IdleSlot == null ||
                profile.MoveSlot == null || profile.AttackSlot == null) return null;
            baseAnimatorController = entry.MeleeComboController != null ? entry.MeleeComboController : profile.BaseController;
            var replacement = new AnimatorOverrideController(baseAnimatorController);
            var clips = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            replacement.GetOverrides(clips);
            var foundIdle = false;
            var foundMove = false;
            var foundAttack = false;
            for (var index = 0; index < clips.Count; index++)
            {
                var original = clips[index].Key;
                AnimationClip shared = null;
                if (original == profile.IdleSlot)
                {
                    shared = entry.Animations.Idle.Clip;
                    foundIdle = true;
                }
                else if (original == profile.MoveSlot)
                {
                    shared = entry.Animations.MoveForward.Clip;
                    foundMove = true;
                }
                else if (original == profile.AttackSlot)
                {
                    shared = entry.Animations.Attack.Clip;
                    foundAttack = true;
                }
                if (shared != null) clips[index] = new KeyValuePair<AnimationClip, AnimationClip>(original, shared);
            }
            if (!foundIdle || !foundMove || !foundAttack)
            {
                DestroyOwned(replacement);
                return null;
            }
            replacement.ApplyOverrides(clips);
            return replacement;
        }

        private void RestoreAnimatorController()
        {
            SaveOriginalAnimatorControllerOnce();
            if (animator != null)
                animator.runtimeAnimatorController = originalAnimatorController;
            ReleaseWeaponAnimatorController();
            if (animator == null || profile == null || profile.BaseController == null ||
                profile.IdleSlot == null || profile.MoveSlot == null || profile.AttackSlot == null ||
                profile.IdleSlot == profile.MoveSlot || profile.IdleSlot == profile.AttackSlot ||
                profile.MoveSlot == profile.AttackSlot || !ValidateProfileSlots() ||
                profile.UnarmedAnimations == null) return;
            var unarmed = profile.UnarmedAnimations;
            foreach (var definition in new[]
                     {
                         unarmed.Idle, unarmed.MoveForward, unarmed.MoveBackward,
                         unarmed.MoveLeft, unarmed.MoveRight
                     })
                if (!ValidateDefinition(definition, CharacterAnimationDefinition.Categories.Unarmed, out _))
                    return;
            baseAnimatorController = profile.BaseController;
            var replacement = new AnimatorOverrideController(baseAnimatorController);
            replacement[profile.IdleSlot] = unarmed.Idle.Clip;
            replacement[profile.MoveSlot] = unarmed.MoveForward.Clip;
            animator.runtimeAnimatorController = replacement;
            weaponAnimatorController = replacement;
            SetMovementDirection(lastHorizontalMotion);
        }

        private void ReleaseWeaponAnimatorController()
        {
            if (weaponAnimatorController == null) return;
            if (Application.isPlaying) Destroy(weaponAnimatorController);
            else DestroyImmediate(weaponAnimatorController);
            weaponAnimatorController = null;
        }

        private void LateUpdate()
        {
            if(GetComponent<PlayerView>()?.SwimmingPresentation==true)return;
            if (visual == null || manualPoseSampling) return;
            ApplyOffhandGrip();
        }

        private void OnDestroy()
        {
            ClearCurrentVisual();
            if (animator != null && weaponAnimatorController != null)
                animator.runtimeAnimatorController = originalAnimatorController;
            ReleaseWeaponAnimatorController();
        }

        /// <summary>造型库手动采样时关闭自动 LateUpdate；调用方在求值后显式握持一次。</summary>
        public void SetManualPoseSampling(bool enabled) => manualPoseSampling = enabled;

        public void ApplyOffhandGrip(CharacterAnimationDefinition definition, float normalizedTime = 0f)
        {
            ApplyAnimationPose(definition);
            if (torchVisual != null) torchVisual.ApplyGrip(animator, current.HandLocalRotation);
            RefreshBow(definition == current?.Animations.Attack ? BowVisualView.AttackDrawWeight(normalizedTime) : 1f);
            if (definition == null || !definition.AllowOffhandGrip) return;
            ApplyOffhandGripCore();
        }

        /// <summary>Animator 完成一次求值后调用；不累计骨骼变换到下一帧。</summary>
        public void ApplyOffhandGrip()
        {
            if (reloadDefinition != null)
            {
                ApplyAnimationPose(reloadDefinition);
                return;
            }
            ApplyCurrentAnimationPose();
            if (torchVisual != null) torchVisual.ApplyGrip(animator, current.HandLocalRotation);
            RefreshBow(CurrentBowDrawWeight());
            if (!CanGripCurrentAnimation()) return;
            ApplyOffhandGripCore();
        }

        /// <summary>沿用角色动画的姿态与进度，只刷新当前弓实例，其他武器没有此表现。</summary>
        private void RefreshBow(float drawWeight)
        {
            if (bowVisual == null || current == null || animator == null) return;
            var bone = current.MainHand == HumanBodyBones.LeftHand ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand;
            bowVisual.Refresh(animator.GetBoneTransform(bone), drawWeight);
        }

        private float CurrentBowDrawWeight()
        {
            if (bowVisual == null || animator == null) return 0f;
            float sum = 0f, weights = 0f;
            Accumulate(animator.GetCurrentAnimatorClipInfo(0), animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
            if (animator.IsInTransition(0))
                Accumulate(animator.GetNextAnimatorClipInfo(0), animator.GetNextAnimatorStateInfo(0).normalizedTime);
            return weights > .0001f ? sum / weights : 0f;

            void Accumulate(AnimatorClipInfo[] infos, float time)
            {
                foreach (var info in infos)
                {
                    var definition = current.FindAnimation(info.clip);
                    var draw = definition == null ? 0f : definition == current.Animations.Attack
                        ? BowVisualView.AttackDrawWeight(time) : 1f;
                    sum += draw * info.weight;
                    weights += info.weight;
                }
            }
        }

        /// <summary>造型库手动采样后按当前选中的动作同步挂点，Inspector 改值下一帧可见。</summary>
        private void ApplyAnimationPose(CharacterAnimationDefinition definition)
        {
            if (current == null || visual == null) return;
            ResolvePose(definition, out var position, out var rotation, out var scale);
            SetVisualPose(position, rotation, scale);
        }

        /// <summary>优先使用活动射击层的片段解析挂点，其余时刻按基础层动作混合。</summary>
        private void ApplyCurrentAnimationPose()
        {
            if (reloadDefinition != null)
            {
                ApplyAnimationPose(reloadDefinition);
                return;
            }
            if (current == null || visual == null || animator == null || !animator.isActiveAndEnabled)
                return;
            var positionSum = Vector3.zero;
            var scaleSum = Vector3.zero;
            var rotation = current.HandLocalRotation;
            var weightSum = 0f;
            int gunLayer = animator.GetLayerIndex(GunFireLayer);
            int poseLayer = gunLayer >= 0 && gunFireRemainingSeconds > 0f && animator.GetLayerWeight(gunLayer) > 0f
                ? gunLayer : 0;
            Accumulate(animator.GetCurrentAnimatorClipInfo(poseLayer));
            if (animator.IsInTransition(poseLayer)) Accumulate(animator.GetNextAnimatorClipInfo(poseLayer));
            if (weightSum <= 0.0001f)
            {
                ApplyAnimationPose(null);
                return;
            }
            SetVisualPose(positionSum / weightSum, rotation, scaleSum / weightSum);
            AlignFlamethrowerMainHand();

            void Accumulate(AnimatorClipInfo[] infos)
            {
                foreach (var info in infos)
                {
                    if (info.clip == null || info.weight <= 0.0001f) continue;
                    ResolvePose(current.FindAnimation(info.clip), out var position, out var clipRotation, out var scale);
                    positionSum += position * info.weight;
                    scaleSum += scale * info.weight;
                    rotation = weightSum <= 0f ? clipRotation :
                        Quaternion.Slerp(rotation, clipRotation, info.weight / (weightSum + info.weight));
                    weightSum += info.weight;
                }
            }
        }

        /// <summary>喷火器按实际Animator姿态校正主手水平朝向，避免手动采样挂点在运行时向左偏；俯仰和翻滚沿用动画。</summary>
        private void AlignFlamethrowerMainHand()
        {
            if (current.Id != "arcade.weapon.flamethrower" || manualPoseSampling || !CanGripCurrentAnimation()) return;
            var forward = Vector3.ProjectOnPlane(visual.transform.forward, transform.up);
            if (forward.sqrMagnitude <= .000001f) return;
            var hand = animator.GetBoneTransform(current.MainHand);
            // 围绕手腕旋转会一并带动挂点和枪口，保持主手握点，不改战斗根或只转枪模。
            hand.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(forward, transform.forward, transform.up), transform.up) * hand.rotation;
        }

        private void SetVisualPose(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            // 每次在解析出的基础姿态上重算，不把上帧后坐累加到手骨或枪口。
            if (!manualPoseSampling && gunKickRemainingSeconds > 0f && gunKickDurationSeconds > 0f)
            {
                var remaining = Mathf.Clamp01(gunKickRemainingSeconds / gunKickDurationSeconds);
                position += rotation * Vector3.back * (gunKickDistance * remaining * remaining);
            }
            var root = visual.transform;
            if (root.localPosition != position) root.localPosition = position;
            if (root.localRotation != rotation) root.localRotation = rotation;
            if (root.localScale != scale) root.localScale = scale;
        }

        /// <summary>首批街机枪体的短回弹，由既有表现帧推进；切装、受击、死亡沿用统一清理入口。</summary>
        private float gunKickRemainingSeconds, gunKickDurationSeconds, gunKickDistance;

        private void BeginArcadeGunKick()
        {
            switch (current?.Id)
            {
                case "arcade.weapon.basic": gunKickDistance = .035f; gunKickDurationSeconds = .12f; break;
                case "arcade.weapon.rapid": gunKickDistance = .022f; gunKickDurationSeconds = .07f; break;
                case "arcade.weapon.spread": gunKickDistance = .075f; gunKickDurationSeconds = .19f; break;
                default: gunKickRemainingSeconds = 0f; return;
            }
            gunKickRemainingSeconds = gunKickDurationSeconds;
        }

        private bool CanGripCurrentAnimation()
        {
            if (manualPoseSampling || current == null || animator == null || !animator.isActiveAndEnabled)
                return false;
            var actions = current.Animations;
            var seen = false;
            for (var layer = 0; layer < animator.layerCount; layer++)
            {
                if (layer > 0 && animator.GetLayerWeight(layer) <= 0.0001f) continue;
                foreach (var info in animator.GetCurrentAnimatorClipInfo(layer))
                    if (info.weight > 0.0001f)
                    {
                        seen = true;
                        if (!AllowsGrip(actions, info.clip)) return false;
                    }
                if (!animator.IsInTransition(layer)) continue;
                foreach (var info in animator.GetNextAnimatorClipInfo(layer))
                    if (info.weight > 0.0001f)
                    {
                        seen = true;
                        if (!AllowsGrip(actions, info.clip)) return false;
                    }
            }
            return seen;
        }

        private static bool AllowsGrip(CharacterAnimationSet actions, AnimationClip clip)
        {
            foreach (var definition in new[]
                     {
                         actions.Idle, actions.MoveForward, actions.MoveBackward,
                         actions.MoveLeft, actions.MoveRight, actions.Attack
                     })
                if (definition?.Clip == clip) return definition.AllowOffhandGrip;
            if (actions.Extras != null)
                foreach (var definition in actions.Extras)
                    if (definition?.Clip == clip) return definition.AllowOffhandGrip;
            return false;
        }

        private void ApplyOffhandGripCore()
        {
            if (current == null || !current.HasOffhandGrip || visual == null || animator == null ||
                !animator.isHuman) return;
            var otherIsRight = current.MainHand == HumanBodyBones.LeftHand;
            var upper = animator.GetBoneTransform(otherIsRight ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            var lower = animator.GetBoneTransform(otherIsRight ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            var hand = animator.GetBoneTransform(otherIsRight ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            if (upper == null || lower == null || hand == null) return;
            var target = visual.transform.TransformPoint(current.OffhandGripLocalPosition);
            var shoulder = upper.position;
            var upperLength = Vector3.Distance(shoulder, lower.position);
            var lowerLength = Vector3.Distance(lower.position, hand.position);
            var toTarget = target - shoulder;
            if (upperLength <= 0.0001f || lowerLength <= 0.0001f || toTarget.sqrMagnitude <= 0.000001f)
                return;
            // 不同 Avatar 的臂长会改变握点可达性；先用有限锁骨位移消除悬空。
            var clavicle = animator.GetBoneTransform(otherIsRight ? HumanBodyBones.RightShoulder : HumanBodyBones.LeftShoulder);
            if (clavicle != null && toTarget.magnitude > upperLength + lowerLength)
            {
                clavicle.position += toTarget.normalized *
                                     Mathf.Min(0.08f, toTarget.magnitude - upperLength - lowerLength + 0.001f);
                shoulder = upper.position;
                toTarget = target - shoulder;
            }
            var distance = Mathf.Clamp(toTarget.magnitude, 0.0001f,
                upperLength + lowerLength - 0.0001f);
            var direction = toTarget.normalized;
            var elbowSide = Vector3.ProjectOnPlane(lower.position - shoulder, direction);
            if (elbowSide.sqrMagnitude <= 0.000001f)
                elbowSide = Vector3.ProjectOnPlane(transform.forward, direction);
            if (elbowSide.sqrMagnitude <= 0.000001f) return;
            var along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) /
                        (2f * distance);
            var height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            var desiredElbow = shoulder + direction * along + elbowSide.normalized * height;
            var handRotation = hand.rotation;
            upper.rotation = Quaternion.FromToRotation(lower.position - shoulder, desiredElbow - shoulder) *
                             upper.rotation;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, target - lower.position) *
                             lower.rotation;
            hand.rotation = handRotation;
        }
    }
}
