using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class EquipmentManagementPageUI : BaseUI
    {
        public Button BtnAccessory;
        public Button BtnBack;
        public Button BtnFeet;
        public Button BtnHands;
        public Button BtnHead;
        public Button BtnLegs;
        public Button BtnTorso;
        public TextMeshProUGUI DetailDescriptionText;
        public Image DetailIcon;
        public TextMeshProUGUI DetailNameText;
        public TextMeshProUGUI DetailSourceText;
        public TextMeshProUGUI DetailStatsText;
        public Button BtnUnequip;
        public RawImage CharacterPreview;
        public GameObject CharacterPreviewPrefab;
    }
}
