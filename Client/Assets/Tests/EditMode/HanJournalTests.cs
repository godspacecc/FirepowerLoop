using System.Collections.Generic;
using System.Linq;
using Game;
using Luban;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>验证发现记录的保存、失败边界和正式日志页面；不改写产品资源。</summary>
    public sealed class HanJournalTests
    {
        private static IRuntimeConfigProvider Config() => new LubanRuntimeConfigProvider(() => new Tables(name =>
            new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/Game/Config/{name}.bytes").bytes)));
        private static BaseSaveData Basis() => new BaseSaveData { Sections = new Dictionary<string, JToken>
        {
            [CurrencyModel.SaveSectionId] = new JObject { ["schemaVersion"] = 1, ["gold"] = 10 },
            [CivilisationTechnologyModel.Section] = CivilisationTechnologyModel.Empty()
        }};
        private static ExpeditionSaveData Journey() => new ExpeditionSaveData
        {
            ExpeditionId = "0123456789abcdef0123456789abcdef", MapId = MapIds.CivilisationValley,
            Sections = new Dictionary<string, JToken>
            {
                [CivilisationExpeditionModel.Section] = CivilisationExpeditionModel.Empty(),
                ["another-day.player-bag"] = new JObject { ["capacity"] = 20, ["riskState"] = "InExpedition", ["slots"] = new JArray(), ["pocketSlots"] = new JArray() }
            }
        };
        [Test] public void SuccessfulReturnArchivesRecordsAndDeduplicates()
        {
            var basis = Basis(); var journey = Journey();
            journey.Sections[CivilisationExpeditionModel.Section]["records"] = new JArray("civilisation.clue.upstream.record");
            for (var i = 0; i < 2; i++) Assert.That(CivilisationSettlementPolicy.TryApply(basis, journey, false, Config(), out var error), Is.True, error);
            Assert.That(basis.Sections[CivilisationTechnologyModel.Section]["records"]?.Values<string>(), Is.EquivalentTo(new[] { "civilisation.clue.upstream.record" }));
            Assert.That(basis.Sections[CivilisationTechnologyModel.Section]["completedChapters"], Is.Empty);
        }
        [Test] public void DefeatDoesNotArchiveNewRecords()
        {
            var basis = Basis(); var before = JObject.FromObject(basis.Sections); var journey = Journey();
            journey.Sections[CivilisationExpeditionModel.Section]["records"] = new JArray("civilisation.point.proof.record");
            Assert.That(CivilisationSettlementPolicy.TryApply(basis, journey, true, Config(), out var error), Is.True, error);
            Assert.That(JToken.DeepEquals(before, JObject.FromObject(basis.Sections)), Is.True);
        }
        [Test] public void InvalidRecordRejectsSettlementWithoutPartialRewards()
        {
            var basis = Basis(); var before = JObject.FromObject(basis.Sections); var journey = Journey();
            journey.Sections[CivilisationExpeditionModel.Section]["records"] = new JArray("unknown.record");
            Assert.That(CivilisationSettlementPolicy.TryApply(basis, journey, false, Config(), out _), Is.False);
            Assert.That(JToken.DeepEquals(before, JObject.FromObject(basis.Sections)), Is.True);
        }
        [Test] public void DuplicateRecordsAreRejectedAndOldSavesRemainValid()
        {
            var data = CivilisationExpeditionModel.Empty(); data.Remove("records");
            Assert.That(new CivilisationExpeditionModel().Validate(data, out _), Is.True);
            data["records"] = new JArray("civilisation.clue.upstream.record", "civilisation.clue.upstream.record");
            Assert.That(new CivilisationExpeditionModel().Validate(data, out _), Is.False);
            var permanent = CivilisationTechnologyModel.Empty(); permanent["records"] = data["records"].DeepClone();
            Assert.That(new CivilisationTechnologyModel().Validate(permanent, out _), Is.False);
        }
        [Test] public void CollectionsPageShowsDiscoveredTextButDoesNotRevealUnreadProof()
        {
            var fixture = new CivilisationProductionTests(); fixture.SetUp(); GameObject root = null;
            var controller = new JournalManagementPageCtrl();
            try
            {
                var state = CivilisationTechnologyModel.Empty(); state["records"] = new JArray("civilisation.clue.upstream.record");
                Game.Interface.GetModel<CivilisationTechnologyModel>().Restore(state);
                root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Page/JournalManagement.prefab"));
                var view = root.GetComponent<JournalManagementPageUI>(); controller.SetUI(view); controller.Show();
                view.CollectionsButton.onClick.Invoke();
                Assert.That(view.DetailBodyText.text, Does.Contain("车辙"));
                Assert.That(view.DetailBodyText.text, Does.Not.Contain("伤者"));
                controller.Hide(); controller.Show(); view.CollectionsButton.onClick.Invoke();
                Assert.That(view.DetailBodyText.text, Does.Contain("车辙"));
                Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
                Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));
                Assert.That(view.DetailBodyText.text, Does.Contain("Wheel ruts"));
                Assert.That(view.DetailBodyText.text, Does.Not.Contain("车辙"));
                var carried = CivilisationExpeditionModel.Empty(); carried["records"] = new JArray("civilisation.point.proof.record");
                Game.Interface.GetModel<CivilisationExpeditionModel>().Restore(carried);
                Game.Interface.GetModel<SaveModel>().SetExpedition(new ExpeditionSaveData());
                view.CollectionsButton.onClick.Invoke(); Assert.That(view.DetailBodyText.text, Does.Contain("The transport party's letter"));
                Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
                view.CollectionsButton.onClick.Invoke(); Assert.That(view.DetailBodyText.text, Does.Not.Contain("The transport party's letter"));
            }
            finally { controller.SetUI(null); if (root != null) Object.DestroyImmediate(root); fixture.TearDown(); }
        }
        [Test] public void EveryDiscoverableReadingHasBothLanguages()
        {
            var config = Config(); Assert.That(config.TryGetCivilisationCatalog(out var catalog, out var error), Is.True, error);
            var keys = catalog.Points.Where(p => !string.IsNullOrEmpty(p.TechnologyId) || !string.IsNullOrEmpty(p.ChapterId))
                .Select(p => p.NameKey + ".record").Concat(new[] { "civilisation.clue.upstream.record", "civilisation.clue.camp.record" });
            foreach (var language in new[] { "zh-CN", "en-US" })
            {
                var texts = JObject.Parse(System.IO.File.ReadAllText("Assets/Game/Config/Localization/" + language + ".json"));
                foreach (var key in keys) Assert.That((string)texts[key], Is.Not.Null.And.Not.Empty, language + ":" + key);
            }
        }
        [Test] public void DiscoveredCraftAndProofSurviveDiskReloadReturnAndProduction()
        {
            var fixture = new CivilisationHarvestTests(); fixture.SetUp();
            try
            {
                var config = Game.Interface.GetUtility<IRuntimeConfigProvider>();
                Assert.That(config.TryGetCivilisationCatalog(out var catalog, out var error), Is.True, error);
                foreach (var id in new[] { "han.iron-tools", "han.proof" })
                {
                    var point = catalog.Points.Single(p => p.Id == id);
                    Game.Interface.GetSystem<UnitSystem>().TryUpdateTransform(1, point.Position, Quaternion.identity);
                    Assert.That(Game.Interface.GetSystem<CivilisationSearchSystem>().TryBeginSearch(1, id, out error), Is.True, error);
                    Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(3.1f);
                }
                var save = Game.Interface.GetSystem<SaveSystem>(); var stored = save.InspectExpedition();
                Assert.That(stored.Status, Is.EqualTo(SaveFileStatus.Valid), stored.Error);
                var model = Game.Interface.GetModel<CivilisationExpeditionModel>();
                model.Reset(); model.Restore(stored.Data.Sections[CivilisationExpeditionModel.Section]);
                Assert.That(model.Records, Is.EquivalentTo(new[] { "civilisation.point.iron_tools.record", "civilisation.point.proof.record" }));
                var basis = Basis(); basis.Sections[CurrencyModel.SaveSectionId]["gold"] = 100;
                Assert.That(CivilisationSettlementPolicy.TryApply(basis, stored.Data, false, config, out error), Is.True, error);
                var permanent = Game.Interface.GetModel<CivilisationTechnologyModel>(); permanent.Restore(basis.Sections[CivilisationTechnologyModel.Section]);
                Assert.That(permanent.Records, Is.EquivalentTo(model.Records));
                Assert.That(permanent.IsChapterCompleted("chapter.han-transport"), Is.True);
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
                Game.Interface.GetModel<SaveModel>().SetBase(basis, true);
                Game.Interface.GetModel<CurrencyModel>().Restore(basis.Sections[CurrencyModel.SaveSectionId]);
                Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.BaseSafe);
                Assert.That(save.CaptureBaseSections(basis, out error), Is.True, error);
                Assert.That(save.SaveBase(basis, out error), Is.True, error);
                Assert.That(Game.Interface.GetSystem<CivilisationProductionSystem>().TryProduce("production.iron-axe", 1, out error), Is.True, error);
                Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(32), Is.EqualTo(1));
                model.Reset(); Assert.That(permanent.Records.Length, Is.EqualTo(2)); Assert.That(model.Records, Is.Empty);
            }
            finally { fixture.TearDown(); }
        }
        [Test] public void FailedSearchSaveRollsBackTheNewReading()
        {
            var fixture = new CivilisationHarvestTests(); fixture.SetUp();
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HanReadingFailure", System.Guid.NewGuid().ToString("N"));
            try
            {
                System.IO.Directory.CreateDirectory(directory); var blocked = System.IO.Path.Combine(directory, "file"); System.IO.File.WriteAllText(blocked, "blocked");
                var config = Game.Interface.GetUtility<IRuntimeConfigProvider>(); config.TryGetCivilisationCatalog(out var catalog, out _);
                var proof = catalog.Points.Single(p => p.Id == "han.proof");
                Game.Interface.GetSystem<UnitSystem>().TryUpdateTransform(1, proof.Position, Quaternion.identity);
                Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(System.IO.Path.Combine(blocked, "child"));
                Assert.That(Game.Interface.GetSystem<CivilisationSearchSystem>().TryBeginSearch(1, proof.Id, out var error), Is.True, error);
                Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(3.1f);
                Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().Records, Is.Empty);
                Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited(proof.Id), Is.False);
            }
            finally { fixture.TearDown(); if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        }
    }
}
