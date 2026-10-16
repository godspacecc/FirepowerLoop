using System;
using System.Collections.Generic;
using System.Linq;

namespace Game
{
    /// <summary>安全节点只承担恢复与准备，数值顺序兼容本项目旧局阶段。</summary>
    public enum ArcadeRunPhase { None, SafeCheckpoint, Arena, Challenge, Victory, Result }
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



    /// <summary>保存目的地、战斗随机种子与挑战恢复状态；不包含商品或交易数据。</summary>
    [Serializable]
    public sealed class ArcadeCheckpointSaveData
    {
        public int ArenaNumber { get; set; } = 1;
        public int GenerationSeed { get; set; }
        /// <summary>当前安全节点可进入的挑战；空值表示本节点没有挑战。</summary>
        public string ChallengeId { get; set; }
        /// <summary>进入挑战前先持久化，确保退出、失败或崩溃不能重复刷取奖励。</summary>
        public bool ChallengeAttempted { get; set; }

        public ArcadeCheckpointSaveData Clone() => new ArcadeCheckpointSaveData
        {
            ArenaNumber = ArenaNumber,
            GenerationSeed = GenerationSeed,
            ChallengeId = ChallengeId,
            ChallengeAttempted = ChallengeAttempted,
        };
    }

    /// <summary>海岛效果的稳定逻辑状态；位置只用于重建锚点，不保留旧地图实例。</summary>
    [Serializable]
    public sealed class ArcadeIslandPowerupCheckpoint
    {
        /// <summary>正式强化配置身份，恢复时重新解析定义。</summary>
        public string ProductId { get; set; }
        /// <summary>尚未消耗的有效秒数。</summary>
        public float RemainingSeconds { get; set; }
        /// <summary>护盾、木桶等剩余触发次数，不重新补满。</summary>
        public int Charges { get; set; }
        /// <summary>下一次周期结算前的剩余秒数。</summary>
        public float CooldownSeconds { get; set; }
        /// <summary>固定效果在目标地图内的合法横向锚点。</summary>
        public float PositionX { get; set; }
        /// <summary>固定效果在目标地图内的合法纵向锚点。</summary>
        public float PositionY { get; set; }
        /// <summary>复制稳定值，快照不共享可变效果实例。</summary>
        public ArcadeIslandPowerupCheckpoint Clone() => (ArcadeIslandPowerupCheckpoint)MemberwiseClone();
    }

    /// <summary>海岛每轮开始前的角色与效果节点；敌人、弹丸、掉落和事件均从本轮固定起点重建。</summary>
    [Serializable]
    public sealed class ArcadeIslandRoundCheckpoint
    {
        /// <summary>一至四轮；第四轮意味着第三轮后的宝藏流程已完成并提交。</summary>
        public int RoundNumber { get; set; }
        /// <summary>稳定地图编号 island.round.N，不使用场景实例标识。</summary>
        public string MapId { get; set; }
        /// <summary>本轮开始前生命，不因换图补满。</summary>
        public int PlayerHealth { get; set; }
        /// <summary>当前角色的生命上限。</summary>
        public int PlayerMaxHealth { get; set; }
        /// <summary>已执行本轮补给后的炸弹库存。</summary>
        public int Bombs { get; set; }
        /// <summary>已执行本轮补给后的冲刺库存。</summary>
        public int Dashes { get; set; }
        /// <summary>本竞技场尚未整场结算的金币，不能重复加入 RunGold。</summary>
        public long ArenaGold { get; set; }
        /// <summary>尚有效的临时枪配置身份；到期后为空。</summary>
        public string TemporaryWeaponId { get; set; }
        /// <summary>临时枪剩余秒数，加载等待期间冻结。</summary>
        public float TemporaryWeaponRemaining { get; set; }
        /// <summary>临时视角剩余秒数，镜头实例在目标地图重建。</summary>
        public float FirstPersonRemaining { get; set; }
        /// <summary>当前武器下一次可发射前的冷却秒数。</summary>
        public float FireCooldown { get; set; }
        /// <summary>角色现有保护剩余秒数。</summary>
        public float InvulnerabilityRemaining { get; set; }
        /// <summary>尚在执行的冲刺剩余秒数。</summary>
        public float DashRemaining { get; set; }
        /// <summary>角色朝向；输入意图不作为持久状态。</summary>
        public float AimDirectionX { get; set; }
        /// <summary>角色朝向纵向分量，不恢复旧移动或持续按压输入。</summary>
        public float AimDirectionY { get; set; }
        /// <summary>每个效果一份稳定逻辑状态，实例跨图重新挂接。</summary>
        public List<ArcadeIslandPowerupCheckpoint> Powerups { get; set; } = new List<ArcadeIslandPowerupCheckpoint>();
        /// <summary>深拷贝效果列表，使候选保存失败不改变原安全节点。</summary>
        public ArcadeIslandRoundCheckpoint Clone()
        {
            var copy = (ArcadeIslandRoundCheckpoint)MemberwiseClone();
            copy.Powerups = Powerups?.Select(value => value?.Clone()).ToList();
            return copy;
        }
    }

    /// <summary>最近安全节点；可选海岛轮级节点兼容版本一旧档，不保存敌人、弹丸或掉落。</summary>
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
        /// <summary>缺失表示旧整场节点，从原第一轮继续；不迁移或删除旧档。</summary>
        public ArcadeIslandRoundCheckpoint IslandRoundCheckpoint { get; set; }
        /// <summary>海岛离场后延续到后续场地的角色与效果；固定 arena.entry，一场金币已经结清。</summary>
        public ArcadeIslandRoundCheckpoint ArenaEntryState { get; set; }
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
        /// <summary>本项目旧局已持有的属性层数；只恢复效果，不再提供购买或新增入口。</summary>
        public Dictionary<string, int> PurchasedUpgradeStacks { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> Consumables { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, string> EquipmentBySlot { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>下场目的地、战斗种子与挑战尝试状态；与交易无关。</summary>
        public ArcadeCheckpointSaveData Checkpoint { get; set; } = new ArcadeCheckpointSaveData();
        /// <summary>仅接受本项目旧节点身份，报价由 JSON 反序列化自动忽略；新文件不再写入 Shop。</summary>
        [Newtonsoft.Json.JsonProperty("Shop")]
        private ArcadeCheckpointSaveData LegacyShop { set { if (value != null) Checkpoint = value; } }

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
            IslandRoundCheckpoint = IslandRoundCheckpoint?.Clone(),
            ArenaEntryState = ArenaEntryState?.Clone(),
            Victory = Victory, Outcome = Outcome,
            RemainingLives = RemainingLives,
            RunGold = RunGold, Score = Score, ScoreMultiplier = ScoreMultiplier, MultiplierProgress = MultiplierProgress,
            EmergencyResources = new Dictionary<string, int>(EmergencyResources ?? new Dictionary<string, int>(), StringComparer.Ordinal),
            PurchasedUpgradeStacks = new Dictionary<string, int>(PurchasedUpgradeStacks ?? new Dictionary<string, int>(), StringComparer.Ordinal),
            Consumables = new Dictionary<string, int>(Consumables ?? new Dictionary<string, int>(), StringComparer.Ordinal),
            EquipmentBySlot = new Dictionary<string, string>(EquipmentBySlot ?? new Dictionary<string, string>(), StringComparer.Ordinal),
            Checkpoint = Checkpoint?.Clone(),
            PendingRewards = PendingRewards?.Select(value => value?.Clone()).ToList() ?? new List<ArcadeRewardSaveData>(),
            UpdatedAtUtc = UpdatedAtUtc
        };
    }

    /// <summary>战斗模块完成整座竞技场后提交的稳定结果；瞬时战斗对象不跨越此边界。</summary>
    public sealed class ArcadeArenaCompletion
    {
        /// <summary>下场角色与效果入场状态；金币必须清零，胜利结果不保留。</summary>
        public ArcadeIslandRoundCheckpoint PlayerState { get; set; }
        public int ArenaNumber { get; set; }
        public long EarnedGold { get; set; }
        public long Score { get; set; }
        public int ScoreMultiplier { get; set; } = 1;
        public int MultiplierProgress { get; set; }
        public IReadOnlyDictionary<string, int> EmergencyResources { get; set; }
        public IReadOnlyDictionary<string, string> EquipmentBySlot { get; set; }
        public IReadOnlyList<ArcadeRewardSaveData> EarnedRewards { get; set; }
        public ArcadeCheckpointSaveData NextCheckpoint { get; set; }
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
        /// <summary>单人海岛地图与转场状态；联机及其他主题保持空值。</summary>
        public string IslandMapNameKey { get; set; }
        /// <summary>资源或轮级节点尚未准备完成，界面显示等待或重试。</summary>
        public bool IslandTransitioning { get; set; }
        /// <summary>普通海岛轮已清场，允许拾取并等待单出口确认。</summary>
        public bool IslandAwaitingRoundExit { get; set; }
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
