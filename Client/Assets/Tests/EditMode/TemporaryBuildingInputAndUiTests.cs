using System.Linq;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Reflection;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Game.Tests
{
    public sealed class TemporaryBuildingInputAndUiTests
    {
        private FakeTemporaryBuildingViewPort viewPort;
        private GameObject ground;
        private GameObject uiRoot;
        private TemporaryBuildingCtrl buildingCtrl;
        private FakeInput input;
        private LocalizationAssetResourceLoader resourceLoader;
        private Sprite testIcon;

        [SetUp]
        public void SetUp()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            testIcon = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(.5f, .5f));
            resourceLoader = new LocalizationAssetResourceLoader
            {
                IconAddress = "test.temp_building.icon",
                IconSprite = testIcon
            };
            ResourceLoader.SetLoaderForTests(resourceLoader);
            LocalizationLanguages.ResetForTests();
            viewPort = new FakeTemporaryBuildingViewPort();
            TemporaryBuildingBehaviorTestSupport.InstallArchitecture(new TestRuntimeConfigProvider(arrowIconAddress: "test.temp_building.icon"), viewPort, new FakeProjectilePort());
            // 夹具同步正式输入依赖；构建架构会安装默认加载器，图标加载器必须随后设置。
            ResourceLoader.SetLoaderForTests(resourceLoader);
            Game.Interface.RegisterSystem(new MobileInputSystem());
            Game.Interface.RegisterSystem(new AppearanceGallerySceneFlowSystem());
            TemporaryBuildingBehaviorTestSupport.EnterExpedition();
            ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
            uiRoot = TemporaryBuildingBehaviorTestSupport.CreateUiRoot();
            // 右侧面板按屏幕高度伸展，夹具需要给出真实的父级区域。
            ((RectTransform)uiRoot.transform).sizeDelta = new Vector2(1920, 1080);
            foreach (Transform layer in uiRoot.transform)
            {
                var rect = layer as RectTransform;
                if (rect == null) continue;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
            buildingCtrl = new TemporaryBuildingCtrl((Game)Game.Interface);
            Game.Interface.RegisterModel(new HotKeyModel());
            Game.Interface.RegisterUtility(new HotKeyActionRegistry());
            PlayerInputSystem.SetInputReaderFactoryForTests(() => input = new FakeInput());
            Game.Interface.RegisterSystem(new PlayerInputSystem());
            Game.Interface.RegisterSystem(new HotKeySystem());
        }

        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
            UIRoot.ClearForTests();
            PlayerInputSystem.SetInputReaderFactoryForTests(null);
            buildingCtrl?.ReleaseAll();
            if (uiRoot != null) Object.DestroyImmediate(uiRoot);
            if (ground != null) Object.DestroyImmediate(ground);
            if (testIcon != null) Object.DestroyImmediate(testIcon);
            TemporaryBuildingBehaviorTestSupport.Reset();
        }

        [Test]
        public void SelectionDisplaysThreeOrderedRowsWithIndependentNameAndMaterialsAndClickStartsPreview()
        {
            Assert.That(TemporaryBuildingSelectUICtrl.TryShow(out var error), Is.True, error);
            var ui = Object.FindObjectOfType<TemporaryBuildingSelectUI>();
            Assert.That(ui, Is.Not.Null);
            Assert.That(ui.GetComponent<ReferenceCollector>(), Is.Not.Null);
            Assert.That(ui.BtnClose.transform, Is.Not.EqualTo(ui.BuildingList.transform));

            var buildingScrollField = typeof(TemporaryBuildingSelectUI).GetField(
                "BuildingScroll",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(buildingScrollField, Is.Not.Null, "生成 UI 缺少 BuildingScroll 字段");
            Assert.That(buildingScrollField.FieldType, Is.EqualTo(typeof(ScrollRect)));
            var buildingScroll = buildingScrollField.GetValue(ui) as ScrollRect;
            Assert.That(buildingScroll, Is.Not.Null, "BuildingScroll 未绑定到正式 ScrollRect");
            Assert.That(buildingScroll.content, Is.SameAs(ui.BuildingList.transform));
            Assert.That(buildingScroll.viewport, Is.Not.Null);
            Assert.That(buildingScroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(buildingScroll.horizontal, Is.False);
            Assert.That(buildingScroll.vertical, Is.True);
            Assert.That(buildingScroll.movementType, Is.EqualTo(ScrollRect.MovementType.Clamped));
            Assert.That(buildingScroll.inertia, Is.False);
            Assert.That(buildingScroll.scrollSensitivity, Is.EqualTo(24f));
            Assert.That(buildingScroll.horizontalScrollbar, Is.Null);
            Assert.That(buildingScroll.verticalScrollbar, Is.Not.Null);

            Assert.That(ui.BuildingList.totalCount, Is.EqualTo(3), "选择 Controller 未将配置建筑数量写入列表");
            Assert.That(ui.BuildingList.itemPrefab, Is.Not.Null, "BuildingList.itemPrefab 未接线");
            Assert.That(ui.BuildingList.transform.childCount, Is.EqualTo(3), "列表未生成三个子节点");
            Assert.That(ui.BuildingList.transform.Cast<Transform>().Count(child => child.gameObject.activeSelf), Is.EqualTo(3), "列表子节点未全部激活");

            var rows = ui.BuildingList.GetDraggableItems().Select(item => item.GetComponent<TemporaryBuildingOptionBoxUI>()).ToArray();
            Assert.That(rows, Has.Length.EqualTo(3));
            Assert.That(rows.Select(row => row.TextName.text), Is.EqualTo(new[] { "固定箭塔", "木质路障", "火堆" }));
            Assert.That(rows[0].TextName, Is.Not.EqualTo(rows[0].TextMaterials));
            Assert.That(rows[0].TextMaterials.text, Does.Contain("金属零件"));
            Assert.That(rows[1].TextMaterials.text, Does.Contain("木材"));
            Assert.That(rows[2].TextMaterials.text, Does.Contain("木材"));
            Assert.That(rows[0].Icon.transform, Is.Not.EqualTo(rows[0].transform));
            Assert.That(rows[0].Icon.gameObject.activeSelf, Is.True);
            Assert.That(rows[0].Icon.sprite, Is.SameAs(testIcon));
            Assert.That(rows[1].Icon.gameObject.activeSelf, Is.False);
            Assert.That(rows[1].gameObject.activeSelf, Is.True);

            RebuildScrollLayout(buildingScroll);
            var cards = rows.Select(row => WorldRect((RectTransform)row.transform)).ToArray();
            var viewportRect = WorldRect(buildingScroll.viewport);
            Assert.That(cards[0].yMin, Is.GreaterThan(cards[1].yMin));
            Assert.That(cards[1].yMin, Is.GreaterThan(cards[2].yMin));
            for (var index = 0; index < cards.Length; index++)
            {
                Assert.That(cards[index].xMin, Is.GreaterThanOrEqualTo(viewportRect.xMin - 0.001f));
                Assert.That(cards[index].xMax, Is.LessThanOrEqualTo(viewportRect.xMax + 0.001f));
                Assert.That(cards[index].yMin, Is.GreaterThanOrEqualTo(viewportRect.yMin - 0.001f));
                Assert.That(cards[index].yMax, Is.LessThanOrEqualTo(viewportRect.yMax + 0.001f));
            }

            Assert.That(cards[0].yMin, Is.GreaterThanOrEqualTo(cards[1].yMax));
            Assert.That(cards[1].yMin, Is.GreaterThanOrEqualTo(cards[2].yMax));
            Assert.That(buildingScroll.content.rect.height, Is.LessThanOrEqualTo(buildingScroll.viewport.rect.height));
            var contentPositionBeforeScroll = buildingScroll.content.anchoredPosition;
            buildingScroll.OnScroll(CreatePointerEvent(Vector2.zero, new Vector2(0f, 1f)));
            RebuildScrollLayout(buildingScroll);
            Assert.That(Vector2.Distance(buildingScroll.content.anchoredPosition, contentPositionBeforeScroll), Is.LessThanOrEqualTo(0.001f));

            var title = ui.transform.Find("Text");
            Assert.That(title, Is.Not.Null);
            Assert.That(title.IsChildOf(buildingScroll.transform), Is.False);
            Assert.That(ui.BtnClose.transform.IsChildOf(buildingScroll.transform), Is.False);

            rows[1].BtnSelect.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Mode, Is.EqualTo(TemporaryBuildingMode.BuildPreview));
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().PreviewConfigId, Is.EqualTo("temp_roadblock_wood"));
            Assert.That(viewPort.PreviewCreateCount, Is.EqualTo(1));
        }

        [Test]
        public void OverflowDragPreservesVisibleRefreshPositionAndReopenResetsTop()
        {
            Assert.That(TemporaryBuildingSelectUICtrl.TryShow(out var error), Is.True, error);
            var ui = Object.FindObjectOfType<TemporaryBuildingSelectUI>();
            Assert.That(ui, Is.Not.Null);
            var buildingScroll = ui.BuildingScroll;
            Assert.That(buildingScroll, Is.Not.Null);

            var viewport = buildingScroll.viewport;
            viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 220f);
            RebuildScrollLayout(buildingScroll);
            Assert.That(buildingScroll.content.rect.height, Is.GreaterThan(viewport.rect.height));

            var title = ui.transform.Find("Text");
            Assert.That(title, Is.Not.Null);
            var titlePosition = title.position;
            var closePosition = ui.BtnClose.transform.position;
            var viewportWorldRect = WorldRect(viewport);
            var start = viewportWorldRect.center;
            var dragData = CreatePointerEvent(start);
            buildingScroll.OnBeginDrag(dragData);
            dragData.position = start + Vector2.up * 120f;
            buildingScroll.OnDrag(dragData);
            buildingScroll.OnEndDrag(dragData);
            RebuildScrollLayout(buildingScroll);

            Assert.That(buildingScroll.verticalNormalizedPosition, Is.LessThan(1f));
            Assert.That(Vector3.Distance(title.position, titlePosition), Is.LessThanOrEqualTo(0.001f));
            Assert.That(Vector3.Distance(ui.BtnClose.transform.position, closePosition), Is.LessThanOrEqualTo(0.001f));
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Mode, Is.EqualTo(TemporaryBuildingMode.Normal));
            Assert.That(viewPort.PreviewCreateCount, Is.Zero);

            buildingScroll.verticalNormalizedPosition = 0.5f;
            RebuildScrollLayout(buildingScroll);
            var normalizedPositionBeforeScroll = buildingScroll.verticalNormalizedPosition;
            buildingScroll.OnScroll(CreatePointerEvent(viewportWorldRect.center, new Vector2(0f, 1f)));
            RebuildScrollLayout(buildingScroll);
            Assert.That(Mathf.Abs(buildingScroll.verticalNormalizedPosition - normalizedPositionBeforeScroll), Is.GreaterThan(0.01f));
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Mode, Is.EqualTo(TemporaryBuildingMode.Normal));
            Assert.That(viewPort.PreviewCreateCount, Is.Zero);

            var visibleRefreshPosition = buildingScroll.verticalNormalizedPosition;
            Game.Interface.SendEvent(new InventoryChangedEvent());
            RebuildScrollLayout(buildingScroll);
            Assert.That(Mathf.Abs(buildingScroll.verticalNormalizedPosition - visibleRefreshPosition), Is.LessThanOrEqualTo(0.01f));

            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
            Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));
            RebuildScrollLayout(buildingScroll);
            Assert.That(Mathf.Abs(buildingScroll.verticalNormalizedPosition - visibleRefreshPosition), Is.LessThanOrEqualTo(0.01f));

            var controller = UIHelper.GetUICtrl<TemporaryBuildingSelectUICtrl>();
            Assert.That(controller, Is.Not.Null);
            controller.Hide();
            Assert.That(TemporaryBuildingSelectUICtrl.TryShow(out error), Is.True, error);
            RebuildScrollLayout(buildingScroll);
            Assert.That(buildingScroll.verticalNormalizedPosition, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void EverySelectionRowStartsItsCorrespondingPreviewAndMaterialsRefreshAfterBagChange()
        {
            var expectedIds = new[] { "temp_arrow_tower", "temp_roadblock_wood", "temp_campfire" };
            for (var index = 0; index < expectedIds.Length; index++)
            {
                Assert.That(TemporaryBuildingSelectUICtrl.TryShow(out var error), Is.True, error);
                var ui = Object.FindObjectOfType<TemporaryBuildingSelectUI>();
                var rows = ui.BuildingList.GetDraggableItems().Select(item => item.GetComponent<TemporaryBuildingOptionBoxUI>()).ToArray();
                if (index == 0)
                {
                    var before = rows[0].TextMaterials.text;
                    TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.MetalParts, 2);
                    TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
                    Game.Interface.SendEvent(new InventoryChangedEvent());
                    Assert.That(rows[0].TextMaterials.text, Is.Not.EqualTo(before));
                    Assert.That(rows[0].TextMaterials.text, Does.Contain("2/2"));
                    Assert.That(rows[0].TextMaterials.text, Does.Contain("3/3"));
                }

                rows[index].BtnSelect.onClick.Invoke();
                var model = Game.Interface.GetModel<TemporaryBuildingModel>();
                Assert.That(model.Mode, Is.EqualTo(TemporaryBuildingMode.BuildPreview));
                Assert.That(model.PreviewConfigId, Is.EqualTo(expectedIds[index]));
                Game.Interface.GetSystem<TemporaryBuildingSystem>().CancelCurrentMode();
            }
        }

        [Test]
        public void CloseButtonHidesSelectionWithoutCreatingPreview()
        {
            Assert.That(TemporaryBuildingSelectUICtrl.TryShow(out var error), Is.True, error);
            var ui = Object.FindObjectOfType<TemporaryBuildingSelectUI>();
            ui.BtnClose.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Mode, Is.EqualTo(TemporaryBuildingMode.Normal));
            Assert.That(viewPort.PreviewCreateCount, Is.EqualTo(0));
            Assert.That(ui.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void OptionPrefabDoesNotOwnFixedLocalizedText()
        {
            var option = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/TemporaryBuilding/Box/TemporaryBuildingOptionBox.prefab");
            var ui = option.GetComponent<TemporaryBuildingOptionBoxUI>();
            Assert.That(ui.TextName, Is.Not.Null);
            Assert.That(ui.TextMaterials, Is.Not.Null);
            Assert.That(ui.TextName, Is.Not.EqualTo(ui.TextMaterials));
            Assert.That(option.GetComponentsInChildren<LocalizedText>(true), Is.Empty);
        }

        [Test]
        public void BuildInputOpensSelectionAndPreservesMovement()
        {
            var playerInput = Game.Interface.GetSystem<PlayerInputSystem>();
            InvokeUpdate(playerInput);
            input.BuildPressedThisFrame = true;
            input.MoveInput = Vector2.one;
            InvokeUpdate(playerInput);

            var ui = Object.FindObjectOfType<TemporaryBuildingSelectUI>();
            Assert.That(ui, Is.Not.Null);
            Assert.That(ui.gameObject.activeSelf, Is.True);
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Mode, Is.EqualTo(TemporaryBuildingMode.Normal));
            Assert.That(viewPort.PreviewCreateCount, Is.Zero);
            Assert.That(playerInput.MoveInput, Is.EqualTo(Vector2.one));

            input.BuildPressedThisFrame = true;
            InvokeUpdate(playerInput);
            Assert.That(viewPort.PreviewCreateCount, Is.Zero);
        }

        [Test]
        public void SelectionAndPreviewKeepMobileMovementWhileConstructionStillBlocksIt()
        {
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            Assert.That(mobile.TrySetEditorSimulation(true), Is.True);
            Assert.That(TemporaryBuildingSelectUICtrl.TryShow(out var error), Is.True, error);
            Assert.That(UIHelper.HasUIOpen, Is.False);
            Assert.That(mobile.CanAcceptMovement(), Is.True, "浏览建筑列表时应保留摇杆");
            UIHelper.HideUI<TemporaryBuildingSelectUICtrl>();
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            Assert.That(system.BeginBuildPreview("temp_roadblock_wood", out error), Is.True, error);
            input.MoveInput = Vector2.one;
            Assert.That(Game.Interface.GetSystem<PlayerInputSystem>().MoveInput, Is.EqualTo(Vector2.one));
            Assert.That(mobile.CanAcceptMovement(), Is.True, "预览时应允许走近放置位置");
            Game.Interface.GetModel<TemporaryBuildingModel>().Mode = TemporaryBuildingMode.BuildProgress;
            Assert.That(mobile.CanAcceptMovement(), Is.False);
            Assert.That(Game.Interface.GetSystem<PlayerInputSystem>().MoveInput, Is.EqualTo(Vector2.zero));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UserCancelPreviewReturnsToSelectionAndAllowsSelectingAgain(bool keyboard)
        {
            Assert.That(TemporaryBuildingSelectUICtrl.TryShow(out var error), Is.True, error);
            var ui = Object.FindObjectOfType<TemporaryBuildingSelectUI>();
            ui.BuildingList.GetDraggableItems().First().GetComponent<TemporaryBuildingOptionBoxUI>().BtnSelect.onClick.Invoke();
            Assert.That(ui.gameObject.activeSelf, Is.False);
            if (keyboard)
            {
                input.CancelPressedThisFrame = true;
                InvokeUpdate(Game.Interface.GetSystem<PlayerInputSystem>());
                input.CancelPressedThisFrame = false;
            }
            else Object.FindObjectOfType<TemporaryBuildingHudUI>().BtnCancel.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Mode, Is.EqualTo(TemporaryBuildingMode.Normal));
            Assert.That(ui.gameObject.activeSelf, Is.True, "玩家主动取消后应恢复建筑列表");
            Assert.That(ui.BuildingList.totalCount, Is.EqualTo(3));
            ui.BuildingList.GetDraggableItems().First().GetComponent<TemporaryBuildingOptionBoxUI>().BtnSelect.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Mode, Is.EqualTo(TemporaryBuildingMode.BuildPreview));
            Assert.That(ui.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void OpenSelectionPreventsAttackInteractWeaponSlotsRepeatedBuildAndDestroyFromChangingGameplayState()
        {
            var attacks = 0;
            var interactions = 0;
            var slotSwitches = 0;
            Game.Interface.RegisterEvent<PlayerAttackRequestedEvent>(_ => attacks++);
            Game.Interface.RegisterEvent<PlayerInteractRequestedEvent>(_ => interactions++);
            Game.Interface.RegisterEvent<WeaponSlotSwitchRequestedEvent>(_ => slotSwitches++);

            Assert.That(TemporaryBuildingSelectUICtrl.TryShow(out var error), Is.True, error);
            input.AttackPressedThisFrame = true;
            input.InteractPressedThisFrame = true;
            input.MeleeSlotPressedThisFrame = true;
            input.RangedSlotPressedThisFrame = true;
            input.BuildPressedThisFrame = true;
            input.DestroyPressedThisFrame = true;
            InvokeUpdate(Game.Interface.GetSystem<PlayerInputSystem>());

            Assert.That(attacks, Is.Zero);
            Assert.That(interactions, Is.Zero);
            Assert.That(slotSwitches, Is.Zero);
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Mode, Is.EqualTo(TemporaryBuildingMode.Normal));
            Assert.That(viewPort.PreviewCreateCount, Is.Zero);
        }

        [Test]
        public void SelectionRefreshesNamesAndMaterialsWhenLanguageChanges()
        {
            Assert.That(TemporaryBuildingSelectUICtrl.TryShow(out var error), Is.True, error);
            var ui = Object.FindObjectOfType<TemporaryBuildingSelectUI>();
            var rows = ui.BuildingList.GetDraggableItems().Select(item => item.GetComponent<TemporaryBuildingOptionBoxUI>()).ToArray();
            Assert.That(rows[0].TextName.text, Is.EqualTo("固定箭塔"));

            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
            Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));

            Assert.That(rows[0].TextName.text, Is.EqualTo("Fixed Arrow Tower"));
            Assert.That(rows[1].TextName.text, Is.EqualTo("Wooden Roadblock"));
            Assert.That(rows[1].TextMaterials.text, Does.Contain("Wood"));
        }

        [Test]
        public void EscapeClosesSelectionWithoutEnteringPreviewAndManualTowerKeepsFixedHudHidden()
        {
            Assert.That(TemporaryBuildingSelectUICtrl.TryShow(out var error), Is.True, error);
            var selectionUi = Object.FindObjectOfType<TemporaryBuildingSelectUI>();
            Assert.That(selectionUi, Is.Not.Null);
            var hotKeys = Game.Interface.GetSystem<HotKeySystem>();
            Assert.That(hotKeys.ExecuteBinding(HotKeyDefaults.Escape), Is.True);
            Assert.That(selectionUi.gameObject.activeSelf, Is.False);
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Mode, Is.EqualTo(TemporaryBuildingMode.Normal));
            Assert.That(viewPort.PreviewCreateCount, Is.Zero);

            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.MetalParts, 2);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
            var arrow = TemporaryBuildingBehaviorTestSupport.Build("temp_arrow_tower", new Vector3(1f, 0f, 0f));
            Assert.That(system.TryEnterManualAttackOperation(arrow, out error), Is.True, error);
            var hud = Object.FindObjectOfType<TemporaryBuildingHudUI>(true);
            Assert.That(hud.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void ActualHudStaysHiddenDuringManualAttackMode()
        {
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            Assert.That(system.BeginBuildPreview("temp_arrow_tower", out var error), Is.True, error);
            var hud = Object.FindObjectOfType<TemporaryBuildingHudUI>(true);
            Assert.That(hud, Is.Not.Null);
            Assert.That(hud.ResourceText.text, Does.Not.Contain("箭矢"));

            system.CancelCurrentMode();
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.MetalParts, 2);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
            var arrowTower = TemporaryBuildingBehaviorTestSupport.Build("temp_arrow_tower", Vector3.forward);
            Assert.That(system.TryEnterManualAttackOperation(arrowTower, out error), Is.True, error);

            hud = Object.FindObjectOfType<TemporaryBuildingHudUI>(true);
            Assert.That(hud.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void DemolitionShowsTooFarAndRevalidatesStationaryPointerAfterPlayerMoves()
        {
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
            var id = TemporaryBuildingBehaviorTestSupport.Build("temp_roadblock_wood", Vector3.forward);
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            Assert.That(system.BeginDestroyMode(out var error), Is.True, error);
            viewPort.PickedRuntimeId = id;
            TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.back * 4f);
            system.RefreshDestroyTarget(new Ray(Vector3.up * 10, Vector3.down));
            var hud = Object.FindObjectOfType<TemporaryBuildingHudUI>();
            Assert.That(hud.StatusText.text, Does.Contain("距离太远"));
            Assert.That(system.GetHudSnapshot().DestroyTargetBuildingId, Is.Zero);
            Assert.That(system.ConfirmDestroy(out _), Is.False);

            TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.zero);
            typeof(TemporaryBuildingSystem).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(system, null);
            Assert.That(system.GetHudSnapshot().DestroyTargetBuildingId, Is.EqualTo(id));
            Assert.That(hud.StatusText.text, Does.Not.Contain("距离太远"));
            Assert.That(hud.StatusText.text, Does.Contain("木质路障"));
            system.CancelCurrentMode();
            Assert.That(system.BeginDestroyMode(out error), Is.True, error);
            Assert.That(hud.StatusText.text, Does.Not.Contain("距离太远"));
            Assert.That(system.GetHudSnapshot().DestroyTargetBuildingId, Is.Zero);
        }

        [Test]
        public void DemolitionRangeFollowsPlayerAndIsReleasedOnCancelAndMapRelease()
        {
            var handle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.SurvivalSample, TemporaryBuildingBehaviorTestSupport.MapInstanceId);
            var root = handle.Context.gameObject;
            try
            {
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, handle);
                var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
                Assert.That(system.BeginDestroyMode(out var error), Is.True, error);
                var ring = root.GetComponentInChildren<LineRenderer>();
                Assert.That(ring, Is.Not.Null, "进入拆除模式应立即显示范围圈");
                Assert.That(ring.loop, Is.True);
                Assert.That(new Vector2(ring.GetPosition(0).x, ring.GetPosition(0).z).magnitude, Is.EqualTo(3f).Within(.01f));
                TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.right);
                typeof(TemporaryBuildingCtrl).GetMethod("OnLateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(buildingCtrl, null);
                Assert.That(ring.transform.position.x, Is.EqualTo(1f).Within(.01f));
                system.CancelCurrentMode();
                Assert.That(root.GetComponentInChildren<LineRenderer>(), Is.Null);
                Assert.That(system.BeginDestroyMode(out error), Is.True, error);
                Assert.That(root.GetComponentInChildren<LineRenderer>(), Is.Not.Null);
                system.ClearForMap(TemporaryBuildingBehaviorTestSupport.MapInstanceId);
                Assert.That(root.GetComponentInChildren<LineRenderer>(), Is.Null);
                Assert.That(system.BeginBuildPreview("temp_roadblock_wood", out error), Is.True, error);
                ring = root.GetComponentInChildren<LineRenderer>();
                Assert.That(ring, Is.Not.Null, "建造预览也应显示玩家操作圈");
                Assert.That(new Vector2(ring.GetPosition(0).x, ring.GetPosition(0).z).magnitude, Is.EqualTo(3f).Within(.01f));
                system.CancelCurrentMode();
                Assert.That(root.GetComponentInChildren<LineRenderer>(), Is.Null);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void HoldingRotationContinuesAndReleaseDoesNotAddExtraStep()
        {
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            Assert.That(system.BeginBuildPreview("temp_roadblock_wood", out var error), Is.True, error);
            var hud = Object.FindObjectOfType<TemporaryBuildingHudUI>();
            var press = hud.BtnRotate.GetComponent<MobileCombatPressView>();
            Assert.That(press, Is.Not.Null);
            Assert.That(hud.RotatePress, Is.SameAs(press), "生成 View 必须绑定正式按压组件");
            var ctrl = UIHelper.GetUICtrl<TemporaryBuildingHudUICtrl>();
            var tick = typeof(TemporaryBuildingHudUICtrl).GetMethod("TickRotationPress", BindingFlags.Instance | BindingFlags.NonPublic);
            var data = CreatePointerEvent(Vector2.zero);
            press.OnPointerDown(data);
            tick.Invoke(ctrl, new object[] { .35f });
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().PreviewRotation.eulerAngles.y, Is.EqualTo(0f).Within(.01f));
            tick.Invoke(ctrl, new object[] { 1f });
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().PreviewRotation.eulerAngles.y, Is.EqualTo(90f).Within(.01f));
            tick.Invoke(ctrl, new object[] { 1f });
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().PreviewRotation.eulerAngles.y, Is.EqualTo(180f).Within(.01f));
            press.OnPointerUp(data);
            hud.BtnRotate.onClick.Invoke();
            tick.Invoke(ctrl, new object[] { 1f });
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().PreviewRotation.eulerAngles.y, Is.EqualTo(180f).Within(.01f));
            press.OnPointerDown(data);
            system.CancelCurrentMode();
            tick.Invoke(ctrl, new object[] { 1f });
            Assert.That(system.BeginBuildPreview("temp_roadblock_wood", out error), Is.True, error);
            tick.Invoke(ctrl, new object[] { 1f });
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().PreviewRotation.eulerAngles.y, Is.EqualTo(0f).Within(.01f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RotationPointerPressOwnsWorldClickUntilRelease(bool hold)
        {
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 20);
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            var playerInput = Game.Interface.GetSystem<PlayerInputSystem>();
            InvokeUpdate(playerInput);
            Assert.That(system.BeginBuildPreview("temp_roadblock_wood", out var error), Is.True, error);
            var hud = Object.FindObjectOfType<TemporaryBuildingHudUI>();
            var ctrl = UIHelper.GetUICtrl<TemporaryBuildingHudUICtrl>();
            Canvas.ForceUpdateCanvases();
            var buttonPosition = RectTransformUtility.WorldToScreenPoint(null, hud.BtnRotate.transform.position);
            var mouse = InputSystem.AddDevice<Mouse>();
            PlayerInputSystem.SetPointerGroundPositionResolverForTests(_ => Vector3.right * 2);
            try
            {
                // 先模拟共享 Update 读到鼠标按下，再分发 PointerDown，覆盖输入与 UI 的先后顺序。
                InputState.Change(mouse, new MouseState { position = buttonPosition, buttons = 1 });
                input.AttackPressedThisFrame = true;
                InvokeUpdate(playerInput);
                Assert.That(system.Mode, Is.EqualTo(TemporaryBuildingMode.BuildPreview), "按在旋转按钮上不能先启动施工");
                var pointer = CreatePointerEvent(buttonPosition);
                ExecuteEvents.Execute(hud.BtnRotate.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                if (hold)
                    typeof(TemporaryBuildingHudUICtrl).GetMethod("TickRotationPress", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(ctrl, new object[] { 1.35f });
                // 捕获后拖出按钮也不能把本次按压转交给场景。
                InputState.Change(mouse, new MouseState { position = Vector2.zero, buttons = 1 });
                InvokeUpdate(playerInput);
                Assert.That(system.Mode, Is.EqualTo(TemporaryBuildingMode.BuildPreview));
                ExecuteEvents.Execute(hud.BtnRotate.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(hud.BtnRotate.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                InvokeUpdate(playerInput);
                Assert.That(system.Mode, Is.EqualTo(TemporaryBuildingMode.BuildPreview), "松开旋转仍不能误施工");
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().PreviewRotation.eulerAngles.y,
                    Is.EqualTo(hold ? 90f : 15f).Within(.01f));
            }
            finally { InputSystem.RemoveDevice(mouse); PlayerInputSystem.SetPointerGroundPositionResolverForTests(null); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DesktopWorldClickAndConfirmButtonStillStartConstruction(bool useConfirmButton)
        {
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 20);
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            var playerInput = Game.Interface.GetSystem<PlayerInputSystem>();
            InvokeUpdate(playerInput);
            Assert.That(system.BeginBuildPreview("temp_roadblock_wood", out var error), Is.True, error);
            var hud = Object.FindObjectOfType<TemporaryBuildingHudUI>();
            var mouse = InputSystem.AddDevice<Mouse>();
            PlayerInputSystem.SetPointerGroundPositionResolverForTests(_ => Vector3.right * 2);
            try
            {
                Canvas.ForceUpdateCanvases();
                var scenePosition = new Vector2(Screen.width * .25f, Screen.height * .75f);
                Assert.That(UIHelper.GetUICtrl<TemporaryBuildingHudUICtrl>().CapturesWorldPointer(scenePosition), Is.False);
                InputState.Change(mouse, new MouseState { position = scenePosition });
                InvokeUpdate(playerInput);
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().PreviewValid, Is.True);
                if (useConfirmButton)
                {
                    Assert.That(hud.BtnConfirm.interactable, Is.True);
                    hud.BtnConfirm.onClick.Invoke();
                }
                else
                {
                    input.AttackPressedThisFrame = true;
                    InvokeUpdate(playerInput);
                }
                Assert.That(system.Mode, Is.EqualTo(TemporaryBuildingMode.BuildProgress));
            }
            finally { InputSystem.RemoveDevice(mouse); PlayerInputSystem.SetPointerGroundPositionResolverForTests(null); }
        }

        [Test]
        public void RotationButtonUsesFineStepsAndDesktopPlacementPreservesRotation()
        {
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            Assert.That(system.BeginBuildPreview("temp_roadblock_wood", out var error), Is.True, error);
            var hud = Object.FindObjectOfType<TemporaryBuildingHudUI>();
            hud.BtnRotate.onClick.Invoke();
            var model = Game.Interface.GetModel<TemporaryBuildingModel>();
            Assert.That(model.PreviewRotation.eulerAngles.y, Is.EqualTo(15f).Within(.01f));
            PlayerInputSystem.SetPointerGroundPositionResolverForTests(_ => Vector3.right * 2);
            try
            {
                InvokeUpdate(Game.Interface.GetSystem<PlayerInputSystem>());
                Assert.That(model.PreviewPosition, Is.EqualTo(Vector3.right * 2));
                Assert.That(model.PreviewRotation.eulerAngles.y, Is.EqualTo(15f).Within(.01f));
            }
            finally { PlayerInputSystem.SetPointerGroundPositionResolverForTests(null); }
        }

        [Test]
        public void MobileDemolitionShowsTargetNameAndDisablesConfirmationWithoutSelection()
        {
            Assert.That(Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(true), Is.True);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
            var id = TemporaryBuildingBehaviorTestSupport.Build("temp_roadblock_wood", Vector3.forward);
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            Assert.That(system.BeginDestroyMode(out var error), Is.True, error);
            var hud = Object.FindObjectOfType<TemporaryBuildingHudUI>();
            Assert.That(hud.BtnConfirm.interactable, Is.False, "无目标不能确认拆除");
            viewPort.PickedRuntimeId = id;
            system.RefreshDestroyTarget(new Ray(Vector3.up * 10, Vector3.down));
            Assert.That(system.GetHudSnapshot().SelectedBuildingName, Is.EqualTo("木质路障"));
            Assert.That(hud.StatusText.text, Does.Contain("木质路障"));
            Assert.That(hud.BtnConfirm.interactable, Is.True);
            Assert.That(viewPort.Buildings.ContainsKey(id), Is.True, "选择目标不能立即拆除");
            viewPort.PickedRuntimeId = 0;
            system.RefreshDestroyTarget(new Ray(Vector3.up * 10, Vector3.down));
            Assert.That(hud.BtnConfirm.interactable, Is.False);
            Assert.That(hud.StatusText.text, Does.Not.Contain("木质路障"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MobileTapSelectsDemolitionTargetWithAnotherFingerHeld(bool holdAnotherFinger)
        {
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
            var id = TemporaryBuildingBehaviorTestSupport.Build("temp_roadblock_wood", Vector3.forward);
            viewPort.PickedRuntimeId = id;
            var building = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            Assert.That(building.BeginDestroyMode(out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(true), Is.True);
            var camera = new PlayerCameraSystem(); Game.Interface.RegisterSystem(camera);
            typeof(PlayerCameraSystem).GetField("cameraPort", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(camera, new TouchTestCamera());
            typeof(PlayerCameraSystem).GetField("unitId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(camera, PlayerModel.LocalPlayerUnitId);
            typeof(PlayerCameraSystem).GetField("mapInstanceId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(camera, TemporaryBuildingBehaviorTestSupport.MapInstanceId);
            var device = InputSystem.AddDevice<Touchscreen>();
            try
            {
                if (holdAnotherFinger)
                {
                    SetTouch(device, 0, new Vector2(20, 20));
                }
                SetTouch(device, holdAnotherFinger ? 1 : 0, new Vector2(Screen.width * .25f, Screen.height * .75f));
                var tappedPosition = new Vector2(Screen.width * .25f, Screen.height * .75f);
                Assert.That(camera.TryCreateAimRay(tappedPosition, out _), Is.True, "相机夹具未准备好");
                Assert.That(UIHelper.HasUIOpen, Is.False, "非阻断建造 HUD 不应阻断选中输入");
                Assert.That((bool)typeof(PlayerInputSystem).GetMethod("TouchHitsUi", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { tappedPosition }), Is.False, "夹具触点落入 UI");
                Assert.That(building.Mode, Is.EqualTo(TemporaryBuildingMode.DestroyMode), "拆除模式在触摸前被重置");
                var testTouch = device.touches[holdAnotherFinger ? 1 : 0];
                Assert.That(testTouch.press.wasPressedThisFrame, Is.True, "触摸夹具未产生本帧按下：phase=" + testTouch.phase.ReadValue() + ", press=" + testTouch.press.ReadValue() + ", enabled=" + device.enabled + ", updated=" + device.wasUpdatedThisFrame);
                InvokeUpdate(Game.Interface.GetSystem<PlayerInputSystem>());
                Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().DestroyTargetBuildingId, Is.EqualTo(id), "本次新按下的场景触点必须选中目标，不能只读 primaryTouch");
                Assert.That(viewPort.HighlightedRuntimeId, Is.EqualTo(id));
            }
            finally { InputSystem.RemoveDevice(device); }
        }

        private sealed class TouchTestCamera : IPlayerCameraPort
        {
            public bool IsValid => true;
            public void ApplyProfile() { }
            public void LateUpdate() { }
            public bool TryGetCameraPose(out Vector3 position, out Vector3 up) { position = Vector3.up * 10; up = Vector3.forward; return true; }
            public bool TryGetScreenPointRay(Vector2 position, out Ray ray) { ray = new Ray(Vector3.up * 10, Vector3.down); return true; }
        }

        private static void SetTouch(Touchscreen device, int slot, Vector2 position)
        {
            // EditMode 不处理 Player 的触摸事件队列，直接更新真实 TouchControl 状态来验证消费链路。
            InputSystem.Update();
            var state = new TouchState { touchId = slot + 1, phase = UnityEngine.InputSystem.TouchPhase.Began, position = position };
            var held = new TouchState { touchId = 1, phase = UnityEngine.InputSystem.TouchPhase.Stationary, position = new Vector2(20, 20) };
            // 一次写入整帧，避免 Editor 对两次局部写入分别翻转前后缓冲区。
            InputState.Change(device, new TouchFrame { Primary = slot == 0 ? state : held, First = slot == 0 ? state : held, Second = slot == 1 ? state : default });
            // InputState.Change 的 Editor 路径不更新设备帧号，补齐设备事件在 Player 中设置的计数。
            typeof(InputDevice).GetField("m_CurrentUpdateStepCount", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(device, InputState.updateCount);
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct TouchFrame
        {
            public TouchState Primary;
            public TouchState First;
            public TouchState Second;
        }

        private static void InvokeUpdate(PlayerInputSystem system)
        {
            typeof(PlayerInputSystem).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(system, null);
        }

        private static PointerEventData CreatePointerEvent(Vector2 position, Vector2 scrollDelta = default)
        {
            var eventSystem = EventSystem.current ?? Object.FindObjectOfType<EventSystem>();
            Assert.That(eventSystem, Is.Not.Null);
            return new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                position = position,
                pressPosition = position,
                scrollDelta = scrollDelta
            };
        }

        private static void RebuildScrollLayout(ScrollRect scroll)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            Canvas.ForceUpdateCanvases();
        }

        private static Rect WorldRect(RectTransform transform)
        {
            var corners = new Vector3[4];
            transform.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private sealed class FakeInput : PlayerInputSystem.IPlayerInputReader
        {
            public Vector2 MoveInput { get; set; }
            public bool IsEnabled { get; private set; }
            public bool AttackPressedThisFrame { get; set; }
            public bool HolsterPressedThisFrame { get; set; }
            public bool MeleeSlotPressedThisFrame { get; set; }
            public bool RangedSlotPressedThisFrame { get; set; }
            public bool InteractPressedThisFrame { get; set; }
            public bool SprintHeld { get; set; }
            public bool BuildPressedThisFrame { get; set; }
            public bool DestroyPressedThisFrame { get; set; }
            public bool CancelPressedThisFrame { get; set; }
            public bool ReloadPressedThisFrame { get; set; }
            public void Enable() => IsEnabled = true;
            public void Disable() => IsEnabled = false;
            public void Dispose() { }
        }
    }
}
