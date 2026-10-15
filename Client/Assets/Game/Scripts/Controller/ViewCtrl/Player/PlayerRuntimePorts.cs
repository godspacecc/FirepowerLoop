using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 暴露玩家移动、朝向和战斗反馈能力，隐藏具体 PlayerView。
    /// </summary>
    public interface ICharacterMotorPort
    {
        int UnitId { get; }
        long MapInstanceId { get; }
        bool IsValid { get; }
        bool IsEnabled { get; }
        Vector3 Position { get; }
        Quaternion Rotation { get; }
        float MovementYawDegrees { get; }
        bool IsGrounded { get; }
        void Move(Vector3 motion);
        /// <summary>领域水陆状态的表现镜像；默认端口无骨骼，不因此获得第二份状态权威。</summary>
        void SetSwimmingPresentation(bool value) { }
        /// <summary>区域切换时直接投影权威位置，避免角色碰撞把跨区域传送解释为移动。</summary>
        void SetArcadeRegionPosition(Vector3 position) => Move(position - Position);
        void RotateTowards(Quaternion rotation, float maxDegreesDelta);
        void FaceDirection(Vector3 direction);
        void SetVisible(bool visible) { }
        /// <summary>切换玩家存活表现；死亡不会隐藏 Humanoid，以便播放死亡动画。</summary>
        void SetAlivePresentation(bool alive) { SetVisible(alive); }
        /// <summary>切换街机武器纯表现；空值或未知 ID 表示隐藏街机专用模型。</summary>
        void SetArcadeWeaponPresentation(string weaponId) { }
        /// <summary>使用配置资源名、枪口火光开关和发射点位置切换街机武器纯表现。</summary>
        void SetArcadeWeaponPresentation(string visualResourceName, bool muzzleFlash,
            Vector3 projectileFirePointLocalPosition) => SetArcadeWeaponPresentation(visualResourceName);
        /// <summary>配置携带稳定武器 ID 时，正式玩家可使用五角色共享的模型与动作目录。</summary>
        void SetArcadeWeaponPresentation(ArcadeWeaponDefinition weapon) =>
            SetArcadeWeaponPresentation(weapon.HeldVisualResourceName, weapon.MuzzleFlash,
                weapon.ProjectileFirePointLocalPosition);
        /// <summary>切换街机专用单位识别表现；不改变玩家碰撞、生命或移动状态。</summary>
        void SetArcadePresentationActive(bool active) { }
        /// <summary>同步实际有效保护的纯视觉状态，不改变保护时长或受伤规则。</summary>
        void SetArcadeProtectionActive(bool active) { }
        void SetTiliRing(float current, float maximum, bool isSprinting, bool exhausted);
        void ShowActionProgress(float progress01, string iconAddress)
        {
        }
        void HideActionProgress()
        {
        }
        bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin);
        void FaceWorldHud(Vector3 cameraPosition, Vector3 cameraUp);
        void PlayAttackFeedback();
        void PlayWeaponSwingFeedback();
        /// <summary>单人街机的实际攻击间隔（秒），仅控制挥刀播放；其他实现保留原始反馈。</summary>
        void PlayWeaponSwingFeedback(float attackIntervalSeconds) => PlayWeaponSwingFeedback();
        /// <summary>执行已由武器系统接受的连招段；段号从零开始，时长单位为秒。</summary>
        void PlayMeleeComboStage(int stage, float durationSeconds) => PlayWeaponSwingFeedback(durationSeconds);
        /// <summary>完成或中断连招时释放表现，不改变领域冷却或命中。</summary>
        void StopMeleeCombo() { }
        void PlayProjectileFireFeedback()
        {
            PlayBowFireFeedback();
        }

        void PlayBowFireFeedback()
        {
        }

        void PlayHurtFeedback();
        /// <summary>显示玩家受到的伤害数值；不改变生命状态。</summary>
        void ShowDamage(int damage) { }
        void TickCombatFeedback(float deltaTime);
    }

    /// <summary>暴露本地玩家手电筒的纯表现能力，不公开具体 Light 或 PlayerView。</summary>
    public interface IPlayerFlashlightPort
    {
        bool IsValid { get; }
        void SetActive(bool active);
        void SetWorldDirection(Vector3 direction);
    }

    /// <summary>
    /// 暴露玩家相机更新和只读姿态查询能力，隐藏具体相机控制器。
    /// </summary>
    public interface IPlayerCameraPort
    {
        /// <summary>按会话临时隐藏本机模型、装备和世界 HUD；释放时恢复原值，不改变碰撞或镜头模式。</summary>
        void SetDialoguePresentationHidden(bool hidden) { }
        /// <summary>设置临时交谈焦点；null 幂等释放，不支持此能力的端口拒绝接管。</summary>
        bool TrySetDialogueFocus(Vector3? npcPosition) => !npcPosition.HasValue;
        /// <summary>接受 System 裁决的临时越肩构图；旧端口只允许释放，不默认支持瞄准。</summary>
        bool TrySetShoulderAim(bool aiming) => !aiming;
        bool IsValid { get; }
        float TopDownYawDegrees => 0f;
        void SetArcadeMode(bool enabled) { }
        void ApplyProfile();
        void LateUpdate();
        bool TryGetCameraPose(out Vector3 position, out Vector3 up);
        bool SupportsMode(PlayerCameraMode mode, out string error)
        {
            if (mode == PlayerCameraMode.TopDown)
            {
                error = null;
                return true;
            }

            error = "相机端口不支持越肩模式";
            return false;
        }

        void ApplyMode(PlayerCameraMode mode, float shoulderYawDegrees, float shoulderPitchDegrees)
        {
            if (mode == PlayerCameraMode.TopDown) ApplyProfile();
        }

        void LateUpdate(PlayerCameraMode mode, float shoulderYawDegrees, float shoulderPitchDegrees)
        {
            LateUpdate();
        }

        bool TryGetScreenPointRay(Vector2 screenPosition, out Ray ray)
        {
            ray = default;
            return false;
        }

        bool TryGetViewportCenterRay(out Ray ray)
        {
            ray = default;
            return false;
        }
    }

    /// <summary>
    /// 为系统层提供当前活动玩家端口，不暴露玩家控制器实现。
    /// </summary>
    public readonly struct PlayerPoseSnapshot
    {
        public PlayerPoseSnapshot(
            int unitId,
            long mapInstanceId,
            Vector3 position,
            Quaternion rotation,
            bool isActive)
        {
            UnitId = unitId;
            MapInstanceId = mapInstanceId;
            Position = position;
            Rotation = rotation;
            IsActive = isActive;
        }

        public int UnitId { get; }
        public long MapInstanceId { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public bool IsActive { get; }
    }

    /// <summary>当前活动玩家相对于所属地图根节点的只读姿态快照。</summary>
    public readonly struct PlayerMapLocalPoseSnapshot
    {
        public PlayerMapLocalPoseSnapshot(int unitId, long mapInstanceId, Vector3 localPosition, float localYawDegrees, bool isActive)
        {
            UnitId = unitId;
            MapInstanceId = mapInstanceId;
            LocalPosition = localPosition;
            LocalYawDegrees = localYawDegrees;
            IsActive = isActive;
        }

        public int UnitId { get; }
        public long MapInstanceId { get; }
        public Vector3 LocalPosition { get; }
        public float LocalYawDegrees { get; }
        public bool IsActive { get; }
    }

    public interface IPlayerRuntimePortProvider : IUtility
    {
        bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort port);
        bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port);
        bool TryGetFlashlightPort(int unitId, long mapInstanceId, out IPlayerFlashlightPort port)
        {
            port = null;
            return false;
        }
        bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider);
        bool TryGetCurrentPlayerPose(out PlayerPoseSnapshot snapshot, out string error)
        {
            snapshot = default;
            error = "当前玩家姿态不可用";
            return false;
        }

        bool TryGetCurrentPlayerMapLocalPose(out PlayerMapLocalPoseSnapshot snapshot, out string error)
        {
            snapshot = default;
            error = "当前玩家地图本地姿态不可用";
            return false;
        }
    }
}
