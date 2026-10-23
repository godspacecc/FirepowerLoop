using System.Collections.Generic;

namespace ET.Server
{
    /// <summary>当前地图内的捐命回执；随战场销毁，不生成额外中途存档。</summary>
    [EnableClass]
    public sealed class OnlineArcadeDonationReceipt
    {
        public long TargetPlayerId;
        public bool Success;
    }

    public enum OnlineArcadeRunPhase
    {
        SafeCheckpoint = 1,
        Arena = 2,
        Challenge = 3,
        Victory = 4,
    }

    /// <summary>Boss 技能现场阶段；数值与客户端 ArcadeBossPhase 及外部协议保持一致。</summary>
    public enum OnlineArcadeBossPhase
    {
        None = 0,
        Telegraph = 1,
        Charging = 2,
        RayTelegraph = 3,
    }

    /// <summary>安全节点写操作类型；协议只传递数值，权威校验由街机 Actor 完成。</summary>
    public enum OnlineArcadeSafeCheckpointActionKind
    {
        SetReady = 3,
        ConfirmSettlement = 4,
        ConfirmAbandon = 5,
        SetChallengeReady = 7,
        SetSlayerExit = 8,
    }



    /// <summary>单名原始成员的局内安全节点；双方资源和准备状态始终独立。</summary>
    [EnableClass]
    public sealed class OnlineArcadePlayerRecord
    {
        public long PlayerId { get; set; }
        public string DisplayName { get; set; }
        public int Lives { get; set; }
        public int RunGold { get; set; }
        /// <summary>个人累计积分及倍率进度，随整场成功提交，旧档缺省 0/1/0。</summary>
        public long Score { get; set; }
        public int ScoreMultiplier { get; set; } = 1;
        public int MultiplierProgress { get; set; }
        public int Bombs { get; set; }
        public int Dashes { get; set; }
        public string WeaponId { get; set; }
        /// <summary>轮前快照完整角色状态，仅保存逻辑剩余量，不保存战场实体。</summary>
        public int RoundHealth { get; set; }
        public bool RoundAlive { get; set; } = true;
        /// <summary>保护、复活、冲刺与攻击冷却只保存剩余秒数，切轮不刷新或丢失。</summary>
        public float RoundInvulnerabilitySeconds { get; set; }
        public float RoundRespawnSeconds { get; set; }
        /// <summary>零命恢复资格及独立倒地剩余秒数；不借用短复活。</summary>
        public bool CoopRescueEligible { get; set; }
        public float RoundDownedSeconds { get; set; }
        public float RoundDashSeconds { get; set; }
        /// <summary>轮前保留未结束冲刺的锁定方向，避免恢复后跟随新输入。</summary>
        public float RoundDashDirectionX { get; set; }
        public float RoundDashDirectionZ { get; set; }
        public float RoundFireCooldownSeconds { get; set; }
        public string RoundTemporaryWeaponId { get; set; } = string.Empty;
        public float RoundTemporaryWeaponSeconds { get; set; }
        public List<OnlineArcadePowerupRuntime> RoundPowerups { get; set; } = new();
        public bool Ready { get; set; }
        /// <summary>是否已确认进入本安全节点的特殊挑战；与普通开战准备互斥。</summary>
        public bool ChallengeReady { get; set; }
        public Dictionary<string, int> UpgradeStacks { get; set; } = new();
        public Dictionary<string, int> Consumables { get; set; } = new();
        public Dictionary<string, string> EquipmentBySlot { get; set; } = new();
        public bool SettlementConfirmed { get; set; }
        public bool AbandonConfirmed { get; set; }
        /// <summary>0=尚未确认本局结果，1=本局结果已成功保存。</summary>
        public int SettlementState { get; set; }
    }

    /// <summary>已经成功落盘的可重试操作身份；相同 ID 只能重放完全相同的参数。</summary>
    [EnableClass]
    public sealed class OnlineArcadeOperationRecord
    {
        public string OperationId { get; set; }
        public long PlayerId { get; set; }
        public int ActionKind { get; set; }
        public string Fingerprint { get; set; }
        public string CompletedAtUtc { get; set; }
    }

    /// <summary>一局联机街机的持久化安全节点，不包含任何竞技场瞬时实体或位置。</summary>
    [EnableClass]
    public sealed class OnlineArcadeRunRecord
    {
        /// <summary>新商店仅对明确启用的新局开放，旧记录缺省0。</summary>
        public int SlayerMartRulesVersion { get; set; }
        /// <summary>已提交轮前的全员商店账本，不与战斗现场共用对象。</summary>
        public FirepowerLoop.Arcade.ArcadeSlayerMartState SlayerMart { get; set; }
        public long RunId { get; set; }
        public long RoomId { get; set; }
        public long Revision { get; set; }
        public OnlineArcadeRunPhase Phase { get; set; }
        public int CompletedArena { get; set; }
        public int NextArena { get; set; }
        /// <summary>新局使用十场休闲分支；旧档缺省为 false，保留旧推进方式。</summary>
        public bool IsCasualRoute { get; set; }
        /// <summary>已选场地配置身份；与 NextArena 主线序号分离。</summary>
        public int ArenaConfigNumber { get; set; }
        /// <summary>本安全节点可用的挑战 ID；空字符串表示没有挑战入口。</summary>
        /// <summary>已提交轮前节点；空 ID 表示旧整场节点，版本不匹配拒绝恢复。</summary>
        public string RoundLayoutId { get; set; } = string.Empty;
        public int RoundLayoutVersion { get; set; }
        public int RoundNumber { get; set; } = 1;
        public int RoundGeneration { get; set; }
        public bool RoundBossEncounter { get; set; }
        public List<int> DestroyedCrateIds { get; set; } = new();
        /// <summary>仍存活箱子的轮前耐久；旧节点缺省空字典保持兼容。</summary>
        public Dictionary<int,int> CrateHealth { get; set; } = new();
        /// <summary>同一轮地图版本内已销毁的预置桶零基索引，只随全员轮前事务保存。</summary>
        public List<int> DestroyedBarrelIds { get; set; } = new();
        public string ChallengeId { get; set; }
        /// <summary>进入挑战前即持久化，确保失败、断线或重启后不能重复刷取。</summary>
        public bool ChallengeAttempted { get; set; }
        public string ClientVersion { get; set; }
        public string ConfigFingerprint { get; set; }
        public string CreatedAtUtc { get; set; }
        public string UpdatedAtUtc { get; set; }
        public List<OnlineArcadePlayerRecord> Players { get; set; } = new();
        public List<OnlineArcadeOperationRecord> Operations { get; set; } = new();
    }

    [EnableClass]
    public sealed class OnlineArcadeRunStoreSnapshot
    {
        public int SchemaVersion { get; set; } = 2;
        public List<OnlineArcadeRunRecord> Runs { get; set; } = new();
    }

    /// <summary>竞技场内单名玩家的服务器权威瞬时状态；退出现场后全部丢弃。</summary>
    [EnableClass]
    public sealed class OnlineArcadePowerupRuntime
    {
        [System.Text.Json.Serialization.JsonInclude]
        public string ProductId = string.Empty;
        [System.Text.Json.Serialization.JsonInclude]
        public ArcadePowerupBehavior Behavior;
        [System.Text.Json.Serialization.JsonInclude]
        public float Magnitude;
        [System.Text.Json.Serialization.JsonInclude]
        public int Damage;
        [System.Text.Json.Serialization.JsonInclude]
        public float IntervalSeconds;
        [System.Text.Json.Serialization.JsonInclude]
        public float Radius;
        [System.Text.Json.Serialization.JsonInclude]
        public float ProjectileSpeed;
        [System.Text.Json.Serialization.JsonInclude]
        public float RemainingSeconds;
        [System.Text.Json.Serialization.JsonInclude]
        public int Charges;
        [System.Text.Json.Serialization.JsonInclude]
        public float CooldownSeconds;
        public float PositionX;
        public float PositionZ;
        /// <summary>最后一次成功发射的竞技场单位方向；未开火时为零，不写入安全节点。</summary>
        public float ShotDirectionX;
        public float ShotDirectionZ;
        /// <summary>仅当前场次的无人机状态，不序列化目标；恢复轮前节点后按剩余时长重新跟随。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public FirepowerLoop.Arcade.ArcadeDroneState Drone;
        public int DronePowerTier=1;
    }

    [EnableClass]
    public sealed class OnlineArcadeBattlePlayerRuntime
    {
        /// <summary>本次保底房已进入过，防止提前离开的成员被自动送回。</summary>
        /// <summary>轮代次加载确认、待仲裁出口及冰面速度，均为现场状态。</summary>
        public int ReadyRoundGeneration;
        public int ConfirmedExitTarget = int.MinValue;
        public float IceVelocityX, IceVelocityZ, ContentSlowRemaining, ContentBumperCooldown;
        public bool TreasureVisited;
        /// <summary>本帧移动前的场地坐标，只用于机关连续接触判定。</summary>
        public float HazardPreviousX;
        public float HazardPreviousZ;
        public long PlayerId;
        public float PositionX;
        public float PositionZ;
        public float AimX;
        public float AimZ = 1f;
        public int CurrentHealth;
        public int MaxHealth;
        public bool Alive = true;
        /// <summary>当前房间区域身份，仅在本场现场有效。</summary>
        public bool InTreasureRoom;
        public float MoveX;
        public float MoveZ;
        public bool Firing;
        /// <summary>清场松开后再次射击才能主动推进；不持久化到安全节点。</summary>
        public bool RestAttackReleased;
        public float CampfireCooldown;
        public float FireCooldownSeconds;
        /// <summary>本场实际成功攻击后递增的表现序列；空挥也递增，冷却拒绝不递增。</summary>
        public int FireSequence;
        /// <summary>受击后的剩余无敌秒数；复活时使用规则配置的双倍时长。</summary>
        public float InvulnerabilitySeconds;
        /// <summary>大于零时玩家保持死亡表现，归零后在死亡原地恢复。</summary>
        public float RespawnSeconds;
        /// <summary>零命合作倒地时钟，独立于短复活和机关冻结。</summary>
        public float DownedSeconds;
        /// <summary>单调捐命操作序号及最近回执；旧序号被淘汰后也不能再次扣命。</summary>
        public long LastDonationOperation;
        public SortedDictionary<long, OnlineArcadeDonationReceipt> DonationReceipts = new();
        /// <summary>当前竞技场临时武器及剩余秒数；离开现场即丢弃，不写安全节点。</summary>
        public string TemporaryWeaponId = string.Empty;
        public float TemporaryWeaponSeconds;
        /// <summary>当前竞技场并存强化；按行为去重，同类重复拾取只刷新瞬时状态。</summary>
        public Dictionary<ArcadePowerupBehavior, OnlineArcadePowerupRuntime> Powerups { get; } = new();
        /// <summary>从安全节点带入本场的支援装备产品 ID；只负责快照同步，不承担装备持久化。</summary>
        public string SupportId = string.Empty;
        /// <summary>权威冲刺剩余秒数；大于零时提高移动速度，并由无敌计时覆盖受击保护。</summary>
        public float DashSeconds;
        /// <summary>开始冲刺时锁定的单位方向；两名玩家各自持有，由服务器同步给预测端。</summary>
        public float DashDirectionX;
        public float DashDirectionZ;
        public long LastInputSequence;
        public long LastInputAt;
    }

    /// <summary>竞技场敌人瞬时状态；RuntimeId 只在本次现场生命周期内稳定。</summary>
    [EnableClass]
    public sealed class OnlineArcadeEnemyRuntime
    {
        /// <summary>真实喷火命中后的纯表现时钟，不参与持续伤害或存档。</summary>
        public float FlameHitRemainingSeconds;
        /// <summary>最后一次实际伤害的火焰来源，用于本轮死亡事实。</summary>
        public bool LastDamageWasFlame;
        /// <summary>最近一次实际玩家伤害归属，零为清理/无玩家；死亡后随瞬时敌人丢弃。</summary>
        /// <summary>主题身份及受击冻结时刻，只属于当前地图。</summary>
        public int SpecialKind, ContentBossKind;
        /// <summary>地图活动时钟上的出生时刻；受控停步不暂停携桶存活倒计时。</summary>
        public double ContentBornAt;
        public double ContentElapsed, LastContentHitAt = -1, ContentStunUntil;
        public int ContentMannequinHits;
        public bool ContentEvolved;
        public long ContentTelegraphCycle = -1;
        public float ContentLockedX, ContentLockedZ, ContentLockedTargetX, ContentLockedTargetY;
        public bool ContentWarning;
        public long LastContentAttackCycle = -1;
        public long LastDamagePlayerId;
        /// <summary>当前轮护甲耐久，不进入安全存档；协议只读投影供破甲表现。</summary>
        public int ArmorHealth;
        public float HazardPreviousX;
        public float HazardPreviousZ;
        public long RuntimeId;
        public string EnemyConfigId;
        /// <summary>出生时由敌人配置固定的禁紫资格，不随关卡和生命成长解除。</summary>
        public bool BlocksPurpleSkullDrop;
        public float PositionX;
        public float PositionZ;
        public int CurrentHealth;
        public int MaxHealth;
        public int Damage;
        public float MoveSpeed;
        public EnemyAttackKind AttackKind;
        public float AttackRange;
        public float AttackIntervalSeconds;
        public float AttackCooldownSeconds;
        public string ProjectileConfigId;
        public long TargetPlayerId;
        public int WaveNumber;
        public string BossId = string.Empty;
        public OnlineArcadeBossPhase BossPhase;
        public float BossPhaseSeconds;
        public float BossSkillCooldownSeconds;
        public float BossRayCooldownSeconds;
        public float BossChargeDirectionX;
        public float BossChargeDirectionZ;
        /// <summary>一次冲锋内已经命中的玩家；冲锋结束时清空。</summary>
        public HashSet<long> BossHitPlayers { get; } = new();
    }

    /// <summary>玩家投射物的服务器权威瞬时状态；命中或寿命结束后立即移除。</summary>
    [EnableClass]
    public sealed class OnlineArcadeProjectileRuntime
    {
        /// <summary>购买支援的稳定实例来源，不复用普通武器标记。</summary>
        public string SupportInstanceId;
        public long RuntimeId;
        /// <summary>主题追踪弹的限幅转向速度，零为原有直线弹。</summary>
        public float TrackingTurnRate, ContentSlowSeconds;
        /// <summary>由玩家武器Fire实际创建，支援或敌弹不能借玩家归属引爆地图桶。</summary>
        public bool FromPlayerWeapon;
        /// <summary>创建弹丸时固定武器身份，供双端表现选择火焰等独立外观。</summary>
        public string SourceWeaponId;
        /// <summary>开枪时的玩家位置；仅用于服务端近距死亡分类，不需要下发弹丸快照。</summary>
        public float SourcePositionX, SourcePositionZ;
        /// <summary>发射时确定的头骨层级；仅同步该弹丸外观，不从玩家当前状态推断。</summary>
        public int WeaponPowerLevel;
        /// <summary>发射时固定的紫射线反射余量，仅当前地图。</summary>
        public int RemainingReflections;
        /// <summary>紫火箭锁定的敌人身份，仅权威现场持有，不写安全节点。</summary>
        public long TrackingEnemyId;
        public long OwnerPlayerId;
        public float PositionX;
        public float PositionZ;
        public float VelocityX;
        public float VelocityZ;
        public int Damage;
        public float RemainingSeconds;
        public ArcadeWeaponBehavior Behavior;
        public int RemainingHits;
        public float ExplosionRadius;
        /// <summary>贯穿弹已经结算过的稳定敌人 ID；弹丸销毁时随瞬时对象释放。</summary>
        public HashSet<long> HitEnemyIds { get; } = new();
    }

    /// <summary>全队共享且唯一的现场掉落实体；首名满足拾取判定的玩家获得后立即删除。</summary>
    [EnableClass]
    public sealed class OnlineArcadeDropRuntime
    {
        /// <summary>固定地图刷枪使用权威空投时钟，普通战利品保持即时拾取。</summary>
        public bool WeaponAirdrop;
        public float DeliveryRemainingSeconds;
        public long RuntimeId;
        public string DropConfigId;
        public float PositionX;
        public float PositionZ;
        /// <summary>现场掉落剩余寿命，只随活动战斗推进；到期直接移除，不发放奖励。</summary>
        public float RemainingSeconds = 30f;
        /// <summary>成功拾取记录的领取者；可见掉落为零，不写安全存档。</summary>
        public long CollectedByPlayerId;
        /// <summary>本次实际增加次数；仅成功补给记录为正。</summary>
        public int AddedAmount;
        /// <summary>成功领取的真实积分及金币变化；不把积分当作购物金币。</summary>
        public long ScoreAdded;
        public int GoldAdded;
    }

    /// <summary>死亡毒云等地图局部领域，只有权威现场拥有寿命与接触账本。</summary>
    [EnableClass]
    public sealed class OnlineArcadeContentZoneRuntime
    {
        public int Id, Kind;
        public float X, Y, EndX, EndY, Radius, Remaining, WarningRemaining, DamageRatio;
        public bool Segment, EnemyDamage;
        public readonly FirepowerLoop.Arcade.ArcadeContentClock Clock = new();
    }

    /// <summary>不落盘的服务器权威战斗现场；保存与正式奖励仍由安全节点事务负责。</summary>
    [EnableClass]
    public sealed class OnlineArcadeBattleRuntime
    {
        /// <summary>仅当前轮拥有的奖励现场，不进入轮前存档。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public FirepowerLoop.Arcade.ArcadeRoundRewardState RoundReward;
        /// <summary>整关清场后中央出口的活动时钟和接触状态。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public FirepowerLoop.Arcade.ArcadeStagePortalState StagePortal;
        /// <summary>本局新商店版本和全员内存事务，成功轮前保存后才公开提交。</summary>
        public int SlayerMartRulesVersion;
        public FirepowerLoop.Arcade.ArcadeSlayerMartState SlayerMart;
        /// <summary>商店现场返回目标与区域准备状态，进店和购买不单独保存。</summary>
        public int SlayerMartReturnWave;
        public bool SlayerMartRegionPrepared;
        /// <summary>服务器购物硬截止及断线暂停起点，只属于现场，不写入安全节点。</summary>
        public long SlayerMartDeadlineAt;
        public long SlayerMartPausedAt;
        /// <summary>同步风暴结算作用域，阻止自身或连锁击杀给风暴充能；不保存。</summary>
        public bool SlayerMartStormDamage;
        /// <summary>轮地图候选由管理器原子提交；加载与保存失败时冻结活动时钟。</summary>
        public FirepowerLoop.Arcade.ArcadeRoundLayoutParameters RoundLayout;
        /// <summary>当前地图的权威武器刷新时钟；候选现场替换后旧计划随之释放，不同步给客户端计时。</summary>
        public FirepowerLoop.Arcade.ArcadeWeaponSpawnSchedule WeaponSpawns;
        /// <summary>服务器独占的半批重叠调度，各通道共享整轮预算和存活上限。</summary>
        public FirepowerLoop.Arcade.ArcadePassageSchedule IslandPassages;
        /// <summary>最近推进通道计划的权威毫秒；暂停和加载时重置锚点，避免补算冻结时间。</summary>
        public long PassageSpawnTickAt;
        public int RoundGeneration;
        public bool RoundTransitionPending;
        public bool PendingBossEncounter;
        public string RoundLoadError = string.Empty;
        public bool RoundBossEncounter;
        public bool RoundBossDefeated;
        public long ContentEventCycle = -1, ContentEventTargetCycle = -1;
        public float ContentEventTargetX, ContentEventTargetY;
        public readonly FirepowerLoop.Arcade.ArcadeContentClock ContentClock = new();
        public readonly List<FirepowerLoop.Arcade.ArcadeContentAreaSnapshot> ContentAreas = new();
        /// <summary>不参与伤害判定的瞬时电弧，权威活动帧扣除余量。</summary>
        public readonly List<FirepowerLoop.Arcade.ArcadeRayArcState> PurpleRayArcs=new();
        /// <summary>地图现场拥有的牛群与活动倒计时，轮前保存不包含半次冲撞。</summary>
        public readonly List<FirepowerLoop.Arcade.ArcadeHerdState> HerdCows=new();
        public bool HerdInitialized;
        public float HerdUntilNext;
        public int HerdBatch;
        public readonly List<OnlineArcadeContentZoneRuntime> DeathZones = new();
        public readonly HashSet<int> DestroyedCrateIds = new();
        public readonly HashSet<int> DestroyedBarrelIds = new();
        /// <summary>载体放下的独立桶与所有桶的剩余耐久；仅属于本地图，不写入轮前节点。</summary>
        public readonly Dictionary<int,FirepowerLoop.Arcade.ArcadeRegionPoint> DroppedBarrels = new();
        public readonly Dictionary<int,int> GroundBarrelHealth = new();
        public int NextGroundBarrelId;
        public bool CratesInitialized;
        public readonly Dictionary<int, FirepowerLoop.Arcade.ArcadeCrateState> Crates = new();
        /// <summary>只随活动战斗增长的机关秒数；不进入安全存档，快照供客户端显示。</summary>
        public double HazardElapsedSeconds;
        public List<FirepowerLoop.Arcade.ArcadeHazardState> Hazards { get; } = new();
        public int ArenaNumber;
        public int ArenaConfigNumber;
        /// <summary>随本场运行局确定；旧安全档不强制切换到新休闲路线。</summary>
        public bool IsCasualRoute;
        /// <summary>独立于四轮主线的临时阶段，安全节点不保存现场进度。</summary>
        public bool IsFinalBoss;
        /// <summary>最终 Boss 的稳定实体 ID；零表示尚未生成，炸弹不能清除该实体。</summary>
        public long FinalBossEnemyId;
        public bool FinalBossDefeated;
        public bool AwaitingExit;
        public int SelectedExitConfigNumber;
        public float ReinforcementCooldown;
        public int WaveIndex;
        public int WaveCount;
        public int DifficultyPlayerCount;
        public int SpawnedInWave;
        public int TotalInWave;
        public int MaxAlive;
        public int SpawnQueueIndex;
        public long NextSpawnAt;
        public long NextEnemyRuntimeId = 1;
        public long NextProjectileRuntimeId = 1;
        public long NextDropRuntimeId = 1;
        public long BattleRevision = 1;
        /// <summary>本场成功炸弹序列；仅同步表现，不保存到轮前存档。</summary>
        public long NextBombBlastSequence = 1;
        /// <summary>保留最近三十二次成功使用，覆盖快照间的连续炸弹。</summary>
        public List<OnlineArcadeBombBlastRuntime> BombBlasts { get; } = new();
        /// <summary>最近二十四条死亡表现事实；只保留当前轮，既不保存也不模拟骨骼。</summary>
        public List<OnlineArcadeEnemyDeathRuntime> EnemyDeaths { get; } = new();
        public long LastTickAt;
        public float WaveTransitionSeconds;
        public bool WaveClearAwarded;
        public bool ArenaCompletionReady;
        public bool IsChallenge;
        public string ChallengeId = string.Empty;
        /// <summary>限时挑战剩余秒数；普通竞技场恒为零。</summary>
        public float ChallengeSeconds;
        /// <summary>独立Slayer阶段与共享容错，不能复用普通挑战自动清场成功。</summary>
        public FirepowerLoop.Arcade.ArcadeSlayerChallengeState SlayerChallenge;
        /// <summary>独立Guardian权威攻击与敌人身份，仅本地图有效。</summary>
        public FirepowerLoop.Arcade.ArcadeSlayerGuardianState SlayerGuardian;
        public long SlayerGuardianEnemyId;
        /// <summary>挑战退出操作回执，最多128条，离场即清空。</summary>
        public readonly Dictionary<string,string> SlayerExitRequests=new();
        public bool ChallengeCompletionReady;
        public bool ChallengeSucceeded;
        /// <summary>安全节点写入失败后的最早重试服务器时间戳，单位毫秒。</summary>
        public long NextCompletionAttemptAt;
        public bool InputsDirty;
        /// <summary>本场共享房间账本；不序列化，不作为安全节点。</summary>
        public FirepowerLoop.Arcade.ArcadeTreasureRoomState TreasureRoom;
        public bool TreasureInitialized;
        public bool TreasureDiscovered;
        public bool TreasureCleared;
        public bool TreasureCompleted;
        /// <summary>海岛局部轮廓与当帧实际移动路径，不进入协议或持久记录。</summary>
        public FirepowerLoop.Arcade.ArcadeIslandRegion IslandRegion;
        public bool IslandRegionResolved;
        public readonly List<FirepowerLoop.Arcade.ArcadeRegionPoint> IslandMovementPath = new();
        /// <summary>双方生命均耗尽后停止瞬时模拟；最终清局事务由后续失败结算切片处理。</summary>
        public bool AllPlayersDefeated;
        public List<string> SpawnQueue { get; } = new();
        public Dictionary<long, OnlineArcadeBattlePlayerRuntime> Players { get; } = new();
        public Dictionary<long, OnlineArcadeEnemyRuntime> Enemies { get; } = new();
        public Dictionary<long, OnlineArcadeProjectileRuntime> Projectiles { get; } = new();
        public Dictionary<long, OnlineArcadeDropRuntime> Drops { get; } = new();
        /// <summary>本场已成功消费的补给记录；随竞技场现场释放，重复快照不得再次发奖。</summary>
        public List<OnlineArcadeDropRuntime> SupplyPickups { get; } = new();
        public List<long> EnemyIterationIds { get; } = new();
        public List<long> ProjectileIterationIds { get; } = new();
        public List<long> DropIterationIds { get; } = new();
    }

    /// <summary>管理当前进程内的联机街机 Actor 与原成员索引；持久化成功前不发布新 Actor。</summary>
    [ComponentOf(typeof(Scene))]
    public sealed class OnlineArcadeRunManagerComponent : Entity, IAwake
    {
        public string StorePath;
        public int RemainingFailSaveCount;
        public readonly Dictionary<long, EntityRef<OnlineArcadeRunActor>> Runs = new();
        public readonly Dictionary<long, long> RunIdsByPlayer = new();
        public readonly Dictionary<long, long> RunIdsByRoom = new();
    }

    /// <summary>战斗权威已经裁决的敌人死亡方向与类别。</summary>
    [EnableClass]
    public sealed class OnlineArcadeEnemyDeathRuntime
    {
        /// <summary>0沿用原死亡表现，1能量爆炸，2近距霰弹；不参与伤害计算。</summary>
        public int ImpactKind;
        /// <summary>这次死亡由喷火器的有效伤害造成，不从残留燃烧推断。</summary>
        public bool FlameKill;
        /// <summary>本场已移除的权威敌人身份。</summary>
        public long RuntimeId;
        /// <summary>竞技场平面上的归一化受力方向。</summary>
        public float DirectionX, DirectionZ;
        /// <summary>仅区分尸体表现力度，不表示额外伤害。</summary>
        public bool Explosive;
        /// <summary>限制本事实只能用于原轮次的可见实体。</summary>
        public int RoundGeneration;
    }

    /// <summary>一次已成功消耗的炸弹表现，坐标锁定在使用瞬间。</summary>
    [EnableClass]
    public sealed class OnlineArcadeBombBlastRuntime
    {
        /// <summary>本场单调递增身份。</summary>
        public long Sequence;
        /// <summary>实际使用者。</summary>
        public long PlayerId;
        /// <summary>权威地面坐标。</summary>
        public float PositionX, PositionZ;
        /// <summary>防止旧轮效果进入新地图。</summary>
        public int RoundGeneration;
    }

    /// <summary>联机街机权威工作态与最近提交点；不自行写盘或发奖，Battle 现场仅存在于当前进程。</summary>
    [ChildOf(typeof(OnlineArcadeRunManagerComponent))]
    public sealed class OnlineArcadeRunActor : Entity, IAwake, IDestroy
    {
        /// <summary>权威工作记录，战斗可修改资源；不得作为其他局保存或本场回滚的来源。</summary>
        public OnlineArcadeRunRecord Record;
        /// <summary>最近成功提交记录的深克隆，仅由管理器在加载或写盘成功后替换；不单独序列化。</summary>
        public OnlineArcadeRunRecord CommittedRecord;
        public OnlineArcadeBattleRuntime Battle;
        public int BattleGeneration;
        /// <summary>成员断线时间，单位为服务器毫秒时间；只属于当前进程，不写入安全节点。</summary>
        public readonly Dictionary<long, long> DisconnectedAtByPlayer = new();
        /// <summary>竞技场暂停后的现场重连截止时间，单位为服务器毫秒时间；零表示当前未暂停。</summary>
        public long ArenaReconnectDeadline;
    }
}
