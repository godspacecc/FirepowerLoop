using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class StatusManagementPageUI : BaseUI
    {
        public CustomListView StatusList;
        public CustomListView AfflictionList;
        public TextMeshProUGUI DetailNameText;
        public TextMeshProUGUI DetailValueText;
        public TextMeshProUGUI DetailStateText;
        public TextMeshProUGUI DetailDescriptionText;
        public Button HealthButton;
        public TextMeshProUGUI HealthValueText;
        public Image HealthFill;
        public GameObject PreparationRoot;
        public TextMeshProUGUI BaseDateText;
        public TextMeshProUGUI BaseGoldText;
        public TextMeshProUGUI RestInfoText;
        public Button BtnRest;
    }
}
