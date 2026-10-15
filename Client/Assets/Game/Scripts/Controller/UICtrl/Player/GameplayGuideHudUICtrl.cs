using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 渲染常规离线玩法的暂停入口、电脑操作指引与版本信息；持有会话内提示显隐，不改变玩法状态或街机界面。
    /// </summary>
    public sealed class GameplayGuideHudUICtrl : BaseUICtrl
    {
        private GameplayGuideHudUI UI => BaseUI as GameplayGuideHudUI;
        private bool blockingSubscribed;
        private bool layoutUpdateRegistered;
        private Vector2 layoutSize;
        /// <summary>本会话电脑提示的用户选择；地图、语言和阻断刷新保留，不写入存档或影响手机暂停。</summary>
        private bool desktopGuideHidden;

        public override UILayer Layer() => UILayer.HUD;
        public override bool BlocksGame => false;
        protected override string Path() => "Player/GameplayGuideHud";

        protected override void OnInit()
        {
            UI.BtnPause.onClick.AddListener(OpenPause);
            UI.MobileBtnPause.onClick.AddListener(OpenPause);
            this.RegisterEvent<AppStateChangedEvent>(_ => Refresh()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ => Refresh()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<ResourceUpdateChangedEvent>(_ => Refresh()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<HotKeyBindingsChangedEvent>(_ => Refresh()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MobileInputModeChangedEvent>(_ => Refresh()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<DialogueChangedEvent>(_ => Refresh()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MobileControlLayoutChangedEvent>(_ => ApplyMobileLayout()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapLoadingStartedEvent>(_ => Hide()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapLoadingCompletedEvent>(OnMapLoadingCompleted).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapCurrentReleasedEvent>(_ => Hide()).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            if (!blockingSubscribed)
            {
                UIHelper.BlockingUIChanged += OnBlockingUIChanged;
                blockingSubscribed = true;
            }
            Refresh();
            if (IsShow() && !layoutUpdateRegistered) { LifeCycle.RegisterUpdate(RefreshMobileLayout); layoutUpdateRegistered = true; }
            LifeCycle.RegisterLateUpdate(QueueInteractionPrompt);
        }

        protected override void OnHide() => UnsubscribeBlockingUI();
        protected override void OnUnload() => UnsubscribeBlockingUI();

        private void UnsubscribeBlockingUI()
        {
            LifeCycle.UnregisterLateUpdate(QueueInteractionPrompt);
            if (!UnityObjectState.IsMissing(UI)) UI.InteractionRoot.SetActive(false);
            if (layoutUpdateRegistered) { LifeCycle.UnregisterUpdate(RefreshMobileLayout); layoutUpdateRegistered = false; }
            if (!blockingSubscribed) return;
            UIHelper.BlockingUIChanged -= OnBlockingUIChanged;
            blockingSubscribed = false;
        }

        private void OnBlockingUIChanged(bool _) => Refresh();

        /// <summary>与准星目标查询一起延迟到相机更新之后，UI 只渲染焦点的只读名字和动作。</summary>
        private void QueueInteractionPrompt() => LifeCycle.ScheduleFrameEnd(this, RefreshInteractionPrompt);

        private void RefreshInteractionPrompt()
        {
            if (UnityObjectState.IsMissing(UI)) return;
            var interaction = this.GetSystem<PlayerInteractionSystem>();
            var name = interaction.CrosshairTargetName;
            var visible = IsShow() && interaction.UsesCrosshairTargeting && !UIHelper.HasUIOpen &&
                          this.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing && !string.IsNullOrWhiteSpace(name);
            UI.InteractionRoot.SetActive(visible);
            UI.InteractionText.text = visible ? name + "\n" +
                (this.GetSystem<MobileInputSystem>().IsMobileInputMode ? Texts.MobileInteract : Loc.T(interaction.CrosshairActionKey, "E")) : string.Empty;
        }

        private void Refresh()
        {
            if (UnityObjectState.IsMissing(UI)) return;
            RefreshInteractionPrompt();
            var map = this.GetModel<MapSceneModel>().CurrentMapId;
            if (ShouldSuppressForMap(map))
            {
                Hide();
                return;
            }

            var inOfflineMap = !string.IsNullOrWhiteSpace(map) && this.GetModel<SaveModel>().CurrentDomain != OfflineSaveDomain.None;
            if (!inOfflineMap)
            {
                Hide();
                return;
            }

            if (!IsShow())
            {
                return;
            }

            var playing = this.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing;
            var mobile = this.GetSystem<MobileInputSystem>().IsMobileInputMode;
            var inDialogue = this.GetSystem<DialogueSystem>()?.Current != null;
            // 阻断窗口由固定层级遮挡 HUD；实际输入仍由各入口的门禁拒绝。
            UI.GuideRoot.SetActive(!desktopGuideHidden && !mobile && playing && !inDialogue);
            // 电脑使用暂停快捷键；手机保留触屏入口，仍复用同一暂停事务。
            UI.BtnPause.gameObject.SetActive(false);
            // 交谈期间保留唯一可用的暂停入口，其余手机控件由各自 HUD 隐藏。
            UI.MobileBtnPause.gameObject.SetActive(mobile && playing);
            if (mobile) ApplyMobileLayout();
            // 与摇杆复用当前会话输入模式，Editor 模拟也实时切换，不显示尚未接入的手机操作。
            if (this.GetSystem<MobileInputSystem>().IsMobileInputMode)
                UI.GuideText.text = Loc.T("ui.gameplay_guide.mobile_lines");
            else
            {
                var hotkeys = this.GetModel<HotKeyModel>();
                UI.GuideText.text = Loc.T("ui.gameplay_guide.lines",
                    HotKeyDisplayFormatter.FormatAction(hotkeys, HotKeyActionIds.ToggleInventory),
                    HotKeyDisplayFormatter.FormatAction(hotkeys, HotKeyActionIds.TogglePause),
                    HotKeyDisplayFormatter.FormatAction(hotkeys, HotKeyActionIds.ToggleGameplayGuide));
            }
            var version = this.GetSystem<ResourceUpdateSystem>().Snapshot.CurrentVersion;
            UI.VersionText.text = string.IsNullOrWhiteSpace(version)
                ? string.Empty
                : Loc.T("ui.gameplay_guide.version", version);
            UI.VersionText.gameObject.SetActive(!string.IsNullOrWhiteSpace(UI.VersionText.text));
        }

        /// <summary>仅允许已显示 HUD 的常规离线电脑玩法切换提示；暂停、加载、阻断与手机模式拒绝操作。</summary>
        public bool CanToggleDesktopGuide()
        {
            var map = this.GetModel<MapSceneModel>();
            return !UnityObjectState.IsMissing(UI) && IsShow() &&
                   !this.GetSystem<MobileInputSystem>().IsMobileInputMode &&
                   this.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing &&
                   map.Status.Value == MapLoadStatus.Loaded &&
                   !string.IsNullOrWhiteSpace(map.CurrentMapId) && !ShouldSuppressForMap(map.CurrentMapId) &&
                   this.GetModel<SaveModel>().CurrentDomain != OfflineSaveDomain.None && !UIHelper.HasUIOpen;
        }

        /// <summary>热键通过门禁后切换用户选择并重绘提示；重复按键逐次切换，版本和暂停入口不受影响。</summary>
        public void ToggleDesktopGuide()
        {
            if (!CanToggleDesktopGuide()) return;
            desktopGuideHidden = !desktopGuideHidden;
            Refresh();
        }

        /// <summary>暂停按钮使用自己的安全区容器；尺寸变化和保存事件只重绘位置，不触发暂停。</summary>
        private void ApplyMobileLayout()
        {
            if (UnityObjectState.IsMissing(UI)) return;
            layoutSize = UI.MobilePauseArea.rect.size;
            if (layoutSize.x <= 0 || layoutSize.y <= 0) return;
            MobileControlLayoutGeometry.RenderButton(UI.MobilePauseArea, (RectTransform)UI.MobileBtnPause.transform,
                this.GetModel<MobileControlLayoutModel>().ToData().Pause);
        }

        private void RefreshMobileLayout()
        {
            if (!UnityObjectState.IsMissing(UI) && UI.MobilePauseArea.rect.size != layoutSize) ApplyMobileLayout();
        }

        /// <summary>供同屏操控避让实际暂停入口；未加载或隐藏的 HUD 不占用触控区域。</summary>
        public Rect MobilePauseBounds(RectTransform area)
        {
            if (UnityObjectState.IsMissing(UI) || !UI.MobileBtnPause.gameObject.activeInHierarchy) return default;
            RefreshMobileLayout();
            return MobileControlLayoutGeometry.Bounds(area, (RectTransform)UI.MobileBtnPause.transform);
        }

        /// <summary>透明对话背景只转交暂停按钮区域的点击，不向其它 HUD 或世界转发。</summary>
        internal bool TryOpenPauseAtScreenPosition(Vector2 position)
        {
            if (!IsShow() || UnityObjectState.IsMissing(UI) || !UI.MobileBtnPause.gameObject.activeInHierarchy) return false;
            var canvas = UI.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (!RectTransformUtility.RectangleContainsScreenPoint((RectTransform)UI.MobileBtnPause.transform, position, camera)) return false;
            OpenPause();
            return this.GetModel<AppStateModel>().CurrentState.Value == AppState.Paused;
        }

        /// <summary>手机离线地图复用暂停仲裁；最上层为对话时允许暂停，其它阻断窗口拒绝旧点击。</summary>
        private void OpenPause()
        {
            var map = this.GetModel<MapSceneModel>().CurrentMapId;
            var blocker = UIHelper.GetTopmostBlockingUI();
            if (string.IsNullOrWhiteSpace(map) || ShouldSuppressForMap(map) ||
                this.GetModel<SaveModel>().CurrentDomain == OfflineSaveDomain.None ||
                (!this.GetSystem<MobileInputSystem>().IsMobileInputMode && map != MapIds.Base) ||
                this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing ||
                (blocker != null && !(blocker is DialogueUICtrl))) return;
            if (this.GetSystem<HotKeySystem>().TryExecuteAction(HotKeyActionIds.TogglePause))
                this.GetSystem<AudioSystem>()?.PlayUiSound("FoundationClick");
        }

        /// <summary>地图完成后在常规离线模式重新显示整张指引界面。</summary>
        private void OnMapLoadingCompleted(MapLoadingCompletedEvent _)
        {
            var map = this.GetModel<MapSceneModel>().CurrentMapId;
            var eligible = !ShouldSuppressForMap(map) &&
                           !string.IsNullOrWhiteSpace(map) &&
                           this.GetModel<SaveModel>().CurrentDomain != OfflineSaveDomain.None;
            if (!eligible)
            {
                Hide();
                return;
            }

            if (!IsShow()) Show();
            else Refresh();
        }

        /// <summary>
        /// 街机使用独立界面，常规操作指引和版本文字均不显示。
        /// </summary>
        private static bool ShouldSuppressForMap(string mapId)
        {
            return mapId == MapIds.Arcade;
        }
    }
}
