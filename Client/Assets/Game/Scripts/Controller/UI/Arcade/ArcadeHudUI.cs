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
        public Button BtnBomb;
        public Button BtnDash;
        public Button BtnPause;
        public Button BtnRetry;
        public OnScreenStick MoveJoystick;
        public GameObject MoveJoystickRoot;
        public CustomListView PlayerStatusList;
        public GameObject TemporaryStatusRoot;
        public TextMeshProUGUI TextArena;
        public TextMeshProUGUI TextRespawn;
        public TextMeshProUGUI TextSupport;
        public TextMeshProUGUI TextTemporary;
        public TextMeshProUGUI TextWave;
        public RectTransform JoystickLayoutArea;
        public CanvasGroup MoveOpacity;
        public CanvasGroup AimOpacity;
        public TextMeshProUGUI TextFirstPerson;
        public Button BtnExitFirstPerson;
        public GameObject FirstPersonRoot;
    }
}
