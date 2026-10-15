using System;
using System.IO;
using Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class BaseCampLayoutTests
    {
        private static TempBuildingDefinition Building(string id) => new TempBuildingDefinition(id, id, "", "", "Prefabs/Building/Example",
            Array.Empty<ItemCostDefinition>(), 1, .5f, 20, 0, false, 1);
        [Test]
        public void DefaultLayoutIsSeededOnceAndNeverRestoresDemolishedProps()
        {
            var world = new BaseWorldSaveData();
            var layout = new[] { new BaseCampLayoutDefinition("table", "camp_table", new Vector3(4, 0, 0), 90, .7f) };
            Assert.That(BaseCampLayoutInitializer.TryInitialize(world, layout, Building, out var error), Is.True, error);
            Assert.That(world.TemporaryBuildings.Count, Is.EqualTo(1));
            Assert.That(world.TemporaryBuildings[0].UniformScale, Is.EqualTo(.7f));
            var clone = world.Clone();
            clone.TemporaryBuildings.Clear();
            Assert.That(BaseCampLayoutInitializer.TryInitialize(clone, layout, Building, out error), Is.True, error);
            Assert.That(clone.TemporaryBuildings, Is.Empty);
        }
        [Test]
        public void ExistingBuildingsKeepIdentityAndConflictingDefaultsAreSkippedPermanently()
        {
            var world = new BaseWorldSaveData();
            world.TemporaryBuildings.Add(new ExpeditionTemporaryBuildingSaveData { RuntimeId = 7, ConfigId = "old", Pose = new ExpeditionPoseData { X = 4 } });
            var layout = new[] { new BaseCampLayoutDefinition("conflict", "table", new Vector3(4, 0, 0), 0, 1),
                new BaseCampLayoutDefinition("free", "bench", new Vector3(-4, 0, 0), 0, 1) };
            Assert.That(BaseCampLayoutInitializer.TryInitialize(world, layout, Building, out var error), Is.True, error);
            Assert.That(world.TemporaryBuildings.Count, Is.EqualTo(2));
            Assert.That(world.TemporaryBuildings[0].RuntimeId, Is.EqualTo(7));
            Assert.That(world.TemporaryBuildings[1].RuntimeId, Is.GreaterThan(7));
            Assert.That(world.TemporaryBuildings[1].ConfigId, Is.EqualTo("bench"));
        }
        [Test]
        public void InvalidDefaultFailsWithoutAppendingOrMarkingTheWorld()
        {
            var world = new BaseWorldSaveData();
            var layout = new[] { new BaseCampLayoutDefinition("valid", "table", Vector3.zero, 0, 1),
                new BaseCampLayoutDefinition("invalid", "bench", Vector3.one * 4, 0, float.NaN) };
            Assert.That(BaseCampLayoutInitializer.TryInitialize(world, layout, Building, out _), Is.False);
            Assert.That(world.CampLayoutInitialized, Is.False);
            Assert.That(world.TemporaryBuildings, Is.Empty);
        }

        [Test]
        public void LegacyVersionsPreserveBuildingsAndDefaultTheirScaleBeforeInitialization()
        {
            var root = Path.Combine(Path.GetTempPath(), "AnotherDayCampMigration", Guid.NewGuid().ToString("N"));
            var store = new SaveFileStore(root);
            Directory.CreateDirectory(store.SavesDirectory);
            try
            {
                foreach (var version in new[] { 1, 2, 3 })
                {
                    var data = new BaseSaveData();
                    data.World.TemporaryBuildings.Add(new ExpeditionTemporaryBuildingSaveData { RuntimeId = 7, ConfigId = "old", Pose = new ExpeditionPoseData { X = 4 }, LoadedAmmo = 3 });
                    var json = JObject.FromObject(data);
                    json["Version"] = version;
                    ((JObject)json["World"]).Remove("CampLayoutInitialized");
                    ((JObject)json["World"]["TemporaryBuildings"][0]).Remove("UniformScale");
                    File.WriteAllText(store.BasePath, json.ToString());
                    var result = store.InspectBase();
                    Assert.That(result.IsValid, Is.True, result.Error);
                    Assert.That(result.Data.Version, Is.EqualTo(BaseSaveData.CurrentVersion));
                    Assert.That(result.Data.World.CampLayoutInitialized, Is.False);
                    Assert.That(result.Data.World.TemporaryBuildings[0].RuntimeId, Is.EqualTo(7));
                    Assert.That(result.Data.World.TemporaryBuildings[0].LoadedAmmo, Is.EqualTo(3));
                    Assert.That(result.Data.World.TemporaryBuildings[0].UniformScale, Is.EqualTo(1));
                }
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void EmptyInitializedCampSurvivesDiskRoundTripAndNeverReseeds()
        {
            var root = Path.Combine(Path.GetTempPath(), "AnotherDayCampRoundTrip", Guid.NewGuid().ToString("N"));
            var store = new SaveFileStore(root);
            try
            {
                var data = new BaseSaveData();
                data.World.CampLayoutInitialized = true;
                Assert.That(store.SaveBase(data, out var error), Is.True, error);
                var result = store.InspectBase();
                Assert.That(result.IsValid, Is.True, result.Error);
                Assert.That(BaseCampLayoutInitializer.TryInitialize(result.Data.World,
                    new[] { new BaseCampLayoutDefinition("table", "camp_table", Vector3.right * 4, 90, .7f) }, Building, out error), Is.True, error);
                Assert.That(result.Data.World.TemporaryBuildings, Is.Empty);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void MobilePrefabsHaveVerticalEntriesAndGeneratedPlacementReferences()
        {
            var mobile = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab").GetComponent<MobileJoystickUI>();
            Assert.That(mobile.GetComponent<ReferenceCollector>(), Is.Not.Null);
            Assert.That(mobile.InventoryButton, Is.Not.Null);
            Assert.That(mobile.BuildButton, Is.Not.Null);
            var bag = (RectTransform)mobile.InventoryButton.transform;
            var build = (RectTransform)mobile.BuildButton.transform;
            Assert.That(build.parent, Is.SameAs(bag.parent));
            Assert.That(build.anchoredPosition.x - bag.anchoredPosition.x, Is.EqualTo(96f));
            Assert.That(build.anchoredPosition.y, Is.EqualTo(bag.anchoredPosition.y));
            var select = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/TemporaryBuilding/TemporaryBuildingSelect.prefab").GetComponent<TemporaryBuildingSelectUI>();
            Assert.That(select.BtnDestroy, Is.Not.Null);
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/TemporaryBuilding/TemporaryBuildingHud.prefab").GetComponent<TemporaryBuildingHudUI>();
            Assert.That(hud.TouchControlsRoot, Is.Not.Null);
            Assert.That(hud.BtnRotate, Is.Not.Null);
            Assert.That(hud.BtnConfirm, Is.Not.Null);
            Assert.That(hud.BtnCancel, Is.Not.Null);
            foreach (var button in new[] { select.BtnDestroy, hud.BtnRotate, hud.BtnConfirm, hud.BtnCancel })
            {
                var localized = button.GetComponentInChildren<LocalizedText>(true);
                Assert.That(localized, Is.Not.Null);
                Assert.That(localized.Texts, Is.Empty);
                foreach (var language in new[] { "zh-CN", "en-US" })
                    Assert.That(JObject.Parse(File.ReadAllText($"Assets/Game/Config/Localization/{language}.json"))[localized.Key], Is.Not.Null, localized.Key);
            }
        }
    }
}
