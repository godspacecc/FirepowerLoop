using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class CorpseSearchTests
    {
        private string tempRoot;
        private TestSaveClock saveClock;
        private FakePlayerRuntimePortProvider playerPorts;
        private TestMapContentHandle persistentLootBoxWorld;

        [SetUp]
        public void SetUp()
        {
            UIHelper.ResetForTests();
            UIRoot.ClearForTests();
            TestReset.ResetArchitecture();
            saveClock = new TestSaveClock();
            playerPorts = new FakePlayerRuntimePortProvider();
            ResourceLoader.SetLoaderForTests(new TestResourceLoader());
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(new SearchConfigProvider());
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(playerPorts);
            };
            _ = Game.Interface.GetSystem<SearchContainerSystem>();
            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayCorpseSearchTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot, saveClock);
        }

        [TearDown]
        public void TearDown()
        {
            CleanupSearchSessionForTests();
            persistentLootBoxWorld?.Destroy();
            persistentLootBoxWorld = null;
            UIHelper.ResetForTests();
            UIRoot.ClearForTests();
            ResourceLoader.ResetForTests();
            DestroyNamed("UIRoot");
            DestroyNamed("EventSystem");
            DestroyNamed("Main Camera");
            DestroyNamed("BlockingPrefab");
            Architecture<Game>.OnRegisterPatch = _ => { };
            TestReset.ResetArchitecture();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void CorpseContentsUseEnemyIdAndMinimumSixSlots()
        {
            SendContents(1000, new LootDropBatchEntry("loot.sword", 0, Vector3.zero));

            Assert.That(Game.Interface.GetSystem<SearchContainerSystem>().TryGetSnapshot(1000, out var snapshot), Is.True);
            Assert.That(snapshot.ContainerId, Is.EqualTo(1000));
            Assert.That(snapshot.MapInstanceId, Is.EqualTo(44));
            Assert.That(snapshot.Capacity, Is.EqualTo(6));
            Assert.That(snapshot.Slots.Count, Is.EqualTo(1));
            Assert.That(snapshot.Slots[0].ItemId, Is.EqualTo(ItemIds.ShortSword));
            Assert.That(snapshot.Slots[0].Amount, Is.EqualTo(1));
        }

        [TestCase(13, 7)]
        [TestCase(25, 13)]
        public void CapacityUsesActualAmountAndMaximumStack(int amount, int expectedCapacity)
        {
            SendContents(1001, new LootDropBatchEntry("loot.stack-two", amount, Vector3.zero));

            Assert.That(Game.Interface.GetSystem<SearchContainerSystem>().TryGetSnapshot(1001, out var snapshot), Is.True);
            Assert.That(snapshot.Capacity, Is.EqualTo(expectedCapacity));
            Assert.That(snapshot.Slots.Count, Is.EqualTo(expectedCapacity));
        }

        [Test]
        public void InvalidCandidateFallsBackWithoutHalfContainer()
        {
            var fallbacks = new List<LootDropBatchRequestedEvent>();
            Game.Interface.RegisterEvent<LootDropBatchRequestedEvent>(evt => fallbacks.Add(evt));

            SendContents(
                1002,
                new LootDropBatchEntry("loot.sword", 0, Vector3.zero),
                new LootDropBatchEntry("loot.missing", 0, Vector3.right));

            Assert.That(Game.Interface.GetSystem<SearchContainerSystem>().TryGetSnapshot(1002, out _), Is.False);
            Assert.That(fallbacks.Count, Is.EqualTo(1));
            Assert.That(fallbacks[0].Entries.Count, Is.EqualTo(2));
        }

        [Test]
        public void DuplicateRequestDoesNotDuplicateRewardsOrFallback()
        {
            var fallbacks = 0;
            Game.Interface.RegisterEvent<LootDropBatchRequestedEvent>(_ => fallbacks++);
            var entry = new LootDropBatchEntry("loot.sword", 0, Vector3.zero);

            SendContents(1003, entry);
            SendContents(1003, entry);

            Assert.That(Game.Interface.GetSystem<SearchContainerSystem>().TryGetSnapshot(1003, out var snapshot), Is.True);
            Assert.That(snapshot.Slots.Count, Is.EqualTo(1));
            Assert.That(fallbacks, Is.EqualTo(0));
        }

        [Test]
        public void SnapshotDoesNotExposeMutableContainerAndModelIsNotSaved()
        {
            SendContents(1004, new LootDropBatchEntry("loot.sword", 0, Vector3.zero));
            Assert.That(Game.Interface.GetSystem<SearchContainerSystem>().TryGetSnapshot(1004, out var snapshot), Is.True);

            Assert.That(snapshot.Slots, Is.Not.InstanceOf<ItemContainer>());
            Assert.That(snapshot.Slots, Is.Not.InstanceOf<List<SearchSlotSnapshot>>());
            Assert.That(Game.Interface.GetModel<SearchContainerModel>(), Is.Not.InstanceOf<ISaveSectionProvider>());
        }

        [Test]
        public void RuntimeSlotReferenceRequiresExplicitSearchIdentity()
        {
            var bag = new InventorySlotRef(InventoryContainerKind.PlayerBag, 2);
            var corpse = new InventorySlotRef(InventoryContainerKind.Search, 3, 1005);
            Assert.That(bag.RuntimeContainerId, Is.Zero);
            Assert.That(corpse.RuntimeContainerId, Is.EqualTo(1005));
        }

        [Test]
        public void ChildCorpseTriggerForwardsPlayerFocusAndStartsSearch()
        {
            ConfigureSearchWorld(1006);
            var enemyRoot = new GameObject("CorpseTriggerEnemy");
            var triggerObject = new GameObject("CorpseSearchTrigger");
            var playerObject = new GameObject("CorpseTriggerPlayer");
            try
            {
                triggerObject.transform.SetParent(enemyRoot.transform, false);
                var trigger = triggerObject.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.enabled = false;

                enemyRoot.AddComponent<BoxCollider>();
                enemyRoot.AddComponent<UnityEngine.AI.NavMeshAgent>();
                var view = enemyRoot.AddComponent<EnemyView>();
                typeof(EnemyView)
                    .GetField("corpseSearchTrigger", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(view, trigger);
                view.ConfigureRuntime(1006, 44);
                view.ConfigureRuntime(1006, 44);
                view.MarkCorpse();

                var playerCollider = playerObject.AddComponent<BoxCollider>();
                playerPorts.PlayerCollider = playerCollider;
                var relay = triggerObject.GetComponent<CorpseSearchTriggerRelay>();
                Assert.That(relay, Is.Not.Null);
                Assert.That(triggerObject.GetComponents<CorpseSearchTriggerRelay>().Length, Is.EqualTo(1));
                typeof(CorpseSearchTriggerRelay)
                    .GetMethod("OnTriggerEnter", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(relay, new object[] { playerCollider });
                typeof(CorpseSearchTriggerRelay)
                    .GetMethod("OnTriggerExit", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(relay, new object[] { playerCollider });
                Game.Interface.GetSystem<PlayerInteractionSystem>().RequestInteract();
                Assert.That(Game.Interface.GetSystem<SearchContainerSystem>().IsSearching, Is.False);

                typeof(CorpseSearchTriggerRelay)
                    .GetMethod("OnTriggerEnter", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(relay, new object[] { playerCollider });
                Game.Interface.GetSystem<PlayerInteractionSystem>().RequestInteract();

                Assert.That(Game.Interface.GetSystem<SearchContainerSystem>().IsSearching, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(playerObject);
                UnityEngine.Object.DestroyImmediate(enemyRoot);
            }
        }

        [UnityTest]
        public IEnumerator SearchCompletesOnceAndKeepsEmptyOpenSessionUntilClosed()
        {
            CreateUIRoot();
            ConfigureInventoryPrefabLoader();
            ConfigurePersistentLootBoxWorld(1100);

            var search = Game.Interface.GetSystem<SearchContainerSystem>();
            Assert.That(search.TryBeginSearch(1100, out var error), Is.True, error);
            yield return null;
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(SearchContainerSystem.SearchDurationSeconds);

            var ctrl = UIHelper.GetUICtrl<SearchContainerUICtrl>();
            Assert.That(ctrl, Is.Not.Null);
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(ctrl.CurrentContainerId, Is.EqualTo(1100));
            Assert.That(UIHelper.GetUICtrl<BagUICtrl>(), Is.Null);
            Assert.That(search.OpenContainerId, Is.EqualTo(1100));
            Assert.That(search.TryBeginSearch(1100, out _), Is.False);

            var inventory = Game.Interface.GetSystem<InventorySystem>();
            Assert.That(inventory.TryTransfer(
                new InventorySlotRef(InventoryContainerKind.Search, 0, 1100),
                new InventorySlotRef(InventoryContainerKind.PlayerBag, 0),
                out error), Is.True, error);
            Assert.That(search.TryGetSnapshot(1100, out var emptySnapshot), Is.True);
            Assert.That(emptySnapshot.Slots.Count, Is.EqualTo(0));
            Assert.That(search.IsCurrentSession(1100), Is.True);

            var hotKeys = Game.Interface.GetSystem<HotKeySystem>();
            Assert.That(hotKeys.ExecuteBinding(HotKeyDefaults.Tab), Is.True);
            var bag = UIHelper.GetUICtrl<BagUICtrl>();
            Assert.That(bag, Is.Not.Null);
            Assert.That(bag.IsShow(), Is.True);
            Assert.That(hotKeys.ExecuteBinding(HotKeyDefaults.Tab), Is.True);
            Assert.That(UIHelper.GetUICtrl<BagUICtrl>(), Is.SameAs(bag));
            Assert.That(bag.IsShow(), Is.False);
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(search.IsCurrentSession(1100), Is.True);

            ctrl.Hide();
            Assert.That(search.IsCurrentSession(1100), Is.False);
            Assert.That(search.CanSearch(1100), Is.False);
        }

        [UnityTest]
        public IEnumerator SearchCancelsForInterruptsAndBlockingUiBeforeCompletion()
        {
            CreateUIRoot();
            ConfigureInventoryPrefabLoader();
            ConfigureSearchWorld(1200);
            var search = Game.Interface.GetSystem<SearchContainerSystem>();

            Assert.That(search.TryBeginSearch(1200, out var error), Is.True, error);
            MovePlayer(Vector3.right * (SearchContainerSystem.SearchDistance + 0.25f));
            LifeCycle.Update();
            Assert.That(search.IsSearching, Is.False);
            Assert.That(search.OpenContainerId, Is.Zero);

            MovePlayer(Vector3.zero);
            Assert.That(search.TryBeginSearch(1200, out error), Is.True, error);
            Game.Interface.SendEvent(new CombatDamageAppliedEvent(99, PlayerModel.LocalPlayerUnitId, 1));
            Assert.That(search.IsSearching, Is.False);

            Assert.That(search.TryBeginSearch(1200, out error), Is.True, error);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            LifeCycle.Update();
            Assert.That(search.IsSearching, Is.False);

            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            Assert.That(search.TryBeginSearch(1200, out error), Is.True, error);
            Game.Interface.GetSystem<UnitSystem>().Remove(1200, UnitRemovedReason.MapInstanceReleased);
            Assert.That(search.IsSearching, Is.False);

            ConfigureSearchWorld(1201);
            Assert.That(search.TryBeginSearch(1201, out error), Is.True, error);
            Assert.That(UIHelper.ShowUI<BlockingTestCtrl>().IsShow(), Is.True);
            Assert.That(UIHelper.HasUIOpen, Is.True);
            yield return AdvanceSearchUntilSettled(search);
            Assert.That(search.IsSearching, Is.False);
            Assert.That(search.OpenContainerId, Is.Zero);
            Assert.That(UIHelper.GetUICtrl<SearchContainerUICtrl>(), Is.Null);

            UIHelper.HideUI<BlockingTestCtrl>();
            Assert.That(search.TryBeginSearch(1201, out error), Is.True, error);
            search.CancelSearch();
        }

        [Test]
        public void PlayerDeathCancelsPendingSearchWithoutOpeningInventory()
        {
            ConfigureSearchWorld(1212);
            RegisterUnit(9012, UnitType.Enemy, "attacker", Vector3.right);
            Game.Interface.GetModel<CombatModel>().RegisterOrResetCombatant(9012, CombatFaction.Enemy, 10, 120, PlayerModel.LocalPlayerUnitId);
            var search = Game.Interface.GetSystem<SearchContainerSystem>();
            Assert.That(search.TryBeginSearch(1212, out var error), Is.True, error);

            LogAssert.Expect(LogType.Error, "[CombatFlowSystem] 失败回基地未完成：撤离任务分区捕获失败");
            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryDamage(9012, PlayerModel.LocalPlayerUnitId, 120), Is.True);

            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.Zero);
            Assert.That(search.IsSearching, Is.False);
            Assert.That(search.OpenContainerId, Is.Zero);
            Assert.That(search.TryGetPendingProgress(1212, out _), Is.False);
            Assert.That(UIHelper.GetUICtrl<SearchContainerUICtrl>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator SearchTransfersSupportBagAndSameCorpseButRollbackInvalidCombinations()
        {
            CreateUIRoot();
            ConfigureInventoryPrefabLoader();
            ConfigureSearchWorld(1300);
            var search = Game.Interface.GetSystem<SearchContainerSystem>();
            Assert.That(search.TryBeginSearch(1300, out var error), Is.True, error);
            yield return AdvanceSearchUntilSettled(search);
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var events = 0;
            Game.Interface.RegisterEvent<SearchContainerChangedEvent>(evt =>
            {
                if (evt.ContainerId == 1300)
                {
                    events++;
                }
            });

            Assert.That(search.OpenContainerId, Is.EqualTo(1300));
            Assert.That(inventory.TryTransfer(
                new InventorySlotRef(InventoryContainerKind.Search, 0, 1300),
                new InventorySlotRef(InventoryContainerKind.PlayerBag, 0),
                out error), Is.True, error);
            Assert.That(bag.TryGet(0, out var moved), Is.True);
            Assert.That(moved.ItemId, Is.EqualTo(ItemIds.ShortSword));

            Assert.That(bag.TryAdd(ItemIds.MetalParts, 3, 2, out _, out error), Is.True, error);
            Assert.That(inventory.TryTransfer(
                new InventorySlotRef(InventoryContainerKind.PlayerBag, 1),
                new InventorySlotRef(InventoryContainerKind.Search, 1, 1300),
                out error), Is.True, error);
            Assert.That(inventory.TryTransfer(
                new InventorySlotRef(InventoryContainerKind.Search, 1, 1300),
                new InventorySlotRef(InventoryContainerKind.Search, 2, 1300),
                out error), Is.True, error);
            Assert.That(search.TryGetSnapshot(1300, out var snapshot), Is.True);
            Assert.That(snapshot.Slots.Any(slot => slot.SlotIndex == 2 && slot.ItemId == ItemIds.MetalParts), Is.True);

            var before = SnapshotText(snapshot);
            Assert.That(inventory.TryTransfer(
                new InventorySlotRef(InventoryContainerKind.Search, 2, 1300),
                new InventorySlotRef(InventoryContainerKind.Warehouse, 0),
                out _), Is.False);
            Assert.That(search.TryGetSnapshot(1300, out snapshot), Is.True);
            Assert.That(SnapshotText(snapshot), Is.EqualTo(before));
            Assert.That(events, Is.EqualTo(3));
            Assert.That(Game.Interface.GetModel<SearchContainerModel>(), Is.Not.InstanceOf<ISaveSectionProvider>());
        }

        [UnityTest]
        public IEnumerator SearchTransactionsPublishExactEventsAndDirtyMarks()
        {
            CreateUIRoot();
            ConfigureInventoryPrefabLoader();
            CreateCurrentSave("SearchTxn");
            ConfigureSearchWorld(1310, new LootDropBatchEntry("loot.stack-two", 0, Vector3.zero));
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Paused;
            Assert.That(Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing), Is.True);
            var search = Game.Interface.GetSystem<SearchContainerSystem>();
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(search.OpenContainerId, Is.Zero);
            Assert.That(search.TryBeginSearch(1310, out var error), Is.True, error);
            yield return AdvanceSearchUntilSettled(search);
            Assert.That(search.OpenContainerId, Is.EqualTo(1310),
                $"searching={search.IsSearching}; ui={UIHelper.GetUICtrl<SearchContainerUICtrl>()?.IsShow() ?? false}; " +
                $"map={Game.Interface.GetModel<MapSceneModel>().CurrentMapId}; risk={Game.Interface.GetModel<PlayerBagModel>().RiskState}");
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 1, 2, out _, out error), Is.True, error);
            Assert.That(inventory.TryTransfer(
                new InventorySlotRef(InventoryContainerKind.PlayerBag, 0),
                new InventorySlotRef(InventoryContainerKind.Search, 1, 1310),
                out error), Is.True, error);

            var counts = new InventoryEventCounts();
            var collectionSounds = 0;
            Game.Interface.RegisterEvent<SearchItemCollectedEvent>(_ => collectionSounds++);
            var dirty = new DirtyObserver();
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => counts.Inventory++);
            Game.Interface.RegisterEvent<SearchContainerChangedEvent>(evt =>
            {
                if (evt.ContainerId == 1310 || evt.ContainerId == 1311)
                {
                    counts.Search++;
                }
            });
            dirty.Reset();
            counts.Reset();

            saveClock.Advance(1f);
            Assert.That(inventory.TryTransfer(
                new InventorySlotRef(InventoryContainerKind.Search, 1, 1310),
                new InventorySlotRef(InventoryContainerKind.Search, 0, 1310),
                out error), Is.True, error);
            Assert.That(search.TryGetSnapshot(1310, out var snapshot), Is.True);
            Assert.That(SnapshotText(snapshot), Is.EqualTo($"0:{ItemIds.MetalParts}:2"));
            Assert.That(counts.Inventory, Is.Zero);
            Assert.That(counts.Search, Is.EqualTo(1));
            Assert.That(dirty.ConsumeChange(), Is.EqualTo(0));
            Assert.That(collectionSounds, Is.Zero, "同容器整理不代表收取");

            UIHelper.GetUICtrl<SearchContainerUICtrl>()?.Hide();
            search.CloseOpenSession(1310);
            Assert.That(UIHelper.HasUIOpen, Is.False);
            Assert.That(search.OpenContainerId, Is.Zero);
            RegisterUnit(1311, UnitType.Enemy, "enemy", Vector3.zero);
            SendContents(1311, new LootDropBatchEntry("loot.sword", 0, Vector3.zero));
            Assert.That(search.TryBeginSearch(1311, out error), Is.True, error);
            yield return AdvanceSearchUntilSettled(search);
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 1, 2, out _, out error), Is.True, error);
            Assert.That(inventory.TryTransfer(
                new InventorySlotRef(InventoryContainerKind.PlayerBag, 0),
                new InventorySlotRef(InventoryContainerKind.Search, 1, 1311),
                out error), Is.True, error);
            counts.Reset();
            dirty.Reset();

            saveClock.Advance(1f);
            Assert.That(inventory.TryTransfer(
                new InventorySlotRef(InventoryContainerKind.Search, 0, 1311),
                new InventorySlotRef(InventoryContainerKind.Search, 1, 1311),
                out error), Is.True, error);
            Assert.That(search.TryGetSnapshot(1311, out snapshot), Is.True);
            Assert.That(SnapshotText(snapshot), Is.EqualTo($"0:{ItemIds.MetalParts}:1|1:{ItemIds.ShortSword}:1"));
            Assert.That(counts.Inventory, Is.Zero);
            Assert.That(counts.Search, Is.EqualTo(1));
            Assert.That(dirty.ConsumeChange(), Is.EqualTo(0));

            bag.TryAdd(9999, 1, 1, out _, out _);
            var bagBefore = SnapshotText(bag);
            var searchBefore = SnapshotText(snapshot);
            counts.Reset();
            dirty.Reset();

            saveClock.Advance(1f);
            Assert.That(inventory.TryTransfer(
                new InventorySlotRef(InventoryContainerKind.Search, 1, 1311),
                new InventorySlotRef(InventoryContainerKind.PlayerBag, 0),
                out _), Is.False);
            Assert.That(search.TryGetSnapshot(1311, out snapshot), Is.True);
            Assert.That(SnapshotText(snapshot), Is.EqualTo(searchBefore));
            Assert.That(SnapshotText(bag), Is.EqualTo(bagBefore));
            Assert.That(collectionSounds, Is.Zero, "失败回滚不能发布成功收取");
            Assert.That(counts.Inventory, Is.Zero);
            Assert.That(counts.Search, Is.Zero);
            Assert.That(dirty.ConsumeChange(), Is.EqualTo(0));

            bag.TryRemove(0, 1, out _, out _);
            counts.Reset();
            dirty.Reset();
            saveClock.Advance(1f);
            Assert.That(inventory.TryTransfer(
                new InventorySlotRef(InventoryContainerKind.Search, 1, 1311),
                new InventorySlotRef(InventoryContainerKind.PlayerBag, 0),
                out error), Is.True, error);
            Assert.That(counts.Inventory, Is.EqualTo(1));
            Assert.That(counts.Search, Is.EqualTo(1));
            Assert.That(dirty.ConsumeChange(), Is.EqualTo(1));
            Assert.That(collectionSounds, Is.EqualTo(1), "成功收取只发布一次");

            counts.Reset();
            dirty.Reset();
            saveClock.Advance(1f);
            Assert.That(inventory.TryTransfer(
                new InventorySlotRef(InventoryContainerKind.PlayerBag, 0),
                new InventorySlotRef(InventoryContainerKind.Search, 2, 1311),
                out error), Is.True, error);
            Assert.That(counts.Inventory, Is.EqualTo(1));
            Assert.That(counts.Search, Is.EqualTo(1));
            Assert.That(dirty.ConsumeChange(), Is.EqualTo(1));
        }

        [Test]
        public void EnemyPrefabsSerializeIndependentCorpseSearchPresentation()
        {
            AssertEnemyPrefab("Assets/Game/Prefabs/Characters/GrayboxEnemy.prefab");
            AssertEnemyPrefab("Assets/Game/Prefabs/Characters/GrayboxArcher.prefab");
        }

        [UnityTest]
        public IEnumerator EnemyPrefabsSwitchToPersistentLootBoxesOnDeath()
        {
            CreateUIRoot();
            ConfigureInventoryPrefabLoader();
            yield return AssertPersistentLootBoxLifecycle(
                "Assets/Game/Prefabs/Characters/GrayboxEnemy.prefab", 1400, false);
            yield return AssertPersistentLootBoxLifecycle(
                "Assets/Game/Prefabs/Characters/GrayboxArcher.prefab", 1401, true);
        }

        private static void AssertEnemyPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.NotNull(prefab, path);
            Assert.That(prefab.GetComponentsInChildren<EnemyView>(true).Length, Is.EqualTo(1));
            var view = prefab.GetComponent<EnemyView>();
            var serialized = new SerializedObject(view);
            var trigger = serialized.FindProperty("corpseSearchTrigger").objectReferenceValue as SphereCollider;
            var prompt = serialized.FindProperty("corpsePromptRoot").objectReferenceValue as GameObject;
            var fill = serialized.FindProperty("corpseProgressFill").objectReferenceValue as UnityEngine.UI.Image;
            Assert.NotNull(trigger);
            Assert.That(trigger.isTrigger, Is.True);
            Assert.That(trigger.radius, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(trigger.enabled, Is.False);
            Assert.NotNull(prompt);
            Assert.That(prompt.activeSelf, Is.False);
            Assert.NotNull(fill);
            Assert.That(fill.fillAmount, Is.Zero.Within(0.001f));

            var livingRenderers = serialized.FindProperty("livingRenderers");
            var lootBox = serialized.FindProperty("corpseLootBoxObject").objectReferenceValue as GameObject;
            Assert.NotNull(livingRenderers, path + " must serialize living renderers.");
            Assert.That(livingRenderers.arraySize, Is.EqualTo(path.Contains("Archer") ? 2 : 1));
            Assert.NotNull(lootBox);
            Assert.That(
                prefab.GetComponentsInChildren<Transform>(true).Count(item => item.name == "CorpseLootBox"),
                Is.EqualTo(1));
            Assert.That(lootBox.name, Is.EqualTo("CorpseLootBox"));
            Assert.That(lootBox.activeSelf, Is.False);
            Assert.That(lootBox.transform.localPosition, Is.EqualTo(new Vector3(0f, 0.1125f, 0f)));
            Assert.That(lootBox.transform.localScale, Is.EqualTo(new Vector3(0.9f, 0.225f, 0.65f)));
            Assert.That(lootBox.GetComponent<Collider>(), Is.Null);
            Assert.That(lootBox.GetComponent<Rigidbody>(), Is.Null);
            Assert.That(lootBox.GetComponent<UnityEngine.AI.NavMeshObstacle>(), Is.Null);
            Assert.That(lootBox.GetComponents<MonoBehaviour>(), Is.Empty);

            var actualRenderers = Enumerable.Range(0, livingRenderers.arraySize)
                .Select(index => livingRenderers.GetArrayElementAtIndex(index).objectReferenceValue as Renderer)
                .ToArray();
            Assert.That(actualRenderers, Is.All.Not.Null);
            var bodyRenderer = prefab.GetComponent<Renderer>();
            Assert.NotNull(bodyRenderer);
            var expectedRenderers = path.Contains("Archer")
                ? new[] { bodyRenderer, prefab.transform.Find("ArcherBow").GetComponent<Renderer>() }
                : new[] { bodyRenderer };
            Assert.That(expectedRenderers, Is.All.Not.Null);
            Assert.That(actualRenderers, Is.EquivalentTo(expectedRenderers));
            Assert.That(actualRenderers.Contains(lootBox.GetComponent<Renderer>()), Is.False);
            Assert.That(actualRenderers.Any(renderer => renderer.transform.IsChildOf(prompt.transform)), Is.False);
        }

        private IEnumerator AssertPersistentLootBoxLifecycle(string path, int enemyId, bool expectsBow)
        {
            ConfigurePersistentLootBoxWorld(enemyId);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance = UnityEngine.Object.Instantiate(prefab);
            var expectedSearchActionId = 0L;
            var searchEndedReceived = false;
            var searchEnded = default(PlayerActionEndedEvent);
            var searchEndedUnregister = Game.Interface.RegisterEvent<PlayerActionEndedEvent>(evt =>
            {
                if (expectedSearchActionId <= 0 || evt.ActionId != expectedSearchActionId || evt.Kind != PlayerActionKind.Search)
                {
                    return;
                }

                searchEndedReceived = true;
                searchEnded = evt;
            });
            try
            {
                var view = instance.GetComponent<EnemyView>();
                var serialized = new SerializedObject(view);
                var lootBox = serialized.FindProperty("corpseLootBoxObject").objectReferenceValue as GameObject;
                var combatCollider = serialized.FindProperty("combatCollider").objectReferenceValue as Collider;
                var trigger = serialized.FindProperty("corpseSearchTrigger").objectReferenceValue as SphereCollider;
                var prompt = serialized.FindProperty("corpsePromptRoot").objectReferenceValue as GameObject;
                view.ConfigureRuntime(enemyId, 44);
                Assert.NotNull(lootBox);
                Assert.NotNull(combatCollider);
                Assert.NotNull(trigger);
                Assert.NotNull(prompt);
                AssertCorpsePresentation(instance, view, lootBox, combatCollider, trigger, expectsBow);

                var search = Game.Interface.GetSystem<SearchContainerSystem>();
                Assert.That(search.TryBeginSearch(enemyId, out var error), Is.True, error);
                Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out var startedAction), Is.True,
                    "持久尸体搜索开始后必须存在玩家动作快照。");
                expectedSearchActionId = startedAction.ActionId;
                yield return AdvanceSearchUntilSettled(search);
                var searchUi = UIHelper.GetUICtrl<SearchContainerUICtrl>();
                Assert.NotNull(searchUi, BuildPersistentSearchFailureDiagnostic(
                    enemyId,
                    expectedSearchActionId,
                    searchEndedReceived,
                    searchEnded));
                Assert.That(searchUi.IsShow(), Is.True);
                var inventory = Game.Interface.GetSystem<InventorySystem>();
                while (search.TryGetSnapshot(enemyId, out var snapshot) && snapshot.Slots.Count > 0)
                {
                    var slot = snapshot.Slots[0];
                    Assert.That(inventory.TryTransfer(
                        new InventorySlotRef(InventoryContainerKind.Search, slot.SlotIndex, enemyId),
                        new InventorySlotRef(InventoryContainerKind.PlayerBag, 0),
                        out error), Is.True, error);
                }

                searchUi.Hide();
                Assert.That(search.TryGetSnapshot(enemyId, out var emptySnapshot), Is.True);
                Assert.That(emptySnapshot.Slots, Is.Empty);
                Assert.That(search.CanSearch(enemyId), Is.False);
                Assert.That(search.IsCurrentSession(enemyId), Is.False);
                Assert.That(lootBox.activeSelf, Is.True);
                Assert.That(prompt.activeSelf, Is.False);

                instance.SetActive(false);
                instance.SetActive(true);
                view.MarkDead();
                AssertCorpsePresentation(instance, view, lootBox, combatCollider, trigger, expectsBow);
                Assert.That(search.TryGetSnapshot(enemyId, out emptySnapshot), Is.True);
                Assert.That(emptySnapshot.Slots, Is.Empty);
                Assert.That(search.CanSearch(enemyId), Is.False);
                Assert.That(prompt.activeSelf, Is.False);
            }
            finally
            {
                searchEndedUnregister.UnRegister();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static string BuildPersistentSearchFailureDiagnostic(
            int enemyId,
            long expectedSearchActionId,
            bool searchEndedReceived,
            PlayerActionEndedEvent searchEnded)
        {
            var architecture = Game.Interface;
            var search = architecture.GetSystem<SearchContainerSystem>();
            var actions = architecture.GetSystem<PlayerActionSystem>();
            var hasActiveAction = actions.TryGetSnapshot(out var activeAction);
            var appState = architecture.GetModel<AppStateModel>().CurrentState.Value;
            var map = architecture.GetModel<MapSceneModel>();
            var handleExists = map.CurrentHandle != null;
            var handleIsValid = handleExists && map.CurrentHandle.IsValid;
            var expedition = architecture.GetModel<ExpeditionModel>();
            var bagRisk = architecture.GetModel<PlayerBagModel>().RiskState;
            var playerHp = architecture.GetModel<CombatModel>().PlayerHP.Value;
            var units = architecture.GetSystem<UnitSystem>();
            var playerExists = units.TryGet(PlayerModel.LocalPlayerUnitId, out var playerUnit);
            var enemyExists = units.TryGet(enemyId, out var enemyUnit);
            var corpseExists = search.TryGetSnapshot(enemyId, out var corpseSnapshot);
            var corpseHasContents = corpseExists && corpseSnapshot.Slots.Count > 0;
            var canSearch = search.CanSearch(enemyId);
            var hasUiOpen = UIHelper.HasUIOpen;
            var topBlockingUi = UIHelper.GetTopmostBlockingUI();
            var canOpenBag = InventoryOpenRules.CanOpenPlayerBag(out var bagOpenError);
            var searchUi = UIHelper.GetUICtrl<SearchContainerUICtrl>();
            var searchUiState = searchUi == null ? "未创建" : searchUi.IsShow() ? "已显示" : "已创建未显示";

            return string.Join("; ", new[]
            {
                "持久尸体第一次搜索后未取得 SearchContainerUICtrl",
                $"search.IsSearching={search.IsSearching}",
                $"search.OpenContainerId={search.OpenContainerId}",
                $"activeAction.exists={hasActiveAction}",
                $"activeAction.ActionId={(hasActiveAction ? activeAction.ActionId : 0)}",
                $"activeAction.Kind={(hasActiveAction ? activeAction.Kind.ToString() : "无")}",
                $"activeAction.ElapsedSeconds={(hasActiveAction ? activeAction.ElapsedSeconds : 0f)}",
                $"activeAction.DurationSeconds={(hasActiveAction ? activeAction.DurationSeconds : 0f)}",
                $"searchEnded.received={searchEndedReceived}",
                $"searchEnded.expectedActionId={expectedSearchActionId}",
                $"searchEnded.ActionId={(searchEndedReceived ? searchEnded.ActionId : 0)}",
                $"searchEnded.Kind={(searchEndedReceived ? searchEnded.Kind.ToString() : "无")}",
                $"searchEnded.Completed={(searchEndedReceived && searchEnded.Completed)}",
                $"searchEnded.CancelReason={(searchEndedReceived ? searchEnded.CancelReason.ToString() : "无")}",
                $"appState={appState}",
                $"timeScale={Time.timeScale}",
                $"map.CurrentMapId={map.CurrentMapId ?? "<null>"}",
                $"map.CurrentMode={map.CurrentMode}",
                $"map.HandleExists={handleExists}",
                $"map.HandleIsValid={handleIsValid}",
                $"expedition.Status={expedition.Status}",
                $"expedition.CurrentMapId={expedition.CurrentMapId ?? "<null>"}",
                $"bag.RiskState={bagRisk}",
                $"player.HP={playerHp}",
                $"playerUnit.exists={playerExists}",
                $"playerUnit.MapInstanceId={(playerExists ? playerUnit.MapInstanceId : 0)}",
                $"playerUnit.Position={(playerExists ? playerUnit.Position.ToString("F3") : "<none>")}",
                $"enemyUnit.exists={enemyExists}",
                $"enemyUnit.MapInstanceId={(enemyExists ? enemyUnit.MapInstanceId : 0)}",
                $"enemyUnit.Position={(enemyExists ? enemyUnit.Position.ToString("F3") : "<none>")}",
                $"corpseSnapshot.exists={corpseExists}",
                $"corpseSnapshot.hasContents={corpseHasContents}",
                $"search.CanSearch={canSearch}",
                $"UIHelper.HasUIOpen={hasUiOpen}",
                $"topBlockingUi={(topBlockingUi == null ? "<none>" : topBlockingUi.GetType().FullName)}",
                $"InventoryOpenRules.CanOpenPlayerBag={canOpenBag}",
                $"InventoryOpenRules.error={bagOpenError ?? "<null>"}",
                $"SearchContainerUICtrl.state={searchUiState}"
            });
        }

        private static void AssertCorpsePresentation(
            GameObject instance,
            EnemyView view,
            GameObject lootBox,
            Collider combatCollider,
            SphereCollider trigger,
            bool expectsBow)
        {
            var bodyRenderer = instance.GetComponent<Renderer>();
            var bowRenderer = instance.transform.Find("ArcherBow")?.GetComponent<Renderer>();
            Assert.NotNull(bodyRenderer);
            Assert.That(bowRenderer != null, Is.EqualTo(expectsBow));

            view.MarkCorpse();
            Assert.That(bodyRenderer.enabled, Is.False);
            if (bowRenderer != null) Assert.That(bowRenderer.enabled, Is.False);
            Assert.That(lootBox.activeSelf, Is.True);
            Assert.That(combatCollider.enabled, Is.False);
            Assert.That(trigger.enabled, Is.True);

            view.MarkCorpse();
            Assert.That(bodyRenderer.enabled, Is.False);
            if (bowRenderer != null) Assert.That(bowRenderer.enabled, Is.False);
            Assert.That(lootBox.activeSelf, Is.True);
        }

        private static void SendContents(int enemyId, params LootDropBatchEntry[] entries)
        {
            Game.Interface.SendEvent(new EnemyCorpseContentsRequestedEvent(
                44,
                enemyId,
                Vector3.zero,
                Quaternion.identity,
                entries));
        }

        private void ConfigureSearchWorld(int enemyId, params LootDropBatchEntry[] entries)
        {
            if (Game.Interface.GetModel<SaveModel>().CurrentDomain == OfflineSaveDomain.None)
            {
                Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            }
            Assert.That(Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing), Is.True,
                "搜索测试必须通过应用状态系统进入游戏中状态。");
            if (Game.Interface.GetModel<MapSceneModel>().CurrentHandle == null)
            {
                persistentLootBoxWorld?.Destroy();
                persistentLootBoxWorld = new TestMapContentHandle(MapIds.SurvivalSample);
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, persistentLootBoxWorld);
            }
            if (!Game.Interface.GetModel<ExpeditionModel>().IsTaskManaged)
                Game.Interface.GetModel<ExpeditionModel>().BeginSurvival(MapIds.SurvivalSample, 60f);
            Assert.That(Game.Interface.GetSystem<InventorySystem>()
                .TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out var error), Is.True, error);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 1f, 1f, 5f),
                false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            RegisterUnit(PlayerModel.LocalPlayerUnitId, UnitType.Player, "player", Vector3.zero);
            RegisterUnit(enemyId, UnitType.Enemy, "enemy", Vector3.zero);
            SendContents(enemyId, entries != null && entries.Length > 0
                ? entries
                : new[] { new LootDropBatchEntry("loot.sword", 0, Vector3.zero) });
            Assert.That(InventoryOpenRules.CanOpenPlayerBag(out var openError), Is.True, openError);
            Assert.NotNull(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/SearchContainer.prefab"));
        }

        private void ConfigurePersistentLootBoxWorld(int enemyId)
        {
            if (Game.Interface.GetModel<SaveModel>().CurrentDomain == OfflineSaveDomain.None)
            {
                Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            }

            persistentLootBoxWorld?.Destroy();
            persistentLootBoxWorld = new TestMapContentHandle(MapIds.SurvivalSample);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, persistentLootBoxWorld);
            Assert.That(Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing), Is.True,
                "持久尸体测试必须通过应用状态系统进入游戏中状态。");
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 1f, 1f, 5f),
                false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            RegisterUnit(PlayerModel.LocalPlayerUnitId, UnitType.Player, "player", Vector3.zero);
            RegisterUnit(enemyId, UnitType.Enemy, "enemy", Vector3.zero);
            Game.Interface.GetModel<ExpeditionModel>().BeginSurvival(MapIds.SurvivalSample, 60f);
            Assert.That(Game.Interface.GetSystem<InventorySystem>()
                .TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<MapSceneSystem>().HasRecoverableCurrentWorld(), Is.True,
                "完整空盒链路开始搜索前必须建立可恢复的活动世界。");
            SendContents(enemyId, new LootDropBatchEntry("loot.sword", 0, Vector3.zero));
            Assert.NotNull(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/SearchContainer.prefab"));
        }

        private static void RegisterUnit(int unitId, UnitType type, string configId, Vector3 position)
        {
            Game.Interface.GetSystem<UnitSystem>().Remove(unitId, UnitRemovedReason.Explicit);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                unitId,
                44,
                type,
                type == UnitType.Player ? UnitSource.Player : UnitSource.MapPlaced,
                configId,
                position,
                Quaternion.identity,
                UnitAuthorityMode.Local,
                0,
                true), out var error), Is.True, error);
        }

        private static void MovePlayer(Vector3 position)
        {
            Assert.That(Game.Interface.GetSystem<UnitSystem>()
                .TryUpdateTransform(PlayerModel.LocalPlayerUnitId, position, Quaternion.identity), Is.True);
        }

        private static IEnumerator AdvanceSearchUntilSettled(SearchContainerSystem search)
        {
            var deadline = Time.realtimeSinceStartup + SearchContainerSystem.SearchDurationSeconds + 2f;
            while (search.IsSearching && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                LifeCycle.Update();
            }

            LifeCycle.Update();
        }

        private static void CreateUIRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(UIRoot));
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1920f, 1080f);
            foreach (var layerName in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var layer = new GameObject(layerName, typeof(RectTransform));
                layer.transform.SetParent(root.transform, false);
                var rect = layer.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            new GameObject("EventSystem", typeof(EventSystem), inputModuleType).transform.SetParent(root.transform, false);
        }

        private static void CleanupSearchSessionForTests()
        {
            var search = Game.Interface.GetSystem<SearchContainerSystem>();
            search.CancelSearch();
            search.CloseOpenSession(0);
        }

        private static void ConfigureInventoryPrefabLoader()
        {
            var blockingPrefab = new GameObject("BlockingPrefab", typeof(RectTransform), typeof(BaseUI));
            var uiRoot = GameObject.Find("UIRoot");
            if (uiRoot != null)
            {
                blockingPrefab.transform.SetParent(uiRoot.transform, false);
            }

            UIHelper.SetPrefabLoaderForTests(path => path switch
            {
                "Prefabs/UI/Inventory/Bag" => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Bag.prefab"),
                "Prefabs/UI/Inventory/Warehouse" => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Warehouse.prefab"),
                "Prefabs/UI/Inventory/SearchContainer" => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/SearchContainer.prefab"),
                _ => blockingPrefab
            });
        }

        private static string SnapshotText(SearchContainerSnapshot snapshot)
        {
            return string.Join("|", snapshot.Slots
                .OrderBy(slot => slot.SlotIndex)
                .Select(slot => $"{slot.SlotIndex}:{slot.ItemId}:{slot.Amount}"));
        }

        private static string SnapshotText(ItemContainer container)
        {
            return string.Join("|", container.Slots
                .OrderBy(pair => pair.Key)
                .Select(pair => $"{pair.Key}:{pair.Value.ItemId}:{pair.Value.Amount}"));
        }

        private void CreateCurrentSave(string name)
        {
            var save = Game.Interface.GetSystem<SaveSystem>();
            var expeditionId = Guid.NewGuid().ToString("N");
            var definition = new ExpeditionTaskDefinition(
                "expedition.survival-sample.survive",
                MapIds.SurvivalSample,
                "存活一天",
                "task.expedition.survival_sample.survive.name",
                TaskObjectiveType.Survive,
                60f,
                100);
            Assert.That(Game.Interface.GetSystem<ExpeditionSystem>().TryBeginConfiguredExpedition(
                MapIds.SurvivalSample, expeditionId, new[] { definition }, out var error), Is.True, error);
            var created = Game.Interface.GetModel<ExpeditionModel>().CaptureSave(expeditionId, DateTime.UtcNow);
            Assert.That(save.CaptureExpeditionSections(created, out error), Is.True, error);
            Assert.That(save.CommitExpeditionRuntime(created, out error), Is.True, error);
            persistentLootBoxWorld?.Destroy();
            persistentLootBoxWorld = new TestMapContentHandle(MapIds.SurvivalSample);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, persistentLootBoxWorld);
        }

        private static void DestroyNamed(string name)
        {
            var obj = GameObject.Find(name);
            if (obj != null)
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        private sealed class BlockingTestCtrl : BaseUICtrl
        {
            protected override string Path() { return "Blocking"; }
            protected override void OnInit() { }
            protected override void OnShow() { }
            protected override void OnHide() { }
            public override UILayer Layer() { return UILayer.Dialog; }
        }

        private sealed class InventoryEventCounts
        {
            public int Inventory;
            public int Search;

            public void Reset()
            {
                Inventory = 0;
                Search = 0;
            }
        }

        private sealed class DirtyObserver
        {
            private bool dirty;
            private float dirtyAt;

            public void Reset()
            {
                dirty = GetSaveDirty();
                dirtyAt = GetSaveDirtyAt();
            }

            public int ConsumeChange()
            {
                var nextDirty = GetSaveDirty();
                var nextDirtyAt = GetSaveDirtyAt();
                var changed = nextDirty && (!dirty || Math.Abs(nextDirtyAt - dirtyAt) > 0.001f) ? 1 : 0;
                dirty = nextDirty;
                dirtyAt = nextDirtyAt;
                return changed;
            }

            private static bool GetSaveDirty()
            {
                return (bool)typeof(SaveSystem)
                    .GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(Game.Interface.GetSystem<SaveSystem>());
            }

            private static float GetSaveDirtyAt()
            {
                return (float)typeof(SaveSystem)
                    .GetField("dirtyAt", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(Game.Interface.GetSystem<SaveSystem>());
            }
        }

        private sealed class TestSaveClock : ISaveClock
        {
            public DateTime UtcNow { get; private set; } = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);
            public float RealtimeSinceStartup { get; private set; }

            public void Advance(float seconds)
            {
                RealtimeSinceStartup += seconds;
                UtcNow = UtcNow.AddSeconds(seconds);
            }
        }

        private sealed class FakePlayerRuntimePortProvider : IPlayerRuntimePortProvider
        {
            private readonly FakeCharacterMotorPort port = new FakeCharacterMotorPort();

            public Collider PlayerCollider { get; set; }

            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort motorPort)
            {
                motorPort = unitId == PlayerModel.LocalPlayerUnitId && mapInstanceId == 44 ? port : null;
                return motorPort != null;
            }

            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port)
            {
                port = null;
                return false;
            }

            public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider)
            {
                return unitId == PlayerModel.LocalPlayerUnitId &&
                       mapInstanceId == 44 &&
                       ReferenceEquals(PlayerCollider, collider);
            }

            public bool TryGetCurrentPlayerMapLocalPose(out PlayerMapLocalPoseSnapshot snapshot, out string error)
            {
                snapshot = new PlayerMapLocalPoseSnapshot(
                    PlayerModel.LocalPlayerUnitId,
                    44,
                    Vector3.zero,
                    0f,
                    true);
                error = null;
                return true;
            }
        }

        private sealed class TestMapContentHandle : IMapContentHandle
        {
            private readonly GameObject root;

            public TestMapContentHandle(string mapId)
            {
                MapId = mapId;
                root = new GameObject($"CorpseSearchTests_Map_{mapId}");
                var mapContentRoot = new GameObject("MapContentRoot");
                mapContentRoot.transform.SetParent(root.transform, false);
                var dynamicObjectRoot = new GameObject("DynamicObjectRoot");
                dynamicObjectRoot.transform.SetParent(mapContentRoot.transform, false);
                Context = root.AddComponent<MapContext>();
                typeof(MapContext).GetField("mapContentRoot", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(Context, mapContentRoot);
                typeof(MapContext).GetField("dynamicObjectRoot", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(Context, dynamicObjectRoot.transform);
            }

            public string MapId { get; }
            public MapContext Context { get; }
            public bool IsValid => root != null && Context != null && Context.Validate(out _);

            public void Destroy()
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private sealed class FakeCharacterMotorPort : ICharacterMotorPort
        {
            public int UnitId => PlayerModel.LocalPlayerUnitId;
            public long MapInstanceId => 44;
            public bool IsValid => true;
            public bool IsEnabled => true;
            public Vector3 Position => Vector3.zero;
            public Quaternion Rotation => Quaternion.identity;
            public float MovementYawDegrees => 0f;
            public bool IsGrounded => true;
            public void Move(Vector3 motion) { }
            public void RotateTowards(Quaternion rotation, float maxDegreesDelta) { }
            public void FaceDirection(Vector3 direction) { }
            public void SetTiliRing(float current, float maximum, bool isSprinting, bool exhausted) { }
            public void SetWeaponPresentation(WeaponSlot activeSlot, bool showShortSword, bool showBow) { }
            public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin) { origin = Vector3.zero; return true; }
            public void FaceWorldHud(Vector3 cameraPosition, Vector3 cameraUp) { }
            public void PlayAttackFeedback() { }
            public void PlayWeaponSwingFeedback() { }
            public void PlayBowFireFeedback() { }
            public void PlayHurtFeedback() { }
            public void TickCombatFeedback(float deltaTime) { }
        }

        private sealed class TestResourceLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : UnityEngine.Object
            {
                if (path == LocalizationLanguages.LanguagesAddress)
                {
                    return new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"Chinese\"},{\"code\":\"en-US\",\"label\":\"English\"}]}") as T;
                }

                if (path == "zh-CN.json")
                {
                    return new TextAsset("{\"ui.inventory.title_bag\":\"背包\",\"ui.inventory.player_bag\":\"玩家背包\",\"ui.inventory.equipment\":\"装备\",\"ui.inventory.weapon_slot.melee\":\"近战\",\"ui.inventory.weapon_slot.ranged\":\"远程\",\"ui.inventory.unarmed_marker\":\"拳\",\"ui.inventory.detail_empty\":\"未选择物品\",\"ui.inventory.use\":\"使用\",\"ui.inventory.equip\":\"装备\",\"ui.inventory.unequip\":\"卸下\",\"ui.inventory.split\":\"拆分\",\"ui.inventory.cancel_selection\":\"取消选择\",\"ui.inventory.amount_stack\":\"数量：{0} / 最大堆叠：{1}\",\"ui.inventory.operation_failed\":\"操作失败\",\"ui.hotkey.toggle_inventory\":\"打开背包\"}") as T;
                }

                if (path == "en-US.json")
                {
                    return new TextAsset("{\"ui.inventory.title_bag\":\"Inventory\",\"ui.inventory.player_bag\":\"Player Bag\",\"ui.inventory.equipment\":\"Equipment\",\"ui.inventory.weapon_slot.melee\":\"Melee\",\"ui.inventory.weapon_slot.ranged\":\"Ranged\",\"ui.inventory.unarmed_marker\":\"Fist\",\"ui.inventory.detail_empty\":\"No item selected\",\"ui.inventory.use\":\"Use\",\"ui.inventory.equip\":\"Equip\",\"ui.inventory.unequip\":\"Unequip\",\"ui.inventory.split\":\"Split\",\"ui.inventory.cancel_selection\":\"Cancel Selection\",\"ui.inventory.amount_stack\":\"Amount: {0} / Max Stack: {1}\",\"ui.inventory.operation_failed\":\"Operation failed\",\"ui.hotkey.toggle_inventory\":\"Open Inventory\"}") as T;
                }

                return null;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            {
                var handle = new ResourceLoadHandle(new object());
                onLoaded?.Invoke(LoadAsset<T>(path));
                return handle;
            }

            public void Release(ResourceLoadHandle handle) { }
        }

        private sealed class SearchConfigProvider : IRuntimeConfigProvider
        {
            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                if (id == ItemIds.ShortSword)
                {
                    value = new ItemDefinition(id, "Sword", "", "", "", "", global::ItemType.Weapon, 1, global::ItemUseEffect.None, 0);
                    error = null;
                    return true;
                }

                if (id == ItemIds.MetalParts)
                {
                    value = new ItemDefinition(id, "Parts", "", "", "", "", global::ItemType.Material, 2, global::ItemUseEffect.None, 0);
                    error = null;
                    return true;
                }

                value = default;
                error = "missing item";
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                if (id == "loot.sword")
                {
                    value = new LootConfigDefinition(id, "", ItemIds.ShortSword, 1);
                    error = null;
                    return true;
                }

                if (id == "loot.stack-two")
                {
                    value = new LootConfigDefinition(id, "", ItemIds.MetalParts, 1);
                    error = null;
                    return true;
                }

                value = default;
                error = "missing loot";
                return false;
            }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error) { value = default; error = "missing"; return false; }
            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error) { values = Array.Empty<MapSpawnDefinition>(); error = null; return true; }
            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error) { value = default; error = "missing"; return false; }
            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error) { value = default; error = "missing"; return false; }
            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error) { value = default; error = "missing"; return false; }
        }
    }
}
