using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests
{
    public sealed class HeJiaVillageLocalEntryTests
    {
        [Test]
        public void LocalMapIsSelectableWithSceneAndPlayerSpawn()
        {
            const string mapId = MapIds.HeJiaVillage;
            Assert.That(MapCatalog.TryGet(mapId, out var definition), Is.True);
            Assert.That(definition.Kind, Is.EqualTo(MapKind.Task));
            Assert.That(definition.TaskTargetKills, Is.Zero);
            Assert.That(definition.UnlockedByDefault, Is.True);
            Assert.That(definition.UnlockCostItemId, Is.Zero);
            Assert.That(definition.UnlockCostAmount, Is.Zero);
            Assert.That(MapCatalog.GetExpeditionMaps(), Has.Some.Matches<MapDefinition>(map => map.MapId == mapId));
            Assert.That(MapCatalog.GetExpeditionMaps().Count(map => map.MapId == mapId), Is.EqualTo(1));

            var provider = new LubanRuntimeConfigProvider(() =>
                new Tables(name => new Luban.ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetMap(mapId, out var asset, out var mapError), Is.True, mapError);
            Assert.That(asset.SceneAddresses, Has.Count.EqualTo(1));
            Assert.That(asset.SceneAddresses[0], Is.EqualTo("Scenes/GeneratedMaps/HeJiaVillageLocalPreview/HeJiaVillageLocalPreview"));
            Assert.That(provider.TryGetMapSpawns(mapId, out var spawns, out var spawnError), Is.True, spawnError);
            Assert.That(spawns, Has.Count.EqualTo(1));
            Assert.That(spawns[0].PositionX, Is.EqualTo(220f).Within(0.1f));
            Assert.That(spawns[0].PositionZ, Is.EqualTo(460f).Within(0.1f));
        }

        [Test]
        public void LocalSceneProvidesRuntimeMapRootsAndTerrain()
        {
            const string path = "Assets/Game/Scenes/GeneratedMaps/HeJiaVillageLocalPreview/HeJiaVillageLocalPreview.unity";
            var scene = SceneManager.GetSceneByPath(path);
            var openedForTest = !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            }

            try
            {
                var context = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MapContext>(true)).ToArray();
                Assert.That(context, Has.Length.EqualTo(1));
                Assert.That(context[0].Validate(out var error), Is.True, error);
                Assert.That(scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Terrain>(true)).ToArray(), Has.Length.EqualTo(1));
                Assert.That(scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TerrainCollider>(true)).ToArray(), Has.Length.EqualTo(1));
            }
            finally
            {
                if (openedForTest)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
