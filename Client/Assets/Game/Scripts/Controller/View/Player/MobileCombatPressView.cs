using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>独立按钮的原始按压适配；只捕获起始触点，不裁决武器、弹药或输入资格。</summary>
    public sealed class MobileCombatPressView : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        /// <summary>当前起始触点身份；null 表示必须等待新的按下。</summary>
        private int? pointerId;
        public event Action Pressed;
        public event Action Released;

        /// <summary>按下即提交一次边沿；滑入没有 PointerDown，不触发操作。</summary>
        public void OnPointerDown(PointerEventData data)
        {
            if (data == null || pointerId.HasValue || !isActiveAndEnabled) return;
            pointerId = data.pointerId;
            Pressed?.Invoke();
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (data != null && pointerId == data.pointerId) ResetCapture();
        }

        /// <summary>失焦、隐藏或门禁关闭后释放，旧手指须重新按下才可触发。</summary>
        public void ResetCapture()
        {
            if (!pointerId.HasValue) return;
            pointerId = null;
            Released?.Invoke();
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
