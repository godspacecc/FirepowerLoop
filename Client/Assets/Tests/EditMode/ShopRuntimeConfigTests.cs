using System;
using System.Collections.Generic;
using System.Linq;
using Luban;
using NUnit.Framework;
using UnityEditor;

namespace Game.Tests
{
    public sealed class ShopRuntimeConfigTests
    {
        [Test]
        public void GeneratedShopConfigurationMatchesFirstReleaseContract()
        {
            var provider = new LubanRuntimeConfigProvider(LoadTables);
            Assert.That(provider.TryGetShop("shop.base.supplies", out var shop, out var error), Is.True, error);
            Assert.That(shop.NameKey, Is.EqualTo("shop.base.supplies.name"));
            Assert.That(provider.TryGetShopItems(shop.Id, out var items, out error), Is.True, error);
            Assert.That(items.Count, Is.EqualTo(42));
            Assert.That(items.Select(item => item.ItemId), Is.EquivalentTo(Enumerable.Range(1,48).Except(new[]{10,11,12,13,14,47})));
            Assert.That(items.All(item=>item.BuyPrice>0&&item.SellPrice>0),Is.True);
            Assert.That(items.Where(item=>item.InitiallyOpen).Select(item=>item.ItemId),Is.EquivalentTo(new[]{3,4,7,8}));
            Assert.That(provider.TryGetShopItem(shop.Id, 3, out var medkit, out error), Is.True, error);
            Assert.That(medkit.BuyPrice, Is.EqualTo(40));
            Assert.That(provider.TryGetNpc("npc.base.merchant", out var npc, out error), Is.True, error);
            Assert.That(npc.InteractionKind, Is.EqualTo(global::NpcInteractionKind.Shop));
            Assert.That(npc.InteractionParam, Is.EqualTo(shop.Id));
        }

        [Test]
        public void InvalidShopCatalogMatrixIsRejectedWithTableRecordAndFieldContext()
        {
            AssertInvalid((shops, _, __) => shops[0] = new ShopDefinition(string.Empty, "shop.name"), "TbShop", "<empty>", "Id");
            AssertInvalid((shops, _, __) => shops[0] = new ShopDefinition("shop.valid", string.Empty), "TbShop", "shop.valid", "NameKey");
            AssertInvalid((_, items, __) => items[0] = Item("", "shop.valid", 1, 1, 1, 1), "TbShopItem", "<empty>", "Id");
            AssertInvalid((_, items, __) => items[0] = Item("item.missing-shop", "shop.missing", 1, 1, 1, 1), "TbShopItem", "item.missing-shop", "ShopId");
            AssertInvalid((_, items, __) => items[0] = Item("item.bad-item", "shop.valid", 999, 1, 1, 1), "TbShopItem", "item.bad-item", "ItemId");
            AssertInvalid((shops, _, __) => shops.Add(new ShopDefinition("shop.valid", "shop.other")), "TbShop", "shop.valid", "Id");
            AssertInvalid((_, items, __) => items.Add(Item("item.valid", "shop.valid", 2, 2, 1, 1)), "TbShopItem", "item.valid", "Id");
            AssertInvalid((_, items, __) => items.Add(Item("item.same", "shop.valid", 1, 2, 1, 1)), "TbShopItem", "item.same", "ShopId+ItemId");
            AssertInvalid((_, items, __) => items[0] = Item("item.bad-order", "shop.valid", 1, 0, 1, 1), "TbShopItem", "item.bad-order", "Order");
            AssertInvalid((_, items, __) => items.Add(Item("item.same-order", "shop.valid", 2, 1, 1, 1)), "TbShopItem", "item.same-order", "Order");
            AssertInvalid((_, items, __) => items[0] = Item("item.buy-negative", "shop.valid", 1, 1, -1, 1), "TbShopItem", "item.buy-negative", "BuyPrice");
            AssertInvalid((_, items, __) => items[0] = Item("item.sell-negative", "shop.valid", 1, 1, 1, -1), "TbShopItem", "item.sell-negative", "SellPrice");
            AssertInvalid((_, items, __) => items[0] = Item("item.zero", "shop.valid", 1, 1, 0, 0), "TbShopItem", "item.zero", "BuyPrice/SellPrice");
            AssertInvalid((_, __, npcs) => npcs[0] = Npc(string.Empty), "TbNpc", "npc.shop", "InteractionParam");
            AssertInvalid((_, __, npcs) => npcs[0] = Npc("shop.missing"), "TbNpc", "npc.shop", "InteractionParam");
        }

        private static void AssertInvalid(
            Action<List<ShopDefinition>, List<ShopItemDefinition>, List<NpcConfigDefinition>> mutate,
            params string[] expectedContext)
        {
            var shops = new List<ShopDefinition> { new ShopDefinition("shop.valid", "shop.name") };
            var items = new List<ShopItemDefinition> { Item("item.valid", "shop.valid", 1, 1, 1, 1) };
            var npcs = new List<NpcConfigDefinition> { Npc("shop.valid") };
            mutate(shops, items, npcs);

            var error = LubanRuntimeConfigProvider.ValidateShopCatalog(
                shops,
                items,
                npcs,
                itemId => itemId == 1 || itemId == 2);

            Assert.That(error, Is.Not.Null.And.Not.Empty);
            foreach (var context in expectedContext)
                Assert.That(error, Does.Contain(context));
        }

        private static ShopItemDefinition Item(string id, string shopId, int itemId, int order, int buyPrice, int sellPrice) =>
            new ShopItemDefinition(id, shopId, itemId, order, buyPrice, sellPrice);

        private static NpcConfigDefinition Npc(string shopId) =>
            new NpcConfigDefinition("npc.shop", "npc.shop.name", "Prefabs/Characters/GrayboxNpc", global::NpcInteractionKind.Shop, shopId);

        private static global::Tables LoadTables()
        {
            return new global::Tables(name =>
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>($"Assets/Game/Config/{name}.bytes");
                Assert.That(asset, Is.Not.Null, name);
                return new ByteBuf(asset.bytes);
            });
        }
    }
}
