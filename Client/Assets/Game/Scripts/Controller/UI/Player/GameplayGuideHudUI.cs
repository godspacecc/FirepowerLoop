using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class GameplayGuideHudUI : BaseUI
    {
        public GameObject GuideRoot;
        public TextMeshProUGUI GuideText;
        public TextMeshProUGUI VersionText;
        public Button BtnPause;
        public Button MobileBtnPause;
        public RectTransform MobilePauseArea;
        public GameObject InteractionRoot;
        public TextMeshProUGUI InteractionText;
    }
}
