using QFramework;
using UnityEngine;
namespace Game
{
    /// <summary>幸存者只读HUD，不阻断战斗输入。</summary>
    public sealed partial class SurvivorHudUICtrl : BaseUICtrl
    {
        private SurvivorHudUI UI => (SurvivorHudUI)BaseUI;
        /// <summary>可见期更新与事件订阅由同一开关成对释放。</summary>
        private bool joystickRegistered;
        /// <summary>实时时钟仅在可见期按秒采样、按分钟写文本，不受游戏时间缩放影响。</summary>
        private float nextClockSample;
        private long renderedClockMinute=long.MinValue;
        /// <summary>列表绑定只读取当前装备快照，空槽与已有武器共用同一Box。</summary>
        private string[] weaponKinds=System.Array.Empty<string>();
        public override bool BlocksGame => false;
        public override UILayer Layer() => UILayer.HUD;
        protected override string Path() => "Survivor/SurvivorHud";
        protected override void OnInit()
        {
            goldTextScale=UI.ScoreText.transform.localScale;
            UI.GoldFlightList.AddItemRefreshListener(RefreshGoldFlight);
            UI.WeaponSlots.AddItemRefreshListener((item,index)=>{
                var box=item.GetComponent<SurvivorWeaponSlotBoxUI>();
                RenderWeaponSlot(box,this.GetModel<SurvivorModel>(),index<weaponKinds.Length?weaponKinds[index]:null);
            });
            this.RegisterEvent<MobileInputModeChangedEvent>(_=>{if(IsShow())RefreshJoystick();}).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MobileControlLayoutChangedEvent>(_=>{if(IsShow())RefreshJoystick();}).UnRegisterWhenGameObjectDestroyed(BaseUI);
            UI.BtnPause.onClick.AddListener(() => this.GetSystem<HotKeySystem>().TryExecuteAction(HotKeyActionIds.TogglePause));
            this.RegisterEvent<SurvivorChangedEvent>(_ => { if(IsShow()) Refresh(); }).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ => { if(IsShow()){Refresh();RenderLocalClock(System.DateTime.Now,true);} }).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }
        protected override void OnShow()
        {
            if(!joystickRegistered){UIHelper.BlockingUIChanged+=OnBlockingUiChanged;LifeCycle.RegisterUpdate(RefreshVisibleHud);joystickRegistered=true;}
            nextClockSample=Time.realtimeSinceStartup+1f;RenderLocalClock(System.DateTime.Now,true);
            Canvas.ForceUpdateCanvases();RefreshJoystick();Refresh();
        }
        protected override void OnHide()=>ReleaseJoystick();
        protected override void OnUnload()=>ReleaseJoystick();
        /// <summary>隐藏、卸载和遮挡都通过官方松手入口清零虚拟轴。</summary>
        private void ReleaseJoystick()
        {
            ClearGoldPickupFeedback();
            if(joystickRegistered){UIHelper.BlockingUIChanged-=OnBlockingUiChanged;LifeCycle.UnregisterUpdate(RefreshVisibleHud);joystickRegistered=false;}
            if(UI!=null){UI.MoveJoystick.OnPointerUp(null);UI.MoveJoystickRoot.SetActive(false);}
        }
        /// <summary>可见期回调与摇杆共同注册；暂停只冻结游戏计时，不冻结本地时钟。</summary>
        private void RefreshVisibleHud()
        {
            if(UI==null||!IsShow())return;
            RefreshJoystick();
            AdvanceGoldPickups(Time.unscaledDeltaTime);
            if(Time.realtimeSinceStartup<nextClockSample)return;
            nextClockSample=Time.realtimeSinceStartup+1f;
            RenderLocalClock(System.DateTime.Now,false);
        }
        /// <summary>缓存完整日期分钟，支持跨午夜、系统校时与语言切换；同分钟不改文字。</summary>
        internal bool RenderLocalClock(System.DateTime now,bool force)
        {
            long minute=now.Ticks/System.TimeSpan.TicksPerMinute;
            if(!force&&minute==renderedClockMinute)return false;
            renderedClockMinute=minute;
            UI.ClockText.text=Loc.T("ui.survivor.hud.clock",now.ToString("HH:mm",System.Globalization.CultureInfo.InvariantCulture));
            return true;
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
            RefreshGoldNumber();
            var mission = this.GetSystem<SurvivorSystem>();
            if (mission.IsMission)
            {
                UI.ScoreText.text = Loc.T("ui.mission.progress", mission.MissionCollected, mission.MissionSupplyCount, mission.MissionRemainingEnemies);
                UI.TimeText.text = FormatMissionPhase(mission);
            }
            UI.HealthBar.value=s.MaximumHealth>0?Mathf.Clamp01((float)s.Health/s.MaximumHealth):0;
            UI.ExperienceBar.value=s.RequiredExperience>0?Mathf.Clamp01((float)s.Experience/s.RequiredExperience):0;
            UI.BtnPause.interactable=!s.Paused;
            var model=this.GetModel<SurvivorModel>();weaponKinds=model.EquippedWeapons;
            UI.WeaponSlots.totalCount=model.WeaponSlotCapacity;UI.WeaponSlots.RefreshCells();
        }
        /// <summary>阶段由任务权威提供；当前目标与范围提示不改变规则。</summary>
        private static string FormatMissionPhase(SurvivorSystem mission)
        {
            var offset=mission.MissionTargetOffset;
            switch(mission.MissionPhase)
            {
                case MissionPhase.Entry:return Loc.T("ui.mission.entry");
                case MissionPhase.Search:return mission.MissionCanCollect?FormatMissionTarget(offset,false):Loc.T("ui.mission.encounter",Loc.T("ui.mission.site."+mission.MissionCurrentSearchIndex));
                case MissionPhase.Activate:
                    return offset.magnitude>mission.MissionFacilityRadius?FormatMissionDestination(offset,"ui.mission.facility"):Loc.T("ui.mission.activate",Mathf.CeilToInt(mission.MissionPhaseSecondsRemaining));
                case MissionPhase.Defend:
                    return offset.magnitude>mission.MissionFacilityRadius?Loc.T("ui.mission.return_facility"):mission.MissionPhaseSecondsRemaining>0?Loc.T("ui.mission.defend",Mathf.CeilToInt(mission.MissionPhaseSecondsRemaining)):Loc.T("ui.mission.clear");
                case MissionPhase.Breakthrough:return mission.MissionWaveIndex==0?FormatMissionDestination(offset,"ui.mission.breakthrough"):Loc.T("ui.mission.wave",mission.MissionWaveIndex);
                default:return FormatMissionTarget(offset,true);
            }
        }
        /// <summary>设施及突破点沿用现有方向文本，不创建新的屏幕布局。</summary>
        private static string FormatMissionDestination(Vector2 offset,string key)
        {
            string direction=Mathf.Abs(offset.x)>Mathf.Abs(offset.y)?(offset.x>=0?"east":"west"):(offset.y>=0?"north":"south");
            return Loc.T(key,Loc.T("ui.mission."+direction),Mathf.CeilToInt(offset.magnitude));
        }
        /// <summary>复用现有文字栏提示目标方位和距离，不在界面推进收集或撤离规则。</summary>
        internal static string FormatMissionTarget(Vector2 offset, bool extraction)
        {
            string direction = Mathf.Abs(offset.x) > Mathf.Abs(offset.y)
                ? (offset.x >= 0 ? "east" : "west") : (offset.y >= 0 ? "north" : "south");
            return Loc.T(extraction ? "ui.mission.extract" : "ui.mission.search", Loc.T("ui.mission." + direction), Mathf.CeilToInt(offset.magnitude));
        }

        /// <summary>从权威模型完整覆盖槽状态；无人机无弹夹，空槽不沿用上次图标或进度。</summary>
        internal static void RenderWeaponSlot(SurvivorWeaponSlotBoxUI box,SurvivorModel model,string kind)
        {
            bool rifle=kind=="Rifle",drone=kind=="Drone",molotov=kind=="Molotov";
            box.Icon.gameObject.SetActive(rifle||drone||molotov);
            box.Icon.sprite=rifle?box.RifleIcon:drone?box.DroneIcon:molotov?box.MolotovIcon:null;
            box.AmmoRing.gameObject.SetActive(rifle||molotov);
            // 投掷冷却复用换弹外圈与颜色，仅消费系统快照，不增加UI计时器。
            if(molotov)
            {
                box.AmmoRing.fillAmount=model.MolotovCooldownProgress;
                box.AmmoRing.color=model.MolotovCooldownTimeRemaining>0?new Color(.25f,.75f,1f):new Color(1f,.94f,.72f);
                return;
            }
            bool reloading=rifle&&model.RifleReloadTimeRemaining>0;
            float amount=reloading?model.RifleReloadProgress:
                model.RifleMagazineSize>0?Mathf.Clamp01((float)model.RifleAmmo/model.RifleMagazineSize):0f;
            box.AmmoRing.fillAmount=rifle?amount:0f;
            box.AmmoRing.color=reloading?new Color(.25f,.75f,1f):
                amount<.25f?new Color(1f,.55f,.15f):new Color(1f,.94f,.72f);
        }
        /// <summary>使用累计分钟，不在一小时后回绕。</summary>
        internal static string FormatTime(double seconds){long t=(long)System.Math.Max(0,seconds);return $"{t/60:00}:{t%60:00}";}
    }
}
