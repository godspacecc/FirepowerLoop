using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.InputSystem.OnScreen;

namespace Game
{
    public class ArcadeHudUI : BaseUI
    {
        public OnScreenStick AimJoystick;
        public GameObject AimJoystickRoot;
        public CanvasGroup AimOpacity;
        public Button BtnBomb;
        public Button BtnDash;
        public Button BtnExitFirstPerson;
        public Button BtnPause;
        public Button BtnRetry;
        public GameObject FirstPersonRoot;
        public RectTransform JoystickLayoutArea;
        public OnScreenStick MoveJoystick;
        public GameObject MoveJoystickRoot;
        public CanvasGroup MoveOpacity;
        public CustomListView PlayerStatusList;
        public CanvasGroup StageBannerOpacity;
        public GameObject StageBannerRoot;
        public GameObject TemporaryStatusRoot;
        public TextMeshProUGUI TextArena;
        public TextMeshProUGUI TextFirstPerson;
        public TextMeshProUGUI TextRespawn;
        public TextMeshProUGUI TextStageBanner;
        public TextMeshProUGUI TextSupport;
        public TextMeshProUGUI TextTemporary;
        public TextMeshProUGUI TextWave;
    }
}
