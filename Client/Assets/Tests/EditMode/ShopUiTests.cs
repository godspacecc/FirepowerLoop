using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class ShopUiTests
    {
        private string root;
        private ShopUiConfigProvider config;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            UIHelper.ResetForTests();
            LocalizationLanguages.ResetForTests();
            ResourceLoader.SetLoaderForTests(new ShopLocalizationLoader());
            root = Path.Combine(Path.GetTempPath(), "AnotherDayShopUi", Guid.NewGuid().ToString("N"));
            config = new ShopUiConfigProvider();
        }

        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
            ResourceLoader.ResetForTests();
            TestReset.ResetArchitecture();
            LocalizationLanguages.ResetForTests();
            foreach (var value in Object.FindObjectsOfType<UIRoot>(true)) Object.DestroyImmediate(value.gameObject);
            foreach (var value in Object.FindObjectsOfType<EventSystem>(true)) Object.DestroyImmediate(value.gameObject);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void ProductRowsStartAtTopForSingleAndMultipleItems()
        {
            var host = new GameObject("ShopLayoutHost", typeof(RectTransform));
            try
            {
                ((RectTransform)host.transform).sizeDelta = new Vector2(1920, 1080);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Shop/Shop.prefab");
                var ui = Object.Instantiate(prefab, host.transform, false).GetComponent<ShopUI>();
                var content = (RectTransform)ui.ItemList.transform;
                var scroll = content.GetComponentInParent<ScrollRect>();
                // 覆盖短列表、溢出滚动及从多件缩回单件，防止内容收缩后重新居中。
                foreach (var count in new[] { 1, 3, 8, 1 })
                {
                    ui.ItemList.totalCount = count;
                    ui.ItemList.RefreshCells();
                    LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                    scroll.verticalNormalizedPosition = 1;
                    var viewportCorners = new Vector3[4];
                    var rowCorners = new Vector3[4];
                    scroll.viewport.GetWorldCorners(viewportCorners);
                    ((RectTransform)ui.ItemList.GetDraggableItems()[0]).GetWorldCorners(rowCorners);
                    Assert.That(rowCorners[1].y, Is.EqualTo(viewportCorners[1].y).Within(.1f), $"商品数：{count}");
                }
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void ShopPrefabsDeserializeCompleteGeneratedContractsAndExternalBoxReference()
        {
            var shopPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Shop/Shop.prefab");
            var boxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Shop/Box/ShopItemBox.prefab");
            Assert.That(shopPrefab, Is.Not.Null);
            Assert.That(boxPrefab, Is.Not.Null);
            Assert.That(shopPrefab.GetComponent<ReferenceCollector>(), Is.Not.Null);
            Assert.That(boxPrefab.GetComponent<ReferenceCollector>(), Is.Not.Null);
            var ui = shopPrefab.GetComponent<ShopUI>();
            var box = boxPrefab.GetComponent<ShopItemBoxUI>();
            Assert.That(ui, Is.Not.Null);
            Assert.That(box, Is.Not.Null);
            Assert.That(ui.ItemList.itemPrefab, Is.SameAs(boxPrefab));
            Assert.That(ui.BtnClose, Is.Not.Null);
            Assert.That(ui.BtnBuyTab, Is.Not.Null);
            Assert.That(ui.BtnSellTab, Is.Not.Null);
            Assert.That(ui.BtnTrade, Is.Not.Null);
            Assert.That(ui.DetailRoot, Is.Not.Null);
            Assert.That(ui.TextShopName, Is.Not.Null);
            Assert.That(ui.TextGold, Is.Not.Null);
            Assert.That(box.BtnSelect, Is.Not.Null);
            Assert.That(box.SelectedRoot, Is.Not.Null);
            Assert.That(box.DisabledRoot, Is.Not.Null);
        }

        [Test]
        public void FormalShopButtonsRowsAndVisibilityFollowAuthoritativeSnapshotLifecycle()
        {
            InstallArchitecture();
            CreateUIRoot();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Shop/Shop.prefab");
            UIHelper.SetPrefabLoaderForTests(path => path == "Prefabs/UI/Shop/Shop" ? prefab : null);
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 1, 20, out _, out var error), Is.True, error);
            Game.Interface.GetModel<CurrencyModel>().TrySetGold(6);
            var save = Game.Interface.GetSystem<SaveSystem>();
            save.MarkDirty("shop ui setup");
            Assert.That(save.Flush(out error), Is.True, error);

            Assert.That(ShopUICtrl.TryShow("npc.shop", 1001, "shop.test", out error), Is.True, error);
            var ctrl = UIHelper.GetUICtrl<ShopUICtrl>();
            var ui = Object.FindObjectsOfType<ShopUI>(true).Single(value => value.gameObject.scene.IsValid());
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(Game.Interface.GetSystem<ShopSystem>().Snapshot.SelectedItemId, Is.EqualTo(ItemIds.Arrow));
            var reusedRow = ui.ItemList.GetDraggableItems().Single();
            var buyBox = reusedRow.GetComponent<ShopItemBoxUI>();
            Assert.That(buyBox.TextName.text, Is.EqualTo(Loc.T("item.ammunition.arrow.name")));
            Assert.That(buyBox.Icon.enabled, Is.False);
            Assert.That(buyBox.SelectedRoot.activeSelf, Is.True);
            Assert.That(buyBox.DisabledRoot.activeSelf, Is.False);
            Assert.That(buyBox.BtnSelect.interactable, Is.True);

            Game.Interface.GetModel<CurrencyModel>().TrySetGold(0);
            ui.BtnBuyTab.onClick.Invoke();
            Assert.That(ui.ItemList.GetDraggableItems().Single(), Is.SameAs(reusedRow));
            Assert.That(buyBox.DisabledRoot.activeSelf, Is.True);
            Assert.That(buyBox.BtnSelect.interactable, Is.True);
            Game.Interface.GetModel<CurrencyModel>().TrySetGold(6);
            ui.BtnBuyTab.onClick.Invoke();
            Assert.That(buyBox.DisabledRoot.activeSelf, Is.False);
            Assert.That(buyBox.BtnSelect.interactable, Is.True);

            ui.BtnPlus.onClick.Invoke();
            Assert.That(Game.Interface.GetSystem<ShopSystem>().Snapshot.Quantity, Is.EqualTo(2));
            Assert.That(ui.TextTotal.text, Does.Contain("4"));
            ui.BtnTrade.onClick.Invoke();
            var afterBuy = Game.Interface.GetSystem<ShopSystem>().Snapshot;
            Assert.That(afterBuy.SelectedItemId, Is.EqualTo(ItemIds.Arrow));
            Assert.That(afterBuy.Quantity, Is.EqualTo(1), "余额下降后数量应收敛到新的购买上限");
            Assert.That(bag.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(2));
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(2));

            ui.BtnSellTab.onClick.Invoke();
            var sellRows = ui.ItemList.GetDraggableItems();
            Assert.That(sellRows, Has.Count.EqualTo(2));
            Assert.That(sellRows[0], Is.SameAs(reusedRow), "CustomListView 应复用已创建的正式 Box");
            sellRows[1].GetComponent<ShopItemBoxUI>().BtnSelect.onClick.Invoke();
            Assert.That(Game.Interface.GetSystem<ShopSystem>().Snapshot.SelectedItemId, Is.EqualTo(ItemIds.Arrow));
            buyBox.BtnSelect.onClick.Invoke();
            Assert.That(Game.Interface.GetSystem<ShopSystem>().Snapshot.SelectedItemId, Is.EqualTo(ItemIds.MetalParts),
                "复用行必须清除旧的箭矢选择回调并绑定当前金属行");
            Assert.That(buyBox.TextName.text, Is.EqualTo(Loc.T("item.metal_parts.name")));
            Assert.That(buyBox.TextPrice.text, Does.Contain("5"));
            Assert.That(buyBox.TextOwned.text, Does.Contain("1"));
            Assert.That(buyBox.SelectedRoot.activeSelf, Is.True);
            Assert.That(buyBox.DisabledRoot.activeSelf, Is.False);
            Assert.That(buyBox.BtnSelect.interactable, Is.True);
            ui.BtnTrade.onClick.Invoke();
            Assert.That(Game.Interface.GetSystem<ShopSystem>().Snapshot.SelectedItemId, Is.Zero,
                "出售到零必须清除选择");
            Assert.That(bag.GetTotalAmount(ItemIds.MetalParts), Is.Zero);

            ui.BtnSellTab.onClick.Invoke();
            var arrowBox = ui.ItemList.GetDraggableItems().Single().GetComponent<ShopItemBoxUI>();
            ui.BtnPlus.onClick.Invoke();
            Assert.That(Game.Interface.GetSystem<ShopSystem>().Snapshot.Quantity, Is.EqualTo(2));
            var arrowSlot = bag.Slots.Single(pair => pair.Value.ItemId == ItemIds.Arrow).Key;
            Assert.That(bag.TryRemove(arrowSlot, 1, out _, out error), Is.True, error);
            ui.BtnTrade.onClick.Invoke();
            var failed = Game.Interface.GetSystem<ShopSystem>().Snapshot;
            Assert.That(failed.LastFailure, Is.EqualTo(ShopFailureReason.InsufficientItems));
            Assert.That(failed.SelectedItemId, Is.EqualTo(ItemIds.Arrow));
            Assert.That(failed.Quantity, Is.EqualTo(2));

            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
            Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));
            Assert.That(arrowBox.TextName.text, Is.EqualTo("Arrow"));
            Assert.That(ui.TextQuantity.text, Is.EqualTo("2"));

            var goldBeforeHide = Game.Interface.GetModel<CurrencyModel>().Gold;
            var arrowBeforeHide = bag.GetTotalAmount(ItemIds.Arrow);
            ctrl.Hide();
            Assert.That(ctrl.IsShow(), Is.False);
            ui.BtnTrade.onClick.Invoke();
            arrowBox.BtnSelect.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(goldBeforeHide));
            Assert.That(bag.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(arrowBeforeHide));
            Assert.That(Game.Interface.GetSystem<ShopSystem>().Snapshot.ShopId, Is.Null.Or.Empty);

            Assert.That(ShopUICtrl.TryShow("npc.shop", 1001, "shop.test", out error), Is.True, error);
            config.ShopAvailable = false;
            ui.BtnTrade.onClick.Invoke();
            Assert.That(ctrl.IsShow(), Is.False, "权威配置失效的空快照应立即隐藏商店 UI");
            Assert.That(Game.Interface.GetSystem<ShopSystem>().Snapshot.ShopId, Is.Null.Or.Empty);
        }

        private void InstallArchitecture()
        {
            var architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(architecture, true);
            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterModel(new SaveModel());
            architecture.RegisterModel(new AppStateModel());
            architecture.RegisterModel(new MapSceneModel());
            architecture.RegisterModel(new UnitModel());
            architecture.RegisterModel(new PlayerBagModel());
            architecture.RegisterModel(new WarehouseModel());
            architecture.RegisterModel(new CurrencyModel());
            var localization = new LocalizationUtility(loadConfiguredTables: false);
            architecture.RegisterUtility(localization);
            architecture.RegisterUtility<IRuntimeConfigProvider>(config);
            architecture.RegisterSystem(new SaveSystem());
            architecture.RegisterSystem(new UnitSystem());
            architecture.RegisterSystem(new InventorySystem());
            architecture.RegisterSystem(new ShopSystem());
            localization.LoadFromJson("zh-CN", File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
            localization.LoadFromJson("en-US", File.ReadAllText("Assets/Game/Config/Localization/en-US.json"));
            var save = Game.Interface.GetSystem<SaveSystem>();
            save.ConfigureForTests(root);
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.BaseSafe);
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                1001, 1, UnitType.Npc, UnitSource.MapPlaced, "npc.shop", Vector3.zero,
                Quaternion.identity, UnitAuthorityMode.Local, 0, true), out var error), Is.True, error);
        }

        private static void CreateUIRoot()
        {
            var rootObject = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(UIRoot));
            foreach (var layerName in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var layer = new GameObject(layerName, typeof(RectTransform));
                layer.transform.SetParent(rootObject.transform, false);
            }
            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            Assert.That(inputModuleType, Is.Not.Null);
            new GameObject("EventSystem", typeof(EventSystem), inputModuleType).transform.SetParent(rootObject.transform, false);
        }

        private sealed class ShopUiConfigProvider : IRuntimeConfigProvider
        {
            private static readonly IReadOnlyList<ShopItemDefinition> Items = new[]
            {
                new ShopItemDefinition("shop.metal", "shop.test", ItemIds.MetalParts, 1, 0, 5),
                new ShopItemDefinition("shop.arrow", "shop.test", ItemIds.Arrow, 2, 2, 1)
            };

            public bool ShopAvailable { get; set; } = true;

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error) => Missing(out value, out error);
            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = Array.Empty<MapSpawnDefinition>();
                error = "not used";
                return false;
            }
            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error) => Missing(out value, out error);
            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error) => Missing(out value, out error);
            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error) => Missing(out value, out error);
            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error) => Missing(out value, out error);

            public bool TryGetNpc(string id, out NpcConfigDefinition value, out string error)
            {
                value = new NpcConfigDefinition("npc.shop", "npc.base.merchant.name", "npc/shared", NpcInteractionKind.Shop, "shop.test");
                error = null;
                return id == "npc.shop";
            }

            public bool TryGetShop(string shopId, out ShopDefinition definition, out string error)
            {
                definition = new ShopDefinition("shop.test", "shop.base.supplies.name");
                error = ShopAvailable && shopId == "shop.test" ? null : "missing shop";
                return error == null;
            }

            public bool TryGetShopItems(string shopId, out IReadOnlyList<ShopItemDefinition> definitions, out string error)
            {
                definitions = ShopAvailable && shopId == "shop.test" ? Items : Array.Empty<ShopItemDefinition>();
                error = ShopAvailable && shopId == "shop.test" ? null : "missing shop";
                return error == null;
            }

            public bool TryGetShopItem(string shopId, int itemId, out ShopItemDefinition definition, out string error)
            {
                definition = ShopAvailable
                    ? Items.FirstOrDefault(value => value.ItemId == itemId && value.ShopId == shopId)
                    : default;
                error = definition.ItemId > 0 ? null : "missing item";
                return error == null;
            }

            public bool TryGetItem(int itemId, out ItemDefinition value, out string error)
            {
                if (itemId == ItemIds.MetalParts)
                {
                    value = new ItemDefinition(itemId, "金属零件", "材料", "item.metal_parts.name", "item.metal_parts.desc", string.Empty,
                        ItemType.Material, 20, ItemUseEffect.None, 0);
                    error = null;
                    return true;
                }
                if (itemId == ItemIds.Arrow)
                {
                    value = new ItemDefinition(itemId, "箭矢", "弹药", "item.ammunition.arrow.name", "item.ammunition.arrow.desc", string.Empty,
                        ItemType.Ammunition, 20, ItemUseEffect.None, 0);
                    error = null;
                    return true;
                }
                value = default;
                error = "missing item";
                return false;
            }

            private static bool Missing<T>(out T value, out string error)
            {
                value = default;
                error = "not used";
                return false;
            }
        }

        private sealed class ShopLocalizationLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object
            {
                var assetPath = path == "languages.json"
                    ? "Assets/Game/Config/Localization/languages.json"
                    : $"Assets/Game/Config/Localization/{path}";
                return AssetDatabase.LoadAssetAtPath<T>(assetPath);
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
    }
}
