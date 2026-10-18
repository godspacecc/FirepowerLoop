using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>仅记录已消费的炸弹表现序列；首次进入、隐藏区域与旧轮记录只推进基线。</summary>
    internal sealed class OnlineBombPresentationState
    {
        /// <summary>服务器每轮重建记录序列，同轮快照只保留最高已观察序列。</summary>
        private long lastSequence;
        private int roundGeneration=-1;
        public void Observe(IReadOnlyList<OnlineArcadeBombBlastSnapshot> receipts,bool baseline,int generation,bool visible,Action<Vector2> play)
        {
            if(generation<roundGeneration)return;
            if(generation!=roundGeneration){roundGeneration=generation;lastSequence=0;}
            foreach(var receipt in receipts)
            {
                if(receipt.RoundGeneration!=generation)continue;
                if(receipt.Sequence<=lastSequence)continue;
                lastSequence=receipt.Sequence;
                if(baseline||!visible)continue;
                play(new Vector2(receipt.PositionX,receipt.PositionZ));
            }
        }
        /// <summary>完整离场丢弃轮身份，下一次进入只建立基线。</summary>
        public void Clear(){lastSequence=0;roundGeneration=-1;}
    }
}
