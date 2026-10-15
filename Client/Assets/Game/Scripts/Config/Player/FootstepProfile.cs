using UnityEngine;

namespace Game
{
    /// <summary>脚步纯表现参数和逻辑音效名；不保存速度、冲刺、装备或存档权威。</summary>
    [CreateAssetMenu(menuName = "AnotherDay/Footstep Profile")]
    public sealed class FootstepProfile : ScriptableObject
    {
        // 步距与纠正阈值单位米；快步阈值单位米/秒；间隔和开始延迟单位秒。
        // 常规速度 5 米/秒时约每秒 2.5 声，冲刺 7.5 米/秒时约 3.6 声；不让加长步距抵消加速节奏。
        [Min(0.05f)] public float WalkStride = 2f;
        [Min(0.05f)] public float RunStride = 2.1f;
        [Min(0.1f)] public float FastSpeed = 6f;
        [Min(0f)] public float MinimumInterval = 0.26f;
        [Min(0.1f)] public float CorrectionDistance = 2f;
        [Range(0f, 1f)] public float Gain = 0.6f;
        /// <summary>较快实际移动的单步增益；与全局音量相乘，使共用音色的土路也能区分落脚力度。</summary>
        [Range(0f, 1f)] public float RunGain = 0.75f;
        // 相机 AudioListener 的世界距离，单位米；逐次增益与全局 Sound 音量相乘。
        [Min(0f)] public float MinDistance = 16f;
        [Min(0f)] public float MaxDistance = 35f;
        [Min(0f)] public float MaximumStartDelay = 0.15f;
        // 名字不含路径和扩展名，统一由 ResourceAddress 解析到 Audio/Sound。
        public string[] GrassWalk = System.Array.Empty<string>();
        public string[] GrassRun = System.Array.Empty<string>();
        public string[] Dirt = System.Array.Empty<string>();
        public string[] GravelWalk = System.Array.Empty<string>();
        public string[] GravelRun = System.Array.Empty<string>();
        // 泥沙暂采用备用库的 mud 音色；浅水独立使用 water，不把泥沙当碎石。
        public string[] MudWalk = System.Array.Empty<string>();
        public string[] MudRun = System.Array.Empty<string>();
        public string[] WaterWalk = System.Array.Empty<string>();
        public string[] WaterRun = System.Array.Empty<string>();
        public string[] ConcreteWalk = System.Array.Empty<string>();
        public string[] ConcreteRun = System.Array.Empty<string>();

        public string[] GetSounds(FootstepSurfaceKind surface, bool fast)
        {
            switch (surface)
            {
                case FootstepSurfaceKind.Grass: return fast ? GrassRun : GrassWalk;
                case FootstepSurfaceKind.Dirt: return Dirt;
                case FootstepSurfaceKind.Gravel: return fast ? GravelRun : GravelWalk;
                case FootstepSurfaceKind.Mud: return fast ? MudRun : MudWalk;
                case FootstepSurfaceKind.Water: return fast ? WaterRun : WaterWalk;
                case FootstepSurfaceKind.Concrete: return fast ? ConcreteRun : ConcreteWalk;
                default: return System.Array.Empty<string>();
            }
        }
    }
}
