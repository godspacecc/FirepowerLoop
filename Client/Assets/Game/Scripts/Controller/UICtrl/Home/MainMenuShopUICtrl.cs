using System;
using QFramework;
using UnityEngine.EventSystems;
namespace Game
{
    /// <summary>商店只读现有持有金币，当前无购买功能。</summary>
    public sealed class MainMenuShopUICtrl : BaseUICtrl
    {
        private MainMenuShopUI UI=>(MainMenuShopUI)BaseUI;


        public override UILayer Layer()=>UILayer.Dialog;
        protected override string Path()=>"Home/MainMenuShop";

        protected override void OnInit()
        {
            UI.BtnBack.onClick.AddListener(Hide);
            UI.BtnClose.onClick.AddListener(Hide);
            this.RegisterEvent<LanguageChangedEvent>(_=>{if(IsShow())Refresh();}).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }
        protected override void OnShow(){Refresh();}
        /// <summary>每次显示与语言变化重新只读余额，坏档显示未知与错误，不写盘。</summary>
        private void Refresh()
        {
            bool valid=this.GetSystem<SurvivorSystem>().TryReadGold(out var balance,out var error);
            UI.BalanceText.text=Loc.T("ui.shop.balance",valid?balance.ToString("N0"):"—");
            UI.ErrorText.text=valid?string.Empty:Loc.T("ui.survivor.gold.error",error);
        }
        protected override void OnHide()
        {
            var selected=EventSystem.current?.currentSelectedGameObject;
            if(selected!=null&&selected.transform.IsChildOf(UI.transform))EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
