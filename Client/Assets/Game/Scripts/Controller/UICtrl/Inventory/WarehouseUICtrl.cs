using System.Collections.Generic;
using QFramework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>呈现基地仓库、容量与选中详情并提交存取意图；库存和扩容权威仍属于 InventorySystem。</summary>
    public sealed class WarehouseUICtrl : BaseUICtrl
    {
        private readonly List<InventorySlotBoxUI> slots = new List<InventorySlotBoxUI>();
        private readonly Dictionary<InventorySlotBoxUI, InventorySlotRef> refs =
            new Dictionary<InventorySlotBoxUI, InventorySlotRef>();
        private InventorySlotBoxUI expansionSlot;
        private InventorySlotBoxUI activeDragSource;
        private IItemTransferPort transfer;
        /// <summary>整页退出的重入门禁；Hide 的回调发生在隐藏标记更新前，必须防止两个伴随窗口互相关闭。</summary>
        private bool closingPage;
        private WarehouseUI UI => BaseUI as WarehouseUI;

        public IReadOnlyList<InventorySlotBoxUI> WarehouseSlots => slots;

        public override UILayer Layer()
        {
            return UILayer.Dialog;
        }

        protected override string Path()
        {
            return "Inventory/Warehouse";
        }

        protected override void OnInit()
        {
            transfer = this.GetUtility<IItemTransferPort>();
            UI.BtnClose.onClick.AddListener(() => CloseWithCompanion());
            UI.BtnTake.onClick.AddListener(TakeSelected);
            UI.BtnExpand.onClick.AddListener(OnExpansionClicked);
            UI.WarehouseList?.AddItemRefreshListener(RefreshWarehouseCell);
            transfer.Changed += OnTransferChanged;
            this.RegisterEvent<InventoryChangedEvent>(OnInventoryChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(OnLanguageChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapLoadingStartedEvent>(OnMapLoadingStarted).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<AppStateChangedEvent>(OnAppStateChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            transfer.WindowShown(this, UI.BackgroundImage);
            RefreshAll();
        }

        protected override void OnHide()
        {
            if (!closingPage)
            {
                closingPage = true;
                try { UIHelper.GetUICtrl<BagUICtrl>()?.Hide(); }
                finally { closingPage = false; }
            }
            ClearDragFeedback();
            ClearExpansionCallbacks();
            transfer?.WindowHidden(this);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        /// <summary>统一关闭按钮或伴随背包的 Esc/Tab 入口退出整页；重复调用不再次进入显隐回调。</summary>
        /// <param name="companionAlreadyClosing">伴随背包已在 OnHide 内时为 true，避免对它递归调用 Hide。</param>
        public void CloseWithCompanion(bool companionAlreadyClosing = false)
        {
            if (closingPage || !IsShow()) return;
            closingPage = true;
            try
            {
                if (!companionAlreadyClosing) UIHelper.GetUICtrl<BagUICtrl>()?.Hide();
                Hide();
            }
            finally { closingPage = false; }
        }

        protected override void OnUnload()
        {
            if (transfer != null)
            {
                transfer.Changed -= OnTransferChanged;
                transfer.WindowUnloaded(this);
            }

            slots.Clear();
            refs.Clear();
            expansionSlot = null;
            activeDragSource = null;
        }

        public static bool TryShow(out string error)
        {
            if (!InventoryOpenRules.CanOpenWarehouse(out error))
            {
                return false;
            }

            var ctrl = UIHelper.ShowUI<WarehouseUICtrl>();
            if (!ctrl.IsShow())
            {
                error = "仓库界面加载失败";
                return false;
            }

            BagUICtrl.TryShow(out _);
            error = null;
            return true;
        }

        private void RefreshAll()
        {
            if (UnityObjectState.IsMissing(UI))
            {
                return;
            }

            var container = this.GetModel<WarehouseModel>().Container;
            slots.Clear();
            refs.Clear();
            expansionSlot = null;

            if (UI.WarehouseList == null)
            {
                return;
            }

            UI.WarehouseList.totalCount = container.Capacity + (ShouldShowExpansionSlot() ? 1 : 0);
            UI.WarehouseList.RefreshCells();
            RefreshSummary(container);
        }

        /// <summary>展示权威库存快照；选择为空或来自其它容器时清空详情，避免跨窗口旧数据残留。</summary>
        private void RefreshSummary(ItemContainer container)
        {
            UI.CapacityText.text = Loc.T("ui.inventory.capacity_slots", container.Slots.Count, container.Capacity);
            UI.CapacityText.color = container.Slots.Count >= container.Capacity
                ? new Color(.91f, .51f, .35f) : new Color(.89f, .86f, .78f);
            ItemStack stack = null;
            var selected = transfer.State.SelectedSlot;
            var valid = selected.HasValue && selected.Value.ContainerKind == InventoryContainerKind.Warehouse &&
                        transfer.TryGetStack(selected.Value, out stack) && stack.Amount > 0;
            UI.DetailRoot.SetActive(valid);
            UI.BtnTake.interactable = valid && !transfer.IsOnlineMode;
            UI.DetailNameText.text = string.Empty;
            UI.DetailDescText.text = string.Empty;
            UI.DetailAmountText.text = string.Empty;
            UI.DetailIcon.sprite = null;
            UI.DetailIcon.gameObject.SetActive(false);
            if (valid)
            {
                var found = this.GetUtility<IRuntimeConfigProvider>().TryGetItem(stack.ItemId, out var item, out _);
                UI.DetailNameText.text = found ? ResolveText(item.NameKey, item.Name) : stack.ItemId.ToString();
                UI.DetailDescText.text = found ? ResolveText(item.DescKey, item.Desc) : string.Empty;
                UI.DetailAmountText.text = stack.Amount.ToString();
                UI.DetailIcon.sprite = ResolveIcon(stack.ItemId);
                UI.DetailIcon.gameObject.SetActive(UI.DetailIcon.sprite != null);
            }
            var hasQuote = this.GetSystem<InventorySystem>().TryGetWarehouseExpansionQuote(out var quote, out _);
            UI.BtnExpand.interactable = hasQuote && !quote.IsAtMaximum;
            UI.ExpansionCostText.text = !hasQuote ? string.Empty : quote.IsAtMaximum
                ? Loc.T("ui.inventory.warehouse_maximum", container.Capacity)
                : Loc.T("ui.inventory.warehouse_cost", quote.MetalPartsRequired, quote.ClothRequired);
        }

        private static string ResolveText(string key, string fallback)
        {
            var text = string.IsNullOrWhiteSpace(key) ? string.Empty : Loc.T(key);
            return string.IsNullOrWhiteSpace(text) || text == key ? fallback ?? string.Empty : text;
        }

        /// <summary>按钮点击时重新读取当前选择，整堆转移由领域事务检查容量；失败保持选择。</summary>
        private void TakeSelected()
        {
            var selected = transfer.State.SelectedSlot;
            if (selected.HasValue && selected.Value.ContainerKind == InventoryContainerKind.Warehouse &&
                transfer.TryGetStack(selected.Value, out var stack))
                transfer.MoveSelectedTo(InventoryContainerKind.PlayerBag, stack.Amount);
        }

        private void RefreshWarehouseCell(Transform item, int index)
        {
            var view = item.GetComponent<InventorySlotBoxUI>();
            if (view == null)
            {
                return;
            }

            var container = this.GetModel<WarehouseModel>().Container;
            if (index >= container.Capacity)
            {
                ResetSlotForBinding(view);
                RenderExpansionSlot(view);
                return;
            }

            ResetSlotForBinding(view);
            BindSlotEvents(view);
            var slotRef = new InventorySlotRef(InventoryContainerKind.Warehouse, index);
            refs[view] = slotRef;
            slots.Add(view);
            var stack = container.TryGet(index, out var found) ? found : null;
            RenderSlot(view, slotRef, stack, true);
        }

        private void BindSlotEvents(InventorySlotBoxUI view)
        {
            view.ClickButton.OnPointerClickEvent(OnSlotClicked, view).UnRegisterWhenGameObjectDestroyed(view.gameObject);
            view.ClickButton.OnBeginDragEvent(OnBeginDrag, view).UnRegisterWhenGameObjectDestroyed(view.gameObject);
            view.ClickButton.OnDragEvent(OnDrag, view).UnRegisterWhenGameObjectDestroyed(view.gameObject);
            view.ClickButton.OnDropEvent(OnDrop, view).UnRegisterWhenGameObjectDestroyed(view.gameObject);
            view.ClickButton.OnEndDragEvent(OnEndDrag, view).UnRegisterWhenGameObjectDestroyed(view.gameObject);
        }

        private void ResetSlotForBinding(InventorySlotBoxUI view)
        {
            refs.Remove(view);
            if (ReferenceEquals(activeDragSource, view))
            {
                activeDragSource = null;
            }

            InventorySlotBoxReuse.Reset(view);
        }

        private void RenderSlot(InventorySlotBoxUI view, InventorySlotRef slotRef, ItemStack stack, bool visible)
        {
            var empty = stack == null || stack.Amount <= 0;
            var selected = transfer.State.SelectedSlot.HasValue &&
                           transfer.State.SelectedSlot.Value.ContainerKind == InventoryContainerKind.Warehouse &&
                           transfer.State.SelectedSlot.Value.SlotIndex == slotRef.SlotIndex;
            view.gameObject.SetActive(visible);
            view.ItemContentRoot.SetActive(true);
            view.IconImage.sprite = ResolveIcon(stack?.ItemId ?? 0);
            view.IconImage.gameObject.SetActive(!empty && view.IconImage.sprite != null);
            view.IconImage.enabled = !transfer.ShouldHideDragSourceIcon(slotRef);
            view.EmptyIconRoot.SetActive(empty || view.IconImage.sprite == null);
            view.AmountText.text = empty ? string.Empty : stack.Amount.ToString();
            view.AmountText.gameObject.SetActive(!empty);
            view.SelectedFrame.SetActive(selected);
            view.DisabledOverlay.SetActive(!visible);
            view.ClickButton.interactable = visible;

            if (empty && ReferenceEquals(activeDragSource, view))
            {
                activeDragSource = null;
                view.DragFeedback.SetActive(false);
            }
        }

        private bool ShouldShowExpansionSlot()
        {
            return this.GetSystem<InventorySystem>().TryGetWarehouseExpansionQuote(out var quote, out _) && !quote.IsAtMaximum;
        }

        private void RenderExpansionSlot(InventorySlotBoxUI view)
        {
            expansionSlot = view;
            view.gameObject.SetActive(true);
            view.ExpansionContentRoot.SetActive(true);
            view.ExpansionLabelText.text = Loc.T("ui.inventory.expand_slot");
            view.ExpansionLabelText.gameObject.SetActive(true);
            view.ExpansionButton.onClick.AddListener(OnExpansionClicked);
            view.ExpansionButton.interactable = true;
        }

        private void ClearExpansionCallbacks()
        {
            if (expansionSlot != null)
            {
                expansionSlot.ExpansionButton.RemoveAllEvents();
            }
        }

        private void OnExpansionClicked()
        {
            if (!this.GetSystem<InventorySystem>().TryGetWarehouseExpansionQuote(out var quote, out _))
            {
                Notify.ShowFloatTips(Loc.T("ui.inventory.expand_failed"));
                return;
            }

            Notify.ShowMessage(
                Loc.T("ui.inventory.expand_title"),
                Loc.T("ui.inventory.expand_content", quote.MetalPartsOwned, quote.MetalPartsRequired, quote.ClothOwned, quote.ClothRequired),
                new List<MessageButtonConfig>
                {
                    new MessageButtonConfig
                    {
                        ButtonText = Loc.T("ui.common.confirm"),
                        Role = MessageButtonRole.Confirm,
                        Interactable = quote.CanExpand,
                        OnClick = ConfirmExpansion
                    },
                    new MessageButtonConfig
                    {
                        ButtonText = Loc.T("ui.common.cancel"),
                        Role = MessageButtonRole.Cancel
                    }
                });
        }

        private void ConfirmExpansion()
        {
            if (!this.GetSystem<InventorySystem>().TryExpandWarehouse(out _))
            {
                Notify.ShowFloatTips(Loc.T("ui.inventory.expand_failed"));
            }
        }

        private Sprite ResolveIcon(int itemId)
        {
            if (itemId <= 0 || !this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out var item, out _) ||
                string.IsNullOrWhiteSpace(item.IconAddress))
            {
                return null;
            }

            return ResourceLoader.LoadAsset<Sprite>(item.IconAddress);
        }

        private void OnSlotClicked(PointerEventData _, object[] args)
        {
            var view = args.GetArg<InventorySlotBoxUI>(0, null);
            if (view != null && refs.TryGetValue(view, out var slot))
            {
                transfer.ClickSlot(slot);
            }
        }

        private void OnBeginDrag(PointerEventData eventData, object[] args)
        {
            ClearDragFeedback();
            var view = args.GetArg<InventorySlotBoxUI>(0, null);
            if (view != null && refs.TryGetValue(view, out var slot) && transfer.BeginDrag(slot))
            {
                activeDragSource = view;
                view.DragFeedback.SetActive(true);
                transfer.TryShowDragVisual(view.IconImage, eventData);
            }
        }

        private void OnDrag(PointerEventData eventData, object[] _)
        {
            transfer.UpdateDragVisual(eventData);
        }

        private void OnDrop(PointerEventData _, object[] args)
        {
            var view = args.GetArg<InventorySlotBoxUI>(0, null);
            if (view != null && refs.TryGetValue(view, out var slot))
            {
                transfer.Drop(slot);
            }
        }

        private void OnEndDrag(PointerEventData _, object[] __)
        {
            ClearDragFeedback();
            transfer.EndDrag();
        }

        private void ClearDragFeedback()
        {
            foreach (var slot in slots)
            {
                slot.DragFeedback.SetActive(false);
            }

            activeDragSource = null;
        }

        private void OnTransferChanged()
        {
            if (IsShow())
            {
                RefreshAll();
            }
        }

        private void OnInventoryChanged(InventoryChangedEvent evt) => OnTransferChanged();
        private void OnLanguageChanged(LanguageChangedEvent evt) => OnTransferChanged();
        private void OnMapLoadingStarted(MapLoadingStartedEvent evt) => Hide();

        private void OnAppStateChanged(AppStateChangedEvent evt)
        {
            if (!InventoryOpenRules.CanOpenWarehouse(out _))
            {
                Hide();
            }
        }
    }
}
