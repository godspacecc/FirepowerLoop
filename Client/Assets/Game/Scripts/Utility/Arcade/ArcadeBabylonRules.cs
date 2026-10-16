using System;
using System.Collections.Generic;
using System.Linq;
#if DOTNET
using ArenaConfig = ET.ArcadeArenaConfig;
#else
using ArenaConfig = global::ArcadeArenaConfig;
#endif

namespace FirepowerLoop.Arcade
{
    /// <summary>前期箱子与护甲配置合同；不裁决命中、发奖、保存或 Unity 表现。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeBabylonParameters
    {
        public readonly struct Crate
        {
            public Crate(int id, float x, float y, float radius, int health)
            { Id = id; X = x; Y = y; Radius = radius; Health = health; }
            public int Id { get; }
            public float X { get; }
            public float Y { get; }
            public float Radius { get; }
            public int Health { get; }
        }
        private ArcadeBabylonParameters(Crate[] crates, int wave, int armor, int treasureWeight)
        { Crates = Array.AsReadOnly(crates); ArmorWaveNumber = wave; ArmorHealth = armor; CrateTreasureWeightMultiplier = treasureWeight; }
        public IReadOnlyList<Crate> Crates { get; }
        /// <summary>场内局部轮次，1 表示第五主线轮；0 禁用。只在配置 2 新 Casual 生效。</summary>
        public int ArmorWaveNumber { get; }
        public int ArmorHealth { get; }
        public int CrateTreasureWeightMultiplier { get; }
        public static ArcadeBabylonParameters FromConfig(ArenaConfig row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            var crates = row.Crates.Select(c => new Crate(c.Id, c.X, c.Y, c.Radius, c.Health)).ToArray();
            if (crates.Any(c => c.Id <= 0 || c.Health <= 0 || c.Radius <= 0 || !Finite(c.X) || !Finite(c.Y) || !Finite(c.Radius)) ||
                crates.Select(c => c.Id).Distinct().Count() != crates.Length || row.ArmorWaveNumber < 0 ||
                row.ArmorWaveNumber > 4 || row.ArmorHealth < 0 || (row.ArmorWaveNumber == 0) != (row.ArmorHealth == 0) ||
                row.CrateTreasureWeightMultiplier < 0 || crates.Length > 0 && row.CrateTreasureWeightMultiplier <= 0 ||
                (crates.Length > 0 || row.ArmorHealth > 0) && row.ArenaNumber != 2)
                throw new ArgumentException("Babylon 箱子或护甲配置无效");
            return new ArcadeBabylonParameters(crates, row.ArmorWaveNumber, row.ArmorHealth, row.CrateTreasureWeightMultiplier);
        }
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }

    /// <summary>本场可破坏箱状态，不落盘；破坏后由权威所有者移除并创建唯一掉落实体。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeCrateState
    {
        public ArcadeCrateState(ArcadeBabylonParameters.Crate definition) { Definition = definition; Health = definition.Health; }
        public ArcadeBabylonParameters.Crate Definition { get; }
        public int Health { get; private set; }
        /// <summary>掉落预算不足时拒绝最后一击，防止破坏物品却丢失奖励；已破坏重复调用无效。</summary>
        public bool Damage(int damage, bool rewardSpace)
        {
            if (damage <= 0 || Health <= 0 || damage >= Health && !rewardSpace) return false;
            Health = Math.Max(0, Health - damage); return Health == 0;
        }
    }

    public static class ArcadeBabylonRules
    {
        public static bool Intersects(float ax, float ay, float bx, float by, float x, float y, float radius)
        {
            float dx = bx - ax, dy = by - ay, length = dx * dx + dy * dy;
            float t = length > 0 ? Math.Max(0, Math.Min(1, ((x - ax) * dx + (y - ay) * dy) / length)) : 0;
            float rx = ax + t * dx - x, ry = ay + t * dy - y; return rx * rx + ry * ry <= radius * radius;
        }
        public static bool Enabled(bool casual, int map, bool challenge) => casual && map == 2 && !challenge;
        /// <summary>护甲先吸收，溢出进入原生命；零伤害保持幂等，不改变最大生命。</summary>
        public static int Absorb(ref int armor, int damage)
        {
            damage = Math.Max(0, damage); int absorbed = Math.Min(Math.Max(0, armor), damage);
            armor = Math.Max(0, armor - absorbed); return damage - absorbed;
        }
    }
}
