using System;
using System.Collections.Generic;
using System.Linq;

namespace Game
{
    public enum ArcadeRunPhase { None, SafeShop, Arena, Challenge, Victory, Result }
    public enum ArcadePurchaseKind { Upgrade, ExtraLife, Consumable, Equipment }
    public enum ArcadeDeathResult { Rejected, Respawn, RunFailed }
    public enum ArcadeHudActionKind { Fire, Bomb, Dash, RetryCompletion, ExitFirstPerson }

    /// <summary>街机模式固定开局合同；不读取基地角色、装备、弹药、货币或背包。</summary>
    public static class ArcadeRunDefaults
    {
        public const int InitialLives = 3;
        public const string WeaponSlotId = "weapon";
        public const string SupportSlotId = "support";
        public const string BasicWeaponId = "arcade.weapon.basic";
        /// <summary>街机未指定独立弹体的枪械使用基础子弹；只选择表现，不读取其速度或射程。</summary>
        public const string ProjectileConfigId = "projectile.bullet.basic";
        public const string DropPresentationLootConfigId = "loot.metal-parts";
        public const string BombResourceId = "arcade.resource.bomb";
        public const string DashResourceId = "arcade.resource.dash";
    }

    public static class ArcadeRunIdentity
    {
        public static bool IsValid(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.Length == 32 && value.All(Uri.IsHexDigit);
    }

    [Serializable]
    public sealed class ArcadeRewardSaveData
    {
        public string RewardId { get; set; }
        public long Amount { get; set; }
        public ArcadeRewardSaveData Clone() => new ArcadeRewardSaveData { RewardId = RewardId, Amount = Amount };
    }

    [Serializable]
    public sealed class ArcadeShopOfferSaveData
    {
        public string OfferId { get; set; }
        public string ProductId { get; set; }
        public ArcadePurchaseKind Kind { get; set; }
        public long Price { get; set; }
        public int Amount { get; set; } = 1;
        public string EquipmentSlotId { get; set; }
        public bool Purchased { get; set; }

        public ArcadeShopOfferSaveData Clone() => new ArcadeShopOfferSaveData
        {
            OfferId = OfferId,
            ProductId = ProductId,
            Kind = Kind,
            Price = Price,
            Amount = Amount,
            EquipmentSlotId = EquipmentSlotId,
            Purchased = Purchased
        };
    }

    /// <summary>保存可确定重建同一商店的种子、刷新次数及实际报价，不依赖随机数实现细节。</summary>
    [Serializable]
    public sealed class ArcadeShopSaveData
    {
        public int ArenaNumber { get; set; } = 1;
        public int GenerationSeed { get; set; }
        public int RefreshCount { get; set; }
        /// <summary>当前安全商店可进入的挑战；空值表示本节点没有挑战。</summary>
        public string ChallengeId { get; set; }
        /// <summary>进入挑战前先持久化，确保退出、失败或崩溃不能重复刷取奖励。</summary>
        public bool ChallengeAttempted { get; set; }
        public List<ArcadeShopOfferSaveData> Offers { get; set; } = new List<ArcadeShopOfferSaveData>();

        public ArcadeShopSaveData Clone() => new ArcadeShopSaveData
        {
            ArenaNumber = ArenaNumber,
            GenerationSeed = GenerationSeed,
            RefreshCount = RefreshCount,
            ChallengeId = ChallengeId,
            ChallengeAttempted = ChallengeAttempted,
            Offers = Offers?.Select(value => value?.Clone()).ToList() ?? new List<ArcadeShopOfferSaveData>()
        };
    }

    /// <summary>
    /// 只表示最近一次安全商店提交点；不保存位置、敌人、投射物、掉落或未完成竞技场进度。
    /// </summary>
    [Serializable]
    public sealed class ArcadeRunSaveData
    {
        public const int CurrentVersion = 1;
        public int Version { get; set; } = CurrentVersion;
        public string RunId { get; set; }
        public int CompletedArenaCount { get; set; }
        public int NextArenaNumber { get; set; } = 1;
        /// <summary>新局使用休闲路线；旧文件缺失本字段时保留原顺序循环，避免猜测旧局已走路线。</summary>
        public bool IsCasualRoute { get; set; }
        /// <summary>稳定场地配置编号，独立于本局场序；旧顺序局为零。</summary>
        public int ArenaConfigNumber { get; set; }
        /// <summary>最终 Boss 胜利出口提交后的可重试结算节点；该节点不可继续战斗。</summary>
        public bool Victory { get; set; }
        /// <summary>空值代表可继续本局；victory/failed/abandoned 是待确认结果，不能进入战斗。</summary>
        public string Outcome { get; set; }
        public bool HasResult => Victory || !string.IsNullOrEmpty(Outcome);
        public int RemainingLives { get; set; } = ArcadeRunDefaults.InitialLives;
        public long RunGold { get; set; }
        /// <summary>本局个人累计积分，不是购物余额；随整场安全节点保存。</summary>
        public long Score { get; set; }
        /// <summary>领取前计分倍率；旧档缺省为 1，上限来自房间配置。</summary>
        public int ScoreMultiplier { get; set; } = 1;
        /// <summary>当前倍率累计进度，达到配置阈值升倍；死亡清零。</summary>
        public int MultiplierProgress { get; set; }
        public Dictionary<string, int> EmergencyResources { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>强化 ID 到已购买层数；层数上限来自 Luban ArcadeUpgrade 配置。</summary>
        public Dictionary<string, int> PurchasedUpgradeStacks { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> Consumables { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, string> EquipmentBySlot { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public ArcadeShopSaveData Shop { get; set; } = new ArcadeShopSaveData();
        public List<ArcadeRewardSaveData> PendingRewards { get; set; } = new List<ArcadeRewardSaveData>();
        public string UpdatedAtUtc { get; set; }

        public ArcadeRunSaveData Clone() => new ArcadeRunSaveData
        {
            Version = Version,
            RunId = RunId,
            CompletedArenaCount = CompletedArenaCount,
            NextArenaNumber = NextArenaNumber,
            IsCasualRoute = IsCasualRoute,
            ArenaConfigNumber = ArenaConfigNumber,
            Victory = Victory, Outcome = Outcome,
            RemainingLives = RemainingLives,
            RunGold = RunGold, Score = Score, ScoreMultiplier = ScoreMultiplier, MultiplierProgress = MultiplierProgress,
            EmergencyResources = new Dictionary<string, int>(EmergencyResources ?? new Dictionary<string, int>(), StringComparer.Ordinal),
            PurchasedUpgradeStacks = new Dictionary<string, int>(PurchasedUpgradeStacks ?? new Dictionary<string, int>(), StringComparer.Ordinal),
            Consumables = new Dictionary<string, int>(Consumables ?? new Dictionary<string, int>(), StringComparer.Ordinal),
            EquipmentBySlot = new Dictionary<string, string>(EquipmentBySlot ?? new Dictionary<string, string>(), StringComparer.Ordinal),
            Shop = Shop?.Clone(),
            PendingRewards = PendingRewards?.Select(value => value?.Clone()).ToList() ?? new List<ArcadeRewardSaveData>(),
            UpdatedAtUtc = UpdatedAtUtc
        };
    }

    /// <summary>战斗模块完成整座竞技场后提交的稳定结果；瞬时战斗对象不跨越此边界。</summary>
    public sealed class ArcadeArenaCompletion
    {
        public int ArenaNumber { get; set; }
        public long EarnedGold { get; set; }
        public long Score { get; set; }
        public int ScoreMultiplier { get; set; } = 1;
        public int MultiplierProgress { get; set; }
        public IReadOnlyDictionary<string, int> EmergencyResources { get; set; }
        public IReadOnlyDictionary<string, string> EquipmentBySlot { get; set; }
        public IReadOnlyList<ArcadeRewardSaveData> EarnedRewards { get; set; }
        public ArcadeShopSaveData NextShop { get; set; }
        /// <summary>从当前有效出口选择的配置编号；胜利时为零。</summary>
        public int NextArenaConfigNumber { get; set; }
        public bool Victory { get; set; }
    }

    public readonly struct ArcadeRunChangedEvent
    {
        public ArcadeRunChangedEvent(ArcadeRunPhase phase, ArcadeRunSaveData snapshot)
        {
            Phase = phase;
            Snapshot = snapshot?.Clone();
        }

        public ArcadeRunPhase Phase { get; }
        public ArcadeRunSaveData Snapshot { get; }
    }

    public readonly struct ArcadePowerupHudEntry
    {
        public ArcadePowerupHudEntry(string nameKey, float seconds, int charges)
        {
            NameKey = nameKey ?? string.Empty;
            Seconds = seconds;
            Charges = charges;
        }

        public string NameKey { get; }
        public float Seconds { get; }
        public int Charges { get; }
    }

    /// <summary>战斗模块发布给 HUD 的瞬时显示数据；该快照永不进入安全节点存档。</summary>
    public sealed class ArcadeCombatHudSnapshot
    {
        public int ArenaNumber { get; set; }
        public int WaveNumber { get; set; }
        public int TotalWaves { get; set; }
        public int RemainingEnemies { get; set; }
        public string TemporaryWeaponNameKey { get; set; }
        public float TemporaryWeaponSeconds { get; set; }
        /// <summary>单人临时第一人称剩余秒数；联机快照保持默认零。</summary>
        public float FirstPersonSeconds { get; set; }
        public ArcadePowerupHudEntry[] Powerups { get; set; } = Array.Empty<ArcadePowerupHudEntry>();
        public int Bombs { get; set; }
        public int Dashes { get; set; }
        public long ArenaGold { get; set; }
        public long Score { get; set; }
        public int ScoreMultiplier { get; set; } = 1;
        public bool InTreasureRoom { get; set; }
        public float TreasureSeconds { get; set; }
        public int PlayerHealth { get; set; }
        public int PlayerMaxHealth { get; set; }
        public float RespawnSeconds { get; set; }
        public bool CanRetryCompletion { get; set; }
        public bool IsChallenge { get; set; }
        public bool IsFinalBoss { get; set; }
        public bool AwaitingExit { get; set; }
        public bool Resting { get; set; }
        public float RestSeconds { get; set; }
        public float ChallengeSeconds { get; set; }
        public ArcadeCombatHudSnapshot Clone()
        {
            var clone = (ArcadeCombatHudSnapshot)MemberwiseClone();
            clone.Powerups = Powerups == null ? Array.Empty<ArcadePowerupHudEntry>() :
                (ArcadePowerupHudEntry[])Powerups.Clone();
            return clone;
        }
    }

    public readonly struct ArcadeCombatHudChangedEvent
    {
        public ArcadeCombatHudChangedEvent(ArcadeCombatHudSnapshot snapshot) => Snapshot = snapshot?.Clone();
        public ArcadeCombatHudSnapshot Snapshot { get; }
    }

    /// <summary>HUD 提交给战斗模块的输入意图；资源是否可用及实际消耗由战斗模块裁决。</summary>
    public readonly struct ArcadeHudActionRequestedEvent
    {
        public ArcadeHudActionRequestedEvent(ArcadeHudActionKind action, float x = 0f, float y = 0f)
        {
            Action = action;
            X = x;
            Y = y;
        }

        public ArcadeHudActionKind Action { get; }
        public float X { get; }
        public float Y { get; }
    }
}
