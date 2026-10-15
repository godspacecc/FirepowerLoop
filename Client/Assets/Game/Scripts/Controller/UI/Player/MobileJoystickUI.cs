using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem.OnScreen;

namespace Game
{
    public class MobileJoystickUI : BaseUI
    {
        public GameObject ContentRoot;
        public CanvasGroup ContentVisibility;
        public Button InteractionButton;
        public OnScreenStick JoystickSurface;
        public Button AttackButton;
        public GameObject ReticleRoot;
        public Button InventoryButton;
        public GameObject MoveJoystickRoot;
        public CanvasGroup MoveOpacity;
        public RectTransform JoystickLayoutArea;
        public Button BuildButton;
        public GameObject BaseCombatRoot;
        public GameObject BaseAimRoot;
        public CanvasGroup BaseAimOpacity;
        public MobileAimStickView BaseAimSurface;
        public MobileCombatPressView BaseFirePress;
        public MobileCombatPressView BaseReloadPress;
        public Image BaseReloadProgress;
        public Button SprintButton;
        public Image SprintIcon;
        public CustomListView PocketList;
        public CanvasGroup PocketInputGroup;
        public Button PocketToggleButton;
        public GameObject PocketExpandedMark;
        public GameObject PocketPanel;
        public Button PocketCloseButton;
        public ScrollRect PocketScroll;
    }
}
