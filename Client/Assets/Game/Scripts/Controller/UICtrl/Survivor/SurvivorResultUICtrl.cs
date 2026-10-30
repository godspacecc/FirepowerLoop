using System;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine.EventSystems;
namespace Game
{
    /// <summary>保留当前结果直到保存及地图事务成功，失败可点击原操作再次重试。</summary>
    public sealed class SurvivorResultUICtrl : BaseUICtrl
    {
        private SurvivorResultUI UI => (SurvivorResultUI)BaseUI;
        /// <summary>防止离开与重开同时提交。</summary>
        private bool busy;
        public override UILayer Layer() => UILayer.Dialog;
        protected override string Path() => "Survivor/SurvivorResult";
        protected override void OnInit()
        {
            UI.BtnRestart.onClick.AddListener(()=>Leave(true).Forget());
            UI.BtnMenu.onClick.AddListener(()=>Leave(false).Forget());
            this.RegisterEvent<SurvivorChangedEvent>(_=>{if(IsShow())Refresh();}).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_=>{if(IsShow())Refresh();}).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }
        protected override void OnShow()=>Refresh();
        /// <summary>结果副本由模型提供，保存失败只显示错误而不重建结算。</summary>
        private void Refresh()
        {
            var result=this.GetModel<SurvivorModel>().ResultSnapshot;
            if(result==null){Hide();return;}
            UI.TimeText.text=Loc.T("ui.survivor.time",SurvivorHudUICtrl.FormatTime(result.SurvivalSeconds));
            UI.ScoreText.text=Loc.T("ui.survivor.result.score",result.Score.ToString("N0"));
            var error=this.GetSystem<SurvivorSystem>().LastError;
            UI.ErrorText.text=string.IsNullOrEmpty(error)?string.Empty:Loc.T("ui.survivor.result.retry",error);
            UI.ErrorText.gameObject.SetActive(!string.IsNullOrEmpty(error));
            UI.BtnRestart.interactable=UI.BtnMenu.interactable=!busy;
        }
        /// <summary>场景系统负责先保存后离开；原View仍有效时才恢复按钮。</summary>
        private async UniTask Leave(bool restart)
        {
            if(busy)return;busy=true;var owner=UI;Refresh();
            try
            {
                var flow=this.GetSystem<SurvivorSceneFlowSystem>();
                var result=restart?await flow.RestartAsync():await flow.ReturnToMenuAsync();
                if(!result.Success)Notify.ShowFloatTips(result.Error);
            }
            catch(Exception e){Log.Error($"[SurvivorResultUICtrl] 离开失败：{e}");Notify.ShowFloatTips(Loc.T("ui.survivor.error.open"));}
            finally{busy=false;if(owner!=null && owner==UI && IsShow())Refresh();}
        }
        protected override void OnHide()
        {
            var selected=EventSystem.current?.currentSelectedGameObject;
            if(selected!=null && selected.transform.IsChildOf(UI.transform))EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
