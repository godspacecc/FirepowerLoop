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
                // 写入失败时临时文件可能仍被占用；清理不能覆盖原错误或阻止调用者重试。
                try { TryDelete(TempPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
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

        /// <summary>严格校验可恢复安全节点及海岛轮前稳定状态，不接受现场世界对象。</summary>
        public static bool TryValidate(ArcadeRunSaveData data, out string error)
        {
            if (data == null || data.Version != ArcadeRunSaveData.CurrentVersion || !ArcadeRunIdentity.IsValid(data.RunId) ||
                data.CompletedArenaCount < 0 || data.NextArenaNumber != data.CompletedArenaCount + 1 ||
                data.Score < 0 || data.ScoreMultiplier < 1 || data.MultiplierProgress < 0 || (data.HasResult ? data.RemainingLives < 0 : data.RemainingLives <= 0) || data.RunGold < 0 || data.EmergencyResources == null ||
                data.PurchasedUpgradeStacks == null || data.Consumables == null || data.EquipmentBySlot == null ||
                data.Checkpoint == null || data.PendingRewards == null || string.IsNullOrWhiteSpace(data.UpdatedAtUtc))
            {
                error = "街机安全节点必需字段无效";
                return false;
            }

            if(data.SlayerMartRulesVersion<0||data.SlayerMartRulesVersion>1||
                data.SlayerMartRulesVersion==0&&(data.SlayerMart!=null||data.RoundCheckpoint?.SlayerMart!=null)||
                data.HasResult&&(data.SlayerMart!=null||data.RoundCheckpoint?.SlayerMart!=null))
            {error="商店规则版本或结果状态无效";return false;}
            if(data.SlayerMart!=null)
            {
                int completed=(data.NextArenaNumber-1)*4+(data.RoundCheckpoint?.RoundNumber??1)-1;
                if(!data.IsCasualRoute){error="旧顺序局不能携带新商店";return false;}
                if(!FirepowerLoop.Arcade.ArcadeSlayerMartSaveRules.TryValidate(data.SlayerMart,data.RunId,new long[]{1},completed,out error))return false;
            }
            if(data.RoundCheckpoint!=null&&!JToken.DeepEquals(
                data.SlayerMart==null?JValue.CreateNull():JObject.FromObject(data.SlayerMart),
                data.RoundCheckpoint.SlayerMart==null?JValue.CreateNull():JObject.FromObject(data.RoundCheckpoint.SlayerMart)))
            {error="轮级商店快照与全局账本不一致";return false;}

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
            if (data.Checkpoint.ArenaNumber != data.NextArenaNumber)
            {
                error = "街机恢复节点状态无效";
                return false;
            }

            if (data.IslandRoundCheckpoint != null &&
                (!data.IsCasualRoute || data.NextArenaNumber != 1 || data.ArenaConfigNumber != 1 ||
                 data.HasResult || !TryValidateIslandRound(data.IslandRoundCheckpoint)))
            {
                error = "海岛轮级安全节点无效";
                return false;
            }

            if (data.RoundCheckpoint != null)
            {
                var round = data.RoundCheckpoint;
                if (!data.IsCasualRoute || data.HasResult || data.IslandRoundCheckpoint != null || data.ArenaEntryState != null ||
                    round.ArenaNumber != data.NextArenaNumber || round.ArenaConfigNumber != data.ArenaConfigNumber ||
                    round.ArenaConfigNumber < 1 || round.ArenaConfigNumber > 20 || round.RoundNumber < 1 || round.RoundNumber > 4 ||
                    round.LayoutVersion <= 0 || round.MapId != $"arena.{round.ArenaConfigNumber}.round.{round.RoundNumber}" ||
                    round.PlayerState == null || round.PlayerState.MapId != round.MapId || round.PlayerState.RoundNumber != round.RoundNumber ||
                    !TryValidatePlayerState(round.PlayerState) || round.RemainingCrateIds == null ||
                    round.RemainingCrateIds.Any(id => id <= 0) || round.RemainingCrateIds.Distinct().Count() != round.RemainingCrateIds.Count || round.CrateHealth == null ||
                    round.CrateHealth.Any(pair => pair.Value <= 0 || !round.RemainingCrateIds.Contains(pair.Key)) ||
                    round.DestroyedBarrelIds == null || round.DestroyedBarrelIds.Any(id => id < 0) ||
                    round.DestroyedBarrelIds.Distinct().Count() != round.DestroyedBarrelIds.Count)
                {
                    error = "通用轮级安全节点无效";
                    return false;
                }
            }
            if (data.ArenaEntryState != null &&
                (!data.IsCasualRoute || data.NextArenaNumber <= 1 || data.HasResult || data.IslandRoundCheckpoint != null ||
                 data.ArenaEntryState.RoundNumber != 1 || data.ArenaEntryState.MapId != "arena.entry" ||
                 data.ArenaEntryState.ArenaGold != 0 || !TryValidatePlayerState(data.ArenaEntryState)))
            {
                error = "竞技场角色入场状态无效";
                return false;
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

        /// <summary>文件层验证稳定身份、有限数和非负余额；效果配置兼容性由战斗模块验证。</summary>
        private static bool TryValidateIslandRound(ArcadeIslandRoundCheckpoint value)
        {
            return value.RoundNumber >= 1 && value.RoundNumber <= 4 &&
                value.MapId == $"island.round.{value.RoundNumber}" && TryValidatePlayerState(value);
        }

        /// <summary>轮前节点与后续场地入场状态共享角色数值校验，分别验证地图身份。</summary>
        private static bool TryValidatePlayerState(ArcadeIslandRoundCheckpoint value)
        {
            if (value.PlayerMaxHealth <= 0 || value.PlayerHealth <= 0 || value.PlayerHealth > value.PlayerMaxHealth ||
                value.Bombs < 0 || value.Dashes < 0 || value.ArenaGold < 0 || value.Powerups == null ||
                !ValidTimer(value.TemporaryWeaponRemaining) || !ValidTimer(value.FirstPersonRemaining) ||
                !ValidTimer(value.FireCooldown) || !ValidTimer(value.InvulnerabilityRemaining) || !ValidTimer(value.DashRemaining) ||
                !Finite(value.AimDirectionX) || !Finite(value.AimDirectionY) ||
                (!string.IsNullOrEmpty(value.TemporaryWeaponId) && !ValidStableId(value.TemporaryWeaponId)) ||
                value.TemporaryWeaponRemaining > 0 && string.IsNullOrEmpty(value.TemporaryWeaponId)) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            return value.Powerups.All(effect => effect != null && ValidStableId(effect.ProductId) && ids.Add(effect.ProductId) &&
                ValidTimer(effect.RemainingSeconds) && effect.Charges >= 0 && ValidTimer(effect.CooldownSeconds) &&
                Finite(effect.PositionX) && Finite(effect.PositionY));
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool ValidTimer(float value) => Finite(value) && value >= 0;
        private static bool ValidStableId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
            value.All(character => character >= 'a' && character <= 'z' || character >= 'A' && character <= 'Z' ||
                character >= '0' && character <= '9' || character == '.' || character == '_' || character == '-');

        private static void TryDelete(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
        }
    }
}
