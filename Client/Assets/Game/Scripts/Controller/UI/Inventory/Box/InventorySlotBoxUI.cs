using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class InventorySlotBoxUI : BaseBoxUI
    {
        public TextMeshProUGUI AmountText;
        public Button ClickButton;
        public GameObject DisabledOverlay;
        public GameObject DragFeedback;
        public GameObject EmptyIconRoot;
        public Button ExpansionButton;
        public GameObject ExpansionContentRoot;
        public TextMeshProUGUI ExpansionLabelText;
        public Image IconImage;
        public GameObject ItemContentRoot;
        public GameObject SelectedFrame;
    }
}
