using QFramework;

namespace Game
{
    public enum PlayerCameraMode
    {
        TopDown,
        Shoulder,
        /// <summary>眼睛高度的试用镜头；沿用越肩输入，隐藏本机角色表现。</summary>
        FirstPerson
    }

    /// <summary>仅保存当前进程内的玩家相机状态；普通玩法默认第三人称，模式由相机 System 裁决，不参与存档。</summary>
    public sealed class PlayerCameraModeModel : AbstractModel
    {
        /// <summary>当前镜头模式；新地图首次绑定时由 System 应用普通第三人称或街机俯视规则。</summary>
        public PlayerCameraMode Mode { get; internal set; } = PlayerCameraMode.Shoulder;
        /// <summary>越肩与第一人称共用的水平视线角，单位度；仅当前进程有效。</summary>
        public float ShoulderYawDegrees { get; internal set; }
        public float ShoulderPitchDegrees { get; internal set; } = 15f;
        /// <summary>本次有效右键按压的越肩瞄准状态；由相机 System 裁决，切图和失去输入资格即清除。</summary>
        public bool IsShoulderAiming { get; internal set; }

        protected override void OnInit()
        {
            Mode = PlayerCameraMode.Shoulder;
            ShoulderYawDegrees = 0f;
            ShoulderPitchDegrees = 15f;
            IsShoulderAiming = false;
        }
    }
}
