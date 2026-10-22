using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Luban;

namespace ET
{
    public sealed class ServerConfig : Singleton<ServerConfig>, ISingletonAwake<string>
    {
        private Tables tables;
        private readonly Dictionary<int, UnitConfig> units = new();
        private readonly Dictionary<int, SortedDictionary<int, AIConfig>> aiConfigs = new();
        private readonly Dictionary<int, List<AIConfig>> aiConfigLists = new();
        private readonly Dictionary<int, StartMachineConfig> machines = new();
        private readonly List<StartMachineConfig> machineList = new();
        private readonly Dictionary<int, StartProcessConfig> processes = new();
        private readonly List<StartProcessConfig> processList = new();
        private readonly Dictionary<int, StartZoneConfig> zones = new();
        private readonly Dictionary<int, StartSceneConfig> scenes = new();
        private readonly Dictionary<SceneType, List<StartSceneConfig>> scenesByType = new();
        private readonly MultiMap<int, StartSceneConfig> gates = new();
        private readonly List<StartSceneConfig> gateList = new();
        private readonly MultiMap<int, StartSceneConfig> processScenes = new();
        private readonly Dictionary<long, Dictionary<string, StartSceneConfig>> clientScenesByName = new();
        private readonly List<StartSceneConfig> realms = new();
        private readonly List<StartSceneConfig> routers = new();
        private readonly List<StartSceneConfig> maps = new();
        private ArcadeRuleConfig arcadeRule;
        /// <summary>同源新商店目录；正式能力未完成时目录可验证但入口关闭。</summary>
        public FirepowerLoop.Arcade.ArcadeSlayerMartConfiguration SlayerMart { get; private set; }
        /// <summary>独立Slayer挑战正式配置，启动时完整校验。</summary>
        public FirepowerLoop.Arcade.ArcadeSlayerChallengeConfiguration SlayerChallenge {get;private set;}
        // 街机敌人及其投射物仍消费共用表现配置，不加载开放世界库存或地图规则。
        private readonly Dictionary<string, EnemyConfig> enemies = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ProjectileConfig> projectiles = new(StringComparer.Ordinal);

        public Tables Tables => this.tables;
        public string Profile { get; private set; }
        public IReadOnlyDictionary<int, UnitConfig> Units => this.units;
        public IReadOnlyDictionary<int, SortedDictionary<int, AIConfig>> AIConfigs => this.aiConfigs;
        public IReadOnlyDictionary<int, StartMachineConfig> Machines => this.machines;
        public IReadOnlyDictionary<int, StartProcessConfig> Processes => this.processes;
        public IReadOnlyDictionary<int, StartZoneConfig> Zones => this.zones;
        public IReadOnlyDictionary<int, StartSceneConfig> Scenes => this.scenes;
        public MultiMap<int, StartSceneConfig> Gates => this.gates;
        public List<StartSceneConfig> Realms => this.realms;
        public List<StartSceneConfig> Routers => this.routers;
        public List<StartSceneConfig> Maps => this.maps;
        public StartSceneConfig LocationConfig { get; private set; }
        public StartSceneConfig Match { get; private set; }
        public StartSceneConfig Benchmark { get; private set; }
        public ArcadeRuleConfig ArcadeRule => this.arcadeRule;
        /// <summary>双端同源临时无人机规则，启动时校验。</summary>
        public FirepowerLoop.Arcade.ArcadeDroneTuning Drone {get;private set;}
        /// <summary>每轮奖励及中央传送门的配置参数。</summary>
        public FirepowerLoop.Arcade.ArcadeRoundRewardTuning RoundReward {get;private set;}
        /// <summary>同枪与头骨的共用强化参数。</summary>
        public FirepowerLoop.Arcade.ArcadeWeaponUpgradeTuning WeaponUpgrade {get;private set;}

        public void Awake(string profile)
        {
            this.Initialize(profile, LoadTable);
        }

        public static void ValidateProfile(string profile, Func<string, byte[]> loader)
        {
            CreateForValidation(profile, loader);
        }

        public static ServerConfig CreateForValidation(string profile, Func<string, byte[]> loader)
        {
            ServerConfig config = new();
            config.Initialize(profile, tableName => new ByteBuf(loader(tableName)));
            return config;
        }

        public UnitConfig GetUnit(int id)
        {
            if (this.units.TryGetValue(id, out UnitConfig unitConfig))
            {
                return unitConfig;
            }

            throw this.Missing("TbUnitConfig", id.ToString());
        }

        public IReadOnlyList<AIConfig> GetAI(int aiConfigId)
        {
            if (this.aiConfigLists.TryGetValue(aiConfigId, out List<AIConfig> aiConfigList))
            {
                return aiConfigList;
            }

            throw this.Missing("TbAIConfig", $"AIConfigId:{aiConfigId}");
        }

        public StartMachineConfig GetMachine(int id)
        {
            if (this.machines.TryGetValue(id, out StartMachineConfig machine))
            {
                return machine;
            }

            throw this.Missing("TbStartMachine", id.ToString());
        }

        public StartProcessConfig GetProcess(int id)
        {
            if (this.processes.TryGetValue(id, out StartProcessConfig process))
            {
                return process;
            }

            throw this.Missing("TbStartProcess", id.ToString());
        }

        public StartZoneConfig GetZone(int id)
        {
            if (this.zones.TryGetValue(id, out StartZoneConfig zone))
            {
                return zone;
            }

            throw this.Missing("TbStartZone", id.ToString());
        }

        public StartSceneConfig GetScene(int id)
        {
            if (this.scenes.TryGetValue(id, out StartSceneConfig scene))
            {
                return scene;
            }

            throw this.Missing("TbStartScene", id.ToString());
        }

        public IReadOnlyList<StartMachineConfig> GetMachines() => this.machineList;

        public IReadOnlyList<StartProcessConfig> GetProcesses() => this.processList;

        public EnemyConfig GetEnemy(string id) => this.enemies.TryGetValue(id ?? string.Empty, out EnemyConfig value)
            ? value : throw this.Missing("TbEnemy", id);

        public ProjectileConfig GetProjectile(string id) => this.projectiles.TryGetValue(id ?? string.Empty, out ProjectileConfig value)
            ? value : throw this.Missing("TbProjectile", id);

        public IReadOnlyList<StartSceneConfig> GetScenesByProcess(int process)
        {
            if (this.processScenes.TryGetValue(process, out List<StartSceneConfig> sceneList))
            {
                return sceneList;
            }

            throw this.Missing("TbStartScene", $"Process:{process}");
        }

        public StartSceneConfig GetSceneByName(int zone, string name)
        {
            if (this.clientScenesByName.TryGetValue(zone, out Dictionary<string, StartSceneConfig> scenesByName) &&
                scenesByName.TryGetValue(name, out StartSceneConfig scene))
            {
                return scene;
            }

            throw this.Missing("TbStartScene", $"Zone:{zone},Name:{name}");
        }

        public IReadOnlyList<StartSceneConfig> GetScenes(SceneType type)
        {
            if (this.scenesByType.TryGetValue(type, out List<StartSceneConfig> sceneList))
            {
                return sceneList;
            }

            throw this.Missing("TbStartScene", $"SceneType:{type}");
        }

        private void Initialize(string profile, Func<string, ByteBuf> loader)
        {
            this.Profile = ValidatePureProfile(profile);
            this.tables = new Tables(loader);
            this.BuildIndexes();
        }

        private static string ValidatePureProfile(string profile)
        {
            if (string.IsNullOrWhiteSpace(profile))
            {
                throw new Exception("Luban server config invalid. Profile=<empty>, Table=StartConfig, Key=StartConfig, StartConfig must be a pure Profile name.");
            }

            string trimmed = profile.Trim();
            if (trimmed.Contains("/", StringComparison.Ordinal) || trimmed.Contains("\\", StringComparison.Ordinal))
            {
                throw new Exception($"Luban server config invalid. Profile={trimmed}, Table=StartConfig, Key={trimmed}, StartConfig must be a pure Profile name.");
            }

            return trimmed;
        }

        private static ByteBuf LoadTable(string tableName)
        {
            return new ByteBuf(LoadTableBytes(tableName));
        }

        private static byte[] LoadTableBytes(string tableName)
        {
            string filePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Config", "Luban", $"{tableName}.bytes"));
            if (!File.Exists(filePath))
            {
                filePath = Path.GetFullPath(Path.Combine("..", "Config", "Luban", $"{tableName}.bytes"));
            }

            return File.ReadAllBytes(filePath);
        }

        private void BuildIndexes()
        {
            this.IndexSharedTables();
            this.IndexProfileTables();

            if (this.machines.Count == 0 || this.processes.Count == 0 || this.zones.Count == 0 || this.scenes.Count == 0)
            {
                throw ConfigError(this.Profile, "StartConfig", this.Profile, "profile not found or incomplete.");
            }

            this.ValidateOnlineLocalRoles();
        }

        private void IndexSharedTables()
        {
            foreach (EnemyConfig enemy in this.tables.TbEnemy.DataList)
                if (!this.enemies.TryAdd(enemy.Id, enemy))
                    throw ConfigError(this.Profile, "TbEnemy", enemy.Id, "duplicate Id.");
            foreach (ProjectileConfig projectile in this.tables.TbProjectile.DataList)
                if (string.IsNullOrWhiteSpace(projectile.Id) || !Finite(projectile.MoveSpeed, projectile.MaxTravelDistance) ||
                    projectile.MoveSpeed <= 0f || projectile.MaxTravelDistance <= 0f || !this.projectiles.TryAdd(projectile.Id, projectile))
                    throw ConfigError(this.Profile, "TbProjectile", projectile.Id, "invalid or duplicate projectile.");
            this.ValidateArcadeConfig();

            HashSet<int> unitIds = new();
            foreach (UnitConfig unitConfig in this.tables.TbUnitConfig.DataList)
            {
                if (!unitIds.Add(unitConfig.Id))
                {
                    throw ConfigError(this.Profile, "TbUnitConfig", unitConfig.Id.ToString(), "duplicate Id.");
                }

                this.units.Add(unitConfig.Id, unitConfig);
            }

            HashSet<int> aiIds = new();
            HashSet<string> aiOrders = new(StringComparer.Ordinal);
            foreach (AIConfig aiConfig in this.tables.TbAIConfig.DataList)
            {
                if (!aiIds.Add(aiConfig.Id))
                {
                    throw ConfigError(this.Profile, "TbAIConfig", aiConfig.Id.ToString(), "duplicate Id.");
                }

                string orderKey = $"{aiConfig.AIConfigId}:{aiConfig.Order}";
                if (!aiOrders.Add(orderKey))
                {
                    throw ConfigError(this.Profile, "TbAIConfig", orderKey, "duplicate Order in AIConfigId group.");
                }

                if (!this.aiConfigs.TryGetValue(aiConfig.AIConfigId, out SortedDictionary<int, AIConfig> oneAI))
                {
                    oneAI = new SortedDictionary<int, AIConfig>();
                    this.aiConfigs.Add(aiConfig.AIConfigId, oneAI);
                }

                oneAI.Add(aiConfig.Order, aiConfig);
            }

            foreach (var kv in this.aiConfigs)
            {
                this.aiConfigLists.Add(kv.Key, new List<AIConfig>(kv.Value.Values));
            }
        }

        /// <summary>服务启动时拒绝不能创建权威安全节点的街机配置。</summary>
        private void ValidateArcadeConfig()
        {
            this.SlayerMart=FirepowerLoop.Arcade.ArcadeSlayerMartConfiguration.FromTables(this.tables);
            this.SlayerChallenge=FirepowerLoop.Arcade.ArcadeSlayerChallengeConfiguration.FromTables(this.tables);
            foreach (var row in this.tables.TbArcadeTreasureRoom.DataList)
            {
                var room = FirepowerLoop.Arcade.ArcadeTreasureRoomParameters.FromConfig(row);
                foreach (var reward in room.Rewards.Where(r => r.Kind == FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Weapon))
                {
                    var drop = this.tables.TbArcadeDrop.GetOrDefault(reward.DropId);
                    if (drop == null || drop.Kind != ArcadeDropKind.TemporaryWeapon)
                        throw ConfigError(this.Profile, "TbArcadeTreasureRoom", room.Id, "invalid weapon drop: " + reward.DropId);
                }
                var target = this.tables.TbArcadeArena.GetOrDefault(room.ArenaConfigNumber);
                var arenaRule = this.tables.TbArcadeRule.DataList.FirstOrDefault();
                if (target == null || room.WaveNumber > target.WaveGroupIds.Length || arenaRule == null ||
                    !room.FitsArena(arenaRule.ArenaHalfWidth, arenaRule.ArenaHalfHeight, arenaRule.PlayerRadius))
                    throw ConfigError(this.Profile, "TbArcadeTreasureRoom", room.Id, "invalid arena or wave");
            }
            if(this.tables.TbArcadeWeaponUpgrade.DataList.Count!=1)throw ConfigError(this.Profile,"TbArcadeWeaponUpgrade","arcade.weapon.upgrade","expected one row");
            var upgrade=this.tables.TbArcadeWeaponUpgrade.DataList[0];
            this.WeaponUpgrade=FirepowerLoop.Arcade.ArcadeWeaponUpgradeTuning.FromConfig(upgrade);
            if(upgrade.Id!="arcade.weapon.upgrade"||!FirepowerLoop.Arcade.ArcadeWeaponUpgradeRules.Valid(this.WeaponUpgrade)||
                upgrade.RedPowerupId_Ref.Behavior!=ArcadePowerupBehavior.WeaponSkull||upgrade.RedPowerupId_Ref.Magnitude!=2||
                upgrade.PurplePowerupId_Ref.Behavior!=ArcadePowerupBehavior.WeaponSkull||upgrade.PurplePowerupId_Ref.Magnitude!=3)throw ConfigError(this.Profile,"TbArcadeWeaponUpgrade",upgrade.Id,"invalid tuning or skull reference");
            this.RoundReward=FirepowerLoop.Arcade.ArcadeRoundRewardTuning.FromConfig(this.tables.TbArcadeRoundReward.DataList.FirstOrDefault());
            if(this.tables.TbArcadeRoundReward.DataList.Count!=1||this.tables.TbArcadeRoundReward.DataList[0].Id!="arcade.round.reward"||!FirepowerLoop.Arcade.ArcadeRoundRewardRules.IsValid(this.RoundReward)||!float.IsFinite(this.RoundReward.PortalRadius)||this.RoundReward.PortalRadius<=0||!float.IsFinite(this.RoundReward.PortalAppearSeconds)||this.RoundReward.PortalAppearSeconds<=0)
                throw ConfigError(this.Profile,"TbArcadeRoundReward","arcade.round.reward","invalid round reward tuning");
            this.Drone=FirepowerLoop.Arcade.ArcadeDroneTuning.FromConfig(this.tables.TbArcadeDrone.DataList.FirstOrDefault());
            if(this.tables.TbArcadeDrone.DataList.Count!=1||this.tables.TbArcadeDrone.DataList[0].Id!="arcade.support.drone"||!FirepowerLoop.Arcade.ArcadeDroneRules.IsValid(this.Drone))
                throw ConfigError(this.Profile,"TbArcadeDrone","arcade.support.drone","invalid drone tuning");
            if (this.tables.TbArcadeRule.DataList.Count != 1)
                throw ConfigError(this.Profile, "TbArcadeRule", "arcade.default", "expected exactly one row.");
            this.arcadeRule = this.tables.TbArcadeRule.DataList[0];
            if(Math.Abs(this.RoundReward.PortalX)+this.RoundReward.PortalRadius>=this.arcadeRule.ArenaHalfWidth||Math.Abs(this.RoundReward.PortalZ)+this.RoundReward.PortalRadius>=this.arcadeRule.ArenaHalfHeight)throw ConfigError(this.Profile,"TbArcadeRoundReward","arcade.round.reward","portal outside arena");
            if (!string.Equals(this.arcadeRule.Id, "arcade.default", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(this.arcadeRule.ConfigFingerprint) || this.arcadeRule.ConfigFingerprint.Length > 128 ||
                this.arcadeRule.InitialLives <= 0 || this.arcadeRule.InitialBombs < 0 || this.arcadeRule.InitialDashes < 0 ||
                !float.IsFinite(this.arcadeRule.DropChance) || this.arcadeRule.DropChance < 0 || this.arcadeRule.DropChance > 1 ||
                !float.IsFinite(this.arcadeRule.EnemyDropHealthUnit) || this.arcadeRule.EnemyDropHealthUnit <= 0 ||
                !float.IsFinite(this.arcadeRule.EnemyDropDamageUnit) || this.arcadeRule.EnemyDropDamageUnit <= 0 ||
                !float.IsFinite(this.arcadeRule.EnemyDropMaxChance) || this.arcadeRule.EnemyDropMaxChance < this.arcadeRule.DropChance || this.arcadeRule.EnemyDropMaxChance > 1 ||
                !float.IsFinite(this.arcadeRule.CoopDownedSeconds) || this.arcadeRule.CoopDownedSeconds<=0 ||
                !float.IsFinite(this.arcadeRule.CoopRescueRadius) || this.arcadeRule.CoopRescueRadius<=0 ||
                !float.IsFinite(this.arcadeRule.WeaponDeliverySeconds) || this.arcadeRule.WeaponDeliverySeconds<=0 ||
                !float.IsFinite(this.arcadeRule.CoopRescueSpeed) || this.arcadeRule.CoopRescueSpeed<1 || this.arcadeRule.CoopDonationMinLives<2 ||
                string.IsNullOrWhiteSpace(this.arcadeRule.BaseWeaponId) ||
                this.tables.TbArcadeWeapon.GetOrDefault(this.arcadeRule.BaseWeaponId) == null)
                throw ConfigError(this.Profile, "TbArcadeRule", this.arcadeRule.Id, "identity or initial resources are invalid.");

            HashSet<string> weaponIds = new(StringComparer.Ordinal);
            foreach (ArcadeWeaponConfig weapon in this.tables.TbArcadeWeapon.DataList)
            {
                if (weapon == null || string.IsNullOrWhiteSpace(weapon.Id) || !weaponIds.Add(weapon.Id))
                    throw ConfigError(this.Profile, "TbArcadeWeapon", weapon?.Id ?? "<null>",
                        "missing or duplicate weapon identity.");
                bool validBehavior = weapon.Behavior switch
                {
                    ArcadeWeaponBehavior.Melee => weapon.ProjectileCount == 1 && weapon.SpreadDegrees == 0f &&
                        weapon.MaxHits == 1 && weapon.ExplosionRadius == 0f &&
                        string.IsNullOrWhiteSpace(weapon.ProjectileVisualResourceName) &&
                        Finite(weapon.MeleeRange, weapon.MeleeArcDegrees, 0f, 0f) &&
                        weapon.MeleeRange > 0f && weapon.MeleeArcDegrees > 0f && weapon.MeleeArcDegrees <= 360f,
                    ArcadeWeaponBehavior.Direct => weapon.MaxHits == 1 && weapon.ExplosionRadius == 0f,
                    ArcadeWeaponBehavior.Piercing => weapon.MaxHits > 1 && weapon.ExplosionRadius == 0f,
                    ArcadeWeaponBehavior.Explosive => weapon.MaxHits == 1 && weapon.ExplosionRadius > 0f,
                    ArcadeWeaponBehavior.TimedExplosive => weapon.MaxHits == 1 && weapon.ExplosionRadius > 0f,
                    _ => false,
                };
                if (weapon.Damage <= 0 || weapon.ProjectileCount <= 0 || weapon.ProjectileCount > 12 ||
                    !Finite(weapon.FireIntervalSeconds, weapon.ProjectileSpeed, weapon.ProjectileLifetimeSeconds,
                        weapon.SpreadDegrees) || weapon.FireIntervalSeconds <= 0f ||
                    (weapon.Behavior == ArcadeWeaponBehavior.Melee
                        ? weapon.ProjectileSpeed != 0f || weapon.ProjectileLifetimeSeconds != 0f
                        : weapon.ProjectileSpeed <= 0f || weapon.ProjectileLifetimeSeconds <= 0f) ||
                    weapon.SpreadDegrees < 0f ||
                    weapon.SpreadDegrees > 180f || !Finite(weapon.ExplosionRadius, 1f, 1f, 1f) || !validBehavior ||
                    (weapon.Behavior != ArcadeWeaponBehavior.Melee && (weapon.MeleeRange != 0f || weapon.MeleeArcDegrees != 0f)))
                    throw ConfigError(this.Profile, "TbArcadeWeapon", weapon?.Id ?? "<null>",
                        "invalid weapon values or behavior contract.");
            }

            HashSet<string> powerupIds = new(StringComparer.Ordinal);
            foreach (ArcadePowerupConfig powerup in this.tables.TbArcadePowerup.DataList)
            {
                if (powerup == null || string.IsNullOrWhiteSpace(powerup.Id) || !powerupIds.Add(powerup.Id) ||
                    !Enum.IsDefined(powerup.Behavior) ||
                    !Finite(powerup.Magnitude, powerup.IntervalSeconds, powerup.Radius, powerup.ProjectileSpeed) ||
                    powerup.Magnitude < 0f || powerup.Damage < 0 || powerup.IntervalSeconds < 0f ||
                    (powerup.Behavior==ArcadePowerupBehavior.WeaponSkull&&powerup.Magnitude!=2f&&powerup.Magnitude!=3f) ||
                    powerup.Radius < 0f || powerup.ProjectileSpeed < 0f || powerup.Charges < 0)
                    throw ConfigError(this.Profile, "TbArcadePowerup", powerup?.Id ?? "<null>",
                        "invalid powerup values.");
            }

            HashSet<string> dropIds = new(StringComparer.Ordinal);
            foreach (ArcadeDropConfig drop in this.tables.TbArcadeDrop.DataList)
            {
                bool timed = drop?.Kind is ArcadeDropKind.TemporaryWeapon or ArcadeDropKind.Support or
                    ArcadeDropKind.Powerup;
                if (drop == null || string.IsNullOrWhiteSpace(drop.Id) || !dropIds.Add(drop.Id) ||
                    drop.MinArena <= 0 || drop.MaxArena < 0 || drop.MaxArena != 0 && drop.MaxArena < drop.MinArena ||
                    drop.EnemyMinArena < 0 || drop.Weight <= 0 || string.IsNullOrWhiteSpace(drop.ProductId) || drop.Amount <= 0 ||
                    !Finite(drop.DurationSeconds, 1f, 1f, 1f) || drop.DurationSeconds < 0f ||
                    timed != (drop.DurationSeconds > 0f) ||
                    drop.Kind == ArcadeDropKind.TemporaryWeapon && !weaponIds.Contains(drop.ProductId) ||
                    drop.Kind == ArcadeDropKind.Powerup && !powerupIds.Contains(drop.ProductId)||
                    drop.Kind==ArcadeDropKind.Support&&(drop.ProductId!="arcade.support.drone"||!powerupIds.Contains(drop.ProductId)||drop.DurationSeconds!=this.Drone.DurationSeconds))
                    throw ConfigError(this.Profile, "TbArcadeDrop", drop?.Id ?? "<null>",
                        "invalid drop values or product reference.");
            }

            // 支援视觉仅服务旧局已持有装备，不提供商品与价格。
            HashSet<string> supportIds = new(StringComparer.Ordinal);
            foreach (ArcadeSupportVisualConfig visual in this.tables.TbArcadeSupportVisual.DataList)
                if (string.IsNullOrWhiteSpace(visual.Id) || !supportIds.Add(visual.Id) ||
                    string.IsNullOrWhiteSpace(visual.ProductId) || visual.EquipmentSlotId != "support" ||
                    !float.IsFinite(visual.VisualOffsetX) || !float.IsFinite(visual.VisualOffsetY) || !float.IsFinite(visual.VisualOffsetZ))
                    throw ConfigError(this.Profile, "TbArcadeSupportVisual", visual.Id, "invalid support visual.");

            Dictionary<string, ArcadeBossConfig> bosses = new(StringComparer.Ordinal);
            foreach (ArcadeBossConfig boss in this.tables.TbArcadeBoss.DataList)
            {
                ArcadeEnemyGroupConfig group = string.IsNullOrWhiteSpace(boss?.EnemyGroupId)
                    ? null
                    : this.tables.TbArcadeEnemyGroup.GetOrDefault(boss.EnemyGroupId);
                if (boss == null || string.IsNullOrWhiteSpace(boss.Id) || !bosses.TryAdd(boss.Id, boss) ||
                    group?.Members == null || group.Members.Length != 1 || group.Members[0].Count != 1 ||
                    !Finite(boss.SkillCooldownSeconds, boss.TelegraphSeconds, boss.ChargeDurationSeconds,
                        boss.ChargeSpeedMultiplier) ||
                    !Finite(boss.ImpactRadius, boss.ImpactDamageMultiplier, 1f, 1f) ||
                    boss.SkillCooldownSeconds <= 0f || boss.TelegraphSeconds <= 0f ||
                    boss.ChargeDurationSeconds <= 0f || boss.ChargeSpeedMultiplier <= 1f ||
                    boss.ImpactRadius <= 0f || boss.ImpactDamageMultiplier <= 0f)
                    throw ConfigError(this.Profile, "TbArcadeBoss", boss?.Id ?? "<null>",
                        "invalid boss values or enemy group reference.");
                if (!Finite(boss.RayIntervalSeconds, boss.RayTelegraphSeconds, boss.RaySpeed, boss.RayDamageMultiplier) ||
                    (boss.RayIntervalSeconds == 0f ? boss.RayTelegraphSeconds != 0f || boss.RaySpeed != 0f || boss.RayDamageMultiplier != 0f :
                        boss.RayIntervalSeconds < 0f || boss.RayTelegraphSeconds <= 0f || boss.RaySpeed <= 0f || boss.RayDamageMultiplier <= 0f) ||
                    !Finite(boss.ReinforcementIntervalSeconds, 1f, 1f, 1f) ||
                    (string.IsNullOrEmpty(boss.ReinforcementGroupId) ? boss.ReinforcementIntervalSeconds != 0f :
                        boss.ReinforcementIntervalSeconds <= 0f || this.tables.TbArcadeEnemyGroup.GetOrDefault(boss.ReinforcementGroupId) == null))
                    throw ConfigError(this.Profile, "TbArcadeBoss", boss.Id, "invalid ray or reinforcement values.");
            }

            HashSet<string> challengeIds = new(StringComparer.Ordinal);
            foreach (ArcadeChallengeConfig challenge in this.tables.TbArcadeChallenge.DataList)
            {
                if (challenge == null || string.IsNullOrWhiteSpace(challenge.Id) ||
                    !challengeIds.Add(challenge.Id) || string.IsNullOrWhiteSpace(challenge.EnemyGroupId) ||
                    this.tables.TbArcadeEnemyGroup.GetOrDefault(challenge.EnemyGroupId) == null ||
                    challenge.StartArena <= 0 || challenge.Interval <= 0 ||
                    !Finite(challenge.TimeLimitSeconds, 1f, 1f, 1f) || challenge.TimeLimitSeconds <= 0f)
                    throw ConfigError(this.Profile, "TbArcadeChallenge", challenge?.Id ?? "<null>",
                        "invalid challenge values or enemy group reference.");
            }
            if (challengeIds.Count == 0)
                throw ConfigError(this.Profile, "TbArcadeChallenge", "count", "expected at least one challenge.");

            foreach (ArcadeArenaConfig arena in this.tables.TbArcadeArena.DataList)
            {
                var opening = FirepowerLoop.Arcade.ArcadeOpeningParameters.FromConfig(arena);
                var content = FirepowerLoop.Arcade.ArcadeBabylonParameters.FromConfig(arena);
                if (content.Crates.Any(c => Math.Abs(c.X) + c.Radius > this.arcadeRule.ArenaHalfWidth || Math.Abs(c.Y) + c.Radius > this.arcadeRule.ArenaHalfHeight))
                    throw ConfigError(this.Profile, "TbArcadeArena", arena.ArenaNumber.ToString(), "invalid crate position");
                if (opening.Enabled && (opening.WaterMinY <= -this.arcadeRule.ArenaHalfHeight || opening.WaterMinY >= this.arcadeRule.ArenaHalfHeight ||
                    Math.Abs(opening.CampfireX) + opening.CampfireRadius > this.arcadeRule.ArenaHalfWidth ||
                    Math.Abs(opening.CampfireY) + opening.CampfireRadius > this.arcadeRule.ArenaHalfHeight ||
                    arena.WaveGroupIds.Any(id => this.tables.TbArcadeEnemyGroup.GetOrDefault(id)?.Members.Length != 1)))
                    throw ConfigError(this.Profile, "TbArcadeArena", arena.ArenaNumber.ToString(), "invalid island zones or opening group.");
                if (arena.NextArenaNumbers == null || arena.NextArenaNumbers.Length > 4 ||
                    arena.NextArenaNumbers.Distinct().Count() != arena.NextArenaNumbers.Length ||
                    arena.NextArenaNumbers.Any(next => next == arena.ArenaNumber || this.tables.TbArcadeArena.GetOrDefault(next) == null) ||
                    (!string.IsNullOrEmpty(arena.FinalBossId) && (!bosses.ContainsKey(arena.FinalBossId) || arena.NextArenaNumbers.Length != 0)))
                    throw ConfigError(this.Profile, "TbArcadeArena", arena.ArenaNumber.ToString(), "invalid route or final Boss reference.");
                if (arena.Hazards == null || arena.Hazards.Length > 8)
                    throw ConfigError(this.Profile, "TbArcadeArena", arena.ArenaNumber.ToString(), "invalid hazard count.");
                HashSet<int> hazardIds = new();
                foreach (ArcadeHazardConfig hazard in arena.Hazards)
                {
                    if (hazard == null || !hazardIds.Add(hazard.Id))
                        throw ConfigError(this.Profile, "TbArcadeArena", arena.ArenaNumber.ToString(), "duplicate hazard identity.");
                    var parameters = GetHazardParameters(hazard);
                    string hazardError = FirepowerLoop.Arcade.ArcadeHazardRules.Validate(parameters,
                        this.arcadeRule.ArenaHalfWidth, this.arcadeRule.ArenaHalfHeight, this.arcadeRule.PlayerRadius);
                    if (hazardError != null) throw ConfigError(this.Profile, "TbArcadeArena", arena.ArenaNumber.ToString(), hazardError);
                    foreach (ArcadeObstacleConfig obstacle in arena.Obstacles)
                    {
                        float dx = Math.Max(0f, Math.Abs(parameters.X - obstacle.CenterX) - obstacle.HalfWidth);
                        float dy = Math.Max(0f, Math.Abs(parameters.Y - obstacle.CenterY) - obstacle.HalfHeight);
                        if (dx * dx + dy * dy <= Math.Pow(parameters.Extent + this.arcadeRule.PlayerRadius * 2f, 2))
                            throw ConfigError(this.Profile, "TbArcadeArena", arena.ArenaNumber.ToString(), "hazard blocks obstacle safety passage.");
                    }
                }
                if (string.IsNullOrWhiteSpace(arena.BossId)) continue;
                if (!arena.IsBoss || !bosses.TryGetValue(arena.BossId, out ArcadeBossConfig boss) ||
                    arena.WaveGroupIds == null || arena.WaveGroupIds.Length == 0 ||
                    !string.Equals(arena.WaveGroupIds[^1], boss.EnemyGroupId, StringComparison.Ordinal))
                    throw ConfigError(this.Profile, "TbArcadeArena", arena.ArenaNumber.ToString(),
                        "BossId must match the final wave enemy group.");
            }
            bool ValidateRoute(int id, int depth)
            {
                if (depth > 10) return false;
                var arena = this.tables.TbArcadeArena.Get(id);
                if (arena.NextArenaNumbers.Length == 0) return depth == 10 && !string.IsNullOrEmpty(arena.FinalBossId);
                return depth < 10 && string.IsNullOrEmpty(arena.FinalBossId) &&
                    arena.NextArenaNumbers.All(next => ValidateRoute(next, depth + 1));
            }
            if (!ValidateRoute(1, 1))
                throw ConfigError(this.Profile, "TbArcadeArena", "route", "casual routes must have ten arenas and a final Boss.");
        }

        /// <summary>将 Luban 行转换为与客户端完全相同的纯机关参数；不保存运行态。</summary>
        public static FirepowerLoop.Arcade.ArcadeHazardParameters GetHazardParameters(ArcadeHazardConfig row) =>
            new(row.Id, (int)row.Kind, row.CenterX, row.CenterY, row.DirectionDegrees, row.Radius,
                row.ArmLength, row.ArmHalfWidth, row.IdleSeconds, row.WarningSeconds, row.ActiveSeconds,
                row.AngularSpeedDegrees, row.HitIntervalSeconds, row.PlayerDamageRatio, row.BossDamageRatio);

        private static bool Finite(params float[] values)
        {
            foreach (float value in values) if (float.IsNaN(value) || float.IsInfinity(value)) return false;
            return true;
        }

        private void IndexProfileTables()
        {
            foreach (StartMachineConfig machine in this.tables.TbStartMachine.DataList)
            {
                if (machine.Profile != this.Profile)
                {
                    continue;
                }

                AddLocalId(this.machines, machine.Id, machine, "TbStartMachine");
                this.machineList.Add(machine);
                ValidateIp(machine.InnerIP, "TbStartMachine", machine.Key, "InnerIP");
                ValidateIp(machine.OuterIP, "TbStartMachine", machine.Key, "OuterIP");
                ValidatePort(machine.WatcherPort, "TbStartMachine", machine.Key, "WatcherPort");
            }

            foreach (StartZoneConfig zone in this.tables.TbStartZone.DataList)
            {
                if (zone.Profile == this.Profile)
                {
                    AddLocalId(this.zones, zone.Id, zone, "TbStartZone");
                }
            }

            foreach (StartProcessConfig process in this.tables.TbStartProcess.DataList)
            {
                if (process.Profile != this.Profile)
                {
                    continue;
                }

                AddLocalId(this.processes, process.Id, process, "TbStartProcess");
                this.processList.Add(process);
                ValidateRef(process.MachineKey_Ref, process.MachineKey, "TbStartProcess", process.Key, "MachineKey", "TbStartMachine");
                ValidatePort(process.Port, "TbStartProcess", process.Key, "Port");
            }

            foreach (StartSceneConfig scene in this.tables.TbStartScene.DataList)
            {
                if (scene.Profile != this.Profile)
                {
                    continue;
                }

                AddLocalId(this.scenes, scene.Id, scene, "TbStartScene");
                ValidateRef(scene.ProcessKey_Ref, scene.ProcessKey, "TbStartScene", scene.Key, "ProcessKey", "TbStartProcess");
                ValidateRef(scene.ZoneKey_Ref, scene.ZoneKey, "TbStartScene", scene.Key, "ZoneKey", "TbStartZone");
                ValidatePort(scene.Port, "TbStartScene", scene.Key, "Port");

                if (!Enum.TryParse(scene.SceneType, out SceneType sceneType) || sceneType == SceneType.None)
                {
                    throw ConfigError(this.Profile, "TbStartScene", scene.Key, $"invalid SceneType '{scene.SceneType}'.");
                }

                this.processScenes.Add(scene.Process, scene);

                if (!this.clientScenesByName.TryGetValue(scene.Zone, out Dictionary<string, StartSceneConfig> scenesByName))
                {
                    scenesByName = new Dictionary<string, StartSceneConfig>(StringComparer.Ordinal);
                    this.clientScenesByName.Add(scene.Zone, scenesByName);
                }

                if (!scenesByName.TryAdd(scene.Name, scene))
                {
                    throw ConfigError(this.Profile, "TbStartScene", $"Zone:{scene.Zone},Name:{scene.Name}", "duplicate Scene Name in Zone.");
                }

                this.IndexSceneType(scene, sceneType);
            }
        }

        private void IndexSceneType(StartSceneConfig scene, SceneType sceneType)
        {
            if (!this.scenesByType.TryGetValue(sceneType, out List<StartSceneConfig> sceneList))
            {
                sceneList = new List<StartSceneConfig>();
                this.scenesByType.Add(sceneType, sceneList);
            }

            sceneList.Add(scene);

            switch (sceneType)
            {
                case SceneType.Realm:
                    this.realms.Add(scene);
                    break;
                case SceneType.Gate:
                case SceneType.GameGate:
                    this.gates.Add(scene.Zone, scene);
                    this.gateList.Add(scene);
                    break;
                case SceneType.Location:
                    this.LocationConfig = scene;
                    break;
                case SceneType.Router:
                    this.routers.Add(scene);
                    break;
                case SceneType.Map:
                    this.maps.Add(scene);
                    break;
                case SceneType.Match:
                    this.Match = scene;
                    break;
                case SceneType.BenchmarkServer:
                    this.Benchmark = scene;
                    break;
            }
        }

        private void AddLocalId<T>(Dictionary<int, T> dictionary, int id, T value, string tableName)
        {
            if (!dictionary.TryAdd(id, value))
            {
                throw ConfigError(this.Profile, tableName, id.ToString(), "duplicate local Id.");
            }
        }

        private void ValidateRef<T>(T value, string refKey, string tableName, string key, string fieldName, string refTableName)
            where T : class
        {
            if (value == null)
            {
                throw ConfigError(this.Profile, tableName, key, $"{fieldName} references missing {refTableName} key '{refKey}'.");
            }

            string refProfile = value switch
            {
                StartMachineConfig machine => machine.Profile,
                StartProcessConfig process => process.Profile,
                StartZoneConfig zone => zone.Profile,
                _ => this.Profile
            };

            if (refProfile != this.Profile)
            {
                throw ConfigError(this.Profile, tableName, key, $"{fieldName} references {refTableName} key '{refKey}' from Profile '{refProfile}'.");
            }
        }

        private void ValidateIp(string ip, string tableName, string key, string fieldName)
        {
            if (!IPAddress.TryParse(ip, out _))
            {
                throw ConfigError(this.Profile, tableName, key, $"{fieldName} IP is invalid: '{ip}'.");
            }
        }

        private void ValidatePort(int port, string tableName, string key, string fieldName)
        {
            if (port != 0 && (port < 1 || port > 65535))
            {
                throw ConfigError(this.Profile, tableName, key, $"{fieldName} port is out of range: {port}.");
            }
        }

        private void ValidateOnlineLocalRoles()
        {
            if (this.Profile != "OnlineLocal")
            {
                return;
            }

            RequireExactlyOne(SceneType.GameGate);
            RequireExactlyOne(SceneType.Account);
            RequireExactlyOne(SceneType.Player);
            RequireExactlyOne(SceneType.Location);
            RequireExactlyOne(SceneType.RoomDirectory);
            RequireExactlyOne(SceneType.OnlineRoom);
            RequireExactlyOne(SceneType.Chat);
        }

        private void RequireExactlyOne(SceneType sceneType)
        {
            int count = this.scenesByType.TryGetValue(sceneType, out List<StartSceneConfig> sceneList) ? sceneList.Count : 0;
            if (count != 1)
            {
                throw ConfigError(this.Profile, "TbStartScene", sceneType.ToString(), $"OnlineLocal requires exactly one {sceneType} scene but found {count}.");
            }
        }

        private Exception Missing(string tableName, string key)
        {
            return new KeyNotFoundException($"Luban server config missing. Profile={this.Profile}, Table={tableName}, Key={key}.");
        }

        private static Exception ConfigError(string profile, string tableName, string key, string message)
        {
            return new Exception($"Luban server config invalid. Profile={profile}, Table={tableName}, Key={key}, {message}");
        }
    }
}
