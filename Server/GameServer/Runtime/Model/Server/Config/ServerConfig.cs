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

        /// <summary>新积分商店只引用正式空投，与旧现金商店门禁隔离。</summary>
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
