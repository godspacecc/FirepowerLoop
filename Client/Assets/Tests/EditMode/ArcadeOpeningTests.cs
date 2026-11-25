using System.IO;
using System.Linq;
using Luban;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>通过真实生成配置检查共用武器池。</summary>
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
        public void WeaponSources_DoNotOfferMeleeWeapons()
        {
            var catalog = Load();
            var meleeIds = catalog.Weapons.Where(w => w.Behavior == ArcadeWeaponBehavior.Melee)
                .Select(w => w.Id).ToArray();
            Assert.That(catalog.Drops.Any(d => meleeIds.Contains(d.ProductId)), Is.False,
                "所有场次的掉落池都应移除近战武器");
            Assert.That(catalog.SupportVisuals.Any(s => meleeIds.Contains(s.ProductId)), Is.False,
                "商店不应继续出售近战武器");
        }

        [Test]
        public void OpeningWeaponPool_HasTemporaryGunsAndRapidBaseline()
        {
            var catalog = Load();
            Assert.That(catalog.Rule.BaseWeaponId, Is.EqualTo("arcade.weapon.rapid"));
            Assert.That(catalog.Drops.Any(d => d.MinArena <= 2 && d.Kind == ArcadeDropKind.TemporaryWeapon &&
                d.ProductId == "arcade.weapon.rapid" && d.Weight > 0), Is.True);
        }
    }
}
