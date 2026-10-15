using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game
{
    public sealed class ItemTransferCtrl : IController, IItemTransferPort
    {
        private readonly IArchitecture architecture;
        private readonly InventoryDragVisualPresenter dragVisual = new InventoryDragVisualPresenter();
        private readonly Dictionary<BaseUICtrl, WindowRegistration> windows =
            new Dictionary<BaseUICtrl, WindowRegistration>();
        private long displaySequence;
        private global::ItemUseEffect pendingUseEffect = global::ItemUseEffect.None;
        private IUnRegister useFinishedUnregister;
        private bool disposed;

        public ItemTransferCtrl(IArchitecture architecture)
        {
            this.architecture = architecture ?? throw new ArgumentNullException(nameof(architecture));
            useFinishedUnregister = architecture.RegisterEvent<PlayerItemUseFinishedEvent>(OnPlayerItemUseFinished);
        }

        public ItemTransferState State { get; } = new ItemTransferState();
        public bool IsOnlineMode=>architecture.GetModel<MapSceneModel>().CurrentMode==MapPreparationMode.OnlinePlayersOnly&&architecture.GetSystem<OnlineWorldSystem>()?.Snapshot?.HasWorld==true;
        public event Action Changed;

        public IArchitecture GetArchitecture()
        {
            return architecture;
        }

        public void WindowShown(BaseUICtrl owner, Image background)
        {
            if (disposed || owner == null)
            {
                return;
            }

            BaseUICtrl otherPlayerBag = null;
            if (owner is PlayerBagWindowUICtrl)
            {
                foreach (var candidate in windows.Keys)
                {
                    if (!ReferenceEquals(candidate, owner) && candidate is PlayerBagWindowUICtrl && candidate.IsShow())
                    {
                        otherPlayerBag = candidate;
                        break;
                    }
                }
            }

            windows[owner] = new WindowRegistration(background, ++displaySequence);
            otherPlayerBag?.Hide();
            CoordinateBackgrounds();
        }

        public bool HasVisibleWindow(InventoryWindowKind kind)
        {
            foreach (var owner in windows.Keys)
            {
                if (owner == null || !owner.IsShow())
                {
                    continue;
                }

                if (kind == InventoryWindowKind.Bag && owner is BagUICtrl ||
                    kind == InventoryWindowKind.Warehouse && owner is WarehouseUICtrl ||
                    kind == InventoryWindowKind.SearchContainer && owner is SearchContainerUICtrl)
                {
                    return true;
                }
            }

            return false;
        }

        public void WindowHidden(BaseUICtrl owner)
        {
            if (owner == null)
            {
                return;
            }

            windows.Remove(owner);
            ClearStateForWindow(owner);
            CoordinateBackgrounds();
        }

        public void WindowUnloaded(BaseUICtrl owner)
        {
            WindowHidden(owner);
        }

        public void ClickSlot(InventorySlotRef slot)
        {
            if (!IsSlotContextValid(slot))
            {
                InvalidateContainer(slot.ContainerKind, slot.RuntimeContainerId);
                return;
            }

            State.InteractionSequence++;
            if (State.SelectingSplitTarget && State.SelectedSlot.HasValue)
            {
                HandleSplitTarget(slot);
                return;
            }

            if (State.SelectedEquipmentSlot.HasValue)
            {
                State.ClearSelection();
                SelectIfNotEmpty(slot);
                return;
            }

            // 普通点击只改变选择；物品移动由拖拽放下触发，SelectingSplitTarget 是显式拆分例外。
            if (!State.SelectedSlot.HasValue)
            {
                SelectIfNotEmpty(slot);
                return;
            }

            var source = State.SelectedSlot.Value;
            if (source.ContainerKind == slot.ContainerKind &&
                source.RuntimeContainerId == slot.RuntimeContainerId &&
                source.SlotIndex == slot.SlotIndex)
            {
                State.ClearSelection();
                RaiseChanged();
                return;
            }

            SelectIfNotEmpty(slot);
        }

        public bool BeginDrag(InventorySlotRef slot)
        {
            if (!TryGetStack(slot, out var stack))
            {
                return false;
            }

            dragVisual.Clear();
            State.InteractionSequence++;
            State.SelectingSplitTarget = false;
            State.DragSource = slot;
            State.SelectedSlot = slot;
            State.SelectedEquipmentSlot = null;
            State.SelectedItemId = stack.ItemId;
            CaptureSelectedIdentity(stack);
            RaiseChanged();
            return true;
        }

        public bool TryShowDragVisual(Image sourceImage, PointerEventData eventData)
        {
            return State.DragSource.HasValue && dragVisual.TryShow(sourceImage, eventData);
        }

        public void UpdateDragVisual(PointerEventData eventData)
        {
            dragVisual.UpdatePosition(eventData);
        }

        public bool ShouldHideDragSourceIcon(InventorySlotRef slot)
        {
            if (!dragVisual.IsVisible || !State.DragSource.HasValue)
            {
                return false;
            }

            var source = State.DragSource.Value;
            return source.ContainerKind == slot.ContainerKind &&
                   source.RuntimeContainerId == slot.RuntimeContainerId &&
                   source.SlotIndex == slot.SlotIndex;
        }

        public void Drop(InventorySlotRef target)
        {
            if (!State.DragSource.HasValue)
            {
                return;
            }

            var source = State.DragSource.Value;
            if (!IsSlotContextValid(source) || !IsSlotContextValid(target))
            {
                dragVisual.Clear();
                State.ClearAll();
                RaiseChanged();
                return;
            }

            Move(source, target);
        }

        public void DropEquipment(WeaponSlot target)
        {
            if (!State.DragSource.HasValue)
            {
                return;
            }

            var source = State.DragSource.Value;
            if (!IsSlotContextValid(source))
            {
                dragVisual.Clear();
                State.ClearAll();
                RaiseChanged();
                return;
            }

            Equip(source, target);
        }

        public void EndDrag()
        {
            dragVisual.Clear();
            if (!State.DragSource.HasValue && !State.SelectingSplitTarget)
            {
                return;
            }

            State.DragSource = null;
            State.SelectingSplitTarget = false;
            RaiseChanged();
        }

        public void ClickEquipment(WeaponSlot slot)
        {
            State.InteractionSequence++;
            if (State.SelectingSplitTarget)
            {
                State.ClearSelection();
                ShowOperationFailed();
                RaiseChanged();
                return;
            }

            if (State.SelectedSlot.HasValue)
            {
                Equip(State.SelectedSlot.Value, slot);
                return;
            }

            if (State.SelectedEquipmentSlot == slot)
            {
                State.ClearSelection();
                RaiseChanged();
                return;
            }

            if(IsOnlineMode)
            {
                WeaponEquipmentSnapshot onlineEquipment=GetEquipmentView();
                int onlineItemId=slot==WeaponSlot.Melee?onlineEquipment.MeleeWeaponItemId:onlineEquipment.RangedWeaponItemId;
                if(onlineItemId<=0)
                {
                    State.ClearSelection();
                    if(slot==WeaponSlot.Melee)_=architecture.GetSystem<OnlineWorldInventorySystem>().SetActiveSlotAsync(slot);
                    RaiseChanged();return;
                }
                State.SelectedSlot=null;State.SelectedEquipmentSlot=slot;State.SelectedItemId=onlineItemId;State.SelectingSplitTarget=false;
                _=architecture.GetSystem<OnlineWorldInventorySystem>().SetActiveSlotAsync(slot);RaiseChanged();return;
            }

            var itemId = architecture.GetModel<WeaponEquipmentModel>().GetSlotItem(slot);
            if (itemId <= 0)
            {
                if (slot == WeaponSlot.Melee &&
                    architecture.GetSystem<WeaponEquipmentSystem>().TrySetActiveSlot(slot, out _))
                {
                    State.ClearSelection();
                    RaiseChanged();
                    return;
                }

                State.ClearSelection();
                RaiseChanged();
                return;
            }

            State.SelectedSlot = null;
            State.SelectedEquipmentSlot = slot;
            State.SelectedItemId = itemId;
            State.SelectingSplitTarget = false;
            architecture.GetSystem<WeaponEquipmentSystem>().TrySetActiveSlot(slot, out _);
            RaiseChanged();
        }

        public void BeginSplit()
        {
            if(IsOnlineMode){ShowOperationFailed();return;}
            if (!State.SelectedSlot.HasValue)
            {
                ShowOperationFailed();
                return;
            }

            State.SelectingSplitTarget = true;
            RaiseChanged();
        }

        public void UseSelected()
        {
            if(IsOnlineMode)
            {
                if(State.SelectedSlot.HasValue && IsSelectedClothing())
                {
                    WearOnlineClothing(State.SelectedSlot.Value,State.SelectedInventoryRevision);
                    State.ClearSelection();RaiseChanged();return;
                }
                if(State.SelectedEquipmentSlot.HasValue){_=architecture.GetSystem<OnlineWorldInventorySystem>().HolsterAsync();RaiseChanged();return;}
                if(State.SelectedSlot.HasValue&&TryGetOnlineWeaponSlot(State.SelectedSlot.Value,out WeaponSlot onlineSlot))
                {
                    var source=State.SelectedSlot.Value;
                    if(IsHeldSource(source))_=architecture.GetSystem<OnlineWorldInventorySystem>().HolsterAsync();
                    else _=architecture.GetSystem<OnlineWorldInventorySystem>().EquipWeaponAsync(source,onlineSlot);
                    RaiseChanged();return;
                }
                ShowOperationFailed();return;
            }
            if (State.SelectedEquipmentSlot.HasValue)
            {
                var success = architecture.GetSystem<WeaponEquipmentSystem>()
                    .TryUnequipToPlayerBag(State.SelectedEquipmentSlot.Value, out _);
                State.ClearSelection();
                if (!success)
                {
                    ShowOperationFailed();
                }

                RaiseChanged();
                return;
            }

            if (!State.SelectedSlot.HasValue ||
                !InventorySystem.IsCarriedContainer(State.SelectedSlot.Value.ContainerKind) ||
                !TryGetStack(State.SelectedSlot.Value, out var stack))
            {
                ShowOperationFailed();
                RaiseChanged();
                return;
            }

            architecture.GetUtility<IRuntimeConfigProvider>().TryGetItem(stack.ItemId, out var item, out _);
            if(IsSelectedClothing() || item.ItemType==global::ItemType.Clothing)
            {
                if(!architecture.GetSystem<ClothingEquipmentSystem>().TryEquip(State.SelectedSlot.Value,State.SelectedItemId,State.SelectedItemIdentity,out _))ShowOperationFailed();
                State.ClearSelection();RaiseChanged();return;
            }
            if (item.ActionKind == global::ItemActionKind.OpenMap)
            {
                if (!BagUICtrl.TryShowMapForItem(stack.ItemId, out _))
                {
                    Notify.ShowFloatTips(Loc.T("ui.map.error.unavailable"));
                }
                RaiseChanged();
                return;
            }

            if (item.ActionKind == global::ItemActionKind.ToggleTorch)
            {
                var held = architecture.GetSystem<WeaponEquipmentSystem>();
                var success = IsHeldSource(State.SelectedSlot.Value) ? held.TryToggleTorch(out _) : held.TryHold(State.SelectedSlot.Value, stack.ItemId, out _);
                if (!success) ShowOperationFailed();
                RaiseChanged(); return;
            }
            if (item.ItemType == global::ItemType.Weapon)
            {
                var held=architecture.GetSystem<WeaponEquipmentSystem>();
                var success=IsHeldSource(State.SelectedSlot.Value)?held.TryHolster(out _):held.TryHold(State.SelectedSlot.Value,stack.ItemId,out _);
                if (!success)
                {
                    ShowOperationFailed();
                }

                RaiseChanged();
                return;
            }

            pendingUseEffect = item.UseEffect;
            if (architecture.GetSystem<InventorySystem>()
                .TryBeginUseCarriedItem(State.SelectedSlot.Value, out _))
            {
                Notify.ShowFloatTips(Texts.InventoryUse);
            }
            else
            {
                ShowOperationFailed();
            }

            RaiseChanged();
        }

        public void DropSelectedToGround()
        {
            if(IsOnlineMode){State.ClearAll();ShowOperationFailed();RaiseChanged();return;}
            dragVisual.Clear();

            var selected = State.SelectedSlot;
            if (!selected.HasValue ||
                !InventorySystem.IsCarriedContainer(selected.Value.ContainerKind) ||
                !TryGetStack(selected.Value, out var stack) ||
                stack.ItemId != State.SelectedItemId)
            {
                State.ClearSelection();
                ShowOperationFailed();
                RaiseChanged();
                return;
            }

            var selectedItemId = State.SelectedItemId;
            State.ClearAll();

            IPlayerInventoryDropPort dropPort;
            try
            {
                dropPort = architecture.GetUtility<IPlayerInventoryDropPort>();
            }
            catch (Exception)
            {
                dropPort = null;
            }

            var success = architecture.GetSystem<InventorySystem>()
                .TryDropCarriedStack(selected.Value, dropPort, out _);
            if (!success)
            {
                if (TryGetStack(selected.Value, out var current) && current.ItemId == selectedItemId)
                {
                    State.SelectedSlot = selected;
                    State.SelectedEquipmentSlot = null;
                    State.SelectedItemId = selectedItemId;
                }

                ShowOperationFailed();
            }

            RaiseChanged();
        }

        public void ClearSelection()
        {
            State.ClearSelection();
            RaiseChanged();
        }

        /// <summary>点击转移按钮后提交领域事务；失败保留选择，成功清理拖拽与详情。</summary>
        public void MoveSelectedTo(InventoryContainerKind targetKind, int amount)
        {
            if (IsOnlineMode || !State.SelectedSlot.HasValue ||
                !architecture.GetSystem<InventorySystem>().TryMoveCarriedStack(State.SelectedSlot.Value, targetKind, amount, out _))
            {
                Notify.ShowFloatTips(Loc.T("ui.inventory.transfer_failed"));
                RaiseChanged();
                return;
            }
            dragVisual.Clear();
            State.ClearAll();
            RaiseChanged();
        }

        public void InvalidateContainer(InventoryContainerKind kind, long runtimeContainerId = 0)
        {
            var selected = State.SelectedSlot;
            var drag = State.DragSource;
            var invalidatesDrag = Matches(drag, kind, runtimeContainerId);
            var changed = ClearMatching(ref selected, kind, runtimeContainerId) |
                          ClearMatching(ref drag, kind, runtimeContainerId);
            if (changed)
            {
                if (invalidatesDrag)
                {
                    dragVisual.Clear();
                }

                State.ClearSelection();
                State.DragSource = null;
                RaiseChanged();
            }
        }

        public bool TryGetStack(InventorySlotRef slot, out ItemStack stack)
        {
            stack = null;
            if (slot.SlotIndex < 0)
            {
                return false;
            }

            if (IsOnlineMode)
            {
                if (TryGetContainerView(slot.ContainerKind, slot.RuntimeContainerId, out var online))
                    foreach (var entry in online.Slots) if (entry.SlotIndex == slot.SlotIndex) { stack = new ItemStack(entry.ItemId, entry.Amount); return true; }
                return false; // 联机不可回退读取离线口袋或仓库。
            }

            if (slot.ContainerKind == InventoryContainerKind.Pocket)
                return architecture.GetModel<PlayerBagModel>().Pocket.TryGet(slot.SlotIndex, out stack);

            if (slot.ContainerKind == InventoryContainerKind.PlayerBag)
            {
                return architecture.GetModel<PlayerBagModel>().Container.TryGet(slot.SlotIndex, out stack);
            }

            if (slot.ContainerKind == InventoryContainerKind.Warehouse)
            {
                return architecture.GetModel<WarehouseModel>().Container.TryGet(slot.SlotIndex, out stack);
            }

            if (slot.ContainerKind == InventoryContainerKind.Search && slot.RuntimeContainerId>0&&slot.RuntimeContainerId<=int.MaxValue&&
                architecture.GetSystem<SearchContainerSystem>().IsCurrentSession((int)slot.RuntimeContainerId) &&
                architecture.GetSystem<SearchContainerSystem>().TryGetSnapshot((int)slot.RuntimeContainerId, out var snapshot))
            {
                foreach (var entry in snapshot.Slots)
                {
                    if (entry.SlotIndex == slot.SlotIndex)
                    {
                        stack = new ItemStack(entry.ItemId, entry.Amount);
                        return true;
                    }
                }
            }

            return false;
        }

        public bool TryGetContainerView(InventoryContainerKind kind,long runtimeContainerId,out InventoryContainerViewSnapshot snapshot)
        {
            if(IsOnlineMode)
            {
                if(kind==InventoryContainerKind.PlayerBag){OnlineInventorySnapshot source=architecture.GetSystem<OnlineWorldSystem>().Snapshot.SelfInventory;snapshot=new InventoryContainerViewSnapshot(0,source.Capacity,source.Slots.Select(x=>new InventorySlotViewValue(x.SlotIndex,x.ItemId,x.Amount)).ToList().AsReadOnly());return true;}
                if(kind==InventoryContainerKind.Search&&architecture.GetSystem<OnlineWorldInventorySystem>().TryGetOpenCorpse(out OnlineWorldCorpseContentsSnapshot corpse)&&corpse.Corpse.CorpseId==runtimeContainerId){snapshot=new InventoryContainerViewSnapshot(runtimeContainerId,corpse.Capacity,corpse.Slots.Select(x=>new InventorySlotViewValue(x.SlotIndex,x.ItemId,x.Amount)).ToList().AsReadOnly());return true;}
                snapshot=default;return false;
            }
            if (kind == InventoryContainerKind.Pocket) { snapshot = View(architecture.GetModel<PlayerBagModel>().Pocket, 0); return true; }
            if(kind==InventoryContainerKind.PlayerBag||kind==InventoryContainerKind.Warehouse){ItemContainer container=kind==InventoryContainerKind.PlayerBag?architecture.GetModel<PlayerBagModel>().Container:architecture.GetModel<WarehouseModel>().Container;snapshot=View(container,0);return true;}
            if(kind==InventoryContainerKind.Search&&runtimeContainerId>0&&runtimeContainerId<=int.MaxValue&&architecture.GetSystem<SearchContainerSystem>().IsCurrentSession((int)runtimeContainerId)&&architecture.GetSystem<SearchContainerSystem>().TryGetSnapshot((int)runtimeContainerId,out SearchContainerSnapshot search)){snapshot=new InventoryContainerViewSnapshot(runtimeContainerId,search.Capacity,search.Slots.Select(x=>new InventorySlotViewValue(x.SlotIndex,x.ItemId,x.Amount)).ToList().AsReadOnly());return true;}
            snapshot=default;return false;
        }

        public WeaponEquipmentSnapshot GetEquipmentView(){if(IsOnlineMode){OnlineEquipmentSnapshot e=architecture.GetSystem<OnlineWorldSystem>().Snapshot.SelfInventory.Equipment;return new WeaponEquipmentSnapshot(new HeldItemSnapshot(e.ItemId>0?new InventorySlotRef(InventoryContainerKind.PlayerBag,e.SourceSlotIndex):(InventorySlotRef?)null,e.ItemId,architecture.GetSystem<OnlineWorldSystem>().Snapshot.SelfInventory.Revision),(WeaponSlot)e.ActiveSlot,e.RangedLoadedAmmo);}return architecture.GetModel<WeaponEquipmentModel>().CaptureSnapshot();}
        private bool IsHeldSource(InventorySlotRef source)
        { var held=GetEquipmentView().Source;return held.HasValue&&held.Value.ContainerKind==source.ContainerKind&&held.Value.SlotIndex==source.SlotIndex&&source.RuntimeContainerId==0; }
        public bool CanExecute(InventoryUiAction action)=>!IsOnlineMode||action==InventoryUiAction.Use;
        private static InventoryContainerViewSnapshot View(ItemContainer container,long id)=>new(id,container.Capacity,container.Slots.Select(x=>new InventorySlotViewValue(x.Key,x.Value.ItemId,x.Value.Amount)).ToList().AsReadOnly());

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            dragVisual.Dispose();
            useFinishedUnregister?.UnRegister();
            useFinishedUnregister = null;
            foreach (var registration in windows.Values)
            {
                if (registration.Background != null)
                {
                    registration.Background.raycastTarget = true;
                }
            }

            windows.Clear();
            State.ClearAll();
            Changed = null;
        }

        private void SelectIfNotEmpty(InventorySlotRef slot)
        {
            if (!TryGetStack(slot, out var stack))
            {
                State.ClearSelection();
            }
            else
            {
                State.SelectedSlot = slot;
                State.SelectedEquipmentSlot = null;
                State.SelectedItemId = stack.ItemId;
                CaptureSelectedIdentity(stack);
                State.SelectingSplitTarget = false;
            }

            RaiseChanged();
        }

        private void CaptureSelectedIdentity(ItemStack stack)
        {
            State.SelectedItemIdentity=stack.RuntimeIdentity;
            State.SelectedInventoryRevision=IsOnlineMode?architecture.GetSystem<OnlineWorldSystem>().Snapshot.SelfInventory.Revision:0;
        }

        private async void WearOnlineClothing(InventorySlotRef source,long revision)
        {
            var accepted=await architecture.GetSystem<OnlineWorldInventorySystem>().EquipClothingAsync(source,revision);
            if(!accepted&&!disposed)ShowOperationFailed();
        }

        private bool IsSelectedClothing()=>architecture.GetUtility<IRuntimeConfigProvider>().TryGetItem(State.SelectedItemId,out var item,out _)&&item.ItemType==global::ItemType.Clothing;

        private void Move(InventorySlotRef source, InventorySlotRef target)
        {
            dragVisual.Clear();
            if(IsOnlineMode){_=architecture.GetSystem<OnlineWorldInventorySystem>().MoveWholeStackAsync(source,target);State.ClearSelection();State.DragSource=null;RaiseChanged();return;}
            var success = architecture.GetSystem<InventorySystem>().TryTransfer(source, target, out _);
            State.ClearSelection();
            State.DragSource = null;
            if (!success)
            {
                ShowOperationFailed();
            }

            RaiseChanged();
        }

        private void Equip(InventorySlotRef source, WeaponSlot target)
        {
            dragVisual.Clear();
            var success = false;
            if (IsOnlineMode)
            {
                success = TryGetOnlineWeaponSlot(source,out WeaponSlot onlineSlot)&&onlineSlot==target;
                if (success)
                {
                    _ = architecture.GetSystem<OnlineWorldInventorySystem>().EquipWeaponAsync(source,target);
                }
            }
            else
            {
                success = architecture.GetSystem<WeaponEquipmentSystem>()
                    .TryEquipFromSlot(source, target, out _);
            }

            State.ClearAll();
            if (!success)
            {
                ShowOperationFailed();
            }

            RaiseChanged();
        }

        private void HandleSplitTarget(InventorySlotRef target)
        {
            var source = State.SelectedSlot.Value;
            if (source.ContainerKind == target.ContainerKind &&
                source.RuntimeContainerId == target.RuntimeContainerId &&
                source.SlotIndex == target.SlotIndex)
            {
                State.ClearSelection();
                RaiseChanged();
                return;
            }

            if (TryGetStack(target, out _))
            {
                State.SelectingSplitTarget = false;
                SelectIfNotEmpty(target);
                return;
            }

            var supported = source.RuntimeContainerId == 0 && target.RuntimeContainerId == 0 &&
                            source.ContainerKind == target.ContainerKind &&
                            source.ContainerKind != InventoryContainerKind.Search;
            var success = supported && architecture.GetSystem<InventorySystem>().TrySplitHalf(
                source.ContainerKind,
                source.SlotIndex,
                target.SlotIndex,
                out _);
            State.ClearSelection();
            if (!success)
            {
                ShowOperationFailed();
            }

            RaiseChanged();
        }

        private bool IsSlotContextValid(InventorySlotRef slot)
        {
            if(IsOnlineMode)return slot.ContainerKind==InventoryContainerKind.PlayerBag||slot.ContainerKind==InventoryContainerKind.Search&&architecture.GetSystem<OnlineWorldInventorySystem>().TryGetOpenCorpse(out OnlineWorldCorpseContentsSnapshot corpse)&&corpse.Corpse.CorpseId==slot.RuntimeContainerId;
            return slot.ContainerKind != InventoryContainerKind.Search ||
                   slot.RuntimeContainerId>0&&slot.RuntimeContainerId<=int.MaxValue&&architecture.GetSystem<SearchContainerSystem>().IsCurrentSession((int)slot.RuntimeContainerId);
        }

        private bool TryGetOnlineWeaponSlot(InventorySlotRef slot,out WeaponSlot weaponSlot)
        {
            weaponSlot=WeaponSlot.None;
            if(!IsOnlineMode||slot.ContainerKind!=InventoryContainerKind.PlayerBag||!IsSlotContextValid(slot)||!TryGetStack(slot,out ItemStack stack)||stack.Amount!=1)return false;
            IRuntimeConfigProvider config=architecture.GetUtility<IRuntimeConfigProvider>();
            if(!config.TryGetItem(stack.ItemId,out ItemDefinition item,out _)||item.ItemType!=global::ItemType.Weapon||
               !config.TryGetWeapon(stack.ItemId,out WeaponConfigDefinition weapon,out _))return false;
            weaponSlot=weapon.WeaponKind==global::WeaponKind.Melee?WeaponSlot.Melee:weapon.WeaponKind==global::WeaponKind.Projectile?WeaponSlot.Ranged:WeaponSlot.None;
            return weaponSlot!=WeaponSlot.None;
        }

        private void ClearStateForWindow(BaseUICtrl owner)
        {
            if (owner is WarehouseUICtrl)
            {
                InvalidateContainer(InventoryContainerKind.Warehouse);
            }
            else if (owner is SearchContainerUICtrl search)
            {
                InvalidateContainer(InventoryContainerKind.Search, search.CurrentContainerId);
            }
            else if (owner is PlayerBagWindowUICtrl)
            {
                InvalidateContainer(InventoryContainerKind.PlayerBag);
                InvalidateContainer(InventoryContainerKind.Pocket);
            }
        }

        private static bool ClearMatching(
            ref InventorySlotRef? value,
            InventoryContainerKind kind,
            long runtimeContainerId)
        {
            if (!value.HasValue || value.Value.ContainerKind != kind ||
                kind == InventoryContainerKind.Search && value.Value.RuntimeContainerId != runtimeContainerId)
            {
                return false;
            }

            value = null;
            return true;
        }

        private static bool Matches(
            InventorySlotRef? value,
            InventoryContainerKind kind,
            long runtimeContainerId)
        {
            return value.HasValue && value.Value.ContainerKind == kind &&
                   (kind != InventoryContainerKind.Search || value.Value.RuntimeContainerId == runtimeContainerId);
        }

        private void CoordinateBackgrounds()
        {
            BaseUICtrl top = null;
            long topSequence = long.MinValue;
            var visibleCount = 0;
            foreach (var pair in windows)
            {
                if (pair.Key == null || !pair.Key.IsShow())
                {
                    continue;
                }

                visibleCount++;
                if (pair.Value.Sequence > topSequence)
                {
                    top = pair.Key;
                    topSequence = pair.Value.Sequence;
                }
            }

            foreach (var pair in windows)
            {
                if (pair.Value.Background != null)
                {
                    pair.Value.Background.raycastTarget = pair.Key.IsShow() &&
                                                          (visibleCount <= 1 || !ReferenceEquals(pair.Key, top));
                }
            }
        }

        private void OnPlayerItemUseFinished(PlayerItemUseFinishedEvent evt)
        {
            Notify.ShowFloatTips(evt.Succeeded
                ? FormatUseApplied(pendingUseEffect, evt.AppliedValue)
                : Texts.InventoryOperationFailed);
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            Changed?.Invoke();
        }

        private static void ShowOperationFailed()
        {
            Notify.ShowFloatTips(Loc.T("ui.inventory.operation_failed"));
        }

        private static string FormatUseApplied(global::ItemUseEffect effect, int appliedValue)
        {
            switch (effect)
            {
                case global::ItemUseEffect.RestorePower:
                    return Texts.InventoryUseAppliedPower(appliedValue);
                case global::ItemUseEffect.RestoreWater:
                    return Texts.InventoryUseAppliedWater(appliedValue);
                default:
                    return Texts.InventoryUseApplied(appliedValue);
            }
        }

        private readonly struct WindowRegistration
        {
            public WindowRegistration(Image background, long sequence)
            {
                Background = background;
                Sequence = sequence;
            }

            public Image Background { get; }
            public long Sequence { get; }
        }
    }
}
