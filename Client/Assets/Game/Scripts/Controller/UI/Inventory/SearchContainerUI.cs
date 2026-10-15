using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    public class SearchContainerUI : BaseUI
    {
        public Image BackgroundImage;
        public RectTransform SearchRootRect;
        public ScrollRect SearchScrollRect;
        public LoopGridLayoutGroup SearchList;
        public RectTransform SearchViewport;
        public Button BtnClose;
    }
}
