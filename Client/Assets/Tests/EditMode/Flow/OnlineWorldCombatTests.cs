using System.Collections.Generic;
using System.Collections;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class OnlineWorldCombatTests
    {
        [Test]
        public void SnapshotAndCombatEvent_CopyCallerOwnedCollections()
        {
            var enemies = new[]
            {
                new OnlineWorldEnemySnapshot(
                    10, 1, "map", "enemy", Vector3.zero, 0f, Vector3.one, 45f,
                    20, 15, 20, false, 0, 3, 1)
            };
            var players = new[]
            {
                new OnlineWorldPlayerSnapshot(
                    1, 20, "player", 1, "map", "player", Vector3.zero, 0f,
                    OnlineWorldConnectionState.Online, 2, 90, 100, false, 0, 7, 1)
            };
            var targets = new List<long> { 10 };

            var snapshot = new OnlineWorldSnapshot(
                true, 1, "map", 20, 4, players, enemies, 20, 2, 1234,
                false, OnlineWorldOperationState.Idle, OnlineWorldResultCode.Success);
            var presentation = new OnlineWorldCombatPresentationEvent(
                8, OnlineWorldCombatActionKind.PlayerMelee, 20, 5, targets);

            players[0] = default;
            enemies[0] = default;
            targets[0] = 999;

            Assert.That(snapshot.Players[0].UnitId, Is.EqualTo(20));
            Assert.That(snapshot.Enemies[0].UnitId, Is.EqualTo(10));
            Assert.That(snapshot.NavigationExecutorUnitId, Is.EqualTo(20));
            Assert.That(snapshot.NavigationLeaseRevision, Is.EqualTo(2));
            Assert.That(presentation.TargetUnitIds, Is.EqualTo(new long[] { 10 }));
        }

        [Test]
        public void EnemyViewCtrl_OnlineDeathAndRevive_KeepCorpseUnsearchableAndRestoreLivingPresentation()
        {
            TestReset.ResetArchitecture();
            var root = new GameObject("OnlineEnemyViewTest", typeof(CapsuleCollider), typeof(NavMeshAgent));
            var living = GameObject.CreatePrimitive(PrimitiveType.Cube);
            living.name = "Living";
            living.transform.SetParent(root.transform, false);
            var corpse = new GameObject("Corpse");
            corpse.transform.SetParent(root.transform, false);
            var triggerObject = new GameObject("CorpseSearchTrigger", typeof(SphereCollider));
            triggerObject.transform.SetParent(root.transform, false);
            var prompt = new GameObject("CorpsePrompt");
            prompt.transform.SetParent(root.transform, false);
            var health = new GameObject("HealthBar");
            health.transform.SetParent(root.transform, false);

            EnemyViewCtrl controller = null;
            try
            {
                var view = root.AddComponent<EnemyView>();
                var serialized = new SerializedObject(view);
                serialized.FindProperty("navMeshAgent").objectReferenceValue = root.GetComponent<NavMeshAgent>();
                serialized.FindProperty("combatCollider").objectReferenceValue = root.GetComponent<CapsuleCollider>();
                serialized.FindProperty("corpseSearchTrigger").objectReferenceValue = triggerObject.GetComponent<SphereCollider>();
                serialized.FindProperty("corpsePromptRoot").objectReferenceValue = prompt;
                serialized.FindProperty("healthBarRoot").objectReferenceValue = health.transform;
                serialized.FindProperty("corpseLootBoxObject").objectReferenceValue = corpse;
                var renderers = serialized.FindProperty("livingRenderers");
                renderers.arraySize = 1;
                renderers.GetArrayElementAtIndex(0).objectReferenceValue = living.GetComponent<Renderer>();
                serialized.ApplyModifiedPropertiesWithoutUndo();
                controller = new EnemyViewCtrl(view, 10, 1, new ViewCtrlRegistry(), new DestroyingViewProvider());

                controller.SetHealthBarVisible(true);
                controller.MarkOnlineDead();

                Assert.That(view.CanSearch, Is.False);
                Assert.That(root.GetComponent<CapsuleCollider>().enabled, Is.False);
                Assert.That(triggerObject.GetComponent<SphereCollider>().enabled, Is.False);
                Assert.That(prompt.activeSelf, Is.False);
                Assert.That(living.GetComponent<Renderer>().enabled, Is.False);
                Assert.That(corpse.activeSelf, Is.True);

                controller.ReviveOnline();

                Assert.That(view.CanSearch, Is.False);
                Assert.That(root.GetComponent<CapsuleCollider>().enabled, Is.True);
                Assert.That(triggerObject.GetComponent<SphereCollider>().enabled, Is.False);
                Assert.That(living.GetComponent<Renderer>().enabled, Is.True);
                Assert.That(corpse.activeSelf, Is.False);
                Assert.That(health.activeSelf, Is.True);
            }
            finally
            {
                controller?.Dispose();
                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }
                TestReset.ResetArchitecture();
            }
        }

        [UnityTest]
        public IEnumerator OnlineEnemyPreparation_RejectsDuplicateUnitIdsBeforeLoadingAndCleansCandidate()
        {
            TestReset.ResetArchitecture();
            var root = new GameObject("OnlineEnemyDuplicateMap");
            var content = new GameObject("MapContent");
            content.transform.SetParent(root.transform, false);
            var dynamicRoot = new GameObject("DynamicObjectRoot");
            dynamicRoot.transform.SetParent(content.transform, false);
            var map = root.AddComponent<MapContext>();
            var serialized = new SerializedObject(map);
            serialized.FindProperty("mapContentRoot").objectReferenceValue = content;
            serialized.FindProperty("dynamicObjectRoot").objectReferenceValue = dynamicRoot.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var enemy = new OnlineWorldEnemySnapshot(
                10, 1, "map", "enemy", Vector3.zero, 0f, Vector3.zero, 0f,
                0, 20, 20, false, 0, 1, 1);
            var snapshot = new OnlineWorldSnapshot(
                true, 1, "map", 20, 1, Array.Empty<OnlineWorldPlayerSnapshot>(),
                new[] { enemy, enemy }, 20, 1, 0, false,
                OnlineWorldOperationState.Idle, OnlineWorldResultCode.Success);
            var controller = new OnlineWorldEnemyCtrl(Game.Interface);

            try
            {
                Task<MapControllerResult> preparing = controller.PrepareMapAsync(
                    new MapPreparationContext("map", 1, map, Array.Empty<MapSpawnDefinition>(), snapshot),
                    CancellationToken.None).AsTask();
                while (!preparing.IsCompleted) yield return null;
                if (preparing.IsFaulted) throw preparing.Exception;
                MapControllerResult prepared = preparing.Result;

                Assert.That(prepared.Success, Is.False);
                Assert.That(prepared.Error, Does.Contain("重复"));
                Assert.That(controller.CommitPreparedMap().Success, Is.False);
                Assert.DoesNotThrow(controller.RollbackPreparedMap);
                Assert.DoesNotThrow(controller.ReleaseCurrentMap);
            }
            finally
            {
                controller.Dispose();
                Object.DestroyImmediate(root);
                TestReset.ResetArchitecture();
            }
        }

        [UnityTest]
        public IEnumerator EmptyEnemyMap_PreparesWithoutEnemyControllersOrNavigationExecutor()
        {
            TestReset.ResetArchitecture();
            var root = new GameObject("OnlineEmptyEnemyMap");
            var content = new GameObject("MapContent");
            content.transform.SetParent(root.transform, false);
            var dynamicRoot = new GameObject("DynamicObjectRoot");
            dynamicRoot.transform.SetParent(content.transform, false);
            var map = root.AddComponent<MapContext>();
            var serialized = new SerializedObject(map);
            serialized.FindProperty("mapContentRoot").objectReferenceValue = content;
            serialized.FindProperty("dynamicObjectRoot").objectReferenceValue = dynamicRoot.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var snapshot = new OnlineWorldSnapshot(
                true, 1, MapIds.MissionSample, 20, 1, Array.Empty<OnlineWorldPlayerSnapshot>(),
                Array.Empty<OnlineWorldEnemySnapshot>(), 0, 0, 0, false,
                OnlineWorldOperationState.Idle, OnlineWorldResultCode.Success);
            var controller = new OnlineWorldEnemyCtrl(Game.Interface);
            try
            {
                Task<MapControllerResult> preparing = controller.PrepareMapAsync(
                    new MapPreparationContext(MapIds.MissionSample, 1, map, Array.Empty<MapSpawnDefinition>(), snapshot),
                    CancellationToken.None).AsTask();
                while (!preparing.IsCompleted) yield return null;
                if (preparing.IsFaulted) throw preparing.Exception;
                Assert.That(preparing.Result.Success, Is.True, preparing.Result.Error);
                Assert.That(controller.CommitPreparedMap().Success, Is.True);
                Assert.That(dynamicRoot.transform.childCount, Is.Zero, "empty snapshot must not create enemy views");
                Assert.That(snapshot.NavigationExecutorUnitId, Is.Zero);
            }
            finally
            {
                controller.Dispose();
                Object.DestroyImmediate(root);
                TestReset.ResetArchitecture();
            }
        }

        [Test]
        public void OnlineEnemyCommitFailure_CompensatesPartialActiveAndStagedEntries()
        {
            TestReset.ResetArchitecture();
            LifeCycle.ClearForTests();
            var loader = new CountingResourceLoader();
            var instances = new DestroyingViewProvider();
            ResourceLoader.SetLoaderForTests(loader);
            var root = new GameObject("OnlineEnemyCommitFailureRoot");
            var controller = new OnlineWorldEnemyCtrl(Game.Interface);
            GameObject first = null, second = null;
            try
            {
                SetField(controller, "candidateRoot", root.transform);
                SetField(controller, "candidateMapInstanceId", 1L);
                SetField(controller, "candidateWorldId", 1L);
                SetField(controller, "candidateMapId", "map");
                IList staged = (IList)GetField(controller, "staged");
                object firstRaw = new object(), secondRaw = new object();
                first = CreateCommitEnemy("CommitEnemy1", root.transform);
                second = CreateCommitEnemy("CommitEnemy2", root.transform);
                staged.Add(CreateEntry(10, first, firstRaw, instances));
                staged.Add(CreateEntry(11, second, secondRaw, instances));
                controller.BeforeCommitEntryForTests = unitId => { if (unitId == 11) throw new InvalidOperationException("injected commit failure"); };

                MapControllerResult result = controller.CommitPreparedMap();

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("injected commit failure"));
                Assert.That(((IDictionary)GetField(controller, "active")).Count, Is.Zero);
                Assert.That(((IList)GetField(controller, "staged")).Count, Is.Zero);
                Assert.That(first == null, Is.True, "partially active enemy instance leaked");
                Assert.That(second == null, Is.True, "staged enemy instance leaked");
                Assert.That(loader.ReleaseCount(firstRaw), Is.EqualTo(1));
                Assert.That(loader.ReleaseCount(secondRaw), Is.EqualTo(1));
                Assert.That(controller.CommitPreparedMap().Success, Is.False, "failed candidate remained committable");
                Assert.DoesNotThrow(controller.ReleaseCurrentMap);
            }
            finally
            {
                controller.Dispose();
                ResourceLoader.ResetForTests();
                if (root != null) Object.DestroyImmediate(root);
                LifeCycle.ClearForTests();
                TestReset.ResetArchitecture();
            }
        }

        [Test]
        public void EnemyMissingFromSnapshot_ReleasesActiveViewAndResource()
        {
            TestReset.ResetArchitecture();
            LifeCycle.ClearForTests();
            var loader = new CountingResourceLoader();
            var instances = new DestroyingViewProvider();
            ResourceLoader.SetLoaderForTests(loader);
            var root = new GameObject("OnlineEnemyRemovalRoot");
            var controller = new OnlineWorldEnemyCtrl(Game.Interface);
            GameObject enemy = null;
            object rawHandle = new object();
            try
            {
                enemy = CreateCommitEnemy("RemovedEnemy", root.transform);
                IDictionary active = (IDictionary)GetField(controller, "active");
                active.Add(10L, CreateEntry(10, enemy, rawHandle, instances));
                SetField(controller, "activeRoot", root.transform);
                SetField(controller, "activeWorldId", 1L);
                SetField(controller, "activeMapId", "map");
                SetField(controller, "committed", true);
                var snapshot = new OnlineWorldSnapshot(
                    true, 1, "map", 20, 2, Array.Empty<OnlineWorldPlayerSnapshot>(),
                    Array.Empty<OnlineWorldEnemySnapshot>(), 0, 0, 0, false,
                    OnlineWorldOperationState.Idle, OnlineWorldResultCode.Success);

                controller.ApplySnapshot(snapshot);

                Assert.That(active.Count, Is.Zero);
                Assert.That(enemy == null, Is.True, "enemy view missing from the authoritative snapshot was not released");
                Assert.That(loader.ReleaseCount(rawHandle), Is.EqualTo(1));
            }
            finally
            {
                controller.Dispose();
                ResourceLoader.ResetForTests();
                if (root != null) Object.DestroyImmediate(root);
                LifeCycle.ClearForTests();
                TestReset.ResetArchitecture();
            }
        }

        private static object CreateEntry(long unitId, GameObject instance, object rawHandle, IViewInstanceProvider instances)
        {
            Type type = typeof(OnlineWorldEnemyCtrl).GetNestedType("Entry", BindingFlags.NonPublic);
            object entry = Activator.CreateInstance(type);
            var state = new OnlineWorldEnemySnapshot(unitId, 1, "map", "enemy", Vector3.zero, 0f,
                Vector3.zero, 0f, 0, 20, 20, false, 0, 1, 1);
            SetField(entry, "State", state);
            SetField(entry, "Handle", new ResourceLoadHandle(rawHandle));
            SetField(entry, "View", new EnemyViewCtrl(instance.GetComponent<EnemyView>(), (int)unitId, 1, new ViewCtrlRegistry(), instances));
            return entry;
        }

        private static GameObject CreateCommitEnemy(string name, Transform parent)
        {
            var value = new GameObject(name, typeof(CapsuleCollider), typeof(NavMeshAgent));
            value.transform.SetParent(parent, false);
            EnemyView view = value.AddComponent<EnemyView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("navMeshAgent").objectReferenceValue = value.GetComponent<NavMeshAgent>();
            serialized.FindProperty("combatCollider").objectReferenceValue = value.GetComponent<CapsuleCollider>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return value;
        }

        private static object GetField(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);
        private static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(target, value);

        private sealed class DestroyingViewProvider : IViewInstanceProvider
        {
            public T Instantiate<T>(T prefab, Transform parent, Vector3 position, Quaternion rotation) where T : Component =>
                Object.Instantiate(prefab, position, rotation, parent);

            public void Release(GameObject instance)
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        private sealed class CountingResourceLoader : IResourceLoader
        {
            private readonly Dictionary<object, int> releases = new();
            public T LoadAsset<T>(string path) where T : Object => null;
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object { onLoaded?.Invoke(null); return default; }
            public void Release(ResourceLoadHandle handle) { if(handle.IsValid)releases[handle.RawHandle]=ReleaseCount(handle.RawHandle)+1; }
            public int ReleaseCount(object raw) => releases.TryGetValue(raw, out int count) ? count : 0;
        }
    }
}
