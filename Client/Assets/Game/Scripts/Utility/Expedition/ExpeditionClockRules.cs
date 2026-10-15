using System;

namespace Game
{
    /// <summary>定义远征钟表起点与版本校验；不推进耗时，也不改变任务进度或生存消耗。</summary>
    public static class ExpeditionClockRules
    {
        /// <summary>新局只在 08:00～12:00 的整数分钟中选择一次，随机源与地图生成隔离。</summary>
        public static int DrawStartMinuteOfDay(Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            return random.Next(480, 721);
        }

        /// <summary>旧版本起点为零；拒绝未知版本和非法时刻，不修补保存的数据。</summary>
        public static bool TryValidate(int schemaVersion, float startMinuteOfDay, out string error)
        {
            error = null;
            if ((schemaVersion != 0 && schemaVersion != 1) ||
                float.IsNaN(startMinuteOfDay) || float.IsInfinity(startMinuteOfDay) ||
                startMinuteOfDay < 0 || startMinuteOfDay >= 1440 ||
                (schemaVersion == 0 && startMinuteOfDay != 0))
            { error = "副本钟表起点或版本无效"; return false; }
            return true;
        }

        /// <summary>任务要求使用时长文案，避免把目标分钟误显示为某个出发日期。</summary>
        public static string FormatDuration(float gameMinutes) =>
            Loc.T("ui.expedition.duration", Math.Floor(gameMinutes / 60), Math.Floor(gameMinutes % 60));
    }
}
