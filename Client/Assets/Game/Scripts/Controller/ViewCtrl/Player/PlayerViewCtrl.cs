using UnityEngine;

namespace Game
{
    /// <summary>玩家武器的副手表现类别；只描述姿态，不代表装备或战斗规则。</summary>
    internal enum PlayerWeaponGripPose
    {
        None,
        Pistol,
        Rifle
    }

    /// <summary>
    /// 管理玩家表现对象生命周期，选择武器姿态并转发移动和战斗反馈；不持有战斗权威。
    /// </summary>
    public sealed class PlayerViewCtrl : BaseEntityViewCtrl<PlayerView>, ICharacterMotorPort, IPlayerFlashlightPort
    {
        /// <summary>记录存活表现基线，只在状态变化时清空移动姿态。</summary>
        private bool presentationAlive = true;
        /// <summary>接收领域快照映射后的外观身份，不修改装备。</summary>
        public void ApplyClothing(string torso,string legs) => View.ClothingAppearance?.Apply(torso,legs);
        public void ApplyClothingItems(IRuntimeConfigProvider config,params int[] items) => View.ClothingAppearance?.ApplyItems(config,items);
        /// <summary>只转发已确认的测试造型编号；不重建动作、武器或碰撞。</summary>
        public bool ApplyTestAppearance(int index) => View.ClothingAppearance != null && View.ClothingAppearance.ApplyWholeBody(index);
        public PlayerViewCtrl(
            PlayerView view,
            int unitId,
            long mapInstanceId,
            ViewCtrlRegistry registry,
            IViewInstanceProvider instanceProvider)
            : base(view, unitId, mapInstanceId, registry, instanceProvider)
        {
        }

        public bool IsValid => !IsReleased && GameObject != null && ViewRef != null && ViewRef.CharacterController != null;
        public bool IsEnabled => IsValid && ViewRef.CharacterController.enabled;
        public Vector3 Position => Transform != null ? Transform.position : Vector3.zero;
        public Quaternion Rotation => Transform != null ? Transform.rotation : Quaternion.identity;
        public bool HasSharedWeaponPresenter => ViewRef != null &&
                                                ViewRef.GetComponent<CharacterWeaponPresenter>() != null;
        public string SharedWeaponError { get; private set; }
        internal Vector3 LocalPosition => Transform != null ? Transform.localPosition : Vector3.zero;
        internal Quaternion LocalRotation => Transform != null ? Transform.localRotation : Quaternion.identity;
        /// <summary>离散定位不计入移动；清除上一帧方向以免传送后误播侧移。</summary>
        internal void SnapLocal(Vector3 position,float yaw)
        {
            if (Transform != null)
            {
                Transform.localPosition = position;
                Transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
            ClearMovementPresentation();
        }
        public float MovementYawDegrees => ViewRef != null && ViewRef.CameraProfile != null ? ViewRef.CameraProfile.YawDegrees : 0f;
        public bool IsGrounded => IsValid && ViewRef.CharacterController.isGrounded;
        public void SetSwimmingPresentation(bool value) => ViewRef?.SetSwimmingPresentation(value);
        private PlayerView ViewRef => GameObject != null ? GameObject.GetComponent<PlayerView>() : null;
        // 只用于瞄准转身后的动画方向重算；移动权威仍在 PlayerMoveSystem。
        private Vector3 lastHorizontalMotion;

        /// <summary>街机区域切换专用传送，临时禁用角色碰撞并恢复原启用状态，不产生位移攻击。</summary>
        public void SetArcadeRegionPosition(Vector3 position)
        {
            if (!IsValid) return;
            var controller = ViewRef.CharacterController;
            bool enabled = controller.enabled;
            controller.enabled = false;
            Transform.position = position;
            controller.enabled = enabled;
            ClearMovementPresentation();
        }

        public void Move(Vector3 motion)
        {
            if (IsEnabled)
            {
                var before = Transform.position;
                ViewRef.CharacterController.Move(motion);
                var actual = Transform.position - before;
                lastHorizontalMotion = new Vector3(actual.x, 0f, actual.z);
                var horizontalDistance = lastHorizontalMotion.magnitude;
                ViewRef.SetMovementSpeed(Time.deltaTime > 0f ? horizontalDistance / Time.deltaTime : 0f);
                ViewRef.SetMovementDirection(lastHorizontalMotion);
            }
            else ClearMovementPresentation();
        }

        private void ClearMovementPresentation()
        {
            lastHorizontalMotion = Vector3.zero;
            ViewRef?.SetSprinting(false);
            ViewRef?.SetMovementSpeed(0f);
            ViewRef?.SetMovementDirection(Vector3.zero);
        }

        public void RotateTowards(Quaternion rotation, float maxDegreesDelta)
        {
            if (Transform != null)
            {
                Transform.rotation = Quaternion.RotateTowards(Transform.rotation, rotation, maxDegreesDelta);
                ViewRef?.SetMovementDirection(lastHorizontalMotion);
            }
        }

        public void FaceDirection(Vector3 direction)
        {
            direction.y = 0f;
            if (Transform != null && direction.sqrMagnitude > 0.0001f)
            {
                Transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                ViewRef?.SetMovementDirection(lastHorizontalMotion);
            }
        }

        public void SetAlivePresentation(bool alive)
            => SetAlivePresentation(alive,true);

        /// <summary>同步存活基线及后续转换，状态变化时清空上一段移动姿态。</summary>
        public void SetAlivePresentation(bool alive,bool playTransition)
        {
            if (presentationAlive != alive) ClearMovementPresentation();
            presentationAlive = alive;
            ViewRef?.SetAlivePresentation(alive,playTransition);
        }

        public void SetOnlineAlive(bool alive)
        {
            SetAlivePresentation(alive);
        }
        /// <summary>转发服务器救援状态，控制器不推导救援资格。</summary>
        public void SetArcadeRescueActive(bool active)=>ViewRef?.SetArcadeRescueActive(active);

        /// <summary>接收体力领域结果，同时刷新体力提示与跑步姿态；耗尽时清除加速表现。</summary>
        public void SetTiliRing(float current, float maximum, bool isSprinting, bool exhausted)
        {
            ViewRef?.SetSprinting(isSprinting && !exhausted);
            ViewRef?.SetTiliRing(current, maximum, isSprinting, exhausted);
        }

        public void ShowActionProgress(float progress01, string iconAddress)
        {
            ViewRef?.ShowActionProgress(progress01, iconAddress);
        }

        public void HideActionProgress()
        {
            ViewRef?.HideActionProgress();
        }

        public void SetWeaponPresentation(bool ranged, bool showShortSword, bool showBow)
        {
            ViewRef?.SetWeaponPresentation(ranged, showShortSword, showBow);
        }

        /// <summary>按已装备物品 ID 同步共享表现；未映射或类别不符时清空外观并报告失败。</summary>
        public bool TrySetSharedWeaponPresentation(int itemId)
        {
            SharedWeaponError = null;
            var view = ViewRef;
            var presenter = view != null ? view.GetComponent<CharacterWeaponPresenter>() : null;
            if (view == null || presenter == null) return false;
            view.SetSharedWeaponAnimationActive(true);
            view.SetWeaponPresentation(false, false, false);
            if (itemId <= 0)
            {
                var cleared = presenter.SetWeapon(null);
                if (!cleared) SharedWeaponError = presenter.LastError;
                return cleared;
            }
            var entry = presenter.Catalog?.FindByWorldItemId(itemId);
            if (entry == null)
            {
                presenter.SetWeaponId($"world-item:{itemId}");
                SharedWeaponError = $"普通物品 {itemId} 未映射到共享武器目录";
                return false;
            }
            var provider = Game.Interface.GetUtility<IRuntimeConfigProvider>();
            if (provider == null || !provider.TryGetWeapon(itemId, out var weapon, out _) ||
                weapon.WeaponKind != global::WeaponKind.Melee &&
                weapon.WeaponKind != global::WeaponKind.Projectile ||
                entry.IsProjectile != (weapon.WeaponKind == global::WeaponKind.Projectile))
            {
                presenter.SetWeapon(null);
                SharedWeaponError = $"普通物品 {itemId} 的玩法类别与共享武器 {entry.Id} 不一致或配置缺失";
                return false;
            }
            if (!presenter.SetWeapon(entry))
            {
                SharedWeaponError = presenter.LastError;
                return false;
            }
            return true;
        }

        public void SetArcadeWeaponPresentation(string weaponId)
        {
            if (HasSharedWeaponPresenter)
            {
                if (string.IsNullOrEmpty(weaponId))
                {
                    ViewRef.SetSharedWeaponAnimationActive(true);
                    ViewRef.SetWeaponPresentation(false, false, false);
                    ViewRef.GetComponent<CharacterWeaponPresenter>().SetWeapon(null);
                }
                // 街机完整 Definition 随后到达，只有它可核对玩法类别。
                return;
            }
            ConfigureArcadeWeaponPresentation(ViewRef, weaponId, false, new Vector3(0f, 0.04f, 0.5f));
            if (string.IsNullOrEmpty(weaponId))
            {
                ViewRef?.GetComponent<CharacterWeaponPresenter>()?.SetWeapon(null);
                ViewRef?.SetSharedWeaponAnimationActive(false);
            }
        }

        /// <summary>街机规则传入完整定义后校验类别，仅由共享目录提供模型、动作和枪口。</summary>
        public void SetArcadeWeaponPresentation(ArcadeWeaponDefinition weapon)
        {
            ConfigureSharedArcadeWeaponPresentation(ViewRef, weapon);
        }

        /// <summary>本机与联机远端复用同一共享武器绑定；只选择外观与动作，不修改装备或伤害。</summary>
        internal static void ConfigureSharedArcadeWeaponPresentation(PlayerView view, ArcadeWeaponDefinition weapon)
        {
            if (view == null) return;
            var presenter = view.GetComponent<CharacterWeaponPresenter>();
            if (presenter == null)
            {
                ConfigureArcadeWeaponPresentation(view, weapon.HeldVisualResourceName, weapon.MuzzleFlash,
                    weapon.ProjectileFirePointLocalPosition);
                return;
            }
            view.SetSharedWeaponAnimationActive(true);
            view.SetWeaponPresentation(false, false, false);
            var entry = presenter.Catalog?.Find(weapon.Id);
            var validBehavior = TryIsProjectileBehavior(weapon.Behavior, out var isProjectile);
            var bound = entry != null && validBehavior && entry.IsProjectile == isProjectile &&
                        presenter.SetWeapon(entry);
            if (!bound)
            {
                var error = entry == null ? "共享目录缺少该武器" : !validBehavior ? "武器行为枚举无效" :
                    entry.IsProjectile != isProjectile ? "玩法与表现近远程类别不一致" : presenter.LastError;
                presenter.SetWeapon(null);
                view.SetSharedWeaponAnimationActive(true);
                Debug.LogError($"[PlayerViewCtrl] 街机武器表现不可用：{weapon.Id} / {error}");
                return;
            }
            view.SetSharedWeaponAnimationActive(true);
        }

        private static bool TryIsProjectileBehavior(global::ArcadeWeaponBehavior behavior,
            out bool isProjectile)
        {
            switch (behavior)
            {
                case global::ArcadeWeaponBehavior.Melee:
                    isProjectile = false;
                    return true;
                case global::ArcadeWeaponBehavior.Direct:
                case global::ArcadeWeaponBehavior.Piercing:
                case global::ArcadeWeaponBehavior.Explosive:
                case global::ArcadeWeaponBehavior.TimedExplosive:
                    isProjectile = true;
                    return true;
                default:
                    isProjectile = false;
                    return false;
            }
        }

        public void SetArcadeWeaponPresentation(string visualResourceName, bool muzzleFlash,
            Vector3 projectileFirePointLocalPosition)
        {
            if (HasSharedWeaponPresenter) return;
            ConfigureArcadeWeaponPresentation(ViewRef, visualResourceName, muzzleFlash,
                projectileFirePointLocalPosition);
        }

        /// <summary>正式游戏与造型库共用武器姿态选择；未知武器保持基础动画且不约束副手。</summary>
        internal static void ConfigureArcadeWeaponPresentation(PlayerView view, string visualResourceName,
            bool muzzleFlash, Vector3 projectileFirePointLocalPosition)
        {
            view?.SetArcadeWeaponPresentation(visualResourceName, muzzleFlash,
                projectileFirePointLocalPosition, ResolveGripPose(visualResourceName));
        }

        /// <summary>按资源身份决定当前支持的表现类别；不根据枪口火光推断握持动作。</summary>
        internal static PlayerWeaponGripPose ResolveGripPose(string visualResourceName)
        {
            if (visualResourceName == "ArcadePistol") return PlayerWeaponGripPose.Pistol;
            return UsesRifleAimPose(visualResourceName) ? PlayerWeaponGripPose.Rifle : PlayerWeaponGripPose.None;
        }

        internal static bool UsesRifleAimPose(string visualResourceName) =>
            visualResourceName == "ArcadeRifle" ||
            visualResourceName == "ArcadeFlamethrower" ||
            visualResourceName == "ArcadePiercingGun" ||
            visualResourceName == "ArcadeScifiShotgun" ||
            visualResourceName == "ArcadeScifiPlasmaRifle";

        /// <summary>手动采样仅在兼容片段上约束副手；换弹与受击保留原动画的放手动作。</summary>
        internal static bool AllowsOffhandGrip(string visualResourceName, string animationName)
        {
            if (string.IsNullOrEmpty(animationName)) return false;
            switch (ResolveGripPose(visualResourceName))
            {
                case PlayerWeaponGripPose.Pistol:
                    return animationName == "PistolIdle" || animationName == "PistolFire";
                case PlayerWeaponGripPose.Rifle:
                    return animationName == "RifleIdleAiming" || animationName == "Rifle Idle Aiming" ||
                           animationName == "RifleFire" ||
                           animationName.StartsWith("RifleWalk", System.StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        public void SetArcadePresentationActive(bool active)
        {
            ViewRef?.SetArcadePresentationActive(active);
        }

        /// <summary>把合并后的有效保护交给角色实例呈现，随实例隐藏、死亡及释放。</summary>
        public void SetArcadeProtectionActive(bool active) => ViewRef?.SetArcadeProtectionActive(active);
        public void SetHerdImpact(FirepowerLoop.Arcade.ArcadeHerdImpactState state,Vector3 direction)=>ViewRef?.SetHerdImpact(state,direction);

        public bool TryGetWeaponAttachRoot(global::PlayerWeaponAttachPoint attachPoint, out Transform root)
        {
            root = null;
            return ViewRef?.TryGetWeaponAttachRoot(attachPoint, out root) == true;
        }

        public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin)
        {
            origin = ViewRef != null ? ViewRef.GetProjectileOrigin(direction) : Position;
            return IsValid;
        }

        public void FaceWorldHud(Vector3 cameraPosition, Vector3 cameraUp)
        {
            ViewRef?.FaceWorldHud(cameraPosition, cameraUp);
        }

        public void PlayAttackFeedback()
        {
            ViewRef?.PlayAttackFeedback();
        }

        public void PlayWeaponSwingFeedback()
        {
            ViewRef?.PlayWeaponSwingFeedback();
        }

        /// <summary>单人街机按本次攻击间隔播放完整挥刀；只调整表现，不改变权威冷却。</summary>
        public void PlayWeaponSwingFeedback(float attackIntervalSeconds)
        {
            ViewRef?.PlayWeaponSwingFeedback(attackIntervalSeconds);
        }

        public void PlayMeleeComboStage(int stage, float durationSeconds) => ViewRef?.PlayMeleeComboStage(stage, durationSeconds);
        public void StopMeleeCombo() => ViewRef?.StopMeleeCombo();

        public void PlayProjectileFireFeedback()
        {
            ViewRef?.PlayProjectileFireFeedback();
        }

        public void PlayBowFireFeedback()
        {
            ViewRef?.PlayBowFireFeedback();
        }

        public void PlayHurtFeedback()
        {
            ViewRef?.PlayHurtFeedback();
        }

        public void ShowDamage(int damage)
        {
            ViewRef?.ShowDamage(damage, true);
        }

        public void TickCombatFeedback(float deltaTime)
        {
            ViewRef?.TickCombatFeedback(deltaTime);
        }

        public bool IsPlayerCollider(Collider collider)
        {
            return collider != null &&
                   Transform != null &&
                   collider.transform.IsChildOf(Transform);
        }

        void IPlayerFlashlightPort.SetActive(bool active)
        {
            ViewRef?.SetFlashlightActive(active);
        }

        void IPlayerFlashlightPort.SetWorldDirection(Vector3 direction)
        {
            ViewRef?.SetFlashlightDirection(direction);
        }
    }
}
