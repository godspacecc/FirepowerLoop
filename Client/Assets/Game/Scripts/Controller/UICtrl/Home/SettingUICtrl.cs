using UnityEngine.UI;
using QFramework;

namespace Game
{
    /// <summary>
    /// 设置窗口只负责左侧分页导航与关闭，不持有各设置项的业务状态。
    /// </summary>
    public sealed class SettingUICtrl : BaseUICtrl
    {
        private enum Page { General, Audio, Controls, Network }

        private static Page lastPage;
        private readonly SettingGeneralPageCtrl general = new SettingGeneralPageCtrl();
        private readonly SettingAudioPageCtrl audio = new SettingAudioPageCtrl();
        private readonly SettingControlsPageCtrl controls = new SettingControlsPageCtrl();
        private readonly SettingNetworkPageCtrl network = new SettingNetworkPageCtrl();
        private BasePageCtrl current;

        private SettingUI UI => BaseUI as SettingUI;
        public override UILayer Layer() => UILayer.Dialog;
        protected override string Path() => "Home/Setting";

        protected override void OnInit()
        {
            UI.BtnExit.onClick.AddListener(Hide);
            UI.GeneralTabButton.onClick.AddListener(() => ShowPage(Page.General));
            UI.AudioTabButton.onClick.AddListener(() => ShowPage(Page.Audio));
            UI.ControlsTabButton.onClick.AddListener(() => ShowPage(Page.Controls));
            UI.NetworkTabButton.onClick.AddListener(() => ShowPage(Page.Network));

            general.SetUI(UI.SettingGeneralRoot.GetComponent<SettingGeneralPageUI>());
            audio.SetUI(UI.SettingAudioRoot.GetComponent<SettingAudioPageUI>());
            controls.SetUI(UI.SettingControlsRoot.GetComponent<SettingControlsPageUI>());
            network.SetUI(UI.SettingNetworkRoot.GetComponent<SettingNetworkPageUI>());
            this.RegisterEvent<MobileInputModeChangedEvent>(_ =>
            {
                if (IsShow()) RefreshNavigation();
            }).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow() => RefreshNavigation();

        /// <summary>手机只在通用页提供布局按钮；模式切换时重绘当前页，避免保留桌面列表或空白内容。</summary>
        private void RefreshNavigation()
        {
            ApplyReleaseVisibility(UI);
            var mobile = this.GetSystem<MobileInputSystem>().IsMobileInputMode;
            UI.ControlsTabButton.gameObject.SetActive(!mobile);
            current?.Hide();
            current = null;
            ShowPage(lastPage == Page.Controls && mobile
                ? Page.General : lastPage);
        }

        protected override void OnHide()
        {
            current?.Hide();
            current = null;
        }

        protected override void OnUnload()
        {
            current = null;
        }

        private void ShowPage(Page page)
        {
            if (Boot.ClientReleasePolicy.OfflineSinglePlayer && page == Page.Network) page = Page.General;
            // 重复选择当前页不重新进入，保留未完成的按键录入或地址编辑。
            if (current != null && lastPage == page) return;
            current?.Hide();
            lastPage = page;
            current = page switch
            {
                Page.Audio => audio,
                Page.Controls => controls,
                Page.Network => network,
                _ => general
            };
            current.Show();
            SetTabState(UI.GeneralTabButton, page == Page.General);
            SetTabState(UI.AudioTabButton, page == Page.Audio);
            SetTabState(UI.ControlsTabButton, page == Page.Controls);
            SetTabState(UI.NetworkTabButton, page == Page.Network);
        }

        /// <summary>离线首发不展示服务器和诊断页；旧的静态分页选择由 ShowPage 回到通用页。</summary>
        internal static void ApplyReleaseVisibility(SettingUI view)
        {
            bool online = !Boot.ClientReleasePolicy.OfflineSinglePlayer;
            view.NetworkTabButton.gameObject.SetActive(online);
            if (!online) view.SettingNetworkRoot.SetActive(false);
        }

        /// <summary>选中分页保持可操作，以Prefab配置的强调色显示，避免把当前页表现成禁用项。</summary>
        private static void SetTabState(Button button, bool selected)
        {
            button.interactable = true;
            button.image.color = selected ? button.colors.selectedColor : button.colors.normalColor;
            button.GetComponentInChildren<TMPro.TMP_Text>().color = selected
                ? new UnityEngine.Color32(18, 52, 60, 255)
                : new UnityEngine.Color32(243, 235, 216, 255);
        }

        internal static bool ShouldShowDesktopDisplaySettings(UnityEngine.RuntimePlatform platform)
        {
            return SettingGeneralPageCtrl.ShouldShowDesktopDisplaySettings(platform);
        }
    }
}
