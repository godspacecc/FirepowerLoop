using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class TasksManagementPageUI : BaseUI
    {
        public Button ActiveButton;
        public GameObject ActiveSelected;
        public Button CompletedButton;
        public GameObject CompletedSelected;
        public GameObject DetailRoot;
        public TextMeshProUGUI DetailTitleText;
        public TextMeshProUGUI DetailStatusText;
        public ScrollRect DetailScroll;
        public TextMeshProUGUI DetailBodyText;
        public CustomListView TaskList;
        public GameObject EmptyRoot;
        public TextMeshProUGUI EmptyTitleText;
    }
}
