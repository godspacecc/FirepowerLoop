using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class HeldItemPickerUI : BaseUI
    {
        public Button BtnClose;
        public Button BtnHolster;
        public CustomListView CandidateList;
        public GameObject EmptyRoot;
        public TextMeshProUGUI HintText;
    }
}
