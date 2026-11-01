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
        /// <summary>独立金币倍率在强度封顶后生效，经验与共享闯关规则保持原值。</summary>
        [Test] public void GoldRateScalesAfterCapWithoutChangingExperienceOrArcade()
        {
            var tables=Load();var config=SurvivorConfiguration.FromTables(tables);
            Assert.That(tables.TbSurvivorRule.Get("survivor.default").Id,Is.EqualTo("survivor.default"));
            Assert.That(config.GoldDropMultiplier,Is.EqualTo(.01f));
            Assert.That(config.ExperienceDropChance,Is.EqualTo(1f));
            foreach(var chance in new[]{.2236f,.2324f,.4743f,.5f})
                Assert.That(chance*config.GoldDropMultiplier,Is.EqualTo(chance/100).Within(.000001));
            var capped=ArcadeEnemyDropRules.Chance(.2f,int.MaxValue,int.MaxValue,100,10,.5f);
            Assert.That(capped,Is.EqualTo(.5f));
            Assert.That(capped*config.GoldDropMultiplier,Is.EqualTo(.005f).Within(.000001));
            var source=File.ReadAllText(Path.Combine(Application.dataPath,"Game/Scripts/System/Arcade/SurvivorSystem.Gold.cs"));
            Assert.That(source,Does.Contain("r.EnemyDropMaxChance)*config.GoldDropMultiplier"));
            var member=typeof(global::SurvivorRuleConfig).GetField("GoldDropMultiplier");
            foreach(var value in new[]{-.1f,1.1f,float.NaN,float.PositiveInfinity}) {
                member.SetValue(tables.TbSurvivorRule.DataList[0],value);
                Assert.Throws<ArgumentException>(()=>SurvivorConfiguration.FromTables(tables));
            }
        }
        private static Tables Load() => new Tables(n => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", n + ".bytes"))));
        /// <summary>正式源生成后的路线分组、恢复强度及等待时间必须与采用玩法一致。</summary>
        [Test] public void RecoveryAndMagazineRoutesUseAuthoritativeValues()
        {
            var config=SurvivorConfiguration.FromTables(Load());
            foreach(var kind in new[]{"Magazine","Reload"})Assert.That(config.Upgrades.Single(x=>x.Kind==kind).ExclusiveGroup,Is.EqualTo("Magazine"));
            foreach(var row in new[]{("RecoveryStill",2f,.01), ("RecoverySafe",5f,.006), ("RecoveryAlways",0f,.003)})
            {
                var u=config.Upgrades.Single(x=>x.Kind==row.Item1);
                Assert.That(u.ExclusiveGroup,Is.EqualTo("Recovery"));Assert.That(u.ActivationDelaySeconds,Is.EqualTo(row.Item2));
                Assert.That(u.MaxLevel,Is.EqualTo(5));Assert.That(u.BonusAtLevel(1),Is.EqualTo(row.Item3).Within(.000001));
                Assert.That(u.BonusAtLevel(5),Is.EqualTo(row.Item3*5).Within(.000001));
            }
            foreach(var invalid in new[]{-1f,float.NaN,float.PositiveInfinity})
                Assert.Throws<ArgumentException>(()=>new SurvivorUpgradeDefinition("invalid","RecoverySafe",.006f,.03f,5,10,activationDelaySeconds:invalid));
        }
        /// <summary>主武器固定基础能力，强化池只保留玩家属性、弹药与辅助武器。</summary>
        [Test] public void UpgradePoolKeepsSupportAndAmmoWithoutMainWeaponScaling()
        {
            var config = SurvivorConfiguration.FromTables(Load());
            Assert.That(config.Upgrades.Where(x => x.Weight > 0).Select(x => x.Kind), Is.EquivalentTo(new[] { "Health", "MoveSpeed", "Pickup", "Drone", "Magazine", "Reload", "RiflePierce", "RifleExplosion", "RifleSplit", "RifleMultishot", "RecoveryStill", "RecoverySafe", "RecoveryAlways", "RifleFire", "RifleFrost" }));
            Assert.That(config.WeaponLevel("Rifle", 1).Damage, Is.EqualTo(36));
            Assert.That(config.MaximumWeaponLevel("Drone"), Is.EqualTo(5));
        }
        /// <summary>有限强化全部选满后允许只剩生命和弹夹，不强制补回伤害类选项。</summary>
        [Test] public void ConfigurationAllowsTwoUnlimitedChoicesAndNoRifleUpgrade()
        {
            var tables = Load();
            tables.TbSurvivorUpgrade.DataList.RemoveAll(x => x.Kind == "Rifle" || x.Kind == "Damage" || x.Kind == "FireRate");
            Assert.DoesNotThrow(() => SurvivorConfiguration.FromTables(tables));
        }
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
            Assert.That(config.Upgrades.Count, Is.EqualTo(21));
            Assert.That(config.WeaponSlots, Is.EqualTo(3));
            Assert.That(config.RifleMagazineCapacity,Is.EqualTo(30));Assert.That(config.RifleReloadSeconds,Is.EqualTo(2f));Assert.That(config.RifleMinimumReloadSeconds,Is.EqualTo(1f));
            var magazine=config.Upgrades.Single(x=>x.Kind=="Magazine");
            Assert.That(magazine.BonusAtLevel(1),Is.EqualTo(.1).Within(.000001));Assert.That(magazine.BonusAtLevel(10),Is.EqualTo(1).Within(.000001));
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
            var unlimited = config.Upgrades.Where(x => x.MaxLevel == 0 && x.Kind!="Magazine").ToArray();
            Assert.That(unlimited.Length, Is.GreaterThanOrEqualTo(1));
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
