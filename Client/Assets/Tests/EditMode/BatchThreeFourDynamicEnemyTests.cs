using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class BatchThreeFourDynamicEnemyTests
    {
        private const string EnemyConfigId = "enemy.graybox.basic";
        private const string EnemyPrefabPath = "Assets/Game/Prefabs/Characters/GrayboxEnemy.prefab";
        private TestRuntimeConfigProvider config;
        private TestResourceLoader loader;
        private TestViewInstanceProvider instances;
        private GameObject prefab;
        private GameObject sceneObject;
        private GameObject mapContentRoot;
        private Transform runtimeRoot;
        private MapContext mapContext;
        private NavMeshDataInstance navMeshDataInstance;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            config = new TestRuntimeConfigProvider();
            loader = new TestResourceLoader();
            instances = new TestViewInstanceProvider();
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(config);
            Game.Interface.RegisterUtility<IViewInstanceProvider>(instances);
            ResourceLoader.SetLoaderForTests(loader);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            prefab = CreateEnemyPrefab("EnemyPrefab");
            loader.Result = prefab;
            CreateSceneContext();
            AddFlatNavMesh(new Bounds(Vector3.zero, new Vector3(40f, 4f, 40f)));
        }

        [TearDown]
        public void TearDown()
        {
            ResourceLoader.ResetForTests();
            if (navMeshDataInstance.valid)
            {
                navMeshDataInstance.Remove();
            }

            if (prefab != null)
            {
                UnityEngine.Object.DestroyImmediate(prefab);
            }

            if (sceneObject != null)
            {
                UnityEngine.Object.DestroyImmediate(sceneObject);
            }

            if (mapContentRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(mapContentRoot);
            }

            TestReset.ResetArchitecture();
        }

        [Test]
        public void EnemyPrefabAndConfigContainRequiredPresentationAndGameplayData()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
            Assert.NotNull(asset);
            Assert.NotNull(asset.GetComponent<EnemyView>());
            Assert.NotNull(asset.GetComponent<Collider>());
            Assert.NotNull(asset.GetComponent<NavMeshAgent>());
            Assert.IsTrue(Game.Interface.GetUtility<IRuntimeConfigProvider>()
                .TryGetEnemy(EnemyConfigId, out var enemy, out var error), error);
            Assert.That(enemy.PrefabAddress, Is.EqualTo("Prefabs/Characters/GrayboxEnemy"));
            Assert.That(enemy.MaxHP, Is.EqualTo(20));
            Assert.That(enemy.AttackDamage, Is.EqualTo(10));
            Assert.That(enemy.LootConfigId, Is.EqualTo("loot.test-resource"));
        }

        [Test]
        public void BaseMapWithNoEnemySpawnsPreparesAndCommitsSuccessfully()
        {
            var ctrl = new EnemyCtrl(Game.Interface);
            var result = ctrl.PrepareMapAsync(Context(Array.Empty<MapSpawnDefinition>()), default)
                .GetAwaiter()
                .GetResult();
            Assert.IsTrue(result.Success, result.Error);
            Assert.IsTrue(ctrl.CommitPreparedMap().Success);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            ctrl.ReleaseAll();
        }

        [Test]
        public void PrepareCreatesConfiguredEnemiesAtLocalPosesAndLoadsConfigOnce()
        {
            var ctrl = new EnemyCtrl(Game.Interface);
            var result = ctrl.PrepareMapAsync(Context(EnemySpawn(-4, 0.6f, 0), EnemySpawn(4, 0.6f, 2)), default)
                .GetAwaiter()
                .GetResult();
            Assert.IsTrue(result.Success, result.Error);
            Assert.That(loader.Loads, Is.EqualTo(1));
            Assert.That(config.EnemyReads, Is.EqualTo(2));
            Assert.That(instances.Instances.Count, Is.EqualTo(2));
            Assert.That(instances.Instances[0].transform.position, Is.EqualTo(runtimeRoot.TransformPoint(new Vector3(-4, 0.6f, 0))));
            Assert.IsFalse(instances.Instances[0].activeSelf);
            ctrl.ReleaseAll();
        }

        [Test]
        public void ArcadeEnemyPresentation_ReusesRealEnemyViewFromPoolWithoutRegisteringCombatDomains()
        {
            var ctrl = new EnemyCtrl(Game.Interface);
            var context = MapPreparationContext.CreateArcade(MapIds.Arcade, 7, mapContext,
                Array.Empty<MapSpawnDefinition>());
            var prepare = ctrl.PrepareMapAsync(context, default).GetAwaiter().GetResult();
            Assert.That(prepare.Success, Is.True, prepare.Error);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);

            Assert.That(ctrl.TrySetEnemy(101, EnemyConfigId, Vector3.zero, Vector3.forward, 20, 20, out var error),
                Is.True, error);
            Assert.That(instances.Instances, Has.Count.EqualTo(1));
            var first = instances.Instances[0];
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.Zero);

            ctrl.RemoveEnemy(101);
            Assert.That(first.activeSelf, Is.False);
            Assert.That(ctrl.TrySetEnemy(102, EnemyConfigId, Vector3.right, Vector3.left, 10, 20, out error),
                Is.True, error);
            Assert.That(instances.Instances, Has.Count.EqualTo(1));
            Assert.That(first.activeSelf, Is.True);

            ctrl.ClearArcadeEnemies();
            ctrl.ReleaseAll();
            Assert.That(instances.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public void PrepareRejectsMissingConfigOrBrokenPrefabAndRollsBackCandidate()
        {
            var ctrl = new EnemyCtrl(Game.Interface);
            config.MissingEnemy = true;
            var missing = ctrl.PrepareMapAsync(Context(EnemySpawn(0, 0, 0)), default)
                .GetAwaiter()
                .GetResult();
            Assert.IsFalse(missing.Success);

            config.MissingEnemy = false;
            loader.Result = new GameObject("Broken");
            var broken = ctrl.PrepareMapAsync(Context(EnemySpawn(0, 0, 0)), default)
                .GetAwaiter()
                .GetResult();
            Assert.IsFalse(broken.Success);
            Assert.That(instances.ReleaseCount, Is.EqualTo(0));
            UnityEngine.Object.DestroyImmediate(loader.Result);
            ctrl.ReleaseAll();
        }

        [Test]
        public void CancelledEnemyLoadReturnsBeforeCallbackAndBlocksConcurrentLoad()
        {
            loader.Defer = true;
            var ctrl = new EnemyCtrl(Game.Interface);
            using var cts = new CancellationTokenSource();
            var task = ctrl.PrepareMapAsync(Context(EnemySpawn(0, 0, 0)), cts.Token);
            cts.Cancel();
            var cancelled = task.GetAwaiter().GetResult();
            Assert.IsFalse(cancelled.Success);

            var retry = ctrl.PrepareMapAsync(Context(EnemySpawn(0, 0, 0)), default)
                .GetAwaiter()
                .GetResult();
            Assert.IsFalse(retry.Success);
            ctrl.ReleaseAll();
        }

        [Test]
        public void LateEnemyLoadCallbackReleasesHandleOnceAndAllowsRetry()
        {
            loader.Defer = true;
            var ctrl = new EnemyCtrl(Game.Interface);
            using var cts = new CancellationTokenSource();
            var task = ctrl.PrepareMapAsync(Context(EnemySpawn(0, 0, 0)), cts.Token);
            cts.Cancel();
            Assert.IsFalse(task.GetAwaiter().GetResult().Success);
            loader.Complete(prefab);
            Assert.That(loader.ReleaseCount, Is.EqualTo(1));
            loader.Defer = false;
            var retry = ctrl.PrepareMapAsync(Context(EnemySpawn(0, 0, 0)), default)
                .GetAwaiter()
                .GetResult();
            Assert.IsTrue(retry.Success, retry.Error);
            ctrl.ReleaseAll();
        }

        [Test]
        public void CommitRegistersEnemyUnitCombatAndAiFromConfig()
        {
            var ctrl = PrepareAndCommitOne(out var unitId);
            Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryGet(unitId, out var unit));
            Assert.That(unit.ConfigId, Is.EqualTo(EnemyConfigId));
            Assert.IsTrue(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(unitId, out var combat));
            Assert.That(combat.MaxHP, Is.EqualTo(20));
            Assert.IsTrue(Game.Interface.GetSystem<EnemySystem>().TryGetIntent(unitId, out _));
            ctrl.ReleaseAll();
        }

        [Test]
        public void CommitFailureRollsBackUnitCombatAiAndInstances()
        {
            var ctrl = new EnemyCtrl(Game.Interface);
            Assert.IsTrue(ctrl.PrepareMapAsync(Context(EnemySpawn(0, 0, 0)), default).GetAwaiter().GetResult().Success);
            Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                1000, 7, UnitType.Enemy, UnitSource.MapPlaced, EnemyConfigId, Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out _));
            Assert.IsFalse(ctrl.CommitPreparedMap().Success);
            Assert.IsFalse(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(1000, out _));
            Assert.That(instances.ReleaseCount, Is.EqualTo(1));
            ctrl.ReleaseAll();
        }

        [Test]
        public void DeactivateRestoreReleaseAndReleaseAllAreIdempotent()
        {
            var ctrl = PrepareAndCommitOne(out var unitId);
            RegisterPlayerCombatOnly();
            ctrl.SetCurrentMapActive(false);
            Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryGet(unitId, out var inactive));
            Assert.IsFalse(inactive.IsActive);
            ctrl.SetCurrentMapActive(true);
            Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryGet(unitId, out var activeUnit));
            Assert.IsTrue(activeUnit.IsActive);

            Assert.IsTrue(Game.Interface.GetSystem<CombatSystem>().TryDamage(PlayerModel.LocalPlayerUnitId, unitId));
            Assert.IsTrue(Game.Interface.GetSystem<CombatSystem>().TryDamage(PlayerModel.LocalPlayerUnitId, unitId));
            ctrl.SetCurrentMapActive(false);
            Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryGet(unitId, out var deadInactive));
            Assert.IsFalse(deadInactive.IsActive);
            ctrl.SetCurrentMapActive(true);
            Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryGet(unitId, out var stillInactive));
            Assert.IsFalse(stillInactive.IsActive);
            Assert.IsFalse(instances.Instances[0].activeSelf);

            ctrl.ReleaseCurrentMap();
            ctrl.ReleaseCurrentMap();
            ctrl.ReleaseAll();
            ctrl.ReleaseAll();
            Assert.That(instances.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public void CombatRejectsMissingInactiveSameFactionCrossMapAndDeadParticipants()
        {
            RegisterPlayerAndEnemy(out var enemyId);
            var combat = Game.Interface.GetSystem<CombatSystem>();
            var applied = 0;
            var deaths = 0;
            var appliedUnregister = Game.Interface.RegisterEvent<CombatDamageAppliedEvent>(_ => applied++);
            var deathUnregister = Game.Interface.RegisterEvent<CombatantDiedEvent>(_ => deaths++);

            AssertRejectedWithoutEvents(() => combat.TryDamage(999, enemyId), enemyId, 20, () => applied, () => deaths);
            AssertRejectedWithoutEvents(() => combat.TryDamage(enemyId, enemyId), enemyId, 20, () => applied, () => deaths);
            AssertRejectedWithoutEvents(() => combat.TryDamage(PlayerModel.LocalPlayerUnitId, 2000), enemyId, 20, () => applied, () => deaths);
            Game.Interface.GetSystem<UnitSystem>().TrySetActive(enemyId, false);
            AssertRejectedWithoutEvents(() => combat.TryDamage(PlayerModel.LocalPlayerUnitId, enemyId), enemyId, 20, () => applied, () => deaths);
            Game.Interface.GetSystem<UnitSystem>().TrySetActive(enemyId, true);

            var sameFactionEnemyId = 1001;
            RegisterEnemyOnly(sameFactionEnemyId, 7, new Vector3(1, 0, 0));
            AssertRejectedWithoutEvents(() => combat.TryDamage(enemyId, sameFactionEnemyId), sameFactionEnemyId, 20, () => applied, () => deaths);

            var crossMapEnemyId = 1002;
            RegisterEnemyOnly(crossMapEnemyId, 8, new Vector3(2, 0, 0));
            AssertRejectedWithoutEvents(() => combat.TryDamage(PlayerModel.LocalPlayerUnitId, crossMapEnemyId), crossMapEnemyId, 20, () => applied, () => deaths);

            Assert.IsTrue(combat.TryDamage(PlayerModel.LocalPlayerUnitId, enemyId));
            Assert.IsTrue(combat.TryDamage(PlayerModel.LocalPlayerUnitId, enemyId));
            var appliedAfterKill = applied;
            var deathsAfterKill = deaths;
            Assert.IsFalse(combat.TryDamage(PlayerModel.LocalPlayerUnitId, enemyId));
            Assert.That(applied, Is.EqualTo(appliedAfterKill));
            Assert.That(deaths, Is.EqualTo(deathsAfterKill));
            appliedUnregister.UnRegister();
            deathUnregister.UnRegister();
        }

        [Test]
        public void CombatUsesAttackerSnapshotDamageAndPublishesDeathOnce()
        {
            RegisterPlayerAndEnemy(out var enemyId);
            var deaths = 0;
            var unregister = Game.Interface.RegisterEvent<CombatantDiedEvent>(_ => deaths++);
            var combat = Game.Interface.GetSystem<CombatSystem>();
            Assert.IsTrue(combat.TryDamage(PlayerModel.LocalPlayerUnitId, enemyId));
            Assert.IsTrue(combat.TryGetCombatant(enemyId, out var after));
            Assert.That(after.HP, Is.EqualTo(10));
            Assert.IsTrue(combat.TryDamage(PlayerModel.LocalPlayerUnitId, enemyId));
            Assert.IsFalse(combat.TryDamage(PlayerModel.LocalPlayerUnitId, enemyId));
            Assert.That(deaths, Is.EqualTo(1));
            unregister.UnRegister();
        }

        [Test]
        public void EnemyIntentUsesConfiguredRangesAndConfiguredAttackCooldown()
        {
            RegisterPlayerAndEnemy(out var enemyId, enemyPosition: new Vector3(0, 0, 1));
            var port = new TestEnemyPort(enemyId, 7, new Vector3(0, 0, 1));
            Assert.IsTrue(Game.Interface.GetSystem<EnemySystem>().TryRegisterEnemy(port, config.Enemy, out var error), error);
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Game.Interface.GetModel<ExpeditionModel>().BeginSurvival(MapIds.SurvivalSample, 60f);
            LifeCycle.Update();
            Assert.IsTrue(Game.Interface.GetSystem<EnemySystem>().TryGetIntent(enemyId, out var intent));
            Assert.That(intent, Is.EqualTo(EnemyIntent.Attack));
            Assert.That(port.StopCalls, Is.GreaterThan(0));
            Assert.IsTrue(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(PlayerModel.LocalPlayerUnitId, out var player));
            Assert.That(player.HP, Is.EqualTo(90));
            LifeCycle.Update();
            Assert.IsTrue(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(PlayerModel.LocalPlayerUnitId, out player));
            Assert.That(player.HP, Is.EqualTo(90));
        }

        [Test]
        public void EnemySystemStopsWhenGameOrExpeditionCannotRun()
        {
            RegisterPlayerAndEnemy(out var enemyId, enemyPosition: new Vector3(0, 0, 4));
            var port = new TestEnemyPort(enemyId, 7, new Vector3(0, 0, 4));
            Assert.IsTrue(Game.Interface.GetSystem<EnemySystem>().TryRegisterEnemy(port, config.Enemy, out _));
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Paused;
            LifeCycle.Update();
            Assert.That(port.StopCalls, Is.EqualTo(1));
        }

        [Test]
        public void EnemySystemDoesNotStopPortThatBecameInvalid()
        {
            RegisterPlayerAndEnemy(out var enemyId, enemyPosition: new Vector3(0, 0, 4));
            var port = new TestEnemyPort(enemyId, 7, new Vector3(0, 0, 4));
            Assert.IsTrue(Game.Interface.GetSystem<EnemySystem>().TryRegisterEnemy(port, config.Enemy, out var error), error);
            port.IsValid = false;

            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Paused;
            LifeCycle.Update();

            Assert.That(port.StopCalls, Is.EqualTo(0));
        }

        [Test]
        public void DestroyedEnemyViewCtrlRejectsAllPresentationCallsAndDisposesIdempotently()
        {
            var enemyObject = CreateEnemyPrefab("DestroyedEnemyView");
            var view = enemyObject.GetComponent<EnemyView>();
            var provider = new TestViewInstanceProvider();
            var ctrl = new EnemyViewCtrl(
                view,
                1200,
                7,
                Game.Interface.GetUtility<ViewCtrlRegistry>(),
                provider);

            UnityEngine.Object.DestroyImmediate(enemyObject);

            Assert.IsFalse(ctrl.IsValid);
            Assert.That(ctrl.Position, Is.EqualTo(Vector3.zero));
            Assert.That(ctrl.Rotation, Is.EqualTo(Quaternion.identity));
            Assert.DoesNotThrow(() =>
            {
                ctrl.SetVisible(false);
                ctrl.ConfigureNavigation(2f, 1f);
                ctrl.SetHealthDisplay(5, 20);
                ctrl.PlayHitFeedback();
                ctrl.MarkDead();
                ctrl.SetAlerted(true);
                ctrl.FaceWorldHuds(Vector3.forward, Vector3.up);
                ctrl.SetDestination(Vector3.one);
                ctrl.Stop();
                ctrl.TickCombatFeedback(0.1f);
                ctrl.Dispose();
                ctrl.Dispose();
            });
            Assert.That(Game.Interface.GetUtility<ViewCtrlRegistry>().Count, Is.EqualTo(0));
            Assert.That(provider.ReleaseAttempts, Is.EqualTo(0));
        }

        [Test]
        public void DestroyedLootAndExtractionViewCtrlsRejectPresentationCallsAndDispose()
        {
            var lootObject = new GameObject("DestroyedLoot", typeof(BoxCollider), typeof(ResourcePickupView));
            var extractionObject = new GameObject("DestroyedExtraction", typeof(BoxCollider), typeof(TaskExitView));
            var lootProvider = new TestViewInstanceProvider();
            var extractionProvider = new TestViewInstanceProvider();
            var lootCtrl = new LootViewCtrl(
                lootObject.GetComponent<ResourcePickupView>(),
                1201,
                7,
                Game.Interface.GetUtility<ViewCtrlRegistry>(),
                lootProvider,
                true,
                null);
            var extractionCtrl = new ExtractionViewCtrl(
                extractionObject.GetComponent<TaskExitView>(),
                1202,
                7,
                Game.Interface.GetUtility<ViewCtrlRegistry>(),
                extractionProvider,
                null);

            UnityEngine.Object.DestroyImmediate(lootObject);
            UnityEngine.Object.DestroyImmediate(extractionObject);

            Assert.DoesNotThrow(() =>
            {
                lootCtrl.SetVisible(false);
                lootCtrl.SetNameVisible(true);
                lootCtrl.RefreshDisplay("test", 2);
                lootCtrl.FaceName(Vector3.forward, Vector3.up);
                lootCtrl.SetCollected();
                extractionCtrl.SetVisible(false);
                extractionCtrl.SetAvailable(true);
                lootCtrl.Dispose();
                extractionCtrl.Dispose();
                lootCtrl.Dispose();
                extractionCtrl.Dispose();
            });
            Assert.That(lootCtrl.Position, Is.EqualTo(Vector3.zero));
            Assert.That(lootCtrl.Rotation, Is.EqualTo(Quaternion.identity));
            Assert.IsFalse(lootCtrl.IsVisibleTargetActive);
            Assert.That(extractionCtrl.Position, Is.EqualTo(Vector3.zero));
            Assert.That(extractionCtrl.Rotation, Is.EqualTo(Quaternion.identity));
            Assert.That(Game.Interface.GetUtility<ViewCtrlRegistry>().Count, Is.EqualTo(0));
            Assert.That(lootProvider.ReleaseAttempts, Is.EqualTo(0));
            Assert.That(extractionProvider.ReleaseAttempts, Is.EqualTo(0));
        }

        [Test]
        public void ExternallyDestroyedEnemySurvivesLifecycleWindowAndReleasesAllDomains()
        {
            var ctrl = PrepareAndCommitOne(out var unitId);
            var instance = instances.Instances[0];
            var collider = instance.GetComponent<Collider>();
            Assert.IsTrue(ctrl.TryResolveEnemyUnitId(collider, out var resolvedUnitId));
            Assert.That(resolvedUnitId, Is.EqualTo(unitId));

            ctrl.SetCurrentMapActive(false);
            UnityEngine.Object.DestroyImmediate(instance);
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Paused;
            LifeCycle.Update();
            LogAssert.NoUnexpectedReceived();

            Assert.DoesNotThrow(() =>
            {
                ctrl.ReleaseCurrentMap();
                ctrl.ReleaseCurrentMap();
                ctrl.ReleaseAll();
                ctrl.ReleaseAll();
            });
            Assert.IsFalse(Game.Interface.GetSystem<UnitSystem>().TryGet(unitId, out _));
            Assert.IsFalse(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(unitId, out _));
            Assert.IsFalse(Game.Interface.GetSystem<EnemySystem>().TryGetIntent(unitId, out _));
            Assert.That(Game.Interface.GetUtility<ViewCtrlRegistry>().Count, Is.EqualTo(0));
            Assert.That(GetEnemyColliderMapCount(ctrl), Is.EqualTo(0));
        }

        [Test]
        public void EnemyDeathRemainsQueryableUntilNextUpdateThenReleasesCombatAndKeepsCorpseUnit()
        {
            var ctrl = PrepareAndCommitOne(out var unitId);
            RegisterPlayerCombatOnly();
            Assert.IsTrue(Game.Interface.GetSystem<CombatSystem>().TryDamage(PlayerModel.LocalPlayerUnitId, unitId));
            Assert.IsTrue(Game.Interface.GetSystem<CombatSystem>().TryDamage(PlayerModel.LocalPlayerUnitId, unitId));
            Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryGet(unitId, out _));
            Assert.IsTrue(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(unitId, out _));
            LifeCycle.Update();
            Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryGet(unitId, out _));
            Assert.IsFalse(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(unitId, out _));
            Assert.That(instances.ReleaseCount, Is.EqualTo(0));
            ctrl.ReleaseAll();
            Assert.That(instances.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public void EnemyDeathDropUsesCurrentUnitPositionAndConfiguredLootAmountOnce()
        {
            RegisterPlayerAndEnemy(out var enemyId, enemyPosition: new Vector3(3, 0, 5));
            var requests = 0;
            var deaths = 0;
            Vector3 dropPosition = default;
            IReadOnlyList<LootDropBatchEntry> entries = null;
            var unregister = Game.Interface.RegisterEvent<EnemyCorpseContentsRequestedEvent>(evt =>
            {
                requests++;
                dropPosition = evt.Position;
                entries = evt.Entries;
            });
            var deathUnregister = Game.Interface.RegisterEvent<CombatantDiedEvent>(_ => deaths++);
            Game.Interface.GetSystem<EnemyDropSystem>();
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Game.Interface.GetModel<ExpeditionModel>().BeginSurvival(MapIds.SurvivalSample, 60f);
            Assert.That(Game.Interface.GetSystem<InventorySystem>()
                .TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out var riskError), Is.True, riskError);
            Assert.IsTrue(Game.Interface.GetSystem<InventorySystem>().CanUseExpeditionBagItems());
            Assert.IsTrue(Game.Interface.GetSystem<CombatSystem>().TryDamage(PlayerModel.LocalPlayerUnitId, enemyId));
            Assert.IsTrue(Game.Interface.GetSystem<CombatSystem>().TryDamage(PlayerModel.LocalPlayerUnitId, enemyId));
            Assert.That(deaths, Is.EqualTo(1));
            Game.Interface.SendEvent(new CombatantDiedEvent(enemyId, PlayerModel.LocalPlayerUnitId));
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(dropPosition, Is.EqualTo(new Vector3(3, 0, 5)));
            Assert.NotNull(entries);
            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(entries[0].LootConfigId, Is.EqualTo("loot.weapon.guaranteed"));
            Assert.That(entries[1].LootConfigId, Is.EqualTo("loot.test-resource"));
            deathUnregister.UnRegister();
            unregister.UnRegister();
        }

        [Test]
        public void EnemyDamageAlertHealthBarAndDeathFeedbackRemainConnected()
        {
            var ctrl = PrepareAndCommitOne(out var unitId);
            RegisterPlayerCombatOnly();
            Assert.IsTrue(Game.Interface.GetSystem<CombatSystem>().TryDamage(PlayerModel.LocalPlayerUnitId, unitId));
            Assert.IsTrue(instances.Instances[0].activeSelf);
            Assert.IsTrue(Game.Interface.GetSystem<CombatSystem>().TryDamage(PlayerModel.LocalPlayerUnitId, unitId));
            Assert.IsTrue(instances.Instances[0].activeSelf);
            ctrl.ReleaseAll();
        }

        [Test]
        public void EnemyPrepareFailureKeepsCurrentEnemiesAndCleansCandidate()
        {
            var ctrl = PrepareAndCommitOne(out var currentId);
            Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryGet(currentId, out _));
            var activeInstance = instances.Instances[0];
            Game.Interface.RegisterUtility<ViewCtrlRegistry>(null);

            var failed = ctrl.PrepareMapAsync(Context(EnemySpawn(2, 0, 0, "enemy.graybox.alt")), default)
                .GetAwaiter()
                .GetResult();

            Assert.IsFalse(failed.Success);
            Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryGet(currentId, out _));
            Assert.IsTrue(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(currentId, out _));
            Assert.IsTrue(Game.Interface.GetSystem<EnemySystem>().TryGetIntent(currentId, out _));
            Assert.IsTrue(activeInstance.activeSelf);
            Assert.That(instances.ReleaseCount, Is.EqualTo(1));
            Assert.That(loader.ReleaseCount, Is.EqualTo(1));
            ctrl.ReleaseAll();
        }

        [Test]
        public void ScenesContainNoPreplacedEnemiesAndMapContextHasNoEnemyArray()
        {
            AssertSceneHasNoEnemies("Assets/Game/Scenes/BaseMap.unity");
            AssertSceneHasNoEnemies("Assets/Game/Scenes/SurvivalSampleMap.unity");
            AssertSceneHasNoEnemies("Assets/Game/Scenes/TaskSampleMap.unity");
            Assert.That(typeof(MapContext).GetProperty("EnemySpawns"), Is.Null);
            Assert.That(File.ReadAllText("Assets/Game/Scripts/Controller/View/Map/MapContext.cs"), Does.Not.Contain("enemySpawns"));
        }

        [Test]
        public void EnemyViewAndSystemsContainNoGameplayAuthorityOrConcreteViewDependency()
        {
            var viewFields = string.Join(",", Array.ConvertAll(
                typeof(EnemyView).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
                field => field.Name));
            Assert.That(viewFields, Does.Not.Contain("maxHealth"));
            Assert.That(viewFields, Does.Not.Contain("touchDamage"));
            Assert.That(File.ReadAllText("Assets/Game/Scripts/System/Combat/CombatSystem.cs"), Does.Not.Contain("EnemyView"));
            Assert.That(File.ReadAllText("Assets/Game/Scripts/System/Combat/EnemySystem.cs"), Does.Not.Contain("EnemyView"));
        }

        private EnemyCtrl PrepareAndCommitOne(out int unitId)
        {
            var ctrl = new EnemyCtrl(Game.Interface);
            var prepare = ctrl.PrepareMapAsync(Context(EnemySpawn(0, 0, 1)), default)
                .GetAwaiter()
                .GetResult();
            Assert.IsTrue(prepare.Success, prepare.Error);
            var commit = ctrl.CommitPreparedMap();
            Assert.IsTrue(commit.Success, commit.Error);
            unitId = 1000;
            return ctrl;
        }

        private void RegisterPlayerAndEnemy(out int enemyId, Vector3? enemyPosition = null)
        {
            RegisterPlayerCombatOnly();
            enemyId = 1000;
            RegisterEnemyOnly(enemyId, 7, enemyPosition ?? Vector3.zero);
        }

        private void RegisterEnemyOnly(int enemyId, long mapInstanceId, Vector3 enemyPosition)
        {
            var enemyUnit = new UnitSnapshot(
                enemyId,
                mapInstanceId,
                UnitType.Enemy,
                UnitSource.MapPlaced,
                EnemyConfigId,
                enemyPosition,
                Quaternion.identity,
                UnitAuthorityMode.Local,
                0,
                true);
            Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryRegister(enemyUnit, out var error), error);
            Assert.IsTrue(Game.Interface.GetSystem<CombatSystem>().TryRegisterEnemy(enemyId, config.Enemy, out error), error);
        }

        private void RegisterPlayerCombatOnly()
        {
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition(
                    "player.graybox.default",
                    "player",
                    100,
                    10,
                    2f,
                    0.1f,
                    4f,
                    360f,
                    5f),
                false);
            var playerUnit = new UnitSnapshot(
                PlayerModel.LocalPlayerUnitId,
                7,
                UnitType.Player,
                UnitSource.Player,
                "player.graybox.default",
                Vector3.zero,
                Quaternion.identity,
                UnitAuthorityMode.Local,
                0,
                true);
            Game.Interface.GetSystem<UnitSystem>().TryRegister(playerUnit, out _);
            Game.Interface.GetModel<CombatModel>().RegisterOrResetCombatant(
                PlayerModel.LocalPlayerUnitId,
                CombatFaction.Player,
                100,
                10,
                0);
        }

        private void AssertRejectedWithoutEvents(
            Func<bool> action,
            int observedUnitId,
            int expectedHealth,
            Func<int> appliedCount,
            Func<int> deathCount)
        {
            var appliedBefore = appliedCount();
            var deathsBefore = deathCount();
            Assert.IsFalse(action());
            Assert.That(appliedCount(), Is.EqualTo(appliedBefore));
            Assert.That(deathCount(), Is.EqualTo(deathsBefore));
            Assert.IsTrue(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(observedUnitId, out var combatant));
            Assert.That(combatant.HP, Is.EqualTo(expectedHealth));
        }

        private MapPreparationContext Context(params MapSpawnDefinition[] spawns)
        {
            return new MapPreparationContext(MapIds.SurvivalSample, 7, mapContext, spawns);
        }

        private static MapSpawnDefinition EnemySpawn(float x, float y, float z, string configId = EnemyConfigId)
        {
            return new MapSpawnDefinition("enemy.spawn", MapIds.SurvivalSample, MapSpawnObjectType.Enemy, configId, x, y, z, 0);
        }

        private void CreateSceneContext()
        {
            sceneObject = new GameObject("MapContext", typeof(MapContext));
            mapContentRoot = new GameObject("MapContentRoot");
            runtimeRoot = new GameObject("DynamicObjectRoot").transform;
            runtimeRoot.SetParent(mapContentRoot.transform, false);
            mapContext = sceneObject.GetComponent<MapContext>();
            SetField(mapContext, "mapContentRoot", mapContentRoot);
            SetField(mapContext, "dynamicObjectRoot", runtimeRoot);
        }

        private static GameObject CreateEnemyPrefab(string name)
        {
            var prefab = new GameObject(name, typeof(BoxCollider), typeof(NavMeshAgent), typeof(EnemyView));
            var emitter = prefab.AddComponent<DamageNumberEmitter>();
            SetField(emitter, "numberPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/WorldHud/DamageNumberBox.prefab").GetComponent<DamageNumberView>());
            SetField(prefab.GetComponent<EnemyView>(), "damageNumberEmitter", emitter);
            return prefab;
        }

        private void AddFlatNavMesh(Bounds bounds)
        {
            if (navMeshDataInstance.valid)
            {
                navMeshDataInstance.Remove();
            }

            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one),
                    size = new Vector3(bounds.size.x, 0.1f, bounds.size.z),
                    area = 0
                }
            };
            var settings = NavMesh.GetSettingsByID(0);
            var data = NavMeshBuilder.BuildNavMeshData(
                settings,
                sources,
                bounds,
                Vector3.zero,
                Quaternion.identity);
            navMeshDataInstance = NavMesh.AddNavMeshData(data);
        }

        private static void AssertSceneHasNoEnemies(string path)
        {
            var previous = EditorSceneManager.GetActiveScene().path;
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            try
            {
                Assert.That(UnityEngine.Object.FindObjectsOfType<EnemyView>(true).Length, Is.EqualTo(0), path);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(previous))
                {
                    EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                }
            }
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType()
                .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        private static int GetEnemyColliderMapCount(EnemyCtrl ctrl)
        {
            var field = typeof(EnemyCtrl).GetField(
                "colliderUnitIds",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var value = field?.GetValue(ctrl) as System.Collections.IDictionary;
            return value?.Count ?? -1;
        }

        private sealed class TestViewInstanceProvider : IViewInstanceProvider
        {
            public readonly List<GameObject> Instances = new List<GameObject>();
            public int ReleaseAttempts { get; private set; }
            public int ReleaseCount { get; private set; }
            public T Instantiate<T>(T prefab, Transform parent, Vector3 position, Quaternion rotation) where T : Component
            {
                var instance = UnityEngine.Object.Instantiate(prefab, position, rotation, parent);
                Instances.Add(instance.gameObject);
                return instance;
            }

            public void Release(GameObject instance)
            {
                ReleaseAttempts++;
                if (instance == null)
                {
                    return;
                }

                ReleaseCount++;
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private sealed class TestResourceLoader : IResourceLoader
        {
            private Action<GameObject> pending;
            private ResourceLoadHandle pendingHandle;

            public GameObject Result { get; set; }
            public bool Defer { get; set; }
            public int Loads { get; private set; }
            public int ReleaseCount { get; private set; }

            public T LoadAsset<T>(string path) where T : UnityEngine.Object
            {
                return Result as T;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            {
                Loads++;
                var handle = new ResourceLoadHandle(new object());
                if (Defer)
                {
                    pendingHandle = handle;
                    pending = asset => onLoaded(asset as T);
                }
                else
                {
                    onLoaded(Result as T);
                }

                return handle;
            }

            public void Complete(GameObject asset)
            {
                var callback = pending;
                pending = null;
                callback?.Invoke(asset);
            }

            public void Release(ResourceLoadHandle handle)
            {
                if (handle.IsValid || handle.RawHandle == pendingHandle.RawHandle)
                {
                    ReleaseCount++;
                }
            }
        }

        private sealed class TestRuntimeConfigProvider : IRuntimeConfigProvider
        {
            public readonly EnemyConfigDefinition Enemy = new EnemyConfigDefinition(
                EnemyConfigId,
                "Prefabs/Characters/GrayboxEnemy",
                20,
                10,
                8f,
                1.35f,
                1f,
                2.2f,
                "loot.test-resource",
                global::EnemyAttackKind.Melee,
                0f,
                0f,
                string.Empty,
                "loot.weapon.guaranteed",
                string.Empty,
                0,
                0f);

            public bool MissingEnemy { get; set; }
            public int EnemyReads { get; private set; }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = default;
                error = null;
                return false;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = Array.Empty<MapSpawnDefinition>();
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = new PlayerConfigDefinition(id, "player", 100, 10, 2f, 0.1f, 4f, 360f, 5f);
                error = null;
                return true;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                EnemyReads++;
                value = id == "enemy.graybox.alt"
                    ? new EnemyConfigDefinition(
                        "enemy.graybox.alt",
                        Enemy.PrefabAddress,
                        Enemy.MaxHP,
                        Enemy.AttackDamage,
                        Enemy.AggroRange,
                        Enemy.AttackRange,
                        Enemy.AttackCooldownSeconds,
                        Enemy.MoveSpeed,
                        Enemy.LootConfigId,
                        Enemy.AttackKind,
                        Enemy.RetreatDistance,
                        Enemy.TelegraphSeconds,
                        Enemy.ProjectileConfigId,
                        Enemy.GuaranteedWeaponLootConfigId,
                        Enemy.GuaranteedAmmoLootConfigId,
                        Enemy.AmmoCapacity,
                        Enemy.FallbackMeleeAttackRange)
                    : Enemy;
                error = null;
                if (MissingEnemy || (id != EnemyConfigId && id != "enemy.graybox.alt"))
                {
                    value = default;
                    error = "missing enemy";
                    return false;
                }

                return true;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                value = new LootConfigDefinition(id, "loot", ItemIds.MetalParts, 1);
                error = null;
                return id == "loot.test-resource" || id == "loot.weapon.guaranteed";
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                value = default;
                error = null;
                return false;
            }

            public bool TryGetArcadeCatalog(out ArcadeConfigCatalogDefinition value, out string error)
            {
                value = new ArcadeConfigCatalogDefinition(
                    default,
                    Array.Empty<ArcadeWeaponDefinition>(),
                    Array.Empty<ArcadeArenaDefinition>(),
                    new[]
                    {
                        new ArcadeEnemyGroupDefinition("arcade.test", new[]
                        {
                            new ArcadeEnemyMemberDefinition(EnemyConfigId, 1)
                        }, 1f, 1, 1f, 1f, 1f, 0)
                    },
                    Array.Empty<ArcadeBossDefinition>(),
                    Array.Empty<ArcadeChallengeDefinition>(),
                    Array.Empty<ArcadeDropDefinition>(),
                    Array.Empty<ArcadePowerupDefinition>(),
                    Array.Empty<ArcadeShopItemDefinition>(),
                    Array.Empty<ArcadeUpgradeDefinition>(),
                    Array.Empty<ArcadeRewardDefinition>());
                error = null;
                return true;
            }
        }

        private sealed class TestEnemyPort : IEnemyNavigationPort
        {
            public TestEnemyPort(int unitId, long mapInstanceId, Vector3 position)
            {
                UnitId = unitId;
                MapInstanceId = mapInstanceId;
                Position = position;
            }

            public int UnitId { get; }
            public long MapInstanceId { get; }
            public bool IsValid { get; set; } = true;
            public Vector3 Position { get; private set; }
            public Quaternion Rotation { get; } = Quaternion.identity;
            public int StopCalls { get; private set; }
            public int TelegraphVisibleCalls { get; private set; }
            public Vector3 LastRetreatRequest { get; private set; }

            public bool IsOnNavigableSurface(float sampleRadius)
            {
                return true;
            }

            public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin)
            {
                origin = Position + Vector3.up;
                return true;
            }

            public bool TrySetRetreatDestination(Vector3 requestedPosition, float sampleRadius, out Vector3 destination)
            {
                LastRetreatRequest = requestedPosition;
                destination = requestedPosition;
                return true;
            }

            public void PlayAttackAnimation() { }

            public void SetTelegraphVisible(bool visible)
            {
                TelegraphVisibleCalls++;
            }

            public void SetDestination(Vector3 position)
            {
                Position = position;
            }

            public void Stop()
            {
                StopCalls++;
            }
        }
    }
}
