using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game
{
    public sealed class OnlineWorldPlayerCtrl : IController, IDisposable
    {
        private sealed class Entry
        {
            public long UnitId;
            public long Revision;
            public OnlineWorldPlayerSnapshot State;
            public ResourceLoadHandle Handle;
            public OnlineRemotePlayerViewCtrl View;
            public bool Released;
        }

        private readonly IArchitecture architecture;
        private readonly Dictionary<long, Entry> active = new();
        private readonly Dictionary<long, OnlineWorldPlayerSnapshot> desired = new();
        private readonly Dictionary<long, int> loading = new();
        private readonly List<Entry> staged = new();
        private IUnRegister combatPresentationUnregister;

        private Transform activeRoot;
        private long activeMapInstanceId;
        private long activeWorldId;
        private string activeMapId;
        private int activeGeneration;
        private bool committed;

        private Transform candidateRoot;
        private long candidateMapInstanceId;
        private long candidateWorldId;
        private string candidateMapId;
        private int candidateGeneration;

        private int nextGeneration;
        private bool disposed;

        public OnlineWorldPlayerCtrl(IArchitecture architecture)
        {
            this.architecture = architecture;
            combatPresentationUnregister = architecture.RegisterEvent<OnlineWorldCombatPresentationEvent>(OnCombatPresentation);
            LifeCycle.RegisterUpdate(Tick);
        }

        public IArchitecture GetArchitecture() => architecture;

        public async UniTask<MapControllerResult> PrepareMapAsync(MapPreparationContext context, CancellationToken token)
        {
            if (context?.Mode != MapPreparationMode.OnlinePlayersOnly || context.Scene?.DynamicObjectRoot == null || context.OnlineWorld == null)
            {
                return MapControllerResult.Fail("远端玩家准备模式无效");
            }

            RollbackPreparedMap();
            candidateRoot = context.Scene.DynamicObjectRoot;
            candidateMapInstanceId = context.MapInstanceId;
            candidateWorldId = context.OnlineWorld.WorldId;
            candidateMapId = context.OnlineWorld.MapId;
            candidateGeneration = ++nextGeneration;
            int generation = candidateGeneration;
            Transform root = candidateRoot;

            foreach (OnlineWorldPlayerSnapshot player in context.OnlineWorld.Players.Where(x => x.UnitId != context.OnlineWorld.SelfUnitId))
            {
                Entry entry = await Load(player, root, token,
                    () => !disposed && candidateGeneration == generation && candidateRoot == root);
                if (entry == null)
                {
                    if (candidateGeneration == generation) RollbackPreparedMap();
                    return MapControllerResult.Fail("远端玩家 Prefab 加载失败");
                }
                if (candidateGeneration != generation || candidateRoot != root)
                {
                    Release(entry);
                    return MapControllerResult.Fail("远端玩家候选已失效");
                }
                staged.Add(entry);
            }

            return MapControllerResult.Ok();
        }

        public MapControllerResult CommitPreparedMap()
        {
            if (disposed || candidateRoot == null || candidateMapInstanceId <= 0 || candidateWorldId <= 0 ||
                string.IsNullOrWhiteSpace(candidateMapId) || candidateGeneration <= 0)
            {
                return MapControllerResult.Fail("远端玩家候选上下文无效");
            }
            if (committed || activeRoot != null || active.Count > 0 || desired.Count > 0 || loading.Count > 0)
            {
                return MapControllerResult.Fail("远端玩家旧活动地图尚未释放");
            }
            if (staged.Any(entry => entry == null || entry.Released || entry.View == null || !entry.View.IsValid ||
                                    entry.View.Instance.transform.parent != candidateRoot) ||
                staged.Select(entry => entry.UnitId).Distinct().Count() != staged.Count)
            {
                return MapControllerResult.Fail("远端玩家候选条目无效");
            }

            activeRoot = candidateRoot;
            activeMapInstanceId = candidateMapInstanceId;
            activeWorldId = candidateWorldId;
            activeMapId = candidateMapId;
            activeGeneration = candidateGeneration;
            foreach (Entry entry in staged)
            {
                entry.View.Instance.SetActive(true);
                active.Add(entry.UnitId, entry);
            }
            staged.Clear();
            ClearCandidateContext();
            committed = true;
            return MapControllerResult.Ok();
        }

        public void RollbackPreparedMap()
        {
            candidateGeneration = ++nextGeneration;
            foreach (Entry entry in staged) Release(entry);
            staged.Clear();
            ClearCandidateContext();
        }

        public void ReleaseCurrentMap()
        {
            activeGeneration = ++nextGeneration;
            committed = false;
            loading.Clear();
            desired.Clear();
            foreach (Entry entry in active.Values) Release(entry);
            active.Clear();
            activeRoot = null;
            activeMapInstanceId = 0;
            activeWorldId = 0;
            activeMapId = null;
        }

        public void ApplySnapshot(OnlineWorldSnapshot snapshot)
        {
            if (!committed || activeRoot == null || snapshot == null ||
                snapshot.WorldId != activeWorldId || snapshot.MapId != activeMapId)
            {
                return;
            }

            var wanted = new HashSet<long>(snapshot.Players
                .Where(player => player.UnitId != snapshot.SelfUnitId)
                .Select(player => player.UnitId));
            foreach (long unitId in active.Keys.Where(unitId => !wanted.Contains(unitId)).ToArray())
            {
                Release(active[unitId]);
                active.Remove(unitId);
            }
            foreach (long unitId in desired.Keys.Where(unitId => !wanted.Contains(unitId)).ToArray())
            {
                desired.Remove(unitId);
            }

            foreach (OnlineWorldPlayerSnapshot player in snapshot.Players)
            {
                if (player.UnitId == snapshot.SelfUnitId) continue;
                desired[player.UnitId] = player;
                if (active.TryGetValue(player.UnitId, out Entry entry))
                {
                    if (player.StateRevision >= entry.Revision && player.LifecycleRevision >= entry.State.LifecycleRevision)
                    {
                        int damage = entry.State.UnitId != 0 ? Mathf.Max(0, entry.State.CurrentHP - player.CurrentHP) : 0;
                        entry.Revision = player.StateRevision;
                        entry.State = player;
                        entry.View.SetTarget(player.LocalPosition, player.YawDegrees);
                        entry.View.SetAlive(!player.IsDead);
                        ApplyClothing(entry.View,player);
                        if (damage > 0) { entry.View.PlayHurtFeedback(); entry.View.ShowDamage(damage); }
                    }
                }
                else if (!loading.ContainsKey(player.UnitId))
                {
                    int generation = activeGeneration;
                    loading[player.UnitId] = generation;
                    LoadAndActivate(player, generation, activeRoot, activeWorldId, activeMapId).Forget();
                }
            }
        }

        private async UniTaskVoid LoadAndActivate(
            OnlineWorldPlayerSnapshot player,
            int generation,
            Transform root,
            long worldId,
            string mapId)
        {
            try
            {
                Entry entry = await Load(player, root, CancellationToken.None,
                    () => IsActiveContext(generation, root, worldId, mapId));
                if (entry == null) return;
                if (!IsActiveContext(generation, root, worldId, mapId) ||
                    !desired.TryGetValue(player.UnitId, out OnlineWorldPlayerSnapshot latest) ||
                    active.ContainsKey(player.UnitId))
                {
                    Release(entry);
                    return;
                }

                entry.Revision = latest.StateRevision;
                entry.State = latest;
                entry.View.Snap(latest.LocalPosition, latest.YawDegrees);
                entry.View.SetAlive(!latest.IsDead);
                ApplyClothing(entry.View,latest);
                entry.View.Instance.SetActive(true);
                active[player.UnitId] = entry;
            }
            finally
            {
                if (loading.TryGetValue(player.UnitId, out int ownerGeneration) && ownerGeneration == generation)
                {
                    loading.Remove(player.UnitId);
                }
            }
        }

        private async UniTask<Entry> Load(
            OnlineWorldPlayerSnapshot player,
            Transform parent,
            CancellationToken token,
            Func<bool> isContextValid)
        {
            IRuntimeConfigProvider configProvider = this.GetUtility<IRuntimeConfigProvider>();
            if (configProvider == null || !configProvider.TryGetPlayer(player.PlayerConfigId, out PlayerConfigDefinition config, out _))
            {
                return null;
            }

            var completion = new UniTaskCompletionSource<GameObject>();
            ResourceLoadHandle handle = ResourceLoader.LoadAssetAsync<GameObject>(config.PrefabAddress, prefab => completion.TrySetResult(prefab));
            GameObject prefab;
            try
            {
                using (token.Register(() => completion.TrySetCanceled(token)))
                {
                    prefab = await completion.Task;
                }
            }
            catch
            {
                if (handle.IsValid) ResourceLoader.Release(handle);
                return null;
            }

            if (prefab == null || disposed || token.IsCancellationRequested || parent == null || !isContextValid())
            {
                if (handle.IsValid) ResourceLoader.Release(handle);
                return null;
            }

            GameObject instance = Object.Instantiate(prefab, parent);
            if (instance == null)
            {
                if (handle.IsValid) ResourceLoader.Release(handle);
                return null;
            }
            if (disposed || parent == null || !isContextValid())
            {
                this.GetUtility<IViewInstanceProvider>()?.Release(instance);
                if (handle.IsValid) ResourceLoader.Release(handle);
                return null;
            }

            instance.SetActive(false);
            PlayerView view = instance.GetComponent<PlayerView>();
            if (view != null)
            {
                if (view.CharacterController != null) view.CharacterController.enabled = false;
                view.SetTiliRing(0, 1, false, false);
                view.HideActionProgress();
            }
            var controller = new OnlineRemotePlayerViewCtrl(instance, architecture);
            controller.Snap(player.LocalPosition, player.YawDegrees);
            controller.SetAlive(!player.IsDead);
            ApplyClothing(controller,player);
            return new Entry
            {
                UnitId = player.UnitId,
                Revision = player.StateRevision,
                State = player,
                Handle = handle,
                View = controller
            };
        }

        private void ApplyClothing(OnlineRemotePlayerViewCtrl view,OnlineWorldPlayerSnapshot state)
        {
            var config=this.GetUtility<IRuntimeConfigProvider>();
            view.Instance.GetComponent<PlayerView>()?.ClothingAppearance?.ApplyItems(config,state.ClothingItems());
        }

        private bool IsActiveContext(int generation, Transform root, long worldId, string mapId)
        {
            return !disposed && committed && activeGeneration == generation && activeRoot == root &&
                   activeWorldId == worldId && activeMapId == mapId && root != null;
        }

        private void Tick()
        {
            foreach (Entry entry in active.Values) entry.View.Tick(Time.deltaTime);
        }

        private void OnCombatPresentation(OnlineWorldCombatPresentationEvent evt)
        {
            if (!committed || evt.ActorUnitId == architecture.GetSystem<OnlineWorldSystem>()?.Snapshot.SelfUnitId)
            {
                return;
            }
            if (active.TryGetValue(evt.ActorUnitId, out Entry entry))
            {
                if(evt.ActionKind==OnlineWorldCombatActionKind.PlayerMelee)entry.View.PlayMeleeFeedback(evt.WeaponItemId);
                else if(evt.ActionKind==OnlineWorldCombatActionKind.PlayerProjectile)entry.View.PlayProjectileFeedback();
            }
        }

        private void ClearCandidateContext()
        {
            candidateRoot = null;
            candidateMapInstanceId = 0;
            candidateWorldId = 0;
            candidateMapId = null;
            candidateGeneration = 0;
        }

        private void Release(Entry entry)
        {
            if (entry == null || entry.Released) return;
            entry.Released = true;
            entry.View?.Dispose();
            if (entry.View?.Instance != null) this.GetUtility<IViewInstanceProvider>()?.Release(entry.View.Instance);
            if (entry.Handle.IsValid) ResourceLoader.Release(entry.Handle);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            combatPresentationUnregister?.UnRegister();
            combatPresentationUnregister = null;
            LifeCycle.UnregisterUpdate(Tick);
            RollbackPreparedMap();
            ReleaseCurrentMap();
        }
    }
}
