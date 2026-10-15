using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class JournalManagementPageUI : BaseUI
    {
        public Button DailyButton;
        public GameObject DailySelected;
        public Button NotesButton;
        public GameObject NotesSelected;
        public Button CollectionsButton;
        public GameObject CollectionsSelected;
        public GameObject DetailRoot;
        public TextMeshProUGUI DetailTitleText;
        public ScrollRect DetailScroll;
        public TextMeshProUGUI DetailBodyText;
    }
}
