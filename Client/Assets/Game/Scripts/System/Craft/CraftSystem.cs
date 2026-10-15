using System;
using System.Collections.Generic;
using QFramework;

namespace Game
{
    public sealed class CraftSystem : AbstractSystem
    {
        protected override void OnInit()
        {
        }

        public bool TryGetQuotes(out IReadOnlyList<CraftQuote> quotes, out string error)
        {
            return TryGetQuotes(1, out quotes, out error);
        }

        public bool TryGetQuotes(int batchCount, out IReadOnlyList<CraftQuote> quotes, out string error)
        {
            quotes = Array.Empty<CraftQuote>();
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetCraftRecipes(out var recipes, out error))
            {
                return false;
            }

            var inventory = this.GetSystem<InventorySystem>();
            var result = new List<CraftQuote>(recipes.Count);
            foreach (var recipe in recipes)
            {
                if (!inventory.TryGetCraftInventoryQuote(recipe, batchCount, out var quote, out error))
                {
                    return false;
                }

                result.Add(new CraftQuote(recipe, quote));
            }

            quotes = result;
            error = null;
            return true;
        }

        public bool TryCraft(string recipeId, out CraftFailureReason reason, out string error)
        {
            return TryCraft(recipeId, 1, out _, out reason, out error);
        }

        public bool TryCraft(
            string recipeId,
            int batchCount,
            out CraftCommitResult result,
            out CraftFailureReason reason,
            out string error)
        {
            result = default;
            reason = CraftFailureReason.ConfigUnavailable;
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetCraftRecipe(recipeId, out var recipe, out error))
            {
                return false;
            }

            var overflowDropPort = ResolveOverflowDropPort();
            if (!this.GetSystem<InventorySystem>().TryCommitCraftInventory(
                    recipe,
                    batchCount,
                    overflowDropPort,
                    out result,
                    out reason,
                    out error))
            {
                return false;
            }

            reason = CraftFailureReason.None;
            error = null;
            try
            {
                this.SendEvent(new CraftCompletedEvent(result.RecipeId, result.OutputItemId, result.OutputAmount));
            }
            catch (Exception e)
            {
            Log.Error($"[CraftSystem] CraftCompletedEvent 监听器失败：{e.Message}");
            }

            return true;
        }

        private ICraftOverflowDropPort ResolveOverflowDropPort()
        {
            try
            {
                return this.GetUtility<ICraftOverflowDropPort>();
            }
            catch
            {
                return null;
            }
        }
    }
}
