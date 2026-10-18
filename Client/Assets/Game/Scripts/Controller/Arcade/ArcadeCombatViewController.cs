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
        /// <summary>相机准备事务不拥有时长；未支持的表现端口拒绝进入。</summary>
        bool IsFirstPersonActive => false;
        bool TryBeginFirstPerson(out string error) { error = "当前相机不支持街机第一人称"; return false; }
        void EndFirstPerson() { }
        void ApplyFirstPersonLook(Vector2 delta) { }
        bool TryReadFirstPersonAim(out Vector2 aim) { aim = default; return false; }
        void PlayFirstPersonPickup() { }
        void Begin(int arenaNumber, float arenaHalfWidth, float arenaHalfHeight,
            IReadOnlyList<ArcadeObstacleDefinition> obstacles);
        void End();
        /// <summary>只显示本机区域入口和剩余物品；不能从此端口领取、计时或发放积分。</summary>
        void SetTreasureRoom(FirepowerLoop.Arcade.ArcadeTreasureRoomParameters parameters,
            bool inRoom, bool discovered, bool open, IReadOnlyList<int> remainingIds) { }
        /// <summary>只呈现清场或胜利出口；触发与目的地选择由战斗权威裁决。</summary>
        void SetExits(IReadOnlyList<int> destinations, float halfHeight, bool victory) { }
        /// <summary>仅显示权威周期；不自行推进机关时间、不裁决伤害。</summary>
        void SetHazards(IReadOnlyList<FirepowerLoop.Arcade.ArcadeHazardParameters> hazards, double elapsedSeconds) { }
        /// <summary>只按权威配置显示岛岸与第四轮营火，不从视觉碰撞计算伤害。</summary>
        void SetOpening(FirepowerLoop.Arcade.ArcadeOpeningParameters parameters, bool fireActive) { }
        /// <summary>按权威布局准备当前轮，资源就绪后才允许规则开始活动。</summary>
        void SetRoundLayout(FirepowerLoop.Arcade.ArcadeRoundLayoutParameters layout) { }
        bool IsRoundLayoutReady => true;
        string RoundLayoutLoadError => null;
        /// <summary>投影环境活动时钟及区域，伤害仍由单人或服务器裁决。</summary>
        void SetRoundContent(double elapsed, IReadOnlyList<FirepowerLoop.Arcade.ArcadeContentAreaSnapshot> areas) { }
        void SetEnemyContent(int id, int specialKind, int bossKind, bool warning) { }
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
        void SetPlayerWeapon(string weaponId) { }
        void SetPlayerWeapon(ArcadeWeaponDefinition weapon) => SetPlayerWeapon(weapon.Id);
        /// <summary>同步规则层开火序列；序列只用于表现去重，不参与射速或伤害判定。</summary>
        void SetPlayerFireSequence(int sequence) { }
        /// <summary>单人街机传入本次权威攻击间隔（秒），仅用于挥刀节奏；其他表现端口沿用原反馈。</summary>
        void SetPlayerFireSequence(int sequence, float attackIntervalSeconds) => SetPlayerFireSequence(sequence);
        /// <summary>投射物确实引爆时播放表现反馈，不参与范围伤害结算。</summary>
        void PlayProjectileExplosion(Vector2 position, float radius) { }
        /// <summary>局内快捷炸弹成功消耗后播放独立的清场音效。</summary>
        void PlayBombExplosion(Vector2 position) { }
        /// <summary>已结算的单次命中声音，不参与伤害与投射物生命周期。</summary>
        void PlayImpactSound(Vector2 position, bool melee) { }
        /// <summary>普通枪弹实际撞障碍的接触反馈；不作为命中敌人或伤害事件。</summary>
        void PlayProjectileSurfaceImpact(Vector2 position, Vector2 incomingDirection) { }
        void SetPlayerHealth(int current, int maximum);
        void SetEnemy(int id, string configId, Vector2 position, int currentHealth, int maximumHealth,
            bool telegraph = false, int attackSequence = 0, bool persistentHealthBar = false);
        void RemoveEnemy(int id, bool playDeath = false);
        void SetProjectile(int id, bool hostile, Vector2 position, Vector2 direction);
        void SetProjectile(int id, bool hostile, Vector2 position, Vector2 direction, string visualResourceName) =>
            SetProjectile(id, hostile, position, direction);
        /// <summary>玩家武器标记只用于可见弹丸从枪口过渡，不参与权威命中。</summary>
        void SetProjectile(int id, bool hostile, Vector2 position, Vector2 direction, string visualResourceName,
            bool fromPlayerWeapon) => SetProjectile(id, hostile, position, direction, visualResourceName);
        void RemoveProjectile(int id);
        void SetDrop(int id, ArcadeDropDefinition definition, Vector2 position);
        void RemoveDrop(int id);
        /// <summary>显示权威剩余寿命的透明度，不参与领取裁决。</summary>
        void SetDropOpacity(int id, float opacity) { }
        /// <summary>只淡出宝藏房地面物资，入口与返回出口保持可见。</summary>
        void SetTreasureRewardOpacity(float opacity) { }
        /// <summary>仅在权威补给拾取成功后调用，added 是上限截断后的实际增加次数。</summary>
        void PlaySupplyPickup(ArcadeDropDefinition definition, int added) { }
        /// <summary>权威提交后的实际积分与金币分别传入；不能从掉落面值推断。</summary>
        void PlayTreasurePickup(long score, long gold) { }
        /// <summary>仅接受产品定义中的稳定 ID，显示名称沿用现有本地化定义。</summary>
        void PlayProductPickup(string productId) { }
        void BeginEffectSync() { }
        /// <summary>风暴权威半径的专属表现，不复用普通电球。</summary>
        void SetSlayerMartStorm(long ownerPlayerId,Vector2 position,float radius) { }
        /// <summary>稳定实例与接触体身份只用于表现，位置由战斗权威给定。</summary>
        void SetSlayerMartSupport(long ownerPlayerId,string instanceId,int bodyIndex,ArcadePowerupDefinition definition,
            Vector2 bodyPosition,Vector2 shotDirection) { }
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
        private int lastPlayerHealth;
        private bool hasPlayerFireSequence;
        private int lastPlayerFireSequence;
        /// <summary>当前武器 ID 与上次开火音效时刻仅用于本场战斗的声音选择、限频。</summary>
        private string currentWeaponId;
        /// <summary>由完整武器定义确定反馈类型，避免冷兵器被枪械反馈入口忽略。</summary>
        private bool currentWeaponIsMelee;
        private float lastFireSoundTime = float.NegativeInfinity;
        /// <summary>两类爆炸声各自的上次播放时刻，避免高射速镭射弹堆积长尾音效。</summary>
        private float lastExplosionSoundTime = float.NegativeInfinity;
        private float lastSciFiExplosionSoundTime = float.NegativeInfinity;
        private readonly HashSet<int> observedProjectileIds = new();
        private readonly Dictionary<int, ProjectileMuzzleDeparture> projectileMuzzleDepartures = new();
        /// <summary>第一人称弹丸生成时的水平射击高度（地图局部米）；退出后在途弹丸仍保持原高度。</summary>
        private readonly Dictionary<int, float> firstPersonProjectileHeights = new();
        /// <summary>当前视角表现持有的地图租约；0 表示无所有权，旧地图恢复请求不会影响新相机。</summary>
        private long firstPersonMapInstanceId;
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
            ConfigureRoundBounds(arenaHalfWidth, arenaHalfHeight, boundsCatalog.Rule.PlayerRadius);
            playerMotor.SetArcadePresentationActive(true);
            hasPlayerHealthBaseline = false;
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
            ReleaseSlayerMartRegion();
            ReleaseRoundContent();
            ReleaseIslandEntryWarning();
            ClearPickupFeedback();
            EndFirstPerson();
            firstPersonProjectileHeights.Clear();
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
            playerMotor?.SetArcadeWeaponPresentation(null);
            playerMotor?.SetArcadePresentationActive(false);
            playerMotor = null;
            hasPlayerHealthBaseline = false;
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
            if (root == null) return;
            if (hazardPresentation == null && hazards.Count > 0)
                hazardPresentation = new ArcadeHazardPresentation(root.transform, contentTemplates?.Find("ElectricTower")?.gameObject);
            hazardPresentation?.Sync(hazards, elapsedSeconds);
        }

        public void SetExits(IReadOnlyList<int> destinations, float halfHeight, bool victory)
        {
            contentDestinations = destinations.ToArray(); contentExitHeight = halfHeight; contentVictory = victory;
            if (root == null) return;
            var identity = (victory ? "victory" : string.Join(",", destinations)) + ":" + Loc.T("ui.arcade.destination.next");
            if (string.Equals(identity, exitVisualIdentity, StringComparison.Ordinal)) return;
            exitVisualIdentity = identity;
            exitCues.Clear();
            exitCueElapsed = 0;
            foreach (var marker in exitVisuals) DestroyObject(marker);
            exitVisuals.Clear();
            var count = victory ? 1 : destinations.Count;
            if (count == 0) return;
            bool formalExit = roundLayout != null || contentArena <= 3 && !victory;
            if (contentTemplates == null) { exitVisualIdentity = null; return; }
            // 正式门自带材质；旧圆柱出口也从资源引用复制，Android Host 不依赖 Shader 名称查找。
            if (!formalExit && exitMaterial == null)
            {
                exitMaterial = new Material(contentTemplates.Find("RouteGate/ExitCue/GroundRing").GetComponent<LineRenderer>().sharedMaterial);
                exitMaterial.SetColor("_BaseColor", new Color(.15f, .85f, .55f, 1f));
            }
            for (var index = 0; index < count; index++)
            {
                var position = roundLayout != null
                    ? new Vector2(roundLayout.ExitX + (index - (count - 1) * .5f) * roundLayout.ExitSpacing, roundLayout.ExitY)
                    : islandRound > 0 && Game.Interface.GetUtility<IRuntimeConfigProvider>().TryGetArcadeCatalog(out var exitCatalog, out _)
                    ? ArcadeIslandLayout.GetExitPosition(islandRound, index, count, exitCatalog.Rule.ArenaHalfWidth,
                        exitCatalog.Rule.ArenaHalfHeight, exitCatalog.Rule.PlayerRadius)
                    : ArcadeCombatSystem.ArcadeExitPosition(index, count, halfHeight);
                GameObject marker;
                if (formalExit)
                {
                    marker = CreateContent("RouteGate", new Vector3(position.x, 0, position.y));
                    if (!victory && destinations[index] < 0)
                        marker.GetComponentInChildren<TMPro.TextMeshPro>(true).text = Loc.T("ui.arcade.exit.enter_round");
                    else SetContentLabel(marker, victory ? "ui.arcade.exit.victory" : DestinationKey(destinations[index]));
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
                marker.name = victory ? "ArcadeVictoryExit" : "ArcadeRouteExit_" + destinations[index];
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
            SetExitGuidePlayer(position, visible);
            if (playerMotor == null || !playerMotor.IsValid || mapRoot == null) return;
            playerMotor.SetAlivePresentation(visible);
            playerMotor.SetArcadeProtectionActive(visible && invulnerable);
            if (!visible)
            {
                hasPlayerHealthBaseline = false;
                return;
            }
            var local = mapRoot.InverseTransformPoint(playerMotor.Position);
            var target = mapRoot.TransformPoint(new Vector3(position.x, local.y, position.y));
            if (pendingRegionPosition) { playerMotor.SetArcadeRegionPosition(target); pendingRegionPosition = false; }
            else playerMotor.Move(target - playerMotor.Position);
            if (facing.sqrMagnitude > 0.001f)
                playerMotor.FaceDirection(mapRoot.TransformDirection(new Vector3(facing.x, 0f, facing.y)));
        }

        public void SetPlayerWeapon(string weaponId)
        {
            RememberWeaponForAudio(weaponId);
            playerMotor?.SetArcadeWeaponPresentation(weaponId);
        }

        public void SetPlayerWeapon(ArcadeWeaponDefinition weapon)
        {
            RememberWeaponForAudio(weapon.Id);
            currentWeaponIsMelee = weapon.Behavior == global::ArcadeWeaponBehavior.Melee;
            playerMotor?.SetArcadeWeaponPresentation(weapon);
        }

        private void RememberWeaponForAudio(string weaponId)
        {
            if (string.Equals(currentWeaponId, weaponId, StringComparison.Ordinal)) return;
            currentWeaponId = weaponId;
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

        /// <summary>每次成功使用快捷炸弹播放一次；不与投射物爆炸限频共用状态。</summary>
        public void PlayBombExplosion(Vector2 position)
        {
            if (root == null || mapRoot == null) return;
            var worldPosition = mapRoot.TransformPoint(new Vector3(position.x, 0.75f, position.y));
            Game.Interface.GetSystem<AudioSystem>()?.PlaySoundAt("ArcadeBombExplosion", worldPosition, 40f, 30);
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

        public void SetEnemy(int id, string configId, Vector2 position, int currentHealth, int maximumHealth,
            bool telegraph = false, int attackSequence = 0, bool persistentHealthBar = false)
        {
            RecordRoundEnemyPosition(id, position);
            SetRoundEnemyHealth(id, currentHealth, maximumHealth);
            if (HasRoundEnemyContent(id)) return;
            if (enemyPresentation == null || mapRoot == null) return;
            var localPlayer = mapRoot.InverseTransformPoint(playerMotor.Position);
            var facing = new Vector3(localPlayer.x - position.x, 0f, localPlayer.z - position.y);
            enemyPresentation.TrySetEnemy(id, configId, mapRoot.TransformPoint(World(position, 0.6f)),
                mapRoot.TransformDirection(facing), currentHealth, maximumHealth, out _, telegraph,
                attackSequence, persistentHealthBar);
        }

        public void RemoveEnemy(int id, bool playDeath = false)
        {
            RemoveRoundEnemyContent(id);
            RemoveArmor(id);
            enemyPresentation?.RemoveEnemy(id, playDeath);
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
                if (IsFirstPersonActive && Game.Interface.GetSystem<PlayerCameraSystem>()
                    .TryCreateAimRay(Vector2.zero, out var ray))
                    firstPersonProjectileHeights[id] = mapRoot.InverseTransformPoint(ray.origin).y;
                else if (hasLastShotMuzzle)
                    projectileMuzzleDepartures[id] = new ProjectileMuzzleDeparture(lastShotMuzzle, authoritative);
            }
            if (firstPersonProjectileHeights.TryGetValue(id, out var height))
                authoritative = mapRoot.TransformPoint(World(position, height));
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
            firstPersonProjectileHeights.Remove(id);
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

        public void RemoveDrop(int id) => lootPresentation?.RemoveDrop(id);
        public void SetDropOpacity(int id, float opacity) => lootPresentation?.SetDropOpacity(id, opacity);

        public bool IsFirstPersonActive => firstPersonMapInstanceId != 0 &&
            Game.Interface.GetSystem<PlayerCameraSystem>().IsArcadeFirstPerson(firstPersonMapInstanceId);

        /// <summary>正式地图与玩家资源通过检查后复用相机；失败不建立表现所有权。</summary>
        public bool TryBeginFirstPerson(out string error)
        {
            if (root == null || mapRoot == null || playerMotor == null || !playerMotor.IsValid)
            { error = "街机玩家表现尚未准备完成"; return false; }
            var id = Game.Interface.GetSystem<MapSceneSystem>().CurrentMapInstanceId;
            if (!Game.Interface.GetSystem<PlayerCameraSystem>().TryBeginArcadeFirstPerson(id, out error)) return false;
            firstPersonMapInstanceId = id;
            return true;
        }

        /// <summary>仅释放当前地图的视角租约，过期恢复不写入新相机。</summary>
        public void EndFirstPerson()
        {
            if (firstPersonMapInstanceId == 0) return;
            var id = firstPersonMapInstanceId;
            firstPersonMapInstanceId = 0;
            Game.Interface.GetSystem<PlayerCameraSystem>().EndArcadeFirstPerson(id);
        }

        public void ApplyFirstPersonLook(Vector2 delta)
        { if (IsFirstPersonActive) Game.Interface.GetSystem<PlayerCameraSystem>().TryApplyLookDelta(delta); }

        /// <summary>屏幕中心水平射线转成竞技场方向；不从表现端口结算命中。</summary>
        public bool TryReadFirstPersonAim(out Vector2 aim)
        {
            aim = default;
            if (!IsFirstPersonActive || mapRoot == null || !Game.Interface.GetSystem<PlayerCameraSystem>()
                .TryCreateAimRay(Vector2.zero, out var ray)) return false;
            var local = mapRoot.InverseTransformDirection(ray.direction);
            aim = new Vector2(local.x, local.z).normalized;
            return aim.sqrMagnitude > 0.99f && Mathf.Abs(local.y) < 0.001f;
        }

        public void PlayFirstPersonPickup() => ShowPickup("first-person", 1,
            _ => Loc.T("ui.arcade.first_person.picked"));

        /// <summary>沿用浮动提示显示实际补充次数；不根据掉落消失或库存差值推断成功。</summary>
        public void PlaySupplyPickup(ArcadeDropDefinition definition, int added)
        {
            if (added <= 0) return;
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
                if (pair.Value.LastSeenRevision != effectSyncRevision && pair.Value.ShieldFlashRemaining <= 0f)
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
            TickExitCues(deltaTime);
            playerMotor?.TickCombatFeedback(deltaTime);
            foreach (RemotePlayerEntry entry in remotePlayers.Values) entry.View?.Tick(deltaTime);
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
            if (snap) entry.View.Snap(local, yaw); else entry.View.SetTarget(local, yaw);
            entry.View.SetAlive(entry.Visible);
            entry.View.Instance?.GetComponent<PlayerView>()?.SetArcadeProtectionActive(entry.Visible && entry.Invulnerable);
            ApplyRemotePlayerCombat(entry);
        }

        private void ReleaseRemotePlayer(RemotePlayerEntry entry)
        {
            // 街机远端同样拥有脚步句柄和状态订阅，须先取消表现，再释放实例。
            entry?.View?.Dispose();
            if (entry?.View?.Instance != null) Game.Interface.GetUtility<IViewInstanceProvider>()?.Release(entry.View.Instance);
            if (entry != null && entry.Handle.IsValid) ResourceLoader.Release(entry.Handle);
            if (entry != null) { entry.View = null; entry.Handle = ResourceLoadHandle.Empty; }
        }

        private sealed class RemotePlayerEntry
        {
            public long PlayerId;
            public int Generation;
            public Vector2 Position;
            public Vector2 Facing;
            public bool Visible;
            /// <summary>远端异步创建期间缓存最新合并保护状态，首次就绪不补播过期保护。</summary>
            public bool Invulnerable;
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
                GameObject instance = UnityEngine.Object.Instantiate(prefab, placement.Parent, false);
                instance.name = "Visual";
                int variantIndex = PositiveModulo(arenaNumber - 1 + placement.Index * 2, 4);
                for (var childIndex = 0; childIndex < instance.transform.childCount; childIndex++)
                    instance.transform.GetChild(childIndex).gameObject.SetActive(childIndex == variantIndex);
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

        private static string ResolveDropDisplayName(ArcadeDropDefinition definition)
        {
            if (definition.Kind == global::ArcadeDropKind.RunGold)
                return Loc.T("ui.arcade.hud.gold", string.Empty).Trim();
            return ArcadeUiFormatter.ProductName(definition.ProductId);
        }

        private static Vector3 World(Vector2 position, float y) => new Vector3(position.x, y, position.y);

        private static void DestroyObject(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
