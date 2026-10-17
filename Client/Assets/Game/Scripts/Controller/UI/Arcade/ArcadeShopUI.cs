using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class ArcadeShopUI : BaseUI
    {
        public Button BtnBuy;
        public Button BtnCloseMenu;
        public Button BtnEnter;
        public Button BtnFinish;
        public Button BtnReroll;
        public Button BtnRetry;
        public Button BtnSkip;
        public GameObject ChoiceRoot;
        public GameObject LoadingRoot;
        public CustomListView OfferList;
        public GameObject ShoppingRoot;
        public TextMeshProUGUI TextCash;
        public TextMeshProUGUI TextDeadline;
        public TextMeshProUGUI TextDetailEffect;
        public TextMeshProUGUI TextDetailName;
        public TextMeshProUGUI TextMembers;
        public TextMeshProUGUI TextMessage;
        public TextMeshProUGUI TextTokens;
    }
}
