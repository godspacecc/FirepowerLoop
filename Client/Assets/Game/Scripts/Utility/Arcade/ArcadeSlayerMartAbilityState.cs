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
    }
}
