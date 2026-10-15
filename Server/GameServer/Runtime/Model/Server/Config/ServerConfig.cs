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
        private readonly Dictionary<int, WeaponConfig> weapons = new();
        private readonly Dictionary<int, Item> items = new();
        private readonly Dictionary<string, LootConfig> loots = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PlayerConfig> players = new(StringComparer.Ordinal);
        private readonly Dictionary<string, EnemyConfig> enemies = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TempBuildingConfig> temporaryBuildings = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TempBuildingManualAttackConfig> temporaryBuildingAttacks = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ProjectileConfig> projectiles = new(StringComparer.Ordinal);
        private readonly List<MapSpawnConfig> mainWorldEnemySpawns = new();
        private readonly Dictionary<string, MapSpawnConfig> onlineWorldPlayerSpawns = new(StringComparer.Ordinal);
        private readonly List<string> onlineWorldMapIds = new();
        private OnlineWorldRuleConfig onlineWorldRule;
        private ArcadeRuleConfig arcadeRule;
        private const string BaseMapId = "map.another-day.base";

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
        public MapSpawnConfig MainWorldPlayerSpawn { get; private set; }
        public IReadOnlyList<MapSpawnConfig> MainWorldEnemySpawns => this.mainWorldEnemySpawns;
        public IReadOnlyList<string> OnlineWorldMapIds => this.onlineWorldMapIds;
        public OnlineWorldRuleConfig OnlineWorldRule => this.onlineWorldRule;
        public ArcadeRuleConfig ArcadeRule => this.arcadeRule;

        public bool TryGetOnlineWorldPlayerSpawn(string mapId, out MapSpawnConfig playerSpawn)
        {
            return this.onlineWorldPlayerSpawns.TryGetValue(mapId ?? string.Empty, out playerSpawn);
        }

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

        public WeaponConfig GetWeapon(int itemId) => this.weapons.TryGetValue(itemId, out WeaponConfig value) ? value : throw this.Missing("TbWeapon", itemId.ToString());
        public Item GetItem(int itemId) => this.items.TryGetValue(itemId, out Item value) ? value : throw this.Missing("TbItem", itemId.ToString());
        public LootConfig GetLoot(string id) => this.loots.TryGetValue(id ?? string.Empty, out LootConfig value) ? value : throw this.Missing("TbLoot", id);
        public PlayerConfig GetPlayer(string id) => this.players.TryGetValue(id, out PlayerConfig value) ? value : throw this.Missing("TbPlayer", id);
        public EnemyConfig GetEnemy(string id) => this.enemies.TryGetValue(id, out EnemyConfig value) ? value : throw this.Missing("TbEnemy", id);
        public TempBuildingConfig GetTemporaryBuilding(string id) => this.temporaryBuildings.TryGetValue(id ?? string.Empty, out TempBuildingConfig value) ? value : throw this.Missing("TbTempBuilding", id);
        public bool TryGetTemporaryBuildingAttack(string id, out TempBuildingManualAttackConfig value) => this.temporaryBuildingAttacks.TryGetValue(id ?? string.Empty, out value);
        public ProjectileConfig GetProjectile(string id) => this.projectiles.TryGetValue(id ?? string.Empty, out ProjectileConfig value) ? value : throw this.Missing("TbProjectile", id);

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
            foreach (Item value in this.tables.TbItem.DataList)
            {
                if (!this.items.TryAdd(value.Id, value)) throw ConfigError(this.Profile, "TbItem", value.Id.ToString(), "duplicate Id.");
                if (value.MaxStackAmount <= 0) throw ConfigError(this.Profile, "TbItem", value.Id.ToString(), "MaxStackAmount must be positive.");
            }
            foreach (LootConfig value in this.tables.TbLoot.DataList)
            {
                if (!this.loots.TryAdd(value.Id, value)) throw ConfigError(this.Profile, "TbLoot", value.Id, "duplicate Id.");
                if (!this.items.ContainsKey(value.ItemId)) throw ConfigError(this.Profile, "TbLoot", value.Id, $"ItemId references missing TbItem '{value.ItemId}'.");
                if (value.Amount <= 0) throw ConfigError(this.Profile, "TbLoot", value.Id, "Amount must be positive.");
            }
            if (this.tables.TbOnlineWorldRule.DataList.Count != 1)
                throw ConfigError(this.Profile, "TbOnlineWorldRule", "default", $"expected exactly one row but found {this.tables.TbOnlineWorldRule.DataList.Count}.");
            this.onlineWorldRule = this.tables.TbOnlineWorldRule.DataList[0];
            if (!string.Equals(this.onlineWorldRule.Id, "default", StringComparison.Ordinal) || this.onlineWorldRule.BagCapacity <= 0 ||
                this.onlineWorldRule.CorpseLifetimeSeconds <= 0)
                throw ConfigError(this.Profile, "TbOnlineWorldRule", this.onlineWorldRule.Id, "Id, BagCapacity and CorpseLifetimeSeconds are invalid.");
            Item starter = this.GetItem(this.onlineWorldRule.StarterMeleeWeaponItemId);
            WeaponConfig starterWeapon = this.GetWeaponAfterIndex(this.onlineWorldRule.StarterMeleeWeaponItemId);
            if (starter.ItemType != ItemType.Weapon || starterWeapon.WeaponKind != WeaponKind.Melee)
                throw ConfigError(this.Profile, "TbOnlineWorldRule", this.onlineWorldRule.Id, $"StarterMeleeWeaponItemId '{starter.Id}' must reference a melee weapon.");
            Item starterRanged = this.GetItem(this.onlineWorldRule.StarterRangedWeaponItemId);
            WeaponConfig starterRangedWeapon = this.GetWeaponAfterIndex(this.onlineWorldRule.StarterRangedWeaponItemId);
            if (starterRanged.ItemType != ItemType.Weapon || starterRangedWeapon.WeaponKind != WeaponKind.Projectile ||
                starterRangedWeapon.AmmoItemId <= 0 || this.onlineWorldRule.StarterRangedAmmoAmount <= 0)
                throw ConfigError(this.Profile, "TbOnlineWorldRule", this.onlineWorldRule.Id,
                    $"StarterRangedWeaponItemId '{starterRanged.Id}' and StarterRangedAmmoAmount must define a projectile starter weapon and positive ammo amount.");
            Item starterAmmo = this.GetItem(starterRangedWeapon.AmmoItemId);
            if (starterAmmo.ItemType != ItemType.Ammunition)
                throw ConfigError(this.Profile, "TbOnlineWorldRule", this.onlineWorldRule.Id,
                    $"Starter ranged weapon ammo '{starterAmmo.Id}' must reference an ammunition item.");

            this.ValidateArcadeConfig();

            foreach (WeaponConfig value in this.tables.TbWeapon.DataList)
            {
                if (!this.weapons.TryAdd(value.ItemId, value)) throw ConfigError(this.Profile, "TbWeapon", value.ItemId.ToString(), "duplicate ItemId.");
            }
            foreach (PlayerConfig value in this.tables.TbPlayer.DataList)
            {
                if (!Finite(value.BuildingOperateDistance) || value.BuildingOperateDistance <= 0f)
                    throw ConfigError(this.Profile, "TbPlayer", value.Id, "BuildingOperateDistance must be finite and positive.");
                if (!this.players.TryAdd(value.Id, value)) throw ConfigError(this.Profile, "TbPlayer", value.Id, "duplicate Id.");
            }
            foreach (EnemyConfig value in this.tables.TbEnemy.DataList)
            {
                if (!this.enemies.TryAdd(value.Id, value)) throw ConfigError(this.Profile, "TbEnemy", value.Id, "duplicate Id.");
            }
            foreach (ProjectileConfig value in this.tables.TbProjectile.DataList)
            {
                if (string.IsNullOrWhiteSpace(value.Id) || !Finite(value.MoveSpeed, value.MaxTravelDistance) || value.MoveSpeed <= 0 || value.MaxTravelDistance <= 0 ||
                    !this.projectiles.TryAdd(value.Id, value))
                    throw ConfigError(this.Profile, "TbProjectile", value.Id, "invalid or duplicate projectile.");
            }
            foreach (TempBuildingConfig value in this.tables.TbTempBuilding.DataList)
            {
                if (string.IsNullOrWhiteSpace(value.Id) || !Finite(value.BuildSeconds, value.FootprintRadius, value.MaxSlopeDegrees, value.NarrowPassageRadius) ||
                    value.BuildSeconds < 0 || value.FootprintRadius <= 0 ||
                    value.Materials == null || value.Materials.Length == 0 || Array.Exists(value.Materials, x => x.ItemId <= 0 || x.Amount <= 0 || !this.items.ContainsKey(x.ItemId)) ||
                    !this.temporaryBuildings.TryAdd(value.Id, value))
                    throw ConfigError(this.Profile, "TbTempBuilding", value.Id, "invalid or duplicate temporary building.");
            }
            foreach (TempBuildingManualAttackConfig value in this.tables.TbTempBuildingManualAttack.DataList)
            {
                if (string.IsNullOrWhiteSpace(value.BuildingId) || !this.temporaryBuildings.ContainsKey(value.BuildingId) ||
                    !Finite(value.InteractDistance, value.ShootCooldownSeconds, value.ReloadSeconds, value.OperateRotateDegreesPerSecond) ||
                    value.InteractDistance <= 0 || value.ShootCooldownSeconds <= 0 || value.Damage <= 0 || value.AmmoItemId <= 0 || value.AmmoAmount <= 0 ||
                    value.MagazineCapacity <= 0 || value.ReloadSeconds < 0 || !this.items.ContainsKey(value.AmmoItemId) || !this.projectiles.ContainsKey(value.ProjectileConfigId) ||
                    !this.temporaryBuildingAttacks.TryAdd(value.BuildingId, value))
                    throw ConfigError(this.Profile, "TbTempBuildingManualAttack", value.BuildingId, "invalid or duplicate manual attack.");
            }

            foreach (MapSpawnConfig spawn in this.tables.TbMapSpawn.DataList)
            {
                if (spawn.ObjectType == MapSpawnObjectType.Player)
                {
                    if (string.IsNullOrWhiteSpace(spawn.MapId) || string.Equals(spawn.MapId, BaseMapId, StringComparison.Ordinal)) continue;
                    if (!this.players.ContainsKey(spawn.ConfigId))
                        throw ConfigError(this.Profile, "TbMapSpawn", spawn.Id, $"MapId '{spawn.MapId}' player spawn references missing TbPlayer '{spawn.ConfigId}'.");
                    if (!Finite(spawn.PositionX, spawn.PositionY, spawn.PositionZ, spawn.RotationY))
                        throw ConfigError(this.Profile, "TbMapSpawn", spawn.Id, $"MapId '{spawn.MapId}' player spawn pose is not finite.");
                    if (!this.onlineWorldPlayerSpawns.TryAdd(spawn.MapId, spawn))
                        throw ConfigError(this.Profile, "TbMapSpawn", spawn.Id, $"MapId '{spawn.MapId}' has duplicate player spawn.");
                }
                else if (spawn.MapId == Server.OnlineWorldComponent.MainMapId && spawn.ObjectType == MapSpawnObjectType.Enemy)
                {
                    EnemyConfig enemy = this.GetEnemy(spawn.ConfigId);
                    if (enemy.AttackKind == EnemyAttackKind.Melee) this.mainWorldEnemySpawns.Add(spawn);
                }
            }
            this.onlineWorldMapIds.AddRange(this.onlineWorldPlayerSpawns.Keys);
            this.onlineWorldMapIds.Sort(StringComparer.Ordinal);
            this.onlineWorldPlayerSpawns.TryGetValue(Server.OnlineWorldComponent.MainMapId, out MapSpawnConfig mainSpawn);
            this.MainWorldPlayerSpawn = mainSpawn;
            foreach (MapSpawnConfig spawn in this.tables.TbMapSpawn.DataList)
            {
                if (spawn.ObjectType != MapSpawnObjectType.Enemy || string.Equals(spawn.MapId, BaseMapId, StringComparison.Ordinal)) continue;
                EnemyConfig enemy = this.GetEnemy(spawn.ConfigId);
                this.ValidateLootReference(enemy.LootConfigId, enemy.Id, nameof(enemy.LootConfigId));
                this.ValidateLootReference(enemy.GuaranteedWeaponLootConfigId, enemy.Id, nameof(enemy.GuaranteedWeaponLootConfigId));
                this.ValidateLootReference(enemy.GuaranteedAmmoLootConfigId, enemy.Id, nameof(enemy.GuaranteedAmmoLootConfigId));
            }
            this.ValidateMainWorldCombat();

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
            foreach (var row in this.tables.TbArcadeTreasureRoom.DataList)
            {
                var room = AnotherDay.Arcade.ArcadeTreasureRoomParameters.FromConfig(row);
                var target = this.tables.TbArcadeArena.GetOrDefault(room.ArenaConfigNumber);
                var arenaRule = this.tables.TbArcadeRule.DataList.FirstOrDefault();
                if (target == null || room.WaveNumber > target.WaveGroupIds.Length || arenaRule == null ||
                    !room.FitsArena(arenaRule.ArenaHalfWidth, arenaRule.ArenaHalfHeight, arenaRule.PlayerRadius))
                    throw ConfigError(this.Profile, "TbArcadeTreasureRoom", room.Id, "invalid arena or wave");
            }
            if (this.tables.TbArcadeRule.DataList.Count != 1)
                throw ConfigError(this.Profile, "TbArcadeRule", "arcade.default", "expected exactly one row.");
            this.arcadeRule = this.tables.TbArcadeRule.DataList[0];
            if (!string.Equals(this.arcadeRule.Id, "arcade.default", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(this.arcadeRule.ConfigFingerprint) || this.arcadeRule.ConfigFingerprint.Length > 128 ||
                this.arcadeRule.InitialLives <= 0 || this.arcadeRule.InitialBombs < 0 || this.arcadeRule.InitialDashes < 0 ||
                this.arcadeRule.ShopOfferCount <= 0 || string.IsNullOrWhiteSpace(this.arcadeRule.BaseWeaponId) ||
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
                    drop.Weight <= 0 || string.IsNullOrWhiteSpace(drop.ProductId) || drop.Amount <= 0 ||
                    !Finite(drop.DurationSeconds, 1f, 1f, 1f) || drop.DurationSeconds < 0f ||
                    timed != (drop.DurationSeconds > 0f) ||
                    drop.Kind == ArcadeDropKind.TemporaryWeapon && !weaponIds.Contains(drop.ProductId) ||
                    drop.Kind == ArcadeDropKind.Powerup && !powerupIds.Contains(drop.ProductId))
                    throw ConfigError(this.Profile, "TbArcadeDrop", drop?.Id ?? "<null>",
                        "invalid drop values or product reference.");
            }

            HashSet<string> shopIds = new(StringComparer.Ordinal);
            foreach (ArcadeShopItemConfig item in this.tables.TbArcadeShopItem.DataList)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || !shopIds.Add(item.Id) || string.IsNullOrWhiteSpace(item.ProductId) ||
                    item.UnlockArena <= 0 || item.Weight <= 0 || item.BasePrice < 0 || item.PricePerArena < 0 ||
                    item.Amount <= 0 || item.MaxPurchases <= 0)
                    throw ConfigError(this.Profile, "TbArcadeShopItem", item.Id, "invalid or duplicate shop item.");
            }
            if (shopIds.Count < this.arcadeRule.ShopOfferCount)
                throw ConfigError(this.Profile, "TbArcadeShopItem", "count", "not enough shop items for one offer set.");

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
                var opening = AnotherDay.Arcade.ArcadeOpeningParameters.FromConfig(arena);
                var content = AnotherDay.Arcade.ArcadeBabylonParameters.FromConfig(arena);
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
                    string hazardError = AnotherDay.Arcade.ArcadeHazardRules.Validate(parameters,
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
        public static AnotherDay.Arcade.ArcadeHazardParameters GetHazardParameters(ArcadeHazardConfig row) =>
            new(row.Id, (int)row.Kind, row.CenterX, row.CenterY, row.DirectionDegrees, row.Radius,
                row.ArmLength, row.ArmHalfWidth, row.IdleSeconds, row.WarningSeconds, row.ActiveSeconds,
                row.AngularSpeedDegrees, row.HitIntervalSeconds, row.PlayerDamageRatio, row.BossDamageRatio);

        private WeaponConfig GetWeaponAfterIndex(int itemId)
        {
            WeaponConfig value = this.tables.TbWeapon.DataList.Find(x => x.ItemId == itemId);
            return value ?? throw ConfigError(this.Profile, "TbOnlineWorldRule", "default", $"StarterMeleeWeaponItemId references missing TbWeapon '{itemId}'.");
        }

        private void ValidateLootReference(string lootId, string enemyId, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(lootId)) return;
            if (!this.loots.TryGetValue(lootId, out LootConfig loot))
                throw ConfigError(this.Profile, "TbEnemy", enemyId, $"{fieldName} references missing TbLoot '{lootId}'.");
            if (loot.Amount <= 0 || !this.items.TryGetValue(loot.ItemId, out Item item) || item.MaxStackAmount <= 0)
                throw ConfigError(this.Profile, "TbEnemy", enemyId, $"{fieldName} references invalid TbLoot '{lootId}'.");
        }

        private void ValidateMainWorldCombat()
        {
            MapSpawnConfig playerSpawn = this.MainWorldPlayerSpawn ?? throw ConfigError(this.Profile, "TbMapSpawn", Server.OnlineWorldComponent.MainMapId, "main World requires exactly one player spawn.");
            if (!string.Equals(playerSpawn.ConfigId, Server.OnlineWorldComponent.MainPlayerConfigId, StringComparison.Ordinal))
                throw ConfigError(this.Profile, "TbMapSpawn", playerSpawn.Id, $"main World player spawn must reference '{Server.OnlineWorldComponent.MainPlayerConfigId}', actual '{playerSpawn.ConfigId}'.");
            PlayerConfig player = this.GetPlayer(playerSpawn.ConfigId);
            if (!Finite(playerSpawn.PositionX, playerSpawn.PositionY, playerSpawn.PositionZ, playerSpawn.RotationY)) throw ConfigError(this.Profile, "TbMapSpawn", playerSpawn.Id, "player spawn pose is not finite.");
            if (player.MaxHP <= 0 || player.AttackDamage <= 0 || player.UnarmedAttackDamage <= 0 ||
                !Finite(player.AttackRange, player.AttackCooldownSeconds, player.MoveSpeed, player.TurnSpeed,
                    player.BaseRecoveryPerSecond, player.UnarmedAttackRange, player.UnarmedAttackCooldownSeconds,
                    player.UnarmedMeleeArcDegrees) ||
                player.AttackRange <= 0 || player.AttackCooldownSeconds <= 0 || player.MoveSpeed <= 0 ||
                player.TurnSpeed <= 0 || player.BaseRecoveryPerSecond < 0 || player.UnarmedAttackRange <= 0 ||
                player.UnarmedAttackCooldownSeconds <= 0 || player.UnarmedMeleeArcDegrees <= 0 || player.UnarmedMeleeArcDegrees > 360)
                throw ConfigError(this.Profile, "TbPlayer", player.Id, "invalid health or unarmed combat values.");
            WeaponConfig sword = this.GetWeapon(5);
            if (sword.WeaponKind != WeaponKind.Melee || sword.AttackDamage <= 0 ||
                !Finite(sword.AttackRange, sword.AttackCooldownSeconds, sword.MeleeArcDegrees) ||
                sword.AttackRange <= 0 || sword.AttackCooldownSeconds <= 0 || sword.MeleeArcDegrees <= 0 || sword.MeleeArcDegrees > 360)
                throw ConfigError(this.Profile, "TbWeapon", "5", "short sword must contain positive melee values.");
            if (this.mainWorldEnemySpawns.Count != 4) throw ConfigError(this.Profile, "TbMapSpawn", Server.OnlineWorldComponent.MainMapId, $"expected four melee enemy spawns but found {this.mainWorldEnemySpawns.Count}.");
            foreach (MapSpawnConfig spawn in this.mainWorldEnemySpawns)
            {
                EnemyConfig enemy = this.GetEnemy(spawn.ConfigId);
                if (!Finite(spawn.PositionX, spawn.PositionY, spawn.PositionZ, spawn.RotationY,
                        enemy.AggroRange, enemy.AttackRange, enemy.AttackCooldownSeconds, enemy.MoveSpeed) ||
                    enemy.MaxHP <= 0 || enemy.AttackDamage <= 0 || enemy.AggroRange <= 0 ||
                    enemy.AttackRange <= 0 || enemy.AttackCooldownSeconds <= 0 || enemy.MoveSpeed <= 0)
                    throw ConfigError(this.Profile, "TbEnemy", enemy.Id, $"invalid melee values referenced by spawn '{spawn.Id}'.");
            }
        }

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
            RequireExactlyOne(SceneType.OnlineWorld);
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
