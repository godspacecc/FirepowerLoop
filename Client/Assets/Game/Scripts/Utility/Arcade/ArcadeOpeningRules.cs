using System;
#if DOTNET
using ArenaConfig = ET.ArcadeArenaConfig;
#else
using ArenaConfig = global::ArcadeArenaConfig;
#endif

namespace FirepowerLoop.Arcade
{
    /// <summary>岛岸参数的双端不可变合同；源于 Luban，不拥有玩家、伤害计时或 Unity 表现。</summary>
    public readonly struct ArcadeOpeningParameters
    {
        private ArcadeOpeningParameters(ArenaConfig row)
        {
            WaterMinY = row.WaterMinY; WaterSpeedRatio = row.WaterSpeedRatio;
            CampfireX = row.CampfireX; CampfireY = row.CampfireY; CampfireRadius = row.CampfireRadius;
            CampfireDamage = row.CampfireDamage; CampfireIntervalSeconds = row.CampfireIntervalSeconds;
            RoundRestSeconds = row.RoundRestSeconds; ExitRestSeconds = row.ExitRestSeconds;
        }
        public bool Enabled => WaterSpeedRatio > 0;
        /// <summary>休整可独立于水岸启用；单位秒，零表示沿用旧短切轮。</summary>
        public bool HasRest => RoundRestSeconds > 0 && ExitRestSeconds > 0;
        /// <summary>旧矩形浅水阈值，保留配置兼容；当前海岛减速由 ArcadeIslandRegion 的双轮廓判定。</summary>
        public float WaterMinY { get; }
        public float WaterSpeedRatio { get; }
        public float CampfireX { get; }
        public float CampfireY { get; }
        public float CampfireRadius { get; }
        public int CampfireDamage { get; }
        public float CampfireIntervalSeconds { get; }
        public float RoundRestSeconds { get; }
        public float ExitRestSeconds { get; }

        public static ArcadeOpeningParameters FromConfig(ArenaConfig row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            var p = new ArcadeOpeningParameters(row);
            var values = new[] { p.WaterMinY, p.WaterSpeedRatio, p.CampfireX, p.CampfireY,
                p.CampfireRadius, p.CampfireIntervalSeconds, p.RoundRestSeconds, p.ExitRestSeconds };
            foreach (var v in values)
                if (float.IsNaN(v) || float.IsInfinity(v)) throw new ArgumentException("岛岸参数非有限值");
            if (p.Enabled && (row.ArenaNumber != 1 || p.WaterSpeedRatio > 1 || p.CampfireRadius <= 0 ||
                p.CampfireDamage <= 0 || p.CampfireIntervalSeconds <= 0 || p.RoundRestSeconds <= 0 || p.ExitRestSeconds <= 0))
                throw new ArgumentException("岛岸水火或休整参数无效");
            if (p.WaterSpeedRatio < 0) throw new ArgumentException("水域减速比例无效");
            if (p.RoundRestSeconds < 0 || p.ExitRestSeconds < 0 ||
                (p.RoundRestSeconds > 0) != (p.ExitRestSeconds > 0))
                throw new ArgumentException("休整时限必须成对配置");
            return p;
        }

        public bool TouchesCampfire(float x, float y, float playerRadius)
        {
            var dx = x - CampfireX; var dy = y - CampfireY;
            var radius = CampfireRadius + playerRadius;
            return Enabled && dx * dx + dy * dy <= radius * radius;
        }
    }

    /// <summary>已确认的普通轮总量与应急资源规则；不决定敌种、同屏预算、Boss 或持久化。</summary>
    public static class ArcadeOpeningRules
    {
        /// <summary>伤害与显示共用的活动条件；第四波清场、离场和复活等待均熄灭，不拥有伤害计时。</summary>
        public static bool CampfireActive(ArcadeOpeningParameters p, int waveIndex, bool specialBattle,
            bool cleared, bool awaitingExit, bool inRoom, bool respawnWaiting) =>
            p.Enabled && waveIndex == 3 && !specialBattle && !cleared && !awaitingExit && !inRoom && !respawnWaiting;

        /// <summary>本批仅迁移新休闲局前两场的三个地图候选；旧局、挑战和后期不在范围内。</summary>
        public static bool UsesEarlyRounds(bool casual, int mapNumber) => casual && mapNumber >= 1 && mapNumber <= 3;

        /// <summary>按累计权重整数分配已确认总量，差分确保和精确；不规定原作敌种组成。</summary>
        public static int AllocateMember(int total, int previousWeight, int weight, int totalWeight)
        {
            if (total < 0 || previousWeight < 0 || weight <= 0 || totalWeight <= 0 ||
                (long)previousWeight + weight > totalWeight) throw new ArgumentOutOfRangeException();
            return (int)(((long)previousWeight + weight) * total / totalWeight -
                (long)previousWeight * total / totalWeight);
        }

        public static int OrdinaryRoundTotal(int roundNumber, int playerCount)
        {
            if (roundNumber <= 0 || playerCount < 1 || playerCount > 4) throw new ArgumentOutOfRangeException();
            return (int)Math.Min(100L + 10L * roundNumber, 400L) + 50 * (playerCount - 1);
        }
        /// <summary>只补空库存到一，已有库存不增加；普通轮开场调用一次。</summary>
        public static int ReplenishEmpty(int held) => held == 0 ? 1 : held;
    }
}
