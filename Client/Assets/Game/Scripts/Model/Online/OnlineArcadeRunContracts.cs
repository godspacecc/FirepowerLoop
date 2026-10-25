using System.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using QFramework;

namespace Game
{
    public enum OnlineArcadeRunPhase { None = 0, SafeCheckpoint = 1, Arena = 2, Challenge = 3, Victory = 4 }
    public enum OnlineArcadeOperationState
    { Idle, Starting, Restoring, SettingReady, SettingChallengeReady, Settling, Abandoning }
    public enum OnlineArcadeResultCode
    {
        None, Success, NotLoggedIn, InvalidState, SaveFailed, OperationConflict, VersionMismatch,
        OperationInProgress, Timeout, Disconnected, UnknownFailure
    }



    public readonly struct OnlineArcadePlayerSnapshot
    {
        public OnlineArcadePlayerSnapshot(long playerId, string displayName, int lives, int runGold, int bombs,
            int dashes, string weaponId, bool ready,
            IReadOnlyDictionary<string, int> upgradeStacks = null,
            IReadOnlyDictionary<string, int> consumables = null,
            IReadOnlyDictionary<string, string> equipment = null,
            bool settlementConfirmed = false,
            bool abandonConfirmed = false, int settlementState = 0, bool challengeReady = false,
            long score = 0, int scoreMultiplier = 1, int multiplierProgress = 0, bool canReadyForArena=false)
        {
            PlayerId = playerId; DisplayName = displayName ?? string.Empty; Lives = lives; RunGold = runGold;
            Bombs = bombs; Dashes = dashes; WeaponId = weaponId ?? string.Empty; Ready = ready;
            UpgradeStacks = ReadOnly(upgradeStacks);
            Consumables = ReadOnly(consumables);
            Equipment = ReadOnly(equipment);
            SettlementConfirmed = settlementConfirmed; AbandonConfirmed = abandonConfirmed;
            SettlementState = settlementState;
            ChallengeReady = challengeReady;
            Score = score; ScoreMultiplier = scoreMultiplier; MultiplierProgress = multiplierProgress;CanReadyForArena=canReadyForArena||lives>0;
        }
        public long PlayerId { get; }
        public string DisplayName { get; }
        public int Lives { get; }
        public int RunGold { get; }
        public int Bombs { get; }
        public int Dashes { get; }
        public string WeaponId { get; }
        public bool Ready { get; }
        /// <summary>服务端恢复点活动资格；零命恢复者也可正常准备下一场。</summary>
        public bool CanReadyForArena { get; }
        public IReadOnlyDictionary<string, int> UpgradeStacks { get; }
        public IReadOnlyDictionary<string, int> Consumables { get; }
        public IReadOnlyDictionary<string, string> Equipment { get; }
        public bool SettlementConfirmed { get; }
        public bool AbandonConfirmed { get; }
        public int SettlementState { get; }
        public bool ChallengeReady { get; }
        /// <summary>服务器个人积分及倍率；与商店现金独立，客户端只读。</summary>
        public long Score { get; }
        public int ScoreMultiplier { get; }
        public int MultiplierProgress { get; }

        private static IReadOnlyDictionary<string, TValue> ReadOnly<TValue>(IReadOnlyDictionary<string, TValue> source)
        {
            Dictionary<string, TValue> copy = new(StringComparer.Ordinal);
            if (source != null)
                foreach (KeyValuePair<string, TValue> entry in source) copy[entry.Key] = entry.Value;
            return new ReadOnlyDictionary<string, TValue>(copy);
        }
    }

    public readonly struct OnlineArcadePowerupSnapshot
    {
        public OnlineArcadePowerupSnapshot(string productId, float remainingSeconds, int charges,
            float positionX = 0f, float positionZ = 0f, float shotDirectionX = 0f, float shotDirectionZ = 0f,int dronePhase=0,int dronePowerTier=1,bool hasExtraDrone=false,float extraDroneX=0,float extraDroneZ=0,int extraDronePhase=3,float extraDroneDirectionX=0,float extraDroneDirectionZ=0)
        {
            ProductId = productId ?? string.Empty;
            RemainingSeconds = remainingSeconds;
            Charges = charges;
            PositionX = positionX;
            PositionZ = positionZ;
            ShotDirectionX = shotDirectionX;
            ShotDirectionZ = shotDirectionZ; DronePhase=dronePhase;DronePowerTier=dronePowerTier;
            HasExtraDrone=hasExtraDrone;ExtraDroneX=extraDroneX;ExtraDroneZ=extraDroneZ;ExtraDronePhase=extraDronePhase;ExtraDroneDirectionX=extraDroneDirectionX;ExtraDroneDirectionZ=extraDroneDirectionZ;
        }

        public string ProductId { get; }
        public float RemainingSeconds { get; }
        public int Charges { get; }
        /// <summary>固定型强化首次生成的服务器权威竞技场坐标，单位米。</summary>
        public float PositionX { get; }
        public float PositionZ { get; }
        /// <summary>服务器最后一次实际发射方向；零表示尚未发射，不参与客户端战斗裁决。</summary>
        public float ShotDirectionX { get; }
        public float ShotDirectionZ { get; }
        /// <summary>无人机当前权威阶段与玩家强化等级。</summary>
        public int DronePhase { get; }
        public int DronePowerTier { get; }
        /// <summary>第二架只读权威投影，不由客户端推算寿命或伤害。</summary>
        public bool HasExtraDrone { get; }
        public float ExtraDroneX { get; }
        public float ExtraDroneZ { get; }
        public int ExtraDronePhase { get; }
        public float ExtraDroneDirectionX { get; }
        public float ExtraDroneDirectionZ { get; }
    }

    public readonly struct OnlineArcadeBattlePlayerSnapshot
    {
        /// <summary>服务器当前牛撞状态；只读复制，重连不重新开始计时。</summary>
        public FirepowerLoop.Arcade.ArcadeHerdImpactState HerdImpact { get; }
        /// <summary>服务器实际牛撞掉血反馈；与存活击飞分离，致死也能显示，旧序列不会补播。</summary>
        public long HerdContactSequence { get; }
        public float HerdContactX { get; }
        public float HerdContactZ { get; }
        public double HerdContactElapsedSeconds { get; }
        public OnlineArcadeBattlePlayerSnapshot(long playerId, float positionX, float positionZ, float aimX,
            float aimZ, int currentHealth, int maxHealth, bool alive, bool invulnerable, float respawnSeconds,
            string temporaryWeaponId = null, float temporaryWeaponSeconds = 0f, string supportId = null,
            float supportSeconds = 0f, float dashSeconds = 0f,
            IReadOnlyList<OnlineArcadePowerupSnapshot> powerups = null, int fireSequence = 0, bool inTreasureRoom = false, int vestSlots = 0, int plates = 0, bool hasStorm = false,
            int stormCharge = 0, float stormSeconds = 0, IReadOnlyList<OnlineSlayerMartSupportSnapshot> slayerMartSupports = null, float downedSeconds=0, float rescueRate=0,
            float dashDirectionX = 0, float dashDirectionZ = 0, FirepowerLoop.Arcade.ArcadeHerdImpactState herdImpact = default,
            long herdContactSequence=0,float herdContactX=0,float herdContactZ=0,double herdContactElapsedSeconds=0)
        {
            PlayerId = playerId; PositionX = positionX; PositionZ = positionZ; AimX = aimX; AimZ = aimZ;
            CurrentHealth = currentHealth; MaxHealth = maxHealth; Alive = alive;
            Invulnerable = invulnerable; RespawnSeconds = respawnSeconds;DownedSeconds=downedSeconds;RescueRate=rescueRate;
            TemporaryWeaponId = temporaryWeaponId ?? string.Empty; TemporaryWeaponSeconds = temporaryWeaponSeconds;
            SupportId = supportId ?? string.Empty; SupportSeconds = supportSeconds;
            DashSeconds = dashSeconds;
            DashDirectionX = dashDirectionX; DashDirectionZ = dashDirectionZ;HerdImpact=herdImpact;
            HerdContactSequence=herdContactSequence;HerdContactX=herdContactX;HerdContactZ=herdContactZ;HerdContactElapsedSeconds=herdContactElapsedSeconds;
            Powerups = Array.AsReadOnly(Copy(powerups));
            FireSequence = fireSequence; InTreasureRoom = inTreasureRoom;
            VestSlots=vestSlots;Plates=plates;HasStorm=hasStorm;StormCharge=stormCharge;StormSeconds=stormSeconds;
            SlayerMartSupports=Array.AsReadOnly(slayerMartSupports==null?Array.Empty<OnlineSlayerMartSupportSnapshot>():slayerMartSupports.ToArray());
        }
        public long PlayerId { get; }
        public float PositionX { get; }
        public float PositionZ { get; }
        public float AimX { get; }
        public float AimZ { get; }
        public int CurrentHealth { get; }
        public int MaxHealth { get; }
        public bool Alive { get; }
        public bool Invulnerable { get; }
        public float RespawnSeconds { get; }
        /// <summary>服务器独立倒地剩余量与当前救援推进倍率。</summary>
        public float DownedSeconds { get; }
        public float RescueRate { get; }
        public string TemporaryWeaponId { get; }
        public float TemporaryWeaponSeconds { get; }
        public string SupportId { get; }
        public float SupportSeconds { get; }
        public float DashSeconds { get; }
        /// <summary>服务器锁定的本次冲刺方向，独立于本地当前移动和瞄准输入。</summary>
        public float DashDirectionX { get; }
        public float DashDirectionZ { get; }
        public IReadOnlyList<OnlineArcadePowerupSnapshot> Powerups { get; }
        /// <summary>服务器本场成功攻击序列；只驱动表现，不能作为客户端伤害依据。</summary>
        public int FireSequence { get; }
        /// <summary>服务器权威区域身份；用于本机区域过滤和预测重置。</summary>
        public bool InTreasureRoom { get; }
        /// <summary>护甲、风暴与真实支援仅来自服务器权威帧。</summary>
        public int VestSlots { get; }
        public int Plates { get; }
        public bool HasStorm { get; }
        public int StormCharge { get; }
        public float StormSeconds { get; }
        public IReadOnlyList<OnlineSlayerMartSupportSnapshot> SlayerMartSupports { get; }

        private static OnlineArcadePowerupSnapshot[] Copy(IReadOnlyList<OnlineArcadePowerupSnapshot> source)
        {
            if (source == null || source.Count == 0) return Array.Empty<OnlineArcadePowerupSnapshot>();
            var copy = new OnlineArcadePowerupSnapshot[source.Count];
            for (var index = 0; index < source.Count; index++) copy[index] = source[index];
            return copy;
        }
    }

    /// <summary>已经裁决的死亡表现事实，不持有对象、骨骼或资源句柄。</summary>
    public readonly struct OnlineArcadeEnemyDeathSnapshot
    {
        public OnlineArcadeEnemyDeathSnapshot(long runtimeId, float directionX, float directionZ, bool explosive, int roundGeneration,bool flameKill=false,int impactKind=0)
        { RuntimeId = runtimeId; DirectionX = directionX; DirectionZ = directionZ; Explosive = explosive; RoundGeneration = roundGeneration;FlameKill=flameKill;ImpactKind=impactKind; }
        /// <summary>最终伤害确定的尸体力度类别；0兼容既有普通/爆炸死亡。</summary>
        public int ImpactKind { get; }
        /// <summary>本次最终有效伤害由喷火器造成，独立于仍残留的燃烧计时。</summary>
        public bool FlameKill { get; }
        public long RuntimeId { get; }
        public float DirectionX { get; }
        public float DirectionZ { get; }
        public bool Explosive { get; }
        public int RoundGeneration { get; }
    }

    public readonly struct OnlineArcadeEnemySnapshot
    {
        /// <summary>当前海路年龄和车道的只读副本，迟到实例不重新播放起点。</summary>
        public FirepowerLoop.Arcade.ArcadeDockSeaEntryState DockSeaEntry { get; }
        /// <summary>现身和一次重生的只读权威状态；晚加载沿当前阶段采样。</summary>
        public FirepowerLoop.Arcade.ArcadeStageEnemyState StageEnemy { get; }
        /// <summary>服务器当前牛撞状态；只读复制，重连不重新开始计时。</summary>
        public FirepowerLoop.Arcade.ArcadeHerdImpactState HerdImpact { get; }
        public OnlineArcadeEnemySnapshot(long runtimeId, string enemyConfigId, float positionX, float positionZ,
            int currentHealth, int maxHealth, long targetPlayerId, int waveNumber,
            string bossId = "", ArcadeBossPhase bossPhase = ArcadeBossPhase.None, float bossPhaseSeconds = 0f, int armorHealth = 0, int specialKind = 0, int contentBossKind = 0, bool contentWarning = false,float flameHitRemainingSeconds=0, FirepowerLoop.Arcade.ArcadeHerdImpactState herdImpact=default,FirepowerLoop.Arcade.ArcadeStageEnemyState stageEnemy=default,int attackSequence=0,float meleeWindupSeconds=0,float meleeWindupRemaining=0,float meleeRecoveryRemaining=0,FirepowerLoop.Arcade.ArcadeDockSeaEntryState dockSeaEntry=default,float contentAge=-1,float contentFacingX=0,float contentFacingZ=0)
        {
            RuntimeId = runtimeId; EnemyConfigId = enemyConfigId ?? string.Empty; PositionX = positionX;
            PositionZ = positionZ; CurrentHealth = currentHealth; MaxHealth = maxHealth;
            TargetPlayerId = targetPlayerId; WaveNumber = waveNumber;
            BossId = bossId ?? string.Empty; BossPhase = bossPhase; BossPhaseSeconds = bossPhaseSeconds;
            ArmorHealth = armorHealth;SpecialKind=specialKind;ContentBossKind=contentBossKind;ContentWarning=contentWarning;
            FlameHitRemainingSeconds=flameHitRemainingSeconds;HerdImpact=herdImpact;StageEnemy=stageEnemy;AttackSequence=attackSequence;
            MeleeWindupSeconds=meleeWindupSeconds;MeleeWindupRemaining=meleeWindupRemaining;MeleeRecoveryRemaining=meleeRecoveryRemaining;DockSeaEntry=dockSeaEntry;
            ContentAge=contentAge;ContentFacingX=contentFacingX;ContentFacingZ=contentFacingZ;
        }
        public long RuntimeId { get; }
        public string EnemyConfigId { get; }
        /// <summary>已完成的近战挥击序列；只投影，不重算攻击或伤害。</summary>
        public int AttackSequence { get; }
        /// <summary>当前攻击前摇的权威总时长和剩余量；重复快照直接采样。</summary>
        public float MeleeWindupSeconds { get; }
        public float MeleeWindupRemaining { get; }
        /// <summary>命中后的收招剩余秒数，由服务器推进。</summary>
        public float MeleeRecoveryRemaining { get; }
        public float PositionX { get; }
        public float PositionZ { get; }
        public int CurrentHealth { get; }
        public int SpecialKind { get; }
        public int ContentBossKind { get; }
        public bool ContentWarning { get; }
        /// <summary>只读技能年龄与权威锁向；不在本地推进技能阶段。</summary>
        public float ContentAge { get; }
        public float ContentFacingX { get; }
        public float ContentFacingZ { get; }
        /// <summary>最近一次有效喷火命中的可见剩余秒数。</summary>
        public float FlameHitRemainingSeconds { get; }
        public int ArmorHealth { get; }
        public int MaxHealth { get; }
        public long TargetPlayerId { get; }
        public int WaveNumber { get; }
        public string BossId { get; }
        public ArcadeBossPhase BossPhase { get; }
        public float BossPhaseSeconds { get; }
    }

    public readonly struct OnlineArcadeProjectileSnapshot
    {
        public OnlineArcadeProjectileSnapshot(long runtimeId, long ownerPlayerId, float positionX, float positionZ,
            float velocityX, float velocityZ, string sourceWeaponId = null,int weaponPowerLevel=0,float evolutionHitRadius=0,bool evolutionClusterChild=false,float remainingSeconds=0)
        {
            RuntimeId = runtimeId; OwnerPlayerId = ownerPlayerId; PositionX = positionX; PositionZ = positionZ;
            VelocityX = velocityX; VelocityZ = velocityZ;
            SourceWeaponId = sourceWeaponId ?? string.Empty;
            WeaponPowerLevel=weaponPowerLevel;EvolutionHitRadius=evolutionHitRadius;EvolutionClusterChild=evolutionClusterChild;
            RemainingSeconds=remainingSeconds;
        }
        public long RuntimeId { get; }
        public long OwnerPlayerId { get; }
        public float PositionX { get; }
        public float PositionZ { get; }
        public float VelocityX { get; }
        public float VelocityZ { get; }
        /// <summary>保留发射时的武器外观，换枪或武器到期不回改已飞出的弹丸。</summary>
        public string SourceWeaponId { get; }
        /// <summary>弹丸发射时的头骨层级，换枪后保留，敌弹忽略。</summary>
        public int WeaponPowerLevel { get; }
        /// <summary>宽束及子榴弹外观所需的真实发射快照。</summary>
        public float EvolutionHitRadius { get; }
        public bool EvolutionClusterChild { get; }
        public float RemainingSeconds { get; }
    }

    public readonly struct OnlineArcadeDropSnapshot
    {
        public OnlineArcadeDropSnapshot(long runtimeId, string dropConfigId, float positionX, float positionZ,
            long collectedByPlayerId = 0, int addedAmount = 0, long scoreAdded = 0, int goldAdded = 0,
            float remainingSeconds = 30f,bool weaponAirdrop=false,float deliveryRemainingSeconds=0)
        {
            RuntimeId = runtimeId; DropConfigId = dropConfigId ?? string.Empty;
            PositionX = positionX; PositionZ = positionZ;
            CollectedByPlayerId = collectedByPlayerId; AddedAmount = addedAmount;
            ScoreAdded = scoreAdded; GoldAdded = goldAdded;
            RemainingSeconds = remainingSeconds;
            WeaponAirdrop=weaponAirdrop;DeliveryRemainingSeconds=deliveryRemainingSeconds;
        }
        public long RuntimeId { get; }
        public string DropConfigId { get; }
        public float PositionX { get; }
        public float PositionZ { get; }
        /// <summary>成功拾取记录的权威领取者；普通可见掉落为零。</summary>
        public long CollectedByPlayerId { get; }
        /// <summary>本次实际补充次数；客户端仅反馈，不据此增加库存。</summary>
        public int AddedAmount { get; }
        /// <summary>权威领取事务的实际积分与金币增量，不由总库存差值推断。</summary>
        public long ScoreAdded { get; }
        public int GoldAdded { get; }
        /// <summary>服务器权威剩余地面寿命；重复快照或重连不能刷新为完整30秒。</summary>
        public float RemainingSeconds { get; }
        /// <summary>权威空投阶段，客户端按剩余量定位箱体和箱盖，不自行决定可领取时机。</summary>
        public bool WeaponAirdrop { get; }
        public float DeliveryRemainingSeconds { get; }
    }

    /// <summary>服务器确认的原始成员连接状态与截止时间；客户端只用于提示和按钮可用性。</summary>
    public readonly struct OnlineArcadeConnectionSnapshot
    {
        public OnlineArcadeConnectionSnapshot(long playerId, bool connected, long disconnectedAtServerTime,
            long arenaReconnectDeadlineServerTime, long soloEndAvailableAtServerTime)
        {
            PlayerId = playerId; Connected = connected; DisconnectedAtServerTime = disconnectedAtServerTime;
            ArenaReconnectDeadlineServerTime = arenaReconnectDeadlineServerTime;
            SoloEndAvailableAtServerTime = soloEndAvailableAtServerTime;
        }
        public long PlayerId { get; }
        public bool Connected { get; }
        public long DisconnectedAtServerTime { get; }
        public long ArenaReconnectDeadlineServerTime { get; }
        public long SoloEndAvailableAtServerTime { get; }
    }

    /// <summary>客户端只读的服务器战斗现场；当前只包含进入信息与权威敌人生成状态。</summary>
    public sealed class OnlineArcadeRoundRewardSnapshot
    {
        public int RoundIdentity {get;} public float X {get;} public float Z {get;} public float ElapsedSeconds {get;}
        public FirepowerLoop.Arcade.ArcadeRoundRewardPhase Phase {get;}
        public IReadOnlyList<FirepowerLoop.Arcade.ArcadeRoundRewardItem> Items {get;} public IReadOnlyList<int> Collected {get;}
        public OnlineArcadeRoundRewardSnapshot(int round,float x,float z,float elapsed,int phase,IEnumerable<FirepowerLoop.Arcade.ArcadeRoundRewardItem> items,IEnumerable<int> collected)
        {RoundIdentity=round;X=x;Z=z;ElapsedSeconds=elapsed;Phase=(FirepowerLoop.Arcade.ArcadeRoundRewardPhase)phase;Items=Array.AsReadOnly(items.ToArray());Collected=Array.AsReadOnly(collected.ToArray());}
        /// <summary>只为显示创建独立副本，永不交给本地领取规则。</summary>
        public FirepowerLoop.Arcade.ArcadeRoundRewardState ToPresentation()
        {var state=new FirepowerLoop.Arcade.ArcadeRoundRewardState{RoundIdentity=RoundIdentity,X=X,Z=Z,ElapsedSeconds=ElapsedSeconds,Phase=Phase};state.Items.AddRange(Items);state.Collected.UnionWith(Collected);return state;}
    }

    /// <summary>已裁决电击的只读地点与时间，和血量、死亡反馈独立同步。</summary>
    public readonly struct OnlineArcadeElectricHitSnapshot
    {
        public OnlineArcadeElectricHitSnapshot(long sequence,int hazardId,float x,float z,int roundGeneration,float elapsedSeconds,long serverTime=0)
        {Sequence=sequence;HazardId=hazardId;X=x;Z=z;RoundGeneration=roundGeneration;ElapsedSeconds=elapsedSeconds;ServerTime=serverTime;}
        public long Sequence {get;}
        public int HazardId {get;}
        public float X {get;}
        public float Z {get;}
        public int RoundGeneration {get;}
        public float ElapsedSeconds {get;}
        public long ServerTime {get;}
    }

    public sealed class OnlineArcadeBattleSnapshot
    {
        public OnlineArcadeBattleSnapshot(int arenaNumber, int waveNumber, int waveCount, int spawnedInWave,
            int totalInWave, int difficultyPlayerCount, long battleRevision, long serverTime,
            IReadOnlyList<OnlineArcadeBattlePlayerSnapshot> players, IReadOnlyList<OnlineArcadeEnemySnapshot> enemies,
            IReadOnlyList<OnlineArcadeProjectileSnapshot> projectiles = null,
            IReadOnlyList<OnlineArcadeDropSnapshot> drops = null, bool isChallenge = false,
            string challengeId = null, float challengeSeconds = 0f, float hazardElapsedSeconds = 0f,
            int arenaConfigNumber = 0, bool isFinalBoss = false, bool awaitingExit = false,
            string treasureRoomId = null, bool treasureDiscovered = false, bool treasureOpen = false,
            bool treasureExpired = false, float treasureSeconds = 0, IReadOnlyList<int> treasureRewardIds = null,
            IReadOnlyList<int> crateIds = null, IReadOnlyList<OnlineArcadeDropSnapshot> supplyPickups = null,
            float treasureRewardSeconds = 30f, string roundLayoutId = null, int roundLayoutVersion = 0,
            int roundGeneration = 0, bool roundLoading = false, string roundLoadError = null,
            float contentElapsed = 0, IReadOnlyList<FirepowerLoop.Arcade.ArcadeContentAreaSnapshot> contentAreas = null, bool roundBossEncounter = false, bool roundBossDefeated = false, OnlineSlayerMartRegionSnapshot slayerMart = null,OnlineSlayerChallengeSnapshot slayerChallenge=null,
            IReadOnlyList<OnlineArcadeBombBlastSnapshot> bombBlasts=null, IReadOnlyList<OnlineArcadeEnemyDeathSnapshot> enemyDeaths=null, OnlineArcadeRoundRewardSnapshot roundReward=null,float stagePortalSeconds=0,float stagePortalX=0,float stagePortalZ=0,
            IReadOnlyList<FirepowerLoop.Arcade.ArcadeHerdSnapshot> herds=null,IReadOnlyList<OnlineArcadeElectricHitSnapshot> electricHits=null,
            IReadOnlyList<OnlineArcadeWeaponFeedbackSnapshot> weaponFeedback=null)
        {
            SlayerMart=slayerMart;SlayerChallenge=slayerChallenge;RoundReward=roundReward;StagePortalSeconds=stagePortalSeconds;StagePortalX=stagePortalX;StagePortalZ=stagePortalZ;
            ArenaNumber = arenaNumber; WaveNumber = waveNumber; WaveCount = waveCount;
            HazardElapsedSeconds = hazardElapsedSeconds;
            ArenaConfigNumber = arenaConfigNumber;
            IsFinalBoss = isFinalBoss;
            AwaitingExit = awaitingExit;
            TreasureRoomId = treasureRoomId ?? string.Empty; TreasureDiscovered = treasureDiscovered;
            TreasureOpen = treasureOpen; TreasureExpired = treasureExpired; TreasureSeconds = treasureSeconds;
            TreasureRewardSeconds = treasureRewardSeconds;
            RoundLayoutId=roundLayoutId??string.Empty;RoundLayoutVersion=roundLayoutVersion;RoundGeneration=roundGeneration;
            RoundLoading=roundLoading;RoundLoadError=roundLoadError??string.Empty;ContentElapsed=contentElapsed;
            ContentAreas=Array.AsReadOnly(Copy(contentAreas));RoundBossEncounter=roundBossEncounter;RoundBossDefeated=roundBossDefeated;
            Herds=Array.AsReadOnly(Copy(herds));
            TreasureRewardIds = Array.AsReadOnly(Copy(treasureRewardIds));
            CrateIds = Array.AsReadOnly(Copy(crateIds));
            SupplyPickups = Array.AsReadOnly(Copy(supplyPickups));
            BombBlasts = Array.AsReadOnly(Copy(bombBlasts));
            EnemyDeaths = Array.AsReadOnly(Copy(enemyDeaths));
            ElectricHits=Array.AsReadOnly(Copy(electricHits));
            WeaponFeedback=Array.AsReadOnly(Copy(weaponFeedback));
            SpawnedInWave = spawnedInWave; TotalInWave = totalInWave;
            DifficultyPlayerCount = difficultyPlayerCount; BattleRevision = battleRevision; ServerTime = serverTime;
            Players = Array.AsReadOnly(Copy(players)); Enemies = Array.AsReadOnly(Copy(enemies));
            Projectiles = Array.AsReadOnly(Copy(projectiles));
            Drops = Array.AsReadOnly(Copy(drops));
            IsChallenge = isChallenge; ChallengeId = challengeId ?? string.Empty;
            ChallengeSeconds = challengeSeconds;
        }
        /// <summary>服务器提交的地图身份与加载代次；旧快照不能确认新代次。</summary>
        /// <summary>商店区域与资源代次的只读公共状态。</summary>
        public OnlineSlayerMartRegionSnapshot SlayerMart { get; }
        /// <summary>独立Slayer公共状态，不以旧限时清怪倒计时推断阶段。</summary>
        public OnlineSlayerChallengeSnapshot SlayerChallenge {get;}
        /// <summary>每轮宝箱的只读显示事实，客户端不得据此发奖。</summary>
        public OnlineArcadeRoundRewardSnapshot RoundReward {get;}
        public float StagePortalSeconds {get;} public float StagePortalX {get;} public float StagePortalZ {get;}
        public bool RoundBossEncounter { get; }
        public bool RoundBossDefeated { get; }
        public string RoundLayoutId { get; }
        public int RoundLayoutVersion { get; }
        public int RoundGeneration { get; }
        public bool RoundLoading { get; }
        public string RoundLoadError { get; }
        public float ContentElapsed { get; }
        public IReadOnlyList<FirepowerLoop.Arcade.ArcadeContentAreaSnapshot> ContentAreas { get; }
        /// <summary>权威牛群只读副本，不能用于客户端伤害或保存半次冲撞。</summary>
        public IReadOnlyList<FirepowerLoop.Arcade.ArcadeHerdSnapshot> Herds {get;}
        public int ArenaNumber { get; }
        public int WaveNumber { get; }
        /// <summary>服务器选定的实际地图；旧协议为零时回退到序号。</summary>
        public int ArenaConfigNumber { get; }
        public bool IsFinalBoss { get; }
        public bool AwaitingExit { get; }
        /// <summary>本场房间只读投影；物品 ID 从配置取值，不传递可改奖励参数。</summary>
        public string TreasureRoomId { get; }
        public bool TreasureDiscovered { get; }
        public bool TreasureOpen { get; }
        public bool TreasureExpired { get; }
        public float TreasureSeconds { get; }
        /// <summary>宝藏房地面奖励的权威剩余寿命，独立于房间返回时限。</summary>
        public float TreasureRewardSeconds { get; }
        public IReadOnlyList<int> TreasureRewardIds { get; }
        public IReadOnlyList<int> CrateIds { get; }
        public int WaveCount { get; }
        public int SpawnedInWave { get; }
        public int TotalInWave { get; }
        public int DifficultyPlayerCount { get; }
        public long BattleRevision { get; }
        public long ServerTime { get; }
        public IReadOnlyList<OnlineArcadeBattlePlayerSnapshot> Players { get; }
        public IReadOnlyList<OnlineArcadeEnemySnapshot> Enemies { get; }
        /// <summary>服务器权威机关活动时间；暂停快照保持该值不变。</summary>
        public float HazardElapsedSeconds { get; }
        public IReadOnlyList<OnlineArcadeProjectileSnapshot> Projectiles { get; }
        public IReadOnlyList<OnlineArcadeDropSnapshot> Drops { get; }
        /// <summary>本场成功补给记录的只读副本；不包含满额拒绝或自然移除。</summary>
        public IReadOnlyList<OnlineArcadeDropSnapshot> SupplyPickups { get; }
        /// <summary>服务器成功使用炸弹的有界只读记录。</summary>
        public IReadOnlyList<OnlineArcadeBombBlastSnapshot> BombBlasts { get; }
        /// <summary>最多24条同轮死亡事实；只为已经可见且本次移除的敌人播放。</summary>
        public IReadOnlyList<OnlineArcadeEnemyDeathSnapshot> EnemyDeaths { get; }
        /// <summary>每轮最多32条电塔命中事实，仅供去重表现。</summary>
        public IReadOnlyList<OnlineArcadeElectricHitSnapshot> ElectricHits {get;}
        /// <summary>服务器实际命中、撞墙及武器爆炸，不由弹丸快照差分推断。</summary>
        public IReadOnlyList<OnlineArcadeWeaponFeedbackSnapshot> WeaponFeedback {get;}
        public bool IsChallenge { get; }
        public string ChallengeId { get; }
        public float ChallengeSeconds { get; }
        private static T[] Copy<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0) return Array.Empty<T>();
            T[] result = new T[source.Count];
            for (int index = 0; index < source.Count; index++) result[index] = source[index];
            return result;
        }
    }

    /// <summary>客户端只读的联机街机安全节点；所有字段只接受服务器响应。</summary>
    public readonly struct OnlineArcadeRunClientSnapshot
    {
        public OnlineArcadeRunClientSnapshot(bool hasRun, long runId, long roomId, long revision,
            OnlineArcadeRunPhase phase, int completedArena, int nextArena, string configFingerprint,
            IReadOnlyList<OnlineArcadePlayerSnapshot> players, OnlineArcadeOperationState operationState,
            OnlineArcadeResultCode lastResultCode, OnlineArcadeBattleSnapshot battle = null,
            IReadOnlyList<OnlineArcadeConnectionSnapshot> connections = null, bool battlePaused = false,
            string challengeId = null, bool challengeAttempted = false, int casualRouteVersion = 0)
        {
            HasRun = hasRun; RunId = runId; RoomId = roomId; Revision = revision; Phase = phase;
            CompletedArena = completedArena; NextArena = nextArena; ConfigFingerprint = configFingerprint ?? string.Empty;
            Players = Array.AsReadOnly(Copy(players)); OperationState = operationState; LastResultCode = lastResultCode;
            Battle = battle;
            Connections = Array.AsReadOnly(Copy(connections)); BattlePaused = battlePaused;
            ChallengeId = challengeId ?? string.Empty; ChallengeAttempted = challengeAttempted;
            CasualRouteVersion = casualRouteVersion;
        }
        public bool HasRun { get; }
        public long RunId { get; }
        public long RoomId { get; }
        public long Revision { get; }
        public OnlineArcadeRunPhase Phase { get; }
        public int CompletedArena { get; }
        public int NextArena { get; }
        public string ConfigFingerprint { get; }
        public IReadOnlyList<OnlineArcadePlayerSnapshot> Players { get; }
        public OnlineArcadeOperationState OperationState { get; }
        public OnlineArcadeResultCode LastResultCode { get; }
        public OnlineArcadeBattleSnapshot Battle { get; }
        public IReadOnlyList<OnlineArcadeConnectionSnapshot> Connections { get; }
        public bool BattlePaused { get; }
        public string ChallengeId { get; }
        public bool ChallengeAttempted { get; }
        /// <summary>服务器确认的本局路线；0为原四十轮，1为五十二轮，2为确认十关四十轮。</summary>
        public int CasualRouteVersion { get; }
        public static OnlineArcadeRunClientSnapshot Empty(OnlineArcadeResultCode code = OnlineArcadeResultCode.None) =>
            new(false, 0, 0, 0, OnlineArcadeRunPhase.None, 0, 0, string.Empty,
                Array.Empty<OnlineArcadePlayerSnapshot>(), OnlineArcadeOperationState.Idle, code);
        private static OnlineArcadePlayerSnapshot[] Copy(IReadOnlyList<OnlineArcadePlayerSnapshot> source)
        {
            if (source == null || source.Count == 0) return Array.Empty<OnlineArcadePlayerSnapshot>();
            var copy = new OnlineArcadePlayerSnapshot[source.Count];
            for (int i = 0; i < source.Count; i++) copy[i] = source[i];
            return copy;
        }
        private static T[] Copy<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0) return Array.Empty<T>();
            var copy = new T[source.Count];
            for (int i = 0; i < source.Count; i++) copy[i] = source[i];
            return copy;
        }
    }

    public readonly struct OnlineArcadeOperationResult
    {
        public OnlineArcadeOperationResult(OnlineArcadeResultCode code, OnlineArcadeRunClientSnapshot snapshot)
        { Code = code; Snapshot = snapshot; }
        public OnlineArcadeResultCode Code { get; }
        public OnlineArcadeRunClientSnapshot Snapshot { get; }
        public bool Success => Code == OnlineArcadeResultCode.Success;
    }

    public readonly struct OnlineArcadeInputResult
    {
        public OnlineArcadeInputResult(OnlineArcadeResultCode code, bool accepted, long inputSequence,
            OnlineArcadeRunClientSnapshot snapshot)
        {
            Code = code; Accepted = accepted; InputSequence = inputSequence; Snapshot = snapshot;
        }
        public OnlineArcadeResultCode Code { get; }
        public bool Accepted { get; }
        public long InputSequence { get; }
        public OnlineArcadeRunClientSnapshot Snapshot { get; }
    }

    public readonly struct OnlineArcadeRunChangedEvent
    {
        public OnlineArcadeRunChangedEvent(OnlineArcadeRunClientSnapshot snapshot) { Snapshot = snapshot; }
        public OnlineArcadeRunClientSnapshot Snapshot { get; }
    }

    public sealed class OnlineArcadeRunModel : AbstractModel
    {
        public BindableProperty<OnlineArcadeRunClientSnapshot> Snapshot { get; } =
            new(OnlineArcadeRunClientSnapshot.Empty());
        protected override void OnInit() { }
        public void Publish(OnlineArcadeRunClientSnapshot snapshot)
        {
            Snapshot.Value = snapshot;
            this.SendEvent(new OnlineArcadeRunChangedEvent(snapshot));
        }
    }
}
