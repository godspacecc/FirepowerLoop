using System;
using System.Linq;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game
{
    /// <summary>渲染商店系统快照并提交购买/出售意图；不持有价格、金币或背包权威状态。</summary>
    public sealed class ShopUICtrl : BaseUICtrl
    {
        private ShopSnapshot current = ShopSnapshot.Empty();
        private bool hiding;
        private ShopUI UI => BaseUI as ShopUI;

        protected override string Path() => "Shop/Shop";
        public override UILayer Layer() => UILayer.Dialog;
        public override bool BlocksGame => true;

        /// <summary>建立交易会话后确认界面显示；加载失败或异常时回滚该 NPC 会话。</summary>
        public static bool TryShow(string npcConfigId, int npcUnitId, string shopId, out string error)
        {
            var system = Game.Interface.GetSystem<ShopSystem>();
            if (!system.TryOpen(npcConfigId, npcUnitId, shopId, out _, out error)) return false;
            try
            {
                var ctrl = UIHelper.ShowUI<ShopUICtrl>();
                if (ctrl != null && ctrl.IsShow()) return true;
                error = "商店界面加载失败";
            }
            catch (Exception exception)
            {
                error = "商店界面打开异常：" + exception.Message;
            }
            system.CloseForNpc(npcUnitId);
            return false;
        }

        protected override void OnInit()
        {
            // 稳定控件与列表回调只绑定一次；可见期状态始终由 ShopChangedEvent 快照重绘。
            UI.BtnClose.onClick.AddListener(Hide);
            UI.BtnBuyTab.onClick.AddListener(() => SetTradeKind(ShopTradeKind.Buy));
            UI.BtnSellTab.onClick.AddListener(() => SetTradeKind(ShopTradeKind.Sell));
            UI.BtnMinus.onClick.AddListener(() => ChangeQuantity(-1));
            UI.BtnPlus.onClick.AddListener(() => ChangeQuantity(1));
            UI.BtnTrade.onClick.AddListener(Trade);
            UI.ItemList.AddItemRefreshListener(RefreshRow);
            this.RegisterEvent<ShopChangedEvent>(OnShopChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ => RefreshLocalized()).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            // Show 只消费已由 TryShow 校验建立的会话，不在 UI 内复制领域状态。
            Render(this.GetSystem<ShopSystem>().Snapshot);
        }

        protected override void OnHide()
        {
            if (hiding) return;
            hiding = true;
            try
            {
                ClearRows();
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                this.GetSystem<ShopSystem>()?.Close();
                current = ShopSnapshot.Empty();
            }
            finally
            {
                hiding = false;
            }
        }

        protected override void OnUnload()
        {
            ClearRows();
            current = ShopSnapshot.Empty();
        }

        private void OnShopChanged(ShopChangedEvent evt)
        {
            if (!IsShow()) return;
            if (string.IsNullOrWhiteSpace(evt.Snapshot.ShopId))
            {
                Hide();
                return;
            }

            Render(evt.Snapshot);
        }

        private void Render(ShopSnapshot snapshot)
        {
            if (UI == null) return;
            current = snapshot;
            SetText(UI.TextShopName, Loc.T(snapshot.ShopNameKey));
            SetText(UI.TextGold, Loc.T("ui.shop.gold", snapshot.Gold));
            UI.ItemList.totalCount = snapshot.Items?.Count ?? 0;
            UI.ItemList.RefreshCells();
            RenderDetail();
            SetTabState(UI.BtnBuyTab, snapshot.TradeKind == ShopTradeKind.Buy);
            SetTabState(UI.BtnSellTab, snapshot.TradeKind == ShopTradeKind.Sell);
        }

        private void RenderDetail()
        {
            var quote = current.Items?.FirstOrDefault(item => item.ItemId == current.SelectedItemId) ?? default;
            var selected = quote.ItemId > 0;
            UI.DetailRoot.SetActive(selected);
            if (!selected)
            {
                SetText(UI.TextReason, string.IsNullOrWhiteSpace(current.LastErrorKey) ? string.Empty : Loc.T(current.LastErrorKey));
                UI.BtnTrade.interactable = false;
                return;
            }

            TryGetItem(quote.ItemId, out var item);
            SetText(UI.TextDetailName, ResolveItemName(item, quote.ItemId));
            SetText(UI.TextDetailDesc, ResolveItemDesc(item));
            SetText(UI.TextUnitPrice, Loc.T("ui.shop.unit_price", quote.UnitPrice));
            SetText(UI.TextOwned, Loc.T("ui.shop.owned", quote.OwnedAmount));
            SetText(UI.TextQuantity, current.Quantity.ToString());
            long total;
            try { total = checked((long)quote.UnitPrice * current.Quantity); }
            catch (OverflowException) { total = 0; }
            SetText(UI.TextTotal, Loc.T("ui.shop.total", total));
            SetText(UI.TextReason, string.IsNullOrWhiteSpace(current.LastErrorKey) ? string.Empty : Loc.T(current.LastErrorKey));
            UI.BtnMinus.interactable = current.Quantity > 1;
            UI.BtnPlus.interactable = current.Quantity < quote.MaxQuantity;
            UI.BtnTrade.interactable = quote.CanTrade && current.Quantity > 0 && current.Quantity <= quote.MaxQuantity;
            SetButtonLabel(UI.BtnTrade, Loc.T(current.TradeKind == ShopTradeKind.Buy ? "ui.shop.action.buy" : "ui.shop.action.sell"));
            if (UI.DetailIcon != null)
            {
                UI.DetailIcon.sprite = ResolveIcon(item);
                UI.DetailIcon.enabled = UI.DetailIcon.sprite != null;
            }
        }

        private void RefreshRow(Transform transform, int index)
        {
            var box = transform.GetComponent<ShopItemBoxUI>();
            if (box == null || current.Items == null || index < 0 || index >= current.Items.Count) return;
            var quote = current.Items[index];
            TryGetItem(quote.ItemId, out var item);
            box.Icon.sprite = ResolveIcon(item);
            box.Icon.enabled = box.Icon.sprite != null;
            SetText(box.TextName, ResolveItemName(item, quote.ItemId));
            SetText(box.TextPrice, Loc.T("ui.shop.row.price", quote.UnitPrice));
            SetText(box.TextOwned, Loc.T("ui.shop.row.owned", quote.OwnedAmount));
            box.SelectedRoot.SetActive(quote.ItemId == current.SelectedItemId);
            box.DisabledRoot.SetActive(!quote.CanTrade);
            box.BtnSelect.interactable = true;
            box.BtnSelect.onClick.RemoveAllListeners();
            box.BtnSelect.onClick.AddListener(() => SelectItem(quote.ItemId));
        }

        private void SetTradeKind(ShopTradeKind kind)
        {
            if (this.GetSystem<ShopSystem>().TrySetTradeKind(kind, out var next, out _)) Render(next);
        }

        private void SelectItem(int itemId)
        {
            if (this.GetSystem<ShopSystem>().TrySelectItem(itemId, out var next, out _)) Render(next);
        }

        private void ChangeQuantity(int delta)
        {
            if (this.GetSystem<ShopSystem>().TrySetQuantity(current.Quantity + delta, out var next, out _)) Render(next);
        }

        private void Trade()
        {
            if (current.SelectedItemId <= 0) return;
            var result = current.TradeKind == ShopTradeKind.Buy
                ? this.SendCommand(new BuyShopItemCommand(current.SelectedItemId, current.Quantity))
                : this.SendCommand(new SellShopItemCommand(current.SelectedItemId, current.Quantity));
            Render(result.Snapshot);
        }

        private void RefreshLocalized()
        {
            if (UI == null) return;
            foreach (var text in UI.GetComponentsInChildren<LocalizedText>(true)) text.Refresh();
            Render(this.GetSystem<ShopSystem>().Snapshot);
        }

        private void ClearRows()
        {
            if (UI?.ItemList == null) return;
            foreach (Transform child in UI.ItemList.transform)
                if (child.TryGetComponent<ShopItemBoxUI>(out var box)) box.BtnSelect.onClick.RemoveAllListeners();
            UI.ItemList.ClearCells();
        }

        private bool TryGetItem(int itemId, out ItemDefinition item) => this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out item, out _);
        private Sprite ResolveIcon(ItemDefinition item) => string.IsNullOrWhiteSpace(item.IconAddress) ? null : ResourceLoader.LoadAsset<Sprite>(item.IconAddress);
        private static string ResolveItemName(ItemDefinition item, int id) => string.IsNullOrWhiteSpace(item.NameKey) ? (item.Name ?? id.ToString()) : Loc.T(item.NameKey);
        private static string ResolveItemDesc(ItemDefinition item) => string.IsNullOrWhiteSpace(item.DescKey) ? (item.Desc ?? string.Empty) : Loc.T(item.DescKey);
        private static void SetText(TextMeshProUGUI text, string value) { if (text != null) text.text = value ?? string.Empty; }
        private static void SetButtonLabel(Button button, string value) { if (button != null) SetText(button.GetComponentInChildren<TextMeshProUGUI>(true), value); }
        private static void SetTabState(Button button, bool selected) { if (button?.targetGraphic != null) button.targetGraphic.color = selected ? new Color(0.30f, 0.48f, 0.50f, 1f) : new Color(0.18f, 0.23f, 0.25f, 1f); }
    }
}
