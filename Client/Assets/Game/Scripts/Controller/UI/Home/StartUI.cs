using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class StartUI : BaseUI
    {
        public Button BtnCheckUpdate;
        public Button BtnContinue;
        public Button BtnExit;
        public Button BtnNewGame;
        public Button BtnSetting;
        public RectTransform ButtonGroup;
        public RedPointView UpdateRedPoint;
        public TextMeshProUGUI UpdateStatusText;
        public TextMeshProUGUI VersionText;
        public Button BtnShop;
        public GameObject MainMenuPanel;
        public Button BtnChallenge;
        public GameObject ArcadeModePanel;
        public Button BtnArcadeSolo;
        public Button BtnOnline;
        public Button BtnSurvivor;
        public Button BtnArcadeBack;
        public TextMeshProUGUI ArcadeSoloText;
        public TextMeshProUGUI CampaignResourceStatus;
    }
}
