using System;
using System.Collections.Generic;
using System.Linq;

namespace FirepowerLoop.Arcade
{
    /// <summary>商店动作独立于已撤销的旧安全节点购买编号。</summary>
    public enum SlayerMartAction { Buy = 1, Reroll, CollectCash, SetMenuOpen, SetFinished, SelectEnter, SelectSkip }
    /// <summary>只表达本次正式商品效果，不复活旧商品类别。</summary>
    public enum SlayerMartEffect { Bomb = 1, Dash, Tokens, Chicken, Saw, SawBarrel, MagnetMastery, DecoyMastery,
        GlovesMastery, ChickenMastery, SawMastery, DeadOperator, Vest, TacticalVest, Plate, Storm,
        Gems, TeslaPack, BarrelRelay, ChickenPack, RespawnMastery, BarrelMastery, SentryMastery, NextRoundLife }

    /// <summary>纯只读商品参数；由配置适配器一次建立，规则不自行补价格。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerMartItem
    {
        public string Id { get; }
        public SlayerMartEffect Effect { get; }
        public long Price { get; }
        public bool OncePerRun { get; }
        public int Amount { get; }
        public float Duration { get; }
        public ArcadeSlayerMartItem(string id, SlayerMartEffect effect, long price, bool oncePerRun, int amount, float duration)
        { Id=id; Effect=effect; Price=price; OncePerRun=oncePerRun; Amount=amount; Duration=duration; }
    }

    /// <summary>区域经济和时限，箱位置由表现配置另行投影。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerMartRegion
    {
        public int Visit { get; }
        public int AfterRound { get; }
        public int CashBoxes { get; }
        public long CashPerMember { get; }
        public float Duration { get; }
        public ArcadeSlayerMartRegion(int visit,int afterRound,int boxes,long cash,float duration)
        { Visit=visit;AfterRound=afterRound;CashBoxes=boxes;CashPerMember=cash;Duration=duration; }
    }

    /// <summary>双端同源目录；正式开放前必须具有完整24项与两个区域。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerMartCatalog
    {
        public IReadOnlyList<ArcadeSlayerMartItem> Items { get; }
        public IReadOnlyList<ArcadeSlayerMartRegion> Regions { get; }
        public int Cards { get; }
        public int MaxLives { get; }
        public int MaxBombs { get; }
        public int MaxDashes { get; }
        public int MaxChickens { get; }
        public int GemMinimum { get; }
        public int GemMaximum { get; }
        public ArcadeSlayerMartCatalog(IEnumerable<ArcadeSlayerMartItem> items,IEnumerable<ArcadeSlayerMartRegion> regions,
            int cards,int maxLives,int maxBombs,int maxDashes,int maxChickens,int gemMinimum,int gemMaximum)
        {
            Items=Array.AsReadOnly(items.ToArray());Regions=Array.AsReadOnly(regions.ToArray());
            Cards=cards;MaxLives=maxLives;MaxBombs=maxBombs;MaxDashes=maxDashes;MaxChickens=maxChickens;
            GemMinimum=gemMinimum;GemMaximum=gemMaximum;
            if(Items.Count!=24||Items.Select(p=>p.Id).Distinct(StringComparer.Ordinal).Count()!=24 ||
                Items.Select(p=>p.Effect).Distinct().Count()!=24 || Items.Any(p=>string.IsNullOrWhiteSpace(p.Id)||p.Price<=0||p.Amount<=0||float.IsNaN(p.Duration)||float.IsInfinity(p.Duration)||p.Duration<0)||
                Regions.Count!=2||Regions.Select(p=>p.Visit).Distinct().Count()!=2||Regions.Any(p=>p.Visit<1||p.Visit>2||p.AfterRound<=0||p.CashBoxes<=0||p.CashPerMember<=0||float.IsNaN(p.Duration)||float.IsInfinity(p.Duration)||p.Duration<=0)||
                cards!=16||maxLives<1||maxBombs<1||maxDashes<1||maxChickens<3||gemMinimum<1||gemMaximum<gemMinimum)
                throw new ArgumentException("SlayerMart目录缺失或参数无效");
        }
    }

    /// <summary>个人商店工作态；战斗拥有者更新库存能力，克隆后才接受交易结果。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerMartPlayer
    {
        public long PlayerId { get; set; }
        public long Cash { get; set; }
        public int Tokens { get; set; }
        public int RerollSequence { get; set; }
        public int Bombs { get; set; }
        public int Dashes { get; set; }
        public int Lives { get; set; }
        public int VestSlots { get; set; }
        public int Plates { get; set; }
        public int Chickens { get; set; }
        public bool MenuOpen { get; set; }
        public bool Finished { get; set; }
        /// <summary>0未选、1进入、2跳过；不替队友决定。</summary>
        public int Choice { get; set; }
        public List<string> Offers { get; set; } = new List<string>();
        public HashSet<string> Owned { get; set; } = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> Bought { get; set; } = new HashSet<string>(StringComparer.Ordinal);
        public ArcadeSlayerMartPlayer Clone()
        { var p=(ArcadeSlayerMartPlayer)MemberwiseClone();p.Offers=new List<string>(Offers);p.Owned=new HashSet<string>(Owned,StringComparer.Ordinal);p.Bought=new HashSet<string>(Bought,StringComparer.Ordinal);return p; }
    }

    /// <summary>一次已接受操作的稳定回执；同ID异参不重放。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerMartReceipt
    {
        public long PlayerId { get; set; }
        public string OperationId { get; set; }
        public string Fingerprint { get; set; }
        public List<ArcadeSlayerMartGrant> Grants { get; set; } = new List<ArcadeSlayerMartGrant>();
        public ArcadeSlayerMartReceipt Clone()
        { var r=(ArcadeSlayerMartReceipt)MemberwiseClone();r.Grants=Grants.Select(p=>p.Clone()).ToList();return r; }
    }

    /// <summary>购买效果的领域交付，不包含Unity资源；快照恢复不能重复应用重放回执。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerMartGrant
    {
        public string EventId { get; set; }
        public long PlayerId { get; set; }
        public SlayerMartEffect Effect { get; set; }
        public int Amount { get; set; }
        public float Duration { get; set; }
        public ArcadeSlayerMartGrant Clone() => (ArcadeSlayerMartGrant)MemberwiseClone();
    }

    /// <summary>完整区域事务工作态；只有宿主轮前保存成功才成为持久节点。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerMartState
    {
        public int RulesVersion { get; set; } = 1;
        public string RegionInstanceId { get; set; }
        public int Visit { get; set; }
        public int Generation { get; set; }
        public long Revision { get; set; }
        public ulong Seed { get; set; }
        public float RemainingSeconds { get; set; }
        public bool Ready { get; set; }
        public bool Closing { get; set; }
        public bool TeamLifeBought { get; set; }
        public long GrantSequence { get; set; }
        public List<ArcadeSlayerMartPlayer> Players { get; set; } = new List<ArcadeSlayerMartPlayer>();
        public HashSet<int> CollectedBoxes { get; set; } = new HashSet<int>();
        public List<ArcadeSlayerMartReceipt> Receipts { get; set; } = new List<ArcadeSlayerMartReceipt>();
        /// <summary>能力/全队加命交付账本，与扣款一起提交；宿主按EventId幂等消费。</summary>
        public List<ArcadeSlayerMartGrant> PendingGrants { get; set; } = new List<ArcadeSlayerMartGrant>();
        public ArcadeSlayerMartState Clone()
        { var s=(ArcadeSlayerMartState)MemberwiseClone();s.Players=Players.Select(p=>p.Clone()).ToList();s.CollectedBoxes=new HashSet<int>(CollectedBoxes);s.Receipts=Receipts.Select(p=>p.Clone()).ToList();s.PendingGrants=PendingGrants.Select(p=>p.Clone()).ToList();return s; }
    }

    /// <summary>请求不传价格、金额或伤害；Amount只确认资源截断结果。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerMartRequest
    {
        public SlayerMartAction Action { get; set; }
        public long PlayerId { get; set; }
        public string RegionInstanceId { get; set; }
        public int Generation { get; set; }
        public long ExpectedRevision { get; set; }
        public string OperationId { get; set; }
        public string ItemId { get; set; }
        public int BoxId { get; set; }
        public int ConfirmedAmount { get; set; }
        public bool Value { get; set; }
    }

    /// <summary>纯计算返回候选；重放不交付新效果，失败不暴露半成品。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerMartResult
    {
        public bool Success { get; set; }
        public bool Replayed { get; set; }
        public string Error { get; set; }
        public ArcadeSlayerMartState Candidate { get; set; }
    }
}
