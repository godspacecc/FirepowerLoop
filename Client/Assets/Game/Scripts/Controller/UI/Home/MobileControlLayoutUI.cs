using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class MobileControlLayoutUI : BaseUI
    {
        public RectTransform LayoutArea;
        public Button MoveButton;
        public Button AimButton;
        public Button TrialButton;
        public TextMeshProUGUI TrialText;
        public Slider SizeSlider;
        public TextMeshProUGUI SizeValue;
        public Slider OpacitySlider;
        public TextMeshProUGUI OpacityValue;
        public TextMeshProUGUI HintText;
        public Button ResetButton;
        public Button SaveButton;
        public Button CancelButton;
        public RectTransform DashArea;
        public RectTransform BombArea;
        public MobileLayoutStickHandle MovePreview;
        public CanvasGroup MovePreviewOpacity;
        public MobileLayoutStickHandle AimPreview;
        public CanvasGroup AimPreviewOpacity;
        public MobileLayoutStickHandle PausePreview;
        public MobileLayoutStickHandle InventoryPreview;
        public MobileLayoutStickHandle BuildPreview;
        public MobileLayoutStickHandle AttackPreview;
        public MobileLayoutStickHandle ReloadPreview;
        public Button ModeButton;
        public TextMeshProUGUI ModeText;
        public MobileLayoutStickHandle StatusPreview;
        public MobileLayoutStickHandle HealthPreview;
        public MobileLayoutStickHandle WeaponPreview;
    }
}
