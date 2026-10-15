using System;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game
{
    /// <summary>微调按钮的单指按压端口；不持有姿态和计时规则，普通提交仍走 Button.onClick。</summary>
    public sealed class AppearanceGalleryAdjustButton : Button
    {
        public event Action PressStarted;
        public event Action PressEnded;
        // 首个手指独占本次按压；第二个触点不能结束或重启旧操作。
        private int pointerId;
        private PointerEventData ownedPointer;
        public new bool IsPressed { get; private set; }

        public override void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || IsPressed || !IsActive() || !IsInteractable()) return;
            base.OnPointerDown(eventData);
            pointerId = eventData.pointerId; ownedPointer = eventData; IsPressed = true; PressStarted?.Invoke();
        }
        public override void OnPointerUp(PointerEventData eventData)
        {
            if (!IsPressed || eventData.pointerId != pointerId) return;
            base.OnPointerUp(eventData); CancelPress();
        }
        public override void OnPointerExit(PointerEventData eventData)
        {
            base.OnPointerExit(eventData);
            if (IsPressed && eventData.pointerId == pointerId) CancelPress();
        }
        // 按下已经调整一次，释放后的 PointerClick 不再追加；键盘 Submit 仍沿用父类。
        public override void OnPointerClick(PointerEventData eventData) { }
        /// <summary>页面退出或应用失焦时由拥有者主动取消；多次取消幂等。</summary>
        public void CancelPress()
        {
            if (!IsPressed) return;
            IsPressed = false;
            // 移出或程序取消也清理父类按下状态，避免再次进入按钮时遗留按压高亮。
            base.OnPointerUp(ownedPointer); ownedPointer = null; PressEnded?.Invoke();
        }
        protected override void OnDisable() { CancelPress(); base.OnDisable(); }
    }
}
