using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>展示随身库存中的手持候选并提交拿出/收起意图；不复制库存或管理角色模型。</summary>
    public sealed class HeldItemPickerUICtrl : BaseUICtrl
    {
        private HeldItemPickerUI UI => BaseUI as HeldItemPickerUI;
        private IItemTransferPort transfer;
        private readonly List<Candidate> candidates = new List<Candidate>();
        private readonly struct Candidate
        {
            public Candidate(InventorySlotRef source, int itemId, Guid identity)
            { Source = source; ItemId = itemId; Identity = identity; }
            public InventorySlotRef Source { get; }
            public int ItemId { get; }
            public Guid Identity { get; }
        }
        protected override string Path() => "Inventory/HeldItemPicker";
        public override UILayer Layer() => UILayer.Dialog;
        protected override BackgroundConfig GetBackgroundConfig() => new BackgroundConfig
        { ColorType = BackgroundColorType.Transparent, ClickEffect = BackgroundClickEffect.CloseOnly, Alpha = 0f };
        protected override void OnInit()
        {
            transfer = this.GetUtility<IItemTransferPort>();
            UI.BtnClose.onClick.AddListener(Hide);
            UI.BtnHolster.onClick.AddListener(Holster);
            UI.CandidateList.AddItemRefreshListener(RenderCandidate);
            this.RegisterEvent<InventoryChangedEvent>(_ => Refresh()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<WeaponEquipmentChangedEvent>(_ => Refresh()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineWorldInventoryChangedEvent>(_ => Refresh()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ => Refresh()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapLoadingStartedEvent>(_ => Hide()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<AppStateChangedEvent>(evt => { if (!InventoryOpenRules.CanOpenPlayerBag(out _)) Hide(); }).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }
        protected override void OnShow() => Refresh();
        protected override void OnHide() { candidates.Clear(); UI.CandidateList.ClearCells(); }
        /// <summary>重新读权威快照；联机只读取服务器背包，不回退到本地口袋。</summary>
        private void Refresh()
        {
            if (!IsShow()) return;
            if (!InventoryOpenRules.CanOpenPlayerBag(out _)) { Hide(); return; }
            candidates.Clear();
            foreach (var kind in new[] { InventoryContainerKind.Pocket, InventoryContainerKind.PlayerBag })
            {
                if (!transfer.TryGetContainerView(kind, 0, out var container)) continue;
                foreach (var slot in container.Slots)
                    if (slot.Amount == 1 && WeaponEquipmentModel.ValidateHeldWeapon(slot.ItemId,this.GetUtility<IRuntimeConfigProvider>(),out _) &&
                        (!transfer.IsOnlineMode || this.GetUtility<IRuntimeConfigProvider>().TryGetWeapon(slot.ItemId,out _,out _)))
                    {
                        var source = new InventorySlotRef(kind, slot.SlotIndex);
                        var identity = !transfer.IsOnlineMode && transfer.TryGetStack(source, out var stack) ? stack.RuntimeIdentity : Guid.Empty;
                        candidates.Add(new Candidate(source, slot.ItemId, identity));
                    }
            }
            UI.HintText.text = Loc.T(transfer.IsOnlineMode ? "ui.held.online_hint" : "ui.held.picker_hint");
            UI.EmptyRoot.SetActive(candidates.Count == 0);
            UI.BtnHolster.interactable = transfer.GetEquipmentView().Source.HasValue;
            UI.CandidateList.totalCount = candidates.Count;
            UI.CandidateList.RefreshCells();
        }
        private void RenderCandidate(Transform item, int index)
        {
            var view = item.GetComponent<InventorySlotBoxUI>();
            InventorySlotBoxReuse.Reset(view);
            var candidate = candidates[index];
            var provider = this.GetUtility<IRuntimeConfigProvider>();
            provider.TryGetItem(candidate.ItemId, out var config, out _);
            var held = transfer.GetEquipmentView().Source;
            var current = held.HasValue && held.Value.ContainerKind == candidate.Source.ContainerKind && held.Value.SlotIndex == candidate.Source.SlotIndex;
            view.ItemContentRoot.SetActive(true);
            view.IconImage.sprite = string.IsNullOrEmpty(config.IconAddress) ? null : ResourceLoader.LoadAsset<Sprite>(config.IconAddress);
            view.IconImage.gameObject.SetActive(view.IconImage.sprite != null);
            view.EmptyIconRoot.SetActive(view.IconImage.sprite == null);
            view.AmountText.text = current ? Loc.T("ui.held.current") : Loc.T(candidate.Source.ContainerKind == InventoryContainerKind.Pocket ? "ui.equipment.source_pocket" : "ui.equipment.source_bag");
            view.AmountText.gameObject.SetActive(true);
            view.SelectedFrame.SetActive(current);
            view.ClickButton.interactable = true;
            var bound = BaseUI;
            view.ClickButton.onClick.AddListener(() =>
            {
                if (!IsShow() || !ReferenceEquals(bound, BaseUI)) return;
                // 隐藏前校验实例，旧行不能把同格后放入的同类物品当成原候选。
                if (!transfer.TryGetStack(candidate.Source, out var stack) || stack.ItemId != candidate.ItemId ||
                    !transfer.IsOnlineMode && stack.RuntimeIdentity != candidate.Identity) { Refresh(); return; }
                Hide();
                if (transfer.IsOnlineMode) _ = this.GetSystem<OnlineWorldInventorySystem>().EquipWeaponAsync(candidate.Source, this.GetUtility<IRuntimeConfigProvider>().TryGetWeapon(candidate.ItemId,out var weapon,out _)&&weapon.WeaponKind==global::WeaponKind.Projectile ? WeaponSlot.Ranged : WeaponSlot.Melee);
                else this.GetSystem<WeaponEquipmentSystem>().TryHold(candidate.Source, candidate.ItemId, out _);
            });
        }
        private void Holster()
        {
            if (!UI.BtnHolster.interactable) return;
            Hide();
            if (transfer.IsOnlineMode) _ = this.GetSystem<OnlineWorldInventorySystem>().HolsterAsync();
            else this.GetSystem<WeaponEquipmentSystem>().TryHolster(out _);
        }
    }
}
