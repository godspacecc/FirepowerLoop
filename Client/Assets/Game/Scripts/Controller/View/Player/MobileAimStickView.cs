using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>固定方向摇杆的触点和回弹表现；提交屏幕二维方向，不控制镜头、不触发攻击。</summary>
    public sealed class MobileAimStickView : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform Handle;
        /// <summary>摇杆最大行程，单位为本地 Canvas；死区占行程的 15%。</summary>
        public float Travel = 75f;
        /// <summary>只接受当前方向手指的拖动和抬起；不抢占开火、装填触点。</summary>
        private int? pointerId;
        public event Action<Vector2> DirectionChanged;

        public void OnPointerDown(PointerEventData data)
        {
            if (data == null || pointerId.HasValue || !isActiveAndEnabled) return;
            pointerId = data.pointerId;
            OnDrag(data);
        }

        /// <summary>方向以固定中心计算，三个手指互不抢占；死区内仅停止更新朝向。</summary>
        public void OnDrag(PointerEventData data)
        {
            if (data == null || pointerId != data.pointerId) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform,
                    data.position, data.pressEventCamera, out var local)) return;
            var value = Vector2.ClampMagnitude(local / Mathf.Max(Travel, 1f), 1f);
            Handle.anchoredPosition = value * Travel;
            DirectionChanged?.Invoke(value.sqrMagnitude < .15f * .15f ? Vector2.zero : value);
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (data != null && pointerId == data.pointerId) ResetCapture();
        }

        /// <summary>清空原始输入与表现；最后人物朝向属于输入 System，不随回弹清空。</summary>
        public void ResetCapture()
        {
            pointerId = null;
            if (Handle != null) Handle.anchoredPosition = Vector2.zero;
            DirectionChanged?.Invoke(Vector2.zero);
        }
        private void OnEnable()
        {
            LifeCycle.RegisterApplicationFocus(OnFocus);
            LifeCycle.RegisterApplicationPause(OnPause);
        }
        private void OnDisable()
        {
            LifeCycle.UnregisterApplicationFocus(OnFocus);
            LifeCycle.UnregisterApplicationPause(OnPause);
            ResetCapture();
        }
        private void OnFocus(bool focused) { if (!focused) ResetCapture(); }
        private void OnPause(bool paused) { if (paused) ResetCapture(); }
    }
}
