using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class OnlineLoginUI : BaseUI
    {
        public GameObject AnonymousRoot;
        public TextMeshProUGUI AuthenticatedPlayerText;
        public GameObject AuthenticatedRoot;
        public TextMeshProUGUI AuthenticatedUserText;
        public Button CloseButton;
        public Button LoginButton;
        public Button LogoutButton;
        public TMP_InputField PasswordInput;
        public TextMeshProUGUI ResultText;
        public Button RoomButton;
        public RectTransform RootPanel;
        public TMP_InputField UserNameInput;
        public Button WorldButton;
        public TextMeshProUGUI WorldButtonText;
    }
}
