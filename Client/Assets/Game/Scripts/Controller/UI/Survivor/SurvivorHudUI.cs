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
        public TextMeshProUGUI ExperienceText;
        public Slider HealthBar;
        public TextMeshProUGUI HealthText;
        public RectTransform JoystickLayoutArea;
        public OnScreenStick MoveJoystick;
        public GameObject MoveJoystickRoot;
        public CanvasGroup MoveOpacity;
        public TextMeshProUGUI ScoreText;
        public TextMeshProUGUI TimeText;
    }
}
