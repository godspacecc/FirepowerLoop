using System;
using System.Collections.Generic;
using System.Linq;

namespace FirepowerLoop.Arcade
{
    /// <summary>按存档版本解释路线；新局十场四十轮，旧局保持原十场或十三场及分支。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public static class ArcadeRouteRules
    {
        /// <summary>已发布的十三场版本，后段挑战及旧局恢复仍保留其完整场数。</summary>
        public const int ExpandedVersion = 1;
        /// <summary>2026-10-25确认的唯一十关新局路线。</summary>
        public const int CurrentVersion = 2;
        /// <summary>缺失字段反序列化为零，不把已开始的旧局转成新路线。</summary>
        public static bool IsValidVersion(int version) => version is 0 or ExpandedVersion or CurrentVersion;
        /// <summary>完成计数按本局已保存的版本解释，未知版本必须拒绝恢复。</summary>
        public static int ArenaCount(int version) => version switch { 0 => 10, ExpandedVersion => 13, CurrentVersion => 10, _ => throw new ArgumentOutOfRangeException(nameof(version)) };
        /// <summary>配置只描述当前新局；显式投影被本次缩路线覆盖的旧边，保留旧局三条分支。</summary>
        public static int[] NextArenas(int version, int arenaConfigNumber, IEnumerable<int> configuredNext)
        {
            if (!IsValidVersion(version)) throw new ArgumentOutOfRangeException(nameof(version));
            if (version == 0 && arenaConfigNumber == 22) return new[] { 5 };
            if (version != CurrentVersion)
            {
                switch (arenaConfigNumber)
                {
                    case 5: return new[] { 6 };
                    case 7: return new[] { 9, 10 };
                    case 10: return new[] { 11 };
                }
            }
            return (configuredNext ?? Array.Empty<int>()).ToArray();
        }
    }
}
