using System;
using QFramework;

namespace Game
{
    /// <summary>挑战武器状态提交后通知，不携带可变枪械对象。</summary>
    public readonly struct ChallengeChangedEvent { }

    /// <summary>挑战界面的只读弹药与会话快照。</summary>
    public readonly struct ChallengeSnapshot
    {
        public readonly bool Active, Paused;
        public readonly string WeaponId;
        public readonly int Slot, Magazine, MagazineCapacity, Reserve, ReserveCapacity;
        public readonly float ReloadRemaining, ReloadSeconds;
        internal ChallengeSnapshot(ChallengeModel model)
        {
            Active=model.Active; Paused=model.Paused; Slot=model.SelectedSlot;
            var gun=model.Current;
            WeaponId=gun?.Id??string.Empty; Magazine=gun?.Magazine??0; MagazineCapacity=gun?.MagazineCapacity??0;
            Reserve=gun?.Reserve??0; ReserveCapacity=gun?.ReserveCapacity??0;
            ReloadRemaining=gun?.ReloadRemaining??0; ReloadSeconds=gun?.ReloadSeconds??0;
        }
    }

    /// <summary>挑战局独立权威状态，只有ChallengeSystem修改，不接入旧模式存档。</summary>
    public sealed class ChallengeModel : AbstractModel
    {
        internal bool Active, Paused;
        internal ChallengeCombatState Combat;
        internal int SelectedSlot;
        internal ChallengeWeaponState[] Weapons=Array.Empty<ChallengeWeaponState>();
        internal ChallengeWeaponState Current => Active && SelectedSlot>=0 && SelectedSlot<Weapons.Length ? Weapons[SelectedSlot] : null;
        public ChallengeSnapshot Snapshot => new ChallengeSnapshot(this);
        protected override void OnInit() { }
    }
}
