using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Game
{
    /// <summary>
    /// 按地图配置准备和释放掉落物表现，管理资源句柄及候选提交；不负责库存与掉落规则的权威状态。
    /// </summary>
    public sealed class LootCtrl : IController, ICraftOverflowDropPort, IPlayerInventoryDropPort, IBuildingRefundDropPort,
        IArcadeLootPresentationPort, ICivilisationHarvestDropPort
    {
        public const float DisplayRange = 6f;
        private const float DisplayRangeSqr = DisplayRange * DisplayRange;
        private const string ArcadeVisualCachePrefix = "arcade.visual:";

        private readonly IArchitecture architecture;
        private readonly List<LootRuntimeEntry> staged = new List<LootRuntimeEntry>();
        private readonly List<LootRuntimeEntry> active = new List<LootRuntimeEntry>();
        private readonly List<GameObject> unownedCandidateInstances = new List<GameObject>();
        private readonly Dictionary<string, PrefabCacheEntry> committedPrefabs =
            new Dictionary<string, PrefabCacheEntry>();
        private readonly Dictionary<string, PrefabCacheEntry> temporaryPrefabs =
            new Dictionary<string, PrefabCacheEntry>();
        private readonly Dictionary<string, LoadingState> loadingPrefabs =
            new Dictionary<string, LoadingState>();
        private readonly Dictionary<int, ResourcePickupView> arcadeDrops =
            new Dictionary<int, ResourcePickupView>();
        private readonly Stack<ResourcePickupView> arcadeDropPool = new Stack<ResourcePickupView>();
        private IUnRegister dropRequestedUnregister;
        private IUnRegister dropBatchRequestedUnregister;
        private IUnRegister collectedUnregister;
        private long activeMapInstanceId;
        private Transform activeDynamicObjectRoot;
        private long stagedMapInstanceId;
        private Transform stagedDynamicObjectRoot;
        private bool activeMapActive;
        private bool stagedPrepared;
        private bool stagedArcade;
        private Transform arcadeCandidateRoot;
        private long arcadeCandidateMapInstanceId;
        private Transform arcadeRoot;
        private long arcadeMapInstanceId;
        private bool released;

        public LootCtrl(IArchitecture architecture)
        {
            this.architecture = architecture;
            dropRequestedUnregister = architecture.RegisterEvent<LootDropRequestedEvent>(OnLootDropRequested);
            dropBatchRequestedUnregister = architecture.RegisterEvent<LootDropBatchRequestedEvent>(OnLootDropBatchRequested);
            collectedUnregister = architecture.RegisterEvent<LootCollectedEvent>(OnLootCollected);
            LifeCycle.RegisterUpdate(OnUpdate);
            LifeCycle.RegisterLateUpdate(QueueNameFacing);
        }

        public IArchitecture GetArchitecture()
        {
            return architecture;
        }

        public static bool IsWithinDisplayRange(Vector3 playerPosition, Vector3 lootPosition)
        {
            return (playerPosition - lootPosition).sqrMagnitude <= DisplayRangeSqr;
        }

        public async UniTask<MapControllerResult> PrepareMapAsync(
            MapPreparationContext context,
            CancellationToken cancellationToken)
        {
            if (stagedPrepared)
            {
                return MapControllerResult.Fail("掉落物准备失败：已有候选掉落物");
            }

            if (context?.Scene == null)
            {
                return MapControllerResult.Fail("掉落物准备失败：缺少 MapContext");
            }

            stagedPrepared = true;
            stagedMapInstanceId = context.MapInstanceId;
            stagedDynamicObjectRoot = context.Scene.DynamicObjectRoot;
            try
            {
                if (context.Mode == MapPreparationMode.ArcadePlayerOnly)
                    return await PrepareArcadeAsync(context, cancellationToken);

                var spawns = new List<MapSpawnDefinition>(context.Spawns);
                List<ExpeditionLootSaveData> restoredSnapshots = null;
                var snapshotLoot = context.ExpeditionSnapshot?.World?.Loot ?? context.BaseSnapshot?.World?.Loot;
                if (snapshotLoot != null && snapshotLoot.Count > 0)
                {
                    restoredSnapshots = new List<ExpeditionLootSaveData>();
                    spawns = new List<MapSpawnDefinition>();
                    foreach (var saved in snapshotLoot)
                    {
                        if (saved == null || saved.Pose == null || saved.RuntimeId <= 0 || string.IsNullOrWhiteSpace(saved.ConfigId))
                        {
                            return FailPrepared("掉落物恢复失败：快照实体无效");
                        }
                        var configId = saved.ConfigId;
                        var pos = new Vector3(saved.Pose.X, saved.Pose.Y, saved.Pose.Z);
                        var rot = Quaternion.Euler(0f, saved.Pose.RotationY, 0f);
                        var collected = saved.Collected;
                        if (collected) continue;
                        restoredSnapshots.Add(saved);
                        var local = context.Scene.DynamicObjectRoot.InverseTransformPoint(pos);
                        var localRot = Quaternion.Inverse(context.Scene.DynamicObjectRoot.rotation) * rot;
                        spawns.Add(new MapSpawnDefinition("snapshot-loot", context.MapId, MapSpawnObjectType.Loot, configId, local.x, local.y, local.z, localRot.eulerAngles.y));
                    }
                }
                for (var i = 0; i < spawns.Count; i++)
                {
                    var spawn = spawns[i];
                    if (spawn.ObjectType == MapSpawnObjectType.Loot)
                    {
                        if (!this.GetUtility<IRuntimeConfigProvider>()
                            .TryGetLoot(spawn.ConfigId, out var config, out var configError))
                        {
                            return FailPrepared("掉落物准备失败：" + configError);
                        }

                        var prefabResult = await LoadLootPrefabAsync(config, cancellationToken);
                        if (!prefabResult.Success)
                        {
                            return FailPrepared(prefabResult.Error);
                        }

                        if (!ValidatePrefab(prefabResult.Prefab, out var prefabView, out var prefabError))
                        {
                            return FailPrepared(prefabError);
                        }

                        var worldPosition = context.Scene.DynamicObjectRoot.TransformPoint(spawn.LocalPosition);
                        var worldRotation = context.Scene.DynamicObjectRoot.rotation * spawn.LocalRotation;
                        var view = this.GetUtility<IViewInstanceProvider>().Instantiate(
                            prefabView,
                            context.Scene.DynamicObjectRoot,
                            worldPosition,
                            worldRotation);
                        if (view == null)
                        {
                            return FailPrepared("掉落物准备失败：Prefab 实例化返回空");
                        }

                        var instanceObject = view.gameObject;
                        unownedCandidateInstances.Add(instanceObject);
                        LootViewCtrl viewCtrl = null;
                        try
                        {
                            var restored = restoredSnapshots != null ? restoredSnapshots[staged.Count] : null;
                            viewCtrl = CreateCtrl(view, context.MapInstanceId, restored?.RuntimeId);
                            var restoredConfig = restored == null ? config : new LootConfigDefinition(config.Id, config.PrefabAddress, config.ItemId, restored.RemainingAmount);
                            staged.Add(new LootRuntimeEntry(viewCtrl, restoredConfig, UnitSource.MapPlaced) { TorchRemainingSeconds=restored?.TorchRemainingSeconds,OriginKnown=restored?.OriginKnown??false,OriginExpeditionId=restored?.OriginExpeditionId,AcquiredAmount=restored?.AcquiredAmount??0 });
                            unownedCandidateInstances.Remove(instanceObject);
                        }
                        catch
                        {
                            if (viewCtrl != null)
                            {
                                unownedCandidateInstances.Remove(instanceObject);
                                viewCtrl.Dispose();
                            }

                            throw;
                        }

                        continue;
                    }

                    if (spawn.ObjectType == MapSpawnObjectType.Enemy &&
                        this.GetUtility<IRuntimeConfigProvider>()
                            .TryGetEnemy(spawn.ConfigId, out var enemyConfig, out _) &&
                        await PreloadEnemyLootsAsync(enemyConfig, cancellationToken) is { } preloadError)
                    {
                        return FailPrepared(preloadError);
                    }
                }

                var craftPreloadError = await PreloadCraftOutputLootsAsync(cancellationToken);
                if (craftPreloadError != null)
                {
                    return FailPrepared(craftPreloadError);
                }

                var playerDropPreloadError = await PreloadPlayerInventoryDropLootsAsync(cancellationToken);
                if (playerDropPreloadError != null)
                {
                    return FailPrepared(playerDropPreloadError);
                }

                var refundPreloadError = await PreloadTemporaryBuildingRefundLootsAsync(cancellationToken);
                if (refundPreloadError != null)
                {
                    return FailPrepared(refundPreloadError);
                }

                return MapControllerResult.Ok();
            }
            catch (OperationCanceledException)
            {
                return FailPrepared("掉落物准备失败：请求已取消");
            }
            catch (Exception ex)
            {
                return FailPrepared("掉落物准备失败：" + ex.Message);
            }
        }

        public MapControllerResult CommitPreparedMap()
        {
            if (stagedArcade)
            {
                if (arcadeCandidateRoot == null || arcadeCandidateMapInstanceId <= 0)
                    return FailPrepared("街机掉落提交失败：候选地图无效");
                ReleaseArcadePresentation();
                arcadeRoot = arcadeCandidateRoot;
                arcadeMapInstanceId = arcadeCandidateMapInstanceId;
                arcadeCandidateRoot = null;
                arcadeCandidateMapInstanceId = 0;
                stagedArcade = false;
                stagedPrepared = false;
                stagedMapInstanceId = 0;
                stagedDynamicObjectRoot = null;
                activeMapActive = true;
                PromoteTemporaryPrefabs();
                return MapControllerResult.Ok();
            }

            if (active.Count > 0)
            {
                return MapControllerResult.Fail("掉落物提交失败：已有活动掉落物");
            }

            if (!stagedPrepared)
            {
                return MapControllerResult.Fail("掉落物提交失败：没有候选掉落物");
            }

            var committedThisTurn = new List<LootRuntimeEntry>();
            for (var i = 0; i < staged.Count; i++)
            {
                var loot = staged[i];
                try
                {
                    if (!RegisterCommitted(loot, out var error))
                    {
                        RollbackCommit(committedThisTurn);
                        return FailPrepared(error);
                    }

                    if (loot.TorchRemainingSeconds.HasValue && !this.GetSystem<LootSystem>().TrySetTorchRemaining(loot.ViewCtrl.UnitId,loot.TorchRemainingSeconds))
                        throw new InvalidOperationException("火把掉落余量无效");
                    if(loot.OriginKnown&&!this.GetSystem<LootSystem>().TrySetOrigin(loot.ViewCtrl.UnitId,loot.OriginExpeditionId,loot.AcquiredAmount))throw new InvalidOperationException("掉落来源无效");
                    loot.ViewCtrl.RefreshDisplay(ResolveItemDisplayName(loot.Config.ItemId), loot.Config.Amount);
                    loot.ViewCtrl.SetVisible(true);
                    active.Add(loot);
                    committedThisTurn.Add(loot);
                }
                catch (Exception ex)
                {
                    RemoveDomains(loot, UnitRemovedReason.InvalidPresentation);
                    loot.ViewCtrl.Dispose();
                    RollbackCommit(committedThisTurn);
                    active.Remove(loot);
                    return FailPrepared("掉落物提交失败：" + ex.Message);
                }
            }

            activeMapInstanceId = stagedMapInstanceId;
            activeDynamicObjectRoot = stagedDynamicObjectRoot;

            staged.Clear();
            stagedPrepared = false;
            stagedMapInstanceId = 0;
            stagedDynamicObjectRoot = null;
            activeMapActive = true;
            PromoteTemporaryPrefabs();
            return MapControllerResult.Ok();
        }

        public void RollbackPreparedMap()
        {
            ReleaseStaged();
            ReleaseTemporaryPrefabs();
            arcadeCandidateRoot = null;
            arcadeCandidateMapInstanceId = 0;
            stagedArcade = false;
        }

        public void SetCurrentMapActive(bool activeState)
        {
            activeMapActive = activeState;
            foreach (var view in arcadeDrops.Values)
                if (view != null && view.gameObject.activeSelf != activeState)
                    view.gameObject.SetActive(activeState);
            for (var i = 0; i < active.Count; i++)
            {
                var loot = active[i];
                this.GetSystem<UnitSystem>().TrySetActive(loot.ViewCtrl.UnitId, activeState);
                loot.ViewCtrl.SetVisible(activeState && !loot.Collected);
                if (!activeState)
                {
                    loot.ViewCtrl.SetNameVisible(false);
                }
            }
        }

        public void ReleaseCurrentMap()
        {
            ReleaseCommitted();
            ReleaseArcadePresentation();
        }

        public void CaptureExpeditionWorld(ExpeditionWorldSaveData world)
        {
            if (world == null) return;
            world.Loot.Clear();
            for (var i = 0; i < active.Count; i++)
            {
                var entry = active[i];
                world.Loot.Add(new ExpeditionLootSaveData
                {
                    RuntimeId = entry.ViewCtrl.UnitId,
                    ConfigId = entry.Config.Id,
                    Pose = new ExpeditionPoseData { X = entry.Position.x, Y = entry.Position.y, Z = entry.Position.z, RotationY = entry.Rotation.eulerAngles.y },
                    RemainingAmount = entry.RemainingAmount,
                    OriginKnown=this.GetModel<LootModel>().TryGet(entry.ViewCtrl.UnitId,out var originState)&&originState.OriginKnown,
                    OriginExpeditionId=originState.OriginExpeditionId,AcquiredAmount=originState.AcquiredAmount,
                    TorchRemainingSeconds = this.GetModel<LootModel>().TryGet(entry.ViewCtrl.UnitId,out var fuelState) ? fuelState.TorchRemainingSeconds : entry.TorchRemainingSeconds,
                    Collected = entry.Collected
                });
            }
        }

        public void ReleaseAll()
        {
            released = true;
            ReleaseStaged();
            ReleaseCommitted();
            ReleaseArcadePresentation();
            ReleaseTemporaryPrefabs();
            ReleaseCommittedPrefabs();
            dropRequestedUnregister?.UnRegister();
            dropBatchRequestedUnregister?.UnRegister();
            collectedUnregister?.UnRegister();
            dropRequestedUnregister = null;
            dropBatchRequestedUnregister = null;
            collectedUnregister = null;
            LifeCycle.UnregisterUpdate(OnUpdate);
            LifeCycle.UnregisterLateUpdate(QueueNameFacing);
        }

        public bool TryCreateCraftOverflowDrop(
            int itemId,
            int amount,
            out CraftOverflowDropReceipt receipt,
            out string error)
        {
            receipt = CraftOverflowDropReceipt.Empty;
            if (itemId <= 0 || amount <= 0)
            {
                error = "制作溢出掉落的 ItemId 和数量必须为正数";
                return false;
            }

            if (activeMapInstanceId <= 0 || !activeMapActive || activeDynamicObjectRoot == null)
            {
                error = "制作溢出掉落需要活动地图";
                return false;
            }

            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetLootByItemId(itemId, out var config, out error))
            {
                return false;
            }

            if (!committedPrefabs.TryGetValue(config.Id, out var cache) ||
                cache.Prefab == null ||
                !ValidatePrefab(cache.Prefab, out var prefabView, out error))
            {
                error = string.IsNullOrEmpty(error)
                    ? $"制作溢出掉落 Prefab 不可用：{config.Id}"
                    : error;
                return false;
            }

            if (!this.GetUtility<IPlayerRuntimePortProvider>().TryGetCurrentPlayerPose(out var playerPose, out error) ||
                !playerPose.IsActive ||
                playerPose.MapInstanceId != activeMapInstanceId)
            {
                error = string.IsNullOrEmpty(error) ? "当前玩家姿态不可用" : error;
                return false;
            }

            if (!TryCreatePlayerNearbyRuntimeDrop(
                    config,
                    prefabView,
                    playerPose,
                    amount,
                    "制作溢出掉落",
                    out var created,
                    out error))
            {
                return false;
            }

            receipt = new CraftOverflowDropReceipt(created);
            return true;
        }

        public void RollbackCraftOverflowDrop(CraftOverflowDropReceipt receipt)
        {
            RollbackPlayerNearbyRuntimeDrops(receipt.LootUnitIds);
        }

        public bool TryCreatePlayerInventoryDrop(
            int itemId,
            int amount,
            out PlayerInventoryDropReceipt receipt,
            out string error)
        {
            receipt = PlayerInventoryDropReceipt.Empty;
            if (itemId <= 0 || amount <= 0)
            {
                error = "玩家背包主动丢弃的 ItemId 和数量必须为正数";
                return false;
            }

            if (activeMapInstanceId <= 0 || !activeMapActive || activeDynamicObjectRoot == null)
            {
                error = "玩家背包主动丢弃需要活动地图";
                return false;
            }

            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetLootByItemId(itemId, out var config, out error))
            {
                return false;
            }

            if (!committedPrefabs.TryGetValue(config.Id, out var cache) ||
                cache.Prefab == null ||
                !ValidatePrefab(cache.Prefab, out var prefabView, out error))
            {
                error = string.IsNullOrEmpty(error)
                    ? $"玩家背包主动丢弃 Prefab 不可用：{config.Id}"
                    : error;
                return false;
            }

            if (!this.GetUtility<IPlayerRuntimePortProvider>().TryGetCurrentPlayerPose(out var playerPose, out error) ||
                !playerPose.IsActive ||
                playerPose.MapInstanceId != activeMapInstanceId)
            {
                error = string.IsNullOrEmpty(error) ? "玩家背包主动丢弃的当前玩家姿态不可用" : error;
                return false;
            }

            if (!TryCreatePlayerNearbyRuntimeDrop(
                    config,
                    prefabView,
                    playerPose,
                    amount,
                    "玩家背包主动丢弃",
                    out var created,
                    out error))
            {
                return false;
            }

            receipt = new PlayerInventoryDropReceipt(created);
            return true;
        }

        public void RollbackPlayerInventoryDrop(PlayerInventoryDropReceipt receipt)
        {
            RollbackPlayerNearbyRuntimeDrops(receipt.LootUnitIds);
        }

        // 这里只复用制作溢出与玩家主动丢弃的玩家附近单堆实例、域注册和清理机制；
        // 两个公共入口仍保留各自校验、错误语义和收据，建筑返还不参与本次共享。
        private bool TryCreatePlayerNearbyRuntimeDrop(
            LootConfigDefinition config,
            ResourcePickupView prefabView,
            PlayerPoseSnapshot playerPose,
            int amount,
            string errorPrefix,
            out IReadOnlyList<int> createdUnitIds,
            out string error)
        {
            var forward = playerPose.Rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = Vector3.forward;
            }
            else
            {
                forward.Normalize();
            }

            var position = playerPose.Position + forward * 0.8f;
            var rotation = Quaternion.LookRotation(forward, Vector3.up);
            var created = new List<int>(1);
            createdUnitIds = created;
            LootRuntimeEntry entry = null;
            LootViewCtrl viewCtrl = null;
            GameObject instanceObject = null;
            try
            {
                var view = this.GetUtility<IViewInstanceProvider>().Instantiate(
                    prefabView,
                    activeDynamicObjectRoot,
                    position,
                    rotation);
                if (view == null)
                {
                    error = errorPrefix + "的 Prefab 实例化返回空";
                    return false;
                }

                instanceObject = view.gameObject;
                unownedCandidateInstances.Add(instanceObject);
                PlaceRuntimeDropOnGround(view, playerPose.Position.y);
                instanceObject.SetActive(false);
                viewCtrl = CreateCtrl(view, playerPose.MapInstanceId);
                entry = new LootRuntimeEntry(
                    viewCtrl,
                    new LootConfigDefinition(config.Id, config.PrefabAddress, config.ItemId, amount),
                    UnitSource.RuntimeSpawned);
                if (!RegisterCommitted(entry, out error))
                {
                    return false;
                }

                entry.ViewCtrl.RefreshDisplay(ResolveItemDisplayName(entry.Config.ItemId), entry.Config.Amount);
                entry.ViewCtrl.SetVisible(true);
                active.Add(entry);
                created.Add(entry.ViewCtrl.UnitId);
                unownedCandidateInstances.Remove(instanceObject);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = errorPrefix + "失败：" + ex.Message;
                return false;
            }
            finally
            {
                if (created.Count == 0)
                {
                    if (entry != null)
                    {
                        active.Remove(entry);
                        RemoveDomains(entry, UnitRemovedReason.InvalidPresentation);
                    }

                    if (viewCtrl != null)
                    {
                        if (instanceObject != null)
                        {
                            unownedCandidateInstances.Remove(instanceObject);
                        }

                        viewCtrl.Dispose();
                    }
                    else if (instanceObject != null)
                    {
                        unownedCandidateInstances.Remove(instanceObject);
                        this.GetUtility<IViewInstanceProvider>().Release(instanceObject);
                    }
                }
            }
        }

        private void RollbackPlayerNearbyRuntimeDrops(IReadOnlyList<int> lootUnitIds)
        {
            if (lootUnitIds == null)
            {
                return;
            }

            for (var i = lootUnitIds.Count - 1; i >= 0; i--)
            {
                var unitId = lootUnitIds[i];
                for (var j = active.Count - 1; j >= 0; j--)
                {
                    if (active[j].ViewCtrl.UnitId != unitId)
                    {
                        continue;
                    }

                    RemoveDomains(active[j], UnitRemovedReason.InvalidPresentation);
                    active[j].ViewCtrl.Dispose();
                    active.RemoveAt(j);
                    break;
                }
            }
        }

        public bool TryCreateBuildingRefundDrops(long mapInstanceId, Vector3 position, Quaternion rotation,
            IReadOnlyList<BuildingRefundDrop> drops, out BuildingRefundDropReceipt receipt, out string error)
        {
            return TryCreatePositionedDrops(mapInstanceId, position, rotation, drops, "建筑返还", out receipt, out error);
        }

        /// <summary>采集完成时同步创建产物，复用位置掉落事务；偏移到资源旁，避免与尚未隐藏的树干重叠。</summary>
        public bool TryCreateHarvestDrop(long mapInstanceId, Vector3 position, int itemId, int amount,
            out IReadOnlyList<int> lootUnitIds, out string error)
        {
            lootUnitIds = Array.Empty<int>();
            if (itemId <= 0 || amount <= 0) { error = "采集掉落物品和数量必须为正数"; return false; }
            var dropPosition = position + Vector3.right * .6f;
            // 按偏移后的真实地表落地，坡地不能直接沿用树根高度；无碰撞地面时保留资源点高度。
            dropPosition.y = ResolveDropGroundHeight(dropPosition);
            if (!TryCreatePositionedDrops(mapInstanceId, dropPosition, Quaternion.identity,
                new[] { new BuildingRefundDrop(itemId, amount, Vector3.zero) },
                "采集", out var receipt, out error)) return false;
            lootUnitIds = receipt.LootUnitIds;
            return true;
        }

        /// <summary>保存或采集提交失败时清理本次产物，既有地图物资不受影响。</summary>
        public void RollbackHarvestDrop(IReadOnlyList<int> lootUnitIds)
        {
            RollbackBuildingRefundDrops(new BuildingRefundDropReceipt(lootUnitIds));
        }

        /// <summary>使用已预加载的资源创建位置掉落；注册失败按逆序撤销，本函数从不标脏或保存。</summary>
        private bool TryCreatePositionedDrops(long mapInstanceId, Vector3 position, Quaternion rotation,
            IReadOnlyList<BuildingRefundDrop> drops, string source, out BuildingRefundDropReceipt receipt, out string error)
        {
            receipt = BuildingRefundDropReceipt.Empty;
            error = null;
            if (mapInstanceId <= 0 || mapInstanceId != activeMapInstanceId || !activeMapActive || activeDynamicObjectRoot == null)
            {
                error = source + "需要当前活动地图";
                return false;
            }

            var candidates = new List<(LootRuntimeEntry Entry, GameObject Instance)>();
            try
            {
                foreach (var drop in drops ?? Array.Empty<BuildingRefundDrop>())
                {
                    if (drop.ItemId <= 0 || drop.Amount <= 0) continue;
                    if (!this.GetUtility<IRuntimeConfigProvider>().TryGetLootByItemId(drop.ItemId, out var config, out error) ||
                        !committedPrefabs.TryGetValue(config.Id, out var cache) || !ValidatePrefab(cache.Prefab, out var prefabView, out error))
                    {
                        error = string.IsNullOrWhiteSpace(error) ? source + "掉落配置不可用" : error;
                        return false;
                    }

                    var view = this.GetUtility<IViewInstanceProvider>().Instantiate(prefabView, activeDynamicObjectRoot, position + rotation * drop.Offset, rotation);
                    if (view == null) { error = source + "掉落实例化失败"; return false; }
                    unownedCandidateInstances.Add(view.gameObject);
                    try
                    {
                        PlaceRuntimeDropOnGround(view, position.y);
                        view.gameObject.SetActive(false);
                        var entry = new LootRuntimeEntry(CreateCtrl(view, mapInstanceId),
                            new LootConfigDefinition(config.Id, config.PrefabAddress, config.ItemId, drop.Amount), UnitSource.RuntimeSpawned);
                        candidates.Add((entry, view.gameObject));
                    }
                    catch
                    {
                        // 尚未进入候选列表的实例必须就地释放，不能等地图卸载才清理失败产物。
                        unownedCandidateInstances.Remove(view.gameObject);
                        this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
                        throw;
                    }
                }

                foreach (var candidate in candidates)
                {
                    if (!RegisterCommitted(candidate.Entry, out error)) return false;
                    candidate.Entry.ViewCtrl.RefreshDisplay(ResolveItemDisplayName(candidate.Entry.Config.ItemId), candidate.Entry.Config.Amount);
                    candidate.Entry.ViewCtrl.SetVisible(true);
                    active.Add(candidate.Entry);
                    unownedCandidateInstances.Remove(candidate.Instance);
                }
                receipt = new BuildingRefundDropReceipt(candidates.ConvertAll(x => x.Entry.ViewCtrl.UnitId));
                return true;
            }
            catch (Exception exception)
            {
                error = source + "掉落失败：" + exception.Message;
                return false;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    for (var i = candidates.Count - 1; i >= 0; i--)
                    {
                        var candidate = candidates[i];
                        active.Remove(candidate.Entry);
                        RemoveDomains(candidate.Entry, UnitRemovedReason.InvalidPresentation);
                        unownedCandidateInstances.Remove(candidate.Instance);
                        candidate.Entry.ViewCtrl.Dispose();
                    }
                }
            }
        }

        public void RollbackBuildingRefundDrops(BuildingRefundDropReceipt receipt)
        {
            if (receipt.LootUnitIds == null)
            {
                return;
            }

            for (var i = receipt.LootUnitIds.Count - 1; i >= 0; i--)
            {
                var unitId = receipt.LootUnitIds[i];
                for (var j = active.Count - 1; j >= 0; j--)
                {
                    if (active[j].ViewCtrl.UnitId != unitId)
                    {
                        continue;
                    }

                    RemoveDomains(active[j], UnitRemovedReason.InvalidPresentation);
                    active[j].ViewCtrl.Dispose();
                    active.RemoveAt(j);
                    break;
                }
            }
        }

        private LootViewCtrl CreateCtrl(ResourcePickupView view, long mapInstanceId, int? restoredRuntimeId = null)
        {
            var unitId = restoredRuntimeId.GetValueOrDefault() > 0 ? restoredRuntimeId.Value : this.GetSystem<UnitSystem>().AllocateUnitId();
            return new LootViewCtrl(
                view,
                unitId,
                mapInstanceId,
                this.GetUtility<ViewCtrlRegistry>(),
                this.GetUtility<IViewInstanceProvider>(),
                true,
                OnLootTriggered, OnLootInteractionRequested, OnLootRangeExited,
                this.GetSystem<PlayerInteractionSystem>());
        }

        private bool RegisterCommitted(LootRuntimeEntry loot, out string error)
        {
            var snapshot = new UnitSnapshot(
                loot.ViewCtrl.UnitId,
                loot.ViewCtrl.MapInstanceId,
                UnitType.Loot,
                loot.Source,
                loot.Config.Id,
                loot.ViewCtrl.Position,
                loot.ViewCtrl.Rotation,
                UnitAuthorityMode.Local,
                0,
                true);

            if (!this.GetSystem<UnitSystem>().TryRegister(snapshot, out error))
            {
                return false;
            }

            if (!this.GetSystem<LootSystem>().TryRegisterLoot(loot.ViewCtrl.UnitId, loot.Config, out error))
            {
                this.GetSystem<UnitSystem>().Remove(loot.ViewCtrl.UnitId, UnitRemovedReason.InvalidPresentation);
                return false;
            }

            return true;
        }

        private void OnLootDropRequested(LootDropRequestedEvent evt)
        {
            if (activeMapInstanceId <= 0 ||
                evt.MapInstanceId != activeMapInstanceId ||
                !activeMapActive ||
                string.IsNullOrWhiteSpace(evt.LootConfigId) ||
                !committedPrefabs.TryGetValue(evt.LootConfigId, out var cache) ||
                cache.Prefab == null)
            {
                return;
            }

            if (!ValidatePrefab(cache.Prefab, out var prefabView, out _))
            {
                return;
            }

            var view = this.GetUtility<IViewInstanceProvider>().Instantiate(
                prefabView,
                activeDynamicObjectRoot,
                evt.Position,
                evt.Rotation);
            if (view == null)
            {
                return;
            }

            var instanceObject = view.gameObject;
            unownedCandidateInstances.Add(instanceObject);
            LootViewCtrl viewCtrl = null;
            LootRuntimeEntry entry = null;
            try
            {
                PlaceRuntimeDropOnGround(view, ResolveDropGroundHeight(evt.Position));
                instanceObject.SetActive(false);
                viewCtrl = CreateCtrl(view, evt.MapInstanceId);
                entry = new LootRuntimeEntry(viewCtrl, cache.Config, UnitSource.RuntimeSpawned);
                if (!RegisterCommitted(entry, out _))
                {
                    unownedCandidateInstances.Remove(instanceObject);
                    viewCtrl.Dispose();
                    return;
                }

                entry.ViewCtrl.RefreshDisplay(ResolveItemDisplayName(entry.Config.ItemId), entry.Config.Amount);
                entry.ViewCtrl.SetVisible(true);
                active.Add(entry);
                unownedCandidateInstances.Remove(instanceObject);
            }
            catch
            {
                if (entry != null)
                {
                    active.Remove(entry);
                    RemoveDomains(entry, UnitRemovedReason.InvalidPresentation);
                }

                if (viewCtrl != null)
                {
                    unownedCandidateInstances.Remove(instanceObject);
                    viewCtrl.Dispose();
                }
                else
                {
                    unownedCandidateInstances.Remove(instanceObject);
                    this.GetUtility<IViewInstanceProvider>().Release(instanceObject);
                }

                return;
            }
        }

        private void OnLootDropBatchRequested(LootDropBatchRequestedEvent evt)
        {
            const string stagePrefix = "批次动态掉落失败";
            if (activeMapInstanceId <= 0 ||
                evt.MapInstanceId != activeMapInstanceId ||
                !activeMapActive ||
                evt.Entries == null ||
                evt.Entries.Count == 0)
            {
                return;
            }

            var configs = new List<LootConfigDefinition>(evt.Entries.Count);
            for (var i = 0; i < evt.Entries.Count; i++)
            {
                var entry = evt.Entries[i];
                if (string.IsNullOrWhiteSpace(entry.LootConfigId) ||
                    entry.AmountOverride < 0 ||
                    !committedPrefabs.TryGetValue(entry.LootConfigId, out var cache) ||
                    cache.Prefab == null ||
                    !ValidatePrefab(cache.Prefab, out _, out _))
                {
                    Debug.LogError($"{stagePrefix}：批次校验失败：EnemyUnitId={evt.SourceEnemyUnitId} stage=validate");
                    return;
                }

                var amount = entry.AmountOverride > 0 ? entry.AmountOverride : cache.Config.Amount;
                if (amount <= 0)
                {
                    Debug.LogError($"{stagePrefix}：掉落数量无效：EnemyUnitId={evt.SourceEnemyUnitId} stage=amount");
                    return;
                }

                configs.Add(new LootConfigDefinition(cache.Config.Id, cache.Config.PrefabAddress, cache.Config.ItemId, amount));
            }

            var created = new List<LootRuntimeEntry>(evt.Entries.Count);
            try
            {
                for (var i = 0; i < evt.Entries.Count; i++)
                {
                    var entry = evt.Entries[i];
                    var cache = committedPrefabs[entry.LootConfigId];
                    ValidatePrefab(cache.Prefab, out var prefabView, out _);
                    var view = this.GetUtility<IViewInstanceProvider>().Instantiate(
                        prefabView,
                        activeDynamicObjectRoot,
                        evt.Position + entry.Offset,
                        evt.Rotation);
                    if (view == null)
                    {
                        throw new InvalidOperationException("Prefab 实例化返回空");
                    }

                    var instanceObject = view.gameObject;
                    unownedCandidateInstances.Add(instanceObject);
                    PlaceRuntimeDropOnGround(view, evt.Position.y);
                    instanceObject.SetActive(false);
                    var viewCtrl = CreateCtrl(view, evt.MapInstanceId);
                    unownedCandidateInstances.Remove(instanceObject);
                    created.Add(new LootRuntimeEntry(viewCtrl, configs[i], UnitSource.RuntimeSpawned));
                }

                var committed = new List<LootRuntimeEntry>(created.Count);
                for (var i = 0; i < created.Count; i++)
                {
                    var loot = created[i];
                    if (!RegisterCommitted(loot, out var error))
                    {
                        throw new InvalidOperationException(error);
                    }

                    committed.Add(loot);
                }

                for (var i = 0; i < committed.Count; i++)
                {
                    var loot = committed[i];
                    if (loot.TorchRemainingSeconds.HasValue && !this.GetSystem<LootSystem>().TrySetTorchRemaining(loot.ViewCtrl.UnitId,loot.TorchRemainingSeconds))
                        throw new InvalidOperationException("火把掉落余量无效");
                    if(loot.OriginKnown&&!this.GetSystem<LootSystem>().TrySetOrigin(loot.ViewCtrl.UnitId,loot.OriginExpeditionId,loot.AcquiredAmount))throw new InvalidOperationException("掉落来源无效");
                    loot.ViewCtrl.RefreshDisplay(ResolveItemDisplayName(loot.Config.ItemId), loot.Config.Amount);
                    loot.ViewCtrl.SetVisible(true);
                    active.Add(loot);
                }
            }
            catch (Exception ex)
            {
                for (var i = created.Count - 1; i >= 0; i--)
                {
                    active.Remove(created[i]);
                    RemoveDomains(created[i], UnitRemovedReason.InvalidPresentation);
                    created[i].ViewCtrl.Dispose();
                }

                ReleaseUnownedCandidateInstances();
                Debug.LogError($"{stagePrefix}：批次提交失败：EnemyUnitId={evt.SourceEnemyUnitId} stage=commit error={ex.Message}");
            }
        }

        /// <summary>
        /// 单次掉落提交前解析地表高度，避免把投射物飞行高度当成地面。
        /// 只接受静态场景碰撞体；无地表时保留请求高度，兼容无物理地面的地图准备和测试。
        /// </summary>
        private float ResolveDropGroundHeight(Vector3 position)
        {
            // 向上留出半米探测余量，再向下覆盖常见地形落差；不吸附到运行根下的玩家、敌人或掉落物。
            var hits = Physics.RaycastAll(position + Vector3.up * 0.5f, Vector3.down, 64f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var nearestDistance = float.PositiveInfinity;
            var groundY = position.y;
            foreach (var hit in hits)
            {
                if (hit.collider == null ||
                    (activeDynamicObjectRoot != null && hit.collider.transform.IsChildOf(activeDynamicObjectRoot)) ||
                    hit.distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = hit.distance;
                groundY = hit.point.y;
            }

            return groundY;
        }

        /// <summary>生成后、领域注册前按实际 Trigger 底部对齐地面，保持水平位置和旋转。</summary>
        private static void PlaceRuntimeDropOnGround(ResourcePickupView view, float groundY)
        {
            if (view == null)
            {
                throw new InvalidOperationException("动态掉落落地失败：ResourcePickupView 为空");
            }

            var trigger = view.GetComponent<Collider>();
            if (trigger == null || !trigger.enabled || !trigger.isTrigger)
            {
                throw new InvalidOperationException("动态掉落落地失败：缺少有效的根 Trigger Collider");
            }

            var offsetY = groundY - trigger.bounds.min.y;
            view.transform.position += Vector3.up * offsetY;
        }

        private void OnLootTriggered(int lootUnitId, Collider other)
        {
            var playerId = this.GetModel<PlayerModel>().PlayerUnitId;
            for (var i = 0; i < active.Count; i++)
            {
                var loot = active[i];
                if (loot.ViewCtrl.UnitId != lootUnitId ||
                    loot.Collected ||
                    !this.GetUtility<IPlayerRuntimePortProvider>()
                        .IsPlayerCollider(playerId, loot.ViewCtrl.MapInstanceId, other))
                {
                    continue;
                }

                this.GetSystem<PlayerInteractionSystem>().Focus(loot.ViewCtrl.InteractionTarget);
                if (this.GetSystem<PlayerInteractionSystem>().UsesCrosshairTargeting) return;
                this.GetSystem<LootSystem>().TryCollect(lootUnitId, playerId);
                return;
            }
        }

        /// <summary>第一、第三人称按键请求仍交给正式拾取 System 校验距离、身份和背包容量。</summary>
        private void OnLootInteractionRequested(int unitId) =>
            this.GetSystem<LootSystem>().TryCollect(unitId, this.GetModel<PlayerModel>().PlayerUnitId);

        private void OnLootRangeExited(int unitId, Collider other)
        {
            foreach (var loot in active)
                if (loot.ViewCtrl.UnitId == unitId && this.GetUtility<IPlayerRuntimePortProvider>()
                    .IsPlayerCollider(this.GetModel<PlayerModel>().PlayerUnitId, loot.ViewCtrl.MapInstanceId, other))
                {
                    this.GetSystem<PlayerInteractionSystem>().Blur(loot.ViewCtrl.InteractionTarget);
                    return;
                }
        }

        private void OnLootCollected(LootCollectedEvent evt)
        {
            for (var i = 0; i < active.Count; i++)
            {
                if (active[i].ViewCtrl.UnitId == evt.LootUnitId)
                {
                    active[i].RemainingAmount = evt.RemainingAmount;
                    if (!active[i].ViewCtrl.IsDisposed)
                    {
                        active[i].Position = active[i].ViewCtrl.Position;
                        active[i].Rotation = active[i].ViewCtrl.Rotation;
                    }
                    if (evt.RemainingAmount > 0)
                    {
                        active[i].ViewCtrl.RefreshDisplay(ResolveItemDisplayName(evt.ItemId), evt.RemainingAmount);
                    }
                    else
                    {
                        active[i].Collected = true;
                        active[i].ViewCtrl.SetCollected();
                        active[i].ViewCtrl.Dispose();
                        this.GetSystem<LootSystem>().TryRemoveLoot(evt.LootUnitId);
                    }

                    return;
                }
            }
        }

        /// <summary>
        /// 创建或刷新街机掉落表现。根 Trigger 会被关闭，实际拾取只由 ArcadeCombatSystem 的距离规则裁决。
        /// </summary>
        public bool TrySetDrop(int runtimeId, Vector3 position, string displayName, int amount, out string error)
        {
            return TrySetDrop(runtimeId, position, displayName, amount, null, out error);
        }

        /// <summary>
        /// 创建或刷新街机掉落表现；配置 Prefab 地址只选择模型，不参与拾取和奖励裁决。
        /// </summary>
        public bool TrySetDrop(
            int runtimeId,
            Vector3 position,
            string displayName,
            int amount,
            string visualPrefabAddress,
            out string error)
        {
            error = null;
            if (runtimeId <= 0 || arcadeRoot == null || arcadeMapInstanceId <= 0)
            {
                error = "街机掉落表现不可用";
                return false;
            }

            GameObject visualPrefab = null;
            if (!string.IsNullOrWhiteSpace(visualPrefabAddress))
            {
                var visualCacheKey = ArcadeVisualCacheKey(visualPrefabAddress);
                if (!committedPrefabs.TryGetValue(visualCacheKey, out var visualCache) || visualCache.Prefab == null)
                {
                    error = $"街机掉落视觉 Prefab 未预加载：{visualPrefabAddress}";
                    return false;
                }
                visualPrefab = visualCache.Prefab;
            }

            if (!arcadeDrops.TryGetValue(runtimeId, out var view) || view == null)
            {
                if (!committedPrefabs.TryGetValue(ArcadeRunDefaults.DropPresentationLootConfigId, out var prefab) ||
                    prefab.Prefab == null || !ValidatePrefab(prefab.Prefab, out var prefabView, out error))
                {
                    error = error ?? "街机掉落 Prefab 未预加载";
                    return false;
                }

                view = TakeArcadeDrop();
                if (view == null)
                    view = this.GetUtility<IViewInstanceProvider>().Instantiate(prefabView, arcadeRoot, position, Quaternion.identity);
                if (view == null)
                {
                    error = "街机掉落表现创建失败";
                    return false;
                }

                var trigger = view.GetComponent<Collider>();
                if (trigger != null) trigger.enabled = false;
                view.gameObject.SetActive(true);
                arcadeDrops.Add(runtimeId, view);
            }

            view.transform.position = position;
            view.transform.rotation = Quaternion.Euler(0f,
                (Time.realtimeSinceStartup * 90f + runtimeId * 17f) % 360f, 0f);
            view.SetArcadePresentation(visualPrefab, visualPrefabAddress);
            view.RefreshDisplay(displayName, Mathf.Max(1, amount));
            view.SetNameVisible(true);
            return true;
        }

        public void RemoveDrop(int runtimeId)
        {
            if (!arcadeDrops.TryGetValue(runtimeId, out var view)) return;
            arcadeDrops.Remove(runtimeId);
            RecycleArcadeDrop(view);
        }

        public void ClearArcadeDrops()
        {
            var values = new List<ResourcePickupView>(arcadeDrops.Values);
            arcadeDrops.Clear();
            for (var i = 0; i < values.Count; i++) RecycleArcadeDrop(values[i]);
        }

        /// <summary>街机候选阶段预载通用掉落 Prefab 与配置引用的视觉 Prefab，不创建正式 Loot/Unit 领域实体。</summary>
        private async UniTask<MapControllerResult> PrepareArcadeAsync(
            MapPreparationContext context,
            CancellationToken cancellationToken)
        {
            var provider = this.GetUtility<IRuntimeConfigProvider>();
            string configError = null;
            if (provider == null || !provider.TryGetLoot(ArcadeRunDefaults.DropPresentationLootConfigId,
                    out var config, out configError))
                return FailPrepared("街机掉落准备失败：" + (configError ?? "掉落配置不可用"));

            var prefabResult = await LoadLootPrefabAsync(config, cancellationToken);
            if (!prefabResult.Success) return FailPrepared(prefabResult.Error);
            if (!ValidatePrefab(prefabResult.Prefab, out _, out var prefabError))
                return FailPrepared(prefabError);

            var loadedAddresses = new HashSet<string>(StringComparer.Ordinal);
            if (provider.TryGetArcadeCatalog(out var catalog, out configError))
            {
                for (var index = 0; index < catalog.Drops.Count; index++)
                {
                    var address = catalog.Drops[index].VisualPrefabAddress;
                    if (string.IsNullOrWhiteSpace(address) || !loadedAddresses.Add(address)) continue;
                    var visualConfig = new LootConfigDefinition(ArcadeVisualCacheKey(address), address,
                        ItemIds.MetalParts, 1);
                    var visualResult = await LoadLootPrefabAsync(visualConfig, cancellationToken);
                    if (!visualResult.Success) return FailPrepared(visualResult.Error);
                    if (!ValidateArcadeVisualPrefab(visualResult.Prefab, address, out var visualError))
                        return FailPrepared(visualError);
                }
            }

            arcadeCandidateRoot = context.Scene.DynamicObjectRoot;
            arcadeCandidateMapInstanceId = context.MapInstanceId;
            stagedArcade = true;
            return MapControllerResult.Ok();
        }

        private static string ArcadeVisualCacheKey(string address) => ArcadeVisualCachePrefix + address;

        private static bool ValidateArcadeVisualPrefab(GameObject prefab, string address, out string error)
        {
            if (prefab == null || prefab.GetComponentsInChildren<Renderer>(true).Length == 0)
            {
                error = $"街机掉落视觉 Prefab 缺少 Renderer：{address}";
                return false;
            }
            if (prefab.GetComponentInChildren<ResourcePickupView>(true) != null ||
                prefab.GetComponentsInChildren<Collider>(true).Length > 0)
            {
                error = $"街机掉落视觉 Prefab 只能包含纯视觉对象：{address}";
                return false;
            }
            error = null;
            return true;
        }

        private ResourcePickupView TakeArcadeDrop()
        {
            while (arcadeDropPool.Count > 0)
            {
                var view = arcadeDropPool.Pop();
                if (view != null) return view;
            }
            return null;
        }

        private void RecycleArcadeDrop(ResourcePickupView view)
        {
            if (view == null) return;
            view.SetNameVisible(false);
            view.gameObject.SetActive(false);
            arcadeDropPool.Push(view);
        }

        private void ReleaseArcadePresentation()
        {
            foreach (var view in arcadeDrops.Values)
                if (view != null) this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
            arcadeDrops.Clear();
            while (arcadeDropPool.Count > 0)
            {
                var view = arcadeDropPool.Pop();
                if (view != null) this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
            }
            arcadeRoot = null;
            arcadeMapInstanceId = 0;
        }

        private string ResolveItemDisplayName(int itemId)
        {
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out var item, out _))
            {
                return string.Empty;
            }

            var localized = string.IsNullOrWhiteSpace(item.NameKey) ? string.Empty : Loc.T(item.NameKey);
            return string.IsNullOrWhiteSpace(localized) ? item.Name : localized;
        }

        private void OnUpdate()
        {
            if (!activeMapActive || (active.Count == 0 && arcadeDrops.Count == 0))
            {
                return;
            }

            if (active.Count == 0) return;

            var player = default(UnitSnapshot);
            var canShow = this.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing &&
                          this.GetSystem<UnitSystem>().TryGet(this.GetModel<PlayerModel>().PlayerUnitId, out player) &&
                          player.IsActive;
            var crosshairTargeting = this.GetSystem<PlayerInteractionSystem>().UsesCrosshairTargeting;
            for (var i = 0; i < active.Count; i++)
            {
                var loot = active[i];
                if (loot == null ||
                    loot.Collected ||
                    loot.ViewCtrl.IsDisposed ||
                    !loot.ViewCtrl.IsVisibleTargetActive)
                {
                    continue;
                }

                loot.ViewCtrl.SetInteractionAvailable(canShow &&
                    Vector3.Distance(player.Position, loot.ViewCtrl.Position) <= LootSystem.CollectRange &&
                    this.GetSystem<InventorySystem>().CanCollectWorldLootForCurrentPlayer());
                var visible = !crosshairTargeting && canShow && IsWithinDisplayRange(player.Position, loot.ViewCtrl.Position);
                loot.ViewCtrl.SetNameVisible(visible);
            }
        }

        /// <summary>名称朝向排到共享帧末，避免先于当前帧相机跟随和视角更新。</summary>
        private void QueueNameFacing() => LifeCycle.ScheduleFrameEnd(this, RefreshNameFacing);

        /// <summary>只更新当前地图可见掉落名称；地图释放或无活动相机时保持原表现。</summary>
        private void RefreshNameFacing()
        {
            if (!activeMapActive || (active.Count == 0 && arcadeDrops.Count == 0) ||
                !this.GetSystem<PlayerCameraSystem>().TryGetScreenOrientation(out var forward, out var up)) return;
            foreach (var view in arcadeDrops.Values)
                if (view != null && view.gameObject.activeInHierarchy) view.FaceName(forward, up);
            foreach (var loot in active)
                if (loot != null && !loot.Collected && !loot.ViewCtrl.IsDisposed && loot.ViewCtrl.IsVisibleTargetActive)
                    loot.ViewCtrl.FaceName(forward, up);
        }

        private MapControllerResult FailPrepared(string error)
        {
            ReleaseStaged();
            ReleaseTemporaryPrefabs();
            arcadeCandidateRoot = null;
            arcadeCandidateMapInstanceId = 0;
            stagedArcade = false;
            return MapControllerResult.Fail(error);
        }

        /// <summary>地图准备时复用或异步加载掉落 Prefab；失败释放句柄，取消后拒绝提交过期结果。</summary>
        private async UniTask<PrefabLoadResult> LoadLootPrefabAsync(
            LootConfigDefinition config,
            CancellationToken cancellationToken)
        {
            if (committedPrefabs.TryGetValue(config.Id, out var committed) && committed.Prefab != null)
            {
                return PrefabLoadResult.Ok(committed.Prefab);
            }

            if (temporaryPrefabs.TryGetValue(config.Id, out var temporary) && temporary.Prefab != null)
            {
                return PrefabLoadResult.Ok(temporary.Prefab);
            }

            if (TryFindPrefabByAddress(committedPrefabs, config.PrefabAddress, out var committedByAddress))
            {
                temporaryPrefabs[config.Id] = new PrefabCacheEntry(
                    committedByAddress.Prefab,
                    ResourceLoadHandle.Empty,
                    config);
                return PrefabLoadResult.Ok(committedByAddress.Prefab);
            }

            if (TryFindPrefabByAddress(temporaryPrefabs, config.PrefabAddress, out var temporaryByAddress))
            {
                temporaryPrefabs[config.Id] = new PrefabCacheEntry(
                    temporaryByAddress.Prefab,
                    ResourceLoadHandle.Empty,
                    config);
                return PrefabLoadResult.Ok(temporaryByAddress.Prefab);
            }

            if (loadingPrefabs.ContainsKey(config.PrefabAddress))
            {
                return PrefabLoadResult.Fail("掉落物准备失败：Loot Prefab 正在加载");
            }

            var state = new LoadingState();
            loadingPrefabs.Add(config.PrefabAddress, state);
            state.Handle = ResourceLoader.LoadAssetAsync<GameObject>(config.PrefabAddress, asset =>
            {
                state.CallbackArrived = true;
                if (state.CancelledBeforeCallback || released)
                {
                    ReleaseHandleOnce(state);
                    loadingPrefabs.Remove(config.PrefabAddress);
                    state.Completion.TrySetResult(null);
                    return;
                }

                state.Completion.TrySetResult(asset);
            });

            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                state.CancelledBeforeCallback = !state.CallbackArrived;
                state.Completion.TrySetCanceled(cancellationToken);
            });

            GameObject prefab;
            try
            {
                prefab = await state.Completion.Task;
            }
            catch (OperationCanceledException)
            {
                if (state.CallbackArrived)
                {
                    loadingPrefabs.Remove(config.PrefabAddress);
                }

                return PrefabLoadResult.Fail("掉落物准备失败：请求已取消");
            }

            loadingPrefabs.Remove(config.PrefabAddress);
            if (prefab == null)
            {
                ReleaseHandleOnce(state);
                return PrefabLoadResult.Fail("掉落物准备失败：Loot Prefab 加载失败");
            }

            if (cancellationToken.IsCancellationRequested || released)
            {
                ReleaseHandleOnce(state);
                return PrefabLoadResult.Fail("掉落物准备失败：请求已取消");
            }

            temporaryPrefabs[config.Id] = new PrefabCacheEntry(prefab, state.Handle, config);
            return PrefabLoadResult.Ok(prefab);
        }

        private static bool TryFindPrefabByAddress(
            Dictionary<string, PrefabCacheEntry> prefabs,
            string prefabAddress,
            out PrefabCacheEntry entry)
        {
            foreach (var pair in prefabs)
            {
                if (string.Equals(pair.Value.Config.PrefabAddress, prefabAddress, StringComparison.Ordinal) &&
                    pair.Value.Prefab != null)
                {
                    entry = pair.Value;
                    return true;
                }
            }

            entry = default;
            return false;
        }

        private async UniTask<string> PreloadEnemyLootsAsync(
            EnemyConfigDefinition enemyConfig,
            CancellationToken cancellationToken)
        {
            var ids = new List<string>(3);
            AddLootId(ids, enemyConfig.LootConfigId);
            AddLootId(ids, enemyConfig.GuaranteedWeaponLootConfigId);
            AddLootId(ids, enemyConfig.GuaranteedAmmoLootConfigId);

            for (var i = 0; i < ids.Count; i++)
            {
                if (!this.GetUtility<IRuntimeConfigProvider>().TryGetLoot(ids[i], out var dropConfig, out var lootError))
                {
                    return "掉落物准备失败：" + lootError;
                }

                var prefabResult = await LoadLootPrefabAsync(dropConfig, cancellationToken);
                if (!prefabResult.Success)
                {
                    return prefabResult.Error;
                }

                if (!ValidatePrefab(prefabResult.Prefab, out _, out var prefabError))
                {
                    return prefabError;
                }
            }

            return null;
        }

        private async UniTask<string> PreloadCraftOutputLootsAsync(CancellationToken cancellationToken)
        {
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetCraftRecipes(out var recipes, out var recipeError))
            {
                return "打造产物准备失败：" + recipeError;
            }

            var itemIds = new HashSet<int>();
            for (var i = 0; i < recipes.Count; i++)
            {
                if (!itemIds.Add(recipes[i].OutputItemId))
                {
                    continue;
                }

                if (!this.GetUtility<IRuntimeConfigProvider>().TryGetLootByItemId(recipes[i].OutputItemId, out var dropConfig, out var lootError))
                {
                    return "掉落物准备失败：" + lootError;
                }

                var prefabResult = await LoadLootPrefabAsync(dropConfig, cancellationToken);
                if (!prefabResult.Success)
                {
                    return prefabResult.Error;
                }

                if (!ValidatePrefab(prefabResult.Prefab, out _, out var prefabError))
                {
                    return prefabError;
                }
            }

            return null;
        }

        /// <summary>
        /// 玩家主动丢弃是同步事务，不能在点击后临时异步加载，因此地图准备时预加载所有正式物品的掉落资源。
        /// </summary>
        private async UniTask<string> PreloadPlayerInventoryDropLootsAsync(CancellationToken cancellationToken)
        {
            var configs = this.GetUtility<IRuntimeConfigProvider>();
            if (!configs.TryGetItems(out var items, out _))
            {
                // 该兼容分支只服务局部测试配置；正式 LubanRuntimeConfigProvider 始终提供完整物品表。
                return null;
            }

            var lootIds = new HashSet<string>();
            for (var i = 0; i < items.Count; i++)
            {
                if (!configs.TryGetLootByItemId(items[i].Id, out var loot, out var lootError))
                {
                    return "玩家背包丢弃准备失败：" + lootError;
                }

                if (!lootIds.Add(loot.Id))
                {
                    continue;
                }

                var prefabResult = await LoadLootPrefabAsync(loot, cancellationToken);
                if (!prefabResult.Success)
                {
                    return "玩家背包丢弃准备失败：" + prefabResult.Error;
                }

                if (!ValidatePrefab(prefabResult.Prefab, out _, out var prefabError))
                {
                    return "玩家背包丢弃准备失败：" + prefabError;
                }
            }

            return null;
        }

        /// <summary>
        /// 拆除是在已提交地图中同步执行的事务，不能临时异步加载资源。
        /// 因此在地图准备阶段把所有可建临建的返还物（含手动攻击剩余弹药）预加载进缓存。
        /// </summary>
        private async UniTask<string> PreloadTemporaryBuildingRefundLootsAsync(CancellationToken cancellationToken)
        {
            var configs = this.GetUtility<IRuntimeConfigProvider>();
            if (!configs.TryGetTempBuildings(out var buildings, out var buildingError))
            {
                // 旧地图/测试配置没有临建表时没有可返还的建筑；正式运行时的配置提供器会返回完整表。
                return null;
            }

            var itemIds = new HashSet<int>();
            for (var i = 0; i < buildings.Count; i++)
            {
                var building = buildings[i];
                for (var j = 0; j < building.Materials.Count; j++)
                {
                    if (building.Materials[j].ItemId > 0)
                    {
                        itemIds.Add(building.Materials[j].ItemId);
                    }
                }

                if (configs.TryGetTempBuildingManualAttack(building.Id, out var manualAttack) && manualAttack != null && manualAttack.AmmoItemId > 0)
                {
                    itemIds.Add(manualAttack.AmmoItemId);
                }
            }

            foreach (var itemId in itemIds)
            {
                if (!configs.TryGetLootByItemId(itemId, out var loot, out var lootError))
                {
                    return "建筑返还准备失败：" + lootError;
                }

                var prefabResult = await LoadLootPrefabAsync(loot, cancellationToken);
                if (!prefabResult.Success)
                {
                    return "建筑返还准备失败：" + prefabResult.Error;
                }

                if (!ValidatePrefab(prefabResult.Prefab, out _, out var prefabError))
                {
                    return "建筑返还准备失败：" + prefabError;
                }
            }

            return null;
        }

        private static void AddLootId(List<string> ids, string id)
        {
            if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        private static bool ValidatePrefab(GameObject prefab, out ResourcePickupView view, out string error)
        {
            view = null;
            if (prefab == null)
            {
                error = "掉落物准备失败：Prefab 为空";
                return false;
            }

            view = prefab.GetComponent<ResourcePickupView>();
            if (view == null)
            {
                error = "掉落物准备失败：Prefab 根缺少 ResourcePickupView";
                return false;
            }

            var trigger = prefab.GetComponent<Collider>();
            if (trigger == null)
            {
                error = "掉落物准备失败：Prefab 根缺少 Collider";
                return false;
            }

            if (!trigger.isTrigger)
            {
                error = "掉落物准备失败：Loot Collider 必须是 Trigger";
                return false;
            }

            error = null;
            return true;
        }

        private static void ReleaseHandleOnce(LoadingState state)
        {
            if (state.Handle.IsValid && !state.HandleReleased)
            {
                ResourceLoader.Release(state.Handle);
                state.HandleReleased = true;
            }
        }

        private void PromoteTemporaryPrefabs()
        {
            foreach (var pair in temporaryPrefabs)
            {
                committedPrefabs[pair.Key] = pair.Value;
            }

            temporaryPrefabs.Clear();
        }

        private void ReleaseTemporaryPrefabs()
        {
            foreach (var pair in temporaryPrefabs)
            {
                if (pair.Value.Handle.IsValid)
                {
                    ResourceLoader.Release(pair.Value.Handle);
                }
            }

            temporaryPrefabs.Clear();
        }

        private void ReleaseCommittedPrefabs()
        {
            foreach (var pair in committedPrefabs)
            {
                if (pair.Value.Handle.IsValid)
                {
                    ResourceLoader.Release(pair.Value.Handle);
                }
            }

            committedPrefabs.Clear();
        }

        private void RollbackCommit(List<LootRuntimeEntry> committedThisTurn)
        {
            for (var i = committedThisTurn.Count - 1; i >= 0; i--)
            {
                RemoveDomains(committedThisTurn[i], UnitRemovedReason.InvalidPresentation);
                committedThisTurn[i].ViewCtrl.Dispose();
                active.Remove(committedThisTurn[i]);
            }
        }

        private void ReleaseCommitted()
        {
            for (var i = active.Count - 1; i >= 0; i--)
            {
                RemoveDomains(active[i], UnitRemovedReason.MapInstanceReleased);
                active[i].ViewCtrl.Dispose();
            }

            active.Clear();
            activeMapInstanceId = 0;
            activeDynamicObjectRoot = null;
            activeMapActive = false;
        }

        private void ReleaseStaged()
        {
            for (var i = staged.Count - 1; i >= 0; i--)
            {
                staged[i].ViewCtrl.Dispose();
            }

            staged.Clear();
            ReleaseUnownedCandidateInstances();
            stagedPrepared = false;
            stagedMapInstanceId = 0;
            stagedDynamicObjectRoot = null;
            arcadeCandidateRoot = null;
            arcadeCandidateMapInstanceId = 0;
            stagedArcade = false;
        }

        private void ReleaseUnownedCandidateInstances()
        {
            for (var i = unownedCandidateInstances.Count - 1; i >= 0; i--)
            {
                this.GetUtility<IViewInstanceProvider>().Release(unownedCandidateInstances[i]);
            }

            unownedCandidateInstances.Clear();
        }

        private void RemoveDomains(LootRuntimeEntry loot, UnitRemovedReason reason)
        {
            this.GetSystem<LootSystem>().TryRemoveLoot(loot.ViewCtrl.UnitId);
            this.GetSystem<UnitSystem>().Remove(loot.ViewCtrl.UnitId, reason);
        }

        private sealed class LootRuntimeEntry
        {
            public LootRuntimeEntry(LootViewCtrl viewCtrl, LootConfigDefinition config, UnitSource source)
            {
                ViewCtrl = viewCtrl;
                Config = config;
                Source = source;
                RemainingAmount = config.Amount;
                Position = viewCtrl.Position;
                Rotation = viewCtrl.Rotation;
            }

            public LootViewCtrl ViewCtrl { get; }
            public LootConfigDefinition Config { get; }
            public UnitSource Source { get; }
            public bool Collected { get; set; }
            public int RemainingAmount { get; set; }
            public float? TorchRemainingSeconds { get; set; }
            public bool OriginKnown {get;set;}
            public string OriginExpeditionId {get;set;}
            public int AcquiredAmount {get;set;}
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; set; }
        }

        private readonly struct PrefabCacheEntry
        {
            public PrefabCacheEntry(GameObject prefab, ResourceLoadHandle handle, LootConfigDefinition config)
            {
                Prefab = prefab;
                Handle = handle;
                Config = config;
            }

            public GameObject Prefab { get; }
            public ResourceLoadHandle Handle { get; }
            public LootConfigDefinition Config { get; }
        }

        private sealed class LoadingState
        {
            public readonly UniTaskCompletionSource<GameObject> Completion =
                new UniTaskCompletionSource<GameObject>();
            public ResourceLoadHandle Handle;
            public bool HandleReleased;
            public bool CallbackArrived;
            public bool CancelledBeforeCallback;
        }

        private readonly struct PrefabLoadResult
        {
            private PrefabLoadResult(bool success, GameObject prefab, string error)
            {
                Success = success;
                Prefab = prefab;
                Error = error;
            }

            public bool Success { get; }
            public GameObject Prefab { get; }
            public string Error { get; }

            public static PrefabLoadResult Ok(GameObject prefab)
            {
                return new PrefabLoadResult(true, prefab, null);
            }

            public static PrefabLoadResult Fail(string error)
            {
                return new PrefabLoadResult(false, null, error);
            }
        }
    }
}
