using System.IO;
using System.Linq;
using Luban;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>通过真实生成配置锁定开场四轮与武器池，防止通用敌群改动污染首场。</summary>
    public sealed class ArcadeOpeningTests
    {
        private static ArcadeConfigCatalogDefinition Load()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            return catalog;
        }

        [Test]
        public void IslandFourWaves_UseOnlySlowBasicEnemiesAndFullSoloTotals()
        {
            var catalog = Load();
            var arena = catalog.Arenas.Single(a => a.ArenaNumber == 1);
            var totals = new[] { 110, 120, 130, 140 };
            Assert.That(arena.WaveGroupIds.Count, Is.EqualTo(4));
            for (var i = 0; i < 4; i++)
            {
                var group = catalog.EnemyGroups.Single(g => g.Id == arena.WaveGroupIds[i]);
                Assert.That(group.Members.Sum(m => m.Count), Is.EqualTo(totals[i]), "轮次 " + (i + 1));
                Assert.That(group.Members.All(m => m.EnemyId == "enemy.arcade.basic"), Is.True);
                Assert.That(group.MaxAlive, Is.LessThanOrEqualTo(catalog.Rule.MaxAliveEnemies));
            }
        }

        [Test]
        public void WeaponSources_DoNotOfferMeleeWeapons()
        {
            var catalog = Load();
            var meleeIds = catalog.Weapons.Where(w => w.Behavior == ArcadeWeaponBehavior.Melee)
                .Select(w => w.Id).ToArray();
            Assert.That(catalog.Drops.Any(d => meleeIds.Contains(d.ProductId)), Is.False,
                "所有场次的掉落池都应移除近战武器");
            Assert.That(catalog.ShopItems.Any(s => meleeIds.Contains(s.ProductId)), Is.False,
                "商店不应继续出售近战武器");
        }

        [Test]
        public void OpeningWeaponPool_HasTemporaryGunsAndRapidBaseline()
        {
            var catalog = Load();
            Assert.That(catalog.Rule.BaseWeaponId, Is.EqualTo("arcade.weapon.rapid"));
            Assert.That(catalog.Drops.Any(d => d.MinArena == 1 && d.Kind == ArcadeDropKind.TemporaryWeapon &&
                d.ProductId == "arcade.weapon.spread" && d.Weight > 0), Is.True);
        }
    }
}
