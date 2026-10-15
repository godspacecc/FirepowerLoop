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
        public RectTransform ButtonGroup;
        public RedPointView UpdateRedPoint;
        public TextMeshProUGUI UpdateStatusText;
        public TextMeshProUGUI VersionText;
    }
}
