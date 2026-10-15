namespace Game
{
    /// <summary>手机操作页只提供布局编辑入口，PC 的热键页由设置窗口独立选择。</summary>
    public sealed class SettingMobilePageCtrl : BasePageCtrl
    {
        private SettingMobilePageUI UI => BaseUI as SettingMobilePageUI;

        protected override void OnInit()
        {
            UI.OpenLayoutButton.onClick.AddListener(() => UIHelper.ShowUI<MobileControlLayoutUICtrl>());
        }

        protected override void OnShow()
        {
        }

        protected override void OnHide()
        {
        }
    }
}
