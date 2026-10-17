using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;

namespace Game
{
    /// <summary>
    /// 管理街机局的安全节点事务、死亡、恢复、结算与放弃；战斗现场和界面表现不由本系统保存。
    /// </summary>
    public sealed partial class ArcadeRunSystem : AbstractSystem
    {
        private ArcadeRunFileStore store;
        private ISaveClock clock = new UnitySaveClock();
        private string nextDeleteFailureForTests;

        protected override void OnInit()
        {
            store ??= new ArcadeRunFileStore();
        }

        public void ConfigureForTests(string rootDirectory, ISaveClock saveClock = null)
        {
            store = new ArcadeRunFileStore(rootDirectory);
            clock = saveClock ?? new UnitySaveClock();
            nextDeleteFailureForTests = null;
        }

        public SaveReadResult<ArcadeRunSaveData> InspectPending() => store.Inspect();

        /// <summary>从正式 Luban 目录建立可调数值的新局；随机种子与目的地一并写入安全节点。</summary>
        public bool TryStartConfiguredRun(out ArcadeRunSaveData snapshot, out string error)
        {
            snapshot = null;
            if (!EnsureNoPendingRun(out error)) return false;
            var config = this.GetUtility<IRuntimeConfigProvider>();
            if (config == null || !config.TryGetArcadeCatalog(out var catalog, out error)) return false;

            // 仅正式新局读取发布门禁，恢复和旧兼容入口保留各自已保存的规则版本。
            int slayerMartRulesVersion = 0;
            if (config.HasSlayerMartConfiguration)
            {
                if (!config.TryGetSlayerMart(out var shop, out error) || shop == null)
                {
                    error = string.IsNullOrWhiteSpace(error) ? "正式商店配置不可用，不能创建新局" : error;
                    return false;
                }
                slayerMartRulesVersion = shop.Enabled ? 1 : 0;
            }

            var runId = Guid.NewGuid().ToString("N");
            var seed = unchecked((int)Convert.ToUInt32(runId.Substring(0, 8), 16));
            // 普通开局只保存恢复资料；节点不承载交易数据。
            var checkpoint = new ArcadeCheckpointSaveData { ArenaNumber = 1, GenerationSeed = seed };
            var candidate = CreateNewRunCandidate(runId, catalog.Rule.InitialLives, catalog.Rule.BaseWeaponId, checkpoint);
            candidate.IsCasualRoute = true;
            candidate.SlayerMartRulesVersion = slayerMartRulesVersion;
            candidate.ArenaConfigNumber = catalog.Arenas[0].ArenaNumber;
            candidate.EmergencyResources[ArcadeRunDefaults.BombResourceId] = catalog.Rule.InitialBombs;
            candidate.EmergencyResources[ArcadeRunDefaults.DashResourceId] = catalog.Rule.InitialDashes;
            return TrySaveNewRun(candidate, out snapshot, out error);
        }

        /// <summary>只以固定三命和基础武器建立第一安全节点，先落盘成功才公开运行态。</summary>
        public bool TryStartNewRun(ArcadeCheckpointSaveData initialCheckpoint, out ArcadeRunSaveData snapshot, out string error)
        {
            snapshot = null;
            if (!EnsureNoPendingRun(out error)) return false;

            var checkpoint = initialCheckpoint?.Clone() ?? new ArcadeCheckpointSaveData();
            checkpoint.ArenaNumber = 1;

            var candidate = CreateNewRunCandidate(Guid.NewGuid().ToString("N"), ArcadeRunDefaults.InitialLives, ArcadeRunDefaults.BasicWeaponId, checkpoint);
            return TrySaveNewRun(candidate, out snapshot, out error);
        }

        /// <summary>恢复最后一个安全节点；文件损坏时不修改现有运行态。</summary>
        public bool TryResume(out ArcadeRunSaveData snapshot, out string error)
        {
            snapshot = null;
            var read = store.Inspect();
            if (!read.IsValid)
            {
                error = read.Status == SaveFileStatus.Missing ? "没有可恢复的街机局" : read.Error;
                return false;
            }

            if (read.Data.IsCasualRoute)
            {
                var provider = this.GetUtility<IRuntimeConfigProvider>();
                if (provider == null || !provider.TryGetArcadeCatalog(out var catalog, out _) ||
                    !catalog.Arenas.Any(arena => arena.ArenaNumber == read.Data.ArenaConfigNumber))
                {
                    error = "存档路线场地不在当前配置中";
                    return false;
                }
                // 旧普通节点保留同一目的地和资源；未尝试挑战沿用普通继续规则。
                if (!read.Data.HasResult && read.Data.Checkpoint?.ChallengeAttempted != true)
                    read.Data.Checkpoint = new ArcadeCheckpointSaveData
                    { ArenaNumber = read.Data.NextArenaNumber, GenerationSeed = read.Data.Checkpoint?.GenerationSeed ?? 0 };
            }
            CommitSafeNode(read.Data);
            snapshot = read.Data.Clone();
            error = null;
            return true;
        }

        public bool TryContinue(out ArcadeRunSaveData snapshot, out string error)
        {
            var model = this.GetModel<ArcadeRunModel>();
            if (!model.HasRun || model.Phase != ArcadeRunPhase.SafeCheckpoint)
            {
                snapshot = model.Current;
                error = "当前不在安全节点";
                return false;
            }

            model.EnterArena();
            snapshot = model.Current;
            Publish();
            error = null;
            return true;
        }

        /// <summary>轮开始前写入完整候选；失败保持原节点与运行态，成功也不发布整场阶段事件。</summary>
        public bool TrySaveIslandRound(ArcadeIslandRoundCheckpoint checkpoint, out string error)
        {
            var model = this.GetModel<ArcadeRunModel>();
            var current = model.Current;
            if (model.Phase != ArcadeRunPhase.Arena || current == null || current.HasResult ||
                !current.IsCasualRoute || current.NextArenaNumber != 1 || current.ArenaConfigNumber != 1 || current.RoundCheckpoint != null || checkpoint == null)
            {
                error = "当前不是可保存轮级节点的单人海岛战斗";
                return false;
            }
            var previousRound = current.IslandRoundCheckpoint?.RoundNumber ?? 0;
            if (checkpoint.RoundNumber != previousRound + 1 && checkpoint.RoundNumber != previousRound)
            {
                error = "海岛轮级节点必须连续推进";
                return false;
            }
            // 同轮重试仅接受已提交的相同内容，不能把本轮中途收益覆盖成新的安全节点。
            if (checkpoint.RoundNumber == previousRound)
            {
                if (Newtonsoft.Json.JsonConvert.SerializeObject(checkpoint) !=
                    Newtonsoft.Json.JsonConvert.SerializeObject(current.IslandRoundCheckpoint))
                {
                    error = "海岛同轮节点不能覆盖已提交状态";
                    return false;
                }
                error = null;
                return true;
            }
            var candidate = current.Clone();
            candidate.IslandRoundCheckpoint = checkpoint.Clone();
            candidate.ArenaEntryState = null;
            candidate.EmergencyResources[ArcadeRunDefaults.BombResourceId] = checkpoint.Bombs;
            candidate.EmergencyResources[ArcadeRunDefaults.DashResourceId] = checkpoint.Dashes;
            candidate.UpdatedAtUtc = clock.UtcNow.ToUniversalTime().ToString("O");
            if (!store.Save(candidate, out error)) return false;
            model.CommitIslandRound(candidate);
            return true;
        }

        /// <summary>连续轮前事务：先校验完整候选，再原子落盘，最后公开权威节点。</summary>
        public bool TrySaveRound(ArcadeRoundCheckpoint checkpoint, out string error)
        {
            var model = this.GetModel<ArcadeRunModel>();
            var current = model.Current;
            if (model.Phase != ArcadeRunPhase.Arena || current == null || current.HasResult || !current.IsCasualRoute || checkpoint == null ||
                checkpoint.ArenaNumber != current.NextArenaNumber || checkpoint.ArenaConfigNumber != current.ArenaConfigNumber)
            {
                error = "当前不是可保存该轮节点的单人战斗";
                return false;
            }
            var previousRound = current.RoundCheckpoint?.RoundNumber ?? current.IslandRoundCheckpoint?.RoundNumber ?? 0;
            if (checkpoint.RoundNumber != previousRound + 1 && checkpoint.RoundNumber != previousRound)
            {
                error = "通用轮级节点必须连续推进";
                return false;
            }
            var candidate = current.Clone();
            candidate.RoundCheckpoint = checkpoint.Clone();
            candidate.SlayerMart=checkpoint.SlayerMart?.Clone();
            candidate.IslandRoundCheckpoint = null;
            candidate.ArenaEntryState = null;
            if (checkpoint.PlayerState != null)
            {
                candidate.EmergencyResources[ArcadeRunDefaults.BombResourceId] = checkpoint.PlayerState.Bombs;
                candidate.EmergencyResources[ArcadeRunDefaults.DashResourceId] = checkpoint.PlayerState.Dashes;
            }
            candidate.UpdatedAtUtc = clock.UtcNow.ToUniversalTime().ToString("O");
            if (!ArcadeRunFileStore.TryValidate(candidate, out error)) return false;
            // 同轮只接受完全相同的已提交包装，不能用中途收益改写安全节点。
            if (checkpoint.RoundNumber == previousRound)
            {
                if (current.RoundCheckpoint == null || !Newtonsoft.Json.Linq.JToken.DeepEquals(
                    Newtonsoft.Json.Linq.JObject.FromObject(checkpoint), Newtonsoft.Json.Linq.JObject.FromObject(current.RoundCheckpoint)))
                {
                    error = "同轮节点不能覆盖已提交状态";
                    return false;
                }
                error = null;
                return true;
            }
            if (!store.Save(candidate, out error)) return false;
            model.CommitRound(candidate);
            return true;
        }
        /// <summary>先持久化本安全节点的挑战尝试，再公开挑战阶段；保存失败时仍保留安全节点。</summary>
        public bool TryStartChallenge(out ArcadeRunSaveData snapshot, out string error)
        {
            var model = this.GetModel<ArcadeRunModel>();
            var current = model.Current;
            snapshot = current;
            if (model.Phase != ArcadeRunPhase.SafeCheckpoint || current?.Checkpoint == null ||
                string.IsNullOrWhiteSpace(current.Checkpoint.ChallengeId) || current.Checkpoint.ChallengeAttempted)
            {
                error = "当前安全节点没有可进入的特殊挑战";
                return false;
            }
            var provider = this.GetUtility<IRuntimeConfigProvider>();
            if (provider == null)
            {
                error = "运行时配置不可用";
                return false;
            }
            if (!provider.TryGetArcadeCatalog(out var catalog, out error)) return false;
            if (!catalog.Challenges.Any(value => string.Equals(value.Id, current.Checkpoint.ChallengeId, StringComparison.Ordinal)))
            {
                error = "特殊挑战配置不存在";
                return false;
            }

            var candidate = current.Clone();
            bool isSlayer=provider.TryGetSlayerChallenge(out var slayerConfig,out _)&&slayerConfig.Enabled&&current.SlayerMartRulesVersion==1&&current.Checkpoint.ChallengeId==slayerConfig.ChallengeId;
            if(current.Checkpoint.ChallengeId=="arcade.challenge.slayer-guardian"&&!isSlayer){error="Slayer配置或规则版本不可用";return false;}
            candidate.Checkpoint.ChallengeAttempted = !isSlayer;
            candidate.UpdatedAtUtc = clock.UtcNow.ToUniversalTime().ToString("O");
            if (!store.Save(candidate, out error)) return false;
            model.CommitSafeNode(candidate);
            model.EnterChallenge();
            snapshot = model.Current;
            Publish();
            error = null;
            return true;
        }

        /// <summary>挑战只提交正式奖励，不推进竞技场编号；失败恢复已落盘的“已尝试”安全节点。</summary>
        public bool TryCompleteChallenge(bool succeeded, IReadOnlyList<ArcadeRewardSaveData> rewards,
            out ArcadeRunSaveData snapshot, out string error)
        {
            var model = this.GetModel<ArcadeRunModel>();
            var current = model.Current;
            snapshot = current;
            if (model.Phase != ArcadeRunPhase.Challenge || current?.Checkpoint == null || !current.Checkpoint.ChallengeAttempted)
            {
                error = "特殊挑战完成身份无效";
                return false;
            }
            if (!succeeded) return TryResume(out snapshot, out error);

            var candidate = current.Clone();
            foreach (var reward in rewards ?? Array.Empty<ArcadeRewardSaveData>())
            {
                if (reward == null || string.IsNullOrWhiteSpace(reward.RewardId) || reward.Amount <= 0)
                {
                    error = "特殊挑战奖励无效";
                    return false;
                }
                // 本产品不保存局外奖励；局内资源与分数仍按既有完成事务提交。
            }
            candidate.UpdatedAtUtc = clock.UtcNow.ToUniversalTime().ToString("O");
            if (!store.Save(candidate, out error)) return false;
            CommitSafeNode(candidate);
            snapshot = candidate.Clone();
            return true;
        }

        /// <summary>竞技场中退出或主动返回时重新读取落盘节点，放弃本竞技场全部瞬时收益且不扣生命。</summary>
        public bool TryReturnToSafeNode(out ArcadeRunSaveData snapshot, out string error)
        {
            ArcadeRunPhase phase = this.GetModel<ArcadeRunModel>().Phase;
            if (phase != ArcadeRunPhase.Arena && phase != ArcadeRunPhase.Challenge)
            {
                snapshot = this.GetModel<ArcadeRunModel>().Current;
                error = "当前不在竞技场";
                return false;
            }

            return TryResume(out snapshot, out error);
        }

        /// <summary>死亡仅修改本次竞技场运行态；最后一条生命耗尽时保存结果，保存失败则保持可重试状态。</summary>
        public ArcadeDeathResult TryHandleDeath(out ArcadeRunSaveData snapshot, out string error)
        {
            var model = this.GetModel<ArcadeRunModel>();
            var current = model.Current;
            if (model.Phase != ArcadeRunPhase.Arena || current == null)
            {
                snapshot = current;
                error = "当前没有进行中的竞技场";
                return ArcadeDeathResult.Rejected;
            }

            if (current.RemainingLives > 1)
            {
                model.SetTransientLives(current.RemainingLives - 1);
                snapshot = model.Current;
                Publish();
                error = null;
                return ArcadeDeathResult.Respawn;
            }

            var candidate = current.Clone();
            candidate.RemainingLives = 0;
            candidate.Outcome = "failed";
            candidate.SlayerMart=null;
            candidate.RoundCheckpoint = null;
            candidate.IslandRoundCheckpoint = null;
            candidate.ArenaEntryState = null;
            candidate.PendingRewards.Clear();
            candidate.UpdatedAtUtc = clock.UtcNow.ToUniversalTime().ToString("O");
            if (!store.Save(candidate, out error))
            {
                snapshot = current;
                return ArcadeDeathResult.Rejected;
            }
            CommitSafeNode(candidate);
            snapshot = candidate.Clone();
            return ArcadeDeathResult.RunFailed;
        }

        /// <summary>竞技场全部轮次完成后一次提交稳定结果；保存失败时仍停留在原竞技场运行态。</summary>
        public bool TryCompleteArena(ArcadeArenaCompletion completion, out ArcadeRunSaveData snapshot, out string error)
        {
            var model = this.GetModel<ArcadeRunModel>();
            var current = model.Current;
            snapshot = current;
            if (model.Phase != ArcadeRunPhase.Arena || current == null || completion == null || completion.ArenaNumber != current.NextArenaNumber)
            {
                error = "竞技场完成身份无效";
                return false;
            }

            if (completion.Score < 0 || completion.ScoreMultiplier < 1 || completion.MultiplierProgress < 0 || completion.EarnedGold < 0 || !completion.Victory && (completion.NextCheckpoint == null ||
                completion.NextCheckpoint.ArenaNumber != current.NextArenaNumber + 1))
            {
                error = "竞技场完成结果无效";
                return false;
            }

            if (current.IsCasualRoute)
            {
                var provider = this.GetUtility<IRuntimeConfigProvider>();
                if (provider == null) { error = "运行时配置不可用"; return false; }
                if (!provider.TryGetArcadeCatalog(out var catalog, out error)) return false;
                var arena = catalog.Arenas.FirstOrDefault(value => value.ArenaNumber == current.ArenaConfigNumber);
                var isTerminal = arena != null && arena.NextArenaNumbers.Count == 0 && !string.IsNullOrEmpty(arena.FinalBossId);
                if (arena == null || completion.Victory != isTerminal ||
                    completion.Victory && (current.NextArenaNumber != 10 || completion.NextArenaConfigNumber != 0) ||
                    !completion.Victory && (current.NextArenaNumber >= 10 || !arena.NextArenaNumbers.Contains(completion.NextArenaConfigNumber)))
                { error = "休闲路线选择或胜利身份无效"; return false; }
            }
            else if (completion.Victory)
            { error = "旧顺序局不能直接提交休闲胜利"; return false; }

            if (completion.PlayerState != null &&
                (!current.IsCasualRoute || !(current.NextArenaNumber == 1 && current.ArenaConfigNumber == 1 || current.ArenaEntryState != null || current.RoundCheckpoint != null)))
            {
                error = "当前竞技场不允许建立角色继承状态";
                return false;
            }

            var candidate = current.Clone();
            try
            {
                candidate.RunGold = checked(candidate.RunGold + completion.EarnedGold);
                candidate.IslandRoundCheckpoint = null;
                candidate.RoundCheckpoint = null;
                candidate.Score = completion.Score;
                candidate.ScoreMultiplier = completion.ScoreMultiplier;
                candidate.MultiplierProgress = completion.MultiplierProgress;
                candidate.CompletedArenaCount = completion.ArenaNumber;
                candidate.NextArenaNumber = completion.ArenaNumber + 1;
                candidate.EmergencyResources = CopyAmounts(completion.EmergencyResources);
                candidate.EquipmentBySlot = CopyEquipment(completion.EquipmentBySlot);
                foreach (var reward in completion.EarnedRewards ?? Array.Empty<ArcadeRewardSaveData>())
                {
                    if (reward == null || string.IsNullOrWhiteSpace(reward.RewardId) || reward.Amount <= 0)
                    {
                        error = "竞技场正式奖励无效";
                        return false;
                    }
                    // 本产品不保存局外奖励；局内资源与分数仍按既有完成事务提交。
                }
            }
            catch (OverflowException)
            {
                error = "竞技场结算数值溢出";
                return false;
            }

            candidate.Victory = completion.Victory;
            candidate.ArenaEntryState = completion.Victory ? null : completion.PlayerState?.Clone();
            candidate.Outcome = completion.Victory ? "victory" : null;
            candidate.SlayerMart=completion.Victory?null:(completion.SlayerMart??current.SlayerMart)?.Clone();
            candidate.PendingRewards.Clear();
            if (current.IsCasualRoute && !completion.Victory) candidate.ArenaConfigNumber = completion.NextArenaConfigNumber;
            candidate.Checkpoint = completion.Victory
                ? new ArcadeCheckpointSaveData { ArenaNumber = candidate.NextArenaNumber }
                : completion.NextCheckpoint.Clone();
            ConfigureSlayerOpportunity(candidate);
            candidate.UpdatedAtUtc = clock.UtcNow.ToUniversalTime().ToString("O");
            if (!store.Save(candidate, out error)) return false;
            CommitSafeNode(candidate);
            snapshot = candidate.Clone();
            return true;
        }

        /// <summary>
        /// 确认已经持久化的本局结果；删除失败保留结果以便重试，不写入任何局外资产。
        /// </summary>
        public bool TrySettle(out bool alreadySettled, out string error)
        {
            alreadySettled = false;
            var model = this.GetModel<ArcadeRunModel>();
            if (model.Current?.HasResult != true)
            {
                error = "当前没有待确认的本局结果";
                return false;
            }
            // 玩家确认结果后才删除；失败时保留完整结果以便重试，不接触基地或奖励钱包。
            if (!TryDeleteRunFiles(out error)) return false;
            model.Clear();
            Publish();
            return true;
        }

        public bool TryAbandon(out string error)
        {
            var current = this.GetModel<ArcadeRunModel>().Current;
            if (current == null) { error = "没有可放弃的街机局"; return false; }
            if (current.HasResult) { error = null; return true; }
            var candidate = current.Clone();
            candidate.Outcome = "abandoned";
            candidate.SlayerMart=null;
            candidate.RoundCheckpoint = null;
            candidate.IslandRoundCheckpoint = null;
            candidate.ArenaEntryState = null;
            candidate.PendingRewards.Clear();
            candidate.UpdatedAtUtc = clock.UtcNow.ToUniversalTime().ToString("O");
            if (!store.Save(candidate, out error)) return false;
            CommitSafeNode(candidate);
            return true;
        }

        /// <summary>仅供 EditMode 测试：使下一次删除失败，验证结算幂等重试。</summary>
        public void FailNextDeleteForTests(string error = "测试注入的街机存档删除失败")
        {
            nextDeleteFailureForTests = string.IsNullOrWhiteSpace(error) ? "测试注入的街机存档删除失败" : error;
        }

        private bool EnsureNoPendingRun(out string error)
        {
            var pending = store.Inspect();
            if (pending.Status == SaveFileStatus.Missing)
            {
                error = null;
                return true;
            }
            error = pending.IsValid ? "已有未结束的街机局" : "街机局存档损坏，不能覆盖";
            return false;
        }

        private ArcadeRunSaveData CreateNewRunCandidate(string runId, int initialLives, string baseWeaponId, ArcadeCheckpointSaveData checkpoint)
        {
            var candidate = new ArcadeRunSaveData
            {
                RunId = runId,
                CompletedArenaCount = 0,
                NextArenaNumber = 1,
                RemainingLives = initialLives,
                RunGold = 0,
                Checkpoint = checkpoint,
                UpdatedAtUtc = clock.UtcNow.ToUniversalTime().ToString("O")
            };
            candidate.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId] = baseWeaponId;
            return candidate;
        }

        private bool TrySaveNewRun(ArcadeRunSaveData candidate, out ArcadeRunSaveData snapshot, out string error)
        {
            snapshot = null;
            if (!store.Save(candidate, out error)) return false;
            CommitSafeNode(candidate);
            snapshot = candidate.Clone();
            return true;
        }

        private static Dictionary<string, int> CopyAmounts(IReadOnlyDictionary<string, int> source)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in source ?? new Dictionary<string, int>()) result[pair.Key] = pair.Value;
            return result;
        }

        private static Dictionary<string, string> CopyEquipment(IReadOnlyDictionary<string, string> source)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in source ?? new Dictionary<string, string>()) result[pair.Key] = pair.Value;
            return result;
        }

        private void CommitSafeNode(ArcadeRunSaveData value)
        {
            this.GetModel<ArcadeRunModel>().CommitSafeNode(value);
            Publish();
        }

        private bool TryDeleteRunFiles(out string error)
        {
            if (!string.IsNullOrWhiteSpace(nextDeleteFailureForTests))
            {
                error = nextDeleteFailureForTests;
                nextDeleteFailureForTests = null;
                return false;
            }
            return store.Delete(out error);
        }

        private void Publish()
        {
            var model = this.GetModel<ArcadeRunModel>();
            try
            {
                this.SendEvent(new ArcadeRunChangedEvent(model.Phase, model.Current));
            }
            catch (Exception exception)
            {
                Log.Error($"[ArcadeRunSystem] 街机状态事件监听器失败：{exception.Message}");
            }
        }
    }
}
