using QFramework;

namespace Game
{
    public sealed class SellShopItemCommand : AbstractCommand<ShopTradeResult>
    {
        private readonly int itemId;
        private readonly int quantity;

        public SellShopItemCommand(int itemId, int quantity)
        {
            this.itemId = itemId;
            this.quantity = quantity;
        }

        protected override ShopTradeResult OnExecute()
        {
            return this.GetSystem<ShopSystem>().TrySell(itemId, quantity);
        }
    }
}
