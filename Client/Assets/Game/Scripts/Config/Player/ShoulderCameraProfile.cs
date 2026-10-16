using UnityEngine;

namespace Game
{
    /// <summary>越肩构图及第一人称试用镜头参数；不管理输入、战斗或地图默认模式。</summary>
    [CreateAssetMenu(fileName = "ShoulderCameraProfile", menuName = "FirepowerLoop/Shoulder Camera Profile")]
    public sealed class ShoulderCameraProfile : ScriptableObject
    {
        public float TargetHeight = 1.6f;
        /// <summary>交谈镜头相对 NPC 根节点的注视高度，单位米。</summary>
        public float DialogueTargetHeight = 1.45f;
        /// <summary>交谈镜头距离和横向偏移，单位米；偏移避免玩家背部遮住 NPC。</summary>
        public float DialogueDistance = 2.6f;
        public float DialogueShoulderOffset = 0.65f;
        /// <summary>交谈垂直视野角，单位度；正常退出恢复普通越肩配置。</summary>
        public float DialogueFieldOfView = 45f;
        /// <summary>第一人称眼睛相对玩家根节点的高度，单位米；不跟随头部动画抖动。</summary>
        public float FirstPersonEyeHeight = 1.6f;
        /// <summary>第一人称垂直视野角，单位度。</summary>
        public float FirstPersonFieldOfView = 70f;
        /// <summary>第一人称近裁剪距离，单位米。</summary>
        public float FirstPersonNearClipPlane = 0.05f;
        /// <summary>常态第三人称的横向偏移和跟随距离，单位米；人物略偏左，保留环境视野。</summary>
        public float ShoulderOffset = 0.25f;
        public float FollowDistance = 3.5f;
        /// <summary>常态垂直视野角，单位度；仅镜头构图，不改变射击数值。</summary>
        public float FieldOfView = 60f;
        /// <summary>右键越肩瞄准的横向偏移和跟随距离，单位米；仍受同一碰撞管线约束。</summary>
        public float AimShoulderOffset = 0.55f;
        public float AimFollowDistance = 2.2f;
        public float AimFieldOfView = 45f;
        /// <summary>常态与瞄准之间的单程过渡时间，单位秒；0 表示立即切换。</summary>
        public float AimTransitionSeconds = 0.18f;
        public float DefaultPitchDegrees = 15f;
        public float MinPitchDegrees = -20f;
        public float MaxPitchDegrees = 60f;
        public float LookSensitivity = 0.12f;
        public float NearClipPlane = 0.1f;
        public float FarClipPlane = 80f;
        public float ObstructionRadius = 0.2f;
        /// <summary>未遮挡跟随距离的下限，单位米；近墙时允许进一步收镜以避免穿墙。</summary>
        public float MinimumDistance = 0.5f;
        /// <summary>沿镜头局部 XYZ 轴的跟随阻尼时间，单位秒；0 表示直接跟随。</summary>
        public Vector3 FollowDamping = new Vector3(0.1f, 0.12f, 0.12f);
        /// <summary>参与镜头遮挡的物理层；默认全部层，玩家自身由运行时标签排除。</summary>
        public LayerMask ObstructionLayers = -1;
        /// <summary>离开遮挡后的恢复阻尼时间，单位秒；进入遮挡始终立即收镜。</summary>
        public float ObstructionRecoveryTime = 0.2f;
    }
}
