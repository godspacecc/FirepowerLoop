using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class DialogueUI : BaseUI
    {
        public TextMeshProUGUI NameText;
        public TextMeshProUGUI BodyText;
        public CustomListView ChoiceList;
        public Button BtnNext;
        public TextMeshProUGUI NextText;
        public Button BtnClose;
    }
}
