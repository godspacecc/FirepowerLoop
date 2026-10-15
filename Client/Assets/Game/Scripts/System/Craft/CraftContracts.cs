using System;
using System.Collections.Generic;

namespace Game
{
    public enum CraftFailureReason
    {
        None = 0,
        ConfigUnavailable = 1,
        StateNotAllowed = 2,
        MaterialsInsufficient = 3,
        OutputCapacityInsufficient = 4,
        InventoryOperationFailed = 5,
        DropCreationFailed = 6,
        InvalidBatchCount = 7
    }

    public readonly struct CraftMaterialQuote
    {
        public CraftMaterialQuote(int itemId, int requiredAmount, int bagOwnedAmount, int warehouseOwnedAmount)
        {
            ItemId = itemId;
            RequiredAmount = requiredAmount;
            BagOwnedAmount = bagOwnedAmount;
            WarehouseOwnedAmount = warehouseOwnedAmount;
        }

        public int ItemId { get; }
        public int RequiredAmount { get; }
        /// <summary>随身材料总量（背包加口袋）；保留字段名兼容现有制作界面。</summary>
        public int BagOwnedAmount { get; }
        public int WarehouseOwnedAmount { get; }
        public bool HasEnough => BagOwnedAmount + WarehouseOwnedAmount >= RequiredAmount;
        public int TotalOwnedAmount => BagOwnedAmount + WarehouseOwnedAmount;
    }

    public readonly struct CraftInventoryQuote
    {
        public CraftInventoryQuote(
            IReadOnlyList<CraftMaterialQuote> materials,
            bool stateAllowed,
            bool hasOutputCapacity,
            int outputAmount,
            int storableInBagAmount,
            int storableInWarehouseAmount,
            int droppedAmount,
            CraftFailureReason failureReason)
        {
            Materials = materials ?? Array.Empty<CraftMaterialQuote>();
            StateAllowed = stateAllowed;
            HasOutputCapacity = hasOutputCapacity;
            OutputAmount = outputAmount;
            StorableInBagAmount = storableInBagAmount;
            StorableInWarehouseAmount = storableInWarehouseAmount;
            DroppedAmount = droppedAmount;
            FailureReason = failureReason;
        }

        public IReadOnlyList<CraftMaterialQuote> Materials { get; }
        public bool StateAllowed { get; }
        public bool HasOutputCapacity { get; }
        public int OutputAmount { get; }
        public int StorableInBagAmount { get; }
        public int StorableInWarehouseAmount { get; }
        public int DroppedAmount { get; }
        public CraftFailureReason FailureReason { get; }
        public bool CanCraft => StateAllowed && FailureReason == CraftFailureReason.None;
    }

    public readonly struct CraftQuote
    {
        public CraftQuote(CraftRecipeDefinition recipe, CraftInventoryQuote inventory)
        {
            Recipe = recipe;
            Inventory = inventory;
        }

        public CraftRecipeDefinition Recipe { get; }
        public CraftInventoryQuote Inventory { get; }
    }

    public readonly struct CraftCompletedEvent
    {
        public CraftCompletedEvent(string recipeId, int outputItemId, int outputAmount)
        {
            RecipeId = recipeId;
            OutputItemId = outputItemId;
            OutputAmount = outputAmount;
        }

        public string RecipeId { get; }
        public int OutputItemId { get; }
        public int OutputAmount { get; }
    }

    public readonly struct CraftCommitResult
    {
        public CraftCommitResult(
            string recipeId,
            int outputItemId,
            int outputAmount,
            int storedInBagAmount,
            int storedInWarehouseAmount,
            int droppedAmount)
        {
            RecipeId = recipeId;
            OutputItemId = outputItemId;
            OutputAmount = outputAmount;
            StoredInBagAmount = storedInBagAmount;
            StoredInWarehouseAmount = storedInWarehouseAmount;
            DroppedAmount = droppedAmount;
        }

        public string RecipeId { get; }
        public int OutputItemId { get; }
        public int OutputAmount { get; }
        public int StoredInBagAmount { get; }
        public int StoredInWarehouseAmount { get; }
        public int DroppedAmount { get; }
    }

    public readonly struct CraftOverflowDropReceipt
    {
        public CraftOverflowDropReceipt(IReadOnlyList<int> lootUnitIds)
        {
            LootUnitIds = lootUnitIds ?? Array.Empty<int>();
        }

        public IReadOnlyList<int> LootUnitIds { get; }
        public bool HasDrops => LootUnitIds != null && LootUnitIds.Count > 0;
        public static CraftOverflowDropReceipt Empty => new CraftOverflowDropReceipt(Array.Empty<int>());
    }

    public interface ICraftOverflowDropPort : QFramework.IUtility
    {
        bool TryCreateCraftOverflowDrop(
            int itemId,
            int amount,
            out CraftOverflowDropReceipt receipt,
            out string error);

        void RollbackCraftOverflowDrop(CraftOverflowDropReceipt receipt);
    }
}
