using System.Collections.Generic;
using System.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>呈现通用设置；手机提供语言、帧率与布局编辑入口，桌面提供语言与窗口参数，不持有权威设置。</summary>
    public sealed class SettingGeneralPageCtrl : BasePageCtrl
    {
        private enum Row { Language, Resolution, WindowMode, FrameRate, ControlLayout }
        private static readonly Row[] DesktopRows = { Row.Language, Row.Resolution, Row.WindowMode };
        private static readonly Row[] MobileRows = { Row.Language, Row.FrameRate, Row.ControlLayout };
        private IReadOnlyList<Row> rows = DesktopRows;
        private SettingGeneralPageUI UI => BaseUI as SettingGeneralPageUI;

        protected override void OnInit()
        {
            UI.SettingList.AddItemRefreshListener(OnRefresh);
            UI.ResetButton.onClick.AddListener(ResetPage);
            this.RegisterEvent<LanguageChangedEvent>(OnLanguageChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }
        /// <summary>安卓和编辑器手机模拟使用同一通用设置列表，操作按钮不作为独立分页。</summary>
        protected override void OnShow() { rows = this.GetSystem<MobileInputSystem>().IsMobileInputMode ? MobileRows : DesktopRows; Refresh(); }
        protected override void OnHide() { }

        private void Refresh() { UI.SettingList.totalCount = rows.Count; UI.SettingList.RefreshCells(); }

        private void OnRefresh(Transform transform, int index)
        {
            var row = transform.GetComponent<SettingItemBoxUI>();
            if (row == null || index < 0 || index >= rows.Count) return;
            row.BtnContext.onClick.RemoveAllListeners();
            row.BtnContext.interactable = true;
            var display = this.GetModel<DisplaySettingsModel>();
            switch (rows[index])
            {
                case Row.Language:
                    row.TextSetting.text = Texts.SettingsLanguage;
                    row.TextContext.text = this.GetModel<LocalizationModel>().GetCurrentLanguage().LocalizedLabel;
                    row.BtnContext.onClick.AddListener(SelectLanguage);
                    break;
                case Row.Resolution:
                    row.TextSetting.text = Texts.SettingsWindowSize;
                    if (display.CurrentWindowMode.Value == FullScreenMode.FullScreenWindow)
                    {
                        row.TextContext.text = Texts.SettingsFollowDesktop;
                        row.BtnContext.interactable = false;
                    }
                    else
                    {
                        row.TextContext.text = DisplayResolutionPresets.FindOrDefault(display.CurrentResolutionCode.Value).Label;
                        row.BtnContext.onClick.AddListener(SelectResolution);
                    }
                    break;
                case Row.FrameRate:
                    row.TextSetting.text = Loc.T("ui.settings.frame_rate");
                    row.TextContext.text = display.CurrentMobileFrameRate.Value + " FPS";
                    row.BtnContext.onClick.AddListener(SelectFrameRate);
                    break;
                case Row.ControlLayout:
                    row.TextSetting.text = Loc.T("ui.settings.category.controls");
                    row.TextContext.text = Loc.T("ui.mobile_layout.open");
                    row.BtnContext.onClick.AddListener(() => UIHelper.ShowUI<MobileControlLayoutUICtrl>());
                    break;
                default:
                    row.TextSetting.text = Texts.SettingsWindowMode;
                    row.TextContext.text = display.CurrentWindowMode.Value == FullScreenMode.FullScreenWindow ? Texts.SettingsFullscreenWindow : Texts.SettingsWindowed;
                    row.BtnContext.onClick.AddListener(SelectWindowMode);
                    break;
            }
        }

        private void SelectLanguage() => Notify.ShowMessage(Texts.SettingsSelectLanguageTitle, Texts.SettingsSelectLanguageContent,
            LocalizationLanguages.All.Select(x => new MessageButtonConfig { ButtonText = x.LocalizedLabel, OnClick = () => this.SendCommand(new SetLanguageCommand { LanguageCode = x.Code }) }).ToList(), closeOnBackground: true);

        /// <summary>打开两档选择弹窗；系统立即应用并保存偏好，刷新只读取模型。</summary>
        private void SelectFrameRate() => Notify.ShowMessage(Loc.T("ui.settings.select_frame_rate_title"), Loc.T("ui.settings.select_frame_rate_content"),
            new List<MessageButtonConfig>
            {
                new MessageButtonConfig { ButtonText = "30 FPS", OnClick = () => ApplyFrameRate(30) },
                new MessageButtonConfig { ButtonText = "60 FPS", OnClick = () => ApplyFrameRate(60) }
            }, closeOnBackground: true);

        private void ApplyFrameRate(int framesPerSecond)
        {
            this.GetSystem<SettingsSystem>().SetMobileFrameRate(framesPerSecond);
            Refresh();
        }

        private void SelectResolution() => Notify.ShowMessage(Texts.SettingsSelectWindowSizeTitle, Texts.SettingsSelectWindowSizeContent,
            DisplayResolutionPresets.All.Select(x => new MessageButtonConfig { ButtonText = x.Label, OnClick = () => ApplyDisplay(x.Code, this.GetModel<DisplaySettingsModel>().CurrentWindowMode.Value) }).ToList(), closeOnBackground: true);

        private void SelectWindowMode() => Notify.ShowMessage(Texts.SettingsSelectWindowModeTitle, Texts.SettingsSelectWindowModeContent,
            new List<MessageButtonConfig>
            {
                new MessageButtonConfig { ButtonText = Texts.SettingsWindowed, OnClick = () => ApplyDisplay(this.GetModel<DisplaySettingsModel>().CurrentResolutionCode.Value, FullScreenMode.Windowed) },
                new MessageButtonConfig { ButtonText = Texts.SettingsFullscreenWindow, OnClick = () => ApplyDisplay(this.GetModel<DisplaySettingsModel>().CurrentResolutionCode.Value, FullScreenMode.FullScreenWindow) }
            }, closeOnBackground: true);

        private void ApplyDisplay(string resolution, FullScreenMode mode)
        {
            this.SendCommand(new ApplyDisplaySettingsCommand { ResolutionCode = resolution, WindowMode = mode, Commit = true });
            Refresh();
        }

        private void ResetPage()
        {
            if (!this.GetSystem<SettingsSystem>().ResetGeneralToDefault()) Notify.ShowFloatTips(Texts.SettingsErrorSaveFailed);
            Refresh();
        }

        private void OnLanguageChanged(LanguageChangedEvent _)
        {
            Refresh();
        }

        internal static bool ShouldShowDesktopDisplaySettings(RuntimePlatform platform) => platform != RuntimePlatform.Android;
    }
}
