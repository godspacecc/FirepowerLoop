using System;
using Newtonsoft.Json.Linq;
using QFramework;

namespace Game
{
    public enum PlayerBagRiskState
    {
        BaseSafe = 0,
        EnteringExpedition = 1,
        InExpedition = 2,
        RecoveryRequired = 3
    }

    /// <summary>持有独立背包、四格口袋及共同远征风险；负责保存，不处理界面或物品操作规则。</summary>
    public sealed class PlayerBagModel : AbstractModel, IScopedSaveSectionProvider
    {
        public string SectionId => "another-day.player-bag";
        public SaveSectionScope Scope => SaveSectionScope.SharedPlayer;
        public ItemContainer Container { get; private set; } = new ItemContainer(InventoryDefaults.PlayerBagCapacity);
        /// <summary>独立存储容器，格号为 0～3；随玩家保存和风险清理，旧档恢复为空。</summary>
        public ItemContainer Pocket { get; private set; } = new ItemContainer(InventoryDefaults.PlayerPocketCapacity);
        public PlayerBagRiskState RiskState { get; private set; } = PlayerBagRiskState.BaseSafe;

        protected override void OnInit()
        {
        }

        public bool Validate(JToken data, out string error)
        {
            if (data == null || data.Type != JTokenType.Object)
            {
                error = "玩家背包分区必须为对象";
                return false;
            }

            if (!TryReadCapacity(data, InventoryDefaults.PlayerBagCapacity, out var capacity, out error))
            {
                return false;
            }

            var riskToken = data[InventorySaveContracts.RiskStateKey];
            if (riskToken?.Type != JTokenType.String ||
                !Enum.TryParse(riskToken.Value<string>(), out PlayerBagRiskState parsedRiskState) ||
                !Enum.IsDefined(typeof(PlayerBagRiskState), parsedRiskState))
            {
                error = "玩家背包 riskState 无效";
                return false;
            }

            if (!InventorySaveContracts.ValidateSlots(
                data[InventorySaveContracts.SlotsKey],
                capacity,
                this.GetUtility<IRuntimeConfigProvider>(),
                out _,
                out error)) return false;

            // 只允许旧档缺省；显式 null、错误类型和非法格号均属于损坏数据。
            return ((JObject)data).Property("pocketSlots") == null || InventorySaveContracts.ValidateSlots(
                data["pocketSlots"], InventoryDefaults.PlayerPocketCapacity,
                this.GetUtility<IRuntimeConfigProvider>(), out _, out error);
        }

        /// <summary>正常保存时仅捕获现行背包字段，旧存档的额外字段不会再次写入。</summary>
        public JToken Capture()
        {
            return new JObject
            {
                [InventorySaveContracts.CapacityKey] = Container.Capacity,
                [InventorySaveContracts.RiskStateKey] = RiskState.ToString(),
                [InventorySaveContracts.SlotsKey] = InventorySaveContracts.CaptureSlots(Container),
                ["pocketSlots"] = InventorySaveContracts.CaptureSlots(Pocket)
            };
        }

        public void Restore(JToken data)
        {
            if (!Validate(data, out var error))
            {
                throw new InvalidOperationException(error);
            }

            var capacity = data[InventorySaveContracts.CapacityKey].Value<int>();
            var riskText = data[InventorySaveContracts.RiskStateKey].Value<string>();
            InventorySaveContracts.ValidateSlots(
                data[InventorySaveContracts.SlotsKey],
                capacity,
                this.GetUtility<IRuntimeConfigProvider>(),
                out var slots,
                out _);
            var next = InventorySaveContracts.RestoreContainer(capacity, slots);
            InventorySaveContracts.ValidateSlots(data["pocketSlots"] ?? new JArray(), InventoryDefaults.PlayerPocketCapacity,
                this.GetUtility<IRuntimeConfigProvider>(), out var pocketSlots, out _);
            var nextPocket = InventorySaveContracts.RestoreContainer(InventoryDefaults.PlayerPocketCapacity, pocketSlots);
            Container = next;
            Pocket = nextPocket;
            RiskState = Enum.Parse<PlayerBagRiskState>(riskText);
        }

        public void Reset()
        {
            Container = new ItemContainer(InventoryDefaults.PlayerBagCapacity);
            Pocket = new ItemContainer(InventoryDefaults.PlayerPocketCapacity);
            RiskState = PlayerBagRiskState.BaseSafe;
        }

        internal void SetRiskState(PlayerBagRiskState riskState)
        {
            RiskState = riskState;
        }

        internal void RestoreContainer(ItemContainerSnapshot snapshot)
        {
            Container.RestoreSnapshot(snapshot);
        }

        internal void ClearContainer()
        {
            Container = new ItemContainer(InventoryDefaults.PlayerBagCapacity);
            Pocket = new ItemContainer(InventoryDefaults.PlayerPocketCapacity);
        }

        private static bool TryReadCapacity(JToken data, int minimum, out int capacity, out string error)
        {
            capacity = 0;
            var token = data[InventorySaveContracts.CapacityKey];
            if (token?.Type != JTokenType.Integer)
            {
                error = "容量 无效";
                return false;
            }

            capacity = token.Value<int>();
            if (capacity < minimum)
            {
                error = "容量过小";
                return false;
            }

            error = null;
            return true;
        }
    }
}
