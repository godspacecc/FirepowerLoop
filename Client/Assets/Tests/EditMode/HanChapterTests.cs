using System.Collections.Generic;
using System.Linq;
using Game;
using Luban;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>覆盖首章多科技、独立剧情结算和配置到地图的连通契约。</summary>
    public sealed class HanChapterTests
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
        [Test] public void MoreThanTwoTechnologiesCanBeSavedButDuplicatesAreRejected()
        {
            var state = CivilisationExpeditionModel.Empty();
            state["technologies"] = new JArray("technology.dry-ration", "technology.iron-tools", "technology.steel-blade");
            var model = new CivilisationExpeditionModel();
            Assert.That(model.Validate(state, out var error), Is.True, error);
            ((JArray)state["technologies"]).Add("technology.iron-tools");
            Assert.That(model.Validate(state, out _), Is.False);
        }
        [Test] public void StoryReturnCompletesChapterWithoutAnyTechnology()
        {
            var basis = Basis(); var journey = Journey();
            journey.Sections[CivilisationExpeditionModel.Section]["chapters"] = new JArray("chapter.han-transport");
            Assert.That(CivilisationSettlementPolicy.TryApply(basis, journey, false, Config(), out var error), Is.True, error);
            Assert.That(basis.Sections[CivilisationTechnologyModel.Section]["completedChapters"]?.Values<string>(), Does.Contain("chapter.han-transport"));
            Assert.That(basis.Sections[CivilisationTechnologyModel.Section]["unlocked"], Is.Empty);
        }
        [Test] public void DefeatDoesNotCompleteChapter()
        {
            var basis = Basis(); var before = JObject.FromObject(basis.Sections); var journey = Journey();
            journey.Sections[CivilisationExpeditionModel.Section]["chapters"] = new JArray("chapter.han-transport");
            Assert.That(CivilisationSettlementPolicy.TryApply(basis, journey, true, Config(), out var error), Is.True, error);
            Assert.That(JToken.DeepEquals(before, JObject.FromObject(basis.Sections)), Is.True);
        }
        [Test] public void UnknownChapterRejectsWholeSettlement()
        {
            var basis = Basis(); var before = JObject.FromObject(basis.Sections); var journey = Journey();
            journey.Sections[CivilisationExpeditionModel.Section]["chapters"] = new JArray("chapter.missing");
            Assert.That(CivilisationSettlementPolicy.TryApply(basis, journey, false, Config(), out _), Is.False);
            Assert.That(JToken.DeepEquals(before, JObject.FromObject(basis.Sections)), Is.True);
        }
        [Test] public void RepeatReturnDoesNotRepeatChapterReward()
        {
            var basis = Basis(); var journey = Journey();
            journey.Sections[CivilisationExpeditionModel.Section]["chapters"] = new JArray("chapter.han-transport");
            Assert.That(CivilisationSettlementPolicy.TryApply(basis, journey, false, Config(), out var error), Is.True, error);
            Assert.That(CivilisationSettlementPolicy.TryApply(basis, journey, false, Config(), out error), Is.True, error);
            Assert.That(basis.Sections[CivilisationTechnologyModel.Section]["completedChapters"]?.Count(), Is.EqualTo(1));
            Assert.That(basis.Sections[CivilisationTechnologyModel.Section]["receipt"]["newChapters"], Is.Empty);
        }
        [Test] public void NewRecipesHaveUsableOutputsAndClearUpgrades()
        {
            var config = Config(); Assert.That(config.TryGetCivilisationCatalog(out var catalog, out var error), Is.True, error);
            Assert.That(catalog.Technologies.Select(t => t.Id), Does.Contain("technology.iron-tools"));
            Assert.That(config.TryGetWeapon(32, out var axe, out error), Is.True, error);
            Assert.That(config.TryGetWeapon(17, out var oldAxe, out error), Is.True, error);
            Assert.That(axe.TreeHarvestEfficiency, Is.GreaterThan(oldAxe.TreeHarvestEfficiency));
            Assert.That(config.TryGetWeapon(34, out var blade, out error), Is.True, error);
            Assert.That(blade.AttackDamage, Is.GreaterThan(oldAxe.AttackDamage));
            foreach (var recipe in catalog.Productions)
            {
                Assert.That(config.TryGetItem(recipe.OutputItemId, out _, out error), Is.True, error);
                Assert.That(catalog.ReturnValues[recipe.OutputItemId], Is.LessThanOrEqualTo(recipe.CostGold));
            }
        }
        [Test] public void NewLayoutContainsEveryTechnologyAndIndependentStoryPoint()
        {
            var config = Config(); Assert.That(config.TryGetCivilisationCatalog(out var catalog, out var error), Is.True, error);
            Assert.That(config.TryGetValleyGenerationProfile(MapIds.CivilisationValley, out var profile, out error), Is.True, error);
            Assert.That(ValleyLayoutGenerator.TryGenerate(37, 0, profile, catalog, out var layout, out error), Is.True, error);
            Assert.That(layout.Points.Any(p => p.TemplateId == "han.proof"), Is.True);
            var templates = layout.Points.Select(p => catalog.Points.Single(t => t.Id == p.TemplateId)).ToArray();
            foreach (var technology in catalog.Technologies)
                Assert.That(templates.Any(p => p.TechnologyId == technology.Id), Is.True, technology.Id);
        }
        [Test] public void WorkshopHasSerializedRecipeNavigation()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Civilisation/CivilisationWorkshop.prefab");
            var view = prefab.GetComponent<CivilisationWorkshopUI>();
            foreach (var name in new[] { "BtnPrevious", "BtnNext" })
            {
                var field = typeof(CivilisationWorkshopUI).GetField(name);
                Assert.That(field, Is.Not.Null, name);
                Assert.That(field.GetValue(view) as UnityEngine.Object, Is.Not.Null, name);
            }
        }
        [Test] public void GuardsFollowSavedSearchAnchorsInsteadOfStaticCoordinates()
        {
            var type = typeof(Game).Assembly.GetType("Game.ValleyChapterSpawns");
            Assert.That(type, Is.Not.Null, "随机河谷敌人定位尚未接入");
            var layout = new ValleyLayoutSaveData { Heights = new float[257 * 257], WorldSizeMeters = 1024, HeightSampleSpacingMeters = 4 };
            layout.Points.Add(new ValleyPointData { Id = "generated.han.bow", TemplateId = "han.bow", Pose = new ValleyPosition { X = 120, Z = -70 } });
            var input = new[] { new MapSpawnDefinition("han.bow.guard", MapIds.CivilisationValley, MapSpawnObjectType.Enemy, "enemy.han.archer", 5, 1, 0, 180) };
            var output = ((IEnumerable<MapSpawnDefinition>)type.GetMethod("Resolve").Invoke(null, new object[] { layout, input })).ToArray();
            Assert.That(output.Single().LocalPosition.x, Is.EqualTo(125));
            Assert.That(output.Single().LocalPosition.z, Is.EqualTo(-70));
        }
        [Test] public void OldLayoutWithoutChapterPointsDoesNotGainGuards()
        {
            var type = typeof(Game).Assembly.GetType("Game.ValleyChapterSpawns");
            Assert.That(type, Is.Not.Null);
            var layout = new ValleyLayoutSaveData();
            var input = new[] { new MapSpawnDefinition("han.bow.guard", MapIds.CivilisationValley, MapSpawnObjectType.Enemy, "enemy.han.archer", 5, 1, 0, 180) };
            Assert.That((IEnumerable<MapSpawnDefinition>)type.GetMethod("Resolve").Invoke(null, new object[] { layout, input }), Is.Empty);
        }
    }
}
