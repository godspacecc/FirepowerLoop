using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game
{
    /// <summary>
    /// 收集联机街机的本地持续输入与一次性操作；只缓存玩家意图，不推进任何权威战斗状态。
    /// </summary>
    public sealed class OnlineArcadeInputController : IDisposable
    {
        private const float AimDeadZone = 0.15f;
        private readonly InputAction moveAction;
        private readonly InputAction aimAction;
        private readonly InputAction fireAction;
        private readonly InputAction triggerAction;
        private readonly InputAction bombAction;
        private readonly InputAction dashAction;
        private readonly PointerUiBlocker pointerUiBlocker = new PointerUiBlocker();
        // 帧号隔离同帧技能优先级，旧帧未确认的消耗品不能被新技能丢弃。
        private readonly Queue<int> queuedBombs = new Queue<int>();
        private readonly Queue<int> queuedDashes = new Queue<int>();
        private readonly Queue<int> queuedUpgrades = new Queue<int>();
        private readonly Func<int> frameProvider;
        private readonly ArcadeFieldUpgradeChord chord = new ArcadeFieldUpgradeChord();
        private int lastUpgradeFrame = int.MinValue;

        public OnlineArcadeInputController() : this(()=>Time.frameCount) {}

        public OnlineArcadeInputController(Func<int> frameProvider)
        {
            this.frameProvider=frameProvider??throw new ArgumentNullException(nameof(frameProvider));
            moveAction = new InputAction("OnlineArcadeMove", InputActionType.Value, expectedControlType: "Vector2");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            moveAction.AddBinding("<Gamepad>/leftStick");

            aimAction = new InputAction("OnlineArcadeAim", InputActionType.Value, "<Gamepad>/rightStick");
            fireAction = new InputAction("OnlineArcadeFire", InputActionType.Button);
            fireAction.AddBinding("<Mouse>/leftButton");
            triggerAction = new InputAction("OnlineArcadeTrigger", InputActionType.Button, "<Gamepad>/rightTrigger");
            bombAction = new InputAction("OnlineArcadeBomb", InputActionType.Button);
            bombAction.AddBinding("<Keyboard>/q");
            bombAction.AddBinding("<Gamepad>/buttonWest");
            dashAction = new InputAction("OnlineArcadeDash", InputActionType.Button);
            dashAction.AddBinding("<Keyboard>/space");
            dashAction.AddBinding("<Gamepad>/buttonSouth");
            bombAction.performed += OnBomb;
            dashAction.performed += OnDash;
        }

        public bool IsEnabled => moveAction.enabled;

        public void Enable()
        {
            chord.Enable();
            moveAction.Enable();
            aimAction.Enable();
            fireAction.Enable();
            triggerAction.Enable();
            bombAction.Enable();
            dashAction.Enable();
        }

        public void Disable(bool clearPulses)
        {
            chord.Disable();
            moveAction.Disable();
            aimAction.Disable();
            fireAction.Disable();
            triggerAction.Disable();
            bombAction.Disable();
            dashAction.Disable();
            if (clearPulses) ClearPulses();
        }

        /// <summary>读取持续状态；右摇杆同时表达瞄准和射击，鼠标与实体扳机保留桌面/手柄入口。</summary>
        public OnlineArcadeInputFrame Read(IArcadeCombatViewPort view, Vector2 playerPosition, bool allowFieldUpgrade = true)
        {
            if(chord.TryConsume()&&allowFieldUpgrade)QueueFieldUpgrade();
            if(!allowFieldUpgrade)queuedUpgrades.Clear();
            Vector2 move = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            Vector2 aim = Vector2.ClampMagnitude(aimAction.ReadValue<Vector2>(), 1f);
            bool stickFiring = aim.sqrMagnitude >= AimDeadZone * AimDeadZone;
            bool mouseFiring = fireAction.IsPressed() && Mouse.current != null;
            if (aim.sqrMagnitude < AimDeadZone * AimDeadZone && Mouse.current != null && view != null)
            {
                Vector2 screen = Mouse.current.position.ReadValue();
                if (pointerUiBlocker.IsBlocked(screen))
                {
                    mouseFiring = false;
                }
                else if (view.TryScreenToArena(screen, out Vector2 target))
                {
                    Vector2 mouseAim = target - playerPosition;
                    if (mouseAim.sqrMagnitude >= AimDeadZone * AimDeadZone) aim = mouseAim.normalized;
                }
            }
            if (aim.sqrMagnitude < AimDeadZone * AimDeadZone) aim = Vector2.zero;
            return new OnlineArcadeInputFrame(move, aim, stickFiring || mouseFiring || triggerAction.IsPressed(),
                queuedUpgrades.Count==0&&queuedBombs.Count>0, queuedUpgrades.Count==0&&queuedDashes.Count>0, queuedUpgrades.Count>0);
        }

        public void QueueBomb(){if(lastUpgradeFrame!=frameProvider())queuedBombs.Enqueue(frameProvider());}
        public void QueueDash(){if(lastUpgradeFrame!=frameProvider())queuedDashes.Enqueue(frameProvider());}
        /// <summary>技能优先于同帧炸弹和冲刺，多个输入来源只产生一个技能脉冲。</summary>
        public void QueueFieldUpgrade()
        {
            int frame=frameProvider();if(lastUpgradeFrame==frame)return;
            lastUpgradeFrame=frame;queuedUpgrades.Enqueue(frame);
            RemoveFrame(queuedBombs,frame);RemoveFrame(queuedDashes,frame);
        }
        private static void RemoveFrame(Queue<int> queue,int frame)
        {int count=queue.Count;while(count-->0){int value=queue.Dequeue();if(value!=frame)queue.Enqueue(value);}}
        /// <summary>只在权威确认后消费已捕获的一次脉冲。</summary>
        public void Acknowledge(bool bomb,bool dash,bool fieldUpgrade=false)
        {
            if(bomb&&queuedBombs.Count>0)queuedBombs.Dequeue();
            if(dash&&queuedDashes.Count>0)queuedDashes.Dequeue();
            if(fieldUpgrade&&queuedUpgrades.Count>0)queuedUpgrades.Dequeue();
        }
        internal int QueuedBombs=>queuedBombs.Count;
        internal int QueuedDashes=>queuedDashes.Count;
        internal int QueuedFieldUpgrades=>queuedUpgrades.Count;
        internal void ClearPulses(){queuedBombs.Clear();queuedDashes.Clear();queuedUpgrades.Clear();chord.ClearPulse();lastUpgradeFrame=int.MinValue;}
        private void OnBomb(InputAction.CallbackContext _) => QueueBomb();
        private void OnDash(InputAction.CallbackContext _) => QueueDash();

        public void Dispose()
        {
            bombAction.performed -= OnBomb;
            dashAction.performed -= OnDash;
            Disable(true);
            chord.Dispose();
            moveAction.Dispose(); aimAction.Dispose(); fireAction.Dispose();
            triggerAction.Dispose(); bombAction.Dispose(); dashAction.Dispose();
        }
    }

    /// <summary>单次待发送的联机街机输入采样；脉冲字段在服务端确认前保持不变。</summary>
    public readonly struct OnlineArcadeInputFrame
    {
        public OnlineArcadeInputFrame(Vector2 move, Vector2 aim, bool firing, bool bomb, bool dash, bool fieldUpgrade=false)
        { Move = move; Aim = aim; Firing = firing; Bomb = bomb; Dash = dash; FieldUpgrade=fieldUpgrade; }
        public Vector2 Move { get; }
        public Vector2 Aim { get; }
        public bool Firing { get; }
        public bool Bomb { get; }
        public bool Dash { get; }
        public bool FieldUpgrade { get; }
    }
}
