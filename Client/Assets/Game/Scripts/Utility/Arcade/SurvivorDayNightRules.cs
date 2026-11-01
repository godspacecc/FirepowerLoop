using System;
namespace Game
{
    /// <summary>共享昼夜进度，让攻击范围和照明使用同一权威时钟。</summary>
    public static class SurvivorDayNightRules
    {
        /// <summary>一昼夜固定为六分钟。</summary>
        public const float CycleSeconds=360f;
        /// <summary>白天零、深夜一的平滑余弦权重。</summary>
        public static float NightWeight(double elapsed)=>(float)((1-Math.Cos(elapsed/CycleSeconds*Math.PI*2))*.5);
        /// <summary>按昼夜权重插值主角有效攻击半径。</summary>
        public static float Range(float day,float night,double elapsed)=>day+(night-day)*NightWeight(elapsed);
    }
}
