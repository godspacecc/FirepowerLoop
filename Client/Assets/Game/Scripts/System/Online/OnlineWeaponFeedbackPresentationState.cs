using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>消费权威瞬时反馈；只管理去重和过期，不裁决命中或持有表现资源。</summary>
    internal sealed class OnlineWeaponFeedbackPresentationState
    {
        /// <summary>已消费最高序列，隐藏及过期记录也推进，防止恢复时补播。</summary>
        private long lastSequence;
        /// <summary>当前轮次，拒绝迟到的旧地图事件。</summary>
        private int roundGeneration=-1;
        /// <summary>跨设备只比较相对时差，估计额外网络排队。</summary>
        private double minimumClockOffset=double.PositiveInfinity;

        /// <summary>首次快照仅建立基线；正常帧只播放0.4秒内的新鲜反馈。</summary>
        public void Observe(IReadOnlyList<OnlineArcadeWeaponFeedbackSnapshot> receipts,bool baseline,int generation,bool visible,
            long snapshotServerTime,double receivedSeconds,Action<OnlineArcadeWeaponFeedbackSnapshot> play)
        {
            if(generation<roundGeneration)return;
            if(generation!=roundGeneration){roundGeneration=generation;lastSequence=0;}
            double queuedSeconds=0;
            if(snapshotServerTime>0)
            {
                double offset=receivedSeconds-snapshotServerTime/1000d;
                minimumClockOffset=Math.Min(minimumClockOffset,offset);
                queuedSeconds=Math.Max(0,offset-minimumClockOffset);
            }
            foreach(var hit in receipts)
            {
                if(hit.RoundGeneration!=generation||hit.Sequence<=lastSequence)continue;
                lastSequence=hit.Sequence;
                double age=(snapshotServerTime-hit.ServerTime)/1000d+queuedSeconds;
                if(baseline||!visible||snapshotServerTime<=0||hit.ServerTime<=0||age<-.1||age>.4||!Valid(hit))continue;
                play(hit);
            }
        }

        /// <summary>非法协议值只丢弃当前反馈，不污染后续粒子或声音。</summary>
        private static bool Valid(OnlineArcadeWeaponFeedbackSnapshot hit)=>hit.Kind>=1&&hit.Kind<=5&&
            Finite(hit.X)&&Finite(hit.Z)&&Finite(hit.DirectionX)&&Finite(hit.DirectionZ)&&
            (hit.Kind<3||Finite(hit.Radius)&&hit.Radius>0);
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);

        /// <summary>整场离开清理，下一局重新建立基线。</summary>
        public void Clear(){lastSequence=0;roundGeneration=-1;minimumClockOffset=double.PositiveInfinity;}
    }
}
