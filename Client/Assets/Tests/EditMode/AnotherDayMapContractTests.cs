using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Game.Tests
{
    public sealed class AnotherDayMapContractTests
    {
        [Test]
        public void CatalogContainsBaseMapAddress()
        {
            Assert.IsTrue(MapCatalog.TryGet(MapIds.Base, out var definition));
            Assert.That(definition.MapId, Is.EqualTo(MapIds.Base));
            Assert.That(definition.Kind, Is.EqualTo(MapKind.Base));
        }

        [Test]
        public void SurvivalSampleMapIsRegisteredForStageTwo()
        {
            Assert.IsTrue(MapCatalog.TryGet(MapIds.SurvivalSample, out var definition));
            Assert.That(definition.Kind, Is.EqualTo(MapKind.Survival));
            Assert.That(definition.SurvivalTargetGameMinutes, Is.EqualTo(PlayerConditionRules.GameMinutesPerDay).Within(0.001f));
        }

        [Test]
        public void TaskSampleMapIsRegisteredForStageFive()
        {
            Assert.IsTrue(MapCatalog.TryGet(MapIds.MissionSample, out var definition));
            Assert.That(definition.Kind, Is.EqualTo(MapKind.Task));
            Assert.That(definition.TaskTargetKills, Is.EqualTo(2));
        }

        [Test]
        public void MapContextRejectsMissingRoots()
        {
            var root = new GameObject("Context");
            try
            {
                var context = root.AddComponent<MapContext>();
                Assert.IsFalse(context.Validate(out var error));
                Assert.That(error, Is.Not.Empty);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BaseMapSceneMatchesAnotherDayContract()
        {
            WithOpenedScene("Assets/Game/Scenes/BaseMap.unity", scene =>
            {
                var productContexts = FindInScene<MapContext>(scene);
                Assert.That(productContexts.Count, Is.EqualTo(1));
                Assert.IsTrue(productContexts[0].Validate(out var error), error);
                AssertMapContentRootContract(productContexts[0], scene);

                Assert.That(FindInScene<BaseMapPortal>(scene).Count, Is.EqualTo(0));
                Assert.That(FindInSceneByTypeName(scene, "Game.BaseMapSelectionTerminal").Count, Is.EqualTo(0));
                Assert.That(FindInSceneByTypeName(scene, "Game.BaseInventoryTerminal").Count, Is.EqualTo(0));
                Assert.That(FindInSceneByTypeName(scene, "Game.BaseCraftTerminal").Count, Is.EqualTo(0));
                Assert.That(FindNamed(scene, "SurvivalPortal").Count, Is.EqualTo(0));
                Assert.That(FindNamed(scene, "TaskMapPortal").Count, Is.EqualTo(0));
                Assert.That(FindNamed(scene, "BaseInventoryTerminal").Count, Is.EqualTo(0));
                Assert.That(FindNamed(scene, "BaseCraftTerminal").Count, Is.EqualTo(0));
                Assert.That(FindInScene<NpcView>(scene).Count, Is.EqualTo(0));
                Assert.That(FindInScene<Camera>(scene).Count, Is.EqualTo(0));
                Assert.That(FindInScene<AudioListener>(scene).Count, Is.EqualTo(0));
                Assert.That(FindInScene<EventSystem>(scene).Count, Is.EqualTo(0));
                Assert.That(FindInScene<GameStart>(scene).Count, Is.EqualTo(0));
                Assert.That(FindInScene<PlayerView>(scene).Count, Is.EqualTo(0));
            });
        }

        [Test]
        public void SurvivalSampleMapSceneMatchesAnotherDayContract()
        {
            WithOpenedScene("Assets/Game/Scenes/SurvivalSampleMap.unity", scene =>
            {
                var productContexts = FindInScene<MapContext>(scene);
                Assert.That(productContexts.Count, Is.EqualTo(1));
                Assert.IsTrue(productContexts[0].Validate(out var error), error);
                AssertMapContentRootContract(productContexts[0], scene);

                Assert.That(FindInScene<ResourcePickupView>(scene).Count, Is.EqualTo(0));
                Assert.That(FindInScene<EnemyView>(scene).Count, Is.EqualTo(0));
                AssertEnemyPrefabKeepsPresentationContract();
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<Object>(
                        "Assets/Game/Scenes/SurvivalSampleMap/NavMesh.asset"),
                    Is.Not.Null);
                Assert.That(FindInScene<Camera>(scene).Count, Is.EqualTo(0));
                Assert.That(FindInScene<AudioListener>(scene).Count, Is.EqualTo(0));
                Assert.That(FindInScene<EventSystem>(scene).Count, Is.EqualTo(0));
                Assert.That(FindInScene<GameStart>(scene).Count, Is.EqualTo(0));
                Assert.That(FindInScene<PlayerView>(scene).Count, Is.EqualTo(0));
            });
        }

        [Test]
        public void TaskSampleMapSceneMatchesAnotherDayContract()
        {
            WithOpenedScene("Assets/Game/Scenes/TaskSampleMap.unity", scene =>
            {
                var productContexts = FindInScene<MapContext>(scene);
                Assert.That(productContexts.Count, Is.EqualTo(1));
                Assert.IsTrue(productContexts[0].Validate(out var error), error);
                AssertMapContentRootContract(productContexts[0], scene);

                Assert.That(FindInScene<EnemyView>(scene).Count, Is.EqualTo(0));
                Assert.That(FindInScene<ResourcePickupView>(scene).Count, Is.EqualTo(0));
                AssertEnemyPrefabKeepsPresentationContract();
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<Object>(
                        "Assets/Game/Scenes/TaskSampleMap/NavMesh.asset"),
                    Is.Not.Null);
                Assert.That(FindInSceneByTypeName(scene, "Game.TaskExitView").Count, Is.EqualTo(0));
                Assert.That(FindInScene<Camera>(scene).Count, Is.EqualTo(0));
                Assert.That(FindInScene<AudioListener>(scene).Count, Is.EqualTo(0));
                Assert.That(FindInScene<EventSystem>(scene).Count, Is.EqualTo(0));
                Assert.That(FindInScene<GameStart>(scene).Count, Is.EqualTo(0));
                Assert.That(FindInScene<PlayerView>(scene).Count, Is.EqualTo(0));
            });
        }

        [Test]
        public void ArcadeMapReusesConfiguredWorldInfrastructure()
        {
            Assert.IsTrue(MapCatalog.TryGet(MapIds.Arcade, out var definition));
            Assert.That(definition.Kind, Is.EqualTo(MapKind.Arcade));
        }

        [Test]
        public void HeJiaVillageIsDefaultUnlockedZeroTargetTaskMap()
        {
            Assert.That(MapCatalog.TryGet(MapIds.HeJiaVillage, out var definition), Is.True);
            Assert.That(definition.MapId, Is.EqualTo("map.hejia-village"));
            Assert.That(definition.Kind, Is.EqualTo(MapKind.Task));
            Assert.That(definition.DisplayNameKey, Is.EqualTo("map.hejia_village.name"));
            Assert.That(definition.TaskTargetKills, Is.Zero);
            Assert.That(definition.UnlockedByDefault, Is.True);
            Assert.That(definition.UnlockCostItemId, Is.Zero);
            Assert.That(definition.UnlockCostAmount, Is.Zero);
        }

        [Test]
        public void DefaultCameraProfileMatchesStageFourContract()
        {
            var profile = AssetDatabase.LoadAssetAtPath<TopDownCameraProfile>(
                "Assets/Game/Config/DefaultCameraProfile.asset");

            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.YawDegrees, Is.EqualTo(0f));
            Assert.That(profile.PitchDegrees, Is.EqualTo(75f));
            Assert.That(profile.FieldOfView, Is.EqualTo(38f));
            Assert.That(profile.FollowDistance, Is.EqualTo(14f));
        }

        [Test]
        public void TestResourcePickupPrefabHasWorldNameLabel()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<ResourcePickupView>(
                "Assets/Game/Prefabs/TestResourcePickup.prefab");

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.NameRoot, Is.Not.Null);
            Assert.That(prefab.NameLabel, Is.Not.Null);
            Assert.That(prefab.NameLabel.text, Is.EqualTo("娴嬭瘯鐗╄祫"));
            Assert.That(prefab.NameRoot.GetComponentInChildren<Canvas>(true).renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(prefab.NameRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true), Is.Empty);
        }

        [Test]
        public void SurvivalSampleMapStaticResourcePickupsHaveWorldNameLabels()
        {
            WithOpenedScene("Assets/Game/Scenes/SurvivalSampleMap.unity", scene =>
            {
                AssertResourcePickupsHaveWorldNameLabels(scene);
            });
        }

        [Test]
        public void MainSceneContainsNoWorldRootBindingsAndNoResidentPlayer()
        {
            WithOpenedScene("Assets/Game/Scenes/Main.unity", scene =>
            {
                var players = FindInScene<PlayerView>(scene);
                Assert.That(players.Count, Is.EqualTo(0));
                var characterControllers = FindInScene<CharacterController>(scene);
                Assert.That(characterControllers.Count, Is.EqualTo(0));

                Assert.That(FindInSceneByTypeName(scene, "Game.WorldRootBindings").Count, Is.EqualTo(0));

                var activeCameras = FindInScene<Camera>(scene)
                    .FindAll(item => item.isActiveAndEnabled);
                Assert.That(activeCameras.Count, Is.EqualTo(1));
                Assert.That(FindInScene<AudioListener>(scene).Count, Is.EqualTo(1));

                var cameraBehaviours = activeCameras[0]
                    .GetComponents<MonoBehaviour>();
                Assert.That(
                    System.Array.Exists(
                        cameraBehaviours,
                        item => item != null &&
                                item.GetType().Name == "TopDownCameraController"),
                    Is.False);
            });
        }

        private static void AssertMapContentRootContract(MapContext context, Scene scene)
        {
            Assert.That(context.MapContentRoot, Is.Not.Null);
            Assert.That(context.DynamicObjectRoot, Is.Not.Null);
            Assert.That(context.MapContentRoot.name, Is.EqualTo("MapContentRoot"));
            Assert.That(context.DynamicObjectRoot.name, Is.EqualTo("DynamicObjectRoot"));
            Assert.That(context.MapContentRoot.scene, Is.EqualTo(scene));
            Assert.That(context.DynamicObjectRoot.gameObject.scene, Is.EqualTo(scene));
            Assert.IsFalse(context.MapContentRoot.activeSelf);
            Assert.IsTrue(context.DynamicObjectRoot.IsChildOf(context.MapContentRoot.transform));
            Assert.IsFalse(context.transform.IsChildOf(context.MapContentRoot.transform));
        }

        private static void WithOpenedScene(string path, System.Action<Scene> assertion)
        {
            var previousScene = SceneManager.GetActiveScene();
            var previousPath = previousScene.path;
            var openedScene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            try
            {
                assertion(openedScene);
            }
            finally
            {
                if (!string.IsNullOrEmpty(previousPath))
                {
                    EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
                }
                else
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
            }
        }

        private static List<T> FindInScene<T>(Scene scene) where T : Component
        {
            var results = new List<T>();
            foreach (var root in scene.GetRootGameObjects())
            {
                results.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return results;
        }

        private static List<Component> FindInSceneByTypeName(Scene scene, string typeName)
        {
            var results = new List<Component>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component != null && component.GetType().FullName == typeName)
                    {
                        results.Add(component);
                    }
                }
            }

            return results;
        }

        private static List<GameObject> FindNamed(Scene scene, string objectName)
        {
            var results = new List<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform.gameObject.name == objectName)
                    {
                        results.Add(transform.gameObject);
                    }
                }
            }

            return results;
        }

        private static void AssertInteractableLabel(Scene scene, string expectedText)
        {
            var labels = FindInScene<TextMeshPro>(scene)
                .FindAll(item => item.text == expectedText);

            Assert.That(labels.Count, Is.EqualTo(1), expectedText);
            Assert.That(labels[0].gameObject.activeSelf, Is.False, expectedText);
        }

        private static void AssertEnemiesUseNavMesh(Scene scene, int minimumCount)
        {
            var enemies = FindInScene<EnemyView>(scene);
            Assert.That(enemies.Count, Is.GreaterThanOrEqualTo(minimumCount));
            foreach (var enemy in enemies)
            {
                Assert.That(enemy.GetComponent<Collider>(), Is.Not.Null);
                Assert.That(enemy.GetComponent<NavMeshAgent>(), Is.Not.Null);
            }
        }

        private static void AssertEnemyPrefabKeepsPresentationContract()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxEnemy.prefab");
            Assert.That(prefab, Is.Not.Null);
            var enemy = prefab.GetComponent<EnemyView>();
            Assert.That(enemy, Is.Not.Null);
            Assert.That(enemy.GetComponent<Collider>(), Is.Not.Null);
            Assert.That(enemy.GetComponent<NavMeshAgent>(), Is.Not.Null);
            Assert.That(enemy.HealthBarRoot, Is.Not.Null);
            Assert.That(enemy.HealthBarFill, Is.Not.Null);
        }

        private static void AssertEnemiesHaveWorldHealthBars(Scene scene)
        {
            foreach (var enemy in FindInScene<EnemyView>(scene))
            {
                Assert.That(enemy.HealthBarRoot, Is.Not.Null, enemy.name);
                Assert.That(enemy.HealthBarFill, Is.Not.Null, enemy.name);
                var canvas = enemy.HealthBarRoot.GetComponentInChildren<Canvas>(true);
                Assert.That(canvas, Is.Not.Null, enemy.name);
                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.WorldSpace), enemy.name);
                Assert.That(enemy.HealthBarRoot.GetComponentsInChildren<TextMeshPro>(true), Is.Empty, enemy.name);
            }
        }

        private static void AssertResourcePickupsHaveWorldNameLabels(Scene scene)
        {
            var pickups = FindInScene<ResourcePickupView>(scene);
            Assert.That(pickups.Count, Is.GreaterThanOrEqualTo(1));
            foreach (var pickup in pickups)
            {
                Assert.That(pickup.NameRoot, Is.Not.Null, pickup.name);
                Assert.That(pickup.NameLabel, Is.Not.Null, pickup.name);
                Assert.That(pickup.NameLabel.text, Is.EqualTo("\u6d4b\u8bd5\u7269\u8d44"), pickup.name);
                var canvas = pickup.NameRoot.GetComponentInChildren<Canvas>(true);
                Assert.That(canvas, Is.Not.Null, pickup.name);
                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.WorldSpace), pickup.name);
                Assert.That(
                    pickup.NameRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true),
                    Is.Empty,
                    pickup.name);
            }
        }
    }
}
