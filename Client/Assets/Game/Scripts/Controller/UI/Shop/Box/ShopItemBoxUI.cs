using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class ShopItemBoxUI : BaseBoxUI
    {
        public Button BtnSelect;
        public GameObject DisabledRoot;
        public Image Icon;
        public GameObject SelectedRoot;
        public TextMeshProUGUI TextName;
        public TextMeshProUGUI TextOwned;
        public TextMeshProUGUI TextPrice;
    }
}
