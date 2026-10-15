namespace Game
{
    public sealed class ItemTransferState
    {
        public InventorySlotRef? SelectedSlot { get; internal set; }
        public WeaponSlot? SelectedEquipmentSlot { get; internal set; }
        public InventorySlotRef? DragSource { get; internal set; }
        public int SelectedItemId { get; internal set; }
        /// <summary>选择时捕获的实例及联机版本，防止旧详情操作后来占据同一格的物品。</summary>
        public System.Guid SelectedItemIdentity { get; internal set; }
        public long SelectedInventoryRevision { get; internal set; }
        public bool SelectingSplitTarget { get; internal set; }
        public long InteractionSequence { get; internal set; }

        internal void ClearSelection()
        {
            SelectedSlot = null;
            SelectedEquipmentSlot = null;
            SelectedItemId = 0;
            SelectedItemIdentity = System.Guid.Empty;
            SelectedInventoryRevision = 0;
            SelectingSplitTarget = false;
        }

        internal void ClearAll()
        {
            ClearSelection();
            DragSource = null;
        }
    }
}
