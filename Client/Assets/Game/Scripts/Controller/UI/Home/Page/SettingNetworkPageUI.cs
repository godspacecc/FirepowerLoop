using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class SettingNetworkPageUI : BaseUI
    {
        public TMP_InputField GameServerInput;
        public Button GameServerApplyButton;
        public Button GameServerResetButton;
        public TextMeshProUGUI GameServerFeedback;
        public TMP_InputField ResourceServerInput;
        public Button ResourceServerApplyButton;
        public Button ResourceServerResetButton;
        public TextMeshProUGUI ResourceServerFeedback;
        public TextMeshProUGUI DiagnosticNotice;
        public TextMeshProUGUI DiagnosticPending;
        public Button DiagnosticClearButton;
        public Button DiagnosticUploadButton;
        public TextMeshProUGUI DiagnosticStatus;
    }
}
