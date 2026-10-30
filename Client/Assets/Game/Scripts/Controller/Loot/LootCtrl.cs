using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 按地图配置准备和释放掉落物表现，管理资源句柄及候选提交；不负责库存与掉落规则的权威状态。
    /// </summary>
    public sealed class LootCtrl : IController, IArcadeLootPresentationPort
    {
        public const float DisplayRange = 6f;
        private const float DisplayRangeSqr = DisplayRange * DisplayRange;
        private const string ArcadeVisualCachePrefix = "arcade.visual:";
        private readonly IArchitecture architecture;
        private readonly Dictionary<string, PrefabCacheEntry> committedPrefabs = new Dictionary<string, PrefabCacheEntry>();
        private readonly Dictionary<string, PrefabCacheEntry> temporaryPrefabs = new Dictionary<string, PrefabCacheEntry>();
        private readonly Dictionary<string, LoadingState> loadingPrefabs = new Dictionary<string, LoadingState>();
        private readonly Dictionary<int, ResourcePickupView> arcadeDrops = new Dictionary<int, ResourcePickupView>();
        private readonly Stack<ResourcePickupView> arcadeDropPool = new Stack<ResourcePickupView>();
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

        public async UniTask<MapControllerResult> PrepareMapAsync(MapPreparationContext context, CancellationToken cancellationToken)
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
            try
            {
                return await PrepareArcadeAsync(context, cancellationToken);
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
            if (!stagedPrepared || !stagedArcade)
                return MapControllerResult.Fail("没有街机候选表现");
            if (arcadeCandidateRoot == null || arcadeCandidateMapInstanceId <= 0)
                return FailPrepared("街机掉落提交失败：候选地图无效");
            ReleaseArcadePresentation();
            arcadeRoot = arcadeCandidateRoot;
            arcadeMapInstanceId = arcadeCandidateMapInstanceId;
            arcadeCandidateRoot = null;
            arcadeCandidateMapInstanceId = 0;
            stagedArcade = false;
            stagedPrepared = false;
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
        }

        public void ReleaseCurrentMap()
        {
            ReleaseArcadePresentation();
        }

        public void ReleaseAll()
        {
            released = true;
            ReleaseStaged();
            ReleaseArcadePresentation();
            ReleaseTemporaryPrefabs();
            ReleaseCommittedPrefabs();
            LifeCycle.UnregisterLateUpdate(QueueNameFacing);
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
        public bool TrySetDrop(int runtimeId, Vector3 position, string displayName, int amount, string visualPrefabAddress, out string error)
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
                if (!committedPrefabs.TryGetValue(ArcadeRunDefaults.DropPresentationLootConfigId, out var prefab) || prefab.Prefab == null || !ValidatePrefab(prefab.Prefab, out var prefabView, out error))
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
                if (trigger != null)
                    trigger.enabled = false;
                view.gameObject.SetActive(true);
                arcadeDrops.Add(runtimeId, view);
            }

            view.transform.position = position;
            view.transform.rotation = Quaternion.Euler(0f, (Time.realtimeSinceStartup * 90f + runtimeId * 17f) % 360f, 0f);
            view.SetArcadePresentation(visualPrefab, visualPrefabAddress);
            view.RefreshDisplay(displayName, Mathf.Max(1, amount));
            view.SetNameVisible(!string.IsNullOrWhiteSpace(displayName));
            return true;
        }

        /// <summary>只更新实例视觉透明度，不修改拾取范围或掉落权威状态。</summary>
        public void SetDropOpacity(int runtimeId, float opacity)
        {
            if (arcadeDrops.TryGetValue(runtimeId, out var view) && view != null)
                view.SetArcadeOpacity(opacity);
        }

        /// <summary>仅奖励房请求模型校准；普通地面掉落仍保留资源原始展示大小。</summary>
        public void FitDropVisual(int runtimeId, float maxSize)
        {
            if (arcadeDrops.TryGetValue(runtimeId, out var view) && view != null) view.FitArcadeVisual(maxSize);
        }

        /// <summary>把已裁决的空投进度交给对应掉落实例显示。</summary>
        public void SetDropDelivery(int runtimeId,bool airdrop,float remaining,float duration)
        {
            if(arcadeDrops.TryGetValue(runtimeId,out var view)&&view!=null)view.SetWeaponDelivery(airdrop,remaining,duration);
        }

        public void RemoveDrop(int runtimeId)
        {
            if (!arcadeDrops.TryGetValue(runtimeId, out var view))
                return;
            arcadeDrops.Remove(runtimeId);
            RecycleArcadeDrop(view);
        }

        public void ClearArcadeDrops()
        {
            var values = new List<ResourcePickupView>(arcadeDrops.Values);
            arcadeDrops.Clear();
            for (var i = 0; i < values.Count; i++)
                RecycleArcadeDrop(values[i]);
        }

        /// <summary>街机候选阶段预载通用掉落 Prefab 与配置引用的视觉 Prefab，不创建正式 Loot/Unit 领域实体。</summary>
        private async UniTask<MapControllerResult> PrepareArcadeAsync(MapPreparationContext context, CancellationToken cancellationToken)
        {
            var provider = this.GetUtility<IRuntimeConfigProvider>();
            string configError = null;
            if (provider == null || !provider.TryGetLoot(ArcadeRunDefaults.DropPresentationLootConfigId, out var config, out configError))
                return FailPrepared("街机掉落准备失败：" + (configError ?? "掉落配置不可用"));
            var prefabResult = await LoadLootPrefabAsync(config, cancellationToken);
            if (!prefabResult.Success)
                return FailPrepared(prefabResult.Error);
            if (!ValidatePrefab(prefabResult.Prefab, out _, out var prefabError))
                return FailPrepared(prefabError);
            var loadedAddresses = new HashSet<string>(StringComparer.Ordinal);
            if (provider.TryGetArcadeCatalog(out var catalog, out configError))
            {
                var visualAddresses = new List<string>();
                for (var index = 0; index < catalog.Drops.Count; index++)
                    visualAddresses.Add(catalog.Drops[index].VisualPrefabAddress);
                // 经验使用独立模型，但仍纳入同一地图资源准备与回滚事务。
                if (provider.TryGetSurvivor(out var survivor, out _))
                    visualAddresses.Add(survivor.ExperienceVisualPrefabAddress);
                foreach (var address in visualAddresses)
                {
                    if (string.IsNullOrWhiteSpace(address) || !loadedAddresses.Add(address))
                        continue;
                    var visualConfig = new LootConfigDefinition(ArcadeVisualCacheKey(address), address, config.ItemId, config.Amount);
                    var visualResult = await LoadLootPrefabAsync(visualConfig, cancellationToken);
                    if (!visualResult.Success)
                        return FailPrepared(visualResult.Error);
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

            if (prefab.GetComponentInChildren<ResourcePickupView>(true) != null || prefab.GetComponentsInChildren<Collider>(true).Length > 0)
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
                if (view != null)
                    return view;
            }

            return null;
        }

        private void RecycleArcadeDrop(ResourcePickupView view)
        {
            if (view == null)
                return;
            view.SetNameVisible(false);
            // 回池由Controller显式释放实例材质，不依赖禁用回调是否执行；View重复清理保持幂等。
            view.ClearArcadePresentation();
            view.gameObject.SetActive(false);
            arcadeDropPool.Push(view);
        }

        private void ReleaseArcadePresentation()
        {
            foreach (var view in arcadeDrops.Values)
                if (view != null)
                    this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
            arcadeDrops.Clear();
            while (arcadeDropPool.Count > 0)
            {
                var view = arcadeDropPool.Pop();
                if (view != null)
                    this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
            }

            arcadeRoot = null;
            arcadeMapInstanceId = 0;
        }

        /// <summary>名称朝向排到共享帧末，避免先于当前帧相机跟随和视角更新。</summary>
        private void QueueNameFacing() => LifeCycle.ScheduleFrameEnd(this, RefreshNameFacing);
        private void RefreshNameFacing()
        {
            if (!activeMapActive || arcadeDrops.Count == 0 || !this.GetSystem<PlayerCameraSystem>().TryGetScreenOrientation(out var forward, out var up))
                return;
            foreach (var view in arcadeDrops.Values)
                if (view != null && view.gameObject.activeInHierarchy)
                    view.FaceName(forward, up);
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
        private async UniTask<PrefabLoadResult> LoadLootPrefabAsync(LootConfigDefinition config, CancellationToken cancellationToken)
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
                temporaryPrefabs[config.Id] = new PrefabCacheEntry(committedByAddress.Prefab, ResourceLoadHandle.Empty, config);
                return PrefabLoadResult.Ok(committedByAddress.Prefab);
            }

            if (TryFindPrefabByAddress(temporaryPrefabs, config.PrefabAddress, out var temporaryByAddress))
            {
                temporaryPrefabs[config.Id] = new PrefabCacheEntry(temporaryByAddress.Prefab, ResourceLoadHandle.Empty, config);
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

        private static bool TryFindPrefabByAddress(Dictionary<string, PrefabCacheEntry> prefabs, string prefabAddress, out PrefabCacheEntry entry)
        {
            foreach (var pair in prefabs)
            {
                if (string.Equals(pair.Value.Config.PrefabAddress, prefabAddress, StringComparison.Ordinal) && pair.Value.Prefab != null)
                {
                    entry = pair.Value;
                    return true;
                }
            }

            entry = default;
            return false;
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

        private void ReleaseStaged()
        {
            stagedPrepared = false;
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
            public readonly UniTaskCompletionSource<GameObject> Completion = new UniTaskCompletionSource<GameObject>();
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
