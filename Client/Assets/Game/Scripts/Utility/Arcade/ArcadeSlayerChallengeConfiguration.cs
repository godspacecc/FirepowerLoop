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
        public static ArcadeSlayerChallengeConfiguration FromTables(ChallengeTables tables)
        {
            if(tables==null||tables.TbArcadeSlayerChallenge.DataList.Count!=1)throw new ArgumentException("Slayer配置必须恰好一行");
            var r=tables.TbArcadeSlayerChallenge.DataList.Single();var challenge=tables.TbArcadeChallenge.GetOrDefault(r.ChallengeId);
            var drop=tables.TbArcadeDrop.GetOrDefault(r.CoinDropId);
            if(r.Id!="slayer.guardian"||r.AfterArena<1||r.AfterArena>=10||r.LayoutRound<1||r.LayoutRound>4||
                tables.TbArcadeArena.GetOrDefault(r.ArenaConfigNumber)==null||challenge==null||drop==null||drop.Amount<=0||
                tables.TbArcadeRoundLayout.DataList.All(p=>p.ArenaConfigNumber!=r.ArenaConfigNumber||p.RoundNumber!=r.LayoutRound)||
                tables.TbEnemy.GetOrDefault(r.BossEnemyId)==null||r.DownLimit<1||r.CollectionWaveTotal<1||r.CollectionWaveTotal>1000||
                r.MaxCollectionCash<drop.Amount||r.MaxCollectionCash>100000||r.BossBonusCash<0||r.BossBonusCash>100000||
                r.BossHealthPerPlayer<1||r.BossHealthPerPlayer>int.MaxValue/2||r.FireballDamage<1||r.BurnDamage<1||r.BossContactDamage<1)
                throw new ArgumentException("Slayer身份、预算或引用无效");
            foreach(float value in new[]{r.PreparationSeconds,r.CollectionSeconds,r.RespawnSeconds,r.ShopSeconds,r.BossMoveSpeed,
                r.AttackPeriod,r.AttackWarning,r.HammerRadius,r.FireballSpeed,r.FireballRadius,r.BurnSeconds,r.BurnInterval,r.FlameLength,r.FlameRadius})
                if(float.IsNaN(value)||float.IsInfinity(value)||value<=0)throw new ArgumentException("Slayer参数必须为正有限数");
            if(r.AttackWarning>=r.AttackPeriod||r.BurnInterval>r.BurnSeconds||r.CollectionSeconds!=challenge.TimeLimitSeconds)
                throw new ArgumentException("Slayer时钟与挑战配置不一致");
            return new ArcadeSlayerChallengeConfiguration {Id=r.Id,Enabled=r.Enabled,AfterArena=r.AfterArena,ArenaConfigNumber=r.ArenaConfigNumber,LayoutRound=r.LayoutRound,
                ChallengeId=r.ChallengeId,PreparationSeconds=r.PreparationSeconds,CollectionSeconds=r.CollectionSeconds,DownLimit=r.DownLimit,RespawnSeconds=r.RespawnSeconds,
                CollectionWaveTotal=r.CollectionWaveTotal,CoinDropId=r.CoinDropId,CoinAmount=drop.Amount,MaxCollectionCash=r.MaxCollectionCash,BossBonusCash=r.BossBonusCash,
                ShopSeconds=r.ShopSeconds,BossEnemyId=r.BossEnemyId,BossHealthPerPlayer=r.BossHealthPerPlayer,BossMoveSpeed=r.BossMoveSpeed,AttackPeriod=r.AttackPeriod,
                AttackWarning=r.AttackWarning,HammerRadius=r.HammerRadius,FireballSpeed=r.FireballSpeed,FireballDamage=r.FireballDamage,FireballRadius=r.FireballRadius,
                BurnSeconds=r.BurnSeconds,BurnInterval=r.BurnInterval,BurnDamage=r.BurnDamage,FlameLength=r.FlameLength,FlameRadius=r.FlameRadius,BossContactDamage=r.BossContactDamage};
        }
    }
}
