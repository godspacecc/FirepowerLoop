using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>街机战场表现端口；把规则快照转换为正式玩家、敌人、投射物和掉落表现。</summary>
    public interface IArcadeCombatViewPort : IUtility
    {
        bool IsActive { get; }
        /// <summary>幸存者远近场使用完整相机地面范围；未知相机不进行角色休眠。</summary>
        bool TryGetSurvivorViewBounds(out Rect bounds){bounds=default;return false;}
        /// <summary>返回完整表现创建结果，失败时权威远场记录不得丢弃。</summary>
        bool TryWakeSurvivorEnemy(int id,string configId,Vector2 position,int health,int maximum,int sequence,out string error){error="表现端口未支持尸潮";return false;}
        /// <summary>仅休眠表现，不发生死亡、掉落或击杀。</summary>
        void SuspendSurvivorEnemy(int id) { }
        /// <summary>加载界面预热首批角色，不创建权威敌人。</summary>
        bool TryPrewarmSurvivorEnemy(string configId,int count,out bool complete,out string error){complete=true;error=null;return true;}
        /// <summary>自动武器只锁定已进入画面的目标；端口未提供可见性时安全停火。</summary>
        bool IsArenaTargetVisible(Vector2 position) => false;
        /// <summary>出生体积完全在扩大的相机视锥外才允许生成；未知镜头时安全等待。</summary>
        bool IsArenaSpawnHidden(Vector2 position,float radius) => false;
        /// <summary>相机准备事务不拥有时长；未支持的表现端口拒绝进入。</summary>
        void Begin(int arenaNumber, float arenaHalfWidth, float arenaHalfHeight,
            IReadOnlyList<ArcadeObstacleDefinition> obstacles);
        void End();
        /// <summary>仅幸存者开局接管昼夜光照。</summary>
        void BeginSurvivorDayNight() { }
        /// <summary>只读幸存者本局有效时间，供昼夜表现投影。</summary>
        void SetSurvivorTime(double elapsedSeconds) { }
        /// <summary>只显示本机区域入口和剩余物品；不能从此端口领取、计时或发放积分。</summary>
        void SetTreasureRoom(FirepowerLoop.Arcade.ArcadeTreasureRoomParameters parameters,
            bool inRoom, bool discovered, bool open, IReadOnlyList<int> remainingIds) { }
        /// <summary>只呈现清场或胜利出口；触发与目的地选择由战斗权威裁决。</summary>
        void SetExits(IReadOnlyList<int> destinations, float halfHeight, bool victory) { }
        /// <summary>仅显示权威周期；不自行推进机关时间、不裁决伤害。</summary>
        void SetHazards(IReadOnlyList<FirepowerLoop.Arcade.ArcadeHazardParameters> hazards, double elapsedSeconds) { }
        /// <summary>电塔权威命中的只读反馈，不裁决伤害。</summary>
        void PlayElectricTowerHit(int hazardId, Vector2 position) { }
        /// <summary>只按权威配置显示岛岸与第四轮营火，不从视觉碰撞计算伤害。</summary>
        void SetOpening(FirepowerLoop.Arcade.ArcadeOpeningParameters parameters, bool fireActive) { }
        /// <summary>按权威布局准备当前轮，资源就绪后才允许规则开始活动。</summary>
        void SetRoundLayout(FirepowerLoop.Arcade.ArcadeRoundLayoutParameters layout) { }
        bool IsRoundLayoutReady => true;
        string RoundLayoutLoadError => null;
        /// <summary>投影环境活动时钟及区域，伤害仍由单人或服务器裁决。</summary>
        void SetRoundContent(double elapsed, IReadOnlyList<FirepowerLoop.Arcade.ArcadeContentAreaSnapshot> areas) { }
        void SetHerds(long mapId,IReadOnlyList<FirepowerLoop.Arcade.ArcadeHerdSnapshot> cows) { }
        void SetPlayerHerdImpact(long playerId,FirepowerLoop.Arcade.ArcadeHerdImpactState state) { }
        void SetEnemyHerdImpact(int id,FirepowerLoop.Arcade.ArcadeHerdImpactState state) { }
        /// <summary>只采样右路海中进场的权威年龄，不自行计算出生或进度。</summary>
        void SetEnemyDockSeaEntry(int id,FirepowerLoop.Arcade.ArcadeDockSeaEntryState state) { }
        /// <summary>权威抓挠进度和当前目标方向，旧端口可忽略。</summary>
        void SetEnemyMelee(int id,float duration,float remaining,float recovery,Vector2 direction) { }
        /// <summary>幸存者实例体型与攻击快照独立，不修改源Prefab。</summary>
        void SetEnemyScale(int id,Vector3 scale) { }
        /// <summary>消费幸存者场内爬出进度，不在表现层推进计时。</summary>
        void SetEnemyGroundEntry(int id,SurvivorGroundEntryState state) { }
        void SetHerdActivityPaused(bool paused) { }
        /// <summary>只接收权威实际掉血的牛撞，致死也播放一次短反馈，不由模型接触推算。</summary>
        void PlayHerdContact(Vector2 position) { }
        void SetEnemyContent(int id, int specialKind, int bossKind, bool warning) { }
        /// <summary>只采样潜地怪/犬的当前权威年龄和锁向，不推进技能。</summary>
        void SetCreatureState(int id,float age,Vector2 facing) { }
        /// <summary>只投影终局小龙的权威阶段、年龄和锁向，不改变战斗规则。</summary>
        void SetFinalDragonState(int id,float age,ArcadeBossPhase phase,float remaining,ArcadeBossDefinition boss,Vector2 facing) { }
        /// <summary>四种采用角色只读投影活动年龄、朝向与真实近战窗口。</summary>
        void SetAdoptedCreatureState(int id,float age,Vector2 facing,float meleeDuration,float meleeRemaining,float meleeRecovery) { }
        /// <summary>按权威年龄投影墓穴、乌鸦与蓝影，不自行推进或裁决重生。</summary>
        void SetNextStageEnemyState(int id,int kind,FirepowerLoop.Arcade.ArcadeStageEnemyState state) { }
        /// <summary>本地海岛局部地图准备；其他表现端口沿用原行为。</summary>
        void SetIslandRound(int roundNumber) { }
        bool IsIslandRoundReady => true;
        bool IsIslandTreasureReady => true;
        /// <summary>区域资源就绪才开始商店时钟，旧端口不会误报已支持新区域。</summary>
        bool IsSlayerMartRegionReady => false;
        string SlayerMartRegionLoadError => null;
        void SetSlayerMartRegion(FirepowerLoop.Arcade.ArcadeSlayerMartRegionLayout layout,IReadOnlyList<int> remainingIds) { }
        string IslandRoundLoadError => null;
        void RetryIslandRoundLoad() { }
        void SetIslandCampfireWarning(bool warning) { }
        void SetIslandEntryWarning(Vector2 position, bool visible) { }
        /// <summary>只显示权威剩余箱子和护甲，命中与发奖由领域裁决。</summary>
        void SetBabylon(FirepowerLoop.Arcade.ArcadeBabylonParameters parameters, IReadOnlyList<int> remainingIds) { }
        void SetEnemyArmor(int id, Vector2 position, int health) { }
        bool TryScreenToArena(Vector2 screenPosition, out Vector2 position);
        bool TryGetPlayerPosition(out Vector2 position) { position = default; return false; }
        void SetPlayer(Vector2 position, Vector2 facing, bool visible, bool invulnerable);
        /// <summary>移动结算后只刷新朝向和保护状态，不再次驱动位移或清空腿部动画。</summary>
        void SetPlayerCombatState(Vector2 facing,bool invulnerable) { }
        /// <summary>可救援标记只读取服务器状态，本机为零号，队友使用玩家 ID。</summary>
        void SetPlayerRescue(long playerId,bool active) { }
        /// <summary>只读有效冲刺状态；零号为本机，正数为联机队友，不从无敌或移速推断。</summary>
        void SetPlayerDash(long playerId, Vector2 position, Vector2 direction, float remaining, bool visible) { }
        void SetPlayerWeapon(string weaponId) { }
        void SetPlayerWeapon(ArcadeWeaponDefinition weapon) => SetPlayerWeapon(weapon.Id);
        /// <summary>同步规则层开火序列；序列只用于表现去重，不参与射速或伤害判定。</summary>
        void SetPlayerFireSequence(int sequence) { }
        /// <summary>单人街机传入本次权威攻击间隔（秒），仅用于挥刀节奏；其他表现端口沿用原反馈。</summary>
        void SetPlayerFireSequence(int sequence, float attackIntervalSeconds) => SetPlayerFireSequence(sequence);
        /// <summary>投射物确实引爆时播放表现反馈，不参与范围伤害结算。</summary>
        void PlayProjectileExplosion(Vector2 position, float radius) { }
        /// <summary>紫级进化只消费权威区域、弹丸与爆炸事件，默认替身无需加载资源。</summary>
        void SetWeaponBurnArea(int id, Vector2 position, float radius, float remainingSeconds) { }
        void SetEvolvedPiercing(int id, Vector2 position, Vector2 direction, float radius) { }
        void SetEvolvedGrenade(int id, Vector2 position, Vector2 direction, bool child, float phase) { }
        void PlayEvolvedGrenadeExplosion(Vector2 position, float radius, bool child) { }
        /// <summary>局内快捷炸弹成功消耗后播放三阶段视觉与独立清场音效。</summary>
        void PlayBombExplosion(Vector2 position) { }
        /// <summary>已结算的单次命中声音，不参与伤害与投射物生命周期。</summary>
        void PlayImpactSound(Vector2 position, bool melee) { }
        /// <summary>普通枪弹实际撞障碍的接触反馈；不作为命中敌人或伤害事件。</summary>
        void PlayProjectileSurfaceImpact(Vector2 position, Vector2 incomingDirection) { }
        void SetPlayerHealth(int current, int maximum);
        /// <summary>仅投影权威换弹状态，沿用主角动作与进度圈。</summary>
        void SetPlayerReload(bool active, float progress) { }
        /// <summary>幸存者弹夹快照，不改变其他模式的弹药规则。</summary>
        void SetPlayerAmmo(bool visible,int current,int capacity) { }
        void SetEnemy(int id, string configId, Vector2 position, int currentHealth, int maximumHealth,
            bool telegraph = false, int attackSequence = 0, bool persistentHealthBar = false);
        void RemoveEnemy(int id, bool playDeath = false);
        /// <summary>只传递权威死亡的表现方向与爆炸类别，兼容既有动画端口。</summary>
        void RemoveEnemy(int id, bool playDeath, Vector2 direction, bool explosive) => RemoveEnemy(id, playDeath);
        /// <summary>火焰命中只控制表现；旧实现可忽略，不回写生命。</summary>
        void SetEnemyBurning(int id,float remainingSeconds) { }
        void RemoveEnemy(int id,bool playDeath,Vector2 direction,bool explosive,bool flameKill) => RemoveEnemy(id,playDeath,direction,explosive);
        /// <summary>接收权威死亡力度类别，普通端口可沿用旧表现。</summary>
        void RemoveEnemy(int id,bool playDeath,Vector2 direction,bool explosive,bool flameKill,int impactKind) => RemoveEnemy(id,playDeath,direction,explosive,flameKill);
        /// <summary>无人机弹体只投影权威弹道，使用支援高度，避免从角色枪口出现。</summary>
        void SetDroneProjectile(int id,Vector2 position,Vector2 direction,string resource,float height)=>SetProjectile(id,false,position,direction,resource);
        void SetProjectile(int id, bool hostile, Vector2 position, Vector2 direction);
        void SetProjectile(int id, bool hostile, Vector2 position, Vector2 direction, string visualResourceName) =>
            SetProjectile(id, hostile, position, direction);
        /// <summary>玩家武器标记只用于可见弹丸从枪口过渡，不参与权威命中。</summary>
        void SetProjectile(int id, bool hostile, Vector2 position, Vector2 direction, string visualResourceName,
            bool fromPlayerWeapon) => SetProjectile(id, hostile, position, direction, visualResourceName);
        void RemoveProjectile(int id);
        void SetDrop(int id, ArcadeDropDefinition definition, Vector2 position);
        /// <summary>只呈现经验地面模型与数量，结算由幸存者系统裁决。</summary>
        void SetExperienceDrop(int id,string visualPrefabName,Vector2 position,long experience) { }
        void RemoveDrop(int id);
        /// <summary>切关黑幕只控制可见性；地图事务须等待遮挡后才替换世界。</summary>
        void BeginRoundTransition() { }
        bool IsRoundTransitionCovered => true;
        void EndRoundTransition() { }
        /// <summary>空投开盖阶段只做显示，拾取资格由权威剩余量裁决。</summary>
        void SetDropDelivery(int id,bool airdrop,float remaining,float duration) { }
        /// <summary>显示权威剩余寿命的透明度，不参与领取裁决。</summary>
        void SetDropOpacity(int id, float opacity) { }
        /// <summary>只淡出宝藏房地面物资，入口与返回出口保持可见。</summary>
        void SetTreasureRewardOpacity(float opacity) { }
        /// <summary>仅在权威补给拾取成功后调用，added 是上限截断后的实际增加次数。</summary>
        void PlaySupplyPickup(ArcadeDropDefinition definition, int added) { }
        /// <summary>权威提交后的实际积分与金币分别传入；不能从掉落面值推断。</summary>
        void PlayTreasurePickup(long score, long gold) { }
        /// <summary>成功拾取时携带竞技场坐标；旧表现端仍可沿用数值反馈。</summary>
        void PlayTreasurePickup(long score, long gold, Vector2 position) => PlayTreasurePickup(score, gold);
        /// <summary>仅接受产品定义中的稳定 ID，显示名称沿用现有本地化定义。</summary>
        void PlayProductPickup(string productId) { }
        void BeginEffectSync() { }
        /// <summary>风暴权威半径的专属表现，不复用普通电球。</summary>
        void SetSlayerMartStorm(long ownerPlayerId,Vector2 position,float radius) { }
        /// <summary>稳定实例与接触体身份只用于表现，位置由战斗权威给定。</summary>
        void SetSlayerMartSupport(long ownerPlayerId,string instanceId,int bodyIndex,ArcadePowerupDefinition definition,
            Vector2 bodyPosition,Vector2 shotDirection) { }
        /// <summary>只投影无人机权威阶段与强度；不参与选敌、计时或伤害。</summary>
        void SetRoundReward(FirepowerLoop.Arcade.ArcadeRoundRewardState state,FirepowerLoop.Arcade.ArcadeRoundRewardTuning tuning) { }
        void SetStagePortal(bool visible,Vector2 position,float seconds,float appearSeconds) { }
        void SetDrone(long ownerPlayerId,ArcadePowerupDefinition definition,Vector2 position,Vector2 direction,int phase,int powerTier) { }
        /// <summary>紫级副机单独同步；旧表现端仍可显示主机。</summary>
        void SetDroneUnit(long ownerPlayerId,int unitIndex,ArcadePowerupDefinition definition,Vector2 position,Vector2 direction,int phase,int powerTier)
        {if(unitIndex==0)SetDrone(ownerPlayerId,definition,position,direction,phase,powerTier);}
        void SetPowerup(ArcadePowerupDefinition definition, Vector2 anchorPosition) { }
        void SetPowerup(long ownerPlayerId, ArcadePowerupDefinition definition, Vector2 anchorPosition) =>
            SetPowerup(definition, anchorPosition);
        /// <summary>发射方向由战斗权威提供；零方向保留既有朝向。</summary>
        void SetPowerup(ArcadePowerupDefinition definition, Vector2 anchorPosition, Vector2 shotDirection) =>
            SetPowerup(definition, anchorPosition);
        void SetPowerup(long ownerPlayerId, ArcadePowerupDefinition definition, Vector2 anchorPosition,
            Vector2 shotDirection) => SetPowerup(ownerPlayerId, definition, anchorPosition);
        /// <summary>护盾挡伤后播放短暂闪光；不改变护盾次数或伤害结算。</summary>
        void PlayShieldBlock(long ownerPlayerId, Vector2 anchorPosition) { }
        void SetSupport(ArcadeSupportVisualDefinition definition, Vector2 playerPosition) { }
        void SetSupport(long ownerPlayerId, ArcadeSupportVisualDefinition definition, Vector2 playerPosition) =>
            SetSupport(definition, playerPosition);
        void EndEffectSync() { }
        void SetRemotePlayer(long playerId, Vector2 position, Vector2 facing, bool visible,
            bool invulnerable, int currentHealth, int maximumHealth);
        /// <summary>同步远端武器和权威攻击序列；不裁决近战命中或投射物生成。</summary>
        void SetRemotePlayerCombat(long playerId, ArcadeWeaponDefinition weapon, int fireSequence) { }
        void RemoveRemotePlayer(long playerId);
        /// <summary>表现暂停不能继续播放持枪循环声；无持续音效的替身可保持空实现。</summary>
        void StopPlayerFireAudio() { }
        void TickPlayerFeedback(float deltaTime);
    }

    /// <summary>把街机临时表现及专属场地覆盖层挂到正式地图动态根；沿用正式玩家、相机及灯光。</summary>
    public sealed partial class ArcadeCombatViewController : IArcadeCombatViewPort, IDisposable
    {
        private const string ObstacleVisualAddress =
            "Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet";
        private const float ObstacleVisualBaseWidth = 1.6f;
        private const float ObstacleVisualBaseLength = 4f;
        private const float ShieldBlockFlashSeconds = 0.3f;
        /// <summary>街机平面角色每帧向地面保持的小幅接触距离，由 CharacterController 阻挡，避免出生或传送后悬空。</summary>
        private const float PlayerGroundContactDistance = 0.1f;

        private GameObject root;
        private Transform mapRoot;
        private ICharacterMotorPort playerMotor;
        private IArcadeEnemyPresentationPort enemyPresentation;
        private IArcadeProjectilePresentationPort projectilePresentation;
        private IArcadeLootPresentationPort lootPresentation;
        private readonly Dictionary<long, RemotePlayerEntry> remotePlayers = new();
        private readonly Dictionary<string, EffectVisualEntry> effectVisuals = new(StringComparer.Ordinal);
        private readonly List<string> staleEffectVisualKeys = new();
        private int remoteGeneration;
        private int effectSyncRevision;
        private int obstacleVisualGeneration;
        private ObstacleVisualLoadState obstacleVisualLoad;
        private bool hasPlayerHealthBaseline;
        /// <summary>本机首帧仅建立存活基线，重连时不补播旧死亡。</summary>
        private bool hasPlayerAliveBaseline;
        private int lastPlayerHealth;
        private bool hasPlayerFireSequence;
        private int lastPlayerFireSequence;
        /// <summary>当前武器 ID 与上次开火音效时刻仅用于本场战斗的声音选择、限频。</summary>
        private string currentWeaponId;
        /// <summary>当前正式武器的基础开火间隔，仅供持续声音跨发衔接，不参与战斗计时。</summary>
        private float currentWeaponFireIntervalSeconds;
        /// <summary>由完整武器定义确定反馈类型，避免冷兵器被枪械反馈入口忽略。</summary>
        private bool currentWeaponIsMelee;
        private float lastFireSoundTime = float.NegativeInfinity;
        /// <summary>两类爆炸声各自的上次播放时刻，避免高射速镭射弹堆积长尾音效。</summary>
        private float lastExplosionSoundTime = float.NegativeInfinity;
        private float lastSciFiExplosionSoundTime = float.NegativeInfinity;
        private readonly HashSet<int> observedProjectileIds = new();
        private readonly Dictionary<int, ProjectileMuzzleDeparture> projectileMuzzleDepartures = new();
        private Vector3 lastShotMuzzle;
        private bool hasLastShotMuzzle;
        /// <summary>本场机关表现及材质的唯一所有者；End 先释放，再销毁场地根。</summary>
        private ArcadeHazardPresentation hazardPresentation;
        /// <summary>出口功能表现由本场根独占；材质和标记在 End 成对释放。</summary>
        private readonly List<GameObject> exitVisuals = new();
        private Material exitMaterial;
        private string exitVisualIdentity;
        private const float ProjectileMuzzleBlendDistance = 2f;

        /// <summary>把局内武器 ID 映射到已筛选的试听音效；未知武器保持静音。</summary>
        private static string ResolveFireSoundName(string weaponId)
        {
            switch (weaponId)
            {
                case "arcade.weapon.basic": return "ArcadeFireBasic";
                case "arcade.weapon.rapid": return "ArcadeFireRapid";
                case "arcade.weapon.spread": return "ArcadeFireSpread";
                case "arcade.weapon.piercing": return "ArcadeFirePiercing";
                case "arcade.weapon.rocket":
                case "arcade.weapon.grenade": return "ArcadeFireLauncher";
                case "arcade.weapon.raygun": return "ArcadeFireRaygun";
                default: return null;
            }
        }

        /// <summary>按已导入音频的尾音长度限制高速武器音效；规则层仍逐发结算。</summary>
        private static bool CanPlayFireSound(string weaponId, float elapsed)
        {
            switch (weaponId)
            {
                case "arcade.weapon.rapid": return elapsed >= 0.18f;
                case "arcade.weapon.raygun": return elapsed >= 0.25f;
                default: return true;
            }
        }

        /// <summary>单枚可见弹丸的枪口起点和首次规则位置；只在表现过渡期间存在。</summary>
        private readonly struct ProjectileMuzzleDeparture
        {
            public ProjectileMuzzleDeparture(Vector3 muzzle, Vector3 authoritativeStart)
            {
                Muzzle = muzzle;
                AuthoritativeStart = authoritativeStart;
            }
            public Vector3 Muzzle { get; }
            public Vector3 AuthoritativeStart { get; }
        }

        public bool IsActive => root != null;

        /// <summary>只把已有基线上的可见生命下降解释为受击；首帧、治疗和隐藏态都返回零。</summary>
        internal static int CalculateHealthLoss(bool hasBaseline, int previous, int current, bool visible = true)
        {
            return hasBaseline && visible ? Mathf.Max(0, previous - current) : 0;
        }

        public void Begin(int arenaNumber, float arenaHalfWidth, float arenaHalfHeight,
            IReadOnlyList<ArcadeObstacleDefinition> obstacles)
        {
            End();
            var architecture = Game.Interface;
            var mapModel = architecture.GetModel<MapSceneModel>();
            var mapSystem = architecture.GetSystem<MapSceneSystem>();
            if (mapModel.CurrentMapId != MapIds.Arcade || mapModel.CurrentMode != MapPreparationMode.ArcadePlayerOnly ||
                mapModel.CurrentHandle?.Context?.DynamicObjectRoot == null)
                throw new InvalidOperationException("街机表现启动失败：正式街机地图尚未提交");
            if (!architecture.GetUtility<IPlayerRuntimePortProvider>().TryGetMotorPort(
                    architecture.GetModel<PlayerModel>().PlayerUnitId,
                    mapSystem.CurrentMapInstanceId,
                    out playerMotor) || playerMotor == null || !playerMotor.IsValid)
                throw new InvalidOperationException("街机表现启动失败：正式玩家端口不可用");
            enemyPresentation = architecture.GetUtility<IArcadeEnemyPresentationPort>();
            projectilePresentation = architecture.GetUtility<IArcadeProjectilePresentationPort>();
            lootPresentation = architecture.GetUtility<IArcadeLootPresentationPort>();
            if (enemyPresentation == null || projectilePresentation == null || lootPresentation == null)
                throw new InvalidOperationException("街机表现启动失败：敌人、投射物或掉落表现端口不可用");

            mapRoot = mapModel.CurrentHandle.Context.DynamicObjectRoot;
            islandBaseArenaRoot = mapModel.CurrentHandle.Context.transform;
            root = new GameObject("ArcadeCombatRuntime");
            root.transform.SetParent(mapRoot, false);
            if (!architecture.GetUtility<IRuntimeConfigProvider>().TryGetArcadeCatalog(out var boundsCatalog, out var boundsError))
                throw new InvalidOperationException(boundsError);
            earlyStage=boundsCatalog.EarlyStages.FirstOrDefault(p=>p.ArenaConfigNumber==arenaNumber);
            ConfigureRoundBounds(arenaHalfWidth, arenaHalfHeight, boundsCatalog.Rule.PlayerRadius);
            playerMotor.SetArcadePresentationActive(true);
            hasPlayerHealthBaseline = false;
            hasPlayerAliveBaseline = false;
            hasPlayerFireSequence = false;
            currentWeaponId = null;
            currentWeaponIsMelee = false;
            lastFireSoundTime = float.NegativeInfinity;
            lastExplosionSoundTime = float.NegativeInfinity;
            lastSciFiExplosionSoundTime = float.NegativeInfinity;
            hasLastShotMuzzle = false;
            observedProjectileIds.Clear();
            projectileMuzzleDepartures.Clear();
            CreateArenaCollision(arenaNumber, arenaHalfWidth, arenaHalfHeight, obstacles);
            contentArena = arenaNumber;
            // 所有场地都准备公共命中模板；前三场的环境覆盖仍由 Content 内部限定。
            LoadContentAsync(arenaNumber).Forget();
            pendingRegionPosition = true;
        }

        public void End()
        {
            ReleaseSurvivorDayNight();
            enemyPresentation?.SetHerdActivityPaused(false);
            earlyStage=null;
            ReleaseElectricTowerFeedback();
            StopFlamethrowerAudio();
            ReleaseSlayerMartRegion();
            ReleaseRoundContent();
            ReleaseIslandEntryWarning();
            ClearPickupFeedback();
            if (root != null) root.SetActive(false);
            ReleaseIsland();
            ReleaseBabylon();
            ClearTreasureVisuals();
            exitCues.Clear();
            exitCueElapsed = 0;
            foreach (var marker in exitVisuals) DestroyObject(marker);
            exitVisuals.Clear();
            DestroyObject(exitMaterial);
            exitMaterial = null;
            exitVisualIdentity = null;
            hazardPresentation?.Dispose();
            hazardPresentation = null;
            ClearBulletImpacts();
            ClearProjectileBlasts();
            ClearBombVisuals();
            ClearDashVisuals();
            ReleaseContent();
            remoteGeneration++;
            obstacleVisualGeneration++;
            ReleaseObstacleVisualLoad(obstacleVisualLoad);
            obstacleVisualLoad = null;
            foreach (RemotePlayerEntry entry in remotePlayers.Values) ReleaseRemotePlayer(entry);
            remotePlayers.Clear();
            ReleaseAllEffectVisuals();
            enemyPresentation?.ClearArcadeEnemies();
            projectilePresentation?.ClearArcadeProjectiles();
            lootPresentation?.ClearArcadeDrops();
            playerMotor?.HideActionProgress();
            playerMotor?.SetAmmoHud(false,0,0);
            playerMotor?.SetArcadeWeaponPresentation(null);
            playerMotor?.SetArcadePresentationActive(false);
            playerMotor = null;
            hasPlayerHealthBaseline = false;
            hasPlayerAliveBaseline = false;
            hasPlayerFireSequence = false;
            currentWeaponId = null;
            currentWeaponIsMelee = false;
            lastFireSoundTime = float.NegativeInfinity;
            lastExplosionSoundTime = float.NegativeInfinity;
            lastSciFiExplosionSoundTime = float.NegativeInfinity;
            hasLastShotMuzzle = false;
            observedProjectileIds.Clear();
            projectileMuzzleDepartures.Clear();
            enemyPresentation = null;
            projectilePresentation = null;
            lootPresentation = null;
            mapRoot = null;
            islandBaseArenaRoot = null;
            DestroyObject(root);
            root = null;
            // Begin 复用同一端口；退出本地轮图后，下一次联机或普通地图必须回到默认身份。
            islandRound = 0;
            islandCampfireWarning = false;
            islandFireActive = false;
            islandParameters = default;
        }

        public void SetHazards(IReadOnlyList<FirepowerLoop.Arcade.ArcadeHazardParameters> hazards, double elapsedSeconds)
        {
            contentHazards = hazards; contentHazardElapsed = elapsedSeconds;
            if(hazards!=null&&hazards.Count>0)electricHitDefinitions=hazards;
            if (root == null) return;
            if (hazardPresentation == null && hazards.Count > 0)
            {
                // 第二关码头使用已采用的线圈塔，旧主题保留原模板与表现。
                string towerName=contentArena==21?"DocksElectricTower":"ElectricTower";
                hazardPresentation = new ArcadeHazardPresentation(root.transform, contentTemplates?.Find(towerName)?.gameObject);
            }
            hazardPresentation?.Sync(hazards, elapsedSeconds);
            SyncElectricTowerAudio();
        }

        /// <summary>普通出口只保留指路箭头，场景通道后续逐图补充；最终胜利出口沿用现有显示。</summary>
        public void SetExits(IReadOnlyList<int> destinations, float halfHeight, bool victory)
        {
            contentDestinations = destinations.ToArray(); contentExitHeight = halfHeight; contentVictory = victory;
            if (root == null) return;
            var identity = victory ? "victory:" + Loc.T("ui.arcade.exit.victory") : string.Join(",", destinations);
            if (string.Equals(identity, exitVisualIdentity, StringComparison.Ordinal)) return;
            exitVisualIdentity = identity;
            exitCues.Clear();
            exitCueElapsed = 0;
            foreach (var marker in exitVisuals) DestroyObject(marker);
            exitVisuals.Clear();
            int destinationCount = victory ? 1 : destinations.Count;
            if (destinationCount == 0) return;
            bool fourDirections = roundLayout != null || islandRound > 0;
            var count = islandRound > 0 ? FirepowerLoop.Arcade.ArcadeIslandRegion.PassageCount :
                roundLayout != null ? roundLayout.ExitCount : fourDirections ? FirepowerLoop.Arcade.ArcadeRoundLayoutParameters.ExitDirectionCount : destinationCount;
            bool formalExit = roundLayout != null;
            if (contentTemplates == null) { exitVisualIdentity = null; return; }
            // 正式门自带材质；旧圆柱出口也从资源引用复制，Android Host 不依赖 Shader 名称查找。
            if (victory && !formalExit && exitMaterial == null)
            {
                exitMaterial = new Material(contentTemplates.Find("RouteGate/ExitCue/GroundRing").GetComponent<LineRenderer>().sharedMaterial);
                exitMaterial.SetColor("_BaseColor", new Color(.15f, .85f, .55f, 1f));
            }
            for (var index = 0; index < count; index++)
            {
                var point = roundLayout?.GetExitPosition(index, roundHalfWidth, roundHalfHeight, roundPlayerRadius);
                var position = point.HasValue
                    ? new Vector2(point.Value.X, point.Value.Y)
                    : islandRound > 0 && Game.Interface.GetUtility<IRuntimeConfigProvider>().TryGetArcadeCatalog(out var exitCatalog, out _)
                    ? ArcadeIslandLayout.GetExitPosition(islandRound, index, count, exitCatalog.Rule.ArenaHalfWidth,
                        exitCatalog.Rule.ArenaHalfHeight, exitCatalog.Rule.PlayerRadius)
                    : ArcadeCombatSystem.ArcadeExitPosition(index, count, halfHeight);
                GameObject marker;
                if (!victory)
                {
                    marker = CreateRouteExitGuide(position);
                }
                else if (formalExit)
                {
                    marker = CreateContent("RouteGate", new Vector3(position.x, 0, position.y));
                    SetContentLabel(marker, "ui.arcade.exit.victory");
                    BindExitCue(marker);
                }
                else
                {
                    marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    marker.transform.SetParent(root.transform, false);
                    marker.transform.localPosition = new Vector3(position.x, .05f, position.y);
                    marker.transform.localScale = new Vector3(3f, .05f, 3f);
                    marker.GetComponent<Renderer>().sharedMaterial = exitMaterial;
                    DestroyObject(marker.GetComponent<Collider>());
                }
                marker.name = victory ? "ArcadeVictoryExit" : "ArcadeRouteExit_" + destinations[fourDirections ? 0 : index];
                if (fourDirections && index > 0) marker.name += "_" + index;
                // Collider 不参与选择，避免表现对象成为第二权威或阻挡角色进入出口。
                exitVisuals.Add(marker);
            }
        }

        public bool TryScreenToArena(Vector2 screenPosition, out Vector2 position)
        {
            position = Vector2.zero;
            if (mapRoot == null || !Game.Interface.GetSystem<PlayerCameraSystem>().TryCreateAimRay(screenPosition, out var ray)) return false;
            var plane = new Plane(mapRoot.up, mapRoot.position);
            if (!plane.Raycast(ray, out var distance)) return false;
            var point = mapRoot.InverseTransformPoint(ray.GetPoint(distance));
            position = new Vector2(point.x, point.z);
            return true;
        }

        public void SetPlayer(Vector2 position, Vector2 facing, bool visible, bool invulnerable)
        {
            SyncHerdListener(position,visible);
            if(visible){electricListener=position;electricHasListener=true;}
            electricListenerVisible=visible;SyncElectricTowerAudio();
            if (!visible) StopFlamethrowerAudio();
            dashPresentation?.Move(0,position,visible);
            SetExitGuidePlayer(position, visible);
            if (playerMotor == null || !playerMotor.IsValid || mapRoot == null) return;
            bool playTransition=hasPlayerAliveBaseline;
            hasPlayerAliveBaseline=true;
            playerMotor.SetAlivePresentation(visible,playTransition);
            playerMotor.SetArcadeProtectionActive(visible && invulnerable);
            if (!visible)
            {
                if(!playTransition)
                {
                    // 重连已倒地时仍投影权威位置；后续死亡则留在最后实际显示地点。
                    playerMotor.SetArcadeRegionPosition(mapRoot.TransformPoint(new Vector3(position.x,0,position.y)));
                    if(facing.sqrMagnitude>.001f)playerMotor.FaceDirection(mapRoot.TransformDirection(new Vector3(facing.x,0,facing.y)));
                    pendingRegionPosition=false;
                }
                hasPlayerHealthBaseline = false;
                return;
            }
            // 街机权威只有平面坐标，地面是地图局部零高；不能沿用旧出生高度。
            var target = mapRoot.TransformPoint(new Vector3(position.x, 0f, position.y));
            if (pendingRegionPosition)
            {
                // 先留出碰撞皮肤间隙，再通过 Move 落地，不能把胶囊直接嵌入支撑面。
                playerMotor.SetArcadeRegionPosition(target + mapRoot.up * PlayerGroundContactDistance);
                pendingRegionPosition = false;
            }
            // 同一次移动完成水平投影与向下接触，碰撞皮肤厚度仍由角色控制器处理。
            playerMotor.Move(target - playerMotor.Position - mapRoot.up * PlayerGroundContactDistance);
            if (facing.sqrMagnitude > 0.001f)
                playerMotor.FaceDirection(mapRoot.TransformDirection(new Vector3(facing.x, 0f, facing.y)));
        }

        /// <summary>保留本帧实际移动速度，仅应用射击转向和最新无敌保护。</summary>
        public void SetPlayerCombatState(Vector2 facing,bool invulnerable)
        {
            if(playerMotor==null||!playerMotor.IsValid||mapRoot==null)return;
            playerMotor.SetArcadeProtectionActive(invulnerable);
            if(facing.sqrMagnitude>.001f)
                playerMotor.FaceDirection(mapRoot.TransformDirection(new Vector3(facing.x,0,facing.y)));
        }

        /// <summary>复用角色现有换弹动作及默认图标，不创建第二套计时或资源加载。</summary>
        public void SetPlayerReload(bool active,float progress)
        {
            if(playerMotor==null||!playerMotor.IsValid)return;
            if(active)playerMotor.ShowActionProgress(progress,null);
            else playerMotor.HideActionProgress();
        }

        /// <summary>复用玩家端口的世界HUD，离场时由End清理。</summary>
        public void SetPlayerAmmo(bool visible,int current,int capacity)=>playerMotor?.SetAmmoHud(visible,current,capacity);

        public void SetPlayerWeapon(string weaponId)
        {
            RememberWeaponForAudio(weaponId);
            playerMotor?.SetArcadeWeaponPresentation(weaponId);
        }

        public void SetPlayerWeapon(ArcadeWeaponDefinition weapon)
        {
            RememberWeaponForAudio(weapon.Id);
            currentWeaponFireIntervalSeconds = weapon.FireIntervalSeconds;
            currentWeaponIsMelee = weapon.Behavior == global::ArcadeWeaponBehavior.Melee;
            playerMotor?.SetArcadeWeaponPresentation(weapon);
        }

        private void RememberWeaponForAudio(string weaponId)
        {
            if (string.Equals(currentWeaponId, weaponId, StringComparison.Ordinal)) return;
            StopFlamethrowerAudio();
            currentWeaponId = weaponId;
            currentWeaponFireIntervalSeconds = 0f;
            lastFireSoundTime = float.NegativeInfinity;
        }

        /// <summary>只在攻击序列变化时按当前武器播放反馈，同一发多投射物不会重复触发。</summary>
        public void SetPlayerFireSequence(int sequence) => SetPlayerFireSequence(sequence, 0f);

        /// <summary>只在新攻击时按本次间隔压缩近战动作；零值保留联机的原始播放节奏。</summary>
        public void SetPlayerFireSequence(int sequence, float attackIntervalSeconds)
        {
            if (hasPlayerFireSequence ? sequence != lastPlayerFireSequence : sequence > 0)
            {
                // 先记开火瞬间的枪口，再播放会移动挂点的后坐反馈。
                hasLastShotMuzzle = playerMotor != null && playerMotor.IsValid &&
                                    playerMotor.TryGetProjectileOrigin(Vector3.forward, out lastShotMuzzle);
                if (currentWeaponIsMelee)
                {
                    playerMotor?.PlayWeaponSwingFeedback(attackIntervalSeconds);
                    if (playerMotor != null && playerMotor.IsValid)
                        Game.Interface.SendEvent(new CombatSoundCueEvent(CombatSoundCueKind.Swing, 0, playerMotor.Position));
                }
                else playerMotor?.PlayProjectileFireFeedback();
                PlayFireSound();
            }
            lastPlayerFireSequence = sequence;
            hasPlayerFireSequence = true;
        }

        /// <summary>只在单人战场生效；声音由 AudioSystem 归属地图并在卸载时清理。</summary>
        private void PlayFireSound()
        {
            if (root == null || mapRoot == null || playerMotor == null || !playerMotor.IsValid) return;
            if (currentWeaponId == "arcade.weapon.flamethrower")
            {
                PlayFlamethrowerSound(Game.Interface.GetSystem<AudioSystem>(),
                    hasLastShotMuzzle ? lastShotMuzzle : playerMotor.Position);
                return;
            }
            var soundName = ResolveFireSoundName(currentWeaponId);
            if (soundName == null) return;
            var now = Time.unscaledTime;
            if (!CanPlayFireSound(currentWeaponId, now - lastFireSoundTime)) return;
            var origin = hasLastShotMuzzle ? lastShotMuzzle : playerMotor.Position;
            Game.Interface.GetSystem<AudioSystem>()?.PlaySoundAt(soundName, origin, 18f, 10);
            lastFireSoundTime = now;
        }

        /// <summary>街机规则确认命中后才调用；保持既有枪声，额外反馈同一投射物的实际接触。</summary>
        public void PlayImpactSound(Vector2 position, bool melee)
        {
            if (mapRoot == null || root == null) return;
            if (!melee) PlayBulletImpact(position);
            Game.Interface.SendEvent(new CombatSoundCueEvent(melee ? CombatSoundCueKind.MeleeBody : CombatSoundCueKind.BulletBody,
                0, mapRoot.TransformPoint(new Vector3(position.x, 0f, position.y))));
        }

        /// <summary>只有投射物引爆事件调用；保留原半径音色分类，视觉不受音频限频抑制。</summary>
        public void PlayProjectileExplosion(Vector2 position, float radius)
        {
            if (root == null || mapRoot == null || radius <= 0f) return;
            var sciFi = radius <= 1.5f;
            PlayProjectileBlast(position, radius, sciFi);
            var now = Time.unscaledTime;
            var previous = sciFi ? lastSciFiExplosionSoundTime : lastExplosionSoundTime;
            if (now - previous < (sciFi ? 0.4f : 0.15f)) return;
            var soundName = sciFi ? "ArcadeExplosionSciFi" : "ArcadeExplosion";
            var worldPosition = mapRoot.TransformPoint(new Vector3(position.x, 0.75f, position.y));
            Game.Interface.GetSystem<AudioSystem>()?.PlaySoundAt(soundName, worldPosition, 18f, 20);
            if (sciFi) lastSciFiExplosionSoundTime = now;
            else lastExplosionSoundTime = now;
        }

        /// <summary>每次权威成功使用播放一次视觉和音效；不与投射物爆炸限频共用状态。</summary>
        public void PlayBombExplosion(Vector2 position)
        {
            if (root == null || mapRoot == null) return;
            PlayBombVisual(position);
            var worldPosition = mapRoot.TransformPoint(new Vector3(position.x, 0.75f, position.y));
            Game.Interface.GetSystem<AudioSystem>()?.PlaySoundAt("ArcadeBombExplosion", worldPosition, 40f, 30);
        }

        /// <summary>出生可见性只读缓存，避免搜索远端通道时反复分配视锥平面。</summary>
        private readonly Plane[] spawnFrustumPlanes=new Plane[6];

        /// <summary>按完整身体包围盒判断画面外出生，视锥四边额外扩大10%，涵盖镜头本帧移动余量。</summary>
        public bool IsArenaSpawnHidden(Vector2 position,float radius)
            => IsSpawnHiddenByCamera(Camera.main,mapRoot,position,radius,spawnFrustumPlanes);

        /// <summary>只读相机与地图矩阵计算，独立于场景装配，便于验证镜头边缘和无相机情况。</summary>
        private static bool IsSpawnHiddenByCamera(Camera camera,Transform mapRoot,Vector2 position,float radius,Plane[] planes)
        {
            if(mapRoot==null||camera==null||!camera.isActiveAndEnabled||radius<=0)return false;
            var projection=Matrix4x4.Scale(new Vector3(1f/1.2f,1f/1.2f,1))*camera.projectionMatrix;
            GeometryUtility.CalculateFrustumPlanes(projection*camera.worldToCameraMatrix,planes);
            // 普通幸存者敌人以地面到3米的保守体积计算，避免只检测脚底而让头部在画面内冒出。
            var center=mapRoot.TransformPoint(new Vector3(position.x,1.5f,position.y));
            var a=mapRoot.TransformVector(new Vector3(radius,0,0));
            var b=mapRoot.TransformVector(new Vector3(0,1.5f,0));
            var c=mapRoot.TransformVector(new Vector3(0,0,radius));
            var extents=new Vector3(Mathf.Abs(a.x)+Mathf.Abs(b.x)+Mathf.Abs(c.x),
                Mathf.Abs(a.y)+Mathf.Abs(b.y)+Mathf.Abs(c.y),Mathf.Abs(a.z)+Mathf.Abs(b.z)+Mathf.Abs(c.z));
            return !GeometryUtility.TestPlanesAABB(planes,new Bounds(center,extents*2));
        }

        /// <summary>按正式战场坐标投影目标身体，画面四边留出5%余量，避免刚露边就被击杀。</summary>
        public bool IsArenaTargetVisible(Vector2 position)
        {
            var camera = Camera.main;
            if (mapRoot == null || camera == null || !camera.isActiveAndEnabled) return false;
            var point = camera.WorldToViewportPoint(mapRoot.TransformPoint(new Vector3(position.x, .8f, position.y)));
            return point.z >= camera.nearClipPlane && point.z <= camera.farClipPlane &&
                point.x >= .05f && point.x <= .95f && point.y >= .05f && point.y <= .95f;
        }

        /// <summary>投影四个相机角到角色脚下平面；使用完整视野而非自动瞄准的内缩范围。</summary>
        public bool TryGetSurvivorViewBounds(out Rect bounds)
        {
            bounds=default;var camera=Camera.main;
            if(mapRoot==null||camera==null||!camera.isActiveAndEnabled)return false;
            var plane=new Plane(mapRoot.up,mapRoot.position);
            Vector2 min=new Vector2(float.MaxValue,float.MaxValue),max=new Vector2(float.MinValue,float.MinValue);
            for(int i=0;i<4;i++)
            {
                var ray=camera.ViewportPointToRay(new Vector3(i%2,i/2,0));
                if(!plane.Raycast(ray,out float distance)||distance<0)return false;
                var local=mapRoot.InverseTransformPoint(ray.GetPoint(distance));var point=new Vector2(local.x,local.z);
                min=Vector2.Min(min,point);max=Vector2.Max(max,point);
            }
            bounds=Rect.MinMaxRect(min.x,min.y,max.x,max.y);return true;
        }

        /// <summary>从预加载表现池唤醒；完整快照成功提交后规则层才扣远场人数。</summary>
        public bool TryWakeSurvivorEnemy(int id,string configId,Vector2 position,int health,int maximum,int sequence,out string error)
        {
            error=null;if(enemyPresentation==null||mapRoot==null||playerMotor==null){error="尸潮战场表现尚未准备";return false;}
            var playerLocal=mapRoot.InverseTransformPoint(playerMotor.Position);
            return enemyPresentation.TrySetEnemy(id,configId,mapRoot.TransformPoint(World(position,0)),
                mapRoot.TransformDirection(new Vector3(playerLocal.x-position.x,0,playerLocal.z-position.y)),health,maximum,out error,false,sequence);
        }
        /// <summary>只归还角色表现，不经过共用死亡删除及掉落处理。</summary>
        public void SuspendSurvivorEnemy(int id)=>enemyPresentation?.SuspendEnemy(id);
        public bool TryPrewarmSurvivorEnemy(string configId,int count,out bool complete,out string error)
        {
            complete=false;error=null;if(enemyPresentation==null){error="尸潮预加载表现不可用";return false;}
            return enemyPresentation.TryPrewarmEnemy(configId,count,out complete,out error);
        }

        public bool TryGetPlayerPosition(out Vector2 position)
        {
            position = default;
            if (playerMotor == null || !playerMotor.IsValid || mapRoot == null) return false;
            var local = mapRoot.InverseTransformPoint(playerMotor.Position);
            position = new Vector2(local.x, local.z);
            return true;
        }

        /// <summary>按生命快照差值补齐街机玩家受击动画、闪红和伤害飘字；首次同步只建立基线。</summary>
        public void SetPlayerHealth(int current, int maximum)
        {
            var damage = CalculateHealthLoss(hasPlayerHealthBaseline, lastPlayerHealth, current);
            if (damage > 0)
            {
                playerMotor?.PlayHurtFeedback();
                playerMotor?.ShowDamage(damage);
            }
            lastPlayerHealth = current;
            hasPlayerHealthBaseline = true;
        }

        /// <summary>把当前救援资格传到对应角色，异步队友实例就绪后再应用缓存。</summary>
        public void SetPlayerRescue(long playerId,bool active)
        {
            if(playerId==0){playerMotor?.SetArcadeRescueActive(active);return;}
            if(!remotePlayers.TryGetValue(playerId,out var entry))return;
            entry.RescueActive=active;
            entry.View?.Instance?.GetComponent<PlayerView>()?.SetArcadeRescueActive(active);
        }

        public void SetEnemy(int id, string configId, Vector2 position, int currentHealth, int maximumHealth,
            bool telegraph = false, int attackSequence = 0, bool persistentHealthBar = false)
        {
            RecordRoundEnemyPosition(id, position);
            RecordNextStageEnemyAttack(id,attackSequence);
            RecordNextStageEnemyHealth(id,currentHealth);
            SetRoundEnemyHealth(id, currentHealth, maximumHealth);
            if (HasRoundEnemyContent(id)) return;
            if (enemyPresentation == null || mapRoot == null) return;
            var localPlayer = mapRoot.InverseTransformPoint(playerMotor.Position);
            var facing = new Vector3(localPlayer.x - position.x, 0f, localPlayer.z - position.y);
            // 正式人形 Prefab 以脚底为原点，直接对齐地图地面，不能再叠加旧胶囊中心高度。
            enemyPresentation.TrySetEnemy(id, configId, mapRoot.TransformPoint(World(position, 0f)),
                mapRoot.TransformDirection(facing), currentHealth, maximumHealth, out _, telegraph,
                attackSequence, persistentHealthBar);
        }

        /// <summary>统一转换地图朝向，联机使用权威目标而非本机玩家。</summary>
        public void SetEnemyMelee(int id,float duration,float remaining,float recovery,Vector2 direction)
        {
            if(SetCreatureMelee(id,duration,remaining,recovery,direction))return;
            if(mapRoot==null)return;
            var worldDirection=mapRoot.TransformDirection(new Vector3(direction.x,0,direction.y));
            if(SetNextStageEnemyMelee(id,duration,remaining,recovery,worldDirection))return;
            enemyPresentation?.SetMeleeAttack(id,duration,remaining,recovery,worldDirection);
        }

        /// <summary>把已提交的模式体型交给实例拥有者，不改共用Prefab。</summary>
        public void SetEnemyScale(int id,Vector3 scale) => enemyPresentation?.SetPresentationScale(id,scale);
        public void SetEnemyGroundEntry(int id,SurvivorGroundEntryState state)
        {
            if(mapRoot==null||!state.Started)return;
            enemyPresentation?.SetGroundEntry(id,state,mapRoot.TransformDirection(new Vector3(state.ForwardX,0,state.ForwardZ)),mapRoot.up);
        }

        public void SetEnemyDockSeaEntry(int id,FirepowerLoop.Arcade.ArcadeDockSeaEntryState state)
        {
            if(mapRoot==null)return;
            var direction=FirepowerLoop.Arcade.ArcadeDockSeaEntryRules.Forward(state);
            enemyPresentation?.SetDockSeaEntry(id,state,mapRoot.TransformDirection(new Vector3(direction.X,0,direction.Y)),mapRoot.up);
        }

        public void RemoveEnemy(int id, bool playDeath = false)
            => RemoveEnemy(id, playDeath, Vector2.zero, false);

        public void RemoveEnemy(int id, bool playDeath, Vector2 direction, bool explosive)
            => RemoveEnemy(id,playDeath,direction,explosive,false);

        public void SetEnemyBurning(int id,float remainingSeconds)
        {
            if(SetRoundEnemyBurning(id,remainingSeconds))return;
            enemyPresentation?.SetEnemyBurning(id,remainingSeconds);
        }

        /// <summary>燃烧击杀沿既有尸体预算和回收流程播放，不延长敌人的战斗生命周期。</summary>
        public void RemoveEnemy(int id,bool playDeath,Vector2 direction,bool explosive,bool flameKill)
            => RemoveEnemy(id,playDeath,direction,explosive,flameKill,0);

        /// <summary>将权威力度类别和地图转换后的方向一起交给敌人表现端口。</summary>
        public void RemoveEnemy(int id,bool playDeath,Vector2 direction,bool explosive,bool flameKill,int impactKind)
        {
            RemoveHerdEnemyFeedback(id,playDeath,impactKind);
            islandWading?.Remove(id);
            var worldDirection = new Vector3(direction.x, 0f, direction.y);
            if (mapRoot != null) worldDirection = mapRoot.TransformDirection(worldDirection);
            RemoveNextStageEnemy(id,playDeath,worldDirection,explosive,flameKill,impactKind);
            if(!TryPlayRoundFlameDeath(id,playDeath&&flameKill))RemoveRoundEnemyContent(id);
            RemoveArmor(id);
            enemyPresentation?.RemoveEnemy(id, playDeath, worldDirection, explosive,flameKill,impactKind);
        }

        public void SetProjectile(int id, bool hostile, Vector2 position, Vector2 direction)
        {
            SetProjectile(id, hostile, position, direction, null);
        }

        public void SetProjectile(int id, bool hostile, Vector2 position, Vector2 direction, string visualResourceName)
        {
            SetProjectile(id, hostile, position, direction, visualResourceName, false);
        }

        /// <summary>只在玩家武器弹丸首次显示时从枪口起步，随后两米内回到规则弹道。</summary>
        public void SetProjectile(int id, bool hostile, Vector2 position, Vector2 direction,
            string visualResourceName, bool fromPlayerWeapon)
        {
            if (projectilePresentation == null || mapRoot == null) return;
            var authoritative = mapRoot.TransformPoint(World(position, 0.75f));
            if (observedProjectileIds.Add(id) && !hostile && fromPlayerWeapon)
            {
                if (hasLastShotMuzzle)
                    projectileMuzzleDepartures[id] = new ProjectileMuzzleDeparture(lastShotMuzzle, authoritative);
            }
            var displayed = authoritative;
            if (projectileMuzzleDepartures.TryGetValue(id, out var departure))
            {
                var progress = Mathf.Clamp01(Vector3.Distance(authoritative, departure.AuthoritativeStart) /
                                             ProjectileMuzzleBlendDistance);
                displayed += (departure.Muzzle - departure.AuthoritativeStart) * (1f - progress);
                if (progress >= 1f) projectileMuzzleDepartures.Remove(id);
            }
            projectilePresentation.TrySetProjectile(id, displayed,
                mapRoot.TransformDirection(new Vector3(direction.x, 0f, direction.y)), visualResourceName, hostile, out _);
        }

        public void RemoveProjectile(int id)
        {
            observedProjectileIds.Remove(id);
            projectileMuzzleDepartures.Remove(id);
            projectilePresentation?.RemoveProjectile(id);
        }

        public void SetDrop(int id, ArcadeDropDefinition definition, Vector2 position)
        {
            if (lootPresentation == null || mapRoot == null) return;
            lootPresentation.TrySetDrop(id, mapRoot.TransformPoint(World(position, 0.3f)),
                ResolveDropDisplayName(definition), definition.Amount, definition.VisualPrefabAddress, out _);
        }

        /// <summary>经验只显示晶体，不显示名称或数量；累计经验仍由幸存者权威结算。</summary>
        public void SetExperienceDrop(int id,string visualPrefabName,Vector2 position,long experience)
        {
            if (lootPresentation == null || mapRoot == null) return;
            lootPresentation.TrySetDrop(id,mapRoot.TransformPoint(World(position,.3f)),string.Empty,(int)Math.Min(int.MaxValue,experience),visualPrefabName,out _);
        }

        public void RemoveDrop(int id) => lootPresentation?.RemoveDrop(id);
        /// <summary>初次进局仍由正式加载页遮挡，局内切关使用独立黑幕。</summary>
        public void BeginRoundTransition()
        {
            if(UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow()==true)return;
            var transition=UIHelper.GetUICtrl<ArcadeRoundTransitionUICtrl>()??UIHelper.ShowUI<ArcadeRoundTransitionUICtrl>();
            transition.Begin();
        }
        public bool IsRoundTransitionCovered=>UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow()==true||
            (UIHelper.GetUICtrl<ArcadeRoundTransitionUICtrl>()?.Covered??true);
        /// <summary>候选地图、角色和相机全部就绪后才揭开黑幕。</summary>
        public void EndRoundTransition()=>UIHelper.GetUICtrl<ArcadeRoundTransitionUICtrl>()?.Finish();
        public void SetDropDelivery(int id,bool airdrop,float remaining,float duration)=>lootPresentation?.SetDropDelivery(id,airdrop,remaining,duration);
        public void SetDropOpacity(int id, float opacity) => lootPresentation?.SetDropOpacity(id, opacity);

        /// <summary>沿用浮动提示显示实际补充次数；不根据掉落消失或库存差值推断成功。</summary>
        public void PlaySupplyPickup(ArcadeDropDefinition definition, int added)
        {
            if (added <= 0) return;
            PlayTreasurePickupFlash();
            var key = definition.Kind == global::ArcadeDropKind.Bomb ? "ui.arcade.bomb" : "ui.arcade.dash";
            ShowPickup(key, added, total => $"{Loc.T(key)} +{total}");
        }

        /// <summary>开始一次完整强化表现同步；结束时会回收本帧未出现的过期视觉。</summary>
        public void BeginEffectSync()
        {
            effectSyncRevision++;
            if (effectSyncRevision != 0) return;
            effectSyncRevision = 1;
            foreach (EffectVisualEntry entry in effectVisuals.Values) entry.LastSeenRevision = 0;
            foreach(var storm in stormVisuals.Values)storm.SeenRevision=0;
        }

        public void SetPowerup(ArcadePowerupDefinition definition, Vector2 anchorPosition)
        {
            SetPowerup(0, definition, anchorPosition);
        }

        /// <summary>每个真实接触体持有独立表现；高度沿用美术定义，水平坐标不再二次偏移。</summary>
        public void SetSlayerMartSupport(long ownerPlayerId,string instanceId,int bodyIndex,ArcadePowerupDefinition definition,
            Vector2 bodyPosition,Vector2 shotDirection)
        {
            if(string.IsNullOrWhiteSpace(instanceId)||bodyIndex<0)return;
            string key=$"slayermart:{ownerPlayerId}:{instanceId}:{bodyIndex}";
            SetEffectVisual(key,definition.Id,definition.ActiveVisualPrefabAddress,bodyPosition,new Vector3(0,definition.VisualOffset.y,0));
            if(definition.Behavior!=global::ArcadePowerupBehavior.Chicken||shotDirection.sqrMagnitude<.000001f||
                !effectVisuals.TryGetValue(key,out var entry))return;
            entry.LocalRotation=Quaternion.LookRotation(new Vector3(shotDirection.x,0,shotDirection.y));entry.HasShotDirection=true;
            if(entry.Instance!=null)entry.Instance.transform.localRotation=entry.LocalRotation;
        }

        /// <summary>按玩家身份隔离同类强化视觉；联机双方持有相同强化时不会覆盖彼此。</summary>
        public void SetPowerup(long ownerPlayerId, ArcadePowerupDefinition definition, Vector2 anchorPosition)
        {
            SetEffectVisual($"powerup:{ownerPlayerId}:{(int)definition.Behavior}", definition.Id,
                definition.ActiveVisualPrefabAddress, anchorPosition, definition.VisualOffset);
        }

        public void SetPowerup(ArcadePowerupDefinition definition, Vector2 anchorPosition, Vector2 shotDirection) =>
            SetPowerup(0, definition, anchorPosition, shotDirection);

        /// <summary>同步哨塔和战斗鸡最后一次实际发射朝向；加载前保留旋转，零方向及其它强化不改变姿态。</summary>
        public void SetPowerup(long ownerPlayerId, ArcadePowerupDefinition definition, Vector2 anchorPosition,
            Vector2 shotDirection)
        {
            SetPowerup(ownerPlayerId, definition, anchorPosition);
            if ((definition.Behavior != global::ArcadePowerupBehavior.Sentry &&
                 definition.Behavior != global::ArcadePowerupBehavior.Chicken) ||
                shotDirection.sqrMagnitude < 0.000001f)
                return;
            var key = $"powerup:{ownerPlayerId}:{(int)definition.Behavior}";
            if (!effectVisuals.TryGetValue(key, out EffectVisualEntry entry)) return;
            entry.LocalRotation = Quaternion.LookRotation(new Vector3(shotDirection.x, 0f, shotDirection.y));
            entry.HasShotDirection = true;
            if (entry.Instance != null) entry.Instance.transform.localRotation = entry.LocalRotation;
        }

        public void SetSupport(ArcadeSupportVisualDefinition definition, Vector2 playerPosition)
        {
            SetSupport(0, definition, playerPosition);
        }

        /// <summary>按玩家身份维护支援视觉，使本机和远端装备可独立替换、回收。</summary>
        public void SetSupport(long ownerPlayerId, ArcadeSupportVisualDefinition definition, Vector2 playerPosition)
        {
            SetEffectVisual($"support:{ownerPlayerId}", definition.ProductId, definition.ActiveVisualPrefabAddress,
                playerPosition, definition.VisualOffset);
        }

        /// <summary>复用当前护罩播放挡伤脉冲；护盾本帧耗尽时延迟回收至闪光结束。</summary>
        public void PlayShieldBlock(long ownerPlayerId, Vector2 anchorPosition)
        {
            int shieldBehavior = (int)global::ArcadePowerupBehavior.Shield;
            var key = $"powerup:{ownerPlayerId}:{shieldBehavior}";
            if (!effectVisuals.TryGetValue(key, out EffectVisualEntry entry)) return;
            entry.ShieldFlashRemaining = ShieldBlockFlashSeconds;
            entry.LocalPosition = new Vector3(anchorPosition.x, entry.LocalPosition.y, anchorPosition.y);
            if (entry.Instance == null) return;
            entry.Instance.transform.localPosition = entry.LocalPosition;
            ApplyShieldFlash(entry);
        }

        public void EndEffectSync()
        {
            EndStormSync();
            staleEffectVisualKeys.Clear();
            foreach (KeyValuePair<string, EffectVisualEntry> pair in effectVisuals)
                if (pair.Value.LastSeenRevision != effectSyncRevision && pair.Value.ShieldFlashRemaining <= 0f &&
                    !KeepWeaponEvolutionTail(pair.Value))
                    staleEffectVisualKeys.Add(pair.Key);
            for (var index = 0; index < staleEffectVisualKeys.Count; index++)
                ReleaseEffectVisual(staleEffectVisualKeys[index]);
            staleEffectVisualKeys.Clear();
        }

        public void SetRemotePlayer(long playerId, Vector2 position, Vector2 facing, bool visible,
            bool invulnerable, int currentHealth, int maximumHealth)
        {
            if (playerId <= 0 || mapRoot == null) return;
            if (!remotePlayers.TryGetValue(playerId, out RemotePlayerEntry entry))
            {
                entry = new RemotePlayerEntry { PlayerId = playerId, Generation = remoteGeneration };
                remotePlayers.Add(playerId, entry);
                LoadRemotePlayerAsync(entry).Forget();
            }
            var damage = CalculateHealthLoss(entry.HasHealthBaseline, entry.CurrentHealth, currentHealth, visible);
            entry.Position = position;
            entry.Facing = facing;
            entry.Visible = visible;
            entry.Invulnerable = invulnerable;
            entry.CurrentHealth = currentHealth;
            entry.MaximumHealth = maximumHealth;
            entry.HasHealthBaseline = visible;
            if (entry.View == null) return;
            ApplyRemotePlayer(entry, false);
            if (damage > 0)
            {
                entry.View.PlayHurtFeedback();
                entry.View.ShowDamage(damage);
            }
        }

        public void RemoveRemotePlayer(long playerId)
        {
            dashPresentation?.Remove(playerId);
            if (!remotePlayers.TryGetValue(playerId, out RemotePlayerEntry entry)) return;
            remotePlayers.Remove(playerId);
            ReleaseRemotePlayer(entry);
        }

        /// <summary>保留异步加载期间的最新武器与序列；实例首次就绪只建立基线，不补播历史攻击。</summary>
        public void SetRemotePlayerCombat(long playerId, ArcadeWeaponDefinition weapon, int fireSequence)
        {
            if (!remotePlayers.TryGetValue(playerId, out var entry)) return;
            entry.Weapon = weapon;
            entry.HasWeapon = true;
            entry.FireSequence = fireSequence;
            if (entry.View != null) ApplyRemotePlayerCombat(entry);
        }

        private static void ApplyRemotePlayerCombat(RemotePlayerEntry entry)
        {
            if (!entry.HasWeapon) return;
            var view = entry.View.Instance?.GetComponent<PlayerView>();
            if (view == null) return;
            PlayerViewCtrl.ConfigureSharedArcadeWeaponPresentation(view, entry.Weapon);
            if (entry.HasFireBaseline && entry.LastFireSequence != entry.FireSequence && entry.Visible)
            {
                if (entry.Weapon.Behavior == global::ArcadeWeaponBehavior.Melee) view.PlayWeaponSwingFeedback();
                else view.PlayProjectileFireFeedback();
            }
            entry.LastFireSequence = entry.FireSequence;
            entry.HasFireBaseline = true;
        }

        /// <summary>街机玩家不走普通玩家就绪事件，由街机表现帧统一推进本机与远端受击恢复。</summary>
        public void TickPlayerFeedback(float deltaTime)
        {
            TickWeaponEvolutionVisuals(deltaTime);
            TickFlamethrowerAudio(deltaTime);
            TickHerdFeedback(deltaTime);
            TickElectricTowerFeedback(deltaTime);
            TickRoundFlameFeedback(deltaTime);
            TickNextStageFeedback(deltaTime);
            islandWading?.Tick(deltaTime);
            bombBlasts?.Tick(deltaTime);
            dashPresentation?.Tick(deltaTime);
            TickExitCues(deltaTime);
            TickTreasureVisuals(deltaTime);
            playerMotor?.TickCombatFeedback(deltaTime);
            foreach (RemotePlayerEntry entry in remotePlayers.Values)
            {
                entry.View?.Tick(deltaTime);
                MoveRemoteDashVisual(entry);
            }
            foreach (EffectVisualEntry entry in effectVisuals.Values)
            {
                if (entry.ShieldFlashRemaining <= 0f) continue;
                entry.ShieldFlashRemaining = Mathf.Max(0f, entry.ShieldFlashRemaining - Mathf.Max(0f, deltaTime));
                ApplyShieldFlash(entry);
            }
        }
        public void Dispose() => End();

        /// <summary>异步加载正式玩家 Prefab；代次或所属地图变化时只释放候选，不提交过期实例。</summary>
        private async UniTaskVoid LoadRemotePlayerAsync(RemotePlayerEntry entry)
        {
            if (!Game.Interface.GetUtility<IRuntimeConfigProvider>().TryGetPlayer("player", out PlayerConfigDefinition config, out _))
            {
                remotePlayers.Remove(entry.PlayerId);
                return;
            }
            var completion = new UniTaskCompletionSource<GameObject>();
            ResourceLoadHandle handle = ResourceLoader.LoadAssetAsync<GameObject>(config.PrefabAddress,
                prefab => completion.TrySetResult(prefab));
            GameObject prefab;
            try { prefab = await completion.Task; }
            catch
            {
                if (handle.IsValid) ResourceLoader.Release(handle);
                remotePlayers.Remove(entry.PlayerId);
                return;
            }
            if (prefab == null || mapRoot == null || entry.Generation != remoteGeneration ||
                !remotePlayers.TryGetValue(entry.PlayerId, out RemotePlayerEntry current) || !ReferenceEquals(current, entry))
            {
                if (handle.IsValid) ResourceLoader.Release(handle);
                return;
            }
            GameObject instance = UnityEngine.Object.Instantiate(prefab, mapRoot);
            if (instance == null)
            {
                if (handle.IsValid) ResourceLoader.Release(handle);
                remotePlayers.Remove(entry.PlayerId);
                return;
            }
            PlayerView playerView = instance.GetComponent<PlayerView>();
            if (playerView?.CharacterController != null) playerView.CharacterController.enabled = false;
            playerView?.SetTiliRing(0, 1, false, false);
            playerView?.HideActionProgress();
            playerView?.SetArcadePresentationActive(true);
            entry.Handle = handle;
            entry.View = new OnlineRemotePlayerViewCtrl(instance);
            ApplyRemotePlayer(entry, true);
        }

        private void ApplyRemotePlayer(RemotePlayerEntry entry, bool snap)
        {
            float yaw = entry.Facing.sqrMagnitude > 0.001f
                ? Mathf.Atan2(entry.Facing.x, entry.Facing.y) * Mathf.Rad2Deg : 0f;
            Vector3 local = new(entry.Position.x, 0f, entry.Position.y);
            if (snap) entry.View.Snap(local, yaw);
            else if(entry.Visible)entry.View.SetTarget(local,yaw);
            else
            {
                // 规则可提前准备复活坐标，尸体仍留在最后显示地点并停止旧插值。
                var displayed=entry.View.Instance.transform;
                entry.View.Snap(displayed.localPosition,displayed.localEulerAngles.y);
            }
            entry.View.SetAlive(entry.Visible,!snap);
            SetPlayerHerdImpact(entry.PlayerId,entry.Visible?entry.HerdImpact:default);
            entry.View.Instance?.GetComponent<PlayerView>()?.SetArcadeRescueActive(entry.RescueActive&&!entry.Visible);
            entry.View.Instance?.GetComponent<PlayerView>()?.SetArcadeProtectionActive(entry.Visible && entry.Invulnerable);
            ApplyRemotePlayerCombat(entry);
        }

        private void ReleaseRemotePlayer(RemotePlayerEntry entry)
        {
            if (entry?.View?.Instance != null) Game.Interface.GetUtility<IViewInstanceProvider>()?.Release(entry.View.Instance);
            if (entry != null && entry.Handle.IsValid) ResourceLoader.Release(entry.Handle);
            if (entry != null) { entry.View = null; entry.Handle = ResourceLoadHandle.Empty; }
        }

        private sealed class RemotePlayerEntry
        {
            /// <summary>异步加载等待期间保留最新击飞姿态，不补播历史动作。</summary>
            public FirepowerLoop.Arcade.ArcadeHerdImpactState HerdImpact;
            public long PlayerId;
            public int Generation;
            public Vector2 Position;
            public Vector2 Facing;
            public bool Visible;
            /// <summary>远端异步创建期间缓存最新合并保护状态，首次就绪不补播过期保护。</summary>
            public bool Invulnerable;
            /// <summary>异步创建期间只缓存当前救援资格，不补播历史死亡特效。</summary>
            public bool RescueActive;
            public int CurrentHealth;
            public int MaximumHealth;
            /// <summary>异步实例创建前也保留生命基线，防止首帧快照被误判为受击。</summary>
            public bool HasHealthBaseline;
            /// <summary>本场最新权威武器及攻击序列；加载完成前只缓存，首次绑定不播放旧动作。</summary>
            public ArcadeWeaponDefinition Weapon;
            public bool HasWeapon;
            public int FireSequence;
            public int LastFireSequence;
            public bool HasFireBaseline;
            public ResourceLoadHandle Handle;
            public OnlineRemotePlayerViewCtrl View;
        }

        /// <summary>按配置地址创建或更新一个强化视觉；同一键更换地址时原实例与 Handle 一并释放。</summary>
        private void SetEffectVisual(string key, string displayName, string address, Vector2 anchorPosition,
            Vector3 offset)
        {
            if (root == null || string.IsNullOrWhiteSpace(address))
            {
                ReleaseEffectVisual(key);
                return;
            }

            if (!effectVisuals.TryGetValue(key, out EffectVisualEntry entry) ||
                !string.Equals(entry.Address, address, StringComparison.Ordinal))
            {
                ReleaseEffectVisual(key);
                entry = new EffectVisualEntry
                {
                    Key = key,
                    DisplayName = displayName,
                    Address = address
                };
                effectVisuals.Add(key, entry);
                LoadEffectVisualAsync(entry).Forget();
            }

            entry.LastSeenRevision = effectSyncRevision;
            entry.LocalPosition = new Vector3(anchorPosition.x + offset.x, offset.y, anchorPosition.y + offset.z);
            if (entry.Instance != null) entry.Instance.transform.localPosition = entry.LocalPosition;
        }

        /// <summary>异步加载纯视觉 Prefab；过期回调只能释放自己的 Handle，不得重新挂回已结束战场。</summary>
        private async UniTaskVoid LoadEffectVisualAsync(EffectVisualEntry entry)
        {
            var completion = new UniTaskCompletionSource<GameObject>();
            entry.Handle = ResourceLoader.LoadAssetAsync<GameObject>(entry.Address,
                prefab => completion.TrySetResult(prefab));
            GameObject prefab;
            try
            {
                prefab = await completion.Task;
            }
            catch (Exception exception)
            {
                if (!entry.Released)
                    Log.Error($"[ArcadeCombatViewController] 强化视觉加载异常：{entry.Address} / {exception.Message}");
                ReleaseEffectVisual(entry);
                return;
            }

            if (entry.Released || root == null || !effectVisuals.TryGetValue(entry.Key, out EffectVisualEntry current) ||
                !ReferenceEquals(current, entry))
                return;
            if (!ValidateEffectVisualPrefab(prefab, entry.Address, out string error))
            {
                Log.Error($"[ArcadeCombatViewController] {error}");
                ReleaseEffectVisual(entry.Key);
                return;
            }

            try
            {
                entry.Instance = UnityEngine.Object.Instantiate(prefab, root.transform, false);
                entry.Instance.name = $"Effect_{entry.DisplayName}";
                entry.Instance.transform.localPosition = entry.LocalPosition;
                if (entry.HasShotDirection) entry.Instance.transform.localRotation = entry.LocalRotation;
                if(entry.IsDrone)ApplyDroneAppearance(entry);
                  ApplyWeaponEvolution(entry);
                ApplyRewardPortalAppearance(entry);
                if (entry.ShieldFlashRemaining > 0f) ApplyShieldFlash(entry);
            }
            catch (Exception exception)
            {
                Log.Error($"[ArcadeCombatViewController] 强化视觉实例化失败：{entry.Address} / {exception.Message}");
                ReleaseEffectVisual(entry);
            }
        }

        private static bool ValidateEffectVisualPrefab(GameObject prefab, string address, out string error)
        {
            if (prefab == null || prefab.GetComponentsInChildren<Renderer>(true).Length == 0)
            {
                error = $"强化视觉 Prefab 缺少 Renderer：{address}";
                return false;
            }
            if (prefab.GetComponentsInChildren<Collider>(true).Length > 0)
            {
                error = $"强化视觉 Prefab 不得携带碰撞体：{address}";
                return false;
            }
            error = null;
            return true;
        }

        private void ReleaseEffectVisual(string key)
        {
            if (!effectVisuals.TryGetValue(key, out EffectVisualEntry entry)) return;
            ReleaseEffectVisual(entry);
        }

        private void ReleaseEffectVisual(EffectVisualEntry entry)
        {
            if (entry == null || entry.Released) return;
            if (effectVisuals.TryGetValue(entry.Key, out EffectVisualEntry current) && ReferenceEquals(current, entry))
                effectVisuals.Remove(entry.Key);
            entry.Released = true;
            DestroyObject(entry.Instance);
            entry.Instance = null;
            if (entry.Handle.IsValid) ResourceLoader.Release(entry.Handle);
            entry.Handle = ResourceLoadHandle.Empty;
        }

        private void ReleaseAllEffectVisuals()
        {
            EndStormSync(true);
            staleEffectVisualKeys.Clear();
            staleEffectVisualKeys.AddRange(effectVisuals.Keys);
            for (var index = 0; index < staleEffectVisualKeys.Count; index++)
                ReleaseEffectVisual(staleEffectVisualKeys[index]);
            staleEffectVisualKeys.Clear();
        }

        /// <summary>仅覆盖护罩实例的渲染参数；脉冲结束后清除覆盖以恢复 Prefab 原色。</summary>
        private static void ApplyShieldFlash(EffectVisualEntry entry)
        {
            if (entry.Instance == null) return;
            float strength = Mathf.Clamp01(entry.ShieldFlashRemaining / ShieldBlockFlashSeconds);
            entry.Instance.transform.localScale = Vector3.one * (1f + 0.12f * strength);
            var block = new MaterialPropertyBlock();
            if (strength > 0f)
                block.SetColor("_BaseColor", new Color(0.45f, 0.93f, 1f, 0.2f + 0.6f * strength));
            foreach (Renderer renderer in entry.Instance.GetComponentsInChildren<Renderer>(true))
                renderer.SetPropertyBlock(strength > 0f ? block : null);
        }

        private sealed class EffectVisualEntry
        {
            public string Key;
            public string DisplayName;
            public string Address;
            public int LastSeenRevision;
            public Vector3 LocalPosition;
            /// <summary>相对竞技场根的支援发射旋转；加载完成后应用，随视觉实例一起释放。</summary>
            public Quaternion LocalRotation = Quaternion.identity;
            /// <summary>仅收到射击支援有效方向后覆盖 Prefab 姿态；其它强化保留美术预设旋转。</summary>
            public bool HasShotDirection;
            /// <summary>挡伤脉冲剩余秒数；大于零时允许已耗尽护盾的视觉延迟回收。</summary>
            public float ShieldFlashRemaining;
            /// <summary>异步资源到达前也缓存无人机的最新强度。</summary>
            public FirepowerLoop.Arcade.ArcadeRoundRewardState RoundReward;
            public FirepowerLoop.Arcade.ArcadeRoundRewardTuning RewardTuning;
            public bool IsStagePortal;
            public float PortalSeconds,PortalAppearSeconds;
            public bool IsDrone;
            public int DronePowerTier=1;
            /// <summary>已采用进化模板的最新快照和纯视觉烟尾，不持有玩法状态。</summary>
            public int EvolutionMode;
            public float EvolutionRadius, EvolutionRemaining, EvolutionPhase, EvolutionTailSeconds, EvolutionAge;
            public bool EvolutionChild;
            public bool Released;
            public ResourceLoadHandle Handle;
            public GameObject Instance;
        }

        /// <summary>
        /// 创建规则权威的无渲染碰撞，并异步附加美术表现；美术加载失败不会改变障碍范围或阻塞战斗。
        /// </summary>
        private void CreateArenaCollision(int arenaNumber, float halfWidth, float halfHeight,
            IReadOnlyList<ArcadeObstacleDefinition> obstacles)
        {
            obstacleVisualReady = false;
            obstacleVisualError = null;
            const float wallThickness = 0.5f;
            const float wallHeight = 2f;
            if(earlyStage!=null)CreateBoundary("EarlyStageGround",new Vector3(0,-.15f,0),new Vector3(halfWidth*2,.3f,halfHeight*2));
            CreateBoundary("NorthBoundary", new Vector3(0f, wallHeight * 0.5f, halfHeight + wallThickness * 0.5f),
                new Vector3(halfWidth * 2f + wallThickness * 2f, wallHeight, wallThickness));
            CreateBoundary("SouthBoundary", new Vector3(0f, wallHeight * 0.5f, -halfHeight - wallThickness * 0.5f),
                new Vector3(halfWidth * 2f + wallThickness * 2f, wallHeight, wallThickness));
            CreateBoundary("EastBoundary", new Vector3(halfWidth + wallThickness * 0.5f, wallHeight * 0.5f, 0f),
                new Vector3(wallThickness, wallHeight, halfHeight * 2f));
            CreateBoundary("WestBoundary", new Vector3(-halfWidth - wallThickness * 0.5f, wallHeight * 0.5f, 0f),
                new Vector3(wallThickness, wallHeight, halfHeight * 2f));

            if (obstacles == null) { obstacleVisualReady = true; return; }
            var placements = new List<ObstacleVisualPlacement>(obstacles.Count);
            for (var index = 0; index < obstacles.Count; index++)
            {
                var definition = obstacles[index];
                var obstacle = new GameObject($"Obstacle_{index + 1}", typeof(BoxCollider));
                obstacle.transform.SetParent(root.transform, false);
                obstacle.transform.localPosition = new Vector3(definition.Center.x, 0f, definition.Center.y);
                var size = new Vector3(definition.HalfExtents.x * 2f, 1.4f,
                    definition.HalfExtents.y * 2f);
                var collider = obstacle.GetComponent<BoxCollider>();
                collider.center = new Vector3(0f, size.y * 0.5f, 0f);
                collider.size = size;
                placements.Add(new ObstacleVisualPlacement(obstacle.transform, size.x, size.z, index));
            }

            if(earlyStage!=null){obstacleVisualReady=true;return;}
            if (placements.Count > 0)
                LoadObstacleVisualsAsync(arenaNumber, placements, obstacleVisualGeneration).Forget();
            else obstacleVisualReady = true;
        }

        /// <summary>加载一份视觉集合并按场次稳定选择组合；代次过期时只释放资源，不提交实例。</summary>
        private async UniTaskVoid LoadObstacleVisualsAsync(int arenaNumber,
            IReadOnlyList<ObstacleVisualPlacement> placements, int generation)
        {
            var state = new ObstacleVisualLoadState();
            obstacleVisualLoad = state;
            var completion = new UniTaskCompletionSource<GameObject>();
            GameObject prefab;
            try
            {
                state.Handle = ResourceLoader.LoadAssetAsync<GameObject>(ObstacleVisualAddress,
                    prefab => completion.TrySetResult(prefab));
                prefab = await completion.Task;
            }
            catch (Exception exception)
            {
                if (!state.Released && generation == obstacleVisualGeneration) obstacleVisualError = exception.Message;
                if (!state.Released)
                    Log.Error($"[ArcadeCombatViewController] 障碍视觉加载异常：{exception.Message}");
                ReleaseObstacleVisualLoad(state);
                if (ReferenceEquals(obstacleVisualLoad, state)) obstacleVisualLoad = null;
                return;
            }

            if (state.Released || generation != obstacleVisualGeneration ||
                !ReferenceEquals(obstacleVisualLoad, state) || root == null)
                return;
            if (!ValidateObstacleVisualPrefab(prefab, out string error))
            {
                obstacleVisualError = error;
                Log.Error($"[ArcadeCombatViewController] {error}");
                ReleaseObstacleVisualLoad(state);
                obstacleVisualLoad = null;
                return;
            }

            for (var index = 0; index < placements.Count; index++)
            {
                ObstacleVisualPlacement placement = placements[index];
                if (placement.Parent == null) continue;
                int variantIndex = PositiveModulo(arenaNumber - 1 + placement.Index * 2, 4);
                // 只创建选中的障碍组合，集合中的休眠整图模板继续由各自资源句柄拥有。
                var instance = new GameObject("Visual");
                instance.transform.SetParent(placement.Parent, false);
                var variant = UnityEngine.Object.Instantiate(prefab.transform.GetChild(variantIndex).gameObject,
                    instance.transform, false);
                variant.SetActive(true);
                FitObstacleVisual(instance.transform, placement.Width, placement.Length);
            }
            obstacleVisualReady = true;
            SyncRoundContentEnvironment();
            // 岛岸可能先完成加载；晚到的通用障碍仍须立即切换为本轮同源礁石或船壳。
            try { if (island != null) ConfigureIsland(); }
            catch (Exception exception)
            {
                obstacleVisualError = exception.Message;
                Log.Error("[ArcadeCombatViewController] 海岛地标准备失败：" + exception.Message);
            }
        }

        private static bool ValidateObstacleVisualPrefab(GameObject prefab, out string error)
        {
            if (prefab == null)
            {
                error = $"障碍视觉 Prefab 加载失败：{ObstacleVisualAddress}";
                return false;
            }
            // 前四项始终是障碍变体；之后允许已登记的休眠场地模板，不能把模板计入变体轮换。
            if (prefab.transform.childCount < 4 ||
                Enumerable.Range(0, 4).Any(i => prefab.transform.GetChild(i).GetComponentsInChildren<MeshRenderer>(true).Length == 0) ||
                Enumerable.Range(4, prefab.transform.childCount - 4).Any(i =>
                    prefab.transform.GetChild(i).name != "Island" && prefab.transform.GetChild(i).name != "Crate" &&
                    prefab.transform.GetChild(i).name != "ArmorPlate" && prefab.transform.GetChild(i).name != "Content"))
            {
                error = $"障碍视觉 Prefab 必须包含四套有效组合：{ObstacleVisualAddress}";
                return false;
            }
            if (prefab.GetComponentsInChildren<Collider>(true).Length > 0)
            {
                error = $"障碍视觉 Prefab 不得携带碰撞体：{ObstacleVisualAddress}";
                return false;
            }
            error = null;
            return true;
        }

        private static void FitObstacleVisual(Transform visual, float width, float length)
        {
            bool horizontal = width > length;
            visual.localPosition = Vector3.zero;
            visual.localRotation = horizontal ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
            visual.localScale = horizontal
                ? new Vector3(length / ObstacleVisualBaseWidth, 1f, width / ObstacleVisualBaseLength)
                : new Vector3(width / ObstacleVisualBaseWidth, 1f, length / ObstacleVisualBaseLength);
        }

        private static int PositiveModulo(int value, int modulus) => (value % modulus + modulus) % modulus;

        private static void ReleaseObstacleVisualLoad(ObstacleVisualLoadState state)
        {
            if (state == null || state.Released) return;
            state.Released = true;
            if (state.Handle.IsValid) ResourceLoader.Release(state.Handle);
            state.Handle = ResourceLoadHandle.Empty;
        }

        private sealed class ObstacleVisualLoadState
        {
            /// <summary>异步资源到达前也缓存无人机的最新强度。</summary>
            public bool IsDrone;
            public int DronePowerTier=1;
            public bool Released;
            public ResourceLoadHandle Handle;
        }

        private readonly struct ObstacleVisualPlacement
        {
            public ObstacleVisualPlacement(Transform parent, float width, float length, int index)
            {
                Parent = parent;
                Width = width;
                Length = length;
                Index = index;
            }

            public Transform Parent { get; }
            public float Width { get; }
            public float Length { get; }
            public int Index { get; }
        }

        private void CreateBoundary(string name, Vector3 localPosition, Vector3 size)
        {
            var boundary = new GameObject(name, typeof(BoxCollider));
            boundary.transform.SetParent(root.transform, false);
            boundary.transform.localPosition = localPosition;
            boundary.GetComponent<BoxCollider>().size = size;
        }

        /// <summary>街机地面物资由模型和颜色辨认；空名称隐藏掉落文字，不影响已拾取状态栏。</summary>
        private static string ResolveDropDisplayName(ArcadeDropDefinition definition) => string.Empty;

        private static Vector3 World(Vector2 position, float y) => new Vector3(position.x, y, position.y);

        private static void DestroyObject(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
