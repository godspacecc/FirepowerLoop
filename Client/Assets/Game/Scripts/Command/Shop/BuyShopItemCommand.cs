using QFramework;

namespace Game
{
    public sealed class BuyShopItemCommand : AbstractCommand<ShopTradeResult>
    {
        private readonly int itemId;
        private readonly int quantity;

        public BuyShopItemCommand(int itemId, int quantity)
        {
            this.itemId = itemId;
            this.quantity = quantity;
        }

        protected override ShopTradeResult OnExecute()
        {
            return this.GetSystem<ShopSystem>().TryBuy(itemId, quantity);
        }
    }
}
