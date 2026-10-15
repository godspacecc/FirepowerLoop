using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class TemporaryBuildingHudUI : BaseUI
    {
        public TextMeshProUGUI TitleText;
        public TextMeshProUGUI ModeText;
        public TextMeshProUGUI HintText;
        public TextMeshProUGUI ResourceText;
        public TextMeshProUGUI StatusText;
        public Slider ProgressSlider;
        public GameObject ProgressRoot;
        public GameObject TouchControlsRoot;
        public Button BtnRotate;
        public Button BtnConfirm;
        public Button BtnCancel;
        public MobileCombatPressView RotatePress;
    }
}
