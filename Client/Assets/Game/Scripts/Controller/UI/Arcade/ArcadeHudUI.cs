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
        public Sprite BootsIcon;
        public Button BtnBomb;
        public Button BtnDash;
        public Button BtnExitFirstPerson;
        public Button BtnFieldUpgrade;
        public Button BtnPause;
        public Button BtnRetry;
        public Button BtnSlayerMart;
        public GameObject DesktopAbilityRoot;
        public Image DesktopBombIcon;
        public TextMeshProUGUI DesktopDashIcon;
        public CustomListView EffectList;
        public GameObject FirstPersonRoot;
        public Sprite GlovesIcon;
        public Sprite GrenadeIcon;
        public RectTransform JoystickLayoutArea;
        public OnScreenStick MoveJoystick;
        public GameObject MoveJoystickRoot;
        public CanvasGroup MoveOpacity;
        public Sprite PiercingIcon;
        public CustomListView PlayerStatusList;
        public Sprite RapidIcon;
        public Sprite RaygunIcon;
        public Sprite RocketIcon;
        public Sprite SpreadIcon;
        public CanvasGroup StageBannerOpacity;
        public GameObject StageBannerRoot;
        public GameObject SupportEffectRoot;
        public TextMeshProUGUI TextArena;
        public TextMeshProUGUI TextArmor;
        public TextMeshProUGUI TextBombCount;
        public TextMeshProUGUI TextDashCount;
        public TextMeshProUGUI TextFirstPerson;
        public TextMeshProUGUI TextRespawn;
        public TextMeshProUGUI TextStageBanner;
        public TextMeshProUGUI TextStormCharge;
        public TextMeshProUGUI TextWave;
        public CustomListView WeaponEffectList;
        public GameObject WeaponEffectRoot;
    }
}
