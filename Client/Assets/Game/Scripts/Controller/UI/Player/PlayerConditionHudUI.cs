using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class PlayerConditionHudUI : BaseUI
    {
        public Image AmmoBackground;
        public Image AmmoFill;
        public GameObject ContentRoot;
        public Image HealthFill;
        public TextMeshProUGUI HealthIconText;
        public GameObject HealthRoot;
        public RectTransform HudLayoutArea;
        public GameObject MobileContentRoot;
        public Image MobileHealthFill;
        public TextMeshProUGUI MobileHealthIconText;
        public GameObject MobileHealthRoot;
        public Image MobilePowerFill;
        public Image MobilePowerIcon;
        public GameObject MobilePowerRoot;
        public GameObject MobileStatusRow;
        public Image MobileTemperatureBulb;
        public GameObject MobileTemperatureRoot;
        public Image MobileTemperatureStem;
        public Image MobileTiliFill;
        public Image MobileTiliIcon;
        public GameObject MobileTiliRoot;
        public Image MobileWaterFill;
        public Image MobileWaterIcon;
        public GameObject MobileWaterRoot;
        public Image PowerFill;
        public TextMeshProUGUI PowerIconText;
        public GameObject PowerRoot;
        public TextMeshProUGUI PowerValueText;
        public Image TemperatureFill;
        public TextMeshProUGUI TemperatureIconText;
        public GameObject TemperatureRoot;
        public TextMeshProUGUI TemperatureValueText;
        public Image TiliFill;
        public TextMeshProUGUI TiliIconText;
        public GameObject TiliRoot;
        public TextMeshProUGUI TiliValueText;
        public Image WaterFill;
        public TextMeshProUGUI WaterIconText;
        public GameObject WaterRoot;
        public TextMeshProUGUI WaterValueText;
        public Image WeaponIcon;
        public GameObject WeaponInfoRoot;
    }
}
