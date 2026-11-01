using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.InputSystem.OnScreen;

namespace Game
{
    public class SurvivorHudUI : BaseUI
    {
        public Button BtnPause;
        public Slider ExperienceBar;
        public Slider HealthBar;
        public TextMeshProUGUI HealthText;
        public RectTransform JoystickLayoutArea;
        public OnScreenStick MoveJoystick;
        public GameObject MoveJoystickRoot;
        public CanvasGroup MoveOpacity;
        public TextMeshProUGUI ScoreText;
        public TextMeshProUGUI TimeText;
        /// <summary>设备本地时间，与本局生存计时分开显示。</summary>
        public TextMeshProUGUI ClockText;
        public CustomListView WeaponSlots;
    }
}
