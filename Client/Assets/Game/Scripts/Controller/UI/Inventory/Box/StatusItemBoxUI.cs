using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class StatusItemBoxUI : BaseBoxUI
    {
        public Button ClickButton;
        public Image Icon;
        public TextMeshProUGUI NameText;
        public TextMeshProUGUI ValueText;
        public TextMeshProUGUI StateText;
        public GameObject MeterRoot;
        public Image MeterFill;
        public GameObject Selected;
        public Sprite HealthSprite;
        public Sprite PowerSprite;
        public Sprite WaterSprite;
        public Sprite TemperatureSprite;
        public Sprite StaminaSprite;
    }
}
