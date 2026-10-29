using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Game
{
    /// <summary>读取设备原始意图并管理监听寿命；不裁决相机模式、装备资格或战斗数值。</summary>
    public sealed class PlayerInputCtrl : IDisposable
    {
        /// <summary>只采集原始双肩意图；是否可释放由街机System裁决。</summary>
        private readonly ArcadeFieldUpgradeChord fieldUpgradeChord=new ArcadeFieldUpgradeChord();
        public bool TryConsumeFieldUpgrade()=>fieldUpgradeChord.TryConsume();
        public void ClearFieldUpgradePulse()=>fieldUpgradeChord.ClearPulse();
        public bool FieldUpgradePressedThisFrame=>fieldUpgradeChord.WasPressedThisFrame;
        private readonly InputAction moveAction;
        private readonly InputAction aimAction;
        private readonly InputAction attackAction;
        private readonly InputAction cancelAction;
        private readonly InputAction pointerPositionAction;
        private readonly InputAction lookDeltaAction;
        private readonly PointerUiBlocker pointerUiBlocker = new PointerUiBlocker();
        private readonly MobileLookInputCtrl mobileLook;

        public PlayerInputCtrl()
        {
            mobileLook = new MobileLookInputCtrl(pointerUiBlocker.IsBlocked);
            moveAction = CreateMoveAction();
            aimAction = new InputAction(
                "PlayerAim",
                InputActionType.Value,
                "<Gamepad>/rightStick");
            attackAction = new InputAction("PlayerAttack", InputActionType.Button);
            attackAction.AddBinding("<Mouse>/leftButton");
            cancelAction = new InputAction(
                "FirstPersonCancel",
                InputActionType.Button,
                "<Keyboard>/x");
            pointerPositionAction = new InputAction("PointerPosition", InputActionType.Value, "<Pointer>/position");
            lookDeltaAction = new InputAction("CameraLook", InputActionType.Value, "<Mouse>/delta");

            moveAction.performed += OnMovePerformed;
            moveAction.canceled += OnMoveCanceled;
        }

        public Vector2 MoveInput { get; private set; }
        /// <summary>右摇杆的归一化瞄准意图；普通玩法仍由鼠标射线决定朝向。</summary>
        public Vector2 AimInput => Normalize(aimAction.ReadValue<Vector2>());
        public bool IsEnabled => moveAction.enabled;
        public bool AttackPressedThisFrame => attackAction.enabled && attackAction.WasPressedThisFrame() &&
                                              TryGetUnblockedMousePosition(out _);
        /// <summary>鼠标左键持续按下且未落在 UI 上；屏幕右摇杆通过 AimInput 独立表达街机射击。</summary>
        public bool AttackHeld => attackAction.enabled && attackAction.IsPressed() &&
                                  TryGetUnblockedMousePosition(out _);
        public bool CancelPressedThisFrame => cancelAction.WasPressedThisFrame();
        public Vector2 LookDelta => Mouse.current != null ? lookDeltaAction.ReadValue<Vector2>() : Vector2.zero;
        /// <summary>物理右键持有状态；禁用玩法监听时仍观察松开，避免暂停后旧按压自动恢复。</summary>
        public bool ShoulderAimHeld => Mouse.current != null && Mouse.current.rightButton.isPressed;

        /// <summary>触屏与 Editor 拖动只产生滑屏意图，由统一输入系统分流为镜头或基地人物转向。</summary>
        public Vector2 ReadMobileLookDelta(bool enabled) => mobileLook.ReadDelta(enabled);
        public void ResetMobileLook() => mobileLook.ResetCapture();

        public bool TryGetPointerScreenPosition(out Vector2 position)
        {
            if (!pointerPositionAction.enabled) { position = Vector2.zero; return false; }
            if (Mouse.current == null) { position = Vector2.zero; return false; }
            position = pointerPositionAction.ReadValue<Vector2>();
            return !pointerUiBlocker.IsBlocked(position);
        }

        /// <summary>只接受未被 UI 占用的鼠标位置；移动端触摸不再提交场景攻击。</summary>
        private bool TryGetUnblockedMousePosition(out Vector2 position)
        {
            if (Mouse.current == null) { position = Vector2.zero; return false; }
            position = Mouse.current.position.ReadValue();
            return !pointerUiBlocker.IsBlocked(position);
        }

        public static Vector2 Normalize(Vector2 raw)
        {
            return Vector2.ClampMagnitude(raw, 1f);
        }

        public void Enable()
        {
            fieldUpgradeChord.Enable();
            if (!moveAction.enabled)
            {
                moveAction.Enable();
            }

            if (!aimAction.enabled)
            {
                aimAction.Enable();
            }

            if (!attackAction.enabled)
            {
                attackAction.Enable();
            }

            if (!cancelAction.enabled)
            {
                cancelAction.Enable();
            }

            if (!pointerPositionAction.enabled) pointerPositionAction.Enable();
            if (!lookDeltaAction.enabled) lookDeltaAction.Enable();
        }

        public void Disable()
        {
            fieldUpgradeChord.Disable();
            mobileLook.ResetCapture();
            if (moveAction.enabled)
            {
                moveAction.Disable();
            }

            if (aimAction.enabled)
            {
                aimAction.Disable();
            }

            if (attackAction.enabled)
            {
                attackAction.Disable();
            }

            if (cancelAction.enabled)
            {
                cancelAction.Disable();
            }

            if (pointerPositionAction.enabled) pointerPositionAction.Disable();
            if (lookDeltaAction.enabled) lookDeltaAction.Disable();

            MoveInput = Vector2.zero;
        }

        public void Dispose()
        {
            fieldUpgradeChord.Dispose();
            mobileLook.Dispose();
            moveAction.performed -= OnMovePerformed;
            moveAction.canceled -= OnMoveCanceled;
            moveAction.Dispose();
            aimAction.Dispose();
            attackAction.Dispose();
            cancelAction.Dispose();
            pointerPositionAction.Dispose();
            lookDeltaAction.Dispose();
            MoveInput = Vector2.zero;
        }

        private static InputAction CreateMoveAction()
        {
            var action = new InputAction(
                "PlayerMove",
                InputActionType.Value,
                expectedControlType: "Vector2");

            action.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            action.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            // OnScreenStick 写入虚拟 Gamepad；桌面手柄和移动端因此共用同一条输入通路。
            action.AddBinding("<Gamepad>/leftStick");

            return action;
        }

        private void OnMovePerformed(InputAction.CallbackContext context)
        {
            MoveInput = Normalize(context.ReadValue<Vector2>());
        }

        private void OnMoveCanceled(InputAction.CallbackContext context)
        {
            MoveInput = Vector2.zero;
        }
    }

    /// <summary>以当前 EventSystem 做无分配 UI 射线检测，防止界面点击泄漏为场景攻击。</summary>
    internal sealed class PointerUiBlocker
    {
        private readonly List<RaycastResult> results = new List<RaycastResult>();
        private EventSystem owner;
        private PointerEventData pointer;

        public bool IsBlocked(Vector2 screenPosition)
        {
            var current = EventSystem.current;
            if (current == null) return false;
            if (owner != current || pointer == null)
            {
                owner = current;
                pointer = new PointerEventData(current);
            }
            pointer.Reset();
            pointer.position = screenPosition;
            results.Clear();
            current.RaycastAll(pointer, results);
            return results.Count > 0;
        }
    }
}
