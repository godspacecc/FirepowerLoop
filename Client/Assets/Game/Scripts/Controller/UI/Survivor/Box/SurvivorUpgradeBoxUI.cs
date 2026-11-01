using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class SurvivorUpgradeBoxUI : BaseBoxUI
    {
        public SurvivorUpgradeShape Accent;
        public Button BtnRefresh;
        public SurvivorUpgradeCardFeedback BtnSelect;
        public TextMeshProUGUI CategoryText;
        public TextMeshProUGUI DetailText;
        public GameObject FireBadge;
        public GameObject FrostBadge;
        public TextMeshProUGUI LevelText;
        public GameObject PierceBadge;
        public TextMeshProUGUI RefreshCountText;
        public TextMeshProUGUI TitleText;
        public Image UpgradeIcon;
    }
}
