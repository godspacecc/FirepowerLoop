using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class MapManagementPageUI : BaseUI
    {
        public TextMeshProUGUI RegionText;
        public TextMeshProUGUI ProgressText;
        public RectTransform MapContentRect;
        public RawImage MapImage;
        public RawImage FogImage;
        public CustomListView ExtractionList;
        public GameObject PlayerArrowRoot;
        public RectTransform PlayerArrowRect;
        public MapViewportScrollRect MapScroll;
        public TextMeshProUGUI MessageText;
        public Button ResetButton;
        public Button ExtractionFilterButton;
    }
}
