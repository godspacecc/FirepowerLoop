using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game
{
    /// <summary>原子读写唯一街机安全节点文件；不参与基地或远征分区捕获。</summary>
    public sealed class ArcadeRunFileStore
    {
        private readonly string rootDirectory;

        public ArcadeRunFileStore() : this(Application.persistentDataPath)
        {
        }

        public ArcadeRunFileStore(string rootDirectory)
        {
            this.rootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? Application.persistentDataPath : rootDirectory;
        }

        public string SavesDirectory => Path.Combine(rootDirectory, "Saves");
        public string RunPath => Path.Combine(SavesDirectory, "arcade-run.json");
        public string TempPath => Path.Combine(SavesDirectory, "arcade-run.tmp.json");
        public string BackupPath => Path.Combine(SavesDirectory, "arcade-run.backup.json");

        public SaveReadResult<ArcadeRunSaveData> Inspect()
        {
            var result = new SaveReadResult<ArcadeRunSaveData>();
            if (!File.Exists(RunPath) && !File.Exists(BackupPath))
            {
                result.Status = SaveFileStatus.Missing;
                return result;
            }

            foreach (var candidate in new[] { RunPath, BackupPath })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    var raw = JObject.Parse(File.ReadAllText(candidate));
                    var data = raw.ToObject<ArcadeRunSaveData>();
                    if (!TryValidate(data, out var error)) throw new InvalidDataException(error);
                    result.Status = SaveFileStatus.Valid;
                    result.Data = data.Clone();
                    result.FromBackup = string.Equals(candidate, BackupPath, StringComparison.Ordinal);
                    result.Error = null;
                    return result;
                }
                catch (Exception exception)
                {
                    result.Error = exception.Message;
                }
            }

            result.Status = SaveFileStatus.Corrupted;
            return result;
        }

        public bool Save(ArcadeRunSaveData data, out string error)
        {
            error = null;
            try
            {
                if (!TryValidate(data, out error)) return false;
                Directory.CreateDirectory(SavesDirectory);
                File.WriteAllText(TempPath, JsonConvert.SerializeObject(data, Formatting.Indented));
                JObject.Parse(File.ReadAllText(TempPath));
                if (File.Exists(RunPath))
                {
                    TryDelete(BackupPath);
                    File.Replace(TempPath, RunPath, BackupPath, true);
                }
                else
                {
                    File.Move(TempPath, RunPath);
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                TryDelete(TempPath);
                return false;
            }
        }

        public bool Delete(out string error)
        {
            error = null;
            try
            {
                // 先清理旧备份，最后删除当前结果；任何失败都不会恢复成旧的可战斗节点。
                TryDelete(TempPath);
                TryDelete(BackupPath);
                TryDelete(RunPath);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        /// <summary>严格校验可恢复的安全节点；任何战斗现场字段都不属于此合同。</summary>
        public static bool TryValidate(ArcadeRunSaveData data, out string error)
        {
            if (data == null || data.Version != ArcadeRunSaveData.CurrentVersion || !ArcadeRunIdentity.IsValid(data.RunId) ||
                data.CompletedArenaCount < 0 || data.NextArenaNumber != data.CompletedArenaCount + 1 ||
                data.Score < 0 || data.ScoreMultiplier < 1 || data.MultiplierProgress < 0 || (data.HasResult ? data.RemainingLives < 0 : data.RemainingLives <= 0) || data.RunGold < 0 || data.EmergencyResources == null ||
                data.PurchasedUpgradeStacks == null || data.Consumables == null || data.EquipmentBySlot == null ||
                data.Shop == null || data.PendingRewards == null || string.IsNullOrWhiteSpace(data.UpdatedAtUtc))
            {
                error = "街机安全节点必需字段无效";
                return false;
            }

            if (data.Outcome != null && data.Outcome != "victory" && data.Outcome != "failed" && data.Outcome != "abandoned" ||
                data.Outcome == "victory" && !data.Victory || data.Victory && data.Outcome != "victory")
            {
                error = "街机结果身份无效";
                return false;
            }
            if (!TryValidateAmounts(data.EmergencyResources) || !TryValidateAmounts(data.Consumables) || !TryValidateAmounts(data.PurchasedUpgradeStacks) ||
                data.EquipmentBySlot.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) ||
                !data.EquipmentBySlot.TryGetValue(ArcadeRunDefaults.WeaponSlotId, out var weaponId) || string.IsNullOrWhiteSpace(weaponId))
            {
                error = "街机资源、强化或装备状态无效";
                return false;
            }

            if (data.IsCasualRoute && (data.ArenaConfigNumber <= 0 || data.CompletedArenaCount > 10 ||
                    data.Victory != (data.CompletedArenaCount == 10)) ||
                data.Victory && !data.IsCasualRoute)
            {
                error = "街机休闲路线或胜利节点无效";
                return false;
            }
            if (data.Shop.ArenaNumber != data.NextArenaNumber || data.Shop.RefreshCount < 0 || data.Shop.Offers == null)
            {
                error = "街机商店重建状态无效";
                return false;
            }

            var offerIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var offer in data.Shop.Offers)
            {
                if (offer == null || string.IsNullOrWhiteSpace(offer.OfferId) || !offerIds.Add(offer.OfferId) ||
                    string.IsNullOrWhiteSpace(offer.ProductId) || !Enum.IsDefined(typeof(ArcadePurchaseKind), offer.Kind) ||
                    offer.Price < 0 || offer.Amount <= 0 ||
                    offer.Kind == ArcadePurchaseKind.Equipment && string.IsNullOrWhiteSpace(offer.EquipmentSlotId))
                {
                    error = "街机商店商品状态无效";
                    return false;
                }
            }

            foreach (var reward in data.PendingRewards)
            {
                if (reward == null || string.IsNullOrWhiteSpace(reward.RewardId) || reward.Amount <= 0)
                {
                    error = "街机待结算奖励无效";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private static bool TryValidateAmounts(IEnumerable<KeyValuePair<string, int>> values) =>
            values.All(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value >= 0);

        private static void TryDelete(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
        }
    }
}
