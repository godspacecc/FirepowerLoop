using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class TestCmdUI : BaseUI
    {
        public Button BtnClose;
        public Button BtnExecute;
        public LoopVerticalScrollRect CategoryList;
        public LoopVerticalScrollRect EntryList;
        public TMP_InputField InputCommand;
        public RectTransform ResultContentRect;
        public ScrollRect ResultScrollRect;
        public RectTransform ResultViewportRect;
        public TextMeshProUGUI TextDetails;
        public TextMeshProUGUI TextExample;
        public TextMeshProUGUI TextResult;
        public TextMeshProUGUI TextUsage;
    }
}
