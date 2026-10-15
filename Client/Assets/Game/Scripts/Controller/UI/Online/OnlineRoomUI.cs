using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class OnlineRoomUI : BaseUI
    {
        public Button ChatCloseButton;
        public TMP_InputField ChatInput;
        public LoopVerticalScrollRect ChatMessageList;
        public Button ChatOpenButton;
        public GameObject ChatPanelRoot;
        public GameObject ChatPreviewRoot;
        public TextMeshProUGUI ChatPreviewText;
        public Button ChatSendButton;
        public TMP_Text ChatStateText;
        public TextMeshProUGUI ChatUnreadText;
        public Button CloseButton;
        public Button CreateButton;
        public TMP_Text CurrentRoomCodeText;
        public GameObject DisconnectHintRoot;
        public GameObject EmptyRoot;
        public Button JoinButton;
        public GameObject JoinedRoot;
        public Button LeaveButton;
        public Button LoadMoreButton;
        public CustomListView MemberList;
        public LoopVerticalScrollRect PublicRoomList;
        public TextMeshProUGUI PublicRoomListStateText;
        public Button RefreshRoomListButton;
        public TMP_InputField RoomCodeInput;
        public RectTransform RootPanel;
        public Button WorldButton;
    }
}
