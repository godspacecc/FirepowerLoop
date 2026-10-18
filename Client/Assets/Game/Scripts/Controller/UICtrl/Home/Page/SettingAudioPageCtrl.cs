using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>管理音频设置；滑动与静音切换即时生效并自动保存。</summary>
    public sealed class SettingAudioPageCtrl : BasePageCtrl
    {
        /// <summary>从模型刷新控件时阻止滑条回调重复保存。</summary>
        private bool refreshing;
        private SettingAudioPageUI UI => BaseUI as SettingAudioPageUI;

        protected override void OnInit()
        {
            UI.MasterVolumeSlider.onValueChanged.AddListener(v => Change(v, 0));
            UI.MusicVolumeSlider.onValueChanged.AddListener(v => Change(v, 1));
            UI.SoundVolumeSlider.onValueChanged.AddListener(v => Change(v, 2));
            UI.MuteToggle.onValueChanged.AddListener(ChangeMute);
            UI.TestSoundButton.onClick.AddListener(() => this.GetSystem<AudioSystem>().PlayUiSound("FoundationClick"));
            UI.ResetButton.onClick.AddListener(ResetPage);
        }
        protected override void OnShow() => Refresh();
        protected override void OnHide() { }

        private void Change(float value, int channel)
        {
            if (refreshing) return;
            var model = this.GetModel<AudioSettingsModel>();
            if (channel == 0) model.MasterVolume.Value = Mathf.Clamp01(value);
            else if (channel == 1) model.MusicVolume.Value = Mathf.Clamp01(value);
            else model.SoundVolume.Value = Mathf.Clamp01(value);
            Apply();
            RefreshValues();
        }
        private void ChangeMute(bool value) { if (refreshing) return; this.GetModel<AudioSettingsModel>().Mute.Value = value; Apply(); RefreshValues(); }
        private void Apply() { this.GetSystem<AudioSystem>().ApplyVolumes(); this.GetSystem<SettingsSystem>().MarkDirty(); }
        private void Refresh()
        {
            refreshing = true;
            var model = this.GetModel<AudioSettingsModel>();
            UI.MasterVolumeSlider.value = model.MasterVolume.Value;
            UI.MusicVolumeSlider.value = model.MusicVolume.Value;
            UI.SoundVolumeSlider.value = model.SoundVolume.Value;
            UI.MuteToggle.isOn = model.Mute.Value;
            refreshing = false;
            RefreshValues();
        }

        /// <summary>百分比和开关位置始终读取权威音频模型；这里只更新动态表现。</summary>
        private void RefreshValues()
        {
            var model = this.GetModel<AudioSettingsModel>();
            UI.MasterVolumeValue.text = Mathf.RoundToInt(model.MasterVolume.Value * 100) + "%";
            UI.MusicVolumeValue.text = Mathf.RoundToInt(model.MusicVolume.Value * 100) + "%";
            UI.SoundVolumeValue.text = Mathf.RoundToInt(model.SoundVolume.Value * 100) + "%";
            UI.MuteThumb.anchoredPosition = new Vector2(model.Mute.Value ? 23 : -23, 0);
            UI.MuteTrack.color = model.Mute.Value ? new Color32(244, 184, 75, 255) : new Color32(55, 97, 106, 255);
        }
        private void ResetPage()
        {
            if (!this.GetSystem<SettingsSystem>().ResetAudioToDefault()) Notify.ShowFloatTips(Texts.SettingsErrorSaveFailed);
            Refresh();
        }
    }
}
