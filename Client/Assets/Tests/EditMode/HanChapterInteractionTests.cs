using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class HanChapterInteractionTests
    {
        private CivilisationHarvestTests support;
        [SetUp] public void SetUp() { support = new CivilisationHarvestTests(); support.SetUp(); }
        [TearDown] public void TearDown() => support.TearDown();

        [Test] public void ForgedAxeCompletesSameHarvestInHalfTheTime()
        {
            CivilisationHarvestTests.Equip(32);
            Assert.That(Game.Interface.GetSystem<CivilisationSearchSystem>().TryBeginSearch(1, "tree.valley.5", out var error), Is.True, error);
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(1.6f);
            Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited("tree.valley.5"), Is.True);
            Assert.That(support.Drops.Amount(9), Is.EqualTo(4));
        }
        [Test] public void SearchingProofPersistsEvidenceWithoutUnlockingTechnology()
        {
            var config = Game.Interface.GetUtility<IRuntimeConfigProvider>();
            Assert.That(config.TryGetCivilisationCatalog(out var catalog, out var error), Is.True, error);
            var proof = catalog.Points.Single(p => p.Id == "han.proof");
            Game.Interface.GetSystem<UnitSystem>().TryUpdateTransform(1, proof.Position, Quaternion.identity);
            Assert.That(Game.Interface.GetSystem<CivilisationSearchSystem>().TryBeginSearch(1, proof.Id, out error), Is.True, error);
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(3);
            var saved = Game.Interface.GetSystem<SaveSystem>().InspectExpedition();
            Assert.That(saved.Status, Is.EqualTo(SaveFileStatus.Valid), saved.Error);
            Assert.That(saved.Data.Sections[CivilisationExpeditionModel.Section]["chapters"].Values<string>(), Does.Contain("chapter.han-transport"));
            Assert.That(saved.Data.Sections[CivilisationExpeditionModel.Section]["records"].Values<string>(), Does.Contain("civilisation.point.proof.record"));
            Assert.That(Game.Interface.GetModel<CivilisationTechnologyModel>().IsChapterCompleted("chapter.han-transport"), Is.False);
        }
        [Test] public void NewlyLearnedRecipeProducesItsOwnItemAndSpendsOnlyItsPrice()
        {
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.BaseSafe);
            Game.Interface.GetModel<CurrencyModel>().Restore(new JObject { ["schemaVersion"] = 1, ["gold"] = 100 });
            var knowledge = CivilisationTechnologyModel.Empty(); knowledge["unlocked"] = new JArray("technology.iron-tools");
            Game.Interface.GetModel<CivilisationTechnologyModel>().Restore(knowledge);
            var basis = new BaseSaveData(); var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CaptureBaseSections(basis, out var error), Is.True, error);
            Assert.That(save.SaveBase(basis, out error), Is.True, error);
            Game.Interface.GetModel<SaveModel>().SetBase(basis, true);
            Assert.That(Game.Interface.GetSystem<CivilisationProductionSystem>().TryProduce("production.iron-axe", 1, out error), Is.True, error);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(32), Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(30), Is.Zero);
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(76));
        }
    }
}
