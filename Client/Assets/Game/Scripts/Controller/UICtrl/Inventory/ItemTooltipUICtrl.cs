using QFramework;
using UnityEngine;

namespace Game
{
    public sealed class ItemTooltipUICtrl : BaseUICtrl
    {
        private ItemTooltipUI UI => BaseUI as ItemTooltipUI;
        protected override string Path() => "Inventory/ItemTooltip";
        public override UILayer Layer() => UILayer.Popup;
        public override bool BlocksGame => false;
        protected override void OnInit() { }
        protected override void OnShow() { }
        protected override void OnHide() { }

        public static void ShowItem(int itemId, int amount, Vector2 screenPosition)
        {
            if (itemId <= 0) return;
            var ctrl = UIHelper.ShowUI<ItemTooltipUICtrl>();
            // 拒绝显示时不读取资源或未加载的 View，也不改变已有消息窗口。
            if (ctrl.IsShow()) ctrl.Open(itemId, amount, screenPosition);
        }

        private void Open(int itemId, int amount, Vector2 screenPosition)
        {
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out var item, out _)) { Hide(); return; }
            UI.NameText.text = ResolveText(item.NameKey, item.Name, itemId.ToString());
            UI.DescriptionText.text = ResolveText(item.DescKey, item.Desc, string.Empty);
            UI.AmountText.text = string.Format(Loc.T("ui.inventory.amount_stack"), amount, item.MaxStackAmount);
            UI.IconImage.sprite = string.IsNullOrWhiteSpace(item.IconAddress) ? null : ResourceLoader.LoadAsset<Sprite>(item.IconAddress);
            UI.IconImage.gameObject.SetActive(UI.IconImage.sprite != null);
            UiPopupPosition.Place(UI.PanelRect, screenPosition, new Vector2(16f, -16f));
        }

        private static string ResolveText(string key, string legacy, string fallback)
        {
            var localized = string.IsNullOrWhiteSpace(key) ? string.Empty : Loc.T(key);
            if (!string.IsNullOrWhiteSpace(localized) && localized != key) return localized;
            return string.IsNullOrWhiteSpace(legacy) ? fallback : legacy;
        }
    }
}
