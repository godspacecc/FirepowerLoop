using System;
using System.Linq;
using Luban;
using NUnit.Framework;
using UnityEditor;

namespace Game.Tests
{
    public sealed class BasePreparationConfigTests
    {
        // 缺失适售商品行或仍使用零买价时，成功探索后仍无法购买；此测试约束实际运行配置。
        [Test]
        public void EveryOrdinaryCarriedItemCanBePurchasedAfterDiscovery()
        {
            var provider = new LubanRuntimeConfigProvider(() => new global::Tables(name =>
                new ByteBuf(AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>($"Assets/Game/Config/{name}.bytes").bytes)));
            Assert.That(provider.TryGetShopItems("shop.base.supplies", out var rows, out var error), Is.True, error);
            var expected = Enumerable.Range(1, 48).Except(new[] {10,11,12,13,14,47}).ToArray();
            foreach (var id in expected)
                Assert.That(rows.Any(row => row.ItemId == id && row.BuyPrice > 0), Is.True, $"物品{id}没有可购买商品行");
        }
    }
}
