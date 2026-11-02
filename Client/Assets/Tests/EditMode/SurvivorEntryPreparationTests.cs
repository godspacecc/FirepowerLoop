using System;
using NUnit.Framework;
namespace Game.Tests
{
    /// <summary>幸存者必须具有独立的资源准备模式，避免复用不完整的闯关准备现场。</summary>
    public sealed class SurvivorEntryPreparationTests
    {
        [Test] public void ContextCarriesScopeWithoutChangingDefault()
        {
            var root = new UnityEngine.GameObject("PreparationScope");
            try
            {
                var scene = root.AddComponent<MapContext>();
                var spawns = Array.Empty<MapSpawnDefinition>();
                var normal = MapPreparationContext.CreateArcade(MapIds.Arcade, 1, scene, spawns);
                var survivor = MapPreparationContext.CreateSurvivor(MapIds.Arcade, 2, scene, spawns);
                Assert.That(normal.Mode, Is.EqualTo(MapPreparationMode.ArcadePlayerOnly));
                Assert.That(survivor.Mode, Is.EqualTo(MapPreparationMode.Survivor));
                Assert.That(survivor.Scene, Is.SameAs(scene));
                Assert.That(survivor.MapInstanceId, Is.EqualTo(2));
                Assert.Throws<ArgumentOutOfRangeException>(() => MapPreparationContext.CreateSurvivor(MapIds.Arcade, 0, scene, spawns));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        [Test] public void CommittedScopeClearsWithoutLeakingIntoArcade()
        {
            var model = new MapSceneModel();
            model.SetLoaded(MapIds.Arcade, null, MapPreparationMode.Survivor);
            Assert.That(model.CurrentMode, Is.EqualTo(MapPreparationMode.Survivor));
            model.Clear();
            model.SetLoaded(MapIds.Arcade, null);
            Assert.That(model.CurrentMode, Is.EqualTo(MapPreparationMode.ArcadePlayerOnly));
        }
        [Test] public void SurvivorHasDistinctPreparationMode()
        {
            Assert.That(Enum.GetNames(typeof(MapPreparationMode)), Does.Contain("Survivor"));
            Assert.That(typeof(MapPreparationContext).GetMethod("CreateSurvivor"), Is.Not.Null);
            Assert.That(typeof(MapSceneSystem).GetMethod("LoadSurvivorMapAsync"), Is.Not.Null);
            Assert.That(typeof(ArcadeSceneFlowSystem).GetMethod("EnterSurvivorMapAsync"), Is.Not.Null);
        }
    }
}
