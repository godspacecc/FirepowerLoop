using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    /// <summary>验证正式口袋栏接线与实际点击；测试只实例化资源，不修改正式 Prefab。</summary>
    public sealed class PocketQuickBarHudTests
    {
        private WeaponEquipmentTests environment;
        private GameObject root;
        private MobileJoystickUICtrl controller;
        private CustomListView list;
        private GameObject notificationRoot;
        private GameObject poolRoot;

        [SetUp] public void SetUp() { environment = new WeaponEquipmentTests(); environment.SetUp(); }
        [TearDown] public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (notificationRoot != null) Object.DestroyImmediate(notificationRoot);
            if (poolRoot != null) Object.DestroyImmediate(poolRoot);
            environment.TearDown();
        }

        private void Bind(bool openPocket = true)
        {
            root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab"));
            var view = root.GetComponent<MobileJoystickUI>();
            var field = typeof(MobileJoystickUI).GetField("PocketList");
            Assert.That(field, Is.Not.Null, "HUD 应有生成的口袋列表引用");
            list = (CustomListView)field.GetValue(view);
            Assert.That(list, Is.Not.Null);
            controller = new MobileJoystickUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, view);
            typeof(BaseUICtrl).GetField("_isActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, true);
            typeof(MobileJoystickUICtrl).GetMethod("OnInit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            Refresh();
            if (openPocket) ToggleButton().onClick.Invoke();
        }

        private void Refresh()
        {
            var method = typeof(MobileJoystickUICtrl).GetMethod("RefreshPocket", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(controller, null);
        }
        private PocketSlotBoxUI Cell(int index) => list.transform.GetChild(index).GetComponent<PocketSlotBoxUI>();

        private Button ToggleButton()
        {
            var field = typeof(MobileJoystickUI).GetField("PocketToggleButton");
            Assert.That(field, Is.Not.Null, "口袋开关必须由 Prefab 生成并接线");
            return (Button)field.GetValue(root.GetComponent<MobileJoystickUI>());
        }

        private GameObject Panel => root.GetComponent<MobileJoystickUI>().PocketPanel;

        [Test] public void PocketStartsCollapsedAndButtonTogglesVisibility()
        {
            Bind(false);
            Assert.That(Panel.activeSelf, Is.False);
            ToggleButton().onClick.Invoke();
            Assert.That(Panel.activeSelf, Is.True);
            ToggleButton().onClick.Invoke();
            Assert.That(Panel.activeSelf, Is.False);
            ToggleButton().onClick.Invoke();
            root.GetComponent<MobileJoystickUI>().PocketCloseButton.onClick.Invoke();
            root.GetComponent<MobileJoystickUI>().PocketCloseButton.onClick.Invoke();
            Assert.That(Panel.activeSelf, Is.False, "重复关闭不得重新展开");
        }

        [Test] public void CollapsedPocketRejectsOldItemClick()
        {
            Game.Interface.GetModel<PlayerBagModel>().Pocket.TryAdd(ItemIds.ShortSword, 1, 1, out _, out _);
            Bind();
            ToggleButton().onClick.Invoke();
            Cell(0).ClickButton.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId, Is.Zero);
        }

        [Test] public void HiddenHudRejectsOldToggleClick()
        {
            Bind(false);
            typeof(BaseUICtrl).GetField("_isActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, false);
            ToggleButton().onClick.Invoke();
            Assert.That(Panel.activeSelf, Is.False);
        }

        [Test] public void PocketUsesNarrowBuildingStylePanelAndVerticalRows()
        {
            Bind();
            Assert.That(AssetDatabase.GetAssetPath(list.itemPrefab), Is.EqualTo("Assets/Game/Prefabs/UI/Player/Box/PocketSlotBox.prefab"));
            var building = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/TemporaryBuilding/TemporaryBuildingSelect.prefab");
            var sourcePanel = building.GetComponent<RectTransform>();
            var panel = Panel.GetComponent<RectTransform>();
            Assert.That(panel.sizeDelta.y, Is.EqualTo(sourcePanel.sizeDelta.y));
            Assert.That(panel.sizeDelta.x, Is.InRange(320f, sourcePanel.sizeDelta.x * .8f), "四格口袋应比建筑目录窄");
            Assert.That(panel.anchorMin, Is.EqualTo(sourcePanel.anchorMin));
            Assert.That(panel.anchorMax, Is.EqualTo(sourcePanel.anchorMax));
            Assert.That(panel.anchoredPosition.y, Is.EqualTo(sourcePanel.anchoredPosition.y));
            Assert.That(panel.anchoredPosition.x + panel.sizeDelta.x * .5f,
                Is.EqualTo(sourcePanel.anchoredPosition.x + sourcePanel.sizeDelta.x * .5f), "保留右侧边距");
            var sourceView = building.GetComponent<TemporaryBuildingSelectUI>();
            Assert.That(list.GetComponent<VerticalLayoutGroup>().spacing,
                Is.EqualTo(sourceView.BuildingList.GetComponent<VerticalLayoutGroup>().spacing));
            Assert.That(root.GetComponent<MobileJoystickUI>().PocketScroll.content, Is.EqualTo(list.transform));
            var sourceBox = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/TemporaryBuilding/Box/TemporaryBuildingOptionBox.prefab");
            var sourceHeight = sourceBox.GetComponent<RectTransform>().rect.height;
            Assert.That(list.totalCount, Is.EqualTo(4));
            for (var i = 0; i < 4; i++)
            {
                var rect = Cell(i).GetComponent<RectTransform>();
                Assert.That(rect.rect.height, Is.EqualTo(sourceHeight), "口袋条目应沿用建筑条目高度");
                Assert.That(rect.anchoredPosition.x, Is.EqualTo(Cell(0).GetComponent<RectTransform>().anchoredPosition.x));
                if (i > 0) Assert.That(rect.anchoredPosition.y, Is.LessThan(Cell(i - 1).GetComponent<RectTransform>().anchoredPosition.y - sourceHeight));
            }
        }

        [Test] public void SelectedWeaponHighlightsOnlyEdges()
        {
            Game.Interface.GetModel<PlayerBagModel>().Pocket.TryAdd(ItemIds.ShortSword, 1, 1, out _, out _);
            Bind();
            Cell(0).ClickButton.onClick.Invoke();
            var frame = Cell(0).SelectedFrame;
            Assert.That(frame.activeSelf, Is.True);
            Assert.That(frame.GetComponent<Image>(), Is.Null, "高亮不能使用整格填色");
            var edges = frame.GetComponentsInChildren<Image>();
            Assert.That(edges.Length, Is.EqualTo(4));
            foreach (var edge in edges)
            {
                Assert.That(edge.raycastTarget, Is.False);
                var rect = edge.rectTransform.rect;
                Assert.That(Mathf.Min(rect.width, rect.height), Is.LessThanOrEqualTo(3));
            }
        }

        [Test] public void PocketClicksSwitchWeaponWithoutMovingOrConsuming()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>();
            bag.Pocket.TryAdd(ItemIds.ShortSword, 1, 1, out _, out _);
            bag.Pocket.TryAdd(ItemIds.Bow, 1, 1, out _, out _);
            Bind();
            Cell(0).ClickButton.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId, Is.EqualTo(ItemIds.ShortSword));
            Cell(1).ClickButton.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId, Is.EqualTo(ItemIds.Bow));
            Assert.That(bag.Pocket.GetTotalAmount(ItemIds.ShortSword), Is.EqualTo(1));
            Assert.That(bag.Pocket.GetTotalAmount(ItemIds.Bow), Is.EqualTo(1));
            Assert.That(bag.Container.Slots.Count, Is.Zero);
            Assert.That(Cell(1).SelectedFrame.activeSelf, Is.True);
            Assert.That(Panel.activeSelf, Is.True, "切换武器后保持展开");
            Assert.That(Cell(1).NameText.text, Is.Not.Empty, "条目显示道具名称");
        }

        [TestCase(ItemIds.ShortSword)]
        [TestCase(ItemIds.Bow)]
        public void RepeatedPocketWeaponClickHolstersAndCanHoldAgain(int itemId)
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>();
            bag.Pocket.TryAdd(itemId, 1, 1, out _, out _);
            Bind();
            Cell(0).ClickButton.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId, Is.EqualTo(itemId));
            Assert.That(Cell(0).SelectedFrame.activeSelf, Is.True);

            Cell(0).ClickButton.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId, Is.Zero, "再次点击当前武器应收起");
            Assert.That(Cell(0).SelectedFrame.activeSelf, Is.False);
            Assert.That(bag.Pocket.GetTotalAmount(itemId), Is.EqualTo(1));
            Assert.That(bag.Container.Slots.Count, Is.Zero);
            Assert.That(Panel.activeSelf, Is.True);

            Cell(0).ClickButton.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId, Is.EqualTo(itemId));
            Assert.That(Cell(0).SelectedFrame.activeSelf, Is.True);
        }

        [Test] public void EmptyAndAmmunitionSlotsCannotBeUsed()
        {
            Game.Interface.GetModel<PlayerBagModel>().Pocket.TryAdd(ItemIds.Arrow, 2, 20, out _, out _);
            Bind();
            Assert.That(Cell(0).ClickButton.interactable, Is.False);
            Assert.That(Cell(1).ClickButton.interactable, Is.False);
            Assert.That(Cell(0).AmountText.text, Is.EqualTo("2"));
        }

        [Test] public void OldClickCannotHoldReplacementInSameSlot()
        {
            var pocket = Game.Interface.GetModel<PlayerBagModel>().Pocket;
            pocket.TryAdd(ItemIds.ShortSword, 1, 1, out _, out _);
            Bind();
            pocket.TryRemove(0, 1, out _, out _);
            pocket.TryAdd(ItemIds.ShortSword, 1, 1, out _, out _);
            Cell(0).ClickButton.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId, Is.Zero);
        }

        [Test] public void HiddenHudIgnoresOldClick()
        {
            Game.Interface.GetModel<PlayerBagModel>().Pocket.TryAdd(ItemIds.ShortSword, 1, 1, out _, out _);
            Bind();
            typeof(BaseUICtrl).GetField("_isActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, false);
            Cell(0).ClickButton.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId, Is.Zero);
        }

        [Test] public void WeaponHudRemainsDisplayOnly()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/PlayerConditionHud.prefab");
            var view = prefab.GetComponent<PlayerConditionHudUI>();
            Assert.That(view.WeaponInfoRoot.GetComponent<Button>(), Is.Null, "武器展示不再承担点击选择入口");
        }

        [Test] public void DefaultButtonMigrationPreservesCustomPositions()
        {
            var legacy = new MobileControlLayoutData
            {
                Pause = new MobileButtonLayoutData { X = .95f, Y = .93f },
                Inventory = new MobileButtonLayoutData { X = .95f, Y = .82f },
                Build = new MobileButtonLayoutData { X = .95f, Y = .71f }
            }.Clone();
            Assert.That(legacy.Pause.X, Is.LessThan(.2f));
            Assert.That(legacy.Build.X, Is.LessThan(.2f));
            Assert.That(legacy.Inventory.X, Is.GreaterThan(.8f));
            Assert.That(legacy.Inventory.Y, Is.EqualTo(legacy.Pause.Y));
            var custom = new MobileControlLayoutData { Pause = new MobileButtonLayoutData { X = .4f, Y = .6f } }.Clone();
            Assert.That(custom.Pause.X, Is.EqualTo(.4f));
            Assert.That(custom.Pause.Y, Is.EqualTo(.6f));
        }

        [Test] public void ConsumableClickStartsTimedUseAndConsumesOnlyPocket()
        {
            notificationRoot = (GameObject)typeof(GameplayGuideHudTests)
                .GetMethod("CreateUiRoot", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            poolRoot = new GameObject("PoolRoot");
            ResourceLoader.SetLoaderForTests(new HudResourceLoader());
            var provider = (IRuntimeConfigProvider)typeof(ItemInventoryFoundationTests)
                .GetMethod("CreateRealConfigProvider", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(provider);
            var bag = Game.Interface.GetModel<PlayerBagModel>();
            bag.Pocket.TryAdd(ItemIds.Ration, 2, 20, out _, out _);
            var condition = Game.Interface.GetModel<PlayerConditionModel>();
            typeof(PlayerConditionModel).GetMethod("SetPower", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(condition, new object[] { 50f });
            Bind();
            Cell(0).ClickButton.onClick.Invoke();
            var actionSystem = Game.Interface.GetSystem<PlayerActionSystem>();
            Assert.That(actionSystem.TryGetSnapshot(out var action), Is.True);
            Assert.That(action.Kind, Is.EqualTo(PlayerActionKind.UseItem));
            Assert.That(bag.Pocket.GetTotalAmount(ItemIds.Ration), Is.EqualTo(2), "动作完成前不扣库存");
            actionSystem.TickForTests(30f);
            Assert.That(bag.Pocket.GetTotalAmount(ItemIds.Ration), Is.EqualTo(1));
            Assert.That(condition.Power, Is.GreaterThan(50f));
            Assert.That(bag.Container.GetTotalAmount(ItemIds.Ration), Is.Zero);
            Assert.That(Panel.activeSelf, Is.True, "使用完成后保持展开");
        }

        [Test] public void OnlineModeWithoutSnapshotDoesNotExposeOfflinePocket()
        {
            Game.Interface.GetModel<PlayerBagModel>().Pocket.TryAdd(ItemIds.ShortSword, 1, 1, out _, out _);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null, MapPreparationMode.OnlinePlayersOnly);
            Bind();
            Assert.That(list.totalCount, Is.Zero);
        }

        private sealed class HudResourceLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>("Assets/Game/" + path + (typeof(T) == typeof(GameObject) ? ".prefab" : ""));
                return asset != null ? asset : AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/" + path);
            }
            public ResourceLoadHandle LoadAssetAsync<T>(string path, System.Action<T> callback) where T : Object
            { callback?.Invoke(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
            public void Release(ResourceLoadHandle handle) { }
        }
    }
}
