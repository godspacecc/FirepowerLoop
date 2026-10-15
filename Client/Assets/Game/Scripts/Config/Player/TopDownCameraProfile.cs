using UnityEngine;

namespace Game
{
    [CreateAssetMenu(
        fileName = "TopDownCameraProfile",
        menuName = "Another Day/Top Down Camera Profile")]
    public sealed class TopDownCameraProfile : ScriptableObject
    {
        [Range(65f, 75f)]
        public float PitchDegrees = 70f;

        [Range(-180f, 180f)]
        public float YawDegrees = 45f;

        [Range(20f, 80f)]
        public float FieldOfView = 40f;

        [Min(0.1f)]
        public float FollowDistance = 14f;

        [Min(0f)]
        public float FollowSmoothTime = 0.12f;

        [Min(0.01f)]
        public float NearClipPlane = 0.3f;

        [Min(0.11f)]
        public float FarClipPlane = 80f;
    }
}
