using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class CraftRecipeBoxUI : BaseBoxUI
    {
        public Button BtnSelect;
        public GameObject DisabledRoot;
        public Image Icon;
        public GameObject SelectedRoot;
        public TextMeshProUGUI TextName;
        public TextMeshProUGUI TextOutputAmount;
        public TextMeshProUGUI TextReason;
    }
}
