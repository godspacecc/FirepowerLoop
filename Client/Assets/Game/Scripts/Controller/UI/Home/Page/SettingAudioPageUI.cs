using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class SettingAudioPageUI : BaseUI
    {
        public Slider MasterVolumeSlider;
        public TextMeshProUGUI MasterVolumeValue;
        public Slider MusicVolumeSlider;
        public TextMeshProUGUI MusicVolumeValue;
        public RectTransform MuteThumb;
        public Toggle MuteToggle;
        public Image MuteTrack;
        public Button ResetButton;
        public Slider SoundVolumeSlider;
        public TextMeshProUGUI SoundVolumeValue;
        public Button TestSoundButton;
    }
}
