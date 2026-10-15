using UnityEngine;

namespace Game
{
    /// <summary>仅计算版本3河谷水陆规则；移动系统拥有当前状态，存档保存迟滞状态，不新增生存消耗。</summary>
    public static class ValleySwimmingRules
    {
        public const float SpeedMultiplier = .6f;
        public const float SurfaceFootDepth = .9f;
        /// <summary>水面覆盖岸坡潜在湿区；实际水域仍以保存地形低于水位判断。</summary>
        public const float WaterHalfWidth = 32f;
        /// <summary>不同进入/退出深度防止河岸采样误差反复切换，单位米。</summary>
        public static bool ShouldSwim(float depth,bool wasSwimming) => depth > (wasSwimming ? .7f : 1f);
        /// <summary>只限制设计明确禁止的持续动作；物品使用和普通换弹沿用已有规则。</summary>
        public static bool BlocksAction(PlayerActionKind kind) => kind==PlayerActionKind.Search || kind==PlayerActionKind.ChopTree ||
            kind==PlayerActionKind.MineStone || kind==PlayerActionKind.CollectWater || kind==PlayerActionKind.Build || kind==PlayerActionKind.Extraction;
        public static bool TrySample(ValleyLayoutSaveData layout,Vector3 position,out float water,out float depth)
        {
            water=depth=0;
            if(layout==null||layout.GenerationVersion!=3)return false;
            var distance=ValleyLayoutGenerator.Distance(position,layout.River,out water);
            depth=water-layout.HeightAt(position.x,position.z);
            return distance<WaterHalfWidth && depth>0;
        }
    }
}
