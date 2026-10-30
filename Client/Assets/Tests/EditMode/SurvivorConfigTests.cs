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
        /// <summary>时间成长允许关闭；负数及非有限值仍拒绝开局。</summary>
        [TestCase("EnemyHealthGrowthPerMinute")] [TestCase("EnemyDamageGrowthPerMinute")] [TestCase("EnemySpeedGrowthPerMinute")]
        public void TimeGrowthAllowsZeroAndRejectsInvalidValues(string field)
        {
            var tables=Load();var row=tables.TbSurvivorRule.DataList[0];var member=typeof(global::SurvivorRuleConfig).GetField(field);
            member.SetValue(row,0f);Assert.DoesNotThrow(()=>SurvivorConfiguration.FromTables(tables));
            foreach(var invalid in new[]{-.01f,float.NaN,float.PositiveInfinity})
            {
                member.SetValue(row,invalid);Assert.Throws<ArgumentException>(()=>SurvivorConfiguration.FromTables(tables));
            }
        }
        [Test] public void LoadsIndependentConfigurationBeforeArcadeCatalog()
        {
            var provider = new LubanRuntimeConfigProvider(Load);
            Assert.That(provider.TryGetSurvivor(out var config, out var error), Is.True, error);
            Assert.That(config.LayoutId, Is.EqualTo("arena.22.round.1"));
            Assert.That(config.EnemyHealthGrowthPerMinute,Is.Zero);
            Assert.That(config.EnemyDamageGrowthPerMinute,Is.Zero);
            Assert.That(config.EnemySpeedGrowthPerMinute,Is.Zero);
            Assert.That(config.Upgrades.Count, Is.EqualTo(7));
            Assert.That(config.WeaponSlots, Is.EqualTo(3));
            Assert.That(config.MaximumWeaponLevel("Drone"), Is.EqualTo(5));
            Assert.That(config.WeaponLevel("Drone",2).Damage, Is.GreaterThan(config.WeaponLevel("Drone",1).Damage));
            Assert.That(config.MinimumSpawnIntervalSeconds, Is.LessThanOrEqualTo(config.SpawnIntervalSeconds));
        }
        [Test] public void ExperienceUsesDedicatedCrystalAndGuaranteedDrop()
        {
            var config=SurvivorConfiguration.FromTables(Load());
            Assert.That(config.ExperienceDropChance,Is.EqualTo(1f));
            Assert.That(config.ExperienceVisualPrefabName,Is.EqualTo("SurvivorExperienceCrystal"));
            Assert.That(config.ExperienceVisualPrefabAddress,Is.EqualTo("Prefabs/SceneObjects/SurvivorExperienceCrystal"));
            var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/"+config.ExperienceVisualPrefabAddress+".prefab");
            Assert.That(prefab,Is.Not.Null);Assert.That(prefab.GetComponentInChildren<MeshFilter>().sharedMesh,Is.Not.Null);
            Assert.That(prefab.GetComponentInChildren<MeshRenderer>().sharedMaterial,Is.Not.Null);
            Assert.That(prefab.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.size.y,Is.EqualTo(.5f).Within(.001f));
        }
        [TestCase(-.1f)] [TestCase(1.1f)] [TestCase(float.NaN)]
        public void InvalidExperienceChanceIsRejected(float chance)
        {
            var tables=Load();var row=tables.TbSurvivorRule.DataList[0];
            typeof(global::SurvivorRuleConfig).GetField("ExperienceDropChance").SetValue(row,chance);
            Assert.Throws<ArgumentException>(()=>SurvivorConfiguration.FromTables(tables));
        }
        [Test] public void LevelCostsStartAtTenAndGrowByFive()
        {
            var config=SurvivorConfiguration.FromTables(Load());
            var state=new SurvivorRunState(config.InitialExperienceThreshold,config.ExperienceThresholdGrowth);
            Assert.That(state.RequiredExperience,Is.EqualTo(10));
            state.AddExperience(9);Assert.That(state.Level,Is.EqualTo(1));
            state.AddExperience(1);Assert.That(state.Level,Is.EqualTo(2));Assert.That(state.RequiredExperience,Is.EqualTo(15));
            state.AddExperience(15);Assert.That(state.Level,Is.EqualTo(3));Assert.That(state.RequiredExperience,Is.EqualTo(20));
            Assert.That(config.ExperiencePerKill,Is.EqualTo(1));Assert.That(config.ExperienceDropChance,Is.EqualTo(1));
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
