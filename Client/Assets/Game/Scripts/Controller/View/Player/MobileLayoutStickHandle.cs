using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>布局编辑页的触摸适配器；编辑时上报坐标，试用时仅移动预览，不写虚拟 Gamepad。</summary>
    public sealed class MobileLayoutStickHandle : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        /// <summary>摇杆试用的必需引用；按钮位置预览明确留空，试用时不产生按钮动作。</summary>
        public RectTransform Knob;
        public bool Trial { get; set; }
        public Action Selected;
        public Action<Vector2> Dragged;
        /// <summary>一次只捕获一个触点；松开、隐藏或切换试用后释放，避免旧手势影响新草稿。</summary>
        private int? pointerId;
        private Vector2 down;
        private Vector2 grabOffset;

        public void OnPointerDown(PointerEventData evt)
        {
            if (pointerId.HasValue) return;
            pointerId = evt.pointerId;
            Selected?.Invoke();
            grabOffset = RectTransformUtility.WorldToScreenPoint(evt.pressEventCamera, transform.position) - evt.position;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform,
                evt.position, evt.pressEventCamera, out down);
        }

        public void OnDrag(PointerEventData evt)
        {
            if (pointerId != evt.pointerId) return;
            if (Trial)
            {
                if (Knob == null) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform,
                    evt.position, evt.pressEventCamera, out var current);
                Knob.anchoredPosition = Vector2.ClampMagnitude(current - down, MobileControlLayoutGeometry.Travel);
            }
            else Dragged?.Invoke(evt.position + grabOffset);
        }

        public void OnPointerUp(PointerEventData evt) { if (pointerId == evt.pointerId) ResetGesture(); }
        public void ResetGesture() { pointerId = null; if (Knob != null) Knob.anchoredPosition = Vector2.zero; }
        private void OnDisable() => ResetGesture();
    }
}
