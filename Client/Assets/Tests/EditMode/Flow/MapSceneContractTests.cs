using NUnit.Framework;
namespace Game.Tests
{
    /// <summary>街机地图目录与加载快照契约；不注册已移除的基地或临时建筑领域。</summary>
    public sealed class MapSceneContractTests
    {
        [Test]
        public void CatalogOwnsOnlyArcadeIdentityAndLeavesSceneAddressToConfiguration()
        {
            Assert.That(MapCatalog.TryGet(MapIds.Arcade, out var definition), Is.True);
            Assert.That(definition.Kind, Is.EqualTo(MapKind.Arcade));
            Assert.That(MapCatalog.TryGet("map.another-day.base", out _), Is.False);
        }
        [Test]
        public void LoadedMapKeepsArcadePreparationMode()
        {
            var model = new MapSceneModel();
            model.SetLoaded(MapIds.Arcade, null);
            Assert.That(model.CurrentMapId, Is.EqualTo(MapIds.Arcade));
            Assert.That(model.Status.Value, Is.EqualTo(MapLoadStatus.Loaded));
            Assert.That(model.CurrentMode, Is.EqualTo(MapPreparationMode.ArcadePlayerOnly));
        }
        [Test]
        public void ClearReleasesMapIdentityAndReturnsToIdle()
        {
            var model = new MapSceneModel();
            model.SetLoaded(MapIds.Arcade, null);
            model.Clear();
            Assert.That(model.CurrentMapId, Is.Null);
            Assert.That(model.CurrentHandle, Is.Null);
            Assert.That(model.Status.Value, Is.EqualTo(MapLoadStatus.Idle));
        }
    }
}