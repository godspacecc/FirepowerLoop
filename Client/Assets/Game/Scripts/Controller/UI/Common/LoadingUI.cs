using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Game
{
    public class LoadingUI : BaseUI
    {
        public Button BtnBack;
        public GameObject ErrorRoot;
        public TextMeshProUGUI ErrorText;
        public TextMeshProUGUI MessageText;
        public TextMeshProUGUI PercentText;
        public Slider ProgressSlider;
        public GameObject BaseArtwork;
        public GameObject MapArtwork;
        public GameObject BaseTitle;
        public GameObject MapTitle;
        public GameObject MapRoot;
        public GameObject DownloadRoot;
        public TextMeshProUGUI DownloadPercentText;
        public TextMeshProUGUI DownloadMessageText;
        public Slider DownloadSlider;
        public Button BtnDownloadClose;
        public Button BtnCopyLog;
        public TextMeshProUGUI CopyStatusText;
    }
}
