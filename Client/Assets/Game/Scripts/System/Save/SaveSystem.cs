using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using QFramework;

namespace Game
{
    /// <summary>
    /// 保存运行时存档事务快照，包含当前存档克隆和各分区快照，用于失败回滚。
    /// </summary>
    public sealed class SaveRuntimeSnapshot
    {
        internal SaveRuntimeSnapshot(
            OfflineSaveDomain domain,
            BaseSaveData currentBase,
            ExpeditionSaveData currentExpedition,
            ExpeditionSaveData expeditionRuntime,
            bool expeditionTaskManaged,
            Dictionary<string, JToken> providerData,
            bool dirty,
            float dirtyAt)
        {
            Domain = domain;
            CurrentBase = currentBase?.Clone();
            CurrentExpedition = currentExpedition?.Clone();
            ExpeditionRuntime = expeditionRuntime?.Clone();
            ExpeditionTaskManaged = expeditionTaskManaged;
            ProviderData = SaveDataSections.Clone(providerData);
            Dirty = dirty;
            DirtyAt = dirtyAt;
        }

        public OfflineSaveDomain Domain { get; }
        /// <summary>事务开始时 SaveModel 的基地 DTO 克隆；恢复时不会与当前对象共享可变集合。</summary>
        public BaseSaveData CurrentBase { get; }
        /// <summary>事务开始时 SaveModel 的活动副本 DTO 克隆，区别于可能含未刷盘进度的 ExpeditionRuntime。</summary>
        public ExpeditionSaveData CurrentExpedition { get; }
        /// <summary>事务开始时独立捕获的远征模型瞬时值，包含尚未 Flush 的计时和状态。</summary>
        internal ExpeditionSaveData ExpeditionRuntime { get; }
        /// <summary>顶层 DTO 不持有的任务托管标记；恢复时与远征瞬时值一并原样还原。</summary>
        internal bool ExpeditionTaskManaged { get; }
        /// <summary>事务开始时的自动保存待写状态和起算时刻，失败回滚后继续原等待语义。</summary>
        internal bool Dirty { get; }
        internal float DirtyAt { get; }
        /// <summary>所有注册存档分区的深层快照；目标恢复失败时也用于还原调用前分区。</summary>
        internal Dictionary<string, JToken> ProviderData { get; }
    }

    /// <summary>
    /// 抽象存档时间来源，使文件写入和测试可以使用一致的 UTC 时间语义。
    /// </summary>
    public interface ISaveClock
    {
        DateTime UtcNow { get; }
        float RealtimeSinceStartup { get; }
    }

    /// <summary>
    /// 存档系统默认时间源，返回当前 UTC 时间用于创建和更新存档时间戳。
    /// </summary>
    public sealed class UnitySaveClock : ISaveClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public float RealtimeSinceStartup => UnityEngine.Time.realtimeSinceStartup;
    }

    /// <summary>
    /// 协调固定基地/副本文件和当前活动域的分区快照事务。
    /// </summary>
    public sealed class SaveSystem : AbstractSystem
    {
        private const float AutoSaveDelay = 0.5f;
        private readonly Dictionary<string, ISaveSectionProvider> providers = new Dictionary<string, ISaveSectionProvider>(StringComparer.Ordinal);
        private SaveFileStore store;
        private ISaveClock clock = new UnitySaveClock();
        private string nextDeleteExpeditionFailureForTests;
        private string nextOfflineStorageCleanupFailureForTests;
        private bool offlineStoragePrepared;
        private bool dirty;
        private float dirtyAt;

        protected override void OnInit()
        {
            store ??= new SaveFileStore();
            LifeCycle.RegisterUpdate(Update);
        }

        protected override void OnDeinit()
        {
            Flush(out _);
            LifeCycle.UnregisterUpdate(Update);
        }

        public void ConfigureForTests(string rootDirectory, ISaveClock clock = null)
        {
            store = new SaveFileStore(rootDirectory);
            this.clock = clock ?? new UnitySaveClock();
            offlineStoragePrepared = false;
            nextOfflineStorageCleanupFailureForTests = null;
        }

        public bool RegisterSection(ISaveSectionProvider provider, out string error)
        {
            error = null;
            if (provider == null || string.IsNullOrWhiteSpace(provider.SectionId) || !(provider is IScopedSaveSectionProvider))
            {
                error = "存档分区无效或未声明数据域范围";
                return false;
            }

            if (providers.ContainsKey(provider.SectionId))
            {
                error = $"存档分区重复：{provider.SectionId}";
                return false;
            }

            providers.Add(provider.SectionId, provider);
            return true;
        }

        public bool PrepareOfflineStorage(out string error)
        {
            if (offlineStoragePrepared)
            {
                error = null;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(nextOfflineStorageCleanupFailureForTests))
            {
                error = nextOfflineStorageCleanupFailureForTests;
                nextOfflineStorageCleanupFailureForTests = null;
                return false;
            }

            if (!store.CleanupLegacyFiles(out error))
            {
                error = "离线存档清理失败：" + error;
                return false;
            }

            offlineStoragePrepared = true;
            return true;
        }

        /// <summary>仅供 EditMode 测试：使下一次离线清理失败，失败会自动消费。</summary>
        public void FailNextOfflineStorageCleanupForTests(string error = "测试注入的离线存档清理失败")
        {
            offlineStoragePrepared = false;
            nextOfflineStorageCleanupFailureForTests = string.IsNullOrWhiteSpace(error) ? "测试注入的离线存档清理失败" : error;
        }

        private bool EnsureOfflineStorage(out string error) => PrepareOfflineStorage(out error);

        public SaveReadResult<BaseSaveData> InspectBase()
        {
            if (EnsureOfflineStorage(out var error)) return store.InspectBase();
            return new SaveReadResult<BaseSaveData> { Status = SaveFileStatus.Corrupted, Error = error };
        }

        public SaveReadResult<ExpeditionSaveData> InspectExpedition()
        {
            if (EnsureOfflineStorage(out var error)) return store.InspectExpedition();
            return new SaveReadResult<ExpeditionSaveData> { Status = SaveFileStatus.Corrupted, Error = error };
        }

        /// <summary>读取并验证副本候选数据，不修改当前运行态。</summary>
        public SaveReadResult<ExpeditionSaveData> PrepareExpedition()
        {
            var read = InspectExpedition();
            if (!read.IsValid) return read;
            if (!ValidateSections(read.Data.Sections, OfflineSaveDomain.Expedition, out var error))
            {
                read.Status = SaveFileStatus.Corrupted;
                read.Error = error;
            }
            return read;
        }

        /// <summary>读取并验证基地候选数据，不修改当前运行态。</summary>
        public SaveReadResult<BaseSaveData> PrepareBase()
        {
            var read = InspectBase();
            if (!read.IsValid) return read;
            if (!ValidateSections(read.Data.Sections, OfflineSaveDomain.Base, out var error))
            {
                read.Status = SaveFileStatus.Corrupted;
                read.Error = error;
            }
            if (read.IsValid && !InitializeCamp(read.Data.World, out error))
            { read.Status = SaveFileStatus.Corrupted; read.Error = error; }
            return read;
        }

        /// <summary>只初始化候选快照，实际落盘由原有新建/进入基地事务负责。</summary>
        private bool InitializeCamp(BaseWorldSaveData world, out string error)
        {
            if (world != null && world.CampLayoutInitialized) { error = null; return true; }
            var config = this.GetUtility<IRuntimeConfigProvider>();
            if (!config.TryGetBaseCampLayout(out var layout, out error)) return false;
            return BaseCampLayoutInitializer.TryInitialize(world, layout,
                id => config.TryGetTempBuilding(id, out var definition, out _) ? definition : null, out error);
        }
        /// <summary>删除副本候选文件；测试可注入一次性失败以验证提交后的回滚。</summary>
        public bool DeleteExpeditionFiles(out string error)
        {
            if (!EnsureOfflineStorage(out error)) return false;
            if (!string.IsNullOrEmpty(nextDeleteExpeditionFailureForTests))
            {
                error = nextDeleteExpeditionFailureForTests;
                nextDeleteExpeditionFailureForTests = null;
                return false;
            }

            return store.DeleteExpeditionFiles(out error);
        }

        /// <summary>仅供 EditMode 测试：使下一次副本文件删除失败，故障会被自动消费。</summary>
        public void FailNextDeleteExpeditionForTests(string error = "测试注入的副本删除失败")
        {
            nextDeleteExpeditionFailureForTests = string.IsNullOrWhiteSpace(error)
                ? "测试注入的副本删除失败"
                : error;
        }
        public bool CreateBase(out BaseSaveData data, out string error)
        {
            if (!EnsureOfflineStorage(out error)) { data = null; return false; }
            if (!TryCreateBaseCandidate(out data, out error)) return false;
            if (!store.CreateBase(data, out error)) { data = null; return false; }
            return true;
        }

        /// <summary>建立与新游戏一致的基地候选，但不执行任何文件写入。</summary>
        internal bool TryCreateBaseCandidate(out BaseSaveData data, out string error)
        {
            error = null;
            data = new BaseSaveData { Version = BaseSaveData.CurrentVersion, UpdatedAtUtc = clock.UtcNow.ToUniversalTime().ToString("O") };
            try
            {
                CaptureScopedSections(data.Sections, OfflineSaveDomain.Base);
                // 新游戏从默认基地时刻起步，不继承同会话内另一份存档的昼夜状态。
                if (data.Sections.ContainsKey(BaseDayNightModel.SaveSectionId))
                    data.Sections[BaseDayNightModel.SaveSectionId] = new BaseDayNightModel().Capture();
                if (!BasePreparationPolicy.Initialize(data, this.GetUtility<IRuntimeConfigProvider>(), true, out error)) { data=null; return false; }
                if (!InitializeCamp(data.World, out error)) { data = null; return false; }
                return true;
            }
            catch (Exception exception)
            {
                data = null;
                error = "建立基地候选失败：" + exception.Message;
                return false;
            }
        }

        public bool SaveBase(BaseSaveData data, out string error) => EnsureOfflineStorage(out error) && BasePreparationPolicy.Initialize(data,this.GetUtility<IRuntimeConfigProvider>(),false,out error) && store.SaveBase(data, out error);
        public bool SaveExpedition(ExpeditionSaveData data, out string error) => EnsureOfflineStorage(out error) && store.SaveExpedition(data, out error);

        /// <summary>把当前基地与共享分区捕获到指定 DTO，不执行文件写入或普通 Flush。</summary>
        public bool CaptureBaseSections(BaseSaveData data, out string error)
        {
            error = null;
            if (data == null)
            {
                error = "基地快照数据无效";
                return false;
            }

            // 死亡返程只捕获共享玩家结果；基地权威候选不能被远征中保留的旧Provider覆盖。
            var authoritativeBase = this.GetModel<SaveModel>().CurrentDomain == OfflineSaveDomain.Expedition
                ? data.Sections.Where(p=>providers.TryGetValue(p.Key,out var owner)&&((IScopedSaveSectionProvider)owner).Scope==SaveSectionScope.BaseOnly).ToDictionary(p=>p.Key,p=>p.Value?.DeepClone()) : null;
            data.Sections = new Dictionary<string, JToken>(StringComparer.Ordinal);
            CaptureScopedSections(data.Sections, OfflineSaveDomain.Base);
            if(authoritativeBase!=null)foreach(var section in authoritativeBase)data.Sections[section.Key]=section.Value;
            data.World ??= new BaseWorldSaveData();
            var mapScene = this.GetSystem<MapSceneSystem>();
            if (mapScene != null && string.Equals(mapScene.CurrentMapId, MapIds.Base, StringComparison.Ordinal))
            {
                mapScene.CaptureBaseWorld(data.World);
            }
            return true;
        }

        /// <summary>原子恢复基地分区并提交基地运行态，失败时回滚提供者状态。</summary>
        public bool CommitBaseRuntime(BaseSaveData data, out string error)
        {
            if (!EnsureOfflineStorage(out error)) return false;
            if(data==null){error="缺少基地数据";return false;}
            // 迁移前拒绝损坏的既有玩家分区，避免写出被迁移默认值掩盖的坏档。
            if(!ValidateSections(data.Sections,OfflineSaveDomain.Base,out error))return false;
            var migrating=data.Version==4;
            if(!BasePreparationPolicy.Initialize(data,this.GetUtility<IRuntimeConfigProvider>(),false,out error)||migrating&&!store.SaveBase(data,out error))return false;
            if (!ValidateSections(data.Sections, OfflineSaveDomain.Base, out error)) return false;
            if (!InitializeCamp(data.World, out error)) return false;
            var before = CaptureProviderState();
            try
            {
                RestoreSections(data.Sections, OfflineSaveDomain.Base);
                this.GetModel<SaveModel>().SetBase(data, true);
                dirty = false;
                return true;
            }
            catch (Exception e)
            {
                RestoreProviderState(before);
                error = $"提交基地运行态失败：{e.Message}";
                return false;
            }
        }

        /// <summary>原子恢复副本分区并提交副本运行态，失败时回滚提供者状态。</summary>
        public bool CommitExpeditionRuntime(ExpeditionSaveData data, out string error)
        {
            if (!EnsureOfflineStorage(out error)) return false;
            var persistedBase=InspectBase();
            if(!persistedBase.IsValid){error=persistedBase.Error??"缺少所属基地";return false;}
            if(!ValidateSections(persistedBase.Data.Sections,OfflineSaveDomain.Base,out error))return false;
            var baseMigration=persistedBase.Data.Version==4;
            if(!BasePreparationPolicy.Initialize(persistedBase.Data,this.GetUtility<IRuntimeConfigProvider>(),false,out error)||baseMigration&&!store.SaveBase(persistedBase.Data,out error))return false;
            if(data!=null&&data.Version==3)
            {
                var baseRead=InspectBase();
                if(!baseRead.IsValid){error=baseRead.Error??"缺少迁移基地";return false;}
                if(!BasePreparationPolicy.Initialize(baseRead.Data,this.GetUtility<IRuntimeConfigProvider>(),false,out error)||!store.SaveBase(baseRead.Data,out error))return false;
                var baseClock=baseRead.Data.Sections[BasePreparationModel.Section];
                var ledger=new ExpeditionPreparationModel();ledger.Bind(data.ExpeditionId,baseClock["totalMilliseconds"].Value<long>(),data.ElapsedGameMinutes);
                data.Sections[ExpeditionPreparationModel.Section]=ledger.Capture();data.Version=4;
                foreach(var loot in data.World.Loot){loot.OriginKnown=true;loot.OriginExpeditionId=null;loot.AcquiredAmount=0;}
                if(!store.SaveExpedition(data,out error))return false;
            }
            if (data == null || !ValidateSections(data.Sections, OfflineSaveDomain.Expedition, out error)) return false;
            var before = CaptureProviderState();
            try
            {
                // 继续远征只恢复基地专属权威，不把基地共享玩家快照覆盖到远征身上。
                foreach(var provider in providers.Values)
                    if(((IScopedSaveSectionProvider)provider).Scope==SaveSectionScope.BaseOnly)
                    {persistedBase.Data.Sections.TryGetValue(provider.SectionId,out var baseSection);provider.Restore(baseSection?.DeepClone());}
                RestoreSections(data.Sections, OfflineSaveDomain.Expedition);
                this.GetModel<SaveModel>().SetExpedition(data);
                dirty = false;
                return true;
            }
            catch (Exception e)
            {
                RestoreProviderState(before);
                error = $"提交副本运行态失败：{e.Message}";
                return false;
            }
        }

        public ExpeditionSaveData CaptureExpedition(string expeditionId, string mapId, DateTime utcNow, out string error)
        {
            error = null;
            if (!IsSafeExpeditionId(expeditionId) || string.IsNullOrWhiteSpace(mapId)) { error = "副本身份无效"; return null; }
            var preparation=this.GetModel<ExpeditionPreparationModel>();
            var baseClock=this.GetModel<BasePreparationModel>();
            if(!baseClock.Initialized){error="基地准备日历未初始化";return null;}
            var data = new ExpeditionSaveData { ExpeditionId = expeditionId, MapId = mapId, UpdatedAtUtc = utcNow.ToUniversalTime().ToString("O") };
            CaptureScopedSections(data.Sections, OfflineSaveDomain.Expedition);
            // 独立快照入口也必须生成有效账本，但不改变当前远征身份或清空库存来源。
            if(preparation.ExpeditionId!=expeditionId)
            {var candidateLedger=new ExpeditionPreparationModel();candidateLedger.Bind(expeditionId,baseClock.TotalMilliseconds,0);data.Sections[ExpeditionPreparationModel.Section]=candidateLedger.Capture();}
            else data.ElapsedGameMinutes=this.GetModel<ExpeditionModel>().ElapsedGameMinutes;
            return data;
        }

        /// <summary>
        /// 为候选或 Flush DTO 捕获完整副本分区；只有非空任务分区通过自身校验后才原子替换分区并提升任务版本标记。
        /// 捕获异常、未绑定或校验失败时保留调用前的 Sections 与两个版本标记，避免留下半升级 DTO。
        /// </summary>
        public bool CaptureExpeditionSections(ExpeditionSaveData data, out string error)
        {
            error = null;
            if (data == null || !IsSafeExpeditionId(data.ExpeditionId) || string.IsNullOrWhiteSpace(data.MapId))
            {
                error = "副本快照数据无效";
                return false;
            }
            var capturedSections = new Dictionary<string, JToken>(StringComparer.Ordinal);
            try
            {
                var preparation=this.GetModel<ExpeditionPreparationModel>();
                if(preparation.ExpeditionId!=data.ExpeditionId)
                {
                    var clock=this.GetModel<BasePreparationModel>();
                    if(!clock.Initialized){error="基地准备日历未初始化";return false;}
                    preparation.Bind(data.ExpeditionId,clock.TotalMilliseconds,data.ElapsedGameMinutes);
                    var bag=this.GetModel<PlayerBagModel>();
                    foreach(var container in new[]{bag.Container,bag.Pocket,this.GetModel<ClothingEquipmentModel>().Container})
                        foreach(var stack in container.Slots.Values)stack.ClearExplorationOrigin();
                }
                CaptureScopedSections(capturedSections, OfflineSaveDomain.Expedition);
            }
            catch (Exception exception)
            {
                error = $"副本分区捕获失败：{exception.Message}";
                return false;
            }

            if (!capturedSections.TryGetValue(ExpeditionTaskModel.SaveSectionId, out var taskSection) ||
                taskSection == null ||
                !providers.TryGetValue(ExpeditionTaskModel.SaveSectionId, out var taskProvider) ||
                !taskProvider.Validate(taskSection, out error))
            {
                if (string.IsNullOrWhiteSpace(error)) error = "撤离任务分区捕获失败";
                return false;
            }

            data.Sections = capturedSections;
            data.TaskSchemaVersion = 1;
            data.LegacySourceVersion = 0;
            data.World ??= new ExpeditionWorldSaveData();
            var mapScene = this.GetSystem<MapSceneSystem>();
            mapScene?.CaptureExpeditionWorld(data.World);
            return true;
        }

        /// <summary>在不依赖地图运行态的情况下幂等应用副本返回结算。</summary>
        public bool ApplyExpeditionSettlement(BaseSaveData baseData, ExpeditionSaveData expeditionData, bool defeated, out bool alreadySettled, out string error)
        {
            alreadySettled = false;
            error = null;
            if (baseData == null || expeditionData == null || !IsSafeExpeditionId(expeditionData.ExpeditionId))
            {
                error = "结算数据无效";
                return false;
            }
            var targetBase=baseData;baseData=baseData.Clone();
            // 旧基地可能在更新后仍处于远征域；结算候选先迁移营地，再与结算结果一起原子落盘。
            if (!InitializeCamp(baseData.World, out error)) return false;
            baseData.SettledExpeditionIds ??= new HashSet<string>(StringComparer.Ordinal);
            if (baseData.SettledExpeditionIds.Contains(expeditionData.ExpeditionId))
            {
                alreadySettled = true;
                return true;
            }
            baseData.Sections ??= new Dictionary<string, JToken>(StringComparer.Ordinal);
            var settledSections = new Dictionary<string, JToken>(StringComparer.Ordinal);
            if(!BasePreparationPolicy.Settle(baseData,expeditionData,defeated,this.GetUtility<IRuntimeConfigProvider>(),out error))return false;
            foreach (var section in expeditionData.Sections ?? new Dictionary<string, JToken>(StringComparer.Ordinal))
            {
                // 返回基地只结算已注册的共享玩家分区；副本玩法和未知分区永不跨域复制。
                if (!providers.TryGetValue(section.Key, out var provider) ||
                    !(provider is IScopedSaveSectionProvider scoped) ||
                    scoped.Scope != SaveSectionScope.SharedPlayer)
                {
                    continue;
                }
                if (defeated && string.Equals(section.Key, "another-day.player-bag", StringComparison.Ordinal)) continue;
                var copiedSection = section.Value?.DeepClone();
                // 衣物属于随身风险物品；中断后的失败结算也不能把死亡前衣物带回基地。
                if (section.Key == ClothingEquipmentModel.Section && (defeated || expeditionData.Status == ExpeditionStatus.Defeated))
                    copiedSection = new JObject { ["schemaVersion"] = 1, ["slots"] = new JArray() };
                if (!defeated && string.Equals(section.Key, "another-day.player-bag", StringComparison.Ordinal))
                {
                    if (!(copiedSection is JObject playerBag))
                    {
                        error = "副本玩家背包分区格式无效";
                        return false;
                    }

                    // 正常返回保留背包物品，但基地运行态必须解除副本风险状态。
                    playerBag["riskState"] = PlayerBagRiskState.BaseSafe.ToString();
                }

                settledSections[section.Key] = copiedSection;
            }

            if (string.Equals(expeditionData.MapId, MapIds.CivilisationValley, StringComparison.Ordinal))
            {
                var candidate = baseData.Clone();
                foreach (var section in settledSections) candidate.Sections[section.Key] = section.Value;
                if (!CivilisationSettlementPolicy.TryApply(candidate, expeditionData, defeated, this.GetUtility<IRuntimeConfigProvider>(), out error)) return false;
                settledSections = candidate.Sections;
            }

            foreach (var section in settledSections)
            {
                baseData.Sections[section.Key] = section.Value;
            }
            baseData.SettledExpeditionIds.Add(expeditionData.ExpeditionId);
            baseData.UpdatedAtUtc = clock.UtcNow.ToUniversalTime().ToString("O");
            targetBase.Version=baseData.Version;targetBase.Sections=baseData.Sections;targetBase.World=baseData.World;
            targetBase.SettledExpeditionIds=baseData.SettledExpeditionIds;targetBase.UpdatedAtUtc=baseData.UpdatedAtUtc;
            return true;
        }

        private static bool IsSafeExpeditionId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length == 32 && id.All(Uri.IsHexDigit);

        public SaveRuntimeSnapshot CaptureRuntimeSnapshot()
        {
            var model = this.GetModel<SaveModel>();
            var expedition = this.GetModel<ExpeditionModel>();
            var expeditionRuntime = expedition.IsActive
                ? expedition.CaptureSave(expedition.CurrentExpeditionId, clock.UtcNow)
                : null;
            return new SaveRuntimeSnapshot(
                model.CurrentDomain,
                model.CurrentBase,
                model.CurrentExpedition,
                expeditionRuntime,
                expedition.IsTaskManaged,
                CaptureProviderState(),
                dirty,
                dirtyAt);
        }

        /// <summary>
        /// 恢复进入流程前捕获的运行时快照；目标恢复失败时会尝试回滚到调用前稳定状态。
        /// </summary>
        public bool RestoreRuntimeSnapshot(SaveRuntimeSnapshot snapshot, out string error)
        {
            error = string.Empty;
            if (snapshot == null)
            {
                error = "缺少运行时存档快照";
                return false;
            }

            var beforeRestore = CaptureRuntimeSnapshot();
            try
            {
                RestoreProviderState(snapshot.ProviderData);
                RestoreModel(snapshot.Domain, snapshot.CurrentBase, snapshot.CurrentExpedition);
                RestoreExpeditionRuntime(snapshot);
                dirty = snapshot.Dirty;
                dirtyAt = snapshot.DirtyAt;
                return true;
            }
            catch (Exception e)
            {
                try
                {
                    RestoreProviderState(beforeRestore.ProviderData);
                    RestoreModel(beforeRestore.Domain, beforeRestore.CurrentBase, beforeRestore.CurrentExpedition);
                    RestoreExpeditionRuntime(beforeRestore);
                    dirty = beforeRestore.Dirty;
                    dirtyAt = beforeRestore.DirtyAt;
                }
                catch (Exception restoreException)
                {
                    error = $"恢复目标快照失败：{e.Message}；回滚旧运行时快照也失败：{restoreException.Message}";
                    return false;
                }

                error = $"恢复运行时存档快照失败：{e.Message}";
                return false;
            }
        }

        public void MarkDirty(string reason)
        {
            var model = this.GetModel<SaveModel>();
            // 存档域才是持久化资格；地图句柄在切图、EditMode 和恢复流程中会短暂为空，
            // 不能因此丢掉已经生效的基地/副本数据变更。
            if (model.CurrentDomain == OfflineSaveDomain.None)
            {
                return;
            }

            dirty = true;
            dirtyAt = clock.RealtimeSinceStartup;
        }

        /// <summary>
        /// 合并等待后的当前可变状态与最近成功提交值比较，只追加真实变化；不复制或写入地形。
        /// 分区与世界在同一事务中提交；失败保留旧 DTO 和待重试脏标记，成功后才推进基准。
        /// </summary>
        public bool Flush(out string error)
        {
            if (!EnsureOfflineStorage(out error)) return false;
            if (!dirty)
            {
                return true;
            }

            var model = this.GetModel<SaveModel>();
            if (model.CurrentDomain == OfflineSaveDomain.Expedition && model.CurrentExpedition != null)
            {
                var expedition = model.CurrentExpedition.CloneMutable();
                var runtime = this.GetModel<ExpeditionModel>();
                if (!runtime.IsActive ||
                    !string.Equals(runtime.CurrentExpeditionId, expedition.ExpeditionId, StringComparison.Ordinal) ||
                    !string.Equals(runtime.CurrentMapId, expedition.MapId, StringComparison.Ordinal))
                {
                    error = "活动副本与远征运行态身份不一致";
                    return false;
                }

                // 兼容目标字段沿用进入时的 DTO；只有会持续变化且参与现代一致性校验的顶层值在 Flush 时同步。
                expedition.ElapsedGameMinutes = runtime.ElapsedGameMinutes;
                expedition.ClockSchemaVersion = runtime.ClockSchemaVersion;
                expedition.StartMinuteOfDay = runtime.StartMinuteOfDay;
                expedition.Status = runtime.Status;
                expedition.UpdatedAtUtc = clock.UtcNow.ToUniversalTime().ToString("O");
                if (!CaptureExpeditionSections(expedition, out error) || !store.SaveExpeditionMutable(expedition, out error)) return false;
                model.SetExpeditionMutable(expedition);
                dirty = false;
                return true;
            }
            if (model.CurrentDomain == OfflineSaveDomain.Base && model.CurrentBase != null)
            {
                var baseData = model.CurrentBase.Clone();
                baseData.UpdatedAtUtc = clock.UtcNow.ToUniversalTime().ToString("O");
                if (!CaptureBaseSections(baseData, out error)) return false;
                if (!store.SaveBaseMutable(baseData, out error)) return false;
                model.SetBase(baseData, true);
                dirty = false;
                return true;
            }
            error = "当前没有可写入的活动离线数据域";
            return false;
        }

        private void Update()
        {
            if (dirty && clock.RealtimeSinceStartup - dirtyAt >= AutoSaveDelay)
            {
                Flush(out _);
            }
        }

        private Dictionary<string, JToken> CaptureProviderState()
        {
            return providers.ToDictionary(pair => pair.Key, pair => pair.Value.Capture()?.DeepClone(), StringComparer.Ordinal);
        }

        private void RestoreProviderState(Dictionary<string, JToken> captured)
        {
            foreach (var provider in providers.Values)
            {
                captured.TryGetValue(provider.SectionId, out var data);
                provider.Restore(data);
            }
            this.GetSystem<WeaponEquipmentSystem>().ValidateSourceAfterInventoryMutation();
            this.GetSystem<ClothingEquipmentSystem>().NotifyChanged();
        }

        private void CaptureScopedSections(Dictionary<string, JToken> target, OfflineSaveDomain domain)
        {
            target ??= new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var provider in providers.Values)
            {
                if (provider is IScopedSaveSectionProvider scoped)
                {
                    if (scoped.Scope == SaveSectionScope.BaseOnly && domain == OfflineSaveDomain.Expedition) continue;
                    if (scoped.Scope == SaveSectionScope.ExpeditionOnly && domain == OfflineSaveDomain.Base) continue;
                }
                target[provider.SectionId] = provider.Capture()?.DeepClone();
            }
        }

        private bool ValidateSections(Dictionary<string, JToken> sections, OfflineSaveDomain domain, out string error)
        {
            error = null;
            if(sections!=null&&sections.ContainsKey(WeaponEquipmentModel.Section))
            {
                var candidate=JObject.FromObject(sections);
                if(!HeldItemSaveMigration.TryMigrate(candidate,this.GetUtility<IRuntimeConfigProvider>(),out var migrated,out _,out error))return false;
                foreach(var property in migrated.Properties())sections[property.Name]=property.Value;
            }
            foreach (var provider in providers.Values)
            {
                var scoped = (IScopedSaveSectionProvider)provider;
                if (!BelongsToDomain(scoped.Scope, domain)) continue;
                JToken data = null;
                sections?.TryGetValue(provider.SectionId, out data);
                if (!provider.Validate(data, out error)) return false;
            }
            return true;
        }

        private void RestoreSections(Dictionary<string, JToken> sections, OfflineSaveDomain domain)
        {
            foreach (var provider in providers.Values)
            {
                var scoped = (IScopedSaveSectionProvider)provider;
                if (!BelongsToDomain(scoped.Scope, domain)) continue;
                JToken data = null;
                sections?.TryGetValue(provider.SectionId, out data);
                provider.Restore(data?.DeepClone());
            }
            this.GetSystem<WeaponEquipmentSystem>().ValidateSourceAfterInventoryMutation();
            this.GetSystem<ClothingEquipmentSystem>().NotifyChanged();
        }

        private static bool BelongsToDomain(SaveSectionScope scope, OfflineSaveDomain domain) =>
            scope == SaveSectionScope.SharedPlayer ||
            (scope == SaveSectionScope.BaseOnly && domain == OfflineSaveDomain.Base) ||
            (scope == SaveSectionScope.ExpeditionOnly && domain == OfflineSaveDomain.Expedition);

        private void RestoreModel(OfflineSaveDomain domain, BaseSaveData baseData, ExpeditionSaveData expeditionData)
        {
            var model = this.GetModel<SaveModel>();
            switch (domain)
            {
                case OfflineSaveDomain.Base:
                    model.SetBase(baseData, true);
                    break;
                case OfflineSaveDomain.Expedition:
                    model.SetBase(baseData, false);
                    model.SetExpedition(expeditionData);
                    break;
                default:
                    model.SetBase(baseData, false);
                    break;
            }
        }

        /// <summary>
        /// 恢复事务开始前的远征模型瞬时状态；空快照清空模型，活动快照保留未刷盘进度和任务托管标记。
        /// </summary>
        private void RestoreExpeditionRuntime(SaveRuntimeSnapshot snapshot)
        {
            var expedition = this.GetModel<ExpeditionModel>();
            if (snapshot.ExpeditionRuntime == null)
            {
                expedition.Clear();
                return;
            }

            if (!expedition.TryRestoreSave(snapshot.ExpeditionRuntime, out var error))
                throw new InvalidOperationException(error);
            expedition.RestoreTaskManagedState(snapshot.ExpeditionTaskManaged);
        }
    }
}
