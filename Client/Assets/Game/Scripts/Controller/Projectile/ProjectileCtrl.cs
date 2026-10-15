using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 管理投射物 Prefab 依赖、实例生命周期和碰撞转发，战斗结论留给 ProjectileSystem。
    /// </summary>
    public sealed partial class ProjectileCtrl : IController, IProjectileRuntimePortProvider, IArcadeProjectilePresentationPort, IOnlineWorldProjectilePresentationPort
    {
        private readonly IArchitecture architecture;
        private readonly Dictionary<int, ProjectileView> activeViews = new Dictionary<int, ProjectileView>();
        private readonly Dictionary<string, ProjectilePrefabCacheEntry> committedPrefabs = new Dictionary<string, ProjectilePrefabCacheEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, ProjectilePrefabCacheEntry> temporaryPrefabs = new Dictionary<string, ProjectilePrefabCacheEntry>(StringComparer.Ordinal);
        private readonly HashSet<int> arcadeProjectileIds = new HashSet<int>();
        /// <summary>按实际 Prefab 地址隔离复用，防止短枪弹与原箭形/特殊弹体串用根缩放。</summary>
        private readonly Dictionary<string, Stack<ProjectileView>> arcadeProjectilePools = new(StringComparer.Ordinal);
        private readonly Dictionary<int, string> arcadeProjectileAddresses = new();
        private MapContext currentScene;
        private long currentMapInstanceId;
        private MapContext candidateScene;
        private long candidateMapInstanceId;
        private bool stagedPrepared;
        private int preparationGeneration;
        private bool released;
        private string candidateArcadeProjectileAddress;
        private string arcadeProjectileAddress;
        /// <summary>原箭 Prefab 保留弓箭、敌方辨识度及已有特殊模型尺寸；随地图事务提交/释放。</summary>
        private string candidateArcadeArrowAddress;
        private string arcadeArrowAddress;

        public ProjectileCtrl(IArchitecture architecture)
        {
            this.architecture = architecture;
        }

        public IArchitecture GetArchitecture()
        {
            return architecture;
        }

        public async UniTask<MapControllerResult> PrepareMapAsync(
            MapPreparationContext context,
            CancellationToken cancellationToken)
        {
            if (stagedPrepared)
            {
                return MapControllerResult.Fail("投射物准备失败：已有候选表现");
            }

            if (context?.Scene == null)
            {
                return MapControllerResult.Fail("投射物准备失败：缺少 MapContext");
            }

            stagedPrepared = true;
            var preparation=++preparationGeneration;
            if(context.Mode==MapPreparationMode.OnlinePlayersOnly)
            {
                var indexResult=await PrepareOnlineIndex(context,cancellationToken,preparation);
                if(preparation!=preparationGeneration||released)return MapControllerResult.Fail("投射物准备已过期");
                if(!indexResult.Success)return FailPrepared(indexResult.Error);
            }
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetProjectiles(
                    out var configs,
                    out var configError))
            {
                return FailPrepared("投射物准备失败：" + configError);
            }

            var addresses = new HashSet<string>(StringComparer.Ordinal);
            foreach (var config in configs)
            {
                // 街机才需要枪械弹药；基础地图不能因全表预热隐式下载可选资源。
                if (context.Mode != MapPreparationMode.ArcadePlayerOnly && config.Id != "projectile.arrow.basic") continue;
                if(context.Mode==MapPreparationMode.OnlinePlayersOnly&&config.Id!="projectile.arrow.basic")continue;
                if (context.Mode == MapPreparationMode.ArcadePlayerOnly &&
                    string.Equals(config.Id, ArcadeRunDefaults.ProjectileConfigId, StringComparison.Ordinal))
                    candidateArcadeProjectileAddress = config.PrefabAddress;
                if (context.Mode == MapPreparationMode.ArcadePlayerOnly && config.Id == "projectile.arrow.basic")
                    candidateArcadeArrowAddress = config.PrefabAddress;
                if (string.IsNullOrWhiteSpace(config.PrefabAddress) ||
                    !addresses.Add(config.PrefabAddress))
                {
                    continue;
                }

                ResourceLoadHandle pendingHandle=default;
                var transferred=false;
                try
                {
                    var completion = new UniTaskCompletionSource<GameObject>();
                    var handle = ResourceLoader.LoadAssetAsync<GameObject>(
                        config.PrefabAddress,
                        prefab => completion.TrySetResult(prefab));
                    pendingHandle=handle;
                    var prefab = await completion.Task.AttachExternalCancellation(cancellationToken);
                    if(preparation!=preparationGeneration||released)return MapControllerResult.Fail("投射物准备已过期");
                    if (cancellationToken.IsCancellationRequested)
                    {

                        return FailPrepared("投射物准备失败：请求已取消");
                    }

                    if (!ValidatePrefab(config.PrefabAddress, prefab, out var error))
                    {

                        return FailPrepared(error);
                    }

                    temporaryPrefabs[config.PrefabAddress] = new ProjectilePrefabCacheEntry(prefab, handle);
                    transferred=true;
                }
                catch (OperationCanceledException)
                {
                    return FailPrepared("投射物准备失败：请求已取消");
                }
                finally {if(!transferred&&pendingHandle.IsValid)ResourceLoader.Release(pendingHandle);}
            }

            candidateScene = context.Scene;
            candidateMapInstanceId = context.MapInstanceId;
            if (context.Mode == MapPreparationMode.ArcadePlayerOnly && string.IsNullOrWhiteSpace(candidateArcadeProjectileAddress))
                return FailPrepared($"投射物准备失败：缺少 {ArcadeRunDefaults.ProjectileConfigId}");
            if (context.Mode == MapPreparationMode.ArcadePlayerOnly && string.IsNullOrWhiteSpace(candidateArcadeArrowAddress))
                return FailPrepared("投射物准备失败：缺少 projectile.arrow.basic");
            return MapControllerResult.Ok();
        }

        public MapControllerResult CommitPreparedMap()
        {
            if (!stagedPrepared)
            {
                return MapControllerResult.Fail("投射物提交失败：没有候选表现");
            }

            if (candidateScene == null ||
                candidateMapInstanceId <= 0 ||
                temporaryPrefabs.Count == 0)
            {
                return MapControllerResult.Fail("投射物提交失败：已有活动表现");
            }

            ReleaseOnlineState();
            if(candidateIndexJson!=null&&!WorldProjectileCollisionCatalog.Prepare(candidateIndexMapId,candidateIndexJson,out var indexError))return MapControllerResult.Fail(indexError);
            onlineIndexMapId=candidateIndexMapId;
            candidateIndexJson=null;candidateIndexMapId=null;
            ReleaseCommittedPrefabs();
            currentScene = candidateScene;
            currentMapInstanceId = candidateMapInstanceId;
            arcadeProjectileAddress = candidateArcadeProjectileAddress;
            arcadeArrowAddress = candidateArcadeArrowAddress;
            foreach (var pair in temporaryPrefabs)
            {
                committedPrefabs[pair.Key] = pair.Value;
            }

            temporaryPrefabs.Clear();
            candidateScene = null;
            candidateMapInstanceId = 0;
            candidateArcadeProjectileAddress = null;
            candidateArcadeArrowAddress = null;
            stagedPrepared = false;
            return MapControllerResult.Ok();
        }

        public void RollbackPreparedMap()
        {
            preparationGeneration++;
            ReleaseTemporaryPrefabs();
            candidateIndexJson=null;candidateIndexMapId=null;
            candidateScene = null;
            candidateMapInstanceId = 0;
            candidateArcadeProjectileAddress = null;
            candidateArcadeArrowAddress = null;
            stagedPrepared = false;
        }

        public void SetCurrentMapActive(bool active)
        {
            foreach(var view in onlineViews.Values)if(view!=null)view.gameObject.SetActive(active);
            foreach (var id in arcadeProjectileIds)
                if (activeViews.TryGetValue(id, out var arcadeView) && arcadeView != null)
                    arcadeView.gameObject.SetActive(active);
            if (!active && currentMapInstanceId > 0)
            {
                this.GetSystem<ProjectileSystem>().ClearForMap(currentMapInstanceId, ProjectileEndReason.MapDeactivated);
            }
        }

        public void ReleaseCurrentMap()
        {
            if (currentMapInstanceId > 0)
            {
                this.GetSystem<ProjectileSystem>().ClearForMap(currentMapInstanceId, ProjectileEndReason.MapReleased);
            }

            ReleaseOnlineState();
            ReleaseAllViews();
            ReleaseArcadeProjectilePool();
            ReleaseCommittedPrefabs();
            currentScene = null;
            currentMapInstanceId = 0;
            arcadeProjectileAddress = null;
            arcadeArrowAddress = null;
        }

        public void ReleaseAll()
        {
            released = true;
            preparationGeneration++;
            candidateIndexJson=null;candidateIndexMapId=null;
            if (currentMapInstanceId > 0)
            {
                this.GetSystem<ProjectileSystem>().ClearForMap(currentMapInstanceId, ProjectileEndReason.ArchitectureReleased);
            }

            ReleaseOnlineState();
            ReleaseAllViews();
            ReleaseArcadeProjectilePool();
            ReleaseTemporaryPrefabs();
            ReleaseCommittedPrefabs();
            currentScene = null;
            currentMapInstanceId = 0;
            candidateScene = null;
            candidateMapInstanceId = 0;
            stagedPrepared = false;
            candidateArcadeProjectileAddress = null;
            arcadeProjectileAddress = null;
            candidateArcadeArrowAddress = null;
            arcadeArrowAddress = null;
        }

        public bool TryCreateProjectile(
            int projectileRuntimeId,
            long mapInstanceId,
            Vector3 position,
            Vector3 direction,
            string prefabAddress,
            out string error)
        {
            if (released ||
                projectileRuntimeId <= 0 ||
                mapInstanceId != currentMapInstanceId ||
                currentScene == null)
            {
                error = "投射物表现不可用";
                return false;
            }

            if (string.IsNullOrWhiteSpace(prefabAddress))
            {
                error = "投射物 Prefab 地址为空";
                return false;
            }

            if (!committedPrefabs.TryGetValue(prefabAddress, out var entry) ||
                entry.Prefab == null)
            {
                error = $"投射物 Prefab 未预加载：{prefabAddress}";
                return false;
            }

            var prefabView = entry.Prefab.GetComponent<ProjectileView>();
            var view = this.GetUtility<IViewInstanceProvider>().Instantiate(
                prefabView,
                currentScene.DynamicObjectRoot,
                position,
                Quaternion.identity);
            if (view == null)
            {
                error = "投射物表现创建失败";
                return false;
            }

            view.Bind(projectileRuntimeId, OnProjectileCollision);
            view.ApplyPose(position, direction);
            activeViews[projectileRuntimeId] = view;
            error = null;
            return true;
        }

        public void MoveProjectile(int projectileRuntimeId, Vector3 position, Vector3 direction)
        {
            if (activeViews.TryGetValue(projectileRuntimeId, out var view) && view != null)
            {
                view.ApplyPose(position, direction);
            }
        }

        public void CollectMovementHits(int projectileRuntimeId, Vector3 from, Vector3 to, List<ProjectileMovementHit> results)
        {
            results.Clear();
            if (activeViews.TryGetValue(projectileRuntimeId, out var view) && view != null)
            {
                view.CollectMovementHits(from, to, results);
            }
        }

        public void ReleaseProjectile(int projectileRuntimeId)
        {
            if (!activeViews.TryGetValue(projectileRuntimeId, out var view))
            {
                return;
            }

            activeViews.Remove(projectileRuntimeId);
            if (view != null)
            {
                this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
            }
        }

        /// <summary>复用已预载 ProjectileView，仅同步姿态；街机命中仍由二维规则计算。</summary>
        public bool TrySetProjectile(int runtimeId, Vector3 position, Vector3 direction, out string error)
        {
            return TrySetProjectile(runtimeId, position, direction, null, false, out error);
        }

        /// <summary>普通枪使用短子弹；显式弓箭、特殊模型和敌方沿用原箭载体，仅切换表现，不参与二维命中。</summary>
        public bool TrySetProjectile(int runtimeId, Vector3 position, Vector3 direction, string visualResourceName, bool hostile,
            out string error)
        {
            error = null;
            if (runtimeId <= 0 || currentScene == null || currentMapInstanceId <= 0 ||
                string.IsNullOrWhiteSpace(arcadeProjectileAddress))
            {
                error = "街机投射物表现不可用";
                return false;
            }

            // 特殊模型原本依赖箭 Prefab 的根缩放；保留载体，避免默认变短时连带缩小火箭等模型。
            var address = hostile || !string.IsNullOrWhiteSpace(visualResourceName)
                ? arcadeArrowAddress : arcadeProjectileAddress;
            if (!committedPrefabs.TryGetValue(address ?? string.Empty, out var prefab) || prefab.Prefab == null)
            {
                error = $"街机投射物 Prefab 未预加载：{address}";
                return false;
            }

            if (activeViews.TryGetValue(runtimeId, out var existing) && existing != null &&
                arcadeProjectileAddresses.TryGetValue(runtimeId, out var existingAddress) && existingAddress == address)
            {
                existing.SetArcadePresentation(visualResourceName, hostile);
                existing.ApplyPose(position, direction);
                return true;
            }

            // 同一表现 ID 改用另一种载体时先隐藏旧实例；每次取出仍完整覆盖所有模型开关。
            RemoveProjectile(runtimeId);
            if (!arcadeProjectilePools.TryGetValue(address, out var pool))
                arcadeProjectilePools[address] = pool = new Stack<ProjectileView>();

            ProjectileView view = null;
            while (pool.Count > 0 && view == null) view = pool.Pop();
            if (view == null)
            {
                var prefabView = prefab.Prefab.GetComponent<ProjectileView>();
                view = this.GetUtility<IViewInstanceProvider>().Instantiate(
                    prefabView, currentScene.DynamicObjectRoot, position, Quaternion.identity);
            }
            if (view == null)
            {
                error = "街机投射物表现创建失败";
                return false;
            }

            view.transform.SetParent(currentScene.DynamicObjectRoot, true);
            view.gameObject.SetActive(true);
            view.Bind(runtimeId, null);
            view.SetArcadePresentation(visualResourceName, hostile);
            view.ApplyPose(position, direction);
            activeViews[runtimeId] = view;
            arcadeProjectileAddresses[runtimeId] = address;
            arcadeProjectileIds.Add(runtimeId);
            return true;
        }

        public void RemoveProjectile(int runtimeId)
        {
            if (!arcadeProjectileIds.Remove(runtimeId) || !activeViews.TryGetValue(runtimeId, out var view)) return;
            activeViews.Remove(runtimeId);
            arcadeProjectileAddresses.Remove(runtimeId, out var address);
            if (view == null) return;
            view.Bind(0, null);
            view.gameObject.SetActive(false);
            if (address != null && arcadeProjectilePools.TryGetValue(address, out var pool)) pool.Push(view);
            else this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
        }

        public void ClearArcadeProjectiles()
        {
            var ids = new List<int>(arcadeProjectileIds);
            for (var i = 0; i < ids.Count; i++) RemoveProjectile(ids[i]);
        }

        private static bool ValidatePrefab(string prefabAddress, GameObject prefab, out string error)
        {
            if (prefab == null)
            {
                error = $"投射物准备失败：Prefab 为空：{prefabAddress}";
                return false;
            }

            var view = prefab.GetComponent<ProjectileView>();
            if (view == null)
            {
                error = $"投射物准备失败：Prefab 根缺少 ProjectileView：{prefabAddress}";
                return false;
            }

            return view.Validate(out error);
        }

        private MapControllerResult FailPrepared(string error)
        {
            RollbackPreparedMap();
            return MapControllerResult.Fail(error);
        }

        private void OnProjectileCollision(int projectileRuntimeId, Collider other)
        {
            this.GetSystem<ProjectileSystem>().HandleCollision(projectileRuntimeId, other);
        }

        private void ReleaseAllViews()
        {
            var ids = new List<int>(activeViews.Keys);
            for (var i = 0; i < ids.Count; i++)
            {
                ReleaseProjectile(ids[i]);
            }
            arcadeProjectileIds.Clear();
            arcadeProjectileAddresses.Clear();
        }

        private void ReleaseArcadeProjectilePool()
        {
            foreach (var pool in arcadeProjectilePools.Values)
            {
                while (pool.Count > 0)
                {
                    var view = pool.Pop();
                    if (view != null) this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
                }
            }
            arcadeProjectilePools.Clear();
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

        private readonly struct ProjectilePrefabCacheEntry
        {
            public ProjectilePrefabCacheEntry(GameObject prefab, ResourceLoadHandle handle)
            {
                Prefab = prefab;
                Handle = handle;
            }

            public GameObject Prefab { get; }
            public ResourceLoadHandle Handle { get; }
        }
    }
}
