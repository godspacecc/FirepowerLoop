using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    public readonly struct ItemContextAction
    {
        public ItemContextAction(string label, Action execute) { Label = label ?? string.Empty; Execute = execute; }
        public string Label { get; }
        public Action Execute { get; }
    }

    public sealed class ItemContextMenuUICtrl : BaseUICtrl
    {
        private readonly List<ItemContextAction> actions = new List<ItemContextAction>();
        private ItemContextMenuUI UI => BaseUI as ItemContextMenuUI;
        protected override string Path() => "Inventory/ItemContextMenu";
        public override UILayer Layer() => UILayer.Popup;
        public override bool BlocksGame => false;
        protected override BackgroundConfig GetBackgroundConfig() => new BackgroundConfig
        {
            ColorType = BackgroundColorType.Transparent, ClickEffect = BackgroundClickEffect.CloseOnly, Alpha = 0f
        };
        protected override void OnInit() => UI.ActionList.AddItemRefreshListener(RefreshAction);
        protected override void OnShow() { }
        protected override void OnHide() { actions.Clear(); UI.ActionList.ClearCells(); }

        public static void Show(IReadOnlyList<ItemContextAction> source, Vector2 screenPosition)
        {
            if (source == null || source.Count == 0) return;
            var ctrl = UIHelper.ShowUI<ItemContextMenuUICtrl>();
            // Top 门禁可能拒绝首次加载；拒绝后不能继续访问尚未创建的 View。
            if (ctrl.IsShow()) ctrl.Open(source, screenPosition);
        }

        private void Open(IReadOnlyList<ItemContextAction> source, Vector2 screenPosition)
        {
            actions.Clear(); actions.AddRange(source);
            UI.PanelRect.sizeDelta = new Vector2(240f, 16f + actions.Count * 36f + Mathf.Max(0, actions.Count - 1) * 4f);
            UI.ActionList.totalCount = actions.Count;
            UI.ActionList.RefreshCells();
            UiPopupPosition.Place(UI.PanelRect, screenPosition, new Vector2(12f, -12f));
        }

        private void RefreshAction(Transform item, int index)
        {
            var view = item.GetComponent<ItemContextActionBoxUI>();
            if (view == null || index < 0 || index >= actions.Count) return;
            view.BtnAction.RemoveAllEvents();
            view.LabelText.text = actions[index].Label;
            view.BtnAction.OnClickEvent(OnActionClick, index).UnRegisterWhenGameObjectDestroyed(view.gameObject);
        }

        private void OnActionClick(object[] args)
        {
            var index = args.GetArg(0, -1);
            if (index < 0 || index >= actions.Count) return;
            var action = actions[index].Execute;
            Hide();
            action?.Invoke();
        }
    }
}
