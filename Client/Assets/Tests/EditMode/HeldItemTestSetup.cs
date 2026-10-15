using System.Linq;
using QFramework;

namespace Game.Tests
{
    /// <summary>旧外观测试的布置适配：武器先存在于实际库存，再设置单一手持；不增加运行时代码写入口。</summary>
    internal static class HeldItemTestSetup
    {
        internal static void SetSlotItem(this WeaponEquipmentModel model, WeaponSlot kind, int itemId)
        {
            if (itemId == 0) { if (model.ActiveSlot == kind) model.Reset(); return; }
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var source = bag.Slots.FirstOrDefault(p => p.Value.ItemId == itemId);
            if (source.Value == null)
            {
                bag.TryAdd(itemId, 1, 1, out _, out _);
                source = bag.Slots.First(p => p.Value.ItemId == itemId);
            }
            model.SetHeld(new InventorySlotRef(InventoryContainerKind.PlayerBag, source.Key), itemId, source.Value);
        }
        internal static bool TrySetActiveSlot(this WeaponEquipmentModel model, WeaponSlot kind, out string error) =>
            Game.Interface.GetSystem<WeaponEquipmentSystem>().TrySetActiveSlot(kind, out error);
    }
}
