using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;

namespace Game
{
    /// <summary>管理单个基地商店会话，并原子提交金币、随身背包与基地存档。</summary>
    public sealed class ShopSystem : AbstractSystem
    {
        private string shopId;
        private string shopNameKey;
        private string npcConfigId;
        private int npcUnitId;
        private ShopTradeKind tradeKind;
        private int selectedItemId;
        private int quantity = 1;
        private bool operationInProgress;
        private ShopSnapshot snapshot = ShopSnapshot.Empty();

        public ShopSnapshot Snapshot => snapshot;

        protected override void OnInit()
        {
            var currency = this.GetModel<CurrencyModel>();
            if (!this.GetSystem<SaveSystem>().RegisterSection(currency, out var error))
            {
                throw new InvalidOperationException($"注册金币存档分区 {CurrencyModel.SaveSectionId} 失败：{error}");
            }

            this.RegisterEvent<MapLoadingStartedEvent>(_ => Close());
            this.RegisterEvent<BasePreparationChangedEvent>(_=>{if(HasSession&&!operationInProgress){snapshot=BuildSnapshot(ShopFailureReason.None,null,false,true);PublishSnapshot();}});
            this.RegisterEvent<MapCurrentReleasedEvent>(_ => Close());
            this.RegisterEvent<UnitRemovedEvent>(evt => CloseForNpc(evt.Snapshot.UnitId));
        }

        /// <summary>校验基地、活动存档域、NPC 与配置后打开会话；同一会话重复打开只刷新快照。</summary>
        public bool TryOpen(string requestedNpcConfigId, int requestedNpcUnitId, string requestedShopId, out ShopSnapshot result, out string error)
        {
            if (!TryValidateContext(requestedNpcConfigId, requestedNpcUnitId, requestedShopId, out var shop, out error))
            {
                result = ShopSnapshot.Empty();
                return false;
            }

            if (!string.Equals(shopId, requestedShopId, StringComparison.Ordinal) ||
                !string.Equals(npcConfigId, requestedNpcConfigId, StringComparison.Ordinal) ||
                npcUnitId != requestedNpcUnitId)
            {
                shopId = requestedShopId;
                shopNameKey = shop.NameKey;
                npcConfigId = requestedNpcConfigId;
                npcUnitId = requestedNpcUnitId;
                tradeKind = ShopTradeKind.Buy;
                selectedItemId = 0;
                quantity = 1;
                snapshot = BuildSnapshot(ShopFailureReason.None, null, true, true);
            }
            else
            {
                snapshot = BuildSnapshot(ShopFailureReason.None, null, false, true);
            }

            PublishSnapshot();
            result = snapshot;
            error = null;
            return true;
        }

        public bool TrySetTradeKind(ShopTradeKind value, out ShopSnapshot result, out string error)
        {
            if (!HasSession || (value != ShopTradeKind.Buy && value != ShopTradeKind.Sell))
            {
                error = !HasSession ? "商店会话无效" : "交易标签无效";
                result = snapshot;
                return false;
            }

            tradeKind = value;
            selectedItemId = 0;
            quantity = 1;
            snapshot = BuildSnapshot(ShopFailureReason.None, null, true, true);
            PublishSnapshot();
            result = snapshot;
            error = null;
            return true;
        }

        public bool TrySelectItem(int itemId, out ShopSnapshot result, out string error)
        {
            if (!HasSession)
            {
                error = "商店会话无效";
                result = snapshot;
                return false;
            }

            var current = BuildQuotes();
            if (!current.Any(item => item.ItemId == itemId))
            {
                error = "商品不在当前标签中";
                result = snapshot;
                return false;
            }

            selectedItemId = itemId;
            quantity = 1;
            snapshot = BuildSnapshot(ShopFailureReason.None, null, false, true);
            PublishSnapshot();
            result = snapshot;
            error = null;
            return true;
        }

        public bool TrySetQuantity(int value, out ShopSnapshot result, out string error)
        {
            if (!HasSession || selectedItemId <= 0)
            {
                error = "商店商品未选择";
                result = snapshot;
                return false;
            }

            var quote = BuildQuotes().FirstOrDefault(item => item.ItemId == selectedItemId);
            if (value <= 0 || quote.ItemId == 0 || value > quote.MaxQuantity)
            {
                error = "交易数量超出可成交范围";
                result = snapshot;
                return false;
            }

            quantity = value;
            snapshot = BuildSnapshot(ShopFailureReason.None, null, false, true);
            PublishSnapshot();
            result = snapshot;
            error = null;
            return true;
        }

        public ShopTradeResult TryBuy(int itemId, int amount) => TryTrade(ShopTradeKind.Buy, itemId, amount);
        public ShopTradeResult TrySell(int itemId, int amount) => TryTrade(ShopTradeKind.Sell, itemId, amount);

        public void Close()
        {
            if (!HasSession) return;
            ClearSession();
            PublishSnapshot();
        }

        /// <summary>NPC 释放前先清空会话再发关闭快照，使晚一帧 UI 回调失去成交资格。</summary>
        public void CloseForNpc(int unitId)
        {
            if (!HasSession || npcUnitId != unitId) return;
            ClearSession();
            PublishSnapshot();
        }

        private ShopTradeResult TryTrade(ShopTradeKind requestedKind, int itemId, int amount)
        {
            if (operationInProgress) return TransientFailure(ShopFailureReason.OperationInProgress);
            operationInProgress = true;
            try
            {
                if (!HasSession) return Fail(ShopFailureReason.SessionInvalid, false);
                if (!TryValidateContext(npcConfigId, npcUnitId, shopId, out var shop, out _))
                {
                    // 权威上下文失效后先撤销会话并发布唯一空快照，让仍显示的 UI 立即隐藏。
                    ClearSession();
                    PublishSnapshot();
                    return new ShopTradeResult(ShopFailureReason.StateNotAllowed, snapshot);
                }

                shopNameKey = shop.NameKey;
                if (!this.GetUtility<IRuntimeConfigProvider>().TryGetShopItem(shopId, itemId, out var item, out _))
                {
                    return Fail(ShopFailureReason.ConfigInvalid, true);
                }
                if (amount <= 0) return Fail(ShopFailureReason.InvalidQuantity, true);
                if(requestedKind==ShopTradeKind.Buy&&!item.InitiallyOpen&&!this.GetModel<ShopUnlockModel>().IsUnlocked(itemId))return Fail(ShopFailureReason.NotForSale,true);

                var unitPrice = requestedKind == ShopTradeKind.Buy ? item.BuyPrice : item.SellPrice;
                if (unitPrice <= 0) return Fail(requestedKind == ShopTradeKind.Buy ? ShopFailureReason.NotForSale : ShopFailureReason.NotAccepted, true);

                long total;
                long nextGold;
                try
                {
                    total = checked((long)unitPrice * amount);
                    nextGold = requestedKind == ShopTradeKind.Buy
                        ? checked(this.GetModel<CurrencyModel>().Gold - total)
                        : checked(this.GetModel<CurrencyModel>().Gold + total);
                }
                catch (OverflowException)
                {
                    return Fail(ShopFailureReason.Overflow, true);
                }

                if (nextGold < 0) return Fail(ShopFailureReason.InsufficientGold, true);

                var inventory = this.GetSystem<InventorySystem>();
                ItemContainerSnapshot candidate;
                if (requestedKind == ShopTradeKind.Buy)
                {
                    if (!inventory.TryBuildPlayerBagAddCandidate(itemId, amount, out candidate, out _))
                        return Fail(ShopFailureReason.InsufficientBagSpace, true);
                }
                else if (!inventory.TryBuildPlayerBagRemoveCandidate(itemId, amount, out candidate, out _))
                {
                    return Fail(ShopFailureReason.InsufficientItems, true);
                }

                var currency = this.GetModel<CurrencyModel>();
                var beforeGold = currency.Gold;
                var beforeBag = inventory.CapturePlayerBagSnapshot();
                var committed = false;
                try
                {
                    // 背包与金币先无通知提交，只有 Flush 成功后才对外发布同一份最终状态。
                    if (!inventory.TryApplyPlayerBagCandidate(candidate) || !currency.TrySetGold(nextGold))
                        return Rollback(beforeBag, beforeGold, ShopFailureReason.SaveFailed);
                    this.GetSystem<SaveSystem>().MarkDirty(requestedKind == ShopTradeKind.Buy ? "shop buy" : "shop sell");
                    if (!this.GetSystem<SaveSystem>().Flush(out _))
                        return Rollback(beforeBag, beforeGold, ShopFailureReason.SaveFailed);
                    committed = true;
                }
                catch (Exception e)
                {
                    Log.Error($"[ShopSystem] 商店事务异常：{e.Message}");
                    return Rollback(beforeBag, beforeGold, ShopFailureReason.SaveFailed);
                }

                if (!committed) return Fail(ShopFailureReason.SaveFailed, true);
                tradeKind = requestedKind;
                selectedItemId = itemId;
                quantity = amount;
                if (requestedKind == ShopTradeKind.Sell && inventory.GetPlayerBagOwnedAmount(itemId) == 0)
                    selectedItemId = 0;
                snapshot = BuildSnapshot(ShopFailureReason.None, null, false, true);
                inventory.PublishPlayerBagChanged();
                PublishSnapshot();
                return new ShopTradeResult(ShopFailureReason.None, snapshot);
            }
            finally
            {
                // 无论校验、保存或监听器如何结束，都必须释放防重入标记，下一次显式提交才可继续。
                operationInProgress = false;
            }
        }

        private ShopTradeResult Rollback(ItemContainerSnapshot beforeBag, long beforeGold, ShopFailureReason reason)
        {
            var inventory = this.GetSystem<InventorySystem>();
            // 窄回滚顺序固定为背包再金币，避免下一次自动 Flush 捕获事务中间态。
            inventory.RestorePlayerBagTransactionSnapshot(beforeBag);
            this.GetModel<CurrencyModel>().RestoreGold(beforeGold);
            snapshot = BuildSnapshot(reason, FailureKey(reason), false, false);
            inventory.PublishPlayerBagChanged();
            PublishSnapshot();
            return new ShopTradeResult(reason, snapshot);
        }

        private ShopTradeResult Fail(ShopFailureReason reason, bool publish)
        {
            snapshot = BuildSnapshot(reason, FailureKey(reason), false, false);
            if (publish) PublishSnapshot();
            return new ShopTradeResult(reason, snapshot);
        }

        private ShopTradeResult TransientFailure(ShopFailureReason reason)
        {
            var current = snapshot;
            var failed = new ShopSnapshot(
                current.ShopId,
                current.ShopNameKey,
                current.NpcConfigId,
                current.NpcUnitId,
                current.TradeKind,
                current.SelectedItemId,
                current.Quantity,
                current.Gold,
                current.Items,
                reason,
                FailureKey(reason));
            return new ShopTradeResult(reason, failed);
        }

        private bool TryValidateContext(string expectedNpcConfigId, int expectedNpcUnitId, string expectedShopId, out ShopDefinition shop, out string error)
        {
            shop = default;
            var save = this.GetModel<SaveModel>();
            if (this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing ||
                !string.Equals(this.GetModel<MapSceneModel>().CurrentMapId, MapIds.Base, StringComparison.Ordinal) ||
                this.GetModel<PlayerBagModel>().RiskState != PlayerBagRiskState.BaseSafe ||
                save.CurrentDomain != OfflineSaveDomain.Base || save.CurrentBase == null)
            {
                error = "当前状态不允许商店交易";
                return false;
            }

            if (!this.GetSystem<UnitSystem>().TryGet(expectedNpcUnitId, out var unit) || !unit.IsActive ||
                unit.UnitType != UnitType.Npc || !string.Equals(unit.ConfigId, expectedNpcConfigId, StringComparison.Ordinal))
            {
                error = "商店 NPC 已失效";
                return false;
            }

            var config = this.GetUtility<IRuntimeConfigProvider>();
            if (!config.TryGetNpc(expectedNpcConfigId, out var npc, out error) ||
                npc.InteractionKind != global::NpcInteractionKind.Shop ||
                !string.Equals(npc.InteractionParam, expectedShopId, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(error)) error = "商店 NPC 配置无效";
                return false;
            }

            return config.TryGetShop(expectedShopId, out shop, out error) && config.TryGetShopItems(expectedShopId, out _, out error);
        }

        private IReadOnlyList<ShopItemQuote> BuildQuotes()
        {
            if (!HasSession || !this.GetUtility<IRuntimeConfigProvider>().TryGetShopItems(shopId, out var items, out _))
                return Array.Empty<ShopItemQuote>();

            var inventory = this.GetSystem<InventorySystem>();
            var gold = this.GetModel<CurrencyModel>().Gold;
            var result = new List<ShopItemQuote>();
            foreach (var item in items)
            {
                var owned = inventory.GetPlayerBagOwnedAmount(item.ItemId);
                if (tradeKind == ShopTradeKind.Buy)
                {
                    if (item.BuyPrice <= 0 || !item.InitiallyOpen && !this.GetModel<ShopUnlockModel>().IsUnlocked(item.ItemId)) continue;
                    var affordable = item.BuyPrice == 0 ? 0 : (int)Math.Min(int.MaxValue, gold / item.BuyPrice);
                    var acceptable = inventory.CalculatePlayerBagAcceptableAmount(item.ItemId, affordable);
                    var max = Math.Min(affordable, acceptable);
                    result.Add(new ShopItemQuote(item.Id, item.ItemId, item.Order, item.BuyPrice, owned, max, max > 0));
                }
                else if (item.SellPrice > 0 && owned > 0)
                {
                    result.Add(new ShopItemQuote(item.Id, item.ItemId, item.Order, item.SellPrice, owned, owned, true));
                }
            }

            return result;
        }

        private ShopSnapshot BuildSnapshot(ShopFailureReason failure, string errorKey, bool autoSelect, bool clampQuantity)
        {
            if (!HasSession) return ShopSnapshot.Empty();
            var items = BuildQuotes();
            if (!items.Any(item => item.ItemId == selectedItemId))
            {
                selectedItemId = autoSelect && items.Count > 0 ? items[0].ItemId : 0;
            }

            if (clampQuantity)
            {
                var selected = items.FirstOrDefault(item => item.ItemId == selectedItemId);
                quantity = selected.ItemId == 0 ? 1 : Math.Max(1, Math.Min(quantity, Math.Max(1, selected.MaxQuantity)));
            }

            return new ShopSnapshot(shopId, shopNameKey, npcConfigId, npcUnitId, tradeKind, selectedItemId, quantity,
                this.GetModel<CurrencyModel>().Gold, items, failure, errorKey);
        }

        private void ClearSession()
        {
            shopId = null;
            shopNameKey = null;
            npcConfigId = null;
            npcUnitId = 0;
            tradeKind = ShopTradeKind.Buy;
            selectedItemId = 0;
            quantity = 1;
            snapshot = ShopSnapshot.Empty();
        }

        private void PublishSnapshot()
        {
            try
            {
                this.SendEvent(new ShopChangedEvent(snapshot));
            }
            catch (Exception e)
            {
                Log.Error($"[ShopSystem] ShopChangedEvent 监听器失败：{e.Message}");
            }
        }

        private bool HasSession => !string.IsNullOrWhiteSpace(shopId) && npcUnitId > 0;

        private static string FailureKey(ShopFailureReason reason)
        {
            return reason == ShopFailureReason.None ? null : "ui.shop.error." + ToSnakeCase(reason.ToString());
        }

        private static string ToSnakeCase(string value)
        {
            return string.Concat(value.Select((character, index) => index > 0 && char.IsUpper(character)
                ? "_" + char.ToLowerInvariant(character)
                : char.ToLowerInvariant(character).ToString()));
        }
    }
}
