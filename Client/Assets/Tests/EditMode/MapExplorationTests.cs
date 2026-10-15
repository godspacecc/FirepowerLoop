using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Game.Tests
{
    public sealed class MapExplorationTests
    {
        [Test] public void StartingNewTripClearsValleyFogEvenIfSeedRepeats()
        {
            var model=new MapExplorationModel();var valley=new MapDisplayDefinition(MapIds.CivilisationValley,"image",0,0,1024,1024);
            var home=new MapDisplayDefinition(MapIds.Base,"image",0,0,30,30);
            model.SynchronizeLayoutIdentity(valley,"3:123:0");model.Reveal(valley,Vector3.zero,24);model.Reveal(home,Vector3.zero,3);
            var method=typeof(MapExplorationModel).GetMethod("ForgetMap",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            Assert.That(method,Is.Not.Null);method.Invoke(model,new object[]{MapIds.CivilisationValley});
            Assert.That(model.IsExplored(valley,Vector3.zero),Is.False);Assert.That(model.IsExplored(home,Vector3.zero),Is.True);
        }
        [Test]
        public void RandomLayoutIdentitySurvivesSaveAndNewLayoutClearsOnlyItsFog()
        {
            var model = new MapExplorationModel();
            var valley = new MapDisplayDefinition(MapIds.CivilisationValley, "image", 0, 0, 1024, 1024);
            var home = new MapDisplayDefinition(MapIds.Base, "image", 0, 0, 30, 30);
            var sync = typeof(MapExplorationModel).GetMethod("SynchronizeLayoutIdentity");
            Assert.That(sync, Is.Not.Null, "随机河谷不能复用上一布局的探索位图");
            sync.Invoke(model, new object[] { valley, "2:123:0" });
            model.Reveal(valley, Vector3.zero, 24);
            model.Reveal(home, Vector3.zero, 3);
            var copy = new MapExplorationModel();
            copy.Restore(model.Capture());
            Assert.That((bool)sync.Invoke(copy, new object[] { valley, "2:123:0" }), Is.False);
            Assert.That(copy.IsExplored(valley, Vector3.zero), Is.True);
            var revision = copy.Revision;
            Assert.That((bool)sync.Invoke(copy, new object[] { valley, "2:456:0" }), Is.True);
            Assert.That(copy.Revision, Is.EqualTo(revision + 1));
            Assert.That(copy.GetExploredFraction(valley), Is.Zero);
            Assert.That(copy.IsExplored(home, Vector3.zero), Is.True);
        }

        [Test]
        public void LegacyExplorationRecordsRemainValidAndMalformedIdentityIsRejected()
        {
            var model = new MapExplorationModel();
            var display = new MapDisplayDefinition(MapIds.Base, "image", 0, 0, 30, 30);
            model.Reveal(display, Vector3.zero, 3);
            var data = model.Capture();
            ((JObject)data["maps"][MapIds.Base]).Remove("layoutIdentity");
            var restored = new MapExplorationModel();
            restored.Restore(data);
            Assert.That(restored.SynchronizeLayoutIdentity(display, string.Empty), Is.False);
            Assert.That(restored.IsExplored(display, Vector3.zero), Is.True);
            data["maps"][MapIds.Base]["layoutIdentity"] = 123;
            Assert.That(restored.Validate(data, out _), Is.False);
            Assert.Throws<System.ArgumentException>(() => restored.Restore(data));
            Assert.That(restored.IsExplored(display, Vector3.zero), Is.True);
        }

        [Test]
        public void ExplorationHasPersistentDomainOwner()
        {
            var type = typeof(Game).Assembly.GetType("Game.MapExplorationModel");
            Assert.That(type, Is.Not.Null, "探索记录必须独立于地图界面的生命周期");
            Assert.That(typeof(IScopedSaveSectionProvider).IsAssignableFrom(type), Is.True);
        }

        [Test]
        public void ManagementMapPageExistsInsideBag()
        {
            var page = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Page/MapManagement.prefab");
            Assert.That(page, Is.Not.Null);
            var bag = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Bag.prefab");
            Assert.That(bag.transform.Find("ManagementRoot/MapManagement"), Is.Not.Null);
        }

        [Test]
        public void RevealsOnlyNearbyCellsAndRepeatedVisitsAreIdempotent()
        {
            var model = new MapExplorationModel();
            var display = new MapDisplayDefinition("a", "image", 0, 0, 100, 100);
            Assert.That(model.Reveal(display, Vector3.zero, 5), Is.True);
            Assert.That(model.IsExplored(display, Vector3.zero), Is.True);
            Assert.That(model.IsExplored(display, new Vector3(30, 0, 30)), Is.False);
            var revision = model.Revision;
            Assert.That(model.Reveal(display, Vector3.zero, 5), Is.False);
            Assert.That(model.Revision, Is.EqualTo(revision));
            Assert.That(model.GetExploredFraction(display), Is.InRange(.005f, .02f));
        }

        [Test]
        public void SaveRoundTripPreservesTwoRegionsAndOldSavesStartBlank()
        {
            var model = new MapExplorationModel();
            var a = new MapDisplayDefinition("a", "a", 0, 0, 100, 100);
            var b = new MapDisplayDefinition("b", "b", 0, 0, 100, 100);
            model.Reveal(a, Vector3.zero, 5);
            model.Reveal(b, new Vector3(30, 0, 30), 5);
            var copy = new MapExplorationModel();
            copy.Restore(model.Capture());
            Assert.That(copy.IsExplored(a, Vector3.zero), Is.True);
            Assert.That(copy.IsExplored(b, Vector3.zero), Is.False);
            Assert.That(copy.IsExplored(b, new Vector3(30, 0, 30)), Is.True);
            Assert.That(copy.Scope, Is.EqualTo(SaveSectionScope.SharedPlayer));
            copy.Restore(null);
            Assert.That(copy.GetExploredFraction(a), Is.Zero);
            copy.Restore(JValue.CreateNull());
            Assert.That(copy.GetExploredFraction(a), Is.Zero);
        }

        [Test]
        public void RejectsCorruptSavesWithoutReplacingExistingExploration()
        {
            var model = new MapExplorationModel();
            var display = new MapDisplayDefinition("a", "a", 0, 0, 100, 100);
            model.Reveal(display, Vector3.zero, 5);
            var data = model.Capture();
            data["maps"]["a"]["bits"] = "broken";
            Assert.That(model.Validate(data, out _), Is.False);
            Assert.Throws<System.ArgumentException>(() => model.Restore(data));
            Assert.That(model.IsExplored(display, Vector3.zero), Is.True);
        }

        [Test]
        public void BoundsAndInvalidPositionsCannotRevealOtherRegions()
        {
            var model = new MapExplorationModel();
            var display = new MapDisplayDefinition("a", "a", 0, 0, 100, 100);
            Assert.That(model.Reveal(display, new Vector3(51, 0, 0), 5), Is.False);
            Assert.That(model.Reveal(display, new Vector3(float.NaN, 0, 0), 5), Is.False);
            Assert.That(model.Reveal(display, Vector3.zero, float.PositiveInfinity), Is.False);
            Assert.That(model.Reveal(display, new Vector3(50, 0, 50), 5), Is.True);
            Assert.That(model.IsExplored(display, new Vector3(50, 0, 50)), Is.True);
            Assert.That(model.IsExplored(new MapDisplayDefinition("a", "a", 10, 0, 100, 100), new Vector3(50, 0, 50)), Is.False);
        }
    }
}
