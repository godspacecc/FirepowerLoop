using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>视角切换及暂停后的旧持续按压须释放一次；不保存、不改设备绑定。</summary>
        private bool arcadeAttackRequiresRelease;
        /// <summary>只保存当前触屏按钮的瞬时按压；切换视角、暂停或失效时清空，不进入存档。</summary>
        private bool mobileFirstPersonFireHeld;

        /// <summary>接收独立开火按钮的按下/释放；只有活动单人第一人称现场可以接收新按压。</summary>
        public void SetMobileFirstPersonFireHeld(bool held)
        {
            if (!held) { mobileFirstPersonFireHeld = false; return; }
            var runtime = this.GetModel<ArcadeCombatModel>().Runtime;
            if (runtime == null || runtime.FirstPersonRemaining <= 0 || runtime.IslandFirstPersonSuspended ||
                !view.IsFirstPersonActive || runtime.PlayerHealth <= 0 || runtime.RespawnRemaining > 0 ||
                runtime.InTreasureRoom || this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing ||
                UIHelper.HasUIOpen || this.GetSystem<MobileInputSystem>()?.IsMobileInputMode != true) return;
            mobileFirstPersonFireHeld = true;
            // 按钮已在模式切换时释放触点，此处是新的 PointerDown，不受旧鼠标/摇杆按压闸门影响。
            arcadeAttackRequiresRelease = false;
        }
        /// <summary>只在第一场普通单人现场提交相机事务；成功后才刷新时长，不接触武器计时。</summary>
        private bool TryBeginFirstPerson(ArcadeCombatRuntime runtime, ArcadeDropDefinition drop, out string error)
        {
            error = null;
            if (runtime.ArenaNumber != 1 || runtime.IsChallenge || runtime.InTreasureRoom ||
                runtime.AwaitingExit || runtime.RespawnRemaining > 0 || runtime.PlayerHealth <= 0 ||
                drop.DurationSeconds <= 0)
            { error = "当前街机状态不能进入第一人称"; return false; }
            if (runtime.FirstPersonRemaining <= 0 || !view.IsFirstPersonActive)
            {
                if (!view.TryBeginFirstPerson(out error)) return false;
                ClearArcadeInput(runtime);
            }
            if (!view.TryReadFirstPersonAim(out var forward))
            {
                // 成功准备镜头仍不足以提交拾取：缺少水平中心射线时回滚本次租约。
                view.EndFirstPerson();
                runtime.FirstPersonRemaining = 0;
                ClearArcadeInput(runtime);
                error = "第一人称中心瞄准不可用，已恢复俯视";
                return false;
            }
            runtime.AimDirection = forward;
            runtime.FirstPersonRemaining = drop.DurationSeconds;
            return true;
        }

        /// <summary>先释放规则所有权再恢复镜头；重复退出不重新写相机或武器。</summary>
        private void EndFirstPerson(ArcadeCombatRuntime runtime)
        {
            if (runtime == null || runtime.FirstPersonRemaining <= 0) return;
            runtime.FirstPersonRemaining = 0;
            runtime.IslandFirstPersonSuspended = false;
            view?.EndFirstPerson();
            ClearArcadeInput(runtime);
        }

        private void ClearArcadeInput(ArcadeCombatRuntime runtime)
        {
            runtime.MoveDirection = Vector2.zero;
            runtime.Firing = false;
            mobileFirstPersonFireHeld = false;
            // 同时观察仍按着的触点；新视角不能把旧摇杆手指的 Began 当成新滑屏。
            input?.ReadMobileLookDelta(false);
            input?.ResetMobileLook();
            input?.ClearFieldUpgradePulse();
            arcadeAttackRequiresRelease = true;
        }

        /// <summary>镜头只水平转向；触屏滑动和独立开火分离，桌面保留原鼠标/摇杆操作。</summary>
        private bool ReadFirstPersonInput(ArcadeCombatRuntime runtime, Vector2 move, Vector2 stickAim, bool stickFiring)
        {
            if (runtime.FirstPersonRemaining <= 0) return false;
            if (runtime.IslandFirstPersonSuspended) return false;
            if (!view.IsFirstPersonActive || !view.TryReadFirstPersonAim(out _))
            { EndFirstPerson(runtime); return false; }
            if (input?.CancelPressedThisFrame == true)
            { EndFirstPerson(runtime); return true; }
            var mobile = this.GetSystem<MobileInputSystem>();
            bool mobileMode = mobile != null && mobile.IsMobileInputMode;
            var look = mobileMode
                ? new Vector2(input?.ReadMobileLookDelta(true).x ?? 0, 0)
                : new Vector2(input?.LookDelta.x ?? 0, 0);
            view.ApplyFirstPersonLook(look);
            if (!view.TryReadFirstPersonAim(out var forward))
            { EndFirstPerson(runtime); return true; }
            runtime.AimDirection = forward;
            runtime.MoveDirection = RelativeFirstPersonMovement(move, forward);
            runtime.Firing = mobileMode ? ReadArcadeAttackHeld(mobileFirstPersonFireHeld) : ReadArcadeFiring(stickFiring);
            return true;
        }

        private bool ReadArcadeFiring(bool stickFiring)
            => ReadArcadeAttackHeld(stickFiring || input?.AttackHeld == true);

        /// <summary>共享旧按压释放闸门；触屏第一人称只传独立按钮，滑屏或右摇杆不得夹带射击。</summary>
        private bool ReadArcadeAttackHeld(bool held)
        {
            if (arcadeAttackRequiresRelease)
            {
                if (!held) arcadeAttackRequiresRelease = false;
                return false;
            }
            return held;
        }

        /// <summary>输入以视线为前方，输出竞技场二维方向；斜向移动保持原长度上限。</summary>
        internal static Vector2 RelativeFirstPersonMovement(Vector2 move, Vector2 forward) =>
            Vector2.ClampMagnitude(new Vector2(forward.y, -forward.x) * move.x + forward * move.y, 1);
    }
}
