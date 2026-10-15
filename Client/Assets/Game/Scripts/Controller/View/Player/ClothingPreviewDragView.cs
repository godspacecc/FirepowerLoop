using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Game
{
    /// <summary>将人物区域的拖动、滚轮和双指手势转换为观察意图；不持有装备状态或预览资源。</summary>
    public sealed class ClothingPreviewDragView : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        /// <summary>水平转身与观察俯仰的角度增量，单位为度。</summary>
        public event Action<Vector2> Orbit;
        /// <summary>正值拉近、负值拉远；由预览拥有者约束观察范围。</summary>
        public event Action<float> Zoom;
        private readonly Dictionary<int, Vector2> pointers = new Dictionary<int, Vector2>();
        private float pinchDistance;
        private bool firstPointerIsTouch;

        public void OnPointerDown(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || pointers.ContainsKey(data.pointerId)) return;
            // 鼠标不参与双指手势；第三指也不能夺取已开始的两指观察。
            var touch = data is ExtendedPointerEventData extended ? extended.pointerType == UIPointerType.Touch : data.pointerId >= 0;
            if (pointers.Count >= 2 || (pointers.Count > 0 && (!touch || !firstPointerIsTouch))) return;
            if (pointers.Count == 0) firstPointerIsTouch = touch;
            pointers.Add(data.pointerId, data.position);
            pinchDistance = Distance();
        }

        public void OnBeginDrag(PointerEventData data) => OnPointerDown(data);

        /// <summary>手势只接收从本区域按下的触点；双指期间只缩放，松指后不补算旧位移。</summary>
        public void OnDrag(PointerEventData data)
        {
            if (!pointers.ContainsKey(data.pointerId)) return;
            pointers[data.pointerId] = data.position;
            var rect = (RectTransform)transform;
            var canvas = GetComponentInParent<Canvas>();
            var scale = canvas != null ? canvas.scaleFactor : 1f;
            var width = rect.rect.width * scale;
            var height = rect.rect.height * scale;
            if (width <= 0f || height <= 0f) return;
            if (pointers.Count == 2)
            {
                var distance = Distance();
                if (pinchDistance > 0f) Zoom?.Invoke((distance - pinchDistance) / width);
                pinchDistance = distance;
                return;
            }
            Orbit?.Invoke(new Vector2(-data.delta.x / width * 180f, -data.delta.y / height * 90f));
        }

        public void OnScroll(PointerEventData data) => Zoom?.Invoke(data.scrollDelta.y / 120f * .15f);
        public void OnPointerUp(PointerEventData data)
        { pointers.Remove(data.pointerId); pinchDistance = 0f; if (pointers.Count == 0) firstPointerIsTouch = false; }
        public void OnEndDrag(PointerEventData data) => OnPointerUp(data);
        private float Distance()
        {
            if (pointers.Count != 2) return 0f;
            var values = pointers.Values.GetEnumerator(); values.MoveNext(); var first = values.Current;
            values.MoveNext(); return Vector2.Distance(first, values.Current);
        }
        private void OnDisable() { pointers.Clear(); pinchDistance = 0f; firstPointerIsTouch = false; }
    }
}
