using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 负责基地/远征增量存储的领域校验与读写入口；文件协议归独立存储适配器，旧文件不迁移。
    /// </summary>
    public sealed class SaveFileStore
    {
        private readonly string rootDirectory;
        private IncrementalSaveStore incremental;
        private IncrementalSaveStore Incremental => incremental ??= new IncrementalSaveStore(Path.Combine(SavesDirectory, "incremental-v1"));
        public SaveFileStore() : this(Application.persistentDataPath)
        {
        }

        public SaveFileStore(string rootDirectory)
        {
            this.rootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? Application.persistentDataPath : rootDirectory;
        }

        public string SavesDirectory => Path.Combine(rootDirectory, "Saves");
        public string BasePath => Incremental.PathFor("base");
        public string BaseTempPath => BasePath + ".tmp";
        public string BaseBackupPath => BasePath + ".backup";
        public string ExpeditionPath => Incremental.PathFor("offline-expedition");
        public string ExpeditionTempPath => ExpeditionPath + ".tmp";
        public string ExpeditionBackupPath => ExpeditionPath + ".backup";

        public SaveReadResult<BaseSaveData> InspectBase()
        {
            var read = Incremental.Read<BaseSaveData>("base", ValidateDomain);
            if (read.Status == SaveFileStatus.Missing && File.Exists(Path.Combine(SavesDirectory, "base.json")))
            { read.Status = SaveFileStatus.Unsupported; read.Error = "测试存档格式已更新，请新建游戏"; }
            return read;
        }
        public SaveReadResult<ExpeditionSaveData> InspectExpedition()
        {
            var read = Incremental.Read<ExpeditionSaveData>("offline-expedition", ValidateDomain);
            if (read.Status == SaveFileStatus.Missing && !File.Exists(BasePath) && File.Exists(Path.Combine(SavesDirectory, "offline-expedition.json")))
            { read.Status = SaveFileStatus.Unsupported; read.Error = "测试存档格式已更新，请新建游戏"; }
            return read;
        }
        public bool SaveBase(BaseSaveData data, out string error) => Incremental.Save("base", data, ValidateDomain, true, out error);
        public bool SaveExpedition(ExpeditionSaveData data, out string error) => Incremental.Save("offline-expedition", data, ValidateDomain, true, out error);
        internal bool CreateBase(BaseSaveData data, out string error) => Incremental.CreateBase(data, ValidateDomain, out error);
        internal bool SaveBaseMutable(BaseSaveData data, out string error) => Incremental.Save("base", data, ValidateDomain, false, out error);
        internal bool SaveExpeditionMutable(ExpeditionSaveData data, out string error) => Incremental.Save("offline-expedition", data, ValidateDomain, false, out error);
        public bool DeleteExpeditionFiles(out string error)
        {
            error = null;
            try { Incremental.DeleteExpedition(); return true; }
            catch (Exception e) { error = e.Message; return false; }
        }

        /// <summary>
        /// 保留原准备入口的幂等契约；新格式不删除旧测试存档。
        /// </summary>
        public bool CleanupLegacyFiles(out string error)
        {
            // 测试阶段只支持新格式；旧文件保留，不迁移、不清理。
            error = null;
            return true;
        }

        private static void ValidateDomain<T>(T data)
        {
            if (data is BaseSaveData baseData)
            {
                if (baseData.Version != BaseSaveData.CurrentVersion && baseData.Version != 4) throw new InvalidDataException("基地领域数据版本不受支持");
                if(baseData.Sections==null)throw new InvalidDataException("基地分区缺失");
                if(baseData.Version==5&&(!baseData.Sections.TryGetValue(BasePreparationModel.Section,out var preparation)||preparation==null||!new BasePreparationModel().Validate(preparation,out _)||!baseData.Sections.TryGetValue(ShopUnlockModel.Section,out var unlocks)||unlocks==null||!new ShopUnlockModel().Validate(unlocks,out _)))throw new InvalidDataException("新版本基地准备分区缺失或无效");
                if (baseData.Sections == null || baseData.SettledExpeditionIds == null || baseData.World == null ||
                    baseData.World.Loot == null || baseData.World.TemporaryBuildings == null) throw new InvalidDataException("基地存档必需字段缺失");
                if (baseData.SettledExpeditionIds.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("基地结算 ID 无效");
                ValidateWorldRecords(baseData.World.Loot, item => item?.RuntimeId ?? 0, item => item?.ConfigId, item => item?.Pose, "基地掉落");
                ValidateWorldRecords(baseData.World.TemporaryBuildings, item => item?.RuntimeId ?? 0, item => item?.ConfigId, item => item?.Pose, "基地临时建筑");
                foreach (var loot in baseData.World.Loot)
                {
                    if (loot.RemainingAmount < 0 || !loot.Collected && loot.RemainingAmount <= 0) throw new InvalidDataException("基地掉落数量无效");
                }
                foreach (var building in baseData.World.TemporaryBuildings)
                {
                    if (building.LoadedAmmo < 0 || building.UniformScale <= 0 || float.IsNaN(building.UniformScale) || float.IsInfinity(building.UniformScale)) throw new InvalidDataException("基地临时建筑弹药无效");
                }
                return;
            }
            if (data is ExpeditionSaveData expedition)
            {
                if (expedition.Version != ExpeditionSaveData.CurrentVersion && expedition.Version != 3) throw new InvalidDataException("远征领域数据版本不受支持");
                if(expedition.Sections==null)throw new InvalidDataException("远征分区缺失");
                if(expedition.Version==4)
                {
                    if(!expedition.Sections.TryGetValue(ExpeditionPreparationModel.Section,out var preparation)||preparation==null)throw new InvalidDataException("新版本远征准备账本缺失");
                    var ledger=new ExpeditionPreparationModel();if(!ledger.Validate(preparation,out _)||preparation["expeditionId"].Value<string>()!=expedition.ExpeditionId||preparation["elapsedAnchor"].Value<double>()>expedition.ElapsedGameMinutes)throw new InvalidDataException("远征准备账本身份或耗时无效");
                }
                if (!ExpeditionClockRules.TryValidate(expedition.ClockSchemaVersion, expedition.StartMinuteOfDay, out var clockError))
                    throw new InvalidDataException(clockError);
                if (expedition.ValleyLayout != null && (expedition.MapId != expedition.ValleyLayout.MapId ||
                    !ValleyLayoutValidator.TryValidateData(expedition.ValleyLayout, out _)))
                    throw new InvalidDataException("副本河谷清单无效或身份不匹配");
                var modernTasks = expedition.TaskSchemaVersion == 1 && expedition.LegacySourceVersion == 0;
                var legacyTasks = expedition.TaskSchemaVersion == 0 && (expedition.LegacySourceVersion == 1 || expedition.LegacySourceVersion == 2);
                if (!IsSafeId(expedition.ExpeditionId) || string.IsNullOrWhiteSpace(expedition.MapId) || expedition.Sections == null || expedition.World == null ||
                    expedition.World.Players == null || expedition.World.Enemies == null || expedition.World.Loot == null ||
                    expedition.World.SearchContainers == null || expedition.World.TemporaryBuildings == null ||
                    float.IsNaN(expedition.ElapsedGameMinutes) || float.IsInfinity(expedition.ElapsedGameMinutes) || expedition.ElapsedGameMinutes < 0f ||
                    float.IsNaN(expedition.TargetGameMinutes) || float.IsInfinity(expedition.TargetGameMinutes) || expedition.TargetGameMinutes < 0f ||
                    expedition.TaskTargetKills < 0 || expedition.TaskKillCount < 0 || expedition.TaskKillCount > expedition.TaskTargetKills)
                    throw new InvalidDataException("副本存档必需字段无效");
                if (!modernTasks && !legacyTasks) throw new InvalidDataException("副本任务版本标记无效");
                if(expedition.ValleyLayout!=null)
                {
                    expedition.Sections.TryGetValue(CivilisationExpeditionModel.Section,out var exploration);
                    if(!ValleyLayoutValidator.TryValidateExploration(expedition.ValleyLayout,exploration,out var explorationError))throw new InvalidDataException(explorationError);
                }
                if (modernTasks && !expedition.Sections.ContainsKey(ExpeditionTaskModel.SaveSectionId)) throw new InvalidDataException("副本任务分区缺失");
                if (legacyTasks && expedition.Sections.ContainsKey(ExpeditionTaskModel.SaveSectionId)) throw new InvalidDataException("旧副本不能包含任务分区");
                if (modernTasks && !new ExpeditionTaskModel().Validate(expedition.Sections[ExpeditionTaskModel.SaveSectionId], out var taskError)) throw new InvalidDataException(taskError);
                if (modernTasks && (expedition.Sections[ExpeditionTaskModel.SaveSectionId]?["mapId"]?.Value<string>() != expedition.MapId || expedition.Sections[ExpeditionTaskModel.SaveSectionId]?["expeditionId"]?.Value<string>() != expedition.ExpeditionId)) throw new InvalidDataException("副本任务分区身份冲突");
                ValidateWorldRecords(expedition.World.Players, item => item?.RuntimeId ?? 0, item => item?.ConfigId, item => item?.Pose, "玩家");
                ValidateWorldRecords(expedition.World.Enemies, item => item?.RuntimeId ?? 0, item => item?.ConfigId, item => item?.Pose, "敌人");
                ValidateWorldRecords(expedition.World.Loot, item => item?.RuntimeId ?? 0, item => item?.ConfigId, item => item?.Pose, "掉落");
                ValidateWorldRecords(expedition.World.TemporaryBuildings, item => item?.RuntimeId ?? 0, item => item?.ConfigId, item => item?.Pose, "临时建筑");
                foreach (var enemy in expedition.World.Enemies)
                {
                    if (!IsFinite(enemy.CurrentHealth) || !IsFinite(enemy.MaxHealth) || enemy.MaxHealth <= 0f || enemy.CurrentHealth < 0f || enemy.CurrentHealth > enemy.MaxHealth)
                        throw new InvalidDataException("副本敌人生命快照无效");
                }
                foreach (var loot in expedition.World.Loot)
                {
                    if (loot.RemainingAmount < 0 || !loot.Collected && loot.RemainingAmount <= 0)
                        throw new InvalidDataException("副本掉落数量无效");
                    if(loot.AcquiredAmount<0||loot.AcquiredAmount>loot.RemainingAmount||loot.AcquiredAmount>0&&(!loot.OriginKnown||loot.OriginExpeditionId!=expedition.ExpeditionId))
                        throw new InvalidDataException("副本掉落探索来源无效");
                }
                foreach (var building in expedition.World.TemporaryBuildings)
                {
                    if (building.LoadedAmmo < 0 || building.UniformScale <= 0 || float.IsNaN(building.UniformScale) || float.IsInfinity(building.UniformScale)) throw new InvalidDataException("副本临时建筑弹药无效");
                }
                ValidateSearchContainers(expedition.World.SearchContainers, expedition.World.Enemies);
            }
        }

        private static void ValidateWorldRecords<T>(IEnumerable<T> records, Func<T, int> idOf, Func<T, string> configOf, Func<T, ExpeditionPoseData> poseOf, string kind)
        {
            var ids = new HashSet<int>();
            foreach (var record in records)
            {
                var pose = poseOf(record);
                if (idOf(record) <= 0 || string.IsNullOrWhiteSpace(configOf(record)) || pose == null ||
                    !IsFinite(pose.X) || !IsFinite(pose.Y) || !IsFinite(pose.Z) || !IsFinite(pose.RotationY) || !ids.Add(idOf(record)))
                {
                    throw new InvalidDataException($"副本{kind}快照包含重复 ID 或无效坐标");
                }
            }
        }

        private static void ValidateSearchContainers(IEnumerable<ExpeditionSearchContainerSaveData> records, IEnumerable<ExpeditionEnemySaveData> enemies)
        {
            var enemyIds = new HashSet<int>((enemies ?? Enumerable.Empty<ExpeditionEnemySaveData>()).Where(enemy => enemy != null).Select(enemy => enemy.RuntimeId));
            var ids = new HashSet<int>();
            foreach (var record in records ?? Enumerable.Empty<ExpeditionSearchContainerSaveData>())
            {
                if (record?.Pose == null || record.RuntimeId <= 0 || record.Capacity <= 0 || record.SourceEnemyRuntimeId <= 0 ||
                    !enemyIds.Contains(record.SourceEnemyRuntimeId) || !ids.Add(record.RuntimeId) || !IsFinite(record.Pose.X) || !IsFinite(record.Pose.Y) || !IsFinite(record.Pose.Z) ||
                    !IsFinite(record.Pose.RotationY) ||
                    record.Slots == null || record.Slots.Any(slot => slot == null || slot.Index < 0 || slot.Index >= record.Capacity || slot.ItemId <= 0 || slot.Amount <= 0))
                {
                    throw new InvalidDataException("副本搜索容器快照无效");
                }
            }
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsSafeId(string saveId)
        {
            return !string.IsNullOrWhiteSpace(saveId) &&
                   saveId.Length == 32 &&
                   saveId.All(Uri.IsHexDigit);
        }

    }
}
