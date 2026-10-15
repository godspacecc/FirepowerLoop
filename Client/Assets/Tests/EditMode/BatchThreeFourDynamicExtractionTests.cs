using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Luban;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class BatchThreeFourDynamicExtractionTests
    {
        private TestResourceLoader loader;
        private TestViewInstanceProvider instances;
        private TestConfigProvider configs;
        private TestPlayerRuntimeProvider playerPorts;
        private TestMapContentLoader mapLoader;
        private int returnStartedCount;
        private GameObject testCamera;
        private string tempSaveRoot;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            ResourceLoader.ResetForTests();
            tempSaveRoot = Path.Combine(Path.GetTempPath(), "AnotherDayExtraction", Guid.NewGuid().ToString("N"));
            loader = new TestResourceLoader();
            instances = new TestViewInstanceProvider();
            configs = new TestConfigProvider();
            playerPorts = new TestPlayerRuntimeProvider();
            mapLoader = new TestMapContentLoader();
            returnStartedCount = 0;
            testCamera = new GameObject("Extraction Test Camera", typeof(Camera), typeof(AudioListener));
            testCamera.tag = "MainCamera";
            ResourceLoader.SetLoaderForTests(loader);
            Game.Interface.RegisterUtility<IViewInstanceProvider>(instances);
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(configs);
            Game.Interface.RegisterUtility<IPlayerRuntimePortProvider>(playerPorts);
            Game.Interface.RegisterUtility<IMapContentLoader>(mapLoader);
            var save = Game.Interface.GetSystem<SaveSystem>();
            save.ConfigureForTests(tempSaveRoot);
            Assert.That(save.CreateBase(out var baseData, out var saveError), Is.True, saveError);
            Assert.That(save.CommitBaseRuntime(baseData, out saveError), Is.True, saveError);
            Game.Interface.RegisterEvent<ExtractionReturnStartedEvent>(_ => returnStartedCount++);
        }

        [TearDown]
        public void TearDown()
        {
            if (testCamera != null)
            {
                Object.DestroyImmediate(testCamera);
            }

            ResourceLoader.ResetForTests();
            TestReset.ResetArchitecture();
            if (!string.IsNullOrEmpty(tempSaveRoot) && Directory.Exists(tempSaveRoot))
                Directory.Delete(tempSaveRoot, true);
        }

        [Test]
        public void ExtractionConfigBytesAndPrefabContainRequiredData()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes($"Assets/Game/Config/{name}.bytes"))));
            Assert.That(provider.TryGetExtraction("extraction.task.main", out var config, out var error), Is.True, error);
            Assert.That(config.PrefabAddress, Is.EqualTo("Prefabs/SceneObjects/TaskExit"));

            var prefab = AssetDatabase.LoadAssetAtPath<TaskExitView>(
                "Assets/Game/Prefabs/SceneObjects/TaskExit.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(AssetDatabase.AssetPathToGUID("Assets/Game/Prefabs/SceneObjects/TaskExit.prefab"),
                Is.EqualTo("7601f4cf38f9df64ba0ae5ac580b6bf7"));
            Assert.That(prefab.GetComponent<Collider>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<Collider>().isTrigger, Is.True);
            Assert.That(prefab.HasVisualRoot, Is.True);
            Assert.That(prefab.HasPromptRoot, Is.True);
            Assert.That(prefab.HasPromptText, Is.True);
            Assert.That(prefab.TryValidatePresentation(out var presentationError), Is.True, presentationError);
            var promptRoot = prefab.transform.Find("PromptRoot");
            Assert.That(promptRoot, Is.Not.Null);
            Assert.That(promptRoot.gameObject.activeSelf, Is.False);
            Assert.That(promptRoot.localPosition, Is.EqualTo(new Vector3(0f, 1.5f, 0f)));
            Assert.That(promptRoot.localScale, Is.EqualTo(new Vector3(0.01f, 0.01f, 0.01f)));
            Assert.That(promptRoot.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(promptRoot.GetComponent<UnityEngine.UI.GraphicRaycaster>(), Is.Null);
            var promptText = promptRoot.Find("PromptText").GetComponent<TMP_Text>();
            Assert.That(promptText.rectTransform.sizeDelta, Is.EqualTo(new Vector2(360f, 60f)));
            Assert.That(promptText.fontSize, Is.EqualTo(28f));
            Assert.That(promptText.raycastTarget, Is.False);
        }

        [Test]
        public void MapsWithoutExtractionPrepareAndCommitSuccessfully()
        {
            var ctrl = new ExtractionCtrl(Game.Interface);
            var context = CreateContext(10, Array.Empty<MapSpawnDefinition>());

            Assert.That(Prepare(ctrl, context).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<ExtractionModel>().Extractions.Count, Is.EqualTo(0));
        }

        [Test]
        public void PrepareCreatesConfiguredExtractionAtLocalPoseAndLoadsOnce()
        {
            loader.Asset = CreateExitPrefab("exit-prefab");
            var ctrl = new ExtractionCtrl(Game.Interface);
            var spawns = new[]
            {
                ExtractionSpawn("a", new Vector3(0f, 0.6f, 12f)),
                ExtractionSpawn("b", new Vector3(2f, 0.6f, 12f))
            };
            var context = CreateContext(11, spawns);

            Assert.That(Prepare(ctrl, context).Success, Is.True);

            Assert.That(loader.LoadCount("Prefabs/SceneObjects/TaskExit"), Is.EqualTo(1));
            Assert.That(instances.Created.Count, Is.EqualTo(2));
            Assert.That(instances.Created.Select(item => item.transform.localPosition),
                Is.EquivalentTo(spawns.Select(item => item.LocalPosition)));
            Assert.That(instances.Created.All(item => !item.activeSelf), Is.True);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<ExtractionModel>().Extractions.Count, Is.EqualTo(0));
        }

        [Test]
        public void PrepareRejectsMissingConfigAndBrokenPrefabWithoutResidue()
        {
            var missing = new ExtractionCtrl(Game.Interface);
            Assert.That(Prepare(missing, CreateContext(12, new[]
            {
                ExtractionSpawn("missing", Vector3.zero, "extraction.missing")
            })).Success, Is.False);

            loader.Asset = new GameObject("broken", typeof(BoxCollider));
            loader.Asset.GetComponent<BoxCollider>().isTrigger = true;
            var broken = new ExtractionCtrl(Game.Interface);
            Assert.That(Prepare(broken, CreateContext(13, new[]
            {
                ExtractionSpawn("broken", Vector3.zero)
            })).Success, Is.False);

            Assert.That(instances.Created.Count, Is.EqualTo(0));
            Assert.That(instances.ReleaseCount, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<ExtractionModel>().Extractions.Count, Is.EqualTo(0));
        }

        [Test]
        public void CancelledExtractionLoadReturnsBeforeCallbackAndBlocksConcurrentLoad()
        {
            loader.DeferCallbacks = true;
            loader.Asset = CreateExitPrefab("delayed-exit");
            var ctrl = new ExtractionCtrl(Game.Interface);
            using var cts = new CancellationTokenSource();
            var context = CreateContext(14, new[] { ExtractionSpawn("a", Vector3.zero) });

            var first = ctrl.PrepareMapAsync(context, cts.Token);
            var concurrent = Prepare(ctrl, context);
            cts.Cancel();
            var cancelled = first.GetAwaiter().GetResult();

            Assert.That(concurrent.Success, Is.False);
            Assert.That(cancelled.Success, Is.False);
            Assert.That(instances.Created.Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<ExtractionModel>().Extractions.Count, Is.EqualTo(0));
        }

        [Test]
        public void LateExtractionLoadCallbackReleasesHandleOnceAndAllowsSameControllerRetry()
        {
            loader.DeferCallbacks = true;
            loader.Asset = CreateExitPrefab("late-exit");
            var ctrl = new ExtractionCtrl(Game.Interface);
            using var cts = new CancellationTokenSource();

            var first = ctrl.PrepareMapAsync(CreateContext(15, new[] { ExtractionSpawn("a", Vector3.zero) }), cts.Token);
            cts.Cancel();
            Assert.That(first.GetAwaiter().GetResult().Success, Is.False);

            loader.CompletePending();
            Assert.That(loader.ReleaseCount, Is.EqualTo(1));

            loader.DeferCallbacks = false;
            Assert.That(Prepare(ctrl, CreateContext(16, new[] { ExtractionSpawn("b", Vector3.one) })).Success, Is.True);
            Assert.That(loader.LoadCount("Prefabs/SceneObjects/TaskExit"), Is.EqualTo(2));
            Assert.That(instances.Created.Count, Is.EqualTo(1));

            ctrl.RollbackPreparedMap();
            Assert.That(loader.ReleaseCount, Is.EqualTo(2));
            Assert.That(instances.ReleaseCount, Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<ExtractionModel>().Extractions.Count, Is.EqualTo(0));
        }

        [Test]
        public void CommitRegistersUnitAndExtractionFromConfig()
        {
            loader.Asset = CreateExitPrefab("exit-prefab");
            Game.Interface.GetModel<ExpeditionModel>().BeginTask(MapIds.MissionSample, 0);
            var ctrl = new ExtractionCtrl(Game.Interface);

            Assert.That(Prepare(ctrl, CreateContext(17, new[]
            {
                ExtractionSpawn("a", new Vector3(0f, 0.6f, 12f))
            })).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);

            var unit = Game.Interface.GetModel<UnitModel>().Units.Values.Single();
            Assert.That(unit.UnitType, Is.EqualTo(UnitType.ExtractionPoint));
            Assert.That(unit.UnitSource, Is.EqualTo(UnitSource.MapPlaced));
            Assert.That(unit.ConfigId, Is.EqualTo("extraction.task.main"));
            Assert.That(unit.MapInstanceId, Is.EqualTo(17));
            Assert.That(Game.Interface.GetModel<ExtractionModel>().TryGet(unit.UnitId, out var extraction), Is.True);
            Assert.That(extraction.MapInstanceId, Is.EqualTo(17));
            Assert.That(extraction.Available, Is.True);
            Assert.That(instances.Created[0].activeSelf, Is.True);
        }

        [Test]
        public void CommitFailureRollsBackCurrentBatchAndKeepsExistingMap()
        {
            loader.Asset = CreateExitPrefab("exit-prefab");
            var current = new ExtractionCtrl(Game.Interface);
            Assert.That(Prepare(current, CreateContext(18, new[] { ExtractionSpawn("old", Vector3.zero) })).Success, Is.True);
            Assert.That(current.CommitPreparedMap().Success, Is.True);
            var oldUnit = Game.Interface.GetModel<UnitModel>().Units.Keys.Single();

            var next = new ExtractionCtrl(Game.Interface);
            Assert.That(Prepare(next, CreateContext(19, new[]
            {
                ExtractionSpawn("new-a", Vector3.one),
                ExtractionSpawn("new-b", Vector3.one * 2f)
            })).Success, Is.True);
            RegisterUnit(1002, 99, UnitType.Loot, "blocking.unit", true);

            Assert.That(next.CommitPreparedMap().Success, Is.False);

            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(oldUnit, out _), Is.True);
            Assert.That(Game.Interface.GetModel<ExtractionModel>().TryGet(oldUnit, out _), Is.True);
            Assert.That(Game.Interface.GetModel<UnitModel>().Units.Values.Count(item => item.ConfigId == "extraction.task.main"), Is.EqualTo(1));
            Assert.That(instances.ReleaseCount, Is.EqualTo(2));
        }

        [Test]
        public void DeactivateRestoreRollbackReleaseAndReleaseAllAreIdempotent()
        {
            loader.Asset = CreateExitPrefab("exit-prefab");
            Game.Interface.GetModel<ExpeditionModel>().BeginTask(MapIds.MissionSample, 0);
            var ctrl = new ExtractionCtrl(Game.Interface);
            Assert.That(Prepare(ctrl, CreateContext(20, new[] { ExtractionSpawn("a", Vector3.zero) })).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            var unitId = Game.Interface.GetModel<UnitModel>().Units.Keys.Single();

            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            RegisterUnit(PlayerModel.LocalPlayerUnitId, 20, UnitType.Player,
                "player.graybox.default", true);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player.graybox.default", string.Empty, 100, 10, 4f, 1f, 1f, 1f, 1f),
                false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            var extractionSystem = Game.Interface.GetSystem<ExtractionSystem>();
            Assert.That(extractionSystem.TrySetPlayerInRange(
                unitId, PlayerModel.LocalPlayerUnitId, true, out var rangeError), Is.True, rangeError);
            var canceled = new List<ExtractionCountdownCanceledEvent>();
            var canceledUnregister = Game.Interface.RegisterEvent<ExtractionCountdownCanceledEvent>(canceled.Add);
            Assert.That(extractionSystem.TryBeginExtraction(
                unitId, PlayerModel.LocalPlayerUnitId, out var actionId, out var beginError), Is.True, beginError);
            Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out var action), Is.True);
            Assert.That(action.Kind, Is.EqualTo(PlayerActionKind.Extraction));
            Assert.That(action.ActionId, Is.EqualTo(actionId));

            LogAssert.Expect(LogType.Error, "[UIRoot] 缺少必需场景对象：UIRoot。");
            ctrl.SetCurrentMapActive(false);
            Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out _), Is.False);
            Assert.That(canceled, Has.Count.EqualTo(1));
            Assert.That(canceled[0].ActionId, Is.EqualTo(actionId));
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(unitId, out var inactive), Is.True);
            Assert.That(inactive.IsActive, Is.False);
            Assert.That(Game.Interface.GetModel<ExtractionModel>().TryGet(unitId, out var unavailable), Is.True);
            Assert.That(unavailable.Available, Is.False);
            Assert.That(unavailable.PlayerInside, Is.False);
            Assert.That(instances.Created[0].activeSelf, Is.False);
            canceledUnregister.UnRegister();

            ctrl.SetCurrentMapActive(true);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(unitId, out var active), Is.True);
            Assert.That(active.IsActive, Is.True);
            Assert.That(Game.Interface.GetModel<ExtractionModel>().TryGet(unitId, out var available), Is.True);
            Assert.That(available.Available, Is.True);

            Game.Interface.GetSystem<UnitSystem>().Remove(
                PlayerModel.LocalPlayerUnitId, UnitRemovedReason.MapInstanceReleased);
            ctrl.ReleaseCurrentMap();
            ctrl.ReleaseCurrentMap();
            ctrl.RollbackPreparedMap();
            ctrl.ReleaseAll();
            ctrl.ReleaseAll();
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<ExtractionModel>().Extractions.Count, Is.EqualTo(0));
            Assert.That(instances.ReleaseCount, Is.EqualTo(1));
            Assert.That(loader.ReleaseCount, Is.EqualTo(1));

            loader.DeferCallbacks = true;
            loader.Asset = CreateExitPrefab("releaseall-loading-exit");
            var loadingCtrl = new ExtractionCtrl(Game.Interface);
            var loading = loadingCtrl.PrepareMapAsync(
                CreateContext(200, new[] { ExtractionSpawn("loading", Vector3.zero) }),
                CancellationToken.None);

            loadingCtrl.ReleaseAll();
            Assert.That(loading.GetAwaiter().GetResult().Success, Is.False);
            Assert.That(loader.ReleaseCount, Is.EqualTo(2));
            Assert.That(instances.Created.Count, Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<ExtractionModel>().Extractions.Count, Is.EqualTo(0));

            loader.CompletePending();
            Assert.That(loader.ReleaseCount, Is.EqualTo(2));
        }

        [Test]
        public void ExtractionRegistrationRejectsInvalidInactiveWrongTypeAndDuplicateUnit()
        {
            var system = Game.Interface.GetSystem<ExtractionSystem>();
            Assert.That(system.TryRegisterExtraction(0, true, out _), Is.False);
            Assert.That(system.TryRegisterExtraction(404, true, out _), Is.False);

            RegisterUnit(30, 21, UnitType.Loot, "loot.test-resource", true);
            Assert.That(system.TryRegisterExtraction(30, true, out _), Is.False);

            RegisterUnit(31, 21, UnitType.ExtractionPoint, "extraction.task.main", false);
            Assert.That(system.TryRegisterExtraction(31, true, out _), Is.False);

            RegisterUnit(32, 21, UnitType.ExtractionPoint, "", true);
            Assert.That(system.TryRegisterExtraction(32, true, out _), Is.False);

            RegisterUnit(33, 21, UnitType.ExtractionPoint, "extraction.task.main", true);
            Assert.That(system.TryRegisterExtraction(33, true, out _), Is.True);
            Assert.That(system.TryRegisterExtraction(33, true, out _), Is.False);
            Assert.That(system.TryRemoveExtraction(999), Is.False);
            Assert.That(system.TrySetAvailable(999, true), Is.False);
        }

        [Test]
        public void AvailabilityFollowsExpeditionQualificationAndMapActivity()
        {
            loader.Asset = CreateExitPrefab("exit-prefab");
            var ctrl = new ExtractionCtrl(Game.Interface);
            Game.Interface.GetModel<ExpeditionModel>().BeginTask(MapIds.MissionSample, 2);
            Assert.That(Prepare(ctrl, CreateContext(22, new[] { ExtractionSpawn("a", Vector3.zero) })).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            var unitId = Game.Interface.GetModel<UnitModel>().Units.Keys.Single();

            Assert.That(Game.Interface.GetModel<ExtractionModel>().TryGet(unitId, out var unavailable), Is.True);
            Assert.That(unavailable.Available, Is.False);
            Assert.That(VisualRoot(instances.Created[0]).activeSelf, Is.False);

            Game.Interface.GetModel<ExpeditionModel>().AddTaskKill();
            Game.Interface.GetModel<ExpeditionModel>().AddTaskKill();
            Game.Interface.SendEvent(new ExpeditionChangedEvent());
            Assert.That(Game.Interface.GetModel<ExtractionModel>().TryGet(unitId, out var qualified), Is.True);
            Assert.That(qualified.Available, Is.True);
            Assert.That(VisualRoot(instances.Created[0]).activeSelf, Is.True);

            ctrl.SetCurrentMapActive(false);
            Assert.That(Game.Interface.GetModel<ExtractionModel>().TryGet(unitId, out var disabled), Is.True);
            Assert.That(disabled.Available, Is.False);
            Assert.That(VisualRoot(instances.Created[0]).activeSelf, Is.False);

            ctrl.SetCurrentMapActive(true);
            Assert.That(Game.Interface.GetModel<ExtractionModel>().TryGet(unitId, out var restored), Is.True);
            Assert.That(restored.Available, Is.True);
            Assert.That(VisualRoot(instances.Created[0]).activeSelf, Is.True);
        }

        [Test]
        public void TriggerRequiresCurrentPlayerColliderAndInteractionCompletesFiveSecondAction()
        {
            loader.Asset = CreateExitPrefab("exit-prefab");
            Game.Interface.GetModel<ExpeditionModel>().BeginTask(MapIds.MissionSample, 0);
            var triggerExpeditionId = Guid.NewGuid().ToString("N");
            Game.Interface.GetModel<ExpeditionModel>().SetExpeditionId(triggerExpeditionId);
            BindEmptyTaskSection(MapIds.MissionSample, triggerExpeditionId);
            var ctrl = new ExtractionCtrl(Game.Interface);
            Assert.That(Prepare(ctrl, CreateContext(23, new[] { ExtractionSpawn("a", Vector3.zero) })).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            var exitUnit = Game.Interface.GetModel<UnitModel>().Units.Keys.Single();
            RegisterUnit(PlayerModel.LocalPlayerUnitId, 23, UnitType.Player, "player.graybox.default", true);
            var wrongCollider = new GameObject("wrong").AddComponent<BoxCollider>();
            var playerCollider = new GameObject("player").AddComponent<BoxCollider>();

            Trigger(instances.Created[0], wrongCollider);
            Assert.That(returnStartedCount, Is.EqualTo(0));

            playerPorts.AcceptedCollider = playerCollider;
            playerPorts.MapInstanceId = 999;
            Trigger(instances.Created[0], playerCollider);
            Assert.That(returnStartedCount, Is.EqualTo(0));

            playerPorts.MapInstanceId = 23;
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            LogAssert.Expect(LogType.Error, "[UIRoot] 缺少必需场景对象：UIRoot。");
            LogAssert.Expect(LogType.Error, "[ExtractionSystem] 返回基地失败：测试地图加载失败");
            LogAssert.Expect(LogType.Error, "[UIRoot] 缺少必需场景对象：UIRoot。");
            Trigger(instances.Created[0], playerCollider);
            Assert.That(Game.Interface.GetModel<ExtractionModel>().TryGet(exitUnit, out var snapshot), Is.True);
            Assert.That(snapshot.PlayerInside, Is.True);
            Assert.That(snapshot.Returning, Is.False);
            Assert.That(returnStartedCount, Is.Zero);
            instances.Created[0].GetComponent<TaskExitView>().Interact();
            Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out var action), Is.True);
            Assert.That(action.Kind, Is.EqualTo(PlayerActionKind.Extraction));
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(4.99f);
            Assert.That(returnStartedCount, Is.Zero);
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(0.01f);
            Assert.That(returnStartedCount, Is.EqualTo(1));
        }

        [Test]
        public void ReturnRejectsUnavailableInactiveWrongTypeAndDifferentMapUnits()
        {
            var system = Game.Interface.GetSystem<ExtractionSystem>();
            RegisterUnit(4100, 24, UnitType.Player, "player.graybox.default", true);
            RegisterUnit(4101, 24, UnitType.ExtractionPoint, "extraction.task.main", true);
            Assert.That(system.TryRegisterExtraction(4101, false, out _), Is.True);
            Assert.That(system.TryReturnToBaseAsync(4101, 4100).GetAwaiter().GetResult().Success, Is.False);

            RegisterUnit(4110, 25, UnitType.Player, "player.graybox.default", true);
            RegisterUnit(4111, 25, UnitType.ExtractionPoint, "extraction.task.main", true);
            Assert.That(system.TryRegisterExtraction(4111, true, out _), Is.True);
            Game.Interface.GetSystem<UnitSystem>().TrySetActive(4111, false);
            Assert.That(system.TryReturnToBaseAsync(4111, 4110).GetAwaiter().GetResult().Success, Is.False);

            RegisterUnit(4120, 26, UnitType.Player, "player.graybox.default", true);
            RegisterUnit(4121, 26, UnitType.ExtractionPoint, "extraction.task.main", true);
            Assert.That(system.TryRegisterExtraction(4121, true, out _), Is.True);
            Game.Interface.GetSystem<UnitSystem>().Remove(4121, UnitRemovedReason.InvalidPresentation);
            RegisterUnit(4121, 26, UnitType.Loot, "loot.test-resource", true);
            Assert.That(system.TryReturnToBaseAsync(4121, 4120).GetAwaiter().GetResult().Success, Is.False);

            RegisterUnit(4130, 27, UnitType.Loot, "loot.test-resource", true);
            RegisterUnit(4131, 27, UnitType.ExtractionPoint, "extraction.task.main", true);
            Assert.That(system.TryRegisterExtraction(4131, true, out _), Is.True);
            Assert.That(system.TryReturnToBaseAsync(4131, 4130).GetAwaiter().GetResult().Success, Is.False);

            RegisterUnit(4140, 100, UnitType.Player, "player.graybox.default", true);
            RegisterUnit(4141, 101, UnitType.ExtractionPoint, "extraction.task.main", true);
            Assert.That(system.TryRegisterExtraction(4141, true, out _), Is.True);
            Assert.That(system.TryReturnToBaseAsync(4141, 4140).GetAwaiter().GetResult().Success, Is.False);
        }

        [Test]
        public void ExtractionAttemptRejectsDuplicatesAndCancelsForDamageAndLeavingRange()
        {
            var unitId = RegisterBeginReadyExtraction(240);
            var system = Game.Interface.GetSystem<ExtractionSystem>();
            var actions = Game.Interface.GetSystem<PlayerActionSystem>();
            var canceled = new List<ExtractionCountdownCanceledEvent>();
            var unregister = Game.Interface.RegisterEvent<ExtractionCountdownCanceledEvent>(canceled.Add);
            try
            {
                Assert.That(system.TryBeginExtraction(unitId, PlayerModel.LocalPlayerUnitId,
                    out var firstActionId, out var error), Is.True, error);
                Assert.That(system.TryBeginExtraction(unitId, PlayerModel.LocalPlayerUnitId,
                    out var rejectedActionId, out _), Is.False);
                Assert.That(rejectedActionId, Is.Zero);
                Assert.That(actions.TryGetSnapshot(out var unchanged), Is.True);
                Assert.That(unchanged.ActionId, Is.EqualTo(firstActionId));

                Assert.That(Game.Interface.GetSystem<CombatSystem>()
                    .TryApplyPlayerEnvironmentalDamage(1, out var applied), Is.True);
                Assert.That(applied, Is.EqualTo(1));
                Assert.That(actions.TryGetSnapshot(out _), Is.False);
                Assert.That(canceled.Last().Reason, Is.EqualTo(PlayerActionCancelReason.Damaged));

                Assert.That(system.TryBeginExtraction(unitId, PlayerModel.LocalPlayerUnitId,
                    out _, out error), Is.True, error);
                Assert.That(system.TrySetPlayerInRange(unitId, PlayerModel.LocalPlayerUnitId,
                    false, out error), Is.True, error);
                Assert.That(actions.TryGetSnapshot(out _), Is.False);
                Assert.That(canceled.Last().Reason, Is.EqualTo(PlayerActionCancelReason.LeftRange));
            }
            finally
            {
                unregister.UnRegister();
            }
        }

        [Test]
        public void ExtractionRemovalCancelsOnceAndPublishesImmutableRemoval()
        {
            var unitId = RegisterBeginReadyExtraction(241);
            var system = Game.Interface.GetSystem<ExtractionSystem>();
            var removed = new List<ExtractionRemovedEvent>();
            var canceled = new List<ExtractionCountdownCanceledEvent>();
            var removeUnregister = Game.Interface.RegisterEvent<ExtractionRemovedEvent>(removed.Add);
            var cancelUnregister = Game.Interface.RegisterEvent<ExtractionCountdownCanceledEvent>(canceled.Add);
            try
            {
                Assert.That(system.TryBeginExtraction(unitId, PlayerModel.LocalPlayerUnitId,
                    out var actionId, out var error), Is.True, error);
                Assert.That(system.TryRemoveExtraction(unitId), Is.True);
                Assert.That(system.TryRemoveExtraction(unitId), Is.False);
                Assert.That(canceled.Count, Is.EqualTo(1));
                Assert.That(canceled[0].ActionId, Is.EqualTo(actionId));
                Assert.That(removed.Count, Is.EqualTo(1));
                Assert.That(removed[0].ExtractionUnitId, Is.EqualTo(unitId));
                Assert.That(removed[0].MapInstanceId, Is.EqualTo(241));
            }
            finally
            {
                removeUnregister.UnRegister();
                cancelUnregister.UnRegister();
            }
        }

        [Test]
        public void FailedOrExceptionalReturnResetsReturningAndAllowsRetry()
        {
            var unitId = RegisterReturnReadyExtraction(25);
            Game.Interface.GetModel<ExpeditionModel>().Clear();
            var failed = Game.Interface.GetSystem<ExtractionSystem>()
                .TryReturnToBaseAsync(unitId, PlayerModel.LocalPlayerUnitId)
                .GetAwaiter()
                .GetResult();
            Assert.That(failed.Success, Is.False);
            Assert.That(Game.Interface.GetModel<ExtractionModel>().TryGet(unitId, out var afterFailure), Is.True);
            Assert.That(afterFailure.Returning, Is.False);

            Game.Interface.GetModel<ExpeditionModel>().BeginTask(MapIds.MissionSample, 0);
            var throwOnce = true;
            var throwingSubscription = Game.Interface.RegisterEvent<ExtractionReturnStartedEvent>(_ =>
            {
                if (!throwOnce)
                {
                    return;
                }

                throwOnce = false;
                throw new InvalidOperationException("测试返回开始事件异常");
            });
            var exceptional = Game.Interface.GetSystem<ExtractionSystem>()
                .TryReturnToBaseAsync(unitId, PlayerModel.LocalPlayerUnitId)
                .GetAwaiter()
                .GetResult();
            Assert.That(exceptional.Success, Is.False);
            Assert.That(Game.Interface.GetModel<ExtractionModel>().TryGet(unitId, out var afterException), Is.True);
            Assert.That(afterException.Returning, Is.False);
            throwingSubscription.UnRegister();

            Game.Interface.GetModel<ExpeditionModel>().BeginTask(MapIds.MissionSample, 0);
            var retry = Game.Interface.GetSystem<ExtractionSystem>()
                .TryReturnToBaseAsync(unitId, PlayerModel.LocalPlayerUnitId)
                .GetAwaiter()
                .GetResult();
            Assert.That(retry.Success, Is.False);
            Assert.That(returnStartedCount, Is.EqualTo(3));
        }

        [Test]
        public void ConcurrentOrRepeatedReturnStartsMapFlowOnce()
        {
            var unitId = RegisterReturnReadyExtraction(26);
            mapLoader.DeferLoad = true;

            var first = Game.Interface.GetSystem<ExtractionSystem>()
                .TryReturnToBaseAsync(unitId, PlayerModel.LocalPlayerUnitId);
            Assert.That(Game.Interface.GetModel<ExtractionModel>().TryGet(unitId, out var returning), Is.True);
            if (!returning.Returning)
            {
                Assert.Fail(first.GetAwaiter().GetResult().Error);
            }

            var second = Game.Interface.GetSystem<ExtractionSystem>()
                .TryReturnToBaseAsync(unitId, PlayerModel.LocalPlayerUnitId)
                .GetAwaiter()
                .GetResult();
            Assert.That(second.Success, Is.False);
            Assert.That(mapLoader.LoadCount, Is.EqualTo(1));
            Assert.That(returnStartedCount, Is.EqualTo(1));

            LogAssert.Expect(LogType.Error, "[BaseUICtrl] UI Prefab 未找到：Prefabs/UI/Home/Start");
            mapLoader.CompleteLoad(MapContentLoadResult.Fail("测试加载失败"));
            Assert.That(first.GetAwaiter().GetResult().Success, Is.False);
            Assert.That(Game.Interface.GetModel<ExtractionModel>().TryGet(unitId, out var reset), Is.True);
            Assert.That(reset.Returning, Is.False);

            ResetHarness();
            loader.Assets["Prefabs/Characters/GrayboxPlayer"] =
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            loader.Assets["Prefabs/SceneObjects/TaskExit"] =
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/TaskExit.prefab");
            loader.Assets["Prefabs/Combat/GrayboxArrow"] =
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxArrow.prefab");
            configs.Player = TestConfigProvider.DefaultPlayer;
            configs.MapSpawns = new[]
            {
                new MapSpawnDefinition("player.mission", MapIds.MissionSample, MapSpawnObjectType.Player, "player.graybox.default", 0f, 0.6f, -12f, 0f),
                new MapSpawnDefinition("extraction.mission", MapIds.MissionSample, MapSpawnObjectType.Extraction, "extraction.task.main", 0f, 0.6f, 12f, 0f),
                new MapSpawnDefinition("player.base", MapIds.Base, MapSpawnObjectType.Player, "player.graybox.default", 0f, 0f, 0f, 0f)
            };
            mapLoader.NextLoadSucceeds = true;
            LogAssert.Expect(LogType.Error, "[PlayerMoveSystem] 玩家移动端口未绑定");
            LogAssert.Expect(LogType.Error, "[PlayerCameraSystem] 玩家相机端口未绑定");
            LogAssert.Expect(LogType.Error, "[PlayerConditionSystem] 玩家体力环端口未绑定");
            var loadedMission = Game.Interface.GetSystem<MapSceneSystem>()
                .LoadMapAsync(MapIds.MissionSample)
                .GetAwaiter()
                .GetResult();
            Assert.That(loadedMission.Success, Is.True, loadedMission.Error);
            mapLoader.LoadCount = 0;
            Game.Interface.GetModel<ExpeditionModel>().BeginTask(MapIds.MissionSample, 0);
            var successExpeditionId = Guid.NewGuid().ToString("N");
            Game.Interface.GetModel<ExpeditionModel>().SetExpeditionId(successExpeditionId);
            BindEmptyTaskSection(MapIds.MissionSample, successExpeditionId);
            Game.Interface.GetModel<SaveModel>().SetExpedition(new ExpeditionSaveData
            {
                ExpeditionId = successExpeditionId,
                MapId = MapIds.MissionSample,
                Status = ExpeditionStatus.ReturnQualified
            });
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(
                PlayerBagRiskState.InExpedition, "test", false, out var riskError), Is.True, riskError);
            Game.Interface.SendEvent(new ExpeditionChangedEvent());
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.TryAdd(ItemIds.MetalParts, 2, 20, out _, out var error), Is.True, error);
            var successUnitId = Game.Interface.GetModel<UnitModel>()
                .Units
                .Values
                .Single(item => item.UnitType == UnitType.ExtractionPoint)
                .UnitId;

            LogAssert.Expect(LogType.Error, "[PlayerMoveSystem] 玩家移动端口未绑定");
            LogAssert.Expect(LogType.Error, "[PlayerCameraSystem] 玩家相机端口未绑定");
            LogAssert.Expect(LogType.Error, "[PlayerConditionSystem] 玩家体力环端口未绑定");
            var success = Game.Interface.GetSystem<ExtractionSystem>()
                .TryReturnToBaseAsync(successUnitId, PlayerModel.LocalPlayerUnitId)
                .GetAwaiter()
                .GetResult();

            Assert.That(success.Success, Is.True, success.Error);
            Assert.That(mapLoader.LoadCount, Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(2));
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(0));

            var repeated = Game.Interface.GetSystem<ExtractionSystem>()
                .TryReturnToBaseAsync(successUnitId, PlayerModel.LocalPlayerUnitId)
                .GetAwaiter()
                .GetResult();
            Assert.That(repeated.Success, Is.False);
            Assert.That(mapLoader.LoadCount, Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.Zero,
                "重复失败请求不得在成功返回已经完成后再次改变结算状态");
        }

        [Test]
        public void ScenesAndMapContextContainNoPreplacedGameplayObjects()
        {
            foreach (var path in new[]
                     {
                         "Assets/Game/Scenes/BaseMap.unity",
                         "Assets/Game/Scenes/SurvivalSampleMap.unity",
                         "Assets/Game/Scenes/TaskSampleMap.unity"
                     })
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Assert.That(FindInScene<PlayerView>(scene).Count, Is.EqualTo(0), path);
                Assert.That(FindInScene<EnemyView>(scene).Count, Is.EqualTo(0), path);
                Assert.That(FindInScene<ResourcePickupView>(scene).Count, Is.EqualTo(0), path);
                Assert.That(FindInScene<TaskExitView>(scene).Count, Is.EqualTo(0), path);
                var contexts = FindInScene<MapContext>(scene);
                Assert.That(contexts, Has.Count.EqualTo(1), path);
                Assert.That(contexts[0].Validate(out var error), Is.True, error);
            }

            var fields = typeof(MapContext).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(fields.Any(item => item.Name == "extractionPoints"), Is.False);
            Assert.That(typeof(MapContext).GetProperty("ExtractionPoints"), Is.Null);
            Assert.That(AssetDatabase.AssetPathToGUID("Assets/Game/Prefabs/SceneObjects/TaskExit.prefab"),
                Is.EqualTo("7601f4cf38f9df64ba0ae5ac580b6bf7"));
        }

        private static MapControllerResult Prepare(ExtractionCtrl ctrl, MapPreparationContext context)
        {
            return ctrl.PrepareMapAsync(context, default).GetAwaiter().GetResult();
        }

        private void ResetHarness()
        {
            if (testCamera != null)
            {
                Object.DestroyImmediate(testCamera);
            }

            ResourceLoader.ResetForTests();
            TestReset.ResetArchitecture();
            loader = new TestResourceLoader();
            instances = new TestViewInstanceProvider();
            configs = new TestConfigProvider();
            playerPorts = new TestPlayerRuntimeProvider();
            mapLoader = new TestMapContentLoader();
            returnStartedCount = 0;
            testCamera = new GameObject("Extraction Test Camera", typeof(Camera), typeof(AudioListener));
            testCamera.tag = "MainCamera";
            ResourceLoader.SetLoaderForTests(loader);
            Game.Interface.RegisterUtility<IViewInstanceProvider>(instances);
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(configs);
            Game.Interface.RegisterUtility<IPlayerRuntimePortProvider>(playerPorts);
            Game.Interface.RegisterUtility<IMapContentLoader>(mapLoader);
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempSaveRoot);
            Game.Interface.RegisterEvent<ExtractionReturnStartedEvent>(_ => returnStartedCount++);
        }

        private int RegisterReturnReadyExtraction(long mapInstanceId, bool registerPlayer = true)
        {
            Game.Interface.GetModel<ExpeditionModel>().BeginTask(MapIds.MissionSample, 0);
            var expeditionId = Guid.NewGuid().ToString("N");
            Game.Interface.GetModel<ExpeditionModel>().SetExpeditionId(expeditionId);
            BindEmptyTaskSection(MapIds.MissionSample, expeditionId);
            if (registerPlayer)
            {
                RegisterUnit(PlayerModel.LocalPlayerUnitId, mapInstanceId, UnitType.Player, "player.graybox.default", true);
            }

            RegisterUnit(5000 + (int)mapInstanceId, mapInstanceId, UnitType.ExtractionPoint, "extraction.task.main", true);
            var unitId = 5000 + (int)mapInstanceId;
            Assert.That(Game.Interface.GetSystem<ExtractionSystem>().TryRegisterExtraction(unitId, true, out var error), Is.True, error);
            return unitId;
        }

        private int RegisterBeginReadyExtraction(long mapInstanceId)
        {
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            Game.Interface.GetModel<ExpeditionModel>().BeginTask(MapIds.MissionSample, 0);
            RegisterUnit(PlayerModel.LocalPlayerUnitId, mapInstanceId, UnitType.Player,
                "player.graybox.default", true);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player.graybox.default", string.Empty, 100, 10, 4f, 1f, 1f, 1f, 1f),
                false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            var unitId = 5000 + (int)mapInstanceId;
            RegisterUnit(unitId, mapInstanceId, UnitType.ExtractionPoint,
                "extraction.task.main", true);
            var system = Game.Interface.GetSystem<ExtractionSystem>();
            Assert.That(system.TryRegisterExtraction(unitId, true, out var error), Is.True, error);
            Assert.That(system.TrySetPlayerInRange(unitId, PlayerModel.LocalPlayerUnitId,
                true, out error), Is.True, error);
            return unitId;
        }

        private static void RegisterUnit(int unitId, long mapInstanceId, UnitType type, string configId, bool active)
        {
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                    unitId,
                    mapInstanceId,
                    type,
                    type == UnitType.Player ? UnitSource.Player : UnitSource.MapPlaced,
                    configId,
                    Vector3.zero,
                    Quaternion.identity,
                    UnitAuthorityMode.Local,
                    0,
                    active),
                out var error), Is.True, error);
        }

        private static void BindEmptyTaskSection(string mapId, string expeditionId)
        {
            Game.Interface.GetModel<ExpeditionTaskModel>().Restore(new JObject
            {
                ["schemaVersion"] = 1,
                ["mapId"] = mapId,
                ["expeditionId"] = expeditionId,
                ["tasks"] = new JArray()
            });
        }

        private static MapPreparationContext CreateContext(long mapInstanceId, IReadOnlyList<MapSpawnDefinition> spawns)
        {
            var root = new GameObject("map-root");
            var runtime = new GameObject("DynamicObjectRoot").transform;
            runtime.SetParent(root.transform, false);
            var map = new GameObject("MapContext");
            var context = map.AddComponent<MapContext>();
            SetField(context, "mapContentRoot", root);
            SetField(context, "dynamicObjectRoot", runtime);
            return new MapPreparationContext("test.map", mapInstanceId, context, spawns);
        }

        private static MapSpawnDefinition ExtractionSpawn(
            string id,
            Vector3 localPosition,
            string configId = "extraction.task.main")
        {
            return new MapSpawnDefinition(id, "test.map", MapSpawnObjectType.Extraction, configId, localPosition.x, localPosition.y, localPosition.z, 0f);
        }

        private static GameObject CreateExitPrefab(string name)
        {
            var prefab = new GameObject(name, typeof(BoxCollider), typeof(TaskExitView));
            var trigger = prefab.GetComponent<BoxCollider>();
            trigger.isTrigger = true;
            var visual = new GameObject("VisualRoot");
            visual.transform.SetParent(prefab.transform, false);
            var prompt = new GameObject("PromptRoot", typeof(RectTransform), typeof(Canvas));
            prompt.transform.SetParent(prefab.transform, false);
            prompt.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var text = new GameObject("PromptText", typeof(RectTransform), typeof(TextMeshProUGUI));
            text.transform.SetParent(prompt.transform, false);
            var view = prefab.GetComponent<TaskExitView>();
            SetField(view, "visualRoot", visual);
            SetField(view, "promptRoot", prompt);
            SetField(view, "promptText", text.GetComponent<TMP_Text>());
            prompt.SetActive(false);
            return prefab;
        }

        private static GameObject VisualRoot(GameObject exitObject)
        {
            return (GameObject)typeof(TaskExitView)
                .GetField("visualRoot", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(exitObject.GetComponent<TaskExitView>());
        }

        private static List<T> FindInScene<T>(UnityEngine.SceneManagement.Scene scene) where T : Component
        {
            var result = new List<T>();
            foreach (var root in scene.GetRootGameObjects())
            {
                result.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return result;
        }

        private static void SetField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
        {
            typeof(TTarget)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        private static void Trigger(GameObject exitObject, Collider collider)
        {
            typeof(TaskExitView)
                .GetMethod("OnTriggerEnter", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(exitObject.GetComponent<TaskExitView>(), new object[] { collider });
        }

        private sealed class TestConfigProvider : IRuntimeConfigProvider
        {
            public static readonly PlayerConfigDefinition DefaultPlayer = new PlayerConfigDefinition(
                "player.graybox.default",
                "Prefabs/Characters/GrayboxPlayer",
                100,
                10,
                1f,
                1f,
                1f,
                1f,
                1f);

            public PlayerConfigDefinition Player { get; set; }
            public IReadOnlyList<MapSpawnDefinition> MapSpawns { get; set; } = Array.Empty<MapSpawnDefinition>();
            public readonly Dictionary<string, ExtractionConfigDefinition> Extractions =
                new Dictionary<string, ExtractionConfigDefinition>
                {
                    ["extraction.task.main"] = new ExtractionConfigDefinition(
                        "extraction.task.main",
                        "Prefabs/SceneObjects/TaskExit")
                };

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = new MapAssetDefinition(id, "test-scene");
                error = null;
                return true;
            }

            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                if (id == ItemIds.MetalParts)
                {
                    value = new ItemDefinition(
                        ItemIds.MetalParts,
                        "Metal Parts",
                        "Metal Parts",
                        "item.metal_parts.name",
                        "item.metal_parts.desc",
                        string.Empty,
                        ItemType.Material,
                        20,
                        ItemUseEffect.None,
                        0);
                    error = null;
                    return true;
                }

                value = default;
                error = "missing item";
                return false;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = MapSpawns.Where(item => item.MapId == mapId).ToArray();
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                if (!string.IsNullOrWhiteSpace(Player.Id) && Player.Id == id)
                {
                    value = Player;
                    error = null;
                    return true;
                }

                value = default;
                error = "missing player";
                return false;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                value = default;
                error = "missing enemy";
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                value = default;
                error = "missing loot";
                return false;
            }

            public bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error)
            {
                if (id == "projectile.arrow.basic")
                {
                    value = new ProjectileConfigDefinition(id, "Prefabs/Combat/GrayboxArrow", 14f, 14f);
                    error = null;
                    return true;
                }

                value = default;
                error = "missing projectile";
                return false;
            }

            public bool TryGetCraftRecipes(out IReadOnlyList<CraftRecipeDefinition> values, out string error)
            {
                values = Array.Empty<CraftRecipeDefinition>();
                error = null;
                return true;
            }

            public bool TryGetTempBuildings(out IReadOnlyList<TempBuildingDefinition> values, out string error)
            {
                values = Array.Empty<TempBuildingDefinition>();
                error = null;
                return true;
            }

            public bool TryGetExpeditionTasks(string mapId, out IReadOnlyList<ExpeditionTaskDefinition> values, out string error)
            {
                values = Array.Empty<ExpeditionTaskDefinition>();
                error = null;
                return true;
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                if (Extractions.TryGetValue(id, out value))
                {
                    error = null;
                    return true;
                }

                error = "missing extraction";
                return false;
            }
        }

        private sealed class TestResourceLoader : IResourceLoader
        {
            private readonly Dictionary<string, int> loads = new Dictionary<string, int>();
            private readonly List<Action> pending = new List<Action>();
            public readonly Dictionary<string, GameObject> Assets = new Dictionary<string, GameObject>();
            public GameObject Asset { get; set; }
            public bool DeferCallbacks { get; set; }
            public int ReleaseCount { get; private set; }

            public T LoadAsset<T>(string path) where T : Object
            {
                return Resolve(path) as T;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                loads[path] = LoadCount(path) + 1;
                var handle = new ResourceLoadHandle(new object());
                if (DeferCallbacks)
                {
                    pending.Add(() => onLoaded?.Invoke(Resolve(path) as T));
                    return handle;
                }

                onLoaded?.Invoke(Resolve(path) as T);
                return handle;
            }

            private GameObject Resolve(string path)
            {
                return Assets.TryGetValue(path, out var mapped) ? mapped : Asset;
            }

            public void Release(ResourceLoadHandle handle)
            {
                if (handle.IsValid)
                {
                    ReleaseCount++;
                }
            }

            public int LoadCount(string path)
            {
                return loads.TryGetValue(path, out var count) ? count : 0;
            }

            public void CompletePending()
            {
                var callbacks = pending.ToArray();
                pending.Clear();
                for (var i = 0; i < callbacks.Length; i++)
                {
                    callbacks[i]?.Invoke();
                }
            }
        }

        private sealed class TestViewInstanceProvider : IViewInstanceProvider
        {
            public readonly List<GameObject> Created = new List<GameObject>();
            public int ReleaseCount { get; private set; }

            public T Instantiate<T>(T prefab, Transform parent, Vector3 position, Quaternion rotation) where T : Component
            {
                var instance = Object.Instantiate(prefab.gameObject, position, rotation, parent);
                Created.Add(instance);
                return instance.GetComponent<T>();
            }

            public void Release(GameObject instance)
            {
                if (instance == null)
                {
                    return;
                }

                ReleaseCount++;
                Object.DestroyImmediate(instance);
            }
        }

        private sealed class TestPlayerRuntimeProvider : IPlayerRuntimePortProvider
        {
            public Collider AcceptedCollider { get; set; }
            public long MapInstanceId { get; set; }

            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort port)
            {
                port = null;
                return false;
            }

            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port)
            {
                port = null;
                return false;
            }

            public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider)
            {
                return collider != null &&
                       collider == AcceptedCollider &&
                       unitId == PlayerModel.LocalPlayerUnitId &&
                       mapInstanceId == MapInstanceId;
            }
        }

        private sealed class TestMapContentLoader : IMapContentLoader
        {
            private UniTaskCompletionSource<MapContentLoadResult> pending;
            private readonly List<TestMapHandle> handles = new List<TestMapHandle>();
            public bool NextLoadSucceeds { get; set; }
            public bool DeferLoad { get; set; }
            public int LoadCount { get; set; }

            public UniTask<MapContentLoadResult> LoadAsync(
                string mapId,
                string address,
                Action<float> onProgress,
                CancellationToken cancellationToken = default)
            {
                LoadCount++;
                if (DeferLoad)
                {
                    pending = new UniTaskCompletionSource<MapContentLoadResult>();
                    return pending.Task;
                }

                if (NextLoadSucceeds)
                {
                    return UniTask.FromResult(MapContentLoadResult.Ok(CreateHandle(mapId)));
                }

                return UniTask.FromResult(MapContentLoadResult.Fail("测试地图加载失败"));
            }

            public UniTask<MapContentUnloadResult> UnloadAsync(
                IMapContentHandle handle,
                CancellationToken cancellationToken = default)
            {
                if (handle is TestMapHandle testHandle)
                {
                    testHandle.Valid = false;
                    if (testHandle.Root != null)
                    {
                        Object.DestroyImmediate(testHandle.Root);
                    }
                }

                return UniTask.FromResult(MapContentUnloadResult.Ok());
            }

            public void CompleteLoad(MapContentLoadResult result)
            {
                pending?.TrySetResult(result);
                pending = null;
            }

            private TestMapHandle CreateHandle(string mapId)
            {
                var root = new GameObject("Loaded-" + mapId);
                var mapContentRoot = new GameObject("MapContentRoot");
                mapContentRoot.transform.SetParent(root.transform, false);
                var runtimeRoot = new GameObject("DynamicObjectRoot");
                runtimeRoot.transform.SetParent(mapContentRoot.transform, false);
                mapContentRoot.SetActive(false);
                var context = root.AddComponent<MapContext>();
                SetField(context, "mapContentRoot", mapContentRoot);
                SetField(context, "dynamicObjectRoot", runtimeRoot.transform);
                var handle = new TestMapHandle(mapId, context);
                handles.Add(handle);
                return handle;
            }
        }

        private sealed class TestMapHandle : IMapContentHandle
        {
            public TestMapHandle(string mapId, MapContext context)
            {
                MapId = mapId;
                Context = context;
            }

            public string MapId { get; }
            public MapContext Context { get; }
            public GameObject Root => Context != null ? Context.gameObject : null;
            public bool Valid { get; set; } = true;
            public bool IsValid => Valid && Context != null;
        }
    }
}

