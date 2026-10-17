using System;
using System.Collections.Generic;

namespace FirepowerLoop.Arcade
{
    /// <summary>个人本局能力状态；商店账本与轮前保存使用同一份值，不持有表现对象。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public class ArcadeSlayerMartAbilityState
    {
        /// <summary>背心槽和实际板数；购买背心不自动赠板。</summary>
        public int VestSlots { get; set; }
        public int Plates { get; set; }
        /// <summary>唯一商品身份；伤害按原始值只求值一次。</summary>
        public HashSet<string> Owned { get; set; } = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>未使用充能、活动剩余和未满周期累计秒数。</summary>
        public int StormCharge { get; set; }
        public double StormRemaining { get; set; }
        public double StormTickElapsed { get; set; }
        /// <summary>普通锯和爆桶沿用个人轨道时钟，轮前保存直接保留相位。</summary>
        public double OrdinarySawPhase { get; set; }
        public double OrdinaryBarrelPhase { get; set; }
        /// <summary>购买支援、未启动接力、共享命中冷却与已交付事件，均随轮节点整体克隆。</summary>
        public List<ArcadeSlayerMartEffectInstance> Supports { get; set; } = new List<ArcadeSlayerMartEffectInstance>();
        public List<ArcadeSlayerMartEffectQueue> Queues { get; set; } = new List<ArcadeSlayerMartEffectQueue>();
        public List<ArcadeSlayerMartSupportHit> SupportHits { get; set; } = new List<ArcadeSlayerMartSupportHit>();
        public HashSet<string> AppliedSupportGrants { get; set; } = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>宝石与下一普通轮加命的已消费事件，死亡或跨店不能重新领取。</summary>
        public HashSet<string> AppliedBenefitGrants { get; set; } = new HashSet<string>(StringComparer.Ordinal);
    }
}
