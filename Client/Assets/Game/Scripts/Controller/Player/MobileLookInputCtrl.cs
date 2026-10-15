using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Game
{
    /// <summary>
    /// 捕获右半屏空白处起按的一根手指，输出滑屏增量供越肩镜头或基地人物转向使用；不拥有相机、移动或战斗状态。
    /// 不铺设触摸遮罩，避免挡住其它非阻断 HUD 的按钮。
    /// </summary>
    internal sealed class MobileLookInputCtrl : IDisposable
    {
        private readonly Func<Vector2, bool> isUiBlocked;
        private readonly HashSet<int> observedTouches = new HashSet<int>();
        private readonly HashSet<int> currentTouches = new HashSet<int>();
        /// <summary>本次触屏设备内的触点身份；释放、失焦或门禁关闭后不得接管仍按着的触点。</summary>
        private TouchControl capturedTouch;
        private int capturedTouchId;
        private int deviceId;
        private Vector2 lastPosition;
        private bool mouseCaptured;
        private bool mousePressObserved;
        private bool focused = true;
        private bool paused;

        public MobileLookInputCtrl(Func<Vector2, bool> isUiBlocked)
        {
            this.isUiBlocked = isUiBlocked ?? throw new ArgumentNullException(nameof(isUiBlocked));
            LifeCycle.RegisterApplicationFocus(OnFocus);
            LifeCycle.RegisterApplicationPause(OnPause);
            UIHelper.BlockingUIChanged += OnBlockingUiChanged;
        }

        /// <summary>统一输入每帧读取一次；按 1080 像素屏高归一化，分辨率变化不改变转向手感。</summary>
        public Vector2 ReadDelta(bool enabled)
        {
            enabled &= focused && !paused;
            var touchscreen = Touchscreen.current;
            var delta = Vector2.zero;
            if (touchscreen != null)
            {
                if (deviceId != touchscreen.deviceId)
                {
                    ResetCapture();
                    observedTouches.Clear();
                    deviceId = touchscreen.deviceId;
                }
                currentTouches.Clear();
                if (!enabled) ResetCapture();
                foreach (var touch in touchscreen.touches)
                {
                    if (!touch.press.isPressed) continue;
                    var id = touch.touchId.ReadValue();
                    currentTouches.Add(id);
                    var position = touch.position.ReadValue();
                    var began = !observedTouches.Contains(id) && touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Began;
                    if (enabled && capturedTouch == null && began && CanStart(position))
                    {
                        capturedTouch = touch;
                        capturedTouchId = id;
                        lastPosition = position;
                    }
                    if (touch != capturedTouch || id != capturedTouchId) continue;
                    // 进入 UI 后取消本次滑屏，直到重新起按，防止滑过按钮时继续转镜头或人物。
                    if (!enabled || isUiBlocked(position)) { ResetCapture(); continue; }
                    delta = position - lastPosition;
                    lastPosition = position;
                }
                if (capturedTouch != null && !currentTouches.Contains(capturedTouchId)) ResetCapture();
                observedTouches.Clear();
                observedTouches.UnionWith(currentTouches);
                if (currentTouches.Count > 0) return Normalize(delta);
            }
            else
            {
                capturedTouch = null;
                observedTouches.Clear();
            }

            // Editor 的 #mobileinput on 允许用鼠标按住右侧空白拖动，仍需满足与真机相同的门禁。
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.isPressed)
            {
                mouseCaptured = false;
                mousePressObserved = false;
                return Vector2.zero;
            }
            var point = mouse.position.ReadValue();
            if (!mousePressObserved)
            {
                mousePressObserved = true;
                // 用本适配器采样的按压边沿；Input System 的 Editor/Dynamic 更新域可能不同，不能依赖其逐帧标记。
                mouseCaptured = enabled && CanStart(point);
                lastPosition = point;
            }
            if (!enabled || isUiBlocked(point)) mouseCaptured = false;
            if (!mouseCaptured) return Vector2.zero;
            delta = point - lastPosition;
            lastPosition = point;
            return Normalize(delta);
        }

        /// <summary>取消当前手指所有权，保留已观察触点，避免 UI 关闭或换地图后接管旧按压。</summary>
        public void ResetCapture()
        {
            capturedTouch = null;
            capturedTouchId = 0;
            mouseCaptured = false;
        }

        public void Dispose()
        {
            LifeCycle.UnregisterApplicationFocus(OnFocus);
            LifeCycle.UnregisterApplicationPause(OnPause);
            UIHelper.BlockingUIChanged -= OnBlockingUiChanged;
            ResetCapture();
        }

        private bool CanStart(Vector2 position) => position.x >= Screen.width * .5f && !isUiBlocked(position);
        private static Vector2 Normalize(Vector2 delta) => delta * (1080f / Mathf.Max(1, Screen.height));
        private void OnFocus(bool value) { focused = value; if (!value) ResetCapture(); }
        private void OnPause(bool value) { paused = value; if (value) ResetCapture(); }
        private void OnBlockingUiChanged(bool blocked) { if (blocked) ResetCapture(); }
    }
}
