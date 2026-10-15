using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Game
{
    public enum SaveFileStatus { Missing, Valid, Corrupted, Unsupported }
    public enum OfflineSaveDomain { None, Base, Expedition }

    public sealed class SaveReadResult<T>
    {
        public SaveFileStatus Status { get; set; }
        public T Data { get; set; }
        public string Error { get; set; }
        public bool FromBackup { get; set; }
        public bool IsValid => Status == SaveFileStatus.Valid && Data != null;
    }

    public static class SaveDataSections
    {
        public static Dictionary<string, JToken> Clone(Dictionary<string, JToken> sections)
        {
            var result = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var pair in sections ?? new Dictionary<string, JToken>(StringComparer.Ordinal))
            {
                result[pair.Key] = pair.Value?.DeepClone();
            }
            return result;
        }
    }

    public sealed class BaseSaveData
    {
        public const int CurrentVersion = 5;
        public int Version { get; set; } = CurrentVersion;
        public string UpdatedAtUtc { get; set; }
        public Dictionary<string, JToken> Sections { get; set; } = new Dictionary<string, JToken>(StringComparer.Ordinal);
        public HashSet<string> SettledExpeditionIds { get; set; } = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>基地持久世界只包含地面掉落和临时建筑，绝不复用完整副本世界。</summary>
        public BaseWorldSaveData World { get; set; } = new BaseWorldSaveData();

        public BaseSaveData Clone() => new BaseSaveData
        {
            Version = Version,
            UpdatedAtUtc = UpdatedAtUtc,
            Sections = SaveDataSections.Clone(Sections),
            SettledExpeditionIds = new HashSet<string>(SettledExpeditionIds ?? new HashSet<string>(), StringComparer.Ordinal),
            World = World?.Clone()
        };
    }

    /// <summary>副本地图局部姿态；运行时 MapInstanceId 不持久化。</summary>
    public sealed class ExpeditionPoseData
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float RotationY { get; set; }
        public ExpeditionPoseData Clone() => new ExpeditionPoseData { X = X, Y = Y, Z = Z, RotationY = RotationY };
    }

    /// <summary>副本玩家的稳定实体记录。</summary>
    public sealed class ExpeditionPlayerSaveData
    {
        public int RuntimeId { get; set; }
        public string ConfigId { get; set; }
        public ExpeditionPoseData Pose { get; set; } = new ExpeditionPoseData();
        /// <summary>水陆迟滞状态；旧档缺失为步行，原位置不受默认值影响。</summary>
        public bool IsSwimming { get; set; }
        public ExpeditionPlayerSaveData Clone() => new ExpeditionPlayerSaveData { RuntimeId = RuntimeId, ConfigId = ConfigId, Pose = Pose?.Clone(), IsSwimming = IsSwimming };
    }

    /// <summary>副本敌人的稳定实体、生命和尸体记录。</summary>
    public sealed class ExpeditionEnemySaveData
    {
        public int RuntimeId { get; set; }
        public string ConfigId { get; set; }
        public ExpeditionPoseData Pose { get; set; } = new ExpeditionPoseData();
        public float CurrentHealth { get; set; }
        public float MaxHealth { get; set; }
        public bool IsCorpse { get; set; }
        public ExpeditionEnemySaveData Clone() => new ExpeditionEnemySaveData { RuntimeId = RuntimeId, ConfigId = ConfigId, Pose = Pose?.Clone(), CurrentHealth = CurrentHealth, MaxHealth = MaxHealth, IsCorpse = IsCorpse };
    }

    /// <summary>副本掉落的稳定实体和剩余数量记录。</summary>
    public sealed class ExpeditionLootSaveData
    {
        public int RuntimeId { get; set; }
        public string ConfigId { get; set; }
        public ExpeditionPoseData Pose { get; set; } = new ExpeditionPoseData();
        public int RemainingAmount { get; set; }
        public bool Collected { get; set; }
        public float? TorchRemainingSeconds { get; set; }
        public bool OriginKnown {get;set;}
        public string OriginExpeditionId {get;set;}
        public int AcquiredAmount {get;set;}
        public ExpeditionLootSaveData Clone() => new ExpeditionLootSaveData { RuntimeId = RuntimeId, ConfigId = ConfigId, Pose = Pose?.Clone(), RemainingAmount = RemainingAmount, Collected = Collected, TorchRemainingSeconds = TorchRemainingSeconds,OriginKnown=OriginKnown,OriginExpeditionId=OriginExpeditionId,AcquiredAmount=AcquiredAmount };
    }

    /// <summary>副本搜索容器及其尸体来源记录。</summary>
    public sealed class ExpeditionSearchContainerSaveData
    {
        public int RuntimeId { get; set; }
        public int SourceEnemyRuntimeId { get; set; }
        public ExpeditionPoseData Pose { get; set; } = new ExpeditionPoseData();
        public int Capacity { get; set; }
        public List<ItemSlotSaveData> Slots { get; set; } = new List<ItemSlotSaveData>();
        public ExpeditionSearchContainerSaveData Clone() => new ExpeditionSearchContainerSaveData { RuntimeId = RuntimeId, SourceEnemyRuntimeId = SourceEnemyRuntimeId, Pose = Pose?.Clone(), Capacity = Capacity, Slots = Slots?.Select(slot => slot == null ? null : new ItemSlotSaveData { Index = slot.Index, ItemId = slot.ItemId, Amount = slot.Amount, LoadedAmmo=slot.LoadedAmmo, TorchRemainingSeconds=slot.TorchRemainingSeconds, OriginExpeditionId=slot.OriginExpeditionId, AcquiredAmount=slot.AcquiredAmount, LoadedAcquiredAmount=slot.LoadedAcquiredAmount }).ToList() ?? new List<ItemSlotSaveData>() };
    }

    /// <summary>副本临时建筑的稳定实体、弹药和归一化状态记录。</summary>
    public sealed class ExpeditionTemporaryBuildingSaveData
    {
        public int RuntimeId { get; set; }
        public string ConfigId { get; set; }
        public ExpeditionPoseData Pose { get; set; } = new ExpeditionPoseData();
        public int LoadedAmmo { get; set; }
        /// <summary>模型统一比例，旧记录默认为 1；不表示玩家可调整尺寸。</summary>
        public float UniformScale { get; set; } = 1f;
        public ExpeditionTemporaryBuildingSaveData Clone() => new ExpeditionTemporaryBuildingSaveData { RuntimeId = RuntimeId, ConfigId = ConfigId, Pose = Pose?.Clone(), LoadedAmmo = LoadedAmmo, UniformScale = UniformScale };
    }

    /// <summary>只存放类型化的副本世界记录，运行时资源和 MapInstanceId 不会作为恢复身份使用。</summary>
    public sealed class ExpeditionWorldSaveData
    {
        public List<ExpeditionPlayerSaveData> Players { get; set; } = new List<ExpeditionPlayerSaveData>();
        public List<ExpeditionEnemySaveData> Enemies { get; set; } = new List<ExpeditionEnemySaveData>();
        public List<ExpeditionLootSaveData> Loot { get; set; } = new List<ExpeditionLootSaveData>();
        public List<ExpeditionSearchContainerSaveData> SearchContainers { get; set; } = new List<ExpeditionSearchContainerSaveData>();
        public List<ExpeditionTemporaryBuildingSaveData> TemporaryBuildings { get; set; } = new List<ExpeditionTemporaryBuildingSaveData>();

        public ExpeditionWorldSaveData Clone() => new ExpeditionWorldSaveData
        {
            Players = Players?.Select(x => x?.Clone()).ToList() ?? new List<ExpeditionPlayerSaveData>(),
            Enemies = Enemies?.Select(x => x?.Clone()).ToList() ?? new List<ExpeditionEnemySaveData>(),
            Loot = Loot?.Select(x => x?.Clone()).ToList() ?? new List<ExpeditionLootSaveData>(),
            SearchContainers = SearchContainers?.Select(x => x?.Clone()).ToList() ?? new List<ExpeditionSearchContainerSaveData>(),
            TemporaryBuildings = TemporaryBuildings?.Select(x => x?.Clone()).ToList() ?? new List<ExpeditionTemporaryBuildingSaveData>()
        };
    }

    /// <summary>基地持久世界；只保存可由基地地图恢复的掉落与临时建筑。</summary>
    public sealed class BaseWorldSaveData
    {
        /// <summary>初始摆件已处理；即使全部拆除也不能重新赠送。</summary>
        public bool CampLayoutInitialized { get; set; }
        public List<ExpeditionLootSaveData> Loot { get; set; } = new List<ExpeditionLootSaveData>();
        public List<ExpeditionTemporaryBuildingSaveData> TemporaryBuildings { get; set; } = new List<ExpeditionTemporaryBuildingSaveData>();

        public BaseWorldSaveData Clone() => new BaseWorldSaveData
        {
            CampLayoutInitialized = CampLayoutInitialized,
            Loot = Loot?.Select(x => x?.Clone()).ToList() ?? new List<ExpeditionLootSaveData>(),
            TemporaryBuildings = TemporaryBuildings?.Select(x => x?.Clone()).ToList() ?? new List<ExpeditionTemporaryBuildingSaveData>()
        };
    }

    public sealed class ExpeditionSaveData
    {
        public const int CurrentVersion = 4;
        public int Version { get; set; } = CurrentVersion;
        public int TaskSchemaVersion { get; set; } = 1;
        public int LegacySourceVersion { get; set; }
        public string ExpeditionId { get; set; }
        public string MapId { get; set; }
        public string UpdatedAtUtc { get; set; }
        public float ElapsedGameMinutes { get; set; }
        /// <summary>缺失字段按旧午夜起点读取；1 表示保存了独立起始钟表分钟。</summary>
        public int ClockSchemaVersion { get; set; }
        public float StartMinuteOfDay { get; set; }
        /// <summary>null 保留旧固定河谷；非空清单提供当局完整地形和稳定点身份。</summary>
        public ValleyLayoutSaveData ValleyLayout { get; set; }
        public float TargetGameMinutes { get; set; }
        public int TaskTargetKills { get; set; }
        public int TaskKillCount { get; set; }
        public ExpeditionStatus Status { get; set; } = ExpeditionStatus.Playing;
        public Dictionary<string, JToken> Sections { get; set; } = new Dictionary<string, JToken>(StringComparer.Ordinal);
        public ExpeditionWorldSaveData World { get; set; } = new ExpeditionWorldSaveData();

        public ExpeditionSaveData Clone() => CloneCore(true);
        /// <summary>普通保存只捕获可变状态；完整布局继续由已发布的初始快照拥有。</summary>
        internal ExpeditionSaveData CloneMutable() => CloneCore(false);
        private ExpeditionSaveData CloneCore(bool includeLayout) => new ExpeditionSaveData
        {
            Version = Version,
            TaskSchemaVersion = TaskSchemaVersion,
            LegacySourceVersion = LegacySourceVersion,
            ExpeditionId = ExpeditionId,
            MapId = MapId,
            UpdatedAtUtc = UpdatedAtUtc,
            ElapsedGameMinutes = ElapsedGameMinutes,
            ClockSchemaVersion = ClockSchemaVersion,
            StartMinuteOfDay = StartMinuteOfDay,
            ValleyLayout = includeLayout ? ValleyLayout?.Clone() : null,
            TargetGameMinutes = TargetGameMinutes,
            TaskTargetKills = TaskTargetKills,
            TaskKillCount = TaskKillCount,
            Status = Status,
            Sections = SaveDataSections.Clone(Sections),
            World = World?.Clone()
        };
    }
}
