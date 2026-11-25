using System;
using System.Linq;
#if DOTNET
using ChallengeTables=ET.Tables;
#else
using ChallengeTables=global::Tables;
#endif

namespace FirepowerLoop.Arcade
{
    /// <summary>首批Slayer只读规则与Boss参数，双端仅从正式Config构造，不内嵌运行默认值。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerChallengeConfiguration
    {
        public string Id {get;private set;}
        public bool Enabled {get;private set;}
        public int AfterArena {get;private set;}
        public int ArenaConfigNumber {get;private set;}
        public int LayoutRound {get;private set;}
        public string ChallengeId {get;private set;}
        public float PreparationSeconds {get;private set;}
        public float CollectionSeconds {get;private set;}
        public int DownLimit {get;private set;}
        public float RespawnSeconds {get;private set;}
        public int CollectionWaveTotal {get;private set;}
        public string CoinDropId {get;private set;}
        public int CoinAmount {get;private set;}
        public long MaxCollectionCash {get;private set;}
        public long BossBonusCash {get;private set;}
        public float ShopSeconds {get;private set;}
        public string BossEnemyId {get;private set;}
        public int BossHealthPerPlayer {get;private set;}
        public float BossMoveSpeed {get;private set;}
        public float AttackPeriod {get;private set;}
        public float AttackWarning {get;private set;}
        public float HammerRadius {get;private set;}
        public float FireballSpeed {get;private set;}
        public int FireballDamage {get;private set;}
        public float FireballRadius {get;private set;}
        public float BurnSeconds {get;private set;}
        public float BurnInterval {get;private set;}
        public int BurnDamage {get;private set;}
        public float FlameLength {get;private set;}
        public float FlameRadius {get;private set;}
        public int BossContactDamage {get;private set;}

        /// <summary>启动时完整校验唯一挑战及跨表引用，配置坏则拒绝，不静默关闭或回退。</summary>
    }
}
