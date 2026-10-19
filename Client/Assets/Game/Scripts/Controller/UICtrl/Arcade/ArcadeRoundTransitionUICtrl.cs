using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>局内切关的全屏黑幕；只按未缩放时间淡出/淡入，不裁决地图就绪。</summary>
    public sealed class ArcadeRoundTransitionUICtrl : BaseUICtrl
    {
        private ArcadeRoundTransitionUI UI=>BaseUI as ArcadeRoundTransitionUI;
        /// <summary>外部事务仍持有黑幕，重复请求不重置进度。</summary>
        private bool held;
        private bool tickRegistered;
        private const float FadeSeconds=.2f;
        public bool Covered=>!IsShow()||UI.Curtain.alpha>=.999f;
        protected override string Path()=>"Arcade/ArcadeRoundTransition";
        public override UILayer Layer()=>UILayer.Top;
        protected override bool CanHide()=>!held;
        protected override void OnInit()
        {
            this.RegisterEvent<MapCurrentReleasedEvent>(_=>Abort()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<ArcadeRunChangedEvent>(evt=>
            {
                if(evt.Phase!=ArcadeRunPhase.Arena&&evt.Phase!=ArcadeRunPhase.Challenge)Abort();
            }).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }
        protected override void OnShow()
        {
            if(!tickRegistered){LifeCycle.RegisterUpdate(Tick);tickRegistered=true;}
        }
        protected override void OnHide()
        {
            if(tickRegistered)LifeCycle.UnregisterUpdate(Tick);tickRegistered=false;
            UI.Curtain.alpha=0;
            if(UnityEngine.EventSystems.EventSystem.current!=null)UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }
        protected override void OnUnload()
        {
            if(tickRegistered)LifeCycle.UnregisterUpdate(Tick);tickRegistered=false;held=false;
        }
        /// <summary>从当前透明度转为淡出，支持淡入过程中收到新切关请求。</summary>
        public void Begin(){held=true;if(!IsShow())Show();}
        /// <summary>资源已准备完成，下一帧起淡入；输入保持阻断到完全揭幕。</summary>
        public void Finish(){held=false;}
        /// <summary>离局或卸图立即释放黑幕，旧生命周期不能遮住菜单。</summary>
        public void Abort(){held=false;Hide();}
        private void Tick()=>TickFade(Time.unscaledDeltaTime);
        /// <summary>只推进显示透明度；时间不受玩法暂停或 timeScale 影响。</summary>
        private void TickFade(float seconds)
        {
            if(!IsShow())return;
            UI.Curtain.alpha=Mathf.MoveTowards(UI.Curtain.alpha,held?1:0,Mathf.Max(0,seconds)/FadeSeconds);
            if(!held&&UI.Curtain.alpha<=0)Hide();
        }
    }
}
