using System.Linq;
using System.IO;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests
{
    public sealed class NpcRuntimeConfigTests
    {
        [Test]
        public void ProviderLoadsBaseNpcConfigsAndSpawns()
        {
            var provider = new LubanRuntimeConfigProvider(() =>
                new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", $"{name}.bytes")))));
            AssertNpc(provider, "npc.base.map-guide", global::NpcInteractionKind.MapSelection, string.Empty);
            AssertNpc(provider, "npc.base.warehouse-keeper", global::NpcInteractionKind.Warehouse, string.Empty);
            AssertNpc(provider, "npc.base.crafter", global::NpcInteractionKind.Craft, string.Empty);
            AssertNpc(provider, "npc.base.merchant", global::NpcInteractionKind.Shop, "shop.base.supplies");
            AssertNpc(provider, "npc.base.arcade", global::NpcInteractionKind.Arcade, string.Empty);

            foreach (var legacyId in new[]
                     {
                         "npc.base.survival-guide",
                         "npc.base.mission-guide",
                         "npc.base.hejia-village-guide"
                     })
            {
                Assert.That(provider.TryGetNpc(legacyId, out _, out _), Is.False, legacyId);
            }

            Assert.That(provider.TryGetMapSpawns(MapIds.Base, out var spawns, out var error), Is.True, error);
            var npcs = spawns.Where(spawn => spawn.ObjectType == MapSpawnObjectType.Npc).ToArray();
            Assert.That(npcs.Select(spawn => spawn.Id), Is.EquivalentTo(new[]
            {
                "spawn.base.npc.map-guide",
                "spawn.base.npc.warehouse-keeper",
                "spawn.base.npc.crafter",
                "spawn.base.npc.merchant",
                "spawn.base.npc.arcade"
            }));
            Assert.That(npcs.Count(spawn => spawn.ConfigId == "npc.base.map-guide"), Is.EqualTo(1));
            Assert.That(npcs.Any(spawn => spawn.Id == "spawn.base.npc.survival-guide" ||
                                          spawn.Id == "spawn.base.npc.mission-guide" ||
                                          spawn.Id == "spawn.base.npc.hejia-village-guide"), Is.False);
        }

        [Test]
        public void GrayboxNpcPrefabKeepsPresentationContract()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxNpc.prefab");
            Assert.That(prefab, Is.Not.Null);
            var view = prefab.GetComponent<NpcView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<NpcView>(true).Length, Is.EqualTo(1));
            var collider = prefab.GetComponent<Collider>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.enabled, Is.True);
            Assert.That(collider.isTrigger, Is.True);
            Assert.That(view.TryValidatePresentation(out var error), Is.True, error);
        }

        [Test]
        public void BaseNpcAppearancesUseDistinctCityModels()
        {
            var provider = new LubanRuntimeConfigProvider(() =>
                new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", $"{name}.bytes")))));
            var expected = new[]
            {
                (Id: "npc.base.map-guide", Prefab: "BaseNpcMapGuide", Mesh: "Character_Male_Police"),
                (Id: "npc.base.warehouse-keeper", Prefab: "BaseNpcWarehouseKeeper", Mesh: "Character_Male_Jacket"),
                (Id: "npc.base.crafter", Prefab: "BaseNpcCrafter", Mesh: "Character_Female_Coat"),
                (Id: "npc.base.merchant", Prefab: "BaseNpcMerchant", Mesh: "Character_BusinessWoman"),
                (Id: "npc.base.arcade", Prefab: "BaseNpcArcade", Mesh: "Character_Female_Jacket")
            };

            foreach (var entry in expected)
            {
                Assert.That(provider.TryGetNpc(entry.Id, out var npc, out var error), Is.True, error);
                Assert.That(npc.PrefabAddress, Is.EqualTo($"Prefabs/Characters/{entry.Prefab}"), entry.Id);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/Game/Prefabs/Characters/{entry.Prefab}.prefab");
                Assert.That(prefab, Is.Not.Null, entry.Id);
                Assert.That(prefab.GetComponent<NpcView>(), Is.Not.Null, entry.Id);
                Assert.That(prefab.GetComponent<Collider>()?.isTrigger, Is.True, entry.Id);
                var view = prefab.GetComponent<NpcView>();
                Assert.That(view.TryValidatePresentation(out error), Is.True, error);

                var visibleMeshes = prefab.GetComponentsInChildren<SkinnedMeshRenderer>()
                    .Where(renderer => renderer.enabled)
                    .ToArray();
                Assert.That(visibleMeshes.Select(renderer => renderer.sharedMesh.name),
                    Is.EqualTo(new[] { entry.Mesh }), entry.Id);
                Assert.That(visibleMeshes[0].sharedMaterial.name, Is.EqualTo("PlayerCity"), entry.Id);
                var animator = prefab.GetComponentInChildren<Animator>();
                Assert.That(animator?.avatar?.isHuman, Is.True, entry.Id);
                Assert.That(animator.runtimeAnimatorController, Is.Not.Null, entry.Id);
            }
        }

        [Test]
        public void BaseMapHasNoLegacyTerminalsOrPreplacedNpcs()
        {
            var previous = SceneManager.GetActiveScene().path;
            var scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/BaseMap.unity", OpenSceneMode.Single);
            try
            {
                Assert.That(FindNamed(scene, "SurvivalPortal"), Is.EqualTo(0));
                Assert.That(FindNamed(scene, "TaskMapPortal"), Is.EqualTo(0));
                Assert.That(FindNamed(scene, "BaseInventoryTerminal"), Is.EqualTo(0));
                Assert.That(FindNamed(scene, "BaseCraftTerminal"), Is.EqualTo(0));
                Assert.That(FindComponents<NpcView>(scene), Is.EqualTo(0));
                Assert.That(CountMissingScripts(scene), Is.EqualTo(0));
            }
            finally
            {
                if (!string.IsNullOrEmpty(previous))
                {
                    EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                }
            }
        }

        private static void AssertNpc(LubanRuntimeConfigProvider provider, string id, global::NpcInteractionKind kind, string param)
        {
            Assert.That(provider.TryGetNpc(id, out var npc, out var error), Is.True, error);
            Assert.That(npc.InteractionKind, Is.EqualTo(kind));
            Assert.That(npc.InteractionParam, Is.EqualTo(param));
        }

        private static int FindNamed(Scene scene, string objectName)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Count(transform => transform.gameObject.name == objectName);
        }

        private static int FindComponents<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .Count();
        }

        private static int CountMissingScripts(Scene scene)
        {
            return scene.GetRootGameObjects()
                .Sum(root => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root));
        }
    }
}
