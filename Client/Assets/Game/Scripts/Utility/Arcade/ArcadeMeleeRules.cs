using System;
namespace FirepowerLoop.Arcade
{
    /// <summary>空手丧尸的攻击距离与已采用收招时长，不改写碰撞半径或Boss专用规则。</summary>
    public static class ArcadeMeleeRules
    {
        /// <summary>已采用抓挠从接触到完成收手的秒数。</summary>
        public const float RecoverySeconds = .55f;
        /// <summary>只识别街机正式空手丧尸配置族。</summary>
        public static bool IsZombie(string id) => id != null && id.StartsWith("enemy.arcade.zombie.", StringComparison.Ordinal);
        /// <summary>丧尸使用配置手爪距离，其余敌人维持既有身体接触规则。</summary>
        public static float Reach(string id, float configured, float collisionReach) =>
            IsZombie(id) && configured > 0 && !float.IsNaN(configured) && !float.IsInfinity(configured) ? configured : collisionReach;
        /// <summary>只追到手爪起手位置，收招时不追，较大帧步长也不能挤进主角。</summary>
        public static float ApproachSeconds(float distance, float reach, float speed, float seconds, float recovery)
            => recovery > 0 || speed <= 0 ? 0 : Math.Max(0, Math.Min(seconds, (distance - reach) / speed));
    }
}
