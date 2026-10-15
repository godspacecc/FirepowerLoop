using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class CraftUI : BaseUI
    {
        public Button BtnBatchMinus;
        public Button BtnBatchPlus;
        public Button BtnClose;
        public Button BtnCraft;
        public GridLayoutGroup CategoryGrid;
        public CustomListView CategoryList;
        public Image DetailIcon;
        public CustomListView MaterialList;
        public GameObject ReasonRoot;
        public CustomListView RecipeList;
        public TextMeshProUGUI TextBatchCount;
        public TextMeshProUGUI TextDetailDesc;
        public TextMeshProUGUI TextDetailName;
        public TextMeshProUGUI TextOutputTotal;
        public TextMeshProUGUI TextReason;
        public TextMeshProUGUI TextSingleOutput;
    }
}
