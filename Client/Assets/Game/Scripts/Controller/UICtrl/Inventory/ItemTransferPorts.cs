using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game
{
    public enum InventoryWindowKind
    {
        Bag,
        Warehouse,
        SearchContainer
    }

    public enum InventoryUiAction{Use,Drop,Split,Ranged}
    public readonly struct InventorySlotViewValue{public InventorySlotViewValue(int slotIndex,int itemId,int amount){SlotIndex=slotIndex;ItemId=itemId;Amount=amount;}public int SlotIndex{get;}public int ItemId{get;}public int Amount{get;}}
    public readonly struct InventoryContainerViewSnapshot{public InventoryContainerViewSnapshot(long runtimeContainerId,int capacity,IReadOnlyList<InventorySlotViewValue> slots){RuntimeContainerId=runtimeContainerId;Capacity=capacity;Slots=slots??Array.Empty<InventorySlotViewValue>();}public long RuntimeContainerId{get;}public int Capacity{get;}public IReadOnlyList<InventorySlotViewValue> Slots{get;}}

    public interface IItemTransferPort : IUtility, IDisposable
    {
        ItemTransferState State { get; }
        bool IsOnlineMode { get; }
        event Action Changed;
        bool HasVisibleWindow(InventoryWindowKind kind);
        void WindowShown(BaseUICtrl owner, Image background);
        void WindowHidden(BaseUICtrl owner);
        void WindowUnloaded(BaseUICtrl owner);
        void ClickSlot(InventorySlotRef slot);
        bool BeginDrag(InventorySlotRef slot);
        bool TryShowDragVisual(Image sourceImage, PointerEventData eventData);
        void UpdateDragVisual(PointerEventData eventData);
        bool ShouldHideDragSourceIcon(InventorySlotRef slot);
        void Drop(InventorySlotRef target);
        void DropEquipment(WeaponSlot target);
        void EndDrag();
        void ClickEquipment(WeaponSlot slot);
        void BeginSplit();
        void UseSelected();
        /// <summary>按数量把当前选择转入另一随身存储区或基地仓库；联机不提交本地库存。</summary>
        void MoveSelectedTo(InventoryContainerKind targetKind, int amount);
        void DropSelectedToGround();
        void ClearSelection();
        void InvalidateContainer(InventoryContainerKind kind, long runtimeContainerId = 0);
        bool TryGetStack(InventorySlotRef slot, out ItemStack stack);
        bool TryGetContainerView(InventoryContainerKind kind,long runtimeContainerId,out InventoryContainerViewSnapshot snapshot);
        WeaponEquipmentSnapshot GetEquipmentView();
        bool CanExecute(InventoryUiAction action);
    }
}
