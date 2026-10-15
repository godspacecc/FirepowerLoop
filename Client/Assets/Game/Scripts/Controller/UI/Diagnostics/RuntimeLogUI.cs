using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class RuntimeLogUI : BaseUI
    {
        public Button BtnAll;
        public Button BtnNormal;
        public Button BtnWarning;
        public Button BtnError;
        public LoopVerticalScrollRect LogList;
        public ScrollRect DetailScrollRect;
        public TMP_Text DetailText;
        public Button BtnClose;
        public Button BtnClear;
        public Button BtnCopy;
        public Button BtnExport;
    }
}
