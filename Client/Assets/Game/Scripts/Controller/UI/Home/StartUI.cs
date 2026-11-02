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
        public Button BtnOnline;
        public Button BtnSetting;
        public Button BtnSurvivor;
        /// <summary>开始菜单中的单人任务入口，沿用正式菜单布局。</summary>
        public Button BtnMission;
        public RectTransform ButtonGroup;
        public RedPointView UpdateRedPoint;
        public TextMeshProUGUI UpdateStatusText;
        public TextMeshProUGUI VersionText;
        public Button BtnShop;
        public GameObject MainMenuPanel;
        public GameObject ArcadeModePanel;
        public Button BtnArcadeSolo;
        public Button BtnArcadeBack;
        public TextMeshProUGUI ArcadeSoloText;
    }
}
