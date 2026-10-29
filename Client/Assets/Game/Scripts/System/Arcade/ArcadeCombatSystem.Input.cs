using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>暂停及模式切换后的旧持续按压须释放一次；不保存、不改设备绑定。</summary>
        private bool arcadeAttackRequiresRelease;
        /// <summary>清空活动输入并要求重新按下，防止暂停和转场后误射。</summary>
        private void ClearArcadeInput(ArcadeCombatRuntime runtime)
        {
            runtime.MoveDirection = Vector2.zero;
            runtime.Firing = false;
            // 清理残留触点及技能脉冲，避免暂停恢复后延续旧操作。
            input?.ReadMobileLookDelta(false);
            input?.ResetMobileLook();
            input?.ClearFieldUpgradePulse();
            arcadeAttackRequiresRelease = true;
        }

        /// <summary>统一摇杆与设备射击状态，沿用同一个释放闸门。</summary>
        private bool ReadArcadeFiring(bool stickFiring)
            => ReadArcadeAttackHeld(stickFiring || input?.AttackHeld == true);

        /// <summary>共享旧按压释放闸门；暂停恢复后先释放，再接受新开火。</summary>
        private bool ReadArcadeAttackHeld(bool held)
        {
            if (arcadeAttackRequiresRelease)
            {
                if (!held) arcadeAttackRequiresRelease = false;
                return false;
            }
            return held;
        }

    }
}
