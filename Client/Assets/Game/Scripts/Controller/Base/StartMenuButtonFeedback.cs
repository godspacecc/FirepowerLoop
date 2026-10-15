using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game
{
    /// <summary>
    /// 开始页固定菜单的焦点表现组件，响应指针、键盘与显隐事件。
    /// 同组只展示一条横杠；不处理点击、音效或游戏状态，布局和引用由 Prefab 保存。
    /// </summary>
    [ExecuteAlways]
    public sealed class StartMenuButtonFeedback : UIBehaviour,
        IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        [SerializeField] private Button button;
        [SerializeField] private TMP_Text label;
        [SerializeField] private GameObject selectionMark;
        [SerializeField] private Color normalColor = new Color(0.94f, 0.91f, 0.84f, 1f);
        [SerializeField] private Color selectedColor = new Color(0.95f, 0.69f, 0.29f, 1f);

        /// <summary>仅属于本次可见周期；隐藏时清除，避免重开菜单沿用旧指针或焦点。</summary>
        private bool hovered;
        private bool focused;
        /// <summary>同组同步的输入方式；鼠标移出后禁止旧键盘焦点或默认首项抢回高亮。</summary>
        private bool pointerMode;

        protected override void OnEnable()
        {
            base.OnEnable();
            hovered = focused = pointerMode = false;
            RefreshGroup();
        }

        protected override void OnDisable()
        {
            hovered = focused = pointerMode = false;
            RefreshGroup();
            base.OnDisable();
        }

        protected override void OnCanvasGroupChanged()
        {
            base.OnCanvasGroupChanged();
            RefreshGroup();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            foreach (var item in Group())
            {
                item.pointerMode = true;
                item.hovered = item == this && CanHighlight;
            }
            RefreshGroup();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            foreach (var item in Group()) item.pointerMode = true;
            RefreshGroup();
        }

        public void OnSelect(BaseEventData eventData)
        {
            foreach (var item in Group())
            {
                item.pointerMode = eventData is PointerEventData;
                item.focused = item == this && CanHighlight;
            }
            RefreshGroup();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            focused = false;
            RefreshGroup();
        }

        private bool CanHighlight => isActiveAndEnabled && button != null && button.IsInteractable();

        private StartMenuButtonFeedback[] Group()
        {
            return transform.parent.GetComponentsInChildren<StartMenuButtonFeedback>(true);
        }

        /// <summary>事件驱动刷新：鼠标模式只显示悬停项，键盘模式显示焦点或初始首项。</summary>
        private void RefreshGroup()
        {
            // 组件初次挂载、Prefab 导入或销毁时可能尚未具备完整引用。
            if (transform.parent == null) return;
            var group = Group();
            StartMenuButtonFeedback first = null, hover = null, focus = null;
            bool pointerInput = false;
            foreach (var item in group)
            {
                pointerInput |= item.pointerMode;
                if (!item.CanHighlight) continue;
                if (first == null) first = item;
                if (item.hovered) hover = item;
                if (item.focused) focus = item;
            }
            var selected = hover ?? (pointerInput ? null : focus ?? first);
            foreach (var item in group)
            {
                bool active = item == selected;
                if (item.selectionMark != null) item.selectionMark.SetActive(active);
                if (item.label != null) item.label.color = active ? item.selectedColor : item.normalColor;
            }
        }
    }
}
