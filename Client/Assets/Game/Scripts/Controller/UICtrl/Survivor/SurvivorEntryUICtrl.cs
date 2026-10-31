using System;
using QFramework;
using UnityEngine.EventSystems;
namespace Game
{
    /// <summary>幸存者入口展示持有金币并提交开局意图，不持有金币权威状态。</summary>
    public sealed class SurvivorEntryUICtrl : BaseUICtrl
    {
        private SurvivorEntryUI UI=>(SurvivorEntryUI)BaseUI;
        /// <summary>开始菜单提供的开局入口，关闭后清除，避免旧窗口回调。</summary>
        private Action start;
        public override UILayer Layer()=>UILayer.Dialog;
        protected override string Path()=>"Survivor/SurvivorEntry";
        public void Configure(Action action){start=action;Refresh();}
        protected override void OnInit()
        {
            UI.BtnBack.onClick.AddListener(Hide);
            UI.BtnClose.onClick.AddListener(Hide);
            UI.BtnStart.onClick.AddListener(()=>{var action=start;if(action==null)return;start=null;Hide();action();});
            this.RegisterEvent<LanguageChangedEvent>(_=>{if(IsShow())Refresh();}).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }
        protected override void OnShow(){start=null;Refresh();}
        /// <summary>坏档时停留并显示失败，不覆盖余额、不允许开始。</summary>
        private void Refresh()
        {
            bool valid=this.GetSystem<SurvivorSystem>().TryReadGold(out var balance,out var error);
            UI.BalanceText.text=valid?balance.ToString("N0"):"—";
            UI.ErrorText.text=valid?string.Empty:Loc.T("ui.survivor.gold.error",error);
            UI.BtnStart.interactable=valid&&start!=null;
        }
        protected override void OnHide()
        {
            start=null;var selected=EventSystem.current?.currentSelectedGameObject;
            if(selected!=null&&selected.transform.IsChildOf(UI.transform))EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
