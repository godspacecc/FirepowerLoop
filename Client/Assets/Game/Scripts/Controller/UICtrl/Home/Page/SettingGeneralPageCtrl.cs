using System.Collections.Generic;
using System.Linq;
using QFramework;
using UnityEngine;
using UnityEngine.Events;

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
            row.BtnContext.gameObject.SetActive(false);
            // 复用行先关闭原生列表并清除旧回调，避免语言或设备模式切换后选择到旧选项。
            row.ValueDropdown.onValueChanged.RemoveAllListeners();
            row.ValueDropdown.gameObject.SetActive(false);
            var display = this.GetModel<DisplaySettingsModel>();
            switch (rows[index])
            {
                case Row.Language:
                    row.TextSetting.text = Texts.SettingsLanguage;
                    var languages = LocalizationLanguages.All.ToArray();
                    BindDropdown(row, languages.Select(language => language.LocalizedLabel).ToList(),
                        System.Array.FindIndex(languages, language => language.Code == this.GetModel<LocalizationModel>().CurrentLanguageCode.Value),
                        index => this.SendCommand(new SetLanguageCommand { LanguageCode = languages[index].Code }));
                    break;
                case Row.Resolution:
                    row.TextSetting.text = Texts.SettingsWindowSize;
                    if (display.CurrentWindowMode.Value == FullScreenMode.FullScreenWindow)
                    {
                        BindDropdown(row, new List<string> { Texts.SettingsFollowDesktop }, 0, null, false);
                    }
                    else
                    {
                        var resolutions = DisplayResolutionPresets.All.ToArray();
                        BindDropdown(row, resolutions.Select(resolution => resolution.Label).ToList(),
                            System.Array.FindIndex(resolutions, resolution => resolution.Code == display.CurrentResolutionCode.Value),
                            index => ApplyDisplay(resolutions[index].Code, this.GetModel<DisplaySettingsModel>().CurrentWindowMode.Value));
                    }
                    break;
                case Row.FrameRate:
                    row.TextSetting.text = Loc.T("ui.settings.frame_rate");
                    var frameRates = new[] { 30, 60 };
                    BindDropdown(row, frameRates.Select(rate => rate + " FPS").ToList(),
                        System.Array.IndexOf(frameRates, display.CurrentMobileFrameRate.Value),
                        index => ApplyFrameRate(frameRates[index]));
                    break;
                case Row.ControlLayout:
                    row.TextSetting.text = Loc.T("ui.settings.category.controls");
                    row.TextContext.text = Loc.T("ui.mobile_layout.open");
                    row.BtnContext.gameObject.SetActive(true);
                    row.BtnContext.onClick.AddListener(() => UIHelper.ShowUI<MobileControlLayoutUICtrl>());
                    break;
                default:
                    row.TextSetting.text = Texts.SettingsWindowMode;
                    var modes = new[] { FullScreenMode.Windowed, FullScreenMode.FullScreenWindow };
                    BindDropdown(row, new List<string> { Texts.SettingsWindowed, Texts.SettingsFullscreenWindow },
                        System.Array.IndexOf(modes, display.CurrentWindowMode.Value),
                        index => ApplyDisplay(this.GetModel<DisplaySettingsModel>().CurrentResolutionCode.Value, modes[index]));
                    break;
            }
        }

        /// <summary>选项和值来自当前模型快照；绑定时不发送设置修改，仅响应用户实际选择。</summary>
        private static void BindDropdown(SettingItemBoxUI row, List<string> options, int selected,
            UnityAction<int> changed, bool interactable = true)
        {
            var dropdown = row.ValueDropdown;
            dropdown.ClearOptions(); dropdown.AddOptions(options);
            dropdown.SetValueWithoutNotify(Mathf.Max(0, selected));
            dropdown.interactable = interactable;
            if (changed != null) dropdown.onValueChanged.AddListener(changed);
            dropdown.gameObject.SetActive(true);
            dropdown.RefreshShownValue();
        }

        private void ApplyFrameRate(int framesPerSecond)
        {
            this.GetSystem<SettingsSystem>().SetMobileFrameRate(framesPerSecond);
            Refresh();
        }

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
