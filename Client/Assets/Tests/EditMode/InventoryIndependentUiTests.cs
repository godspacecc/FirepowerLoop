using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class InventoryIndependentUiTests
    {
        private const long TestMapInstanceId = 44;

        [Test]
        public void WarehouseDetailsAndTransferButtonFollowSelectionAfterReopen()
        {
            var stock = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(stock.TryAdd(ItemIds.MetalParts, 8, 20, out _, out var error), Is.True, error);
            Assert.That(WarehouseUICtrl.TryShow(out error), Is.True, error);
            var ctrl = UIHelper.GetUICtrl<WarehouseUICtrl>();
            ctrl.Hide();
            Assert.That(WarehouseUICtrl.TryShow(out error), Is.True, error);
            var ui = GetView<WarehouseUI>(ctrl);
            var collector = ui.GetComponent<ReferenceCollector>();
            var button = collector.Get<Button>("BtnTake");
            Assert.That(button, Is.Not.Null, "仓库必须通过生成引用提供取出按钮");
            Assert.That(button.interactable, Is.False);
            Game.Interface.GetUtility<IItemTransferPort>().ClickSlot(new InventorySlotRef(InventoryContainerKind.Warehouse, 0));
            Assert.That(button.interactable, Is.True);
            Assert.That(collector.Get<TMPro.TextMeshProUGUI>("DetailAmountText").text, Does.Contain("8"));
            button.onClick.Invoke();
            Assert.That(stock.GetTotalAmount(ItemIds.MetalParts), Is.Zero);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(8));
            Assert.That(button.interactable, Is.False);
            Assert.That(collector.Get<TMPro.TextMeshProUGUI>("CapacityText").text, Does.Contain("0 / 40"));
        }

        private TestMapContentHandle _testMapHandle;
        private ActiveWorldPlayerPortProvider _playerRuntimePorts;
        private Texture2D _testIconTexture;
        private Sprite _testIconSprite;
        private GameObject _notificationPoolRoot;

        [Test]
        public void WarehouseStoreButtonMovesBagStackAndClearsSelection()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 6, 20, out _, out var error), Is.True, error);
            Assert.That(WarehouseUICtrl.TryShow(out error), Is.True, error);
            var ui = GetView<BagUI>(UIHelper.GetUICtrl<BagUICtrl>());
            var transfer = Game.Interface.GetUtility<IItemTransferPort>();
            transfer.ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag, 0));
            Assert.That(ui.WarehouseBtnStore.gameObject.activeInHierarchy && ui.WarehouseBtnStore.interactable, Is.True);
            ui.WarehouseBtnStore.onClick.Invoke();
            Assert.That(bag.GetTotalAmount(ItemIds.MetalParts), Is.Zero);
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(6));
            Assert.That(transfer.State.SelectedSlot.HasValue, Is.False);
        }

        [Test]
        public void WarehouseAmountTransferRejectsFullBagWithoutPartialMutation()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Cloth, bag.Capacity * 20, 20, out _, out _), Is.True);
            Assert.That(warehouse.TryAdd(ItemIds.MetalParts, 8, 20, out _, out _), Is.True);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryMoveCarriedStack(
                new InventorySlotRef(InventoryContainerKind.Warehouse, 0), InventoryContainerKind.PlayerBag, 8, out _), Is.False);
            Assert.That(warehouse.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(8));
            Assert.That(bag.GetTotalAmount(ItemIds.MetalParts), Is.Zero);
            Assert.That(bag.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(bag.Capacity * 20));
        }

        [SetUp]
        public void SetUp()
        {
            UIHelper.ResetForTests();
            UIRoot.ClearForTests();
            TestReset.ResetArchitecture();
            _testIconTexture = new Texture2D(2, 2);
            _testIconSprite = Sprite.Create(_testIconTexture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
            ResourceLoader.SetLoaderForTests(new InventoryLocalizationLoader(_testIconSprite));
            LocalizationLanguages.ResetForTests();
            _ = Game.Interface;
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(new InventoryConfigProvider());
            _playerRuntimePorts = new ActiveWorldPlayerPortProvider();
            Game.Interface.RegisterUtility<IPlayerRuntimePortProvider>(_playerRuntimePorts);
            CreateUiRoot();
            // 转移拒绝走正式浮动提示；测试场景也提供其必需的对象池根。
            _notificationPoolRoot = new GameObject(SceneRoot.PoolRootName);
            UIHelper.SetPrefabLoaderForTests(LoadPrefab);
            EnterSafeBaseSession();
        }

        [TearDown]
        public void TearDown()
        {
            Game.Interface.GetModel<MapSceneModel>().Clear();
            Game.Interface.GetSystem<UnitSystem>().Remove(PlayerModel.LocalPlayerUnitId, UnitRemovedReason.Explicit);
            _testMapHandle?.Destroy();
            _testMapHandle = null;
            _playerRuntimePorts = null;
            UIHelper.ResetForTests();
            GameObjectPoolHelper.ClearForTests();
            EditModeSceneIsolation.DestroyIfInActiveTestScene(_notificationPoolRoot);
            _notificationPoolRoot = null;
            UIRoot.ClearForTests();
            DestroyNamed("UIRoot");
            DestroyNamed("EventSystem");
            TestReset.ResetArchitecture();
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            Object.DestroyImmediate(_testIconSprite);
            Object.DestroyImmediate(_testIconTexture);
            _testIconSprite = null;
            _testIconTexture = null;
        }

        [Test]
        public void ThreeResourcesAreIndependentAndDeletedResourcesAreGone()
        {
            var contracts = new Dictionary<string, Type>
            {
                ["Bag"] = typeof(BagUI),
                ["Warehouse"] = typeof(WarehouseUI),
                ["SearchContainer"] = typeof(SearchContainerUI)
            };
            foreach (var contract in contracts)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/Prefabs/UI/Inventory/{contract.Key}.prefab");
                Assert.That(prefab, Is.Not.Null, contract.Key);
                Assert.That(prefab.GetComponents<ReferenceCollector>().Length, Is.EqualTo(1), contract.Key);
                Assert.That(prefab.GetComponents<BaseUI>().Length, Is.EqualTo(1), contract.Key);
                var view = prefab.GetComponent(contract.Value) as BaseUI;
                Assert.That(view, Is.Not.Null, contract.Key);
                foreach (var field in contract.Value.GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    Assert.That(field.GetValue(view), Is.Not.Null, $"{contract.Key}.{field.Name}");
                }

                Assert.That((view.GetType().GetField("BackgroundImage")?.GetValue(view) as Image)?.raycastTarget,
                    Is.True, contract.Key);
            }

            var legacyPath = "Assets/Game/Prefabs/UI/Inventory/" + "Inventory" + ".prefab";
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(legacyPath), Is.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/TransferBag.prefab"), Is.Null);
        }

        [Test]
        public void ManagementNavigationPreservesInventorySelectionAndScroll()
        {
            Assert.That(BagUICtrl.TryShow(out var error), Is.True, error);
            var ctrl = UIHelper.GetUICtrl<BagUICtrl>();
            var ui = GetView<BagUI>(ctrl);
            var model = Game.Interface.GetModel<PlayerBagModel>();
            model.Restore(new Newtonsoft.Json.Linq.JObject
            {
                ["capacity"] = 40, ["riskState"] = "BaseSafe", ["slots"] = new Newtonsoft.Json.Linq.JArray()
            });
            model.Pocket.TryAdd(ItemIds.MetalParts, 3, 20, out _, out _);
            var transfer = Game.Interface.GetUtility<IItemTransferPort>();
            transfer.ClickSlot(new InventorySlotRef(InventoryContainerKind.Pocket, 0));
            Canvas.ForceUpdateCanvases();
            ui.BagScrollRect.verticalNormalizedPosition = .35f;
            ui.BtnPageMap.onClick.Invoke();
            Assert.That(ctrl.CurrentPage, Is.EqualTo(BagManagementPage.Map));
            Assert.That(ui.PlaceholderRoot.activeSelf, Is.False);
            Assert.That(ui.MapPage.gameObject.activeSelf, Is.True);
            Assert.That(ui.InventoryPageRoot.activeSelf, Is.False);
            ui.BtnPageTasks.onClick.Invoke();
            Assert.That(ctrl.CurrentPage, Is.EqualTo(BagManagementPage.Tasks));
            Assert.That(ui.TasksPage.gameObject.activeSelf, Is.True);
            Assert.That(ui.MapPage.gameObject.activeSelf, Is.False);
            ui.BtnPageJournal.onClick.Invoke();
            Assert.That(ctrl.CurrentPage, Is.EqualTo(BagManagementPage.Journal));
            Assert.That(ui.JournalPage.gameObject.activeSelf, Is.True);
            Assert.That(ui.TasksPage.gameObject.activeSelf, Is.False);
            ui.BtnPageBackpack.onClick.Invoke();
            Canvas.ForceUpdateCanvases();
            Assert.That(ui.InventoryPageRoot.activeSelf, Is.True);
            Assert.That(ui.JournalPage.gameObject.activeSelf, Is.False);
            Assert.That(transfer.State.SelectedSlot.Value.ContainerKind, Is.EqualTo(InventoryContainerKind.Pocket));
            Assert.That(ui.FullDetailAmountText.text, Is.EqualTo("3"));
            Assert.That(ui.BagScrollRect.verticalNormalizedPosition, Is.EqualTo(.35f).Within(.01f));
            ctrl.Hide();
            Assert.That(BagUICtrl.TryShow(out error), Is.True, error);
            Assert.That(ctrl.CurrentPage, Is.EqualTo(BagManagementPage.Backpack));
        }

        [Test]
        public void ManagementTransferButtonMovesRequestedQuantityOnceAfterReopen()
        {
            Assert.That(BagUICtrl.TryShow(out var error), Is.True, error);
            var ctrl = UIHelper.GetUICtrl<BagUICtrl>();
            ctrl.Hide(); Assert.That(BagUICtrl.TryShow(out error), Is.True, error);
            var model = Game.Interface.GetModel<PlayerBagModel>();
            model.Container.TryAdd(ItemIds.MetalParts, 6, 20, out _, out _);
            var ui = GetView<BagUI>(ctrl);
            Game.Interface.GetUtility<IItemTransferPort>().ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag, 0));
            Assert.That(ui.BtnMove.interactable, Is.True);
            ui.BtnMove.onClick.Invoke();
            var references = ui.GetComponent<ReferenceCollector>();
            var dialog = references.Get<GameObject>("TransferQuantityRoot");
            Assert.That(dialog, Is.Not.Null, "数量选择必须由正式 Prefab 提供");
            Assert.That(dialog.activeSelf, Is.True);
            Assert.That(model.Pocket.GetTotalAmount(ItemIds.MetalParts), Is.Zero, "点击转移尚未确认时不能扣库存");
            for (var i = 0; i < 4; i++) references.Get<Button>("BtnTransferDecrease").onClick.Invoke();
            references.Get<Button>("BtnTransferConfirm").onClick.Invoke();
            references.Get<Button>("BtnTransferConfirm").onClick.Invoke();
            Assert.That(model.Pocket.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(2));
            Assert.That(model.Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(4));
            Assert.That(dialog.activeSelf, Is.False);
            Assert.That(ui.PocketList.totalCount, Is.EqualTo(4));
            Assert.That(ui.ManagementRoot.activeSelf, Is.True);
            Assert.That(ui.CompanionRoot.activeSelf, Is.False);
        }

        [Test]
        public void TransferQuantityCancelAndPageChangeNeverMoveItems()
        {
            Assert.That(BagUICtrl.TryShow(out var error), Is.True, error);
            var ctrl = UIHelper.GetUICtrl<BagUICtrl>();
            var ui = GetView<BagUI>(ctrl);
            var model = Game.Interface.GetModel<PlayerBagModel>();
            model.Container.TryAdd(ItemIds.MetalParts, 6, 20, out _, out _);
            Game.Interface.GetUtility<IItemTransferPort>().ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag, 0));
            var references = ui.GetComponent<ReferenceCollector>();
            ui.BtnMove.onClick.Invoke();
            var dialog = references.Get<GameObject>("TransferQuantityRoot");
            Assert.That(dialog, Is.Not.Null);
            references.Get<Button>("BtnTransferCancel").onClick.Invoke();
            Assert.That(dialog.activeSelf, Is.False);
            references.Get<Button>("BtnTransferConfirm").onClick.Invoke();
            ui.BtnMove.onClick.Invoke();
            ctrl.SelectPage(BagManagementPage.Status);
            Assert.That(dialog.activeSelf, Is.False);
            references.Get<Button>("BtnTransferConfirm").onClick.Invoke();
            Assert.That(model.Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(6));
            Assert.That(model.Pocket.GetTotalAmount(ItemIds.MetalParts), Is.Zero);
        }

        [Test]
        public void TransferSingleItemSkipsQuantityAndPocketCanMoveBack()
        {
            Assert.That(BagUICtrl.TryShow(out var error), Is.True, error);
            var ui = GetView<BagUI>(UIHelper.GetUICtrl<BagUICtrl>());
            var model = Game.Interface.GetModel<PlayerBagModel>();
            model.Container.TryAdd(ItemIds.MetalParts, 1, 20, out _, out _);
            var port = Game.Interface.GetUtility<IItemTransferPort>();
            port.ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag, 0));
            ui.BtnMove.onClick.Invoke();
            Assert.That(model.Pocket.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(1));
            var dialog = ui.GetComponent<ReferenceCollector>().Get<GameObject>("TransferQuantityRoot");
            Assert.That(dialog, Is.Not.Null);
            Assert.That(dialog.activeSelf, Is.False);
            port.ClickSlot(new InventorySlotRef(InventoryContainerKind.Pocket, 0));
            ui.BtnMove.onClick.Invoke();
            Assert.That(model.Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(1));
            Assert.That(model.Pocket.GetTotalAmount(ItemIds.MetalParts), Is.Zero);
        }

        [Test]
        public void TransferQuantityRejectsReplacementOfSameItemInSameSlot()
        {
            Assert.That(BagUICtrl.TryShow(out var error), Is.True, error);
            var ui = GetView<BagUI>(UIHelper.GetUICtrl<BagUICtrl>());
            var model = Game.Interface.GetModel<PlayerBagModel>();
            model.Container.TryAdd(ItemIds.MetalParts, 6, 20, out _, out _);
            Game.Interface.GetUtility<IItemTransferPort>().ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag, 0));
            ui.BtnMove.onClick.Invoke();
            var confirm = ui.GetComponent<ReferenceCollector>().Get<Button>("BtnTransferConfirm");
            Assert.That(confirm, Is.Not.Null);
            Assert.That(model.Container.TryRemove(0, 6, out _, out _), Is.True);
            model.Container.TryAdd(ItemIds.MetalParts, 6, 20, out _, out _);
            confirm.onClick.Invoke();
            Assert.That(model.Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(6));
            Assert.That(model.Pocket.GetTotalAmount(ItemIds.MetalParts), Is.Zero);
        }

        [Test]
        public void TransferQuantityFullDestinationKeepsSourceAndHideClearsPendingRequest()
        {
            Assert.That(BagUICtrl.TryShow(out var error), Is.True, error);
            var ctrl = UIHelper.GetUICtrl<BagUICtrl>();
            var ui = GetView<BagUI>(ctrl);
            var model = Game.Interface.GetModel<PlayerBagModel>();
            model.Container.TryAdd(ItemIds.MetalParts, 6, 20, out _, out _);
            model.Pocket.TryAdd(ItemIds.Cloth, 80, 20, out _, out _);
            Game.Interface.GetUtility<IItemTransferPort>().ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag, 0));
            ui.BtnMove.onClick.Invoke();
            var references = ui.GetComponent<ReferenceCollector>();
            var confirm = references.Get<Button>("BtnTransferConfirm");
            Assert.That(confirm, Is.Not.Null);
            confirm.onClick.Invoke();
            Assert.That(model.Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(6));
            Assert.That(model.Pocket.GetTotalAmount(ItemIds.MetalParts), Is.Zero);
            ui.BtnMove.onClick.Invoke();
            ctrl.Hide();
            Assert.That(BagUICtrl.TryShow(out error), Is.True, error);
            Assert.That(references.Get<GameObject>("TransferQuantityRoot").activeSelf, Is.False);
            confirm.onClick.Invoke();
            Assert.That(model.Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(6));
        }

        [Test]
        public void ManagementPrefabHasResolvedGeneratedReferencesAndSixTabs()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Bag.prefab");
            var ui = root.GetComponent<BagUI>();
            foreach (var field in typeof(BagUI).GetFields())
                if (typeof(Object).IsAssignableFrom(field.FieldType)) Assert.That((Object)field.GetValue(ui), Is.Not.Null, field.Name);
            Assert.That(ui.ManagementRoot.transform.Find("Navigation").childCount, Is.EqualTo(6));
            Assert.That(ui.PocketList.itemPrefab, Is.SameAs(ui.FullPlayerBagList.itemPrefab));
            Assert.That(PrefabUtility.IsPartOfPrefabAsset(ui.PocketList.itemPrefab), Is.True);
            Assert.That(ui.PocketList.GetComponent<GridLayoutGroup>().constraintCount, Is.EqualTo(4));
            Assert.That(ui.BagScrollRect.horizontal, Is.False);
        }

        [Test]
        public void YooAssetRequiredInventoryPrefabsExcludeTransferBag()
        {
            Type utilityType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                utilityType = assembly.GetType("Game.Editor.YooAssetBuildUtility", false);
                if (utilityType != null)
                {
                    break;
                }
            }

            Assert.That(utilityType, Is.Not.Null, "未加载 Game.Editor.YooAssetBuildUtility 所在的 Editor 程序集。");
            var property = utilityType.GetProperty("RuntimePrefabLocations", BindingFlags.Public | BindingFlags.Static);
            Assert.That(property, Is.Not.Null);
            var locations = property.GetValue(null) as IEnumerable<string>;
            Assert.That(locations, Is.Not.Null);

            CollectionAssert.Contains(locations, "Prefabs/UI/Inventory/Bag");
            CollectionAssert.Contains(locations, "Prefabs/UI/Inventory/Warehouse");
            CollectionAssert.Contains(locations, "Prefabs/UI/Inventory/SearchContainer");
            CollectionAssert.DoesNotContain(locations, "Prefabs/UI/Inventory/TransferBag");
        }

        [Test]
        public void WarehousePrefabKeepsEightByFiveViewportAndSeparateCompanionPanels()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Warehouse.prefab");
            var ui = prefab.GetComponent<WarehouseUI>();
            var root = prefab.GetComponent<RectTransform>();
            var panel = prefab.transform.Find("WarehousePanel")?.GetComponent<RectTransform>();
            var close = prefab.transform.Find("BtnClose")?.GetComponent<Button>();
            var title = panel?.Find("WarehouseTitleText");
            var scrollRect = panel?.Find("WarehouseScrollRect")?.GetComponent<RectTransform>();
            var slotRoot = panel?.Find("WarehouseScrollRect/Viewport/WarehouseSlotRoot");

            Assert.That(prefab, Is.Not.Null);
            Assert.That(ui, Is.Not.Null);
            Assert.That(root.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(root.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(root.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(root.sizeDelta, Is.EqualTo(Vector2.zero));
            Assert.That(ui.BackgroundImage.color.a, Is.GreaterThanOrEqualTo(.95f));
            Assert.That(ui.BackgroundImage.raycastTarget, Is.True);
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.parent, Is.SameAs(prefab.transform));
            Assert.That(panel.anchorMin.x, Is.GreaterThan(0));
            Assert.That(panel.anchorMin.y, Is.Zero);
            Assert.That(panel.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(panel.pivot, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(close, Is.Not.Null);
            Assert.That(title, Is.Not.Null);
            Assert.That(title.GetComponent<TMPro.TMP_Text>().text, Is.EqualTo("仓库"));
            Assert.That(title.GetComponent<LocalizedText>().Key, Is.EqualTo("ui.inventory.warehouse_column"));
            var grid = slotRoot.GetComponent<GridLayoutGroup>();
            Assert.That(scrollRect.sizeDelta.y, Is.EqualTo(grid.cellSize.y * 5 + grid.spacing.y * 4));
            Assert.That(grid.constraintCount, Is.EqualTo(8));
            Assert.That(new SerializedObject(ui.WarehouseList).FindProperty("itemPrefab").objectReferenceValue,
                Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Box/WarehouseInventorySlotBox.prefab")));
            Assert.That(typeof(WarehouseUI).GetField("TitleText"), Is.Null);
            foreach (var reference in prefab.GetComponent<ReferenceCollector>().data)
                Assert.That(reference.gameObject, Is.Not.Null, reference.key);

            foreach (var item in prefab.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(item.name, Is.Not.EqualTo("BagAndWarehouseRoot"));
                Assert.That(item.name, Is.Not.EqualTo("PlayerBagPanel"));
                Assert.That(item.name, Is.Not.EqualTo("EquipmentPanel"));
                Assert.That(item.name, Is.Not.EqualTo("DetailPanel"));
            }

            foreach (var size in new[] { new Vector2(1280f, 720f), new Vector2(1600f, 900f), new Vector2(1920f, 1080f) })
            {
                var canvas = new GameObject("StandaloneLayout", typeof(RectTransform), typeof(Canvas));
                var canvasRect = canvas.GetComponent<RectTransform>();
                canvasRect.sizeDelta = new Vector2(1920f, 1080f);
                canvasRect.pivot = Vector2.zero;
                canvasRect.anchoredPosition = Vector2.zero;
                canvas.transform.localScale = Vector3.one * (size.x / 1920f);
                var warehouse = Object.Instantiate(prefab, canvas.transform, false);
                var bag = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Bag.prefab"), canvas.transform, false);
                Canvas.ForceUpdateCanvases();
                var warehouseCorners = new Vector3[4];
                var bagCorners = new Vector3[4];
                warehouse.transform.Find("WarehousePanel").GetComponent<RectTransform>().GetWorldCorners(warehouseCorners);
                bag.transform.Find("WarehouseCompanionRoot").GetComponent<RectTransform>().GetWorldCorners(bagCorners);
                Assert.That(bagCorners[2].x, Is.LessThanOrEqualTo(warehouseCorners[0].x), size.ToString());
                Assert.That(bagCorners[2].y, Is.EqualTo(warehouseCorners[2].y).Within(0.01f));
                if (size == new Vector2(1920f, 1080f))
                {
                    Assert.That(bagCorners[0].x, Is.GreaterThanOrEqualTo(0));
                    Assert.That(warehouseCorners[2].x, Is.LessThanOrEqualTo(size.x));
                    Assert.That(warehouseCorners[0].x - bagCorners[2].x, Is.GreaterThanOrEqualTo(16f));
                    Assert.That(bagCorners[0].x, Is.LessThanOrEqualTo(40f));
                    Assert.That(size.x - warehouseCorners[2].x, Is.LessThanOrEqualTo(40f));
                    Assert.That(warehouse.GetComponent<WarehouseUI>().WarehouseScrollRect.GetComponent<RectTransform>().rect.width,
                        Is.GreaterThanOrEqualTo(grid.cellSize.x * 8 + grid.spacing.x * 7));
                }

                Object.DestroyImmediate(canvas);
            }
        }

        [Test]
        public void WarehouseInteractionOpensWarehouseAndBagTogether()
        {
            Assert.That(WarehouseUICtrl.TryShow(out var error), Is.True, error);
            Assert.That(UIHelper.GetUICtrl<WarehouseUICtrl>().IsShow(), Is.True);
            var bag = UIHelper.GetUICtrl<BagUICtrl>();
            Assert.That(bag, Is.Not.Null);
            Assert.That(bag.IsShow(), Is.True);

            var view = GetView<BagUI>(bag);
            Assert.That(view.transform.Find("BagOnlyRoot/BagOnlyHotKeyText"), Is.Null);
            Assert.That(view.WarehouseDetailRoot.activeSelf, Is.False);
            var bagModel = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bagModel.TryAdd(ItemIds.MetalParts, 3, 20, out _, out error), Is.True, error);
            var transfer = Game.Interface.GetUtility<IItemTransferPort>();
            transfer.ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag, 0));
            Assert.That(view.WarehouseDetailRoot.activeSelf, Is.True);
            Assert.That(view.WarehouseDetailAmountText.text, Is.EqualTo("3"));
            transfer.ClearSelection();
            Assert.That(view.WarehouseDetailRoot.activeSelf, Is.False);
            transfer.ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag, 1));
            Assert.That(view.WarehouseDetailRoot.activeSelf, Is.False);
            transfer.ClickEquipment(WeaponSlot.Melee);
            Assert.That(view.WarehouseDetailRoot.activeSelf, Is.False);
            Game.Interface.GetModel<WarehouseModel>().Container.TryAdd(ItemIds.Cloth, 2, 20, out _, out _);
            transfer.ClickSlot(new InventorySlotRef(InventoryContainerKind.Warehouse, 0));
            Assert.That(view.WarehouseDetailRoot.activeSelf, Is.False);
            Assert.That(GetView<WarehouseUI>(UIHelper.GetUICtrl<WarehouseUICtrl>()).DetailRoot.activeSelf, Is.True);

            Assert.That(Game.Interface.GetSystem<HotKeySystem>().ExecuteBinding(HotKeyDefaults.Tab), Is.True);
            Assert.That(UIHelper.GetUICtrl<BagUICtrl>(), Is.SameAs(bag));
            Assert.That(bag.IsShow(), Is.False);
            Assert.That(UIHelper.GetUICtrl<WarehouseUICtrl>().IsShow(), Is.False);

            Assert.That(Game.Interface.GetSystem<HotKeySystem>().ExecuteBinding(HotKeyDefaults.Tab), Is.True);
            Assert.That(UIHelper.GetUICtrl<BagUICtrl>(), Is.SameAs(bag));
            Assert.That(bag.IsShow(), Is.True);
            Assert.That(UIHelper.GetUICtrl<WarehouseUICtrl>().IsShow(), Is.False);
        }

        [Test]
        public void RealSlotPointerEventsTransferBothDirectionsAcrossWindows()
        {
            var bagModel = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouseModel = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(bagModel.TryAdd(ItemIds.MetalParts, 3, 20, out _, out var error), Is.True, error);
            Assert.That(WarehouseUICtrl.TryShow(out error), Is.True, error);
            Assert.That(BagUICtrl.TryShow(out error), Is.True, error);
            var bag = UIHelper.GetUICtrl<BagUICtrl>();
            var warehouse = UIHelper.GetUICtrl<WarehouseUICtrl>();
            var eventData = new PointerEventData(EventSystem.current) { position = new Vector2(240f, 180f) };

            ExecuteEvents.Execute(bag.PlayerBagSlots[0].ClickButton.gameObject, eventData, ExecuteEvents.beginDragHandler);
            ExecuteEvents.Execute(bag.PlayerBagSlots[0].ClickButton.gameObject, eventData, ExecuteEvents.dragHandler);
            AssertDragVisual(bag.PlayerBagSlots[0]);
            ExecuteEvents.Execute(warehouse.WarehouseSlots[0].ClickButton.gameObject, eventData, ExecuteEvents.dropHandler);
            ExecuteEvents.Execute(bag.PlayerBagSlots[0].ClickButton.gameObject, eventData, ExecuteEvents.endDragHandler);
            Assert.That(GetDragVisual(), Is.Null);
            Assert.That(bagModel.GetTotalAmount(ItemIds.MetalParts), Is.Zero);
            Assert.That(warehouseModel.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(3));

            ExecuteEvents.Execute(warehouse.WarehouseSlots[0].ClickButton.gameObject, eventData, ExecuteEvents.beginDragHandler);
            eventData.position = new Vector2(400f, 320f);
            ExecuteEvents.Execute(warehouse.WarehouseSlots[0].ClickButton.gameObject, eventData, ExecuteEvents.dragHandler);
            AssertDragVisual(warehouse.WarehouseSlots[0]);
            ExecuteEvents.Execute(bag.PlayerBagSlots[1].ClickButton.gameObject, eventData, ExecuteEvents.dropHandler);
            ExecuteEvents.Execute(warehouse.WarehouseSlots[0].ClickButton.gameObject, eventData, ExecuteEvents.endDragHandler);
            Assert.That(GetDragVisual(), Is.Null);
            Assert.That(bagModel.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(3));
            Assert.That(warehouseModel.GetTotalAmount(ItemIds.MetalParts), Is.Zero);
        }

        [Test]
        public void PointerDragShowsFollowerAndCleansBeforeCrossWindowDrop()
        {
            var bagModel = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bagModel.TryAdd(ItemIds.MetalParts, 4, 20, out _, out var error), Is.True, error);
            Assert.That(WarehouseUICtrl.TryShow(out error), Is.True, error);
            Assert.That(BagUICtrl.TryShow(out error), Is.True, error);
            var bag = UIHelper.GetUICtrl<BagUICtrl>();
            var warehouse = UIHelper.GetUICtrl<WarehouseUICtrl>();
            var data = new PointerEventData(EventSystem.current) { position = new Vector2(320f, 240f) };

            var source = bag.PlayerBagSlots[0];
            ExecuteEvents.Execute(source.ClickButton.gameObject, data, ExecuteEvents.beginDragHandler);
            var visual = AssertDragVisual(source);
            var initialPosition = visual.GetComponent<RectTransform>().anchoredPosition;
            Assert.That(source.AmountText.gameObject.activeSelf, Is.True);
            Assert.That(source.AmountText.text, Is.EqualTo("4"));
            Assert.That(source.DragFeedback.activeSelf, Is.True);
            data.position = new Vector2(520f, 360f);
            ExecuteEvents.Execute(source.ClickButton.gameObject, data, ExecuteEvents.dragHandler);
            var movedPosition = visual.GetComponent<RectTransform>().anchoredPosition;
            Assert.That(movedPosition, Is.Not.EqualTo(initialPosition));
            Game.Interface.GetUtility<IItemTransferPort>().UpdateDragVisual(null);
            Assert.That(visual.GetComponent<RectTransform>().anchoredPosition, Is.EqualTo(movedPosition));
            ExecuteEvents.Execute(bag.PlayerBagSlots[1].ClickButton.gameObject, data, ExecuteEvents.dropHandler);
            Assert.That(GetDragVisual(), Is.Null, "Drop 返回前必须清理视觉。");
            ExecuteEvents.Execute(source.ClickButton.gameObject, data, ExecuteEvents.endDragHandler);
            Assert.That(bagModel.TryGet(1, out var moved), Is.True);
            Assert.That(moved.Amount, Is.EqualTo(4));

            source = bag.PlayerBagSlots[1];
            ExecuteEvents.Execute(source.ClickButton.gameObject, data, ExecuteEvents.beginDragHandler);
            ExecuteEvents.Execute(source.ClickButton.gameObject, data, ExecuteEvents.dragHandler);
            AssertDragVisual(source);
            ExecuteEvents.Execute(warehouse.WarehouseSlots[0].ClickButton.gameObject, data, ExecuteEvents.dropHandler);
            Assert.That(GetDragVisual(), Is.Null);
            ExecuteEvents.Execute(source.ClickButton.gameObject, data, ExecuteEvents.endDragHandler);
            Assert.That(bagModel.GetTotalAmount(ItemIds.MetalParts), Is.Zero);
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(4));
        }

        [Test]
        public void PointerDragFromWarehouseEntersBagBeforeTakingOut()
        {
            var warehouseModel = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(warehouseModel.TryAdd(ItemIds.ShortSword, 1, 1, out _, out var error), Is.True, error);
            Assert.That(WarehouseUICtrl.TryShow(out error), Is.True, error);
            Assert.That(BagUICtrl.TryShow(out error), Is.True, error);
            var warehouse = UIHelper.GetUICtrl<WarehouseUICtrl>();
            var bag = UIHelper.GetUICtrl<BagUICtrl>();
            var bagView = GetView<BagUI>(bag);
            var source = warehouse.WarehouseSlots[0];
            var data = new PointerEventData(EventSystem.current) { position = new Vector2(360f, 240f) };

            ExecuteEvents.Execute(source.ClickButton.gameObject, data, ExecuteEvents.beginDragHandler);
            ExecuteEvents.Execute(source.ClickButton.gameObject, data, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(bag.PlayerBagSlots[0].ClickButton.gameObject, data, ExecuteEvents.dropHandler);
            ExecuteEvents.Execute(source.ClickButton.gameObject, data, ExecuteEvents.endDragHandler);

            Assert.That(GetDragVisual(), Is.Null);
            Assert.That(warehouseModel.GetTotalAmount(ItemIds.ShortSword), Is.Zero);
            var transfer = Game.Interface.GetUtility<IItemTransferPort>();
            transfer.ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag, 0)); transfer.UseSelected();
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.ShortSword), Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().MeleeWeaponItemId,
                Is.EqualTo(ItemIds.ShortSword));
        }

        [Test]
        public void MissingVisualInputKeepsLogicalDragAndDropWorking()
        {
            var bagModel = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bagModel.TryAdd(ItemIds.MetalParts, 2, 20, out _, out var error), Is.True, error);
            Assert.That(BagUICtrl.TryShow(out error), Is.True, error);
            var bag = UIHelper.GetUICtrl<BagUICtrl>();
            var source = bag.PlayerBagSlots[0];
            var transfer = Game.Interface.GetUtility<IItemTransferPort>();
            Assert.That(transfer.BeginDrag(new InventorySlotRef(InventoryContainerKind.PlayerBag, 0)), Is.True);
            Assert.That(transfer.TryShowDragVisual(source.IconImage, null), Is.False);
            Assert.That(source.IconImage.enabled, Is.True);
            Assert.That(source.IconImage.sprite, Is.SameAs(_testIconSprite));
            Assert.That(GetDragVisual(), Is.Null);

            var data = new PointerEventData(EventSystem.current);
            ExecuteEvents.Execute(bag.PlayerBagSlots[1].ClickButton.gameObject, data, ExecuteEvents.dropHandler);
            ExecuteEvents.Execute(source.ClickButton.gameObject, data, ExecuteEvents.endDragHandler);
            Assert.That(bagModel.TryGet(1, out var moved), Is.True);
            Assert.That(moved.Amount, Is.EqualTo(2));
        }

        [Test]
        public void DragCleanupHandlesHideDestroyInvalidateAndRepeatedEnd()
        {
            var bagModel = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bagModel.TryAdd(ItemIds.MetalParts, 3, 20, out _, out var error), Is.True, error);
            Assert.That(WarehouseUICtrl.TryShow(out error), Is.True, error);
            Assert.That(BagUICtrl.TryShow(out error), Is.True, error);
            var transfer = Game.Interface.GetUtility<IItemTransferPort>();
            var bag = UIHelper.GetUICtrl<BagUICtrl>();

            var source = bag.PlayerBagSlots[0];
            BeginRealDrag(source);
            UIHelper.GetUICtrl<WarehouseUICtrl>().Hide();
            Assert.That(bag.IsShow(), Is.False, "退出全屏仓库同时关闭伴随背包。");
            AssertCleanDrag(transfer, source.IconImage);
            transfer.EndDrag();
            transfer.EndDrag();
            AssertCleanDrag(transfer, source.IconImage);

            Assert.That(WarehouseUICtrl.TryShow(out error), Is.True, error);
            source = bag.PlayerBagSlots[0];
            BeginRealDrag(source);
            bag.Hide();
            AssertCleanDrag(transfer, source.IconImage);

            Assert.That(BagUICtrl.TryShow(out error), Is.True, error);
            source = bag.PlayerBagSlots[0];
            BeginRealDrag(source);
            transfer.InvalidateContainer(InventoryContainerKind.PlayerBag);
            AssertCleanDrag(transfer, source.IconImage);

            source = bag.PlayerBagSlots[0];
            BeginRealDrag(source);
            Object.DestroyImmediate(GetView<BagUI>(bag).gameObject);
            Assert.That(BagUICtrl.TryShow(out error), Is.True, error);
            Assert.That(transfer.State.DragSource, Is.Null);
            Assert.That(GetDragVisual(), Is.Null);

            var independent = new ItemTransferCtrl(Game.Interface);
            var reloadedSource = UIHelper.GetUICtrl<BagUICtrl>().PlayerBagSlots[0];
            Assert.That(independent.BeginDrag(new InventorySlotRef(InventoryContainerKind.PlayerBag, 0)), Is.True);
            Assert.That(independent.TryShowDragVisual(
                reloadedSource.IconImage,
                new PointerEventData(EventSystem.current) { position = new Vector2(280f, 200f) }), Is.True);
            independent.Dispose();
            independent.Dispose();
            Assert.That(reloadedSource.IconImage.enabled, Is.True);
            Assert.That(GetDragVisual(), Is.Null);
        }

        private static void BeginRealDrag(InventorySlotBoxUI source)
        {
            var data = new PointerEventData(EventSystem.current) { position = new Vector2(260f, 190f) };
            ExecuteEvents.Execute(source.ClickButton.gameObject, data, ExecuteEvents.beginDragHandler);
            ExecuteEvents.Execute(source.ClickButton.gameObject, data, ExecuteEvents.dragHandler);
            AssertDragVisual(source);
        }

        private static void AssertCleanDrag(IItemTransferPort transfer, Image sourceImage)
        {
            Assert.That(transfer.State.DragSource, Is.Null);
            Assert.That(sourceImage.enabled, Is.True);
            Assert.That(GetDragVisual(), Is.Null);
        }

        [Test]
        public void BagCellRefreshUsesTheCurrentRefreshValueSnapshot()
        {
            var bagModel = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bagModel.TryAdd(ItemIds.MetalParts, 3, 20, out _, out var error), Is.True, error);
            Assert.That(BagUICtrl.TryShow(out error), Is.True, error);

            var bag = UIHelper.GetUICtrl<BagUICtrl>();
            var slot = bag.PlayerBagSlots[0];
            Assert.That(slot.AmountText.text, Is.EqualTo("3"));
            Assert.That(bagModel.TryRemove(0, 3, out _, out error), Is.True, error);

            var refreshSlotCell = typeof(PlayerBagWindowUICtrl)
                .GetMethod("RefreshSlotCell", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(refreshSlotCell, Is.Not.Null);
            refreshSlotCell.Invoke(bag, new object[] { slot.transform, 0 });

            Assert.That(slot.AmountText.text, Is.EqualTo("3"),
                "单格复用必须只读取 RefreshSlots 建立的本轮值快照。");
        }

        [Test]
        public void WarehousePanelsRemainInteractiveWithSingleBlockingBackground()
        {
            Assert.That(WarehouseUICtrl.TryShow(out var error), Is.True, error);
            Assert.That(BagUICtrl.TryShow(out error), Is.True, error);
            var warehouseUi = GetView<WarehouseUI>(UIHelper.GetUICtrl<WarehouseUICtrl>());
            var bagUi = GetView<BagUI>(UIHelper.GetUICtrl<BagUICtrl>());

            Assert.That(warehouseUi.BackgroundImage.raycastTarget, Is.True);
            Assert.That(bagUi.BackgroundImage.raycastTarget, Is.False);
            Assert.That(warehouseUi.WarehouseScrollRect.enabled, Is.True);
            Assert.That(bagUi.WarehousePlayerBagList.gameObject.activeInHierarchy, Is.True);

            UIHelper.HideUI<BagUICtrl>();
            Assert.That(UIHelper.GetUICtrl<WarehouseUICtrl>().IsShow(), Is.False);
            Assert.That(UIHelper.HasUIOpen, Is.False);
        }

        [Test]
        public void WarehouseCloseAndEscapeExitTheWholePageOnce()
        {
            Assert.That(WarehouseUICtrl.TryShow(out var error), Is.True, error);
            Assert.That(BagUICtrl.TryShow(out error), Is.True, error);
            GetView<WarehouseUI>(UIHelper.GetUICtrl<WarehouseUICtrl>()).BtnClose.onClick.Invoke();
            Assert.That(UIHelper.GetUICtrl<WarehouseUICtrl>().IsShow(), Is.False);
            Assert.That(UIHelper.GetUICtrl<BagUICtrl>().IsShow(), Is.False);

            UIHelper.GetUICtrl<BagUICtrl>().Hide();
            Assert.That(WarehouseUICtrl.TryShow(out error), Is.True, error);
            Assert.That(BagUICtrl.TryShow(out error), Is.True, error);
            var hotKeys = Game.Interface.GetSystem<HotKeySystem>();
            Assert.That(hotKeys.ExecuteBinding(HotKeyDefaults.Escape), Is.True);
            Assert.That(UIHelper.GetUICtrl<BagUICtrl>().IsShow(), Is.False);
            Assert.That(UIHelper.GetUICtrl<WarehouseUICtrl>().IsShow(), Is.False);
            Assert.That(UIHelper.HasUIOpen, Is.False);
        }

        [Test]
        public void FailedCompanionBagLoadKeepsWarehouseInteractive()
        {
            UIHelper.SetPrefabLoaderForTests(path => path.EndsWith("Bag", StringComparison.Ordinal)
                ? null
                : LoadPrefab(path));

            LogAssert.Expect(LogType.Error, "[BaseUICtrl] UI Prefab 未找到：Prefabs/UI/Inventory/Bag");
            Assert.That(WarehouseUICtrl.TryShow(out var error), Is.True, error);
            var warehouse = UIHelper.GetUICtrl<WarehouseUICtrl>();
            var bag = UIHelper.GetUICtrl<BagUICtrl>();
            Assert.That(bag == null || !bag.IsShow(), Is.True);
            Assert.That(warehouse.IsShow(), Is.True);
            Assert.That(GetView<WarehouseUI>(warehouse).BackgroundImage.raycastTarget, Is.True);
        }

        [Test]
        public void DestroyAndReloadDoesNotLetOldInstanceChangeNewBackground()
        {
            Assert.That(WarehouseUICtrl.TryShow(out var error), Is.True, error);
            var ctrl = UIHelper.GetUICtrl<WarehouseUICtrl>();
            var oldView = GetView<WarehouseUI>(ctrl);
            Object.DestroyImmediate(oldView.gameObject);
            Assert.That(ctrl.IsShow(), Is.False);

            Assert.That(WarehouseUICtrl.TryShow(out error), Is.True, error);
            var newView = GetView<WarehouseUI>(ctrl);
            Assert.That(newView, Is.Not.SameAs(oldView));
            Assert.That(newView.BackgroundImage.raycastTarget, Is.True);
            Assert.That(ctrl.IsShow(), Is.True);
        }

        [Test]
        public void MobileInventoryButton_OpensInBaseAndExpeditionAndRestoresControlsAfterClose()
        {
            UIHelper.SetPrefabLoaderForTests(path => path.EndsWith("Player/MobileJoystick")
                ? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab")
                : LoadPrefab(path));
            var mobileInput = Game.Interface.GetSystem<MobileInputSystem>();
            Assert.That(mobileInput.TrySetEditorSimulation(true), Is.True);
            var hud = UIHelper.ShowUI<MobileJoystickUICtrl>();
            var view = GetView<MobileJoystickUI>(hud);

            foreach (var expedition in new[] { false, true })
            {
                if (expedition) EnterExpeditionSession();
                LifeCycle.Update();
                Assert.That(view.InventoryButton.gameObject.activeInHierarchy, Is.True);
                Assert.That(view.ContentVisibility.blocksRaycasts, Is.True);
                view.InventoryButton.onClick.Invoke();
                var bag = UIHelper.GetUICtrl<BagUICtrl>();
                Assert.That(bag.IsShow(), Is.True, "触屏按钮应打开正式背包窗口");
                Assert.That(UIHelper.HasUIOpen, Is.True);
                Assert.That(mobileInput.CanAcceptMovement(), Is.False);
                Assert.That(mobileInput.CanAcceptShoulderControls(), Is.False);
                Assert.That(view.ContentVisibility.blocksRaycasts, Is.True);
                Assert.That(view.ContentVisibility.alpha, Is.EqualTo(1f));
                Assert.That(view.InventoryButton.gameObject.activeInHierarchy, Is.True);
                UIHelper.ShowUI<MobileJoystickUICtrl>();
                Assert.That(view.transform.parent, Is.SameAs(UIRoot.Instance.GetLayerObj(UILayer.HUD)));
                Assert.That(view.transform.parent.GetSiblingIndex(), Is.LessThan(GetView<BagUI>(bag).transform.parent.GetSiblingIndex()), "HUD 重新移顶仍不能盖过背包");
                GetView<BagUI>(bag).BtnClose.onClick.Invoke();
                Assert.That(UIHelper.HasUIOpen, Is.False);
                Assert.That(mobileInput.CanAcceptMovement(), Is.True);
                Assert.That(view.InventoryButton.gameObject.activeInHierarchy, Is.True);
                Assert.That(view.ContentVisibility.blocksRaycasts, Is.True);
            }

            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Paused;
            view.InventoryButton.onClick.Invoke();
            Assert.That(UIHelper.GetUICtrl<BagUICtrl>().IsShow(), Is.False, "暂停后的过期点击不能打开背包");
            Assert.That(view.InventoryButton.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void OpenRulesRejectInvalidStatesAndAllowSafeBaseAndLivingExpedition()
        {
            Assert.That(InventoryOpenRules.CanOpenPlayerBag(out var error), Is.True, error);
            Game.Interface.GetModel<MapSceneModel>().Clear();
            Assert.That(InventoryOpenRules.CanOpenPlayerBag(out _), Is.False);

            EnterSafeBaseSession();
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Paused;
            Assert.That(InventoryOpenRules.CanOpenPlayerBag(out _), Is.False);
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.InExpedition);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(0);
            Assert.That(InventoryOpenRules.CanOpenPlayerBag(out _), Is.False);

            EnterExpeditionSession();
            Assert.That(InventoryOpenRules.CanOpenPlayerBag(out error), Is.True, error);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Assert.That(InventoryOpenRules.CanOpenPlayerBag(out _), Is.False);
            EnterSafeBaseSession();
            Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.RecoveryRequired);
            Assert.That(InventoryOpenRules.CanOpenPlayerBag(out _), Is.False);
        }

        [Test]
        public void ExpeditionBagStaysOpenAfterNonLethalDamageAndClosesAfterLethalDamage()
        {
            EnterExpeditionSession();
            Assert.That(BagUICtrl.TryShow(out var error), Is.True, error);
            var bag = UIHelper.GetUICtrl<BagUICtrl>();

            Game.Interface.GetModel<CombatModel>().SetPlayerHP(40);
            Game.Interface.SendEvent(new CombatDamageAppliedEvent(99, PlayerModel.LocalPlayerUnitId, 10));
            Assert.That(bag.IsShow(), Is.True);

            Game.Interface.GetModel<CombatModel>().SetPlayerHP(0);
            Game.Interface.SendEvent(new CombatDamageAppliedEvent(99, PlayerModel.LocalPlayerUnitId, 40));
            Assert.That(bag.IsShow(), Is.False);
        }

        [UnityTest]
        public IEnumerator SearchContainer_OpenRefreshAndTransferRemainAvailable()
        {
            const int corpseId = 901;
            var tempSaveRoot = Path.Combine(Path.GetTempPath(), "AnotherDaySearchContainerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempSaveRoot);
            var saveSystem = Game.Interface.GetSystem<SaveSystem>();
            saveSystem.ConfigureForTests(tempSaveRoot);
            Assert.That(saveSystem.CreateBase(out var persisted, out var commitError), Is.True, commitError);
            EnterExpeditionSession();
            Game.Interface.GetModel<SaveModel>().SetExpedition(new ExpeditionSaveData { MapId = MapIds.SurvivalSample });
            RegisterSearchWorld(corpseId);
            Game.Interface.SendEvent(new EnemyCorpseContentsRequestedEvent(
                44, corpseId, Vector3.zero, Quaternion.identity,
                new[] { new LootDropBatchEntry("loot.sword", 0, Vector3.zero) }));
            var search = Game.Interface.GetSystem<SearchContainerSystem>();
            Assert.That(search.TryBeginSearch(corpseId, out var error), Is.True, error);
            yield return AdvanceSearch(search);

            var container = UIHelper.GetUICtrl<SearchContainerUICtrl>();
            Assert.That(container.IsShow(), Is.True);
            var bag = UIHelper.GetUICtrl<BagUICtrl>();
            Assert.That(bag, Is.Not.Null);
            Assert.That(bag.IsShow(), Is.True);
            var searchUi = GetView<SearchContainerUI>(container);
            Assert.That(searchUi.SearchList.totalCount, Is.GreaterThan(0));
            var searchSource = BindSearchCell(container, searchUi, 0);

            yield return null;
            var data = new PointerEventData(EventSystem.current) { position = new Vector2(300f, 220f) };
            ExecuteEvents.Execute(searchSource.ClickButton.gameObject, data, ExecuteEvents.beginDragHandler);
            ExecuteEvents.Execute(searchSource.ClickButton.gameObject, data, ExecuteEvents.dragHandler);
            AssertDragVisual(searchSource);
            ExecuteEvents.Execute(bag.PlayerBagSlots[0].ClickButton.gameObject, data, ExecuteEvents.dropHandler);
            ExecuteEvents.Execute(searchSource.ClickButton.gameObject, data, ExecuteEvents.endDragHandler);
            Assert.That(GetDragVisual(), Is.Null);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.ShortSword), Is.EqualTo(1));

            var bagSource = bag.PlayerBagSlots[0];
            data.position = new Vector2(460f, 340f);
            ExecuteEvents.Execute(bagSource.ClickButton.gameObject, data, ExecuteEvents.beginDragHandler);
            ExecuteEvents.Execute(bagSource.ClickButton.gameObject, data, ExecuteEvents.dragHandler);
            AssertDragVisual(bagSource);
            var searchTarget = BindSearchCell(container, searchUi, 1);
            ExecuteEvents.Execute(searchTarget.ClickButton.gameObject, data, ExecuteEvents.dropHandler);
            ExecuteEvents.Execute(bagSource.ClickButton.gameObject, data, ExecuteEvents.endDragHandler);
            Assert.That(GetDragVisual(), Is.Null);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.ShortSword), Is.Zero);
            Assert.That(search.TryGetSnapshot(corpseId, out var snapshot), Is.True);
            Assert.That(snapshot.Slots.Count, Is.EqualTo(1));
            Assert.That(snapshot.Slots[0].SlotIndex, Is.EqualTo(1));
            Assert.That(Game.Interface.GetUtility<IItemTransferPort>().State.DragSource, Is.Null);

            searchSource = BindSearchCell(container, searchUi, 1);
            data.position = new Vector2(520f, 360f);
            ExecuteEvents.Execute(searchSource.ClickButton.gameObject, data, ExecuteEvents.beginDragHandler);
            ExecuteEvents.Execute(searchSource.ClickButton.gameObject, data, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(bag.PlayerBagSlots[0].ClickButton.gameObject, data, ExecuteEvents.dropHandler);
            ExecuteEvents.Execute(searchSource.ClickButton.gameObject, data, ExecuteEvents.endDragHandler);
            Assert.That(GetDragVisual(), Is.Null);
            var transfer = Game.Interface.GetUtility<IItemTransferPort>();
            transfer.ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag, 0)); transfer.UseSelected();
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.ShortSword), Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().MeleeWeaponItemId,
                Is.EqualTo(ItemIds.ShortSword));
            Assert.That(search.TryGetSnapshot(corpseId, out snapshot), Is.True);
            Assert.That(snapshot.Slots, Is.Empty);
        }

        private static GameObject AssertDragVisual(InventorySlotBoxUI source)
        {
            var visual = GetDragVisual();
            Assert.That(visual, Is.Not.Null);
            var dialog = visual.transform.parent;
            var visualCount = 0;
            foreach (Transform child in dialog)
            {
                if (child.name == "InventoryDragVisual")
                {
                    visualCount++;
                }
            }

            Assert.That(visualCount, Is.EqualTo(1));
            Assert.That(visual.transform.GetSiblingIndex(), Is.EqualTo(visual.transform.parent.childCount - 1));
            var image = visual.GetComponent<Image>();
            var group = visual.GetComponent<CanvasGroup>();
            var rect = visual.GetComponent<RectTransform>();
            Assert.That(image.sprite, Is.SameAs(source.IconImage.sprite));
            Assert.That(image.color.r, Is.EqualTo(source.IconImage.color.r));
            Assert.That(image.color.g, Is.EqualTo(source.IconImage.color.g));
            Assert.That(image.color.b, Is.EqualTo(source.IconImage.color.b));
            Assert.That(image.color.a, Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(image.type, Is.EqualTo(source.IconImage.type));
            Assert.That(image.preserveAspect, Is.EqualTo(source.IconImage.preserveAspect));
            Assert.That(image.raycastTarget, Is.False);
            Assert.That(group.blocksRaycasts, Is.False);
            Assert.That(group.interactable, Is.False);
            Assert.That(rect.rect.size, Is.EqualTo(source.IconImage.rectTransform.rect.size));
            Assert.That(rect.localScale, Is.EqualTo(Vector3.one));
            Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(rect.pivot, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(source.IconImage.enabled, Is.False);
            return visual;
        }

        private static GameObject GetDragVisual()
        {
            return UIRoot.Instance.GetLayerObj(UILayer.Dialog).Find("InventoryDragVisual")?.gameObject;
        }

        private static GameObject LoadPrefab(string path)
        {
            // 保留正式资源路径，使失败提示也能加载真实 Notify/Float，而非错误映射到库存目录。
            return AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab");
        }

        private static InventorySlotBoxUI BindSearchCell(
            SearchContainerUICtrl controller,
            SearchContainerUI ui,
            int index)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Inventory/Box/InventorySlotBox.prefab");
            var instance = Object.Instantiate(prefab, ui.SearchList.GetContentTransform(), false);
            var refresh = typeof(SearchContainerUICtrl).GetMethod(
                "RefreshSearchCell", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(refresh, Is.Not.Null);
            refresh.Invoke(controller, new object[] { instance.transform, index });
            return instance.GetComponent<InventorySlotBoxUI>();
        }

        private static T GetView<T>(BaseUICtrl ctrl) where T : BaseUI
        {
            return (T)typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ctrl);
        }

        private void EnterSafeBaseSession()
        {
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            SetActiveTestWorld(MapIds.Base);
            Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.BaseSafe);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
        }

        private void EnterExpeditionSession()
        {
            Game.Interface.GetModel<SaveModel>().SetExpedition(
                new ExpeditionSaveData { MapId = MapIds.SurvivalSample });
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            SetActiveTestWorld(MapIds.SurvivalSample);
            Game.Interface.GetModel<ExpeditionModel>().BeginSurvival(MapIds.SurvivalSample, 60f);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(
                PlayerBagRiskState.InExpedition, "test", false, out var error), Is.True, error);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
        }

        private void SetActiveTestWorld(string mapId)
        {
            _testMapHandle?.Destroy();
            _testMapHandle = new TestMapContentHandle(mapId);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(mapId, _testMapHandle);

            RegisterUnit(PlayerModel.LocalPlayerUnitId, UnitType.Player, "player", Vector3.zero);
            _playerRuntimePorts.Current = new PlayerMapLocalPoseSnapshot(
                PlayerModel.LocalPlayerUnitId,
                TestMapInstanceId,
                Vector3.zero,
                0f,
                true);

            Assert.That(Game.Interface.GetSystem<MapSceneSystem>().HasRecoverableCurrentWorld(), Is.True,
                "测试库存窗口前必须建立可恢复的活动世界。");
        }

        private static void RegisterSearchWorld(int corpseId)
        {
            RegisterUnit(PlayerModel.LocalPlayerUnitId, UnitType.Player, "player", Vector3.zero);
            RegisterUnit(corpseId, UnitType.Enemy, "enemy", Vector3.zero);
        }

        private static void RegisterUnit(int unitId, UnitType type, string configId, Vector3 position)
        {
            Game.Interface.GetSystem<UnitSystem>().Remove(unitId, UnitRemovedReason.Explicit);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                unitId, TestMapInstanceId, type, type == UnitType.Player ? UnitSource.Player : UnitSource.MapPlaced,
                configId, position, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out var error), Is.True, error);
        }

        private static IEnumerator AdvanceSearch(SearchContainerSystem search)
        {
            Game.Interface.GetSystem<PlayerActionSystem>()
                .TickForTests(SearchContainerSystem.SearchDurationSeconds);
            LifeCycle.Update();
            Assert.That(search.IsSearching, Is.False);
            yield break;
        }

        private static void CreateUiRoot()
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

            var module = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            new GameObject("EventSystem", typeof(EventSystem), module).transform.SetParent(root.transform, false);
        }

        private static void DestroyNamed(string name)
        {
            var value = GameObject.Find(name);
            if (value != null)
            {
                Object.DestroyImmediate(value);
            }
        }

        private sealed class TestMapContentHandle : IMapContentHandle
        {
            private readonly GameObject _root;

            public TestMapContentHandle(string mapId)
            {
                MapId = mapId;
                _root = new GameObject($"InventoryIndependentUiTests_Map_{mapId}");
                var presentation = new GameObject("Presentation");
                presentation.transform.SetParent(_root.transform, false);
                var runtimeUnits = new GameObject("RuntimeUnits");
                runtimeUnits.transform.SetParent(presentation.transform, false);

                Context = _root.AddComponent<MapContext>();
                typeof(MapContext).GetField("mapContentRoot", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(Context, presentation);
                typeof(MapContext).GetField("dynamicObjectRoot", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(Context, runtimeUnits.transform);
            }

            public string MapId { get; }
            public MapContext Context { get; }
            public bool IsValid => _root != null && Context != null && Context.Validate(out _);

            public void Destroy()
            {
                if (_root != null)
                {
                    Object.DestroyImmediate(_root);
                }
            }
        }

        private sealed class ActiveWorldPlayerPortProvider : IPlayerRuntimePortProvider
        {
            public PlayerMapLocalPoseSnapshot Current;

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
                return false;
            }

            public bool TryGetCurrentPlayerMapLocalPose(out PlayerMapLocalPoseSnapshot snapshot, out string error)
            {
                snapshot = Current;
                error = snapshot.IsActive ? null : "test player inactive";
                return snapshot.IsActive;
            }
        }

        private sealed class InventoryLocalizationLoader : IResourceLoader
        {
            private readonly Sprite icon;

            public InventoryLocalizationLoader(Sprite icon)
            {
                this.icon = icon;
            }

            public T LoadAsset<T>(string path) where T : Object
            {
                switch (path)
                {
                    case "inventory-test-icon":
                        return icon as T;
                    case LocalizationLanguages.LanguagesAddress:
                        return new TextAsset(
                            "{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"简体中文\"},{\"code\":\"en-US\",\"label\":\"English\"}]}") as T;
                    case "zh-CN.json":
                        return new TextAsset(
                            "{\"ui.inventory.title_storage\":\"仓库\",\"ui.inventory.expand_slot\":\"扩容\",\"ui.inventory.capacity_slots\":\"{0} / {1} 格\"}") as T;
                    case "en-US.json":
                        return new TextAsset(
                            "{\"ui.inventory.title_storage\":\"Warehouse\",\"ui.inventory.expand_slot\":\"Expand\",\"ui.inventory.capacity_slots\":\"{0} / {1} slots\"}") as T;
                    default:
                        return null;
                }
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                onLoaded?.Invoke(LoadAsset<T>(path));
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }

        private sealed class InventoryConfigProvider : IRuntimeConfigProvider
        {
            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                if (id == ItemIds.MetalParts || id == ItemIds.ShortSword)
                {
                    value = new ItemDefinition(
                        id,
                        id == ItemIds.MetalParts ? "Metal Parts" : "Short Sword",
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        "inventory-test-icon",
                        id == ItemIds.MetalParts ? global::ItemType.Material : global::ItemType.Weapon,
                        id == ItemIds.MetalParts ? 20 : 1,
                        global::ItemUseEffect.None,
                        0);
                    error = null;
                    return true;
                }

                value = default;
                error = $"item {id} is unavailable";
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                if (id == "loot.sword")
                {
                    value = new LootConfigDefinition(id, string.Empty, ItemIds.ShortSword, 1);
                    error = null;
                    return true;
                }

                value = default;
                error = "loot is unavailable";
                return false;
            }

            public bool TryGetWeapon(int itemId, out WeaponConfigDefinition value, out string error)
            {
                if (itemId == ItemIds.ShortSword)
                {
                    value = new WeaponConfigDefinition(
                        itemId,
                        global::WeaponKind.Melee,
                        10,
                        0.45f,
                        2.2f,
                        90f,
                        string.Empty,
                        0);
                    error = null;
                    return true;
                }

                value = default;
                error = "weapon is unavailable";
                return false;
            }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = default;
                error = "map config is unavailable";
                return false;
            }

            public bool TryGetMapSpawns(string mapId, out System.Collections.Generic.IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = Array.Empty<MapSpawnDefinition>();
                error = "map spawn config is unavailable";
                return false;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = default;
                error = "player config is unavailable";
                return false;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                value = default;
                error = "enemy config is unavailable";
                return false;
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                value = default;
                error = "extraction config is unavailable";
                return false;
            }
        }
    }
}
