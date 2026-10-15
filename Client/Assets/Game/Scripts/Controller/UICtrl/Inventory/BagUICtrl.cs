using System;
using System.Collections.Generic;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game
{
    /// <summary>统一管理界面页签身份；顺序与手机底部固定导航一致。</summary>
    public enum BagManagementPage { Backpack, Equipment, Status, Map, Tasks, Journal }

    /// <summary>统一管理界面宿主；拥有背包、装备、状态和地图分页生命周期，不持有库存、属性或地图探索权威状态。</summary>
    public sealed class BagUICtrl : PlayerBagWindowUICtrl
    {
        private BagUI UI => BaseUI as BagUI;
        /// <summary>宿主拥有的装备分页，不单独加载或注册到 UIHelper。</summary>
        private readonly EquipmentManagementPageCtrl equipmentPage = new EquipmentManagementPageCtrl();
        /// <summary>状态页复用宿主绑定；显示订阅随分页显隐释放，不独立注册为阻断窗口。</summary>
        private readonly StatusManagementPageCtrl statusPage = new StatusManagementPageCtrl();
        /// <summary>地图页拥有展示资源，探索权威记录属于地图模型与存档。</summary>
        private readonly MapManagementPageCtrl mapPage = new MapManagementPageCtrl();
        /// <summary>任务与日志同属宿主分页，显示订阅随切页释放。</summary>
        private readonly TasksManagementPageCtrl tasksPage = new TasksManagementPageCtrl();
        private readonly JournalManagementPageCtrl journalPage = new JournalManagementPageCtrl();
        /// <summary>仓库或搜索窗口打开时展示紧凑背包；独立打开时展示统一管理界面。</summary>
        private bool companion;
        /// <summary>基地仓库整页使用专属序列化布局；搜索容器继续复用原紧凑布局。</summary>
        private bool warehouseCompanion;
        /// <summary>仅背包分页可见时拥有预览；完整视角在背包与装备分页之间传递。</summary>
        private ClothingPreviewPresenter backpackPreview;
        /// <summary>x/y 为朝向与俯仰（度），z 为正交半高度（米）；不持久化为玩家数据。</summary>
        private Vector3 previewView=ClothingPreviewPresenter.DefaultView;
        private IUnRegister previewClothing,previewOnline;
        /// <summary>数量弹窗捕获的来源槽位；取消、切页、隐藏或提交后清空，不作为库存权威。</summary>
        private InventorySlotRef? quantitySource;
        /// <summary>打开弹窗时的库存实例身份；同格换入同类物品也必须拒绝旧请求。</summary>
        private Guid quantityIdentity;
        /// <summary>本次待转移件数，范围为 1 到来源当前数量；不提前扣除库存。</summary>
        private int transferQuantity;
        public BagManagementPage CurrentPage { get; private set; }

        protected override Image InputBackground => UI.BackgroundImage;
        protected override CustomListView SlotList => warehouseCompanion ? UI.WarehousePlayerBagList : companion ? UI.PlayerBagList : UI.FullPlayerBagList;
        protected override CustomListView PocketList => companion ? null : UI.PocketList;
        protected override EquipmentSlotBoxUI MeleeSlot => null;
        protected override EquipmentSlotBoxUI RangedSlot => null;
        protected override TMP_Text DetailName => warehouseCompanion ? UI.WarehouseDetailNameText : companion ? UI.DetailNameText : UI.FullDetailNameText;
        protected override TMP_Text DetailDescription => warehouseCompanion ? UI.WarehouseDetailDescText : companion ? UI.DetailDescText : UI.FullDetailDescText;
        protected override TMP_Text DetailAmount => warehouseCompanion ? UI.WarehouseDetailAmountText : companion ? UI.DetailAmountText : UI.FullDetailAmountText;
        protected override GameObject DetailRoot => warehouseCompanion ? UI.WarehouseDetailRoot : companion ? UI.DetailRoot : UI.FullDetailRoot;
        protected override Button UseButton => warehouseCompanion ? UI.WarehouseBtnUse : companion ? UI.BtnUse : UI.FullBtnUse;
        protected override Button SplitButton => warehouseCompanion ? UI.WarehouseBtnSplit : companion ? UI.BtnSplit : UI.FullBtnSplit;
        protected override Button DropButton => warehouseCompanion ? UI.WarehouseBtnDrop : companion ? UI.BtnDrop : UI.FullBtnDrop;
        protected override Button CancelButton => null;
        protected override GameObject ActionRoot => warehouseCompanion ? UI.WarehouseActionRoot : companion ? UI.ActionRoot : UI.FullActionRoot;

        protected override string Path()
        {
            return "Inventory/Bag";
        }

        protected override void OnInit()
        {
            base.OnInit();
            // 与嵌入 Page 的销毁监听保持一致：EditMode 下未执行 Awake 的生成 View 可能不触发 OnDestroy。
            BaseUI.gameObject.AddComponent<PreviewOwnerDestroyWatcher>().Release=ReleasePreview;
            equipmentPage.SetUI(UI.EquipmentPage);
            statusPage.SetUI(UI.StatusPage);
            mapPage.SetUI(UI.MapPage);
            tasksPage.SetUI(UI.TasksPage);
            journalPage.SetUI(UI.JournalPage);
            UI.BtnClose.onClick.AddListener(Hide);
            UI.BtnManagementClose.onClick.AddListener(Hide);
            UI.BtnUse.onClick.AddListener(transfer.UseSelected);
            UI.BtnSplit.onClick.AddListener(transfer.BeginSplit);
            UI.BtnDrop.onClick.AddListener(transfer.DropSelectedToGround);
            UI.BtnMove.onClick.AddListener(MoveSelected);
            UI.BtnStore.onClick.AddListener(StoreSelected);
            UI.WarehouseBtnStore.onClick.AddListener(StoreSelected);
            UI.WarehouseBtnUse.onClick.AddListener(transfer.UseSelected);
            UI.WarehouseBtnSplit.onClick.AddListener(transfer.BeginSplit);
            UI.WarehouseBtnDrop.onClick.AddListener(transfer.DropSelectedToGround);
            UI.BtnTransferDecrease.onClick.AddListener(() => ChangeTransferQuantity(-1));
            UI.BtnTransferIncrease.onClick.AddListener(() => ChangeTransferQuantity(1));
            UI.BtnTransferConfirm.onClick.AddListener(ConfirmQuantityTransfer);
            UI.BtnTransferCancel.onClick.AddListener(CancelQuantityTransfer);
            UI.BtnPageBackpack.onClick.AddListener(() => SelectPage(BagManagementPage.Backpack));
            UI.BtnPageEquipment.onClick.AddListener(() => SelectPage(BagManagementPage.Equipment));
            UI.BtnPageStatus.onClick.AddListener(() => SelectPage(BagManagementPage.Status));
            UI.BtnPageMap.onClick.AddListener(() => SelectPage(BagManagementPage.Map));
            UI.BtnPageTasks.onClick.AddListener(() => SelectPage(BagManagementPage.Tasks));
            UI.BtnPageJournal.onClick.AddListener(() => SelectPage(BagManagementPage.Journal));
        }

        /// <summary>每次重新打开默认进入背包；伴随仓库/搜索时使用 Prefab 已定义的紧凑布局。</summary>
        protected override void OnShow()
        {
            UpdateMode();
            CurrentPage = BagManagementPage.Backpack;
            CancelQuantityTransfer();
            base.OnShow();
            previewClothing=this.RegisterEvent<ClothingEquipmentChangedEvent>(_=>RefreshBackpackPreview());
            previewOnline=this.RegisterEvent<OnlineWorldChangedEvent>(_=>RefreshBackpackPreview());
            SelectPage(CurrentPage);
        }

        private void UpdateMode()
        {
            warehouseCompanion = transfer.HasVisibleWindow(InventoryWindowKind.Warehouse);
            companion = warehouseCompanion ||
                        transfer.HasVisibleWindow(InventoryWindowKind.SearchContainer);
            UI.WarehouseCompanionRoot.SetActive(warehouseCompanion);
            UI.CompanionRoot.SetActive(companion && !warehouseCompanion);
            UI.ManagementRoot.SetActive(!companion);
            if (companion) { equipmentPage.Hide(); statusPage.Hide(); mapPage.Hide(); tasksPage.Hide(); journalPage.Hide(); }
        }

        /// <summary>关闭时取消数量弹窗并清除按钮焦点，避免再次打开后残留提交请求。</summary>
        protected override void OnHide()
        {
            ReleasePreview();
            if (warehouseCompanion && transfer.HasVisibleWindow(InventoryWindowKind.Warehouse))
                UIHelper.GetUICtrl<WarehouseUICtrl>()?.CloseWithCompanion(true);
            equipmentPage.Hide();
            statusPage.Hide();
            mapPage.Hide();
            tasksPage.Hide();
            journalPage.Hide();
            base.OnHide();
            CancelQuantityTransfer();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        protected override void OnUnload()
        {
            ReleasePreview();
            // 销毁期只清理请求身份，不访问可能已经释放的弹窗对象。
            quantitySource = null;
            quantityIdentity = Guid.Empty;
            transferQuantity = 0;
            equipmentPage.SetUI(null);
            statusPage.SetUI(null);
            mapPage.SetUI(null);
            tasksPage.SetUI(null);
            journalPage.SetUI(null);
            base.OnUnload();
        }

        /// <summary>只切换宿主内的页面；保留库存选择和滚动位置，取消尚未提交的拖拽/拆分目标。</summary>
        public void SelectPage(BagManagementPage page)
        {
            if (!Enum.IsDefined(typeof(BagManagementPage), page)) throw new ArgumentOutOfRangeException(nameof(page));
            if(CurrentPage==BagManagementPage.Equipment)previewView=equipmentPage.PreviewView;
            ReleaseBackpackPreview();
            CancelQuantityTransfer();
            CurrentPage = page;
            transfer.EndDrag();
            UIHelper.GetUICtrl<ItemContextMenuUICtrl>()?.Hide();
            UIHelper.GetUICtrl<ItemTooltipUICtrl>()?.Hide();
            UI.InventoryPageRoot.SetActive(page == BagManagementPage.Backpack);
            UI.PlaceholderRoot.SetActive(false);
            if (page == BagManagementPage.Equipment && !companion)
            {equipmentPage.PreviewView=previewView;equipmentPage.Show();}
            else equipmentPage.Hide();
            if(page==BagManagementPage.Backpack&&!companion)
            {
                backpackPreview=new ClothingPreviewPresenter(UI.EquipmentPage.CharacterPreviewPrefab,UI.BackpackCharacterPreview,previewView);
                RefreshBackpackPreview();
            }
            if (page == BagManagementPage.Status && !companion) statusPage.Show();
            else statusPage.Hide();
            if (page == BagManagementPage.Map && !companion) mapPage.Show();
            else mapPage.Hide();
            if (page == BagManagementPage.Tasks && !companion) tasksPage.Show();
            else tasksPage.Hide();
            if (page == BagManagementPage.Journal && !companion) journalPage.Show();
            else journalPage.Hide();
            RefreshPageNavigation();
        }

        /// <summary>只从离线模型或服务端已确认快照重绘七部位，不预测穿戴结果。</summary>
        private void RefreshBackpackPreview()
        {
            if(backpackPreview==null)return;
            int[] items;
            if(this.GetModel<MapSceneModel>().CurrentMode==MapPreparationMode.OnlinePlayersOnly)
                items=this.GetSystem<OnlineWorldSystem>().Snapshot.SelfInventory.ClothingItems();
            else
            {
                var model=this.GetModel<ClothingEquipmentModel>();items=new int[ClothingEquipmentModel.SlotCount];
                for(int i=0;i<items.Length;i++)items[i]=model.GetItemId((global::ClothingSlot)(i+1));
            }
            backpackPreview.RefreshItems(this.GetUtility<IRuntimeConfigProvider>(),items);
        }
        private void ReleaseBackpackPreview()
        {
            if(backpackPreview==null)return;
            previewView=backpackPreview.View;backpackPreview.Dispose();backpackPreview=null;
        }
        /// <summary>隐藏与销毁共用幂等释放；预览根不属于玩法地图，必须由 UI 主动回收。</summary>
        private void ReleasePreview()
        {
            if(CurrentPage==BagManagementPage.Equipment)previewView=equipmentPage.PreviewView;
            ReleaseBackpackPreview();
            previewClothing?.UnRegister();previewOnline?.UnRegister();previewClothing=previewOnline=null;
        }
        /// <summary>宿主对象直接销毁也立即释放离屏资源；正常卸载后的重复回调无副作用。</summary>
        [ExecuteAlways]
        private sealed class PreviewOwnerDestroyWatcher : MonoBehaviour
        {
            public Action Release;
            private void OnDestroy(){Release?.Invoke();Release=null;}
        }

        private void RefreshPageNavigation()
        {
            var selected = new[] { UI.SelectedBackpack, UI.SelectedEquipment, UI.SelectedStatus, UI.SelectedMap, UI.SelectedTasks, UI.SelectedJournal };
            for (var i = 0; i < selected.Length; i++) selected[i].SetActive(i == (int)CurrentPage);
            UI.PageTitleText.text = Loc.T("ui.management." + CurrentPage.ToString().ToLowerInvariant());
        }

        /// <summary>点击随身转移时先选数量；单件直接执行，堆叠物品在确认前不改变库存。</summary>
        private void MoveSelected()
        {
            if (companion || transfer.IsOnlineMode || !transfer.State.SelectedSlot.HasValue) return;
            var source = transfer.State.SelectedSlot.Value;
            if (!InventorySystem.IsCarriedContainer(source.ContainerKind) ||
                !transfer.TryGetStack(source, out var stack) || stack.Amount <= 0) return;
            CancelQuantityTransfer();
            if (stack.Amount == 1)
            {
                transfer.MoveSelectedTo(source.ContainerKind == InventoryContainerKind.Pocket
                    ? InventoryContainerKind.PlayerBag : InventoryContainerKind.Pocket, 1);
                return;
            }
            transfer.EndDrag();
            quantitySource = source;
            quantityIdentity = stack.RuntimeIdentity;
            transferQuantity = stack.Amount;
            UI.TransferQuantityRoot.SetActive(true);
            RefreshQuantityLabel();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(UI.BtnTransferConfirm.gameObject);
        }

        /// <summary>每次调整或确认前重验选择、界面模式与实例身份；过期请求不操作同格新物品。</summary>
        private bool TryGetQuantityStack(out ItemStack stack)
        {
            stack = null;
            var selected = transfer.State.SelectedSlot;
            return quantitySource.HasValue && selected.HasValue && IsShow() && !companion &&
                   CurrentPage == BagManagementPage.Backpack && !transfer.IsOnlineMode &&
                   SameSlot(quantitySource.Value, selected.Value) &&
                   transfer.TryGetStack(quantitySource.Value, out stack) && stack.Amount > 0 &&
                   stack.RuntimeIdentity == quantityIdentity;
        }

        private void ChangeTransferQuantity(int delta)
        {
            if (!TryGetQuantityStack(out var stack)) { CancelQuantityTransfer(); return; }
            transferQuantity = Mathf.Clamp(transferQuantity + delta, 1, stack.Amount);
            RefreshQuantityLabel();
        }

        /// <summary>确认只提交一次；先清理弹窗请求，再由原转移入口验证容量并原子提交。</summary>
        private void ConfirmQuantityTransfer()
        {
            if (!TryGetQuantityStack(out var stack) || transferQuantity < 1 || transferQuantity > stack.Amount)
            { CancelQuantityTransfer(); return; }
            var target = quantitySource.Value.ContainerKind == InventoryContainerKind.Pocket
                ? InventoryContainerKind.PlayerBag : InventoryContainerKind.Pocket;
            var amount = transferQuantity;
            CancelQuantityTransfer();
            transfer.MoveSelectedTo(target, amount);
        }

        /// <summary>取消与重复取消均不改库存；隐藏、切页及失效刷新复用同一清理路径。</summary>
        private void CancelQuantityTransfer()
        {
            quantitySource = null;
            quantityIdentity = Guid.Empty;
            transferQuantity = 0;
            UI.TransferQuantityRoot.SetActive(false);
            var current = EventSystem.current;
            if (current != null && current.currentSelectedGameObject != null &&
                current.currentSelectedGameObject.transform.IsChildOf(UI.TransferQuantityRoot.transform))
                current.SetSelectedGameObject(null);
        }

        /// <summary>基地伴随窗口将当前背包整堆存入仓库；隐藏仓库或选择变化后拒绝旧按钮意图。</summary>
        private void StoreSelected()
        {
            var selected = transfer.State.SelectedSlot;
            if (transfer.HasVisibleWindow(InventoryWindowKind.Warehouse) && selected.HasValue &&
                selected.Value.ContainerKind == InventoryContainerKind.PlayerBag &&
                transfer.TryGetStack(selected.Value, out var stack))
                transfer.MoveSelectedTo(InventoryContainerKind.Warehouse, stack.Amount);
        }

        private void RefreshQuantityLabel()
        {
            if (!TryGetQuantityStack(out var stack)) { CancelQuantityTransfer(); return; }
            transferQuantity = Mathf.Clamp(transferQuantity, 1, stack.Amount);
            UI.TransferQuantityTitleText.text = Loc.T(quantitySource.Value.ContainerKind == InventoryContainerKind.Pocket
                ? "ui.inventory.move_to_bag" : "ui.inventory.move_to_pocket");
            UI.TransferItemNameText.text = UI.FullDetailNameText.text;
            UI.TransferAmountText.text = Loc.T("ui.inventory.transfer_amount", transferQuantity) + " / " + stack.Amount;
            UI.BtnTransferDecrease.interactable = transferQuantity > 1;
            UI.BtnTransferIncrease.interactable = transferQuantity < stack.Amount;
        }

        protected override void RefreshAdditional()
        {
            transfer.TryGetContainerView(InventoryContainerKind.PlayerBag,0,out InventoryContainerViewSnapshot container);
            UI.CapacityText.text = UI.FullCapacityText.text = $"{container.Slots.Count} / {container.Capacity}";
            if (companion) UI.CapacityText.text = Loc.T("ui.inventory.capacity_slots", container.Slots.Count, container.Capacity);
            UI.WarehouseCapacityText.text = Loc.T("ui.inventory.capacity_slots", container.Slots.Count, container.Capacity);
            UI.BtnStore.gameObject.SetActive(companion && transfer.HasVisibleWindow(InventoryWindowKind.Warehouse));
            var hasPocket = transfer.TryGetContainerView(InventoryContainerKind.Pocket, 0, out var pocket);
            UI.PocketCapacityText.text = hasPocket ? $"{pocket.Slots.Count} / {pocket.Capacity}" : "— / 4";
            UI.PocketNoticeText.text = hasPocket ? string.Empty : Loc.T("ui.inventory.pocket_online_unavailable");
            RefreshPageNavigation();
            var source = transfer.State.SelectedSlot;
            var hasSelection = source.HasValue && InventorySystem.IsCarriedContainer(source.Value.ContainerKind) &&
                               transfer.TryGetStack(source.Value, out _);
            UI.EmptyDetailHint.SetActive(!hasSelection && !transfer.State.SelectedEquipmentSlot.HasValue);
            UI.BtnMove.interactable = hasSelection && !transfer.IsOnlineMode;
            UI.BtnStore.interactable = hasSelection && source.Value.ContainerKind == InventoryContainerKind.PlayerBag && !transfer.IsOnlineMode;
            UI.WarehouseBtnStore.interactable = UI.BtnStore.interactable;
            if (warehouseCompanion)
            {
                // 两栏分别显示各自容器的选择，避免仓库物品名称配上上一件背包图标。
                var bagSelection = hasSelection && source.Value.ContainerKind == InventoryContainerKind.PlayerBag;
                UI.WarehouseDetailRoot.SetActive(bagSelection);
                UI.WarehouseActionRoot.SetActive(bagSelection);
            }
            if (hasSelection && transfer.TryGetStack(source.Value, out var stack))
            {
                UI.FullActionRoot.SetActive(true);
                UI.DetailIcon.sprite = ResolveItemIcon(stack.ItemId);
                UI.DetailIcon.gameObject.SetActive(UI.DetailIcon.sprite != null);
                UI.CompanionDetailIcon.sprite = UI.DetailIcon.sprite;
                UI.CompanionDetailIcon.gameObject.SetActive(UI.CompanionDetailIcon.sprite != null);
                UI.WarehouseDetailIcon.sprite = UI.DetailIcon.sprite;
                UI.WarehouseDetailIcon.gameObject.SetActive(UI.WarehouseDetailIcon.sprite != null);
                UI.BtnMove.GetComponentInChildren<TMP_Text>().text = Loc.T(source.Value.ContainerKind == InventoryContainerKind.Pocket
                    ? "ui.inventory.move_to_bag" : "ui.inventory.move_to_pocket");
            }
            if (quantitySource.HasValue) RefreshQuantityLabel();
        }

        private Sprite ResolveItemIcon(int itemId)
        {
            return ResolveIcon(itemId);
        }

        private static bool SameSlot(InventorySlotRef a, InventorySlotRef b) => a.ContainerKind == b.ContainerKind &&
            a.SlotIndex == b.SlotIndex && a.RuntimeContainerId == b.RuntimeContainerId;

        /// <summary>地图快捷键只接管完整管理宿主；暂停和伴随库存窗口保持自身输入优先级。</summary>
        public static bool CanToggleMap()
        {
            var bag = UIHelper.GetUICtrl<BagUICtrl>();
            var top = UIHelper.GetTopmostBlockingUI();
            return InventoryOpenRules.CanOpenPlayerBag(out _) &&
                   (top == null || (top == bag && bag.IsShow() && !bag.companion));
        }

        /// <summary>首次打开或从其他分页切到地图；地图已显示时关闭整个宿主。</summary>
        public static void ToggleMap()
        {
            if (!CanToggleMap()) return;
            var bag = UIHelper.GetUICtrl<BagUICtrl>();
            if (bag != null && bag.IsShow() && bag.CurrentPage == BagManagementPage.Map)
            { bag.Hide(); return; }
            if (TryShow(out _)) UIHelper.GetUICtrl<BagUICtrl>().SelectPage(BagManagementPage.Map);
        }

        /// <summary>当前地图道具进入同一宿主；先验证区域与携带资格，异地道具失败不改变现有窗口。</summary>
        public static bool TryShowMapForItem(int itemId, out string error)
        {
            if (!Game.Interface.GetSystem<MapDisplaySystem>().TryResolveCarriedItem(itemId, out var request, out _, out error) ||
                !InventoryOpenRules.CanOpenPlayerBag(out error)) return false;
            if (request.MapId != Game.Interface.GetModel<MapSceneModel>().CurrentMapId)
            { error = Loc.T("ui.map.error.not_current_region"); return false; }
            var top = UIHelper.GetTopmostBlockingUI();
            if (top != null && !(top is BagUICtrl) && !(top is WarehouseUICtrl) && !(top is SearchContainerUICtrl))
            { error = "当前窗口不允许打开地图"; return false; }
            UIHelper.GetUICtrl<WarehouseUICtrl>()?.Hide();
            UIHelper.GetUICtrl<SearchContainerUICtrl>()?.Hide();
            if (!TryShow(out error)) return false;
            var bag = UIHelper.GetUICtrl<BagUICtrl>();
            bag.SelectPage(BagManagementPage.Map);
            return bag.mapPage.TrySelectItem(itemId, out error);
        }

        public static bool TryShow(out string error)
        {
            if (!InventoryOpenRules.CanOpenPlayerBag(out error))
            {
                return false;
            }

            var wasVisible = UIHelper.GetUICtrl<BagUICtrl>()?.IsShow() == true;
            var ctrl = UIHelper.ShowUI<BagUICtrl>();
            if (!ctrl.IsShow())
            {
                error = "背包界面加载失败";
                return false;
            }

            if (wasVisible)
            {
                ctrl.RefreshWindowOrder();
            }

            error = null;
            return true;
        }

        private void RefreshWindowOrder()
        {
            UpdateMode();
            RefreshAll();
            transfer?.WindowShown(this, InputBackground);
        }
    }

    public abstract class PlayerBagWindowUICtrl : BaseUICtrl
    {
        private readonly List<InventorySlotBoxUI> slots = new List<InventorySlotBoxUI>();
        private readonly List<InventorySlotBoxUI> pocketSlots = new List<InventorySlotBoxUI>();
        private readonly Dictionary<InventorySlotBoxUI, InventorySlotRef> refs =
            new Dictionary<InventorySlotBoxUI, InventorySlotRef>();
        private readonly List<SlotPresentationSnapshot> slotSnapshots = new List<SlotPresentationSnapshot>();
        private InventorySlotBoxUI activeDragSource;
        protected IItemTransferPort transfer;

        protected abstract Image InputBackground { get; }
        protected abstract CustomListView SlotList { get; }
        /// <summary>统一界面口袋列表；其他现有伴随窗口没有口袋显示。</summary>
        protected virtual CustomListView PocketList => null;
        protected abstract EquipmentSlotBoxUI MeleeSlot { get; }
        protected abstract EquipmentSlotBoxUI RangedSlot { get; }
        protected abstract TMP_Text DetailName { get; }
        protected abstract TMP_Text DetailDescription { get; }
        protected abstract TMP_Text DetailAmount { get; }
        /// <summary>物品详情容器；随当前有效选择显隐，不影响下方操作区。</summary>
        protected abstract GameObject DetailRoot { get; }
        protected abstract Button UseButton { get; }
        protected abstract Button SplitButton { get; }
        protected abstract Button DropButton { get; }
        protected abstract Button CancelButton { get; }
        protected abstract GameObject ActionRoot { get; }
        protected virtual void RefreshAdditional() { }

        public IReadOnlyList<InventorySlotBoxUI> PlayerBagSlots => slots;

        public override UILayer Layer()
        {
            return UILayer.Dialog;
        }

        protected override void OnInit()
        {
            transfer = this.GetUtility<IItemTransferPort>();
            UseButton.onClick.AddListener(transfer.UseSelected);
            SplitButton.onClick.AddListener(transfer.BeginSplit);
            DropButton.onClick.AddListener(transfer.DropSelectedToGround);
            CancelButton?.onClick.AddListener(transfer.ClearSelection);
            SlotList?.AddItemRefreshListener(RefreshSlotCell);
            BindEquipmentSlots();
            BindEquipmentDropEvents();
            transfer.Changed += OnTransferChanged;

            this.RegisterEvent<InventoryChangedEvent>(OnInventoryChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<WeaponEquipmentChangedEvent>(OnEquipmentChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(OnLanguageChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapLoadingStartedEvent>(OnMapLoadingStarted).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<AppStateChangedEvent>(OnAppStateChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<CombatDamageAppliedEvent>(OnDamageApplied).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineWorldInventoryChangedEvent>(OnOnlineInventoryChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            transfer.WindowShown(this, InputBackground);
            RefreshAll();
        }

        protected override void OnHide()
        {
            ClearDragFeedback();
            HideTransientItemUi();
            MeleeSlot?.ClearCallbacks();
            RangedSlot?.ClearCallbacks();
            transfer?.WindowHidden(this);
        }

        protected override void OnUnload()
        {
            if (transfer != null)
            {
                transfer.Changed -= OnTransferChanged;
                transfer.WindowUnloaded(this);
            }

            slots.Clear();
            pocketSlots.Clear();
            refs.Clear();
            activeDragSource = null;
        }

        protected void RefreshAll()
        {
            if (UnityObjectState.IsMissing(BaseUI))
            {
                return;
            }

            BindEquipmentSlots();
            ValidateSelection();
            RefreshSlots();
            RefreshEquipment();
            RefreshDetails();
            RefreshAdditional();
        }

        private void BindEquipmentSlots()
        {
            if (MeleeSlot != null)
            {
                MeleeSlot.ClearCallbacks();
                MeleeSlot.Clicked += transfer.ClickEquipment;
            }

            if (RangedSlot != null)
            {
                RangedSlot.ClearCallbacks();
                RangedSlot.Clicked += transfer.ClickEquipment;
            }
        }

        private void BindEquipmentDropEvents()
        {
            MeleeSlot?.ClickButton.OnDropEvent(OnEquipmentDrop, WeaponSlot.Melee)
                .UnRegisterWhenGameObjectDestroyed(MeleeSlot.gameObject);
            RangedSlot?.ClickButton.OnDropEvent(OnEquipmentDrop, WeaponSlot.Ranged)
                .UnRegisterWhenGameObjectDestroyed(RangedSlot.gameObject);
        }

        private void RefreshSlots()
        {
            if(!transfer.TryGetContainerView(InventoryContainerKind.PlayerBag,0,out InventoryContainerViewSnapshot container))return;
            slotSnapshots.Clear();
            for (var index = 0; index < container.Capacity; index++)
            {
                InventorySlotViewValue found=default;bool has=false;foreach(InventorySlotViewValue value in container.Slots)if(value.SlotIndex==index){found=value;has=true;break;}
                if (has)
                {
                    slotSnapshots.Add(new SlotPresentationSnapshot(found.ItemId, found.Amount));
                }
                else
                {
                    slotSnapshots.Add(default);
                }
            }

            slots.Clear();
            pocketSlots.Clear();
            refs.Clear();
            if (SlotList == null)
            {
                return;
            }

            SlotList.AddItemRefreshListener(RefreshSlotCell);
            SlotList.totalCount = slotSnapshots.Count;
            SlotList.RefreshCells();
            if (PocketList != null)
            {
                PocketList.AddItemRefreshListener(RefreshPocketCell);
                PocketList.totalCount = transfer.TryGetContainerView(InventoryContainerKind.Pocket, 0, out var pocket) ? pocket.Capacity : 0;
                PocketList.RefreshCells();
            }
        }

        private void RefreshPocketCell(Transform item, int index)
        {
            var view = item.GetComponent<InventorySlotBoxUI>();
            ResetSlotForBinding(view);
            BindSlotEvents(view);
            var slotRef = new InventorySlotRef(InventoryContainerKind.Pocket, index);
            refs[view] = slotRef;
            pocketSlots.Add(view);
            var occupied = transfer.TryGetStack(slotRef, out var stack);
            RenderSlot(view, slotRef, occupied ? stack.ItemId : 0, occupied ? stack.Amount : 0, IsSelected(slotRef));
        }

        private void RefreshSlotCell(Transform item, int index)
        {
            var view = item.GetComponent<InventorySlotBoxUI>();
            if (view == null)
            {
                return;
            }

            ResetSlotForBinding(view);
            BindSlotEvents(view);
            var slotRef = new InventorySlotRef(InventoryContainerKind.PlayerBag, index);
            refs[view] = slotRef;
            slots.Add(view);
            var snapshot = index >= 0 && index < slotSnapshots.Count ? slotSnapshots[index] : default;
            RenderSlot(view, slotRef, snapshot.ItemId, snapshot.Amount, IsSelected(slotRef));
        }

        private void BindSlotEvents(InventorySlotBoxUI slot)
        {
            slot.ClickButton.OnPointerClickEvent(OnSlotClicked, slot).UnRegisterWhenGameObjectDestroyed(slot.gameObject);
            slot.ClickButton.OnPointerEnterEvent(OnSlotEnter, slot).UnRegisterWhenGameObjectDestroyed(slot.gameObject);
            slot.ClickButton.OnPointerExitEvent(OnSlotExit, slot).UnRegisterWhenGameObjectDestroyed(slot.gameObject);
            slot.ClickButton.OnBeginDragEvent(OnBeginDrag, slot).UnRegisterWhenGameObjectDestroyed(slot.gameObject);
            slot.ClickButton.OnDragEvent(OnDrag, slot).UnRegisterWhenGameObjectDestroyed(slot.gameObject);
            slot.ClickButton.OnDropEvent(OnDrop, slot).UnRegisterWhenGameObjectDestroyed(slot.gameObject);
            slot.ClickButton.OnEndDragEvent(OnEndDrag, slot).UnRegisterWhenGameObjectDestroyed(slot.gameObject);
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

        private void RefreshEquipment()
        {
            WeaponEquipmentSnapshot model=transfer.GetEquipmentView();
            RenderEquipment(MeleeSlot, WeaponSlot.Melee, model.MeleeWeaponItemId, model.ActiveSlot);
            RenderEquipment(RangedSlot, WeaponSlot.Ranged, model.RangedWeaponItemId, model.ActiveSlot);
        }

        private void RenderEquipment(EquipmentSlotBoxUI view, WeaponSlot slot, int itemId, WeaponSlot activeSlot)
        {
            if (view == null)
            {
                return;
            }

            var hasItem = itemId > 0;
            var unarmed = slot == WeaponSlot.Melee && !hasItem;
            view.gameObject.SetActive(true);
            view.Bind(
                slot,
                ResolveIcon(itemId),
                hasItem,
                unarmed,
                unarmed ? Loc.T("ui.inventory.unarmed_marker") : string.Empty,
                transfer.State.SelectedEquipmentSlot == slot || activeSlot == slot,
                activeSlot == slot,
                transfer.IsOnlineMode && slot == WeaponSlot.Ranged);
        }

        private void RenderSlot(InventorySlotBoxUI view, InventorySlotRef slotRef, int itemId, int amount, bool selected)
        {
            var empty = amount <= 0;
            view.ItemContentRoot.SetActive(true);
            view.IconImage.sprite = ResolveIcon(itemId);
            view.IconImage.gameObject.SetActive(!empty && view.IconImage.sprite != null);
            view.IconImage.enabled = !transfer.ShouldHideDragSourceIcon(slotRef);
            view.EmptyIconRoot.SetActive(empty || view.IconImage.sprite == null);
            var held = transfer.GetEquipmentView().Source;
            var holding = held.HasValue && held.Value.ContainerKind==slotRef.ContainerKind && held.Value.SlotIndex==slotRef.SlotIndex && slotRef.RuntimeContainerId==0;
            view.AmountText.text = empty ? string.Empty : holding ? Loc.T("ui.held.current") : amount.ToString();
            view.AmountText.gameObject.SetActive(!empty);
            view.SelectedFrame.SetActive(selected);
            view.DisabledOverlay.SetActive(false);
            view.ClickButton.interactable = true;

            view.gameObject.SetActive(true);
            if (empty && ReferenceEquals(activeDragSource, view))
            {
                activeDragSource = null;
                view.DragFeedback.SetActive(false);
            }
        }

        /// <summary>打开窗口或选择变化后刷新详情；没有有效物品时隐藏整个详情背景。</summary>
        private void RefreshDetails()
        {
            var state = transfer.State;
            ItemStack stack = null;
            var itemId = 0;
            var amount = 0;
            var equipment = state.SelectedEquipmentSlot.HasValue;
            if (equipment)
            {
                WeaponEquipmentSnapshot equipped=transfer.GetEquipmentView();itemId=state.SelectedEquipmentSlot.Value==WeaponSlot.Melee?equipped.MeleeWeaponItemId:equipped.RangedWeaponItemId;
                amount = itemId > 0 ? 1 : 0;
            }
            else if (state.SelectedSlot.HasValue && transfer.TryGetStack(state.SelectedSlot.Value, out stack))
            {
                itemId = stack.ItemId;
                amount = stack.Amount;
            }

            var item = default(ItemDefinition);
            DetailRoot.SetActive(itemId > 0 && amount > 0);
            var hasConfig = itemId > 0 && this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out item, out _);
            DetailName.text = itemId > 0
                ? ResolveText(hasConfig ? item.NameKey : null, hasConfig ? item.Name : null, itemId.ToString())
                : Loc.T("ui.inventory.detail_empty");
            DetailDescription.text = hasConfig ? ResolveText(item.DescKey, item.Desc, string.Empty) : string.Empty;
            DetailAmount.text = itemId > 0 ? amount.ToString() : string.Empty;

            if (hasConfig && item.ActionKind == global::ItemActionKind.ToggleTorch && stack != null) DetailDescription.text += "\n" + Loc.T("ui.torch.remaining", Mathf.CeilToInt(stack.TorchRemainingSeconds ?? item.BurnSeconds));
            var isClothing = hasConfig && item.ItemType == global::ItemType.Clothing;
            var isTorch = hasConfig && item.ActionKind == global::ItemActionKind.ToggleTorch;
            var isWeapon = hasConfig && item.ItemType == global::ItemType.Weapon;
            var isMap = hasConfig && item.ItemType == global::ItemType.Tool &&
                        item.ActionKind == global::ItemActionKind.OpenMap && !string.IsNullOrWhiteSpace(item.ActionTarget);
            bool onlineSupported = transfer.IsOnlineMode && !isTorch && state.SelectedSlot.HasValue && state.SelectedSlot.Value.ContainerKind == InventoryContainerKind.PlayerBag && amount == 1 && (isClothing || WeaponEquipmentModel.ValidateHeldWeapon(itemId,this.GetUtility<IRuntimeConfigProvider>(),out _));
            var canUse = transfer.CanExecute(InventoryUiAction.Use) && (transfer.IsOnlineMode ? onlineSupported :
                         equipment || state.SelectedSlot.HasValue &&
                         InventorySystem.IsCarriedContainer(state.SelectedSlot.Value.ContainerKind) &&
                         (isClothing || isWeapon || isTorch || isMap || CanUseConsumable(item)));
            SetButtonText(UseButton, equipment
                ? Loc.T("ui.inventory.unequip")
                : isTorch ? Loc.T(IsSelectedHeld() ? (this.GetModel<WeaponEquipmentModel>().TorchLit ? "ui.torch.extinguish" : "ui.torch.ignite") : "ui.held.take_out") : isClothing ? Loc.T("ui.clothing.equip") : isWeapon ? Loc.T(IsSelectedHeld() ? "ui.held.holster" : "ui.held.take_out") : isMap ? Loc.T("ui.inventory.open_map") : Loc.T("ui.inventory.use"));
            UseButton.interactable = canUse;
            SplitButton.interactable = transfer.CanExecute(InventoryUiAction.Split)&&state.SelectedSlot.HasValue &&
                                       InventorySystem.IsCarriedContainer(state.SelectedSlot.Value.ContainerKind) &&
                                       amount >= 2;
            DropButton.interactable = transfer.CanExecute(InventoryUiAction.Drop)&&state.SelectedSlot.HasValue &&
                                      InventorySystem.IsCarriedContainer(state.SelectedSlot.Value.ContainerKind) &&
                                      amount > 0 &&
                                      this.GetSystem<InventorySystem>().CanModifyPlayerBag(out _);
            if (ActionRoot != null)
            {
                ActionRoot.SetActive(itemId > 0 && (canUse || SplitButton.interactable || DropButton.interactable));
            }

            if (CancelButton != null)
            {
                CancelButton.interactable = itemId > 0;
            }
        }

        private bool IsSelectedHeld()
        {
            var selected=transfer.State.SelectedSlot;var held=transfer.GetEquipmentView().Source;
            return selected.HasValue&&held.HasValue&&selected.Value.ContainerKind==held.Value.ContainerKind&&selected.Value.SlotIndex==held.Value.SlotIndex&&selected.Value.RuntimeContainerId==0;
        }

        private bool CanUseConsumable(ItemDefinition item)
        {
            if (item.ItemType != global::ItemType.Consumable || item.UseValue <= 0)
            {
                return false;
            }

            return item.UseEffect == global::ItemUseEffect.RestoreHealth
                ? this.GetSystem<InventorySystem>().CanUseExpeditionBagItems()
                : (item.UseEffect == global::ItemUseEffect.RestorePower ||
                   item.UseEffect == global::ItemUseEffect.RestoreWater) &&
                  this.GetSystem<InventorySystem>().CanUseBaseOrExpeditionPowerItems();
        }

        private void ValidateSelection()
        {
            var state = transfer.State;
            if (state.SelectedEquipmentSlot.HasValue)
            {
                WeaponEquipmentSnapshot equipment=transfer.GetEquipmentView();int item=state.SelectedEquipmentSlot.Value==WeaponSlot.Melee?equipment.MeleeWeaponItemId:equipment.RangedWeaponItemId;if(item != state.SelectedItemId)
                {
                    transfer.ClearSelection();
                }
            }
            else if (state.SelectedSlot.HasValue && InventorySystem.IsCarriedContainer(state.SelectedSlot.Value.ContainerKind) &&
                     (!transfer.TryGetStack(state.SelectedSlot.Value, out var stack) || stack.ItemId != state.SelectedItemId))
            {
                transfer.ClearSelection();
            }
        }

        private bool IsSelected(InventorySlotRef slot)
        {
            return transfer.State.SelectedSlot.HasValue && transfer.State.SelectedSlot.Value.ContainerKind == slot.ContainerKind &&
                   transfer.State.SelectedSlot.Value.RuntimeContainerId == slot.RuntimeContainerId &&
                   transfer.State.SelectedSlot.Value.SlotIndex == slot.SlotIndex;
        }

        protected Sprite ResolveIcon(int itemId)
        {
            if (itemId <= 0 || !this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out var item, out _) ||
                string.IsNullOrWhiteSpace(item.IconAddress))
            {
                return null;
            }

            return ResourceLoader.LoadAsset<Sprite>(item.IconAddress);
        }

        private void OnSlotClicked(PointerEventData eventData, object[] args)
        {
            var view = args.GetArg<InventorySlotBoxUI>(0, null);
            if (view == null || !refs.TryGetValue(view, out var slot))
            {
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Left)
            {
                UIHelper.GetUICtrl<ItemContextMenuUICtrl>()?.Hide();
                transfer.ClickSlot(slot);
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Right && transfer.TryGetStack(slot, out _))
            {
                transfer.ClickSlot(slot);
                RefreshAll();
                OpenSlotContextMenu(eventData.position);
            }
        }

        private void OnSlotEnter(PointerEventData eventData, object[] args)
        {
            var view = args.GetArg<InventorySlotBoxUI>(0, null);
            if (view != null && refs.TryGetValue(view, out var slot) && transfer.TryGetStack(slot, out var stack))
            {
                ItemTooltipUICtrl.ShowItem(stack.ItemId, stack.Amount, eventData.position);
            }
        }

        private static void OnSlotExit(PointerEventData _, object[] __)
        {
            UIHelper.GetUICtrl<ItemTooltipUICtrl>()?.Hide();
        }

        /// <summary>选中物品并刷新后展示当前可执行的背包操作；实际库存修改仍由物品事务入口负责。</summary>
        private void OpenSlotContextMenu(Vector2 screenPosition)
        {
            UIHelper.GetUICtrl<ItemTooltipUICtrl>()?.Hide();
            var actions = new List<ItemContextAction>();
            if (UseButton.interactable)
            {
                actions.Add(new ItemContextAction(GetButtonText(UseButton), transfer.UseSelected));
            }

            if (SplitButton.interactable)
            {
                actions.Add(new ItemContextAction(GetButtonText(SplitButton), transfer.BeginSplit));
            }

            if (DropButton.interactable)
            {
                actions.Add(new ItemContextAction(GetButtonText(DropButton), transfer.DropSelectedToGround));
            }

            if (actions.Count > 0)
            {
                ItemContextMenuUICtrl.Show(actions, screenPosition);
            }
        }

        private static string GetButtonText(Button button)
        {
            return button != null ? button.GetComponentInChildren<TMP_Text>(true)?.text ?? string.Empty : string.Empty;
        }

        private static void HideTransientItemUi()
        {
            UIHelper.GetUICtrl<ItemTooltipUICtrl>()?.Hide();
            UIHelper.GetUICtrl<ItemContextMenuUICtrl>()?.Hide();
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

        private void OnEquipmentDrop(PointerEventData _, object[] args)
        {
            var slot = args.GetArg(0, WeaponSlot.None);
            if (slot != WeaponSlot.None)
            {
                transfer.DropEquipment(slot);
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
            foreach (var slot in pocketSlots) slot.DragFeedback.SetActive(false);

            activeDragSource = null;
        }

        private void OnTransferChanged()
        {
            if (IsShow())
            {
                RefreshAll();
            }
        }

        private void OnInventoryChanged(InventoryChangedEvent _)
        {
            OnTransferChanged();
        }

        private void OnOnlineInventoryChanged(OnlineWorldInventoryChangedEvent _)
        {
            OnTransferChanged();
        }

        private void OnEquipmentChanged(WeaponEquipmentChangedEvent _)
        {
            OnTransferChanged();
        }

        private void OnLanguageChanged(LanguageChangedEvent _)
        {
            OnTransferChanged();
        }

        private void OnMapLoadingStarted(MapLoadingStartedEvent _)
        {
            Hide();
        }

        private void OnAppStateChanged(AppStateChangedEvent evt)
        {
            if (!InventoryOpenRules.CanOpenPlayerBag(out _))
            {
                Hide();
            }
        }

        private void OnDamageApplied(CombatDamageAppliedEvent evt)
        {
            if (evt.TargetUnitId == PlayerModel.LocalPlayerUnitId && evt.Damage > 0 &&
                !InventoryOpenRules.CanOpenPlayerBag(out _))
            {
                Hide();
            }
        }

        private static string ResolveText(string key, string legacy, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                var localized = Loc.T(key);
                if (!string.IsNullOrWhiteSpace(localized) && !string.Equals(localized, key, StringComparison.Ordinal))
                {
                    return localized;
                }
            }

            return string.IsNullOrWhiteSpace(legacy) ? fallback ?? string.Empty : legacy;
        }

        private static void SetButtonText(Button button, string value)
        {
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = value;
            }
        }

        private readonly struct SlotPresentationSnapshot
        {
            public SlotPresentationSnapshot(int itemId, int amount)
            {
                ItemId = itemId;
                Amount = amount;
            }

            public int ItemId { get; }
            public int Amount { get; }
        }
    }
}
