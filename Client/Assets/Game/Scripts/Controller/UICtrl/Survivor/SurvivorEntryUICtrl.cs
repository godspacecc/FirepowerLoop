using System;
using System.Linq;
using QFramework;
using UnityEngine;
using UnityEngine.EventSystems;
namespace Game
{
    /// <summary>呈现角色权威快照并提交购买或选择意图，不持有第二份钱包。</summary>
    public sealed class SurvivorEntryUICtrl : BaseUICtrl
    {
        /// <summary>生成器绑定的当前界面。</summary>
        private SurvivorEntryUI UI => (SurvivorEntryUI)BaseUI;
        /// <summary>仅供当前列表绘制的一致性快照。</summary>
        private SurvivorCharacterProduct[] products = Array.Empty<SurvivorCharacterProduct>();
        /// <summary>当前失败提示，语言变化时重新翻译。</summary>
        private string errorCode;
        /// <summary>仅当前菜单会话有效的开局回调。</summary>
        private Action start;
        /// <summary>绑定当前菜单请求，重复打开刷新当前快照。</summary>
        public void Configure(Action action) { start = action; errorCode = null; Refresh(); }
        /// <summary>提交前重验所有权；先消费回调再关闭，阻止重复开局。</summary>
        private void Begin()
        {
            if (!IsShow() || start == null) return;
            if (!this.GetSystem<SurvivorSystem>().TrySelectCharacter(this.GetModel<SurvivorModel>().SelectedCharacter, out errorCode)) { Refresh(); return; }
            var callback = start;
            start = null;
            Hide();
            callback();
        }
        public override UILayer Layer() => UILayer.Dialog;
        protected override string Path() => "Survivor/SurvivorEntry";
        protected override void OnInit()
        {
            UI.BtnBack.onClick.AddListener(Hide);
            UI.BtnClose.onClick.AddListener(Hide);
            UI.Cards.AddItemRefreshListener(RenderCard);
            UI.BtnStart.onClick.AddListener(Begin);
            this.RegisterEvent<LanguageChangedEvent>(_ => { if (IsShow()) Refresh(); }).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<SurvivorChangedEvent>(_ => { if (IsShow()) Refresh(); }).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }
        protected override void OnShow() { errorCode = null; start = null; Refresh(); }
        /// <summary>重复打开也显式刷新，不依赖重复Show触发生命周期。</summary>
        public void RefreshContent() { errorCode = null; Refresh(); }
        /// <summary>读取失败保留未知余额并清除旧商品，绝不伪造零金币。</summary>
        private void Refresh()
        {
            bool valid = this.GetSystem<SurvivorSystem>().TryReadCharacterShop(out var snapshot, out var error);
            products = valid ? snapshot.Products : Array.Empty<SurvivorCharacterProduct>();
            UI.BalanceText.text = Loc.T("ui.shop.balance", valid ? snapshot.Balance.ToString("N0") : "—");
            UI.ErrorText.text = ErrorText(valid ? errorCode : error);
            UI.Cards.totalCount = valid ? products.Length + 1 : 0;
            UI.Cards.RefreshCells();
            UI.BtnStart.interactable = valid && start != null;
        }
        /// <summary>每次复用重置所有文本、图片、选择、禁用状态和回调。</summary>
        private void RenderCard(Transform target, int index)
        {
            var product = index == 0 ? new SurvivorCharacterProduct("", 0, 0, "ui.survivor.character.0", true) : products[index - 1];
            var box = target.GetComponent<CharacterCardBoxUI>();
            int id = product.AppearanceId;
            box.NameText.text = Loc.T(product.NameKey);
            box.Portrait.sprite = id == 0 ? UI.Portrait0 : id == 1 ? UI.Portrait1 : id == 7 ? UI.Portrait7 : id == 3 ? UI.Portrait3 : id == 4 ? UI.Portrait4 : UI.Portrait9;
            bool selected = this.GetModel<SurvivorModel>().SelectedCharacter == id;
            box.Portrait.color = product.Owned ? Color.white : new Color(.35f,.35f,.35f);
            box.SelectionOutline.effectColor = selected ? new Color(.95f,.76f,.33f) : new Color(.24f,.33f,.38f);
            box.ActionText.text = Loc.T("ui.character." + (!product.Owned ? "locked" : selected ? "selected" : "select"));
            box.BtnAction.image.color = selected ? new Color(.70f, .55f, .27f) : new Color(.22f, .30f, .35f);
            box.BtnAction.interactable = product.Owned;
            box.BtnAction.onClick.RemoveAllListeners();
            var owner = BaseUI;
            box.BtnAction.onClick.AddListener(() => {
                if (!IsShow() || owner == null || !ReferenceEquals(owner, BaseUI)) return;
                this.GetSystem<SurvivorSystem>().TrySelectCharacter(id, out errorCode);
                Refresh();
            });
        }
        /// <summary>错误码仅映射玩家文案，不泄露内部代码。</summary>
        private static string ErrorText(string error)
        {
            if (string.IsNullOrEmpty(error)) return string.Empty;
            string key = error == "wallet.insufficient" ? "insufficient" : error == "wallet.read_failed" ? "read_failed" : error == "character.not_owned" ? "not_owned" : "error";
            return Loc.T("ui.character." + key);
        }
        protected override void OnHide()
        {
            start = null;
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(UI.transform)) EventSystem.current.SetSelectedGameObject(null);
        }
        protected override void OnUnload() { start = null; products = Array.Empty<SurvivorCharacterProduct>(); }
    }
}
