using System.Collections.Generic;
using QFramework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>展示搜索容器并提交物品交互；关闭时释放当前搜索会话，不持有容器物品权威状态。</summary>
    public sealed class SearchContainerUICtrl : BaseUICtrl
    {
        private readonly List<InventorySlotBoxUI> slots = new List<InventorySlotBoxUI>();
        private readonly Dictionary<InventorySlotBoxUI, InventorySlotRef> refs =
            new Dictionary<InventorySlotBoxUI, InventorySlotRef>();
        private InventorySlotBoxUI activeDragSource;
        private IItemTransferPort transfer;
        private long currentContainerId;
        private InventoryContainerViewSnapshot currentSnapshot;
        private readonly Dictionary<int, InventorySlotViewValue> snapshotByIndex =
            new Dictionary<int, InventorySlotViewValue>();
        private SearchContainerUI UI => BaseUI as SearchContainerUI;

        public long CurrentContainerId => currentContainerId;
        public IReadOnlyList<InventorySlotBoxUI> SearchSlots => slots;

        public override UILayer Layer()
        {
            return UILayer.Dialog;
        }

        protected override string Path()
        {
            return "Inventory/SearchContainer";
        }

        protected override void OnInit()
        {
            transfer = this.GetUtility<IItemTransferPort>();
            UI.BtnClose.onClick.AddListener(Hide);
            UI.SearchList?.AddItemRefreshListener(RefreshSearchCell);
            transfer.Changed += OnTransferChanged;
            this.RegisterEvent<InventoryChangedEvent>(OnInventoryChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<SearchContainerChangedEvent>(OnSearchChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapLoadingStartedEvent>(OnMapLoadingStarted).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<AppStateChangedEvent>(OnAppStateChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<CombatDamageAppliedEvent>(OnDamageApplied).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineWorldInventoryChangedEvent>(_=>OnTransferChanged()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineWorldSelfDamagedEvent>(_=>Hide()).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            transfer.WindowShown(this, UI.BackgroundImage);
            RefreshAll(true);
        }

        protected override void OnHide()
        {
            var closedId = currentContainerId;
            ClearDragFeedback();
            transfer?.WindowHidden(this);
            currentContainerId = 0;
            if (closedId > 0)
            {
                if(transfer.IsOnlineMode)this.GetSystem<OnlineWorldInventorySystem>().CloseOpenCorpse();else if(closedId<=int.MaxValue)this.GetSystem<SearchContainerSystem>().CloseOpenSession((int)closedId);
            }
        }

        protected override void OnUnload()
        {
            var unloadedContainerId = currentContainerId;
            if (transfer != null)
            {
                transfer.Changed -= OnTransferChanged;
                transfer.WindowUnloaded(this);
            }

            currentContainerId = 0;
            if (unloadedContainerId > 0)
            {
                if(transfer.IsOnlineMode)this.GetSystem<OnlineWorldInventorySystem>().CloseOpenCorpse();else if(unloadedContainerId<=int.MaxValue)this.GetSystem<SearchContainerSystem>().CloseOpenSession((int)unloadedContainerId);
            }

            slots.Clear();
            refs.Clear();
            snapshotByIndex.Clear();
            currentSnapshot = default;
            activeDragSource = null;
        }

        public static bool TryShow(long containerId, out string error)
        {
            if (!InventoryOpenRules.CanOpenSearchContainer(containerId, out error))
            {
                return false;
            }

            var ctrl = UIHelper.GetUICtrl<SearchContainerUICtrl>() ?? UIHelper.ShowUI<SearchContainerUICtrl>();
            ctrl.currentContainerId = containerId;
            ctrl.Show();
            if (!ctrl.IsShow())
            {
                error = "搜索容器界面加载失败";
                return false;
            }

            ctrl.RefreshAll(true);
            BagUICtrl.TryShow(out _);
            error = null;
            return true;
        }

        private void RefreshAll(bool refillFromTop = false)
        {
            if (UnityObjectState.IsMissing(UI) || currentContainerId <= 0 ||
                !transfer.TryGetContainerView(InventoryContainerKind.Search,currentContainerId,out var snapshot))
            {
                if (IsShow() && currentContainerId > 0)
                {
                    Hide();
                }

                return;
            }

            currentSnapshot = snapshot;
            snapshotByIndex.Clear();
            foreach (var entry in snapshot.Slots)
            {
                snapshotByIndex[entry.SlotIndex] = entry;
            }

            slots.Clear();
            refs.Clear();
            if (UI.SearchList == null)
            {
                return;
            }

            UI.SearchList.totalCount = snapshot.Capacity;
            UI.SearchList.RefreshCells(refillFromTop);
            if (UI.SearchScrollRect != null)
            {
                UI.SearchScrollRect.vertical = snapshot.Capacity > 12;
            }
        }

        private void RefreshSearchCell(Transform item, int index)
        {
            var view = item.GetComponent<InventorySlotBoxUI>();
            if (view == null)
            {
                return;
            }

            ResetSlotForBinding(view);
            BindSlotEvents(view);
            var hasStack = snapshotByIndex.TryGetValue(index, out var found);

            var slotRef = new InventorySlotRef(InventoryContainerKind.Search, index, currentContainerId);
            refs[view] = slotRef;
            if (!slots.Contains(view))
            {
                slots.Add(view);
            }

            RenderSlot(view, slotRef, hasStack ? found.ItemId : 0, hasStack ? found.Amount : 0, index < currentSnapshot.Capacity);
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

        private void RenderSlot(InventorySlotBoxUI view, InventorySlotRef slotRef, int itemId, int amount, bool visible)
        {
            var empty = amount <= 0;
            var selected = transfer.State.SelectedSlot.HasValue &&
                           transfer.State.SelectedSlot.Value.ContainerKind == InventoryContainerKind.Search &&
                           transfer.State.SelectedSlot.Value.RuntimeContainerId == currentContainerId &&
                           transfer.State.SelectedSlot.Value.SlotIndex == slotRef.SlotIndex;
            view.gameObject.SetActive(visible);
            view.ItemContentRoot.SetActive(true);
            view.IconImage.sprite = ResolveIcon(itemId);
            view.IconImage.gameObject.SetActive(!empty && view.IconImage.sprite != null);
            view.IconImage.enabled = !transfer.ShouldHideDragSourceIcon(slotRef);
            view.EmptyIconRoot.SetActive(empty || view.IconImage.sprite == null);
            view.AmountText.text = empty ? string.Empty : amount.ToString();
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

        private void OnSearchChanged(SearchContainerChangedEvent evt)
        {
            if (evt.ContainerId == currentContainerId)
            {
                OnTransferChanged();
            }
        }

        private void OnMapLoadingStarted(MapLoadingStartedEvent evt) => Hide();

        private void OnAppStateChanged(AppStateChangedEvent evt)
        {
            if (evt.Current != AppState.Playing)
            {
                Hide();
            }
        }

        private void OnDamageApplied(CombatDamageAppliedEvent evt)
        {
            if (evt.TargetUnitId == PlayerModel.LocalPlayerUnitId && evt.Damage > 0)
            {
                Hide();
            }
        }
    }
}
