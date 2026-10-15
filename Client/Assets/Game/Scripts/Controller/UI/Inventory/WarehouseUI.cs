using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class WarehouseUI : BaseUI
    {
        public Image BackgroundImage;
        public Button BtnClose;
        public CustomListView WarehouseList;
        public ScrollRect WarehouseScrollRect;
        public TextMeshProUGUI CapacityText;
        public GameObject DetailRoot;
        public Image DetailIcon;
        public TextMeshProUGUI DetailNameText;
        public TextMeshProUGUI DetailDescText;
        public TextMeshProUGUI DetailAmountText;
        public Button BtnTake;
        public Button BtnExpand;
        public TextMeshProUGUI ExpansionCostText;
    }
}
