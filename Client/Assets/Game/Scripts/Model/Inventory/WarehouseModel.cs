using System;
using Newtonsoft.Json.Linq;
using QFramework;

namespace Game
{
    public sealed class WarehouseModel : AbstractModel, IScopedSaveSectionProvider
    {
        public string SectionId => "another-day.warehouse";
        public SaveSectionScope Scope => SaveSectionScope.BaseOnly;
        public ItemContainer Container { get; private set; } = new ItemContainer(InventoryDefaults.WarehouseCapacity);

        protected override void OnInit()
        {
        }

        public bool Validate(JToken data, out string error)
        {
            if (data == null || data.Type != JTokenType.Object)
            {
                error = "仓库分区必须为对象";
                return false;
            }

            if (!TryReadCapacity(data, out var capacity, out error))
            {
                return false;
            }

            return InventorySaveContracts.ValidateSlots(
                data[InventorySaveContracts.SlotsKey],
                capacity,
                this.GetUtility<IRuntimeConfigProvider>(),
                out _,
                out error);
        }

        public JToken Capture()
        {
            return new JObject
            {
                [InventorySaveContracts.CapacityKey] = Container.Capacity,
                [InventorySaveContracts.SlotsKey] = InventorySaveContracts.CaptureSlots(Container)
            };
        }

        public void Restore(JToken data)
        {
            if (!Validate(data, out var error))
            {
                throw new InvalidOperationException(error);
            }

            var capacity = data[InventorySaveContracts.CapacityKey].Value<int>();
            InventorySaveContracts.ValidateSlots(
                data[InventorySaveContracts.SlotsKey],
                capacity,
                this.GetUtility<IRuntimeConfigProvider>(),
                out var slots,
                out _);
            Container = InventorySaveContracts.RestoreContainer(capacity, slots);
        }

        public void Reset()
        {
            Container = new ItemContainer(InventoryDefaults.WarehouseCapacity);
        }

        private static bool TryReadCapacity(JToken data, out int capacity, out string error)
        {
            capacity = 0;
            var token = data[InventorySaveContracts.CapacityKey];
            if (token?.Type != JTokenType.Integer)
            {
                error = "容量 无效";
                return false;
            }

            capacity = token.Value<int>();
            if (capacity < InventoryDefaults.WarehouseCapacity)
            {
                error = "容量过小";
                return false;
            }

            error = null;
            return true;
        }
    }
}
