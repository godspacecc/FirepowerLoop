using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class ShopUI : BaseUI
    {
        public Button BtnBuyTab;
        public Button BtnClose;
        public Button BtnMinus;
        public Button BtnPlus;
        public Button BtnSellTab;
        public Button BtnTrade;
        public Image DetailIcon;
        public GameObject DetailRoot;
        public CustomListView ItemList;
        public TextMeshProUGUI TextDetailDesc;
        public TextMeshProUGUI TextDetailName;
        public TextMeshProUGUI TextGold;
        public TextMeshProUGUI TextOwned;
        public TextMeshProUGUI TextQuantity;
        public TextMeshProUGUI TextReason;
        public TextMeshProUGUI TextShopName;
        public TextMeshProUGUI TextTotal;
        public TextMeshProUGUI TextUnitPrice;
    }
}
