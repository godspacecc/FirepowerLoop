using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>火把实例余量、克隆和保存契约；不创建或修改正式美术资源。</summary>
    public sealed class HandheldTorchTests
    {
        private static PropertyInfo Fuel => typeof(ItemStack).GetProperty("TorchRemainingSeconds");

        [Test]
        public void FuelBelongsToInventoryInstanceAndSurvivesClone()
        {
            Assert.That(Fuel, Is.Not.Null, "火把缺少按库存实例保存的燃烧余量");
            var first = new ItemStack(48, 1);
            Fuel.SetValue(first, 123f);
            var second = new ItemStack(48, 1);
            Assert.That(Fuel.GetValue(first.Clone()), Is.EqualTo(123f));
            Assert.That(Fuel.GetValue(second), Is.Null);
        }

        [Test]
        public void CapturedInventoryKeepsSpentTorchDistinctFromFreshTorch()
        {
            Assert.That(Fuel, Is.Not.Null);
            var container = new ItemContainer(2);
            container.TryAdd(48, 2, 1, out _, out _);
            Fuel.SetValue(container.Slots[0], 0f);
            Fuel.SetValue(container.Slots[1], 120f);
            var slots = InventorySaveContracts.CaptureSlots(container);
            Assert.That(slots[0]["torchRemainingSeconds"].Value<float>(), Is.Zero);
            Assert.That(slots[1]["torchRemainingSeconds"].Value<float>(), Is.EqualTo(120f));
        }
    }
}
