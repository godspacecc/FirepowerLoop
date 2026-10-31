using QFramework;
using UnityEngine;
namespace Game
{
    /// <summary>幸存者只读HUD，不阻断战斗输入。</summary>
    public sealed class SurvivorHudUICtrl : BaseUICtrl
    {
        private SurvivorHudUI UI => (SurvivorHudUI)BaseUI;
        /// <summary>可见期更新与事件订阅由同一开关成对释放。</summary>
        private bool joystickRegistered;
        /// <summary>列表绑定只读取当前装备快照，空槽与已有武器共用同一Box。</summary>
        private string[] weaponKinds=System.Array.Empty<string>();
        public override bool BlocksGame => false;
        public override UILayer Layer() => UILayer.HUD;
        protected override string Path() => "Survivor/SurvivorHud";
        protected override void OnInit()
        {
            UI.WeaponSlots.AddItemRefreshListener((item,index)=>{
                var box=item.GetComponent<SurvivorWeaponSlotBoxUI>();
                box.NameText.text=index<weaponKinds.Length?Loc.T("ui.survivor.weapon.slot",Loc.T("ui.survivor.weapon."+weaponKinds[index]),this.GetModel<SurvivorModel>().WeaponLevel(weaponKinds[index])):Loc.T("ui.survivor.weapon.empty");
            });
            this.RegisterEvent<MobileInputModeChangedEvent>(_=>{if(IsShow())RefreshJoystick();}).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MobileControlLayoutChangedEvent>(_=>{if(IsShow())RefreshJoystick();}).UnRegisterWhenGameObjectDestroyed(BaseUI);
            UI.BtnPause.onClick.AddListener(() => this.GetSystem<HotKeySystem>().TryExecuteAction(HotKeyActionIds.TogglePause));
            this.RegisterEvent<SurvivorChangedEvent>(_ => { if(IsShow()) Refresh(); }).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ => { if(IsShow()) Refresh(); }).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }
        protected override void OnShow()
        {
            if(!joystickRegistered){UIHelper.BlockingUIChanged+=OnBlockingUiChanged;LifeCycle.RegisterUpdate(RefreshJoystick);joystickRegistered=true;}
            Canvas.ForceUpdateCanvases();RefreshJoystick();Refresh();
        }
        protected override void OnHide()=>ReleaseJoystick();
        protected override void OnUnload()=>ReleaseJoystick();
        /// <summary>隐藏、卸载和遮挡都通过官方松手入口清零虚拟轴。</summary>
        private void ReleaseJoystick()
        {
            if(joystickRegistered){UIHelper.BlockingUIChanged-=OnBlockingUiChanged;LifeCycle.UnregisterUpdate(RefreshJoystick);joystickRegistered=false;}
            if(UI!=null){UI.MoveJoystick.OnPointerUp(null);UI.MoveJoystickRoot.SetActive(false);}
        }
        private void OnBlockingUiChanged(bool blocked)=>RefreshJoystick();
        /// <summary>使用闯关同一份移动布局设置；仅渲染移动摇杆。</summary>
        private void RefreshJoystick()
        {
            if(UI==null)return;
            // PC通过Esc暂停，触屏模式保留按钮；跟随输入模式切换即时刷新。
            bool mobile=this.GetSystem<MobileInputSystem>().IsMobileInputMode;
            UI.BtnPause.gameObject.SetActive(mobile);
            var state=this.GetModel<SurvivorModel>().Snapshot;
            bool visible=IsShow()&&state.Active&&!state.Finished&&!state.Paused&&!UIHelper.HasUIOpen&&mobile;
            if(!visible&&UI.MoveJoystickRoot.activeSelf)UI.MoveJoystick.OnPointerUp(null);
            UI.MoveJoystickRoot.SetActive(visible);
            if(UI.JoystickLayoutArea.rect.width<=0||UI.JoystickLayoutArea.rect.height<=0)return;
            var layout=this.GetModel<MobileControlLayoutModel>().ToData();
            var obstacles=new[]{MobileControlLayoutGeometry.Bounds(UI.JoystickLayoutArea,(RectTransform)UI.BtnPause.transform)};
            MobileControlLayoutGeometry.Render(UI.JoystickLayoutArea,(RectTransform)UI.MoveJoystickRoot.transform,UI.MoveOpacity,layout.Move,true,obstacles);
        }
        /// <summary>只展示当前快照；每次覆盖文字和条值，不缓存领域状态。</summary>
        private void Refresh()
        {
            var s=this.GetModel<SurvivorModel>().Snapshot;
            if(!s.Active || s.Finished){Hide();return;}
            UI.HealthText.text=Loc.T("ui.survivor.hud.health",s.Health,s.MaximumHealth);
            UI.TimeText.text=Loc.T("ui.survivor.time",FormatTime(s.SurvivalSeconds));
            UI.ScoreText.text=Loc.T("ui.survivor.hud.score",s.Score.ToString("N0"));
            UI.HealthBar.value=s.MaximumHealth>0?Mathf.Clamp01((float)s.Health/s.MaximumHealth):0;
            UI.ExperienceBar.value=s.RequiredExperience>0?Mathf.Clamp01((float)s.Experience/s.RequiredExperience):0;
            UI.BtnPause.interactable=!s.Paused;
            var model=this.GetModel<SurvivorModel>();weaponKinds=model.EquippedWeapons;
            UI.WeaponSlots.totalCount=model.WeaponSlotCapacity;UI.WeaponSlots.RefreshCells();
        }
        /// <summary>使用累计分钟，不在一小时后回绕。</summary>
        internal static string FormatTime(double seconds){long t=(long)System.Math.Max(0,seconds);return $"{t/60:00}:{t%60:00}";}
    }
}
