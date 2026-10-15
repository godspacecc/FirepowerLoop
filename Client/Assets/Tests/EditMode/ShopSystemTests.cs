using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class ShopSystemTests
    {
        private string root;
        private ToggleShopConfigProvider config;
        private ItemContainerSnapshot emptyBag;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            root = Path.Combine(Path.GetTempPath(), "AnotherDayShopSystem", Guid.NewGuid().ToString("N"));
            config = new ToggleShopConfigProvider(new LubanRuntimeConfigProvider(LoadTables));
            InstallArchitecture();
            var save = Game.Interface.GetSystem<SaveSystem>();
            save.ConfigureForTests(root);
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.BaseSafe);
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                1001, 1, UnitType.Npc, UnitSource.MapPlaced, "npc.base.merchant", Vector3.zero,
                Quaternion.identity, UnitAuthorityMode.Local, 0, true), out var error), Is.True, error);
            emptyBag = Game.Interface.GetSystem<InventorySystem>().CapturePlayerBagSnapshot();
        }

        [TearDown]
        public void TearDown()
        {
            TestReset.ResetArchitecture();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void SellThenBuyCommitsGoldAndBagTogether()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 10, 20, out _, out var error), Is.True, error);
            PersistBeforeTrade();
            var shop = Open();

            var sell = shop.TrySell(ItemIds.MetalParts, 2);
            Assert.That(sell.Success, Is.True);
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(10));
            Assert.That(bag.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(8));

            var buy = shop.TryBuy(ItemIds.Arrow, 5);
            Assert.That(buy.Success, Is.True);
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(0));
            Assert.That(bag.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(5));

            var saved = Game.Interface.GetSystem<SaveSystem>().InspectBase();
            Assert.That(saved.IsValid, Is.True, saved.Error);
            Assert.That(saved.Data.Sections[CurrencyModel.SaveSectionId]["gold"].Value<long>(), Is.Zero);
            Assert.That(saved.Data.Sections["another-day.player-bag"]["slots"], Is.TypeOf<JArray>());
        }

        [Test]
        public void FailedValidationDoesNotChangeGoldBagOrFile()
        {
            Game.Interface.GetModel<CurrencyModel>().TrySetGold(1);
            PersistBeforeTrade();
            var before = File.ReadAllText(new SaveFileStore(root).BasePath);
            var result = Open().TryBuy(ItemIds.Medkit, 1);
            Assert.That(result.Failure, Is.EqualTo(ShopFailureReason.InsufficientGold));
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Medkit), Is.Zero);
            Assert.That(File.ReadAllText(new SaveFileStore(root).BasePath), Is.EqualTo(before));
        }

        [Test]
        public void NpcReleaseInvalidatesOldSessionAndQuantity()
        {
            Game.Interface.GetModel<CurrencyModel>().TrySetGold(100);
            PersistBeforeTrade();
            var shop = Open();
            shop.CloseForNpc(1001);
            var result = shop.TryBuy(ItemIds.Medkit, 1);
            Assert.That(result.Failure, Is.EqualTo(ShopFailureReason.SessionInvalid));
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(100));
        }

        [Test]
        public void InvalidAuthoritativeContextClosesSessionOnceAndRejectsStaleRequests()
        {
            Game.Interface.GetModel<CurrencyModel>().TrySetGold(100);
            PersistBeforeTrade();
            var beforeFile = ReadBaseFile();
            var beforeBag = BagSignature();
            var shop = Open();
            var events = new List<ShopSnapshot>();
            var unregister = Game.Interface.RegisterEvent<ShopChangedEvent>(evt => events.Add(evt.Snapshot));
            config.ShopAvailable = false;

            var invalidated = shop.TryBuy(ItemIds.Arrow, 1);
            var stale = shop.TryBuy(ItemIds.Arrow, 1);
            unregister.UnRegister();

            Assert.That(invalidated.Failure, Is.EqualTo(ShopFailureReason.StateNotAllowed));
            Assert.That(invalidated.Snapshot.ShopId, Is.Null.Or.Empty);
            Assert.That(stale.Failure, Is.EqualTo(ShopFailureReason.SessionInvalid));
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].ShopId, Is.Null.Or.Empty);
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(100));
            Assert.That(BagSignature(), Is.EqualTo(beforeBag));
            Assert.That(ReadBaseFile(), Is.EqualTo(beforeFile));

            config.ShopAvailable = true;
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            shop = Open();
            events.Clear();
            unregister = Game.Interface.RegisterEvent<ShopChangedEvent>(evt => events.Add(evt.Snapshot));
            config.NpcAvailable = false;
            invalidated = shop.TryBuy(ItemIds.Arrow, 1);
            stale = shop.TryBuy(ItemIds.Arrow, 1);
            unregister.UnRegister();
            Assert.That(invalidated.Failure, Is.EqualTo(ShopFailureReason.StateNotAllowed));
            Assert.That(stale.Failure, Is.EqualTo(ShopFailureReason.SessionInvalid));
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].ShopId, Is.Null.Or.Empty);
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(100));
            Assert.That(BagSignature(), Is.EqualTo(beforeBag));
            Assert.That(ReadBaseFile(), Is.EqualTo(beforeFile));
        }

        [Test]
        public void SaveDomainGateMatrixRejectsOpenAndInvalidatesActiveTradeWithoutMutation()
        {
            Game.Interface.GetModel<CurrencyModel>().TrySetGold(100);
            PersistBeforeTrade();
            var beforeFile = ReadBaseFile();
            var beforeBag = BagSignature();
            var saveModel = Game.Interface.GetModel<SaveModel>();

            AssertOpenRejected(() => saveModel.SetBase(new BaseSaveData(), false));
            AssertOpenRejected(() => saveModel.SetExpedition(new ExpeditionSaveData
            {
                ExpeditionId = "expedition.test",
                MapId = MapIds.SurvivalSample
            }));
            AssertOpenRejected(saveModel.ClearCurrent);

            AssertActiveTradeInvalidated(() => saveModel.SetBase(new BaseSaveData(), false));
            AssertActiveTradeInvalidated(() => saveModel.SetExpedition(new ExpeditionSaveData
            {
                ExpeditionId = "expedition.test",
                MapId = MapIds.SurvivalSample
            }));
            AssertActiveTradeInvalidated(saveModel.ClearCurrent);

            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(100));
            Assert.That(BagSignature(), Is.EqualTo(beforeBag));
            Assert.That(ReadBaseFile(), Is.EqualTo(beforeFile));
        }

        [Test]
        public void TradeFailureMatrixAndCrossSlotCandidatesNeverPartiallyMutate()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Game.Interface.GetModel<CurrencyModel>().TrySetGold(0);
            PersistBeforeTrade();
            var shop = Open();
            AssertFailureWithoutMutation(() => shop.TryBuy(ItemIds.Medkit, 1), ShopFailureReason.InsufficientGold);
            AssertFailureWithoutMutation(() => shop.TryBuy(ItemIds.MetalParts, 1), ShopFailureReason.NotForSale);
            AssertFailureWithoutMutation(() => shop.TryBuy(ItemIds.Arrow, 0), ShopFailureReason.InvalidQuantity);
            AssertFailureWithoutMutation(() => shop.TryBuy(ItemIds.Arrow, -1), ShopFailureReason.InvalidQuantity);

            config.MaximumArrowBuyPrice = true;
            AssertFailureWithoutMutation(() => shop.TryBuy(ItemIds.Arrow, int.MaxValue), ShopFailureReason.InsufficientGold);
            config.MaximumArrowBuyPrice = false;

            config.DisableArrowSell = true;
            AssertFailureWithoutMutation(() => shop.TrySell(ItemIds.Arrow, 1), ShopFailureReason.NotAccepted);
            config.DisableArrowSell = false;

            RestoreEmptyBag();
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 2, 1, out _, out var error), Is.True, error);
            AssertFailureWithoutMutation(() => shop.TrySell(ItemIds.MetalParts, 3), ShopFailureReason.InsufficientItems);

            RestoreEmptyBag();
            Assert.That(bag.TryAdd(ItemIds.ShortSword, InventoryDefaults.PlayerBagCapacity, 1, out _, out error), Is.True, error);
            Game.Interface.GetModel<CurrencyModel>().TrySetGold(100);
            AssertFailureWithoutMutation(() => shop.TryBuy(ItemIds.Arrow, 1), ShopFailureReason.InsufficientBagSpace);

            RestoreEmptyBag();
            Assert.That(bag.TryAdd(ItemIds.Arrow, 1, 20, out _, out error), Is.True, error);
            Game.Interface.GetModel<CurrencyModel>().TrySetGold(long.MaxValue);
            AssertFailureWithoutMutation(() => shop.TrySell(ItemIds.Arrow, 1), ShopFailureReason.Overflow);

            RestoreEmptyBag();
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 25, 20, out _, out error), Is.True, error);
            Game.Interface.GetModel<CurrencyModel>().TrySetGold(0);
            var success = shop.TrySell(ItemIds.MetalParts, 22);
            Assert.That(success.Success, Is.True);
            Assert.That(bag.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(3));
            Assert.That(bag.Slots, Has.Count.EqualTo(1));
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(110));
        }

        [Test]
        public void SaveFailureRestoresMemoryDtoAndNextFlushPersistsPreTradeState()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 5, 20, out _, out var error), Is.True, error);
            Game.Interface.GetModel<CurrencyModel>().TrySetGold(7);
            PersistBeforeTrade();
            var beforeFile = ReadBaseFile();
            var beforeDto = JToken.FromObject(Game.Interface.GetModel<SaveModel>().CurrentBase).ToString();
            var beforeBag = BagSignature();
            var save = Game.Interface.GetSystem<SaveSystem>();
            save.FailNextOfflineStorageCleanupForTests("shop flush failure");

            var result = Open().TrySell(ItemIds.MetalParts, 2);

            Assert.That(result.Failure, Is.EqualTo(ShopFailureReason.SaveFailed));
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(7));
            Assert.That(BagSignature(), Is.EqualTo(beforeBag));
            Assert.That(JToken.FromObject(Game.Interface.GetModel<SaveModel>().CurrentBase).ToString(), Is.EqualTo(beforeDto));
            Assert.That(ReadBaseFile(), Is.EqualTo(beforeFile));
            Assert.That(save.Flush(out error), Is.True, error);
            var persisted = save.InspectBase();
            Assert.That(persisted.IsValid, Is.True, persisted.Error);
            Assert.That(persisted.Data.Sections[CurrencyModel.SaveSectionId]["gold"].Value<long>(), Is.EqualTo(7));
            Assert.That(ReadBaseFile(), Is.Not.Empty);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(5));
        }

        [Test]
        public void EventReentryIsRejectedAndFinallyAllowsNextExplicitTrade()
        {
            Game.Interface.GetModel<CurrencyModel>().TrySetGold(100);
            PersistBeforeTrade();
            var shop = Open();
            ShopTradeResult reentrant = default;
            var attempted = false;
            var unregister = Game.Interface.RegisterEvent<ShopChangedEvent>(evt =>
            {
                if (attempted || evt.Snapshot.LastFailure != ShopFailureReason.None) return;
                attempted = true;
                reentrant = shop.TryBuy(ItemIds.Arrow, 1);
            });

            var outer = shop.TryBuy(ItemIds.Arrow, 1);
            unregister.UnRegister();

            Assert.That(outer.Success, Is.True);
            Assert.That(reentrant.Failure, Is.EqualTo(ShopFailureReason.OperationInProgress));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(98));
            var next = shop.TryBuy(ItemIds.Arrow, 1);
            Assert.That(next.Success, Is.True);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(2));
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(96));
        }

        private ShopSystem Open()
        {
            var shop = Game.Interface.GetSystem<ShopSystem>();
            Assert.That(shop.TryOpen("npc.base.merchant", 1001, "shop.base.supplies", out _, out var error), Is.True, error);
            return shop;
        }

        private void PersistBeforeTrade()
        {
            var save = Game.Interface.GetSystem<SaveSystem>();
            save.MarkDirty("shop test setup");
            Assert.That(save.Flush(out var error), Is.True, error);
        }

        private void AssertOpenRejected(Action arrangeInvalidState)
        {
            var save = Game.Interface.GetModel<SaveModel>();
            save.SetBase(new BaseSaveData(), true);
            arrangeInvalidState();
            var events = 0;
            var unregister = Game.Interface.RegisterEvent<ShopChangedEvent>(_ => events++);
            Assert.That(Game.Interface.GetSystem<ShopSystem>().TryOpen(
                "npc.base.merchant", 1001, "shop.base.supplies", out var snapshot, out _), Is.False);
            unregister.UnRegister();
            Assert.That(snapshot.ShopId, Is.Null.Or.Empty);
            Assert.That(events, Is.Zero);
        }

        private void AssertActiveTradeInvalidated(Action arrangeInvalidState)
        {
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            var shop = Open();
            arrangeInvalidState();
            var events = 0;
            var unregister = Game.Interface.RegisterEvent<ShopChangedEvent>(_ => events++);
            var result = shop.TryBuy(ItemIds.Arrow, 1);
            unregister.UnRegister();
            Assert.That(result.Failure, Is.EqualTo(ShopFailureReason.StateNotAllowed));
            Assert.That(result.Snapshot.ShopId, Is.Null.Or.Empty);
            Assert.That(events, Is.EqualTo(1));
        }

        private void AssertFailureWithoutMutation(Func<ShopTradeResult> act, ShopFailureReason expected)
        {
            var gold = Game.Interface.GetModel<CurrencyModel>().Gold;
            var bag = BagSignature();
            var file = ReadBaseFile();
            var result = act();
            Assert.That(result.Failure, Is.EqualTo(expected));
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(gold));
            Assert.That(BagSignature(), Is.EqualTo(bag));
            Assert.That(ReadBaseFile(), Is.EqualTo(file));
        }

        private string ReadBaseFile() => File.ReadAllText(new SaveFileStore(root).BasePath);

        private static string BagSignature() => string.Join("|", Game.Interface.GetModel<PlayerBagModel>().Container.Slots
            .OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key}:{pair.Value.ItemId}:{pair.Value.Amount}"));

        private void RestoreEmptyBag()
        {
            Game.Interface.GetSystem<InventorySystem>().RestorePlayerBagSnapshot(emptyBag);
        }

        private void InstallArchitecture()
        {
            var architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(architecture, true);
            architecture.RegisterModel(new SaveModel());
            architecture.RegisterModel(new AppStateModel());
            architecture.RegisterModel(new MapSceneModel());
            architecture.RegisterModel(new UnitModel());
            architecture.RegisterModel(new PlayerBagModel());
            architecture.RegisterModel(new WarehouseModel());
            architecture.RegisterModel(new CurrencyModel());
            architecture.RegisterUtility<IRuntimeConfigProvider>(config);
            architecture.RegisterSystem(new SaveSystem());
            architecture.RegisterSystem(new UnitSystem());
            architecture.RegisterSystem(new InventorySystem());
            architecture.RegisterSystem(new ShopSystem());
        }

        private static global::Tables LoadTables()
        {
            return new global::Tables(name =>
            {
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/Game/Config/{name}.bytes");
                Assert.That(asset, Is.Not.Null, name);
                return new ByteBuf(asset.bytes);
            });
        }

        private sealed class ToggleShopConfigProvider : IRuntimeConfigProvider
        {
            private readonly IRuntimeConfigProvider inner;

            public ToggleShopConfigProvider(IRuntimeConfigProvider inner)
            {
                this.inner = inner;
            }

            public bool ShopAvailable { get; set; } = true;
            public bool NpcAvailable { get; set; } = true;
            public bool DisableArrowSell { get; set; }
            public bool MaximumArrowBuyPrice { get; set; }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error) => inner.TryGetMap(id, out value, out error);
            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error) => inner.TryGetMapSpawns(mapId, out values, out error);
            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error) => inner.TryGetPlayer(id, out value, out error);
            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error) => inner.TryGetEnemy(id, out value, out error);
            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error) => inner.TryGetLoot(id, out value, out error);
            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error) => inner.TryGetExtraction(id, out value, out error);
            public bool TryGetItem(int itemId, out ItemDefinition value, out string error) => inner.TryGetItem(itemId, out value, out error);

            public bool TryGetNpc(string id, out NpcConfigDefinition value, out string error)
            {
                if (NpcAvailable) return inner.TryGetNpc(id, out value, out error);
                value = default;
                error = $"TbNpc[{id}] 测试失效";
                return false;
            }

            public bool TryGetShop(string shopId, out ShopDefinition definition, out string error)
            {
                if (ShopAvailable) return inner.TryGetShop(shopId, out definition, out error);
                definition = default;
                error = $"TbShop[{shopId}] 测试失效";
                return false;
            }

            public bool TryGetShopItems(string shopId, out IReadOnlyList<ShopItemDefinition> definitions, out string error)
            {
                if (!ShopAvailable)
                {
                    definitions = Array.Empty<ShopItemDefinition>();
                    error = $"TbShop[{shopId}] 测试失效";
                    return false;
                }
                if (!inner.TryGetShopItems(shopId, out var source, out error))
                {
                    definitions = Array.Empty<ShopItemDefinition>();
                    return false;
                }
                definitions = source.Select(Override).ToArray();
                return true;
            }

            public bool TryGetShopItem(string shopId, int itemId, out ShopItemDefinition definition, out string error)
            {
                if (!ShopAvailable)
                {
                    definition = default;
                    error = $"TbShopItem[{shopId}/{itemId}] 测试失效";
                    return false;
                }
                if (!inner.TryGetShopItem(shopId, itemId, out definition, out error))
                {
                    definition = default;
                    return false;
                }
                definition = Override(definition);
                return true;
            }

            private ShopItemDefinition Override(ShopItemDefinition value)
            {
                if (value.ItemId != ItemIds.Arrow) return value;
                return new ShopItemDefinition(
                    value.Id,
                    value.ShopId,
                    value.ItemId,
                    value.Order,
                    MaximumArrowBuyPrice ? int.MaxValue : value.BuyPrice,
                    DisableArrowSell ? 0 : value.SellPrice);
            }
        }
    }
}
