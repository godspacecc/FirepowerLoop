using System;
using System.IO;
using System.Linq;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>只验证幸存者独立配置读取与强化边界。</summary>
    public sealed class SurvivorConfigTests
    {
        private static Tables Load() => new Tables(n => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", n + ".bytes"))));
        [Test] public void LoadsIndependentConfigurationBeforeArcadeCatalog()
        {
            var provider = new LubanRuntimeConfigProvider(Load);
            Assert.That(provider.TryGetSurvivor(out var config, out var error), Is.True, error);
            Assert.That(config.LayoutId, Is.EqualTo("arena.22.round.1"));
            Assert.That(config.Upgrades.Count, Is.EqualTo(7));
            Assert.That(config.WeaponSlots, Is.EqualTo(3));
            Assert.That(config.MaximumWeaponLevel("Drone"), Is.EqualTo(5));
            Assert.That(config.WeaponLevel("Drone",2).Damage, Is.GreaterThan(config.WeaponLevel("Drone",1).Damage));
            Assert.That(config.MinimumSpawnIntervalSeconds, Is.LessThanOrEqualTo(config.SpawnIntervalSeconds));
        }
        [Test] public void MissingTablesReturnsActionableFailure()
        {
            var provider = new LubanRuntimeConfigProvider(() => null);
            Assert.That(provider.TryGetSurvivor(out var config, out var error), Is.False);
            Assert.That(config, Is.Null); Assert.That(error, Is.Not.Empty);
        }
        [Test] public void ThreeUnlimitedChoicesRemainMonotonicWithinBounds()
        {
            var config = SurvivorConfiguration.FromTables(Load());
            var unlimited = config.Upgrades.Where(x => x.MaxLevel == 0).ToArray();
            Assert.That(unlimited.Length, Is.GreaterThanOrEqualTo(3));
            foreach (var option in unlimited)
            {
                Assert.That(option.BonusAtLevel(0), Is.Zero);
                Assert.That(option.BonusAtLevel(100001), Is.GreaterThan(option.BonusAtLevel(100000)));
                Assert.That(option.BonusAtLevel(int.MaxValue), Is.LessThanOrEqualTo(option.MaximumBonus));
            }
        }
        [Test] public void UnknownAndNonFiniteUpgradeDefinitionsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new SurvivorUpgradeDefinition("x", "Unknown", 1, 2, 0));
            Assert.Throws<ArgumentException>(() => new SurvivorUpgradeDefinition("x", "Damage", float.NaN, 2, 0));
            Assert.Throws<ArgumentException>(() => new SurvivorUpgradeDefinition("x", "Damage", 1, float.PositiveInfinity, 0));
        }
        [Test] public void LimitedChoicesStopAtConfiguredLevel()
        {
            foreach (var option in SurvivorConfiguration.FromTables(Load()).Upgrades.Where(x => x.MaxLevel > 0))
                Assert.That(option.BonusAtLevel(int.MaxValue), Is.EqualTo(option.BonusAtLevel(option.MaxLevel)));
        }
    }
}
