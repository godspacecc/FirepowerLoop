using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class ContinuousActionTests
    {
        private FakeTemporaryBuildingViewPort viewPort;
        private FakeProjectilePort projectile;
        private GameObject ground;
        private GameObject inventoryObject;
        private GameObject uiRoot;

        [SetUp]
        public void SetUp()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            viewPort = new FakeTemporaryBuildingViewPort();
            projectile = new FakeProjectilePort();
            TemporaryBuildingBehaviorTestSupport.InstallArchitecture(new TestRuntimeConfigProvider(), viewPort, projectile);
            TemporaryBuildingBehaviorTestSupport.RegisterMapSystem(new FakeMapContentLoader());
            Game.Interface.RegisterUtility<IItemTransferPort>(new ItemTransferCtrl(Game.Interface));
            TemporaryBuildingBehaviorTestSupport.EnterExpedition();
            Game.Interface.RegisterUtility<IPlayerRuntimePortProvider>(
                new ActionInputPortProvider(new ActionInputPort()));
            var expeditionId = Game.Interface.GetModel<SaveModel>().CurrentExpedition.ExpeditionId;
            Game.Interface.GetModel<ExpeditionModel>().SetExpeditionId(expeditionId);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(
                MapIds.SurvivalSample,
                TemporaryBuildingBehaviorTestSupport.CreateMapHandle(
                    MapIds.SurvivalSample,
                    TemporaryBuildingBehaviorTestSupport.MapInstanceId));
            Game.Interface.GetModel<SaveModel>().SetExpedition(
                new ExpeditionSaveData
                {
                    ExpeditionId = expeditionId,
                    MapId = MapIds.SurvivalSample,
                    Status = ExpeditionStatus.Playing
                });
            uiRoot = TemporaryBuildingBehaviorTestSupport.CreateUiRoot();
            ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerInputSystem.SetInputReaderFactoryForTests(null);
            PlayerInputSystem.SetPointerGroundPositionResolverForTests(null);
            if (inventoryObject != null) Object.DestroyImmediate(inventoryObject);
            if (ground != null) Object.DestroyImmediate(ground);
            TemporaryBuildingBehaviorTestSupport.Reset();
        }

        [Test]
        public void PlayerActions_AreExclusiveTransactionalAndRenderedBySharedWorldRing()
        {
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            var actions = Game.Interface.GetSystem<PlayerActionSystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var finished = new List<PlayerItemUseFinishedEvent>();
            var ended = new List<PlayerActionEndedEvent>();
            var finishedUnregister = Game.Interface.RegisterEvent<PlayerItemUseFinishedEvent>(finished.Add);
            var endedUnregister = Game.Interface.RegisterEvent<PlayerActionEndedEvent>(ended.Add);
            try
            {
                Assert.That(bag.TryAdd(ItemIds.Medkit, 2, 5, out _, out var addError), Is.True, addError);
                Game.Interface.GetModel<CombatModel>().SetPlayerHP(50);
                Assert.That(inventory.TryBeginUsePlayerBagItem(0, out var error), Is.True, error);
                Assert.That(actions.TryGetSnapshot(out var first), Is.True);
                Assert.That(first.Kind, Is.EqualTo(PlayerActionKind.UseItem));
                Assert.That(bag.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(2));

                Assert.That(inventory.TryBeginUsePlayerBagItem(0, out error), Is.True, error);
                Assert.That(ended.Count, Is.EqualTo(1));
                Assert.That(ended[0].CancelReason, Is.EqualTo(PlayerActionCancelReason.Replaced));
                Assert.That(bag.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(2));

                actions.TickForTests(1f);
                Assert.That(finished.Count, Is.EqualTo(2));
                Assert.That(finished[0].Succeeded, Is.False);
                Assert.That(finished[1].Succeeded, Is.True);
                Assert.That(ended.Count, Is.EqualTo(2));
                Assert.That(ended[1].Completed, Is.True);
                Assert.That(bag.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(1));
                Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(80));
            }
            finally
            {
                finishedUnregister.UnRegister();
                endedUnregister.UnRegister();
            }
        }

        [Test]
        public void PocketConsumableUsesSharedActionFromActualSource()
        {
            var model = Game.Interface.GetModel<PlayerBagModel>();
            Assert.That(model.Pocket.TryAdd(ItemIds.Medkit, 2, 5, out _, out var error), Is.True, error);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(50);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryBeginUseCarriedItem(new InventorySlotRef(InventoryContainerKind.Pocket, 0), out error), Is.True, error);
            Assert.That(model.Pocket.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(2));
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(1f);
            Assert.That(model.Pocket.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(1));
            Assert.That(model.Container.GetTotalAmount(ItemIds.Medkit), Is.Zero);
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(80));
        }

        [Test]
        public void PocketUseRejectsMovedSourceWithoutApplyingEffect()
        {
            var model = Game.Interface.GetModel<PlayerBagModel>();
            model.Pocket.TryAdd(ItemIds.Medkit, 1, 5, out _, out _);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(50);
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            Assert.That(inventory.TryBeginUseCarriedItem(new InventorySlotRef(InventoryContainerKind.Pocket, 0), out var error), Is.True, error);
            Assert.That(inventory.TryMoveCarriedStack(new InventorySlotRef(InventoryContainerKind.Pocket, 0), InventoryContainerKind.PlayerBag, 1, out error), Is.True, error);
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(1f);
            Assert.That(model.Container.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(50));
        }

        [Test]
        public void ExistingBuildAndSearchRules_RemainDomainOwnedAfterSharedTiming()
        {
            var building = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            var actions = Game.Interface.GetSystem<PlayerActionSystem>();
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.MetalParts, 2);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);

            Assert.That(building.BeginBuildPreview("temp_arrow_tower", out var error), Is.True, error);
            Assert.That(building.UpdateBuildPreview(new Vector3(1f, 0f, 0f), Quaternion.identity), Is.True);
            Assert.That(building.ConfirmBuild(out error), Is.True, error);
            Assert.That(actions.TryGetSnapshot(out var snapshot), Is.True);
            Assert.That(snapshot.Kind, Is.EqualTo(PlayerActionKind.Build));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(2));

            actions.TickForTests(1f);
            Assert.That(viewPort.BuildingCreateCount, Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.Zero);
            Assert.That(building.GetHudSnapshot().Progress01, Is.Zero);

            var corpseItems = new ItemContainer(6);
            Assert.That(corpseItems.TryAdd(ItemIds.Wood, 1, 20, out _, out error), Is.True, error);
            Assert.That(Game.Interface.GetModel<SearchContainerModel>().TryRegister(
                new SearchContainerRecord(700, TemporaryBuildingBehaviorTestSupport.MapInstanceId, 700, Vector3.right, corpseItems)), Is.True);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                700, TemporaryBuildingBehaviorTestSupport.MapInstanceId, UnitType.Enemy, UnitSource.MapPlaced, "corpse", Vector3.right,
                Quaternion.identity, UnitAuthorityMode.Local, 0, true), out error), Is.True, error);

            var search = Game.Interface.GetSystem<SearchContainerSystem>();
            Assert.That(search.TryBeginSearch(700, out error), Is.True, error);
            actions.TickForTests(SearchContainerSystem.SearchDurationSeconds);
            Assert.That(search.OpenContainerId, Is.EqualTo(700));
            Assert.That(UIHelper.GetUICtrl<SearchContainerUICtrl>().IsShow(), Is.True);
            Assert.That(UIHelper.GetUICtrl<BagUICtrl>().IsShow(), Is.True,
                "搜索窗口应同时打开紧凑背包，允许双向转移");
        }

        [Test]
        public void PlayerActions_CancelAndSlotMutationNeverCommitItemUse()
        {
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            var actions = Game.Interface.GetSystem<PlayerActionSystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Medkit, 2, 5, out _, out var error), Is.True, error);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(50);

            Assert.That(inventory.TryBeginUsePlayerBagItem(0, out error), Is.True, error);
            actions.CancelActive(PlayerActionCancelReason.UserCanceled);
            Assert.That(bag.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(2));

            Assert.That(inventory.TryBeginUsePlayerBagItem(0, out error), Is.True, error);
            Assert.That(bag.TryRemove(0, 2, out _, out error), Is.True, error);
            actions.TickForTests(1f);
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(50));
            Assert.That(bag.GetTotalAmount(ItemIds.Medkit), Is.Zero);
        }

        [Test]
        public void BuildFailureRollsBackAndSearchInvalidationCancelsWithoutOpening()
        {
            var building = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            var actions = Game.Interface.GetSystem<PlayerActionSystem>();
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.MetalParts, 2);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
            viewPort.CreateBuildingSucceeds = false;
            Assert.That(building.BeginBuildPreview("temp_arrow_tower", out var error), Is.True, error);
            Assert.That(building.UpdateBuildPreview(new Vector3(1f, 0f, 0f), Quaternion.identity), Is.True);
            Assert.That(building.ConfirmBuild(out error), Is.True, error);
            LogAssert.Expect(LogType.Error, "[TemporaryBuildingSystem] 建造表现创建失败：create blocked");
            actions.TickForTests(1f);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(2));
            Assert.That(building.Mode, Is.EqualTo(TemporaryBuildingMode.BuildPreview));

            var corpseItems = new ItemContainer(6);
            Assert.That(corpseItems.TryAdd(ItemIds.Wood, 1, 20, out _, out error), Is.True, error);
            Assert.That(Game.Interface.GetModel<SearchContainerModel>().TryRegister(
                new SearchContainerRecord(701, TemporaryBuildingBehaviorTestSupport.MapInstanceId, 701, Vector3.right, corpseItems)), Is.True);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                701, TemporaryBuildingBehaviorTestSupport.MapInstanceId, UnitType.Enemy, UnitSource.MapPlaced, "corpse", Vector3.right,
                Quaternion.identity, UnitAuthorityMode.Local, 0, true), out error), Is.True, error);
            var search = Game.Interface.GetSystem<SearchContainerSystem>();
            Assert.That(search.TryBeginSearch(701, out error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().Remove(701, UnitRemovedReason.Explicit), Is.True);
            Assert.That(search.IsSearching, Is.False);
            actions.TickForTests(1f);
            Assert.That(search.OpenContainerId, Is.Zero);
        }

        [Test]
        public void SharedWorldRing_UsesRadialFillAndSerializedFallbackIcon()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/World/UnitActionProgress.prefab");
            Assert.That(prefab, Is.Not.Null);
            var images = prefab.GetComponentsInChildren<Image>(true);
            Assert.That(images.Length, Is.EqualTo(2));
            var ring = prefab.transform.Find("Ring").GetComponent<Image>();
            var icon = prefab.transform.Find("Icon").GetComponent<Image>();
            Assert.That(ring.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(ring.fillMethod, Is.EqualTo(Image.FillMethod.Radial360));
            Assert.That(icon.sprite, Is.Not.Null);
            Assert.That(prefab.GetComponent<global::Game.PlayerView>(), Is.Null);
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            Assert.That(playerPrefab, Is.Not.Null);
            Assert.That(playerPrefab.GetComponentsInChildren<global::Game.PlayerView>(true).Length, Is.EqualTo(1));
        }

        [Test]
        public void PlayerInputCancelsBeforeUiAndPublishesInteractionAndAttack()
        {
            var architecture = (Game)Game.Interface;
            var actions = architecture.GetSystem<PlayerActionSystem>();
            var inventory = architecture.GetSystem<InventorySystem>();
            var bag = architecture.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Medkit, 4, 5, out _, out var error), Is.True, error);
            architecture.GetModel<CombatModel>().SetPlayerHP(50);

            var shownInventory = CreateShownBag();
            var port = new ActionInputPort();
            architecture.RegisterUtility<IPlayerRuntimePortProvider>(new ActionInputPortProvider(port));
            var input = new ActionInputFake();
            PlayerInputSystem.SetInputReaderFactoryForTests(() => input);
            PlayerInputSystem.SetPointerGroundPositionResolverForTests(_ => Vector3.forward);
            architecture.RegisterSystem(new MobileInputSystem());
            architecture.RegisterSystem(new AppearanceGallerySceneFlowSystem());
            architecture.RegisterSystem(new PlayerInputSystem());
            var inputSystem = architecture.GetSystem<PlayerInputSystem>();
            InvokePlayerInputUpdate(inputSystem);

            var ended = new List<PlayerActionEndedEvent>();
            var interactions = 0;
            var attacks = 0;
            var unregisterEnded = architecture.RegisterEvent<PlayerActionEndedEvent>(ended.Add);
            var unregisterInteract = architecture.RegisterEvent<PlayerInteractRequestedEvent>(_ => interactions++);
            var unregisterAttack = architecture.RegisterEvent<PlayerAttackRequestedEvent>(_ => attacks++);
            try
            {
                Assert.That(inventory.TryBeginUsePlayerBagItem(0, out error), Is.True, error);
                input.CancelPressedThisFrame = true;
                InvokePlayerInputUpdate(inputSystem);
                Assert.That(ended[ended.Count - 1].CancelReason, Is.EqualTo(PlayerActionCancelReason.UserCanceled));
                Assert.That(shownInventory.IsShow(), Is.True, "X 取消读条时背包仍应保持打开");

                input.CancelPressedThisFrame = false;
                Assert.That(inventory.TryBeginUsePlayerBagItem(0, out error), Is.True, error);
                input.InteractPressedThisFrame = true;
                InvokePlayerInputUpdate(inputSystem);
                Assert.That(ended[ended.Count - 1].CancelReason, Is.EqualTo(PlayerActionCancelReason.Interaction));
                Assert.That(interactions, Is.Zero, "背包等阻断 UI 打开时交互请求不应穿透");
                input.InteractPressedThisFrame = false;

                shownInventory.Hide();
                Assert.That(inventory.TryBeginUsePlayerBagItem(0, out error), Is.True, error);
                input.InteractPressedThisFrame = true;
                InvokePlayerInputUpdate(inputSystem);
                Assert.That(ended[ended.Count - 1].CancelReason, Is.EqualTo(PlayerActionCancelReason.Interaction));
                Assert.That(interactions, Is.EqualTo(1));
                input.InteractPressedThisFrame = false;

                Assert.That(inventory.TryBeginUsePlayerBagItem(0, out error), Is.True, error);
                input.AttackPressedThisFrame = true;
                InvokePlayerInputUpdate(inputSystem);
                Assert.That(ended[ended.Count - 1].CancelReason, Is.EqualTo(PlayerActionCancelReason.Attack));
                Assert.That(attacks, Is.EqualTo(1));
                input.AttackPressedThisFrame = false;

                Assert.That(actions.TryBegin(new PlayerActionRequest(
                    PlayerActionKind.Extraction, ExtractionSystem.ExtractionDurationSeconds, string.Empty),
                    out var extractionActionId, out error), Is.True, error);
                input.InteractPressedThisFrame = true;
                InvokePlayerInputUpdate(inputSystem);
                Assert.That(actions.TryGetSnapshot(out var sameExtraction), Is.True);
                Assert.That(sameExtraction.ActionId, Is.EqualTo(extractionActionId));
                Assert.That(interactions, Is.EqualTo(1), "撤离倒计时期间重复交互不得再次发布交互事件");
                input.InteractPressedThisFrame = false;

                input.CancelPressedThisFrame = true;
                InvokePlayerInputUpdate(inputSystem);
                Assert.That(actions.TryGetSnapshot(out _), Is.False);
                Assert.That(ended[ended.Count - 1].CancelReason, Is.EqualTo(PlayerActionCancelReason.UserCanceled));
                input.CancelPressedThisFrame = false;

                Assert.That(actions.TryBegin(new PlayerActionRequest(
                    PlayerActionKind.Extraction, ExtractionSystem.ExtractionDurationSeconds, string.Empty),
                    out _, out error), Is.True, error);
                input.AttackPressedThisFrame = true;
                InvokePlayerInputUpdate(inputSystem);
                Assert.That(actions.TryGetSnapshot(out _), Is.False);
                Assert.That(ended[ended.Count - 1].CancelReason, Is.EqualTo(PlayerActionCancelReason.Attack));
                Assert.That(attacks, Is.EqualTo(2), "攻击取消撤离后仍应发布一次攻击");
                input.AttackPressedThisFrame = false;

                var corpseItems = new ItemContainer(6);
                Assert.That(corpseItems.TryAdd(ItemIds.Wood, 1, 20, out _, out error), Is.True, error);
                Assert.That(architecture.GetModel<SearchContainerModel>().TryRegister(
                    new SearchContainerRecord(702, TemporaryBuildingBehaviorTestSupport.MapInstanceId, 702, Vector3.zero, corpseItems)), Is.True);
                Assert.That(architecture.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                    702, TemporaryBuildingBehaviorTestSupport.MapInstanceId, UnitType.Enemy, UnitSource.MapPlaced, "corpse", Vector3.zero,
                    Quaternion.identity, UnitAuthorityMode.Local, 0, true), out error), Is.True, error);
                var search = architecture.GetSystem<SearchContainerSystem>();
                Assert.That(search.TryBeginSearch(702, out error), Is.True, error);
                input.MoveInput = Vector2.right;
                InvokePlayerInputUpdate(inputSystem);
                Assert.That(search.IsSearching, Is.False, "移动应取消搜索动作");
                Assert.That(actions.TryGetSnapshot(out _), Is.False);
            }
            finally
            {
                unregisterEnded.UnRegister();
                unregisterInteract.UnRegister();
                unregisterAttack.UnRegister();
            }
        }

        [Test]
        public void BagStaysOpenAndProgressViewLoadsThenFallsBackToDefaultIcon()
        {
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Medkit, 1, 5, out _, out var error), Is.True, error);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(50);
            var shownInventory = CreateShownBag();
            Assert.That(inventory.TryBeginUsePlayerBagItem(0, out error), Is.True, error);
            Assert.That(shownInventory.IsShow(), Is.True);
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(1f);
            Assert.That(shownInventory.IsShow(), Is.True, "读条开始和完成都不得自动关闭背包");

            var loader = new LocalizationAssetResourceLoader();
            var defaultSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f));
            var loadedSprite = Sprite.Create(Texture2D.grayTexture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f));
            loader.IconAddress = "test/action/icon";
            loader.IconSprite = loadedSprite;
            ResourceLoader.SetLoaderForTests(loader);
            var root = new GameObject("ActionProgressTestRoot");
            var canvas = root.AddComponent<Canvas>();
            var ring = new GameObject("Ring", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            ring.transform.SetParent(root.transform, false);
            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            icon.transform.SetParent(root.transform, false);
            icon.sprite = defaultSprite;
            var view = new UnitActionProgressView();
            SetPrivateField(view, "root", root);
            SetPrivateField(view, "worldCanvas", canvas);
            SetPrivateField(view, "radialFill", ring);
            SetPrivateField(view, "centerIcon", icon);
            try
            {
                view.Initialize();
                view.Show(new PlayerActionSnapshot(1, PlayerActionKind.UseItem, .5f, 1f, .5f, loader.IconAddress));
                Assert.That(root.activeSelf, Is.True);
                Assert.That(ring.fillAmount, Is.EqualTo(.5f).Within(.001f));
                Assert.That(icon.sprite, Is.SameAs(loadedSprite));
                view.Show(new PlayerActionSnapshot(1, PlayerActionKind.UseItem, .5f, 1f, .5f, "missing/action/icon"));
                Assert.That(icon.sprite, Is.SameAs(defaultSprite));
                view.Hide();
                Assert.That(root.activeSelf, Is.False);
                Assert.That(icon.sprite, Is.SameAs(defaultSprite));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(defaultSprite);
                Object.DestroyImmediate(loadedSprite);
            }
        }

        [Test]
        public void SearchDistanceInvalidationCancelsWithoutOpening()
        {
            var actions = Game.Interface.GetSystem<PlayerActionSystem>();
            var search = Game.Interface.GetSystem<SearchContainerSystem>();
            var corpseItems = new ItemContainer(6);
            Assert.That(corpseItems.TryAdd(ItemIds.Wood, 1, 20, out _, out var error), Is.True, error);
            Assert.That(Game.Interface.GetModel<SearchContainerModel>().TryRegister(
                new SearchContainerRecord(703, TemporaryBuildingBehaviorTestSupport.MapInstanceId, 703, Vector3.zero, corpseItems)), Is.True);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                703, TemporaryBuildingBehaviorTestSupport.MapInstanceId, UnitType.Enemy, UnitSource.MapPlaced, "corpse", Vector3.zero,
                Quaternion.identity, UnitAuthorityMode.Local, 0, true), out error), Is.True, error);
            Assert.That(search.TryBeginSearch(703, out error), Is.True, error);
            TemporaryBuildingBehaviorTestSupport.MovePlayer(new Vector3(SearchContainerSystem.SearchDistance + .1f, 0f, 0f));
            InvokeSearchUpdate(search);
            Assert.That(search.IsSearching, Is.False);
            Assert.That(actions.TryGetSnapshot(out _), Is.False);
            actions.TickForTests(1f);
            Assert.That(search.OpenContainerId, Is.Zero);
        }

        [Test]
        public void LegacyBuildHudProgressRootIsAlwaysHidden()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/TemporaryBuilding/TemporaryBuildingHud.prefab");
            Assert.That(prefab, Is.Not.Null);
            var instance = Object.Instantiate(prefab);
            try
            {
                var ui = instance.GetComponent<TemporaryBuildingHudUI>();
                Assert.That(ui, Is.Not.Null);
                Assert.That(ui.ProgressRoot, Is.Not.Null);
                ui.ProgressRoot.SetActive(true);
                var ctrl = new TemporaryBuildingHudUICtrl();
                SetPrivateField(ctrl, "BaseUI", ui);
                SetPrivateField(ctrl, "_isActive", true);
                typeof(TemporaryBuildingHudUICtrl).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(ctrl, null);
                Assert.That(ui.ProgressRoot.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void InvokePlayerInputUpdate(PlayerInputSystem system)
        {
            typeof(PlayerInputSystem).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(system, null);
        }

        private static void InvokeSearchUpdate(SearchContainerSystem system)
        {
            typeof(SearchContainerSystem).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(system, null);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }
        }

        private BagUICtrl CreateShownBag()
        {
            inventoryObject = new GameObject("BagActionTestUI", typeof(RectTransform), typeof(BagUI));
            var ui = inventoryObject.GetComponent<BagUI>();
            var ctrl = new BagUICtrl();
            SetPrivateField(ctrl, "BaseUI", ui);
            SetPrivateField(ctrl, "_isActive", true);
            var dict = typeof(UIHelper).GetField("UICtrlDict", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as Dictionary<string, BaseUICtrl>;
            Assert.That(dict, Is.Not.Null);
            dict[typeof(BagUICtrl).Name] = ctrl;
            return ctrl;
        }

        private sealed class ActionInputFake : PlayerInputSystem.IPlayerInputReader
        {
            public Vector2 MoveInput { get; set; }
            public bool IsEnabled { get; private set; }
            public bool AttackPressedThisFrame { get; set; }
            public bool HolsterPressedThisFrame { get; set; }
            public bool MeleeSlotPressedThisFrame => false;
            public bool RangedSlotPressedThisFrame => false;
            public bool InteractPressedThisFrame { get; set; }
            public bool SprintHeld => false;
            public bool BuildPressedThisFrame => false;
            public bool DestroyPressedThisFrame => false;
            public bool CancelPressedThisFrame { get; set; }
            public bool ReloadPressedThisFrame { get; set; }
            public void Enable() => IsEnabled = true;
            public void Disable() => IsEnabled = false;
            public void Dispose() { }
        }

        private sealed class ActionInputPort : ICharacterMotorPort
        {
            public int UnitId => PlayerModel.LocalPlayerUnitId;
            public long MapInstanceId => TemporaryBuildingBehaviorTestSupport.MapInstanceId;
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
            public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin) { origin = Vector3.up; return true; }
            public void FaceWorldHud(Vector3 cameraPosition, Vector3 cameraUp) { }
            public void PlayAttackFeedback() { }
            public void PlayWeaponSwingFeedback() { }
            public void PlayBowFireFeedback() { }
            public void PlayHurtFeedback() { }
            public void TickCombatFeedback(float deltaTime) { }
        }

        private sealed class ActionInputPortProvider : IPlayerRuntimePortProvider
        {
            private readonly ActionInputPort port;

            public ActionInputPortProvider(ActionInputPort port) { this.port = port; }
            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort value)
            {
                value = unitId == port.UnitId && mapInstanceId == port.MapInstanceId ? port : null;
                return value != null;
            }
            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort value) { value = null; return false; }
            public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider) => false;
            public bool TryGetCurrentPlayerMapLocalPose(out PlayerMapLocalPoseSnapshot snapshot, out string error)
            {
                snapshot = new PlayerMapLocalPoseSnapshot(
                    port.UnitId, port.MapInstanceId, port.Position, 0f, true);
                error = null;
                return true;
            }
        }
    }
}
