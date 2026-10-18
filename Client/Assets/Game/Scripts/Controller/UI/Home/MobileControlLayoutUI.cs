using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class MobileControlLayoutUI : BaseUI
    {
        public Button AimButton;
        public MobileLayoutStickHandle AimPreview;
        public CanvasGroup AimPreviewOpacity;
        public MobileLayoutStickHandle BombPreview;
        public Button CancelButton;
        public Button CollapseToolbarButton;
        public RectTransform ControlSafeArea;
        public MobileLayoutStickHandle DashPreview;
        public MobileLayoutStickHandle DonateLifePreview;
        public MobileLayoutStickHandle ExitFirstPersonPreview;
        public Button ExpandToolbarButton;
        public MobileLayoutStickHandle FieldUpgradePreview;
        public MobileLayoutStickHandle FirstPersonFirePreview;
        public TextMeshProUGUI HintText;
        public RectTransform LayoutArea;
        public Button ModeButton;
        public TextMeshProUGUI ModeText;
        public Button MoveButton;
        public MobileLayoutStickHandle MovePreview;
        public CanvasGroup MovePreviewOpacity;
        public Slider OpacitySlider;
        public TextMeshProUGUI OpacityValue;
        public MobileLayoutStickHandle PausePreview;
        public RectTransform PreviewViewport;
        public Button ResetButton;
        public Button SaveButton;
        public Slider SizeSlider;
        public TextMeshProUGUI SizeValue;
        public GameObject ToolbarRoot;
        public Button TrialButton;
        public TextMeshProUGUI TrialText;
    }
}
