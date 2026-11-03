using System;

namespace Game
{
    /// <summary>挑战专属单枪弹药事务，由挑战系统持有并推进，不读取其他模式的武器状态。</summary>
    public sealed class ChallengeWeaponState
    {
        /// <summary>稳定武器身份及配置上限。</summary>
        public string Id { get; }
        public int MagazineCapacity { get; }
        public int ReserveCapacity { get; }
        public float ReloadSeconds { get; }
        public float FireInterval { get; }
        /// <summary>当前弹匣、备弹及尚未提交的换弹时间。</summary>
        public int Magazine { get; private set; }
        public int Reserve { get; private set; }
        public float ReloadRemaining { get; private set; }
        /// <summary>射击冷却独立于换弹，切枪不能清除冷却。</summary>
        private float fireRemaining;

        public ChallengeWeaponState(string id, int magazineCapacity, int reserveCapacity, float reloadSeconds, float fireInterval)
        {
            if (string.IsNullOrWhiteSpace(id) || magazineCapacity <= 0 || reserveCapacity < 0 ||
                !PositiveFinite(reloadSeconds) || !PositiveFinite(fireInterval))
                throw new ArgumentException("挑战武器容量或计时配置无效");
            Id=id; MagazineCapacity=magazineCapacity; ReserveCapacity=reserveCapacity;
            ReloadSeconds=reloadSeconds; FireInterval=fireInterval;
            Magazine=magazineCapacity; Reserve=reserveCapacity;
        }

        /// <summary>成功发射一次才扣一发；空弹匣触发换弹但不产生射击。</summary>
        public bool TryFire()
        {
            if (ReloadRemaining > 0 || fireRemaining > 0) return false;
            if (Magazine == 0) { TryReload(); return false; }
            Magazine--; fireRemaining=FireInterval;
            return true;
        }

        /// <summary>换弹起始只创建事务，不提前扣备弹；重复输入不重置进度。</summary>
        public bool TryReload()
        {
            if (ReloadRemaining > 0 || Magazine == MagazineCapacity || Reserve == 0) return false;
            ReloadRemaining=ReloadSeconds;
            return true;
        }

        /// <summary>切枪或结束时取消尚未提交的装填。</summary>
        public void CancelReload() => ReloadRemaining=0;

        /// <summary>只由未暂停的挑战系统推进；拒绝无效时间，完成时守恒转移弹药。</summary>
        public void Advance(float seconds)
        {
            if (!PositiveFinite(seconds)) return;
            fireRemaining=Math.Max(0,fireRemaining-seconds);
            if (ReloadRemaining <= 0) return;
            ReloadRemaining=Math.Max(0,ReloadRemaining-seconds);
            if (ReloadRemaining > 0) return;
            int amount=Math.Min(MagazineCapacity-Magazine,Reserve);
            Magazine+=amount; Reserve-=amount;
        }

        /// <summary>补给只增加备弹，先计算空余量避免整数溢出；返回实际补入数量。</summary>
        public int Supply(int amount)
        {
            int accepted=Math.Min(Math.Max(0,amount),ReserveCapacity-Reserve);
            Reserve+=accepted;
            return accepted;
        }

        /// <summary>所有计时配置与推进量必须为有限正数。</summary>
        private static bool PositiveFinite(float value) => value>0 && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
