using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class MapSelectionUI : BaseUI
    {
        public Button BtnClose;
        public CustomListView MapList;
        public ScrollRect MapScroll;
        public Image Preview;
        public TextMeshProUGUI SelectedName;
        public TextMeshProUGUI EraText;
        public TextMeshProUGUI StoryText;
        public TextMeshProUGUI TechnologyText;
        public GameObject TechRoot;
        public TextMeshProUGUI ResourceText;
        public Button BtnAction;
        public TextMeshProUGUI ActionText;
        public GameObject PreparationRoot;
        public TextMeshProUGUI CarryWeightText;
        public TextMeshProUGUI CarryStateText;
        public CustomListView ForbiddenList;
        public ScrollRect ForbiddenScroll;
        public Button BtnAdjustSupplies;
    }
}
