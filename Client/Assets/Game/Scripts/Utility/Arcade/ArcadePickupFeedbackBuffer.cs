using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>仅合并成功拾取的表现数值；不写奖励，不混合不同奖励身份。</summary>
    public sealed class ArcadePickupFeedbackBuffer
    {
        /// <summary>同类连续拾取的合并窗口及提示寿命，均使用真实秒。</summary>
        public const double MergeSeconds = .3;
        public const float LifetimeSeconds = 1.2f;
        private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
        private long sequence;
        private double lastSound = double.NegativeInfinity;
        private sealed class Entry { public long Amount, Sequence; public double Time; }

        /// <summary>返回本组的稳定提示身份与真实总量；非正奖励不产生提示。</summary>
        public bool TryAdd(string rewardKey, long amount, double now, out string tipKey, out long total)
        {
            tipKey = null; total = 0;
            if (string.IsNullOrWhiteSpace(rewardKey) || amount <= 0 || double.IsNaN(now)) return false;
            if (!entries.TryGetValue(rewardKey, out var entry) || now < entry.Time || now - entry.Time > MergeSeconds ||
                entry.Amount > long.MaxValue - amount)
            {
                entry = new Entry { Sequence = ++sequence };
                entries[rewardKey] = entry;
            }
            entry.Amount += amount;
            entry.Time = now;
            total = entry.Amount;
            tipKey = "arcade.pickup." + entry.Sequence;
            return true;
        }

        /// <summary>密集领取共用声音冷却；同一瞬间最多接受一次。</summary>
        public bool TrySound(double now)
        {
            if (double.IsNaN(now) || now - lastSound < .12) return false;
            lastSound = now;
            return true;
        }

        /// <summary>离开表现现场即丢弃合并状态及冷却，不重播历史。</summary>
        public void Clear() { entries.Clear(); lastSound = double.NegativeInfinity; }
    }
}
