using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;

namespace Game
{
    /// <summary>
    /// 管理街机局的安全节点事务、购买、死亡、恢复、结算与放弃；战斗现场和界面表现不由本系统保存。
    /// </summary>
    public sealed class ArcadeRunSystem : AbstractSystem
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

        /// <summary>从正式 Luban 目录建立可调数值的新局；种子与首个商店内容一并写入安全节点。</summary>
        public bool TryStartConfiguredRun(out ArcadeRunSaveData snapshot, out string error)
        {
            snapshot = null;
            if (!EnsureNoPendingRun(out error)) return false;
            var config = this.GetUtility<IRuntimeConfigProvider>();
            if (config == null || !config.TryGetArcadeCatalog(out var catalog, out error)) return false;

            var runId = Guid.NewGuid().ToString("N");
            var seed = unchecked((int)Convert.ToUInt32(runId.Substring(0, 8), 16));
            // 普通开局只保存恢复资料；空报价节点不代表实际购物区域。
            var shop = new ArcadeShopSaveData { ArenaNumber = 1, GenerationSeed = seed };
            var candidate = CreateNewRunCandidate(runId, catalog.Rule.InitialLives, catalog.Rule.BaseWeaponId, shop);
            candidate.IsCasualRoute = true;
            candidate.ArenaConfigNumber = catalog.Arenas[0].ArenaNumber;
            candidate.EmergencyResources[ArcadeRunDefaults.BombResourceId] = catalog.Rule.InitialBombs;
            candidate.EmergencyResources[ArcadeRunDefaults.DashResourceId] = catalog.Rule.InitialDashes;
            return TrySaveNewRun(candidate, out snapshot, out error);
        }

        /// <summary>按竞技场、保存种子和当前强化层数确定性抽取下一商店，供完成事务复用。</summary>
        public bool TryBuildConfiguredShop(int arenaNumber, int seed, ArcadeRunSaveData run,
            out ArcadeShopSaveData shop, out string error)
        {
            shop = null;
            var config = this.GetUtility<IRuntimeConfigProvider>();
            if (config == null)
            {
                error = "运行时配置不可用";
                return false;
            }
            if (!config.TryGetArcadeCatalog(out var catalog, out error)) return false;
            return TryBuildConfiguredShop(catalog, arenaNumber, seed, run, out shop, out error);
        }

        /// <summary>只以固定三命和基础武器建立第一安全节点，先落盘成功才公开运行态。</summary>
        public bool TryStartNewRun(ArcadeShopSaveData initialShop, out ArcadeRunSaveData snapshot, out string error)
        {
            snapshot = null;
            if (!EnsureNoPendingRun(out error)) return false;

            var shop = initialShop?.Clone() ?? new ArcadeShopSaveData();
            shop.ArenaNumber = 1;
            if (shop.Offers.Any(offer => offer != null && offer.Purchased))
            {
                error = "新开局商店不能包含已购买商品";
                return false;
            }

            var candidate = CreateNewRunCandidate(Guid.NewGuid().ToString("N"), ArcadeRunDefaults.InitialLives, ArcadeRunDefaults.BasicWeaponId, shop);
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
                // 旧普通节点的随机报价只作历史数据；恢复仍使用同一目的地和资源，不重新要求购物。
                if (!read.Data.HasResult && read.Data.Shop?.ChallengeAttempted != true)
                    read.Data.Shop = new ArcadeShopSaveData
                    { ArenaNumber = read.Data.NextArenaNumber, GenerationSeed = read.Data.Shop?.GenerationSeed ?? 0 };
            }
            CommitSafeNode(read.Data);
            snapshot = read.Data.Clone();
            error = null;
            return true;
        }

        public bool TryContinue(out ArcadeRunSaveData snapshot, out string error)
        {
            var model = this.GetModel<ArcadeRunModel>();
            if (!model.HasRun || model.Phase != ArcadeRunPhase.SafeShop)
            {
                snapshot = model.Current;
                error = "当前不在安全商店";
                return false;
            }

            model.EnterArena();
            snapshot = model.Current;
            Publish();
            error = null;
            return true;
        }

        /// <summary>先持久化本安全节点的挑战尝试，再公开挑战阶段；保存失败时仍停留在商店。</summary>
        public bool TryStartChallenge(out ArcadeRunSaveData snapshot, out string error)
        {
            var model = this.GetModel<ArcadeRunModel>();
            var current = model.Current;
            snapshot = current;
            if (model.Phase != ArcadeRunPhase.SafeShop || current?.Shop == null ||
                string.IsNullOrWhiteSpace(current.Shop.ChallengeId) || current.Shop.ChallengeAttempted)
            {
                error = "当前安全商店没有可进入的特殊挑战";
                return false;
            }
            var provider = this.GetUtility<IRuntimeConfigProvider>();
            if (provider == null)
            {
                error = "运行时配置不可用";
                return false;
            }
            if (!provider.TryGetArcadeCatalog(out var catalog, out error)) return false;
            if (!catalog.Challenges.Any(value => string.Equals(value.Id, current.Shop.ChallengeId, StringComparison.Ordinal)))
            {
                error = "特殊挑战配置不存在";
                return false;
            }

            var candidate = current.Clone();
            candidate.Shop.ChallengeAttempted = true;
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
            if (model.Phase != ArcadeRunPhase.Challenge || current?.Shop == null || !current.Shop.ChallengeAttempted)
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

            if (completion.Score < 0 || completion.ScoreMultiplier < 1 || completion.MultiplierProgress < 0 || completion.EarnedGold < 0 || !completion.Victory && (completion.NextShop == null ||
                completion.NextShop.ArenaNumber != current.NextArenaNumber + 1))
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

            var candidate = current.Clone();
            try
            {
                candidate.RunGold = checked(candidate.RunGold + completion.EarnedGold);
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
            candidate.Outcome = completion.Victory ? "victory" : null;
            candidate.PendingRewards.Clear();
            if (current.IsCasualRoute && !completion.Victory) candidate.ArenaConfigNumber = completion.NextArenaConfigNumber;
            candidate.Shop = completion.Victory
                ? new ArcadeShopSaveData { ArenaNumber = candidate.NextArenaNumber }
                : completion.NextShop.Clone();
            candidate.UpdatedAtUtc = clock.UtcNow.ToUniversalTime().ToString("O");
            if (!store.Save(candidate, out error)) return false;
            CommitSafeNode(candidate);
            snapshot = candidate.Clone();
            return true;
        }

        /// <summary>购买先构造完整候选并保存；只有安全节点写入成功才提交金币与商品状态。</summary>
        public bool TryPurchase(string offerId, out ArcadeRunSaveData snapshot, out string error)
        {
            var model = this.GetModel<ArcadeRunModel>();
            var current = model.Current;
            snapshot = current;
            if (model.Phase != ArcadeRunPhase.SafeShop || current == null)
            {
                error = "当前不在安全商店";
                return false;
            }

            var offer = current.Shop.Offers.FirstOrDefault(value => value != null && string.Equals(value.OfferId, offerId, StringComparison.Ordinal));
            if (offer == null || offer.Purchased)
            {
                error = "商品不存在或已经购买";
                return false;
            }
            // 旧安全节点保留报价用于读档，但已移除的近战商品必须在扣款前拒绝。
            if (offer.Kind == ArcadePurchaseKind.Equipment &&
                offer.EquipmentSlotId == ArcadeRunDefaults.WeaponSlotId)
            {
                var provider = this.GetUtility<IRuntimeConfigProvider>();
                if (provider != null && provider.TryGetArcadeCatalog(out var activeCatalog, out _) &&
                    activeCatalog.Weapons.Any(w => w.Id == offer.ProductId &&
                        w.Behavior == global::ArcadeWeaponBehavior.Melee))
                {
                    error = "街机已移除近战武器商品";
                    return false;
                }
            }
            if (current.RunGold < offer.Price)
            {
                error = Loc.T("ui.shop.error.insufficient_gold");
                return false;
            }

            var candidate = current.Clone();
            var candidateOffer = candidate.Shop.Offers.First(value => string.Equals(value.OfferId, offerId, StringComparison.Ordinal));
            try
            {
                candidate.RunGold = checked(candidate.RunGold - candidateOffer.Price);
                switch (candidateOffer.Kind)
                {
                    case ArcadePurchaseKind.Upgrade:
                        candidate.PurchasedUpgradeStacks.TryGetValue(candidateOffer.ProductId, out var stacks);
                        candidate.PurchasedUpgradeStacks[candidateOffer.ProductId] = checked(stacks + candidateOffer.Amount);
                        break;
                    case ArcadePurchaseKind.ExtraLife:
                        candidate.RemainingLives = checked(candidate.RemainingLives + candidateOffer.Amount);
                        break;
                    case ArcadePurchaseKind.Consumable:
                        candidate.Consumables.TryGetValue(candidateOffer.ProductId, out var count);
                        candidate.Consumables[candidateOffer.ProductId] = checked(count + candidateOffer.Amount);
                        break;
                    case ArcadePurchaseKind.Equipment:
                        candidate.EquipmentBySlot[candidateOffer.EquipmentSlotId] = candidateOffer.ProductId;
                        break;
                    default:
                        error = "商品类型无效";
                        return false;
                }
            }
            catch (OverflowException)
            {
                error = "购买结果数值溢出";
                return false;
            }

            candidateOffer.Purchased = true;
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

        private ArcadeRunSaveData CreateNewRunCandidate(string runId, int initialLives, string baseWeaponId, ArcadeShopSaveData shop)
        {
            var candidate = new ArcadeRunSaveData
            {
                RunId = runId,
                CompletedArenaCount = 0,
                NextArenaNumber = 1,
                RemainingLives = initialLives,
                RunGold = 0,
                Shop = shop,
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

        private static bool TryBuildConfiguredShop(ArcadeConfigCatalogDefinition catalog, int arenaNumber, int seed, ArcadeRunSaveData run,
            out ArcadeShopSaveData shop, out string error)
        {
            shop = null;
            if (catalog == null || arenaNumber <= 0)
            {
                error = "街机商店生成参数无效";
                return false;
            }

            var upgrades = catalog.Upgrades.ToDictionary(value => value.Id, StringComparer.Ordinal);
            var candidates = new List<ArcadeShopItemDefinition>();
            foreach (var item in catalog.ShopItems)
            {
                if (item.UnlockArena > arenaNumber) continue;
                if (item.ProductType == global::ArcadeShopProductType.Upgrade && run != null &&
                    upgrades.TryGetValue(item.ProductId, out var upgrade))
                {
                    run.PurchasedUpgradeStacks.TryGetValue(item.ProductId, out var stacks);
                    if (stacks >= Math.Min(item.MaxPurchases, upgrade.MaxStacks)) continue;
                }
                candidates.Add(item);
            }
            if (candidates.Count < catalog.Rule.ShopOfferCount)
            {
                error = $"竞技场 {arenaNumber} 可用街机商品不足 {catalog.Rule.ShopOfferCount} 个";
                return false;
            }

            var random = new Random(seed);
            var offers = new List<ArcadeShopOfferSaveData>();
            for (var index = 0; index < catalog.Rule.ShopOfferCount; index++)
            {
                var totalWeight = candidates.Sum(value => value.Weight);
                var roll = random.Next(totalWeight);
                var selectedIndex = 0;
                while (roll >= candidates[selectedIndex].Weight) roll -= candidates[selectedIndex++].Weight;
                var selected = candidates[selectedIndex];
                candidates.RemoveAt(selectedIndex);
                long price;
                try { price = checked((long)selected.BasePrice + (long)selected.PricePerArena * (arenaNumber - 1)); }
                catch (OverflowException) { error = $"街机商品 {selected.Id} 价格溢出"; return false; }
                offers.Add(new ArcadeShopOfferSaveData
                {
                    OfferId = $"{selected.Id}.{arenaNumber}.{index}",
                    ProductId = selected.ProductId,
                    Kind = ToPurchaseKind(selected.ProductType),
                    Price = price,
                    Amount = selected.Amount,
                    EquipmentSlotId = selected.EquipmentSlotId
                });
            }

            var completedArena = arenaNumber - 1;
            var challengeId = catalog.Challenges
                .Where(value => completedArena >= value.StartArena &&
                                (completedArena - value.StartArena) % value.Interval == 0)
                .OrderBy(value => value.Id, StringComparer.Ordinal)
                .Select(value => value.Id)
                .FirstOrDefault();
            shop = new ArcadeShopSaveData
            {
                ArenaNumber = arenaNumber,
                GenerationSeed = seed,
                RefreshCount = 0,
                ChallengeId = challengeId,
                ChallengeAttempted = false,
                Offers = offers
            };
            error = null;
            return true;
        }

        private static ArcadePurchaseKind ToPurchaseKind(global::ArcadeShopProductType value)
        {
            switch (value)
            {
                case global::ArcadeShopProductType.Upgrade: return ArcadePurchaseKind.Upgrade;
                case global::ArcadeShopProductType.ExtraLife: return ArcadePurchaseKind.ExtraLife;
                case global::ArcadeShopProductType.Consumable: return ArcadePurchaseKind.Consumable;
                case global::ArcadeShopProductType.Equipment: return ArcadePurchaseKind.Equipment;
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
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
