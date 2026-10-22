using System;

namespace FirepowerLoop.Arcade
{
    /// <summary>双端共用击杀掉率；仅依赖敌人战斗强度和配置，不读取关卡编号或表现身份。</summary>
    public static class ArcadeEnemyDropRules
    {
        /// <summary>生命与攻击的相对强度取几何平均；双精度中间值避免大整数相乘溢出。</summary>
        public static float Chance(float baseChance, int maximumHealth, int damage,
            float healthUnit, float damageUnit, float maximumChance)
        {
            if (!Finite(baseChance) || baseChance < 0 || baseChance > 1 ||
                !Finite(healthUnit) || healthUnit <= 0 || !Finite(damageUnit) || damageUnit <= 0 ||
                !Finite(maximumChance) || maximumChance < baseChance || maximumChance > 1)
                throw new ArgumentException("敌人强度掉率参数无效");
            if (maximumHealth <= 0 || damage < 0) return 0;
            double strength = Math.Sqrt((double)maximumHealth / healthUnit * Math.Max(1, damage) / damageUnit);
            return (float)Math.Min(maximumChance, baseChance * strength);
        }

        /// <summary>兼容 Unity 与服务端的浮点有限值判断。</summary>
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
