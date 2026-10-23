using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>每轮单调序列去重；首次、隐藏区域和网络过时命中只推进基线，不重播历史电击。</summary>
    internal sealed class OnlineElectricHitPresentationState
    {
        private long lastSequence;
        private int roundGeneration = -1;
        /// <summary>以已接收快照的最低相对时差估计新增网络排队，不假设两台设备系统时钟相同。</summary>
        private double minimumClockOffset = double.PositiveInfinity;

        public void Observe(IReadOnlyList<OnlineArcadeElectricHitSnapshot> receipts, bool baseline,
            int generation, bool visible, float elapsed, Action<int, Vector2> play, long snapshotServerTime=0, double receivedSeconds=0)
        {
            if (generation < roundGeneration) return;
            if (generation != roundGeneration) { roundGeneration = generation; lastSequence = 0; }
            double queuedSeconds=0;
            if(snapshotServerTime>0)
            {
                double offset=receivedSeconds-snapshotServerTime/1000d;
                minimumClockOffset=Math.Min(minimumClockOffset,offset);
                queuedSeconds=Math.Max(0,offset-minimumClockOffset);
            }
            foreach (var receipt in receipts)
            {
                if (receipt.RoundGeneration != generation || receipt.Sequence <= lastSequence) continue;
                lastSequence = receipt.Sequence;
                double age = receipt.ServerTime>0&&snapshotServerTime>0 ? (snapshotServerTime-receipt.ServerTime)/1000d+queuedSeconds : elapsed-receipt.ElapsedSeconds+queuedSeconds;
                if (baseline || !visible || age < -.1f || age > .4f) continue;
                play(receipt.HazardId, new Vector2(receipt.X, receipt.Z));
            }
        }
        /// <summary>完整离场后允许下一场重新建立轮次基线。</summary>
        public void Clear() { lastSequence = 0; roundGeneration = -1;minimumClockOffset=double.PositiveInfinity; }
    }
}
