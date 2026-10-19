using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>Boss 技能瞬时阶段；只存在于当前竞技场，联机协议使用相同数值。</summary>
    public enum ArcadeBossPhase
    {
        None = 0,
        Telegraph = 1,
        Charging = 2,
        RayTelegraph = 3,
    }

    internal sealed class ArcadeEnemyRuntime
    {
        /// <summary>真实火焰命中后的短暂表现计时，不造成持续伤害。</summary>
        public float FlameHitRemainingSeconds;
        /// <summary>最后一次有效伤害是否来自喷火器，仅用于死亡表现。</summary>
        public bool LastDamageWasFlame;
        public int Id;
        public string ConfigId;
        public global::EnemyAttackKind AttackKind;
        public Vector2 Position;
        public int Health;
        public int MaxHealth;
        /// <summary>当前轮独立护甲耐久，归还对象池时清零，不计入生命或存档。</summary>
        public int ArmorHealth;
        public int Damage;
        public float Speed;
        public float AttackRange;
        public float AttackInterval;
        public float AttackCooldown;
        /// <summary>标记当前波次中的 Boss，只供常驻血条等表现读取，不参与伤害或行为判断。</summary>
        public bool IsBossPresentation;
        /// <summary>每次规则层接受攻击时递增；仅用于让表现层去重播放攻击动画。</summary>
        public int AttackSequence;
        /// <summary>本轮特殊敌人与独立Boss身份，不进入本局文件。</summary>
        public int ContentSpecialKind, ContentBossKind;
        /// <summary>固定出生序已赋值，重复绑定Boss不重复缩放生命。</summary>
        public bool ContentAssigned, ContentWarning, ContentEvolved;
        /// <summary>出生、最后受击与眩晕的活动时刻。</summary>
        public double ContentBornAt, ContentLastHitAt = -1, ContentStunUntil;
        /// <summary>技能预警锁定的方向及人偶已破坏阶段。</summary>
        public Vector2 ContentLockedDirection, ContentLockedTarget;
        public int ContentMannequinHits, ContentSkillCycle = -1, ContentTelegraphCycle = -1;
        /// <summary>本活动帧附近祭司提供的移动和攻击增益。</summary>
        public float ContentAllyBuff = 1;
        public string BossId;
        public ArcadeBossDefinition BossDefinition;
        public ArcadeBossPhase BossPhase;
        public float BossPhaseRemaining;
        public float BossSkillCooldown;
        public Vector2 BossChargeDirection;
        public bool BossImpactConsumed;
        /// <summary>射线独立冷却（秒）；预警方向在进入 RayTelegraph 时锁定。</summary>
        public float BossRayCooldown;

        /// <summary>归还规则对象池前清除身份与战斗状态，避免下一个实体继承旧值。</summary>
        public void Reset()
        {
            FlameHitRemainingSeconds=0;LastDamageWasFlame=false;
            Id = 0;
            ConfigId = null;
            AttackKind = default;
            Position = default;
            Health = 0;
            MaxHealth = 0;
            ArmorHealth = 0;
            Damage = 0;
            Speed = 0f;
            AttackRange = 0f;
            AttackInterval = 0f;
            AttackCooldown = 0f;
            IsBossPresentation = false;
            AttackSequence = 0;
            ContentSpecialKind = ContentBossKind = ContentMannequinHits = 0;
            ContentAssigned = ContentWarning = ContentEvolved = false;
            ContentBornAt = ContentStunUntil = 0;
            ContentLastHitAt = -1;
            ContentSkillCycle = ContentTelegraphCycle = -1;
            ContentLockedDirection = ContentLockedTarget = default;
            ContentAllyBuff = 1;
            BossId = null;
            BossDefinition = default;
            BossPhase = ArcadeBossPhase.None;
            BossPhaseRemaining = 0f;
            BossSkillCooldown = 0f;
            BossChargeDirection = default;
            BossImpactConsumed = false;
            BossRayCooldown = 0f;
        }
    }

    internal sealed class ArcadeProjectileRuntime
    {
        public int Id;
        /// <summary>追踪弹转速和寄生弹剩余减速秒数，由本弹的生成方固定。</summary>
        public float ContentTurnRate, ContentSlowSeconds;
        public bool Hostile;
        public Vector2 Position;
        public Vector2 Velocity;
        public int Damage;
        public float RemainingSeconds;
        public global::ArcadeWeaponBehavior Behavior;
        /// <summary>仅供表现层选择模型的配置资源名；不参与伤害、命中或寿命规则。</summary>
        public string ProjectileVisualResourceName;
        /// <summary>仅标记玩家武器弹丸，用于客户端枪口衔接；支援和敌方弹丸不使用该表现。</summary>
        public bool FromPlayerWeapon;
        /// <summary>生成时固定的武器配置身份，仅用于命中音色；换武器后在途弹丸仍保留原身份。</summary>
        public string SourceWeaponId;
        /// <summary>购买支援的稳定来源，普通武器和普通掉落为空。</summary>
        public string SupportInstanceId;
        /// <summary>本弹丸尚可命中的不同敌人数；只存在于当前竞技场。</summary>
        public int RemainingHits;
        /// <summary>命中敌人或障碍时的爆炸半径，单位为竞技场米。</summary>
        public float ExplosionRadius;
        public readonly HashSet<int> HitEnemyIds = new HashSet<int>();

        public void Reset()
        {
            Id = 0;
            ContentTurnRate = ContentSlowSeconds = 0;
            Hostile = false;
            Position = default;
            Velocity = default;
            Damage = 0;
            RemainingSeconds = 0f;
            Behavior = global::ArcadeWeaponBehavior.Direct;
            ProjectileVisualResourceName = null;
            FromPlayerWeapon = false;
            SourceWeaponId = null;
            SupportInstanceId = null;
            RemainingHits = 0;
            ExplosionRadius = 0f;
            HitEnemyIds.Clear();
        }
    }

    internal sealed class ArcadeDropRuntime
    {
        /// <summary>只标记地图定点刷枪；投送完成前禁止拾取，不占用地面存留寿命。</summary>
        public bool WeaponAirdrop;
        public float DeliveryRemainingSeconds;
        public int Id;
        public ArcadeDropDefinition Definition;
        public Vector2 Position;
        /// <summary>地面存留秒数；所有来源统一30秒，暂停冻结，归零即回收。</summary>
        public float RemainingSeconds = 30f;

        public void Reset()
        {
            Id = 0;
            Definition = default;
            Position = default;
            WeaponAirdrop=false;DeliveryRemainingSeconds=0;
            RemainingSeconds = 30f;
        }
    }

    /// <summary>单个强化道具的竞技场瞬时状态；同类再次拾取会刷新该实例，不负责跨安全节点持久化。</summary>
    internal sealed class ArcadePowerupRuntime
    {
        public string ProductId;
        public ArcadePowerupDefinition Definition;
        /// <summary>剩余有效时间，单位秒；归零后由战斗系统统一移除。</summary>
        public float RemainingSeconds;
        /// <summary>护盾或木桶的剩余触发次数；不使用次数的强化保持为零。</summary>
        public int Charges;
        /// <summary>周期效果距离下一次结算的秒数。</summary>
        public float CooldownSeconds;
        /// <summary>哨戒炮、诱饵等固定效果的竞技场坐标。</summary>
        public Vector2 Position;
        /// <summary>强化最后一次实际发射的竞技场方向；零表示尚未开火，只用于视觉朝向。</summary>
        public Vector2 ShotDirection;
    }

    /// <summary>战斗系统拥有的临时圆形或线段领域，包含预警、寿命与伤害窗口。</summary>
    internal sealed class ArcadeContentAreaRuntime
    {
        /// <summary>地图内稳定领域编号与事件种类。</summary>
        public int Id, Kind;
        /// <summary>圆心或胶囊起终点，均为竞技场米。</summary>
        public Vector2 Position, End;
        /// <summary>圆半径或线半宽及最大生命比例伤害。</summary>
        public float Radius, DamageRatio;
        /// <summary>活动时钟上的预警结束和领域释放时刻。</summary>
        public double WarningUntil, ExpiresAt;
        /// <summary>是否采用胶囊接触，以及是否同时结算敌人伤害。</summary>
        public bool Segment, EnemyDamage;
    }
    /// <summary>保存当前竞技场全部瞬时战斗数据；退出竞技场时整体清空，永不进入安全节点文件。</summary>
    internal sealed class ArcadeCombatRuntime
    {
        /// <summary>本局新商店版本与独占工作态，外部保存只能使用捕获的深拷贝。</summary>
        public int SlayerMartRulesVersion;
        public FirepowerLoop.Arcade.ArcadeSlayerMartState SlayerMart;
        /// <summary>商店返回目标只在现场保留，购买和进店不建立独立安全节点。</summary>
        public int SlayerMartReturnWave;
        public bool SlayerMartRegionPrepared;
        /// <summary>场景准备失败保留当前账本和期限，显式重试只重建资源。</summary>
        public string SlayerMartRegionError;
        /// <summary>下轮资源就绪时交付加命，再随同一个轮前候选保存。</summary>
        public bool SlayerMartRoundBenefitsPending;
        /// <summary>当前同步风暴伤害作用域，不保存，避免自身及连锁死亡充能。</summary>
        public bool SlayerMartStormDamage;
        /// <summary>本轮内容活动时钟；加载、清场和暂停由系统门禁停止推进。</summary>
        public FirepowerLoop.Arcade.ArcadeContentClock ContentClock = new();
        /// <summary>本地图独占领域，离场整体释放，不写入存档。</summary>
        public readonly List<ArcadeContentAreaRuntime> ContentAreas = new();
        public readonly List<FirepowerLoop.Arcade.ArcadeContentAreaSnapshot> ContentAreaSnapshots = new();
        /// <summary>冰面速度、寄生/孢子减速及事件锚点均只属于本次地图。</summary>
        public Vector2 ContentVelocity, ContentEventAnchor, ContentEventTarget;
        public float ContentSlowRemaining, ContentBumperCooldown;
        public int ContentSpawnSequence, ContentNextAreaId = 1, ContentEventCycle = -1, ContentEventTargetCycle = -1;
        /// <summary>独立主题Boss位于第四轮之后，不增加普通轮次。</summary>
        public bool ThemeBossStarted, ThemeBossDefeated;
        public int ContentBossEnemyId;
        /// <summary>地面桶属于当前稳定地图，不计敌数，也不参与角色阻挡。</summary>
        public string GroundBarrelMapId;
        public int GroundBarrelLayoutVersion;
        public readonly Dictionary<int, Vector2> GroundBarrels = new();
        /// <summary>仅当前地图内已毁索引，重复准备/宝藏返回不重新补发。</summary>
        public readonly HashSet<int> DestroyedGroundBarrelIds = new();
        /// <summary>危险与静态桶的只读表现缓存，避免逐帧分配集合。</summary>
        public readonly List<FirepowerLoop.Arcade.ArcadeContentAreaSnapshot> ContentPresentationSnapshots = new();
        public bool CratesInitialized;
        /// <summary>本场剩余箱子，跨普通轮保留，整场释放或回滚时丢弃。</summary>
        public readonly Dictionary<int, FirepowerLoop.Arcade.ArcadeCrateState> Crates = new();
        /// <summary>本竞技场临时共享实例；整场结束或回滚时丢弃，不单独落盘。</summary>
        public FirepowerLoop.Arcade.ArcadeTreasureRoomState TreasureRoom;
        public bool TreasureInitialized;
        public bool TreasureDiscovered;
        public bool TreasureCleared;
        /// <summary>本次保底奖励房已结束；返回后不再自动重复进入，不进入安全存档。</summary>
        public bool TreasureCompleted;
        public bool InTreasureRoom;
        public int PresentedRegion;
        /// <summary>四轮完成后独立 Boss 战；不改变 WaveIndex 或场序。</summary>
        public bool IsFinalBoss;
        public int FinalBossEnemyId;
        public bool FinalBossDefeated;
        public float ReinforcementCooldown;
        /// <summary>所有危险已停止，等待实体出口；选择只在完成事务保存成功后持久化。</summary>
        public bool AwaitingExit;
        public int SelectedExitConfigNumber;
        public int ArenaNumber;
        public bool IsChallenge;
        public string ChallengeId;
        public float ChallengeSeconds;
        /// <summary>Slayer独立阶段与队伍容错，不等同于主线轮或旧清怪挑战。</summary>
        public FirepowerLoop.Arcade.ArcadeSlayerChallengeState SlayerChallenge;
        /// <summary>独立Guardian攻击现场与敌人ID，不进入主线节点。</summary>
        public FirepowerLoop.Arcade.ArcadeSlayerGuardianState SlayerGuardian;
        public int SlayerGuardianEnemyId;
        public int WaveIndex;
        public Vector2 PlayerPosition;
        public Vector2 AimDirection = Vector2.up;
        public Vector2 MoveDirection;
        /// <summary>当前帧的持续开火意图，由鼠标按键或右摇杆越过死区产生。</summary>
        public bool Firing;
        public int PlayerHealth;
        public int PlayerMaxHealth;
        public float PlayerInvulnerability;
        public float RespawnRemaining;
        public float FireCooldown;
        /// <summary>每次规则层实际生成至少一枚玩家投射物后递增；仅供表现端去重播放开火反馈。</summary>
        public int PlayerFireSequence;
        public float DashRemaining;
        /// <summary>本次冲刺开始时锁定的竞技场单位方向，与后续移动和瞄准输入独立。</summary>
        public Vector2 DashDirection;
        public float TemporaryWeaponRemaining;
        /// <summary>单人视角道具剩余秒数；暂停冻结，退出/死亡/离场清零，独立于临时枪且不保存。</summary>
        public float FirstPersonRemaining;
        public string TemporaryWeaponId;
        /// <summary>允许不同强化并存；键为行为类型，保证同类重复拾取只刷新而不叠出多个实例。</summary>
        public readonly Dictionary<global::ArcadePowerupBehavior, ArcadePowerupRuntime> Powerups =
            new Dictionary<global::ArcadePowerupBehavior, ArcadePowerupRuntime>();
        public int Bombs;
        public int Dashes;
        public long ArenaGold;
        public float SpawnCooldown;
        /// <summary>本轮实际入场序号；独立于弹丸/掉落实体 ID，确保岸边与水侧交替且不改变清场计数。</summary>
        public int IslandSpawnSequence;
        /// <summary>单人海岛转场期间冻结所有局内时钟；资源和轮级节点均成功后才解除。</summary>
        public bool IslandTransitionPending;
        /// <summary>地图资源、相机或安全节点准备失败的原始原因；新请求及重试时清空。</summary>
        public string IslandPreparationError;
        /// <summary>宝藏区域资源准备冻结，仅冻结加载，不冻结房间实际活动时间。</summary>
        public bool IslandRoomTransitionPending;
        /// <summary>转场失败保留同一候选；重试不能重新补给、发奖或重建敌群预算。</summary>
        public bool IslandTransitionViewPrepared;
        /// <summary>普通清场的单出口状态；不与整场完成出口混用。</summary>
        public bool IslandRoundAwaitingExit;
        /// <summary>固定入口批次及预告剩余秒数；换向停止旧入口生成，保留在场敌人。</summary>
        public int IslandEntryBatch;
        /// <summary>当前轮完整有限预算，按已出队数量划分固定入口批次。</summary>
        public int IslandWaveBudget;
        /// <summary>当前入口批次生成前的剩余预告秒数。</summary>
        public float IslandEntryWarningRemaining;
        /// <summary>第四图初始预警期间只显示边界，不结算营火伤害。</summary>
        public float IslandCampfireWarningRemaining;
        /// <summary>宝藏内或转场时仅释放相机实例，保留视角效果逻辑时长。</summary>
        public bool IslandFirstPersonSuspended;
        public float WaveTransitionRemaining;
        public bool WaveClearAwarded;
        /// <summary>清场后须先松开射击再按下才提前推进，避免持续射击跳过休整。</summary>
        public bool RestAttackReleased;
        /// <summary>营火单次伤害节流秒数；权威活动帧推进，宝藏房与休整期间停止伤害。</summary>
        public float CampfireCooldown;
        public bool CompletionSubmitted;
        public bool CompletionAwaitingRetry;
        public bool ArenaClearAwarded;
        public int NextEntityId = 1;
        /// <summary>本场活动战斗累计秒数，暂停与复活等待不增长；不写安全节点。</summary>
        public double HazardElapsedSeconds;
        public readonly List<FirepowerLoop.Arcade.ArcadeHazardState> Hazards = new List<FirepowerLoop.Arcade.ArcadeHazardState>();
        public readonly Dictionary<int, Vector2> HazardPreviousEnemyPositions = new Dictionary<int, Vector2>();
        public readonly Queue<string> SpawnQueue = new Queue<string>();
        public readonly Dictionary<int, ArcadeEnemyRuntime> Enemies = new Dictionary<int, ArcadeEnemyRuntime>();
        public readonly Dictionary<int, ArcadeProjectileRuntime> Projectiles = new Dictionary<int, ArcadeProjectileRuntime>();
        public readonly Dictionary<int, ArcadeDropRuntime> Drops = new Dictionary<int, ArcadeDropRuntime>();
        /// <summary>当前地图的武器错峰计划；不写存档，切轮替换，释放地图时丢弃。</summary>
        public FirepowerLoop.Arcade.ArcadeWeaponSpawnSchedule WeaponSpawns;
    }

    /// <summary>街机战斗现场的唯一瞬时状态所有者；规则推进由 ArcadeCombatSystem 完成。</summary>
    public sealed class ArcadeCombatModel : AbstractModel
    {
        internal ArcadeCombatRuntime Runtime { get; private set; }
        /// <summary>单人当前战斗的阶段提示消费记录，独立于 HUD 实例生命周期。</summary>
        internal ArcadeStageNoticeState StageNotices { get; } = new();
        public bool IsActive => Runtime != null;
        protected override void OnInit() { }
        internal void Begin(ArcadeCombatRuntime runtime) { StageNotices.Clear(); Runtime = runtime; }
        internal void Clear() { StageNotices.Clear(); Runtime = null; }
    }
}
