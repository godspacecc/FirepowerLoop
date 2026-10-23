using System;
using System.Collections.Generic;
using System.Linq;

namespace FirepowerLoop.Arcade
{
    /// <summary>保存中的休闲路线版本；旧局保持四十轮，新局追加三关后五十二轮。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public static class ArcadeRouteRules
    {
        public const int CurrentVersion = 1;
        /// <summary>缺失字段反序列化为零，不把已开始的旧局转成新路线。</summary>
        public static bool IsValidVersion(int version) => version is 0 or CurrentVersion;
        /// <summary>完成计数按本局已保存的版本解释，未知版本必须拒绝恢复。</summary>
        public static int ArenaCount(int version) => version switch { 0 => 10, CurrentVersion => 13, _ => throw new ArgumentOutOfRangeException(nameof(version)) };
        /// <summary>配置描述当前路线；唯一旧路线差异是在农场直接返回原第五关。</summary>
        public static int[] NextArenas(int version, int arenaConfigNumber, IEnumerable<int> configuredNext)
        {
            if (!IsValidVersion(version)) throw new ArgumentOutOfRangeException(nameof(version));
            return version == 0 && arenaConfigNumber == 22 ? new[] { 5 } : (configuredNext ?? Array.Empty<int>()).ToArray();
        }
    }
}
