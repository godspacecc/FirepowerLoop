using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game
{
    /// <summary>地图视口的拖动和缩放输入；只提交缩放意图，不加载地图、不修改探索或玩家状态。</summary>
    public sealed class MapViewportScrollRect : ScrollRect
    {
        public event Action<float, Vector2> ZoomRequested;
        private readonly List<RaycastResult> hits = new List<RaycastResult>();
        /// <summary>两指身份与上次像素距离仅在一次捏合中有效，换指、隐藏和释放时清空。</summary>
        private int firstTouchId = -1, secondTouchId = -1;
        private float pinchDistance;
        private bool resumeDrag;

        /// <summary>滚轮围绕指针缩放，不再调用原生 ScrollRect 的滚动平移。</summary>
        public override void OnScroll(PointerEventData data)
        {
            if (!IsActive() || data.scrollDelta.y == 0) return;
            ZoomRequested?.Invoke(Mathf.Pow(1.2f, Mathf.Clamp(data.scrollDelta.y, -12f, 12f)), data.position);
        }

        /// <summary>由地图分页的共享生命周期轮询；必须两指都从当前可交互视口开始。</summary>
        public void PollPinch()
        {
            var screen = Touchscreen.current;
            if (!IsActive() || screen == null) { ResetGesture(); return; }
            UnityEngine.InputSystem.Controls.TouchControl first = null, second = null;
            foreach (var touch in screen.touches)
            {
                if (!touch.press.isPressed) continue;
                if (first == null) first = touch;
                else if (second == null) second = touch;
                else { ResetGesture(); return; }
            }
            if (second == null) { ResetGesture(); return; }
            var a = first.position.ReadValue(); var b = second.position.ReadValue();
            var distance = Vector2.Distance(a, b);
            var firstId = first.touchId.ReadValue(); var secondId = second.touchId.ReadValue();
            if (firstTouchId != firstId || secondTouchId != secondId)
            {
                ResetGesture();
                if (distance < 1 || !IsOverViewport(a) || !IsOverViewport(b)) return;
                firstTouchId = firstId; secondTouchId = secondId; pinchDistance = distance;
                StopMovement(); resumeDrag = true;
                return;
            }
            if (distance < 1) { ResetGesture(); return; }
            ZoomRequested?.Invoke(distance / pinchDistance, (a + b) * .5f);
            pinchDistance = distance;
        }

        private bool IsOverViewport(Vector2 position)
        {
            if (EventSystem.current == null) return false;
            var canvas = GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (!RectTransformUtility.RectangleContainsScreenPoint(viewport, position, camera)) return false;
            hits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
            // 最上层命中必须属于地图，弹窗与图标按钮上的两指不得穿透到地图。
            return hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(viewport);
        }

        public override void OnBeginDrag(PointerEventData data)
        {
            if (firstTouchId < 0) base.OnBeginDrag(data);
        }

        public override void OnDrag(PointerEventData data)
        {
            if (firstTouchId >= 0) return;
            // 捏合后重设原生拖动起点，避免沿用缩放前坐标导致地图突然跳动。
            if (resumeDrag) { base.OnBeginDrag(data); resumeDrag = false; }
            base.OnDrag(data);
        }

        public void ResetGesture()
        { firstTouchId = secondTouchId = -1; pinchDistance = 0; }

        protected override void OnDisable()
        { ResetGesture(); resumeDrag = false; base.OnDisable(); }
    }
}
