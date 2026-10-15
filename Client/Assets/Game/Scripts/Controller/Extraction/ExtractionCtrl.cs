using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 负责地图撤离点的准备、提交/回滚、活动表现、事件订阅及对称释放。
    /// 撤离资格与持续动作分别归 ExtractionSystem、PlayerActionSystem；本 Controller 只保存地图实例和表现所需的会话临时状态。
    /// </summary>
    public sealed class ExtractionCtrl : IController
    {
        private readonly IArchitecture architecture;
        private readonly List<ExtractionRuntimeEntry> staged = new List<ExtractionRuntimeEntry>();
        private readonly List<ExtractionRuntimeEntry> active = new List<ExtractionRuntimeEntry>();
        private readonly List<GameObject> unownedCandidateInstances = new List<GameObject>();
        private readonly Dictionary<string, PrefabCacheEntry> committedPrefabs =
            new Dictionary<string, PrefabCacheEntry>();
        private readonly Dictionary<string, PrefabCacheEntry> temporaryPrefabs =
            new Dictionary<string, PrefabCacheEntry>();
        private readonly Dictionary<string, LoadingState> loadingPrefabs =
            new Dictionary<string, LoadingState>();

        private IUnRegister expeditionChangedUnregister;
        private IUnRegister countdownStartedUnregister;
        private IUnRegister countdownCanceledUnregister;
        private IUnRegister actionChangedUnregister;
        private IUnRegister returnStartedUnregister;
        private IUnRegister returnFailedUnregister;
        private IUnRegister languageChangedUnregister;
        private long activeMapInstanceId;
        private long stagedMapInstanceId;
        private bool activeMapActive;
        private bool stagedPrepared;
        private bool released;
        private bool lateUpdateRegistered;

        public ExtractionCtrl(IArchitecture architecture)
        {
            this.architecture = architecture;
            // 构造时注册的架构事件与 LateUpdate 只在本 Controller 生命周期内有效，
            // ReleaseAll 必须幂等并逐项成对解除，防止地图切换后继续驱动已释放表现。
            expeditionChangedUnregister = architecture.RegisterEvent<ExpeditionChangedEvent>(OnExpeditionChanged);
            countdownStartedUnregister = architecture.RegisterEvent<ExtractionCountdownStartedEvent>(OnCountdownStarted);
            countdownCanceledUnregister = architecture.RegisterEvent<ExtractionCountdownCanceledEvent>(OnCountdownCanceled);
            actionChangedUnregister = architecture.RegisterEvent<PlayerActionChangedEvent>(OnPlayerActionChanged);
            returnStartedUnregister = architecture.RegisterEvent<ExtractionReturnStartedEvent>(OnReturnStarted);
            returnFailedUnregister = architecture.RegisterEvent<ExtractionReturnFailedEvent>(OnReturnFailed);
            languageChangedUnregister = architecture.RegisterEvent<LanguageChangedEvent>(_ => RefreshPromptTexts());
            LifeCycle.RegisterLateUpdate(OnLateUpdate);
            lateUpdateRegistered = true;
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
                return MapControllerResult.Fail("撤离点准备失败：已有候选撤离点");
            }

            if (context?.Scene == null)
            {
                return MapControllerResult.Fail("撤离点准备失败：缺少 MapContext");
            }

            stagedPrepared = true;
            stagedMapInstanceId = context.MapInstanceId;

            try
            {
                var spawns = context.Spawns;
                for (var i = 0; i < spawns.Count; i++)
                {
                    var spawn = spawns[i];
                    if (spawn.ObjectType != MapSpawnObjectType.Extraction)
                    {
                        continue;
                    }

                    if (!this.GetUtility<IRuntimeConfigProvider>()
                            .TryGetExtraction(spawn.ConfigId, out var config, out var configError))
                    {
                        return FailPrepared("撤离点准备失败：" + configError);
                    }

                    if (string.IsNullOrWhiteSpace(config.Id) ||
                        string.IsNullOrWhiteSpace(config.PrefabAddress))
                    {
                        return FailPrepared("撤离点准备失败：撤离点配置无效");
                    }

                    var prefabResult = await LoadExtractionPrefabAsync(config, cancellationToken);
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
                        return FailPrepared("撤离点准备失败：Prefab 实例化返回空");
                    }

                    var instanceObject = view.gameObject;
                    unownedCandidateInstances.Add(instanceObject);
                    ExtractionViewCtrl viewCtrl = null;
                    try
                    {
                        instanceObject.SetActive(false);
                        viewCtrl = CreateCtrl(view, context.MapInstanceId);
                        staged.Add(new ExtractionRuntimeEntry(viewCtrl, config));
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
                }

                return MapControllerResult.Ok();
            }
            catch (OperationCanceledException)
            {
                return FailPrepared("撤离点准备失败：请求已取消");
            }
            catch (Exception ex)
            {
                return FailPrepared("撤离点准备失败：" + ex.Message);
            }
        }

        public MapControllerResult CommitPreparedMap()
        {
            if (active.Count > 0)
            {
                return MapControllerResult.Fail("撤离点提交失败：已有活动撤离点");
            }

            if (!stagedPrepared)
            {
                return MapControllerResult.Fail("撤离点提交失败：没有候选撤离点");
            }

            activeMapActive = true;
            var committedThisTurn = new List<ExtractionRuntimeEntry>();
            for (var i = 0; i < staged.Count; i++)
            {
                var extraction = staged[i];
                try
                {
                    if (!RegisterCommitted(extraction, out var error))
                    {
                        RollbackCommit(committedThisTurn);
                        activeMapActive = false;
                        return FailPrepared(error);
                    }

                    RefreshEntryAvailability(extraction);
                    extraction.ViewCtrl.SetVisible(true);
                    active.Add(extraction);
                    committedThisTurn.Add(extraction);
                }
                catch (Exception ex)
                {
                    RemoveDomains(extraction, UnitRemovedReason.InvalidPresentation);
                    extraction.ViewCtrl.Dispose();
                    active.Remove(extraction);
                    RollbackCommit(committedThisTurn);
                    activeMapActive = false;
                    return FailPrepared("撤离点提交失败：" + ex.Message);
                }
            }

            activeMapInstanceId = stagedMapInstanceId;
            staged.Clear();
            stagedPrepared = false;
            stagedMapInstanceId = 0;
            PromoteTemporaryPrefabs();
            return MapControllerResult.Ok();
        }

        public void RollbackPreparedMap()
        {
            ReleaseStaged();
            ReleaseTemporaryPrefabs();
        }

        public void SetCurrentMapActive(bool activeState)
        {
            activeMapActive = activeState;
            for (var i = active.Count - 1; i >= 0; i--)
            {
                var extraction = active[i];
                if (!this.GetSystem<UnitSystem>().TryGet(extraction.ViewCtrl.UnitId, out _) ||
                    !this.GetModel<ExtractionModel>().TryGet(extraction.ViewCtrl.UnitId, out _))
                {
                    active.RemoveAt(i);
                    extraction.ViewCtrl.Dispose();
                    continue;
                }

                if (!activeState)
                {
                    extraction.PlayerColliderIds.Clear();
                    // 必须先在 Unit 仍活动时报告 inside=false；否则身份校验会拒绝 inactive Unit，
                    // 当前撤离动作也就无法因地图停用而立即取消。
                    this.GetSystem<ExtractionSystem>().TrySetPlayerInRange(
                        extraction.ViewCtrl.UnitId,
                        PlayerModel.LocalPlayerUnitId,
                        false,
                        out _);
                    extraction.ViewCtrl.SetPlayerInside(false);
                }
                this.GetSystem<UnitSystem>().TrySetActive(extraction.ViewCtrl.UnitId, activeState);
                RefreshEntryAvailability(extraction);
                extraction.ViewCtrl.SetVisible(activeState);
            }
        }

        public void ReleaseCurrentMap()
        {
            ReleaseCommitted();
        }

        public void ReleaseAll()
        {
            if (released)
            {
                return;
            }

            released = true;
            ReleaseStaged();
            ReleaseCommitted();
            ReleaseLoadingPrefabs();
            ReleaseTemporaryPrefabs();
            ReleaseCommittedPrefabs();
            expeditionChangedUnregister?.UnRegister();
            expeditionChangedUnregister = null;
            countdownStartedUnregister?.UnRegister();
            countdownCanceledUnregister?.UnRegister();
            actionChangedUnregister?.UnRegister();
            returnStartedUnregister?.UnRegister();
            returnFailedUnregister?.UnRegister();
            languageChangedUnregister?.UnRegister();
            countdownStartedUnregister = null;
            countdownCanceledUnregister = null;
            actionChangedUnregister = null;
            returnStartedUnregister = null;
            returnFailedUnregister = null;
            languageChangedUnregister = null;
            if (lateUpdateRegistered)
            {
                LifeCycle.UnregisterLateUpdate(OnLateUpdate);
                lateUpdateRegistered = false;
            }
        }

        private ExtractionViewCtrl CreateCtrl(TaskExitView view, long mapInstanceId)
        {
            var unitId = this.GetSystem<UnitSystem>().AllocateUnitId();
            return new ExtractionViewCtrl(
                view,
                unitId,
                mapInstanceId,
                this.GetUtility<ViewCtrlRegistry>(),
                this.GetUtility<IViewInstanceProvider>(),
                OnRangeEntered,
                OnRangeExited,
                OnInteractionRequested);
        }

        private bool RegisterCommitted(ExtractionRuntimeEntry extraction, out string error)
        {
            var snapshot = new UnitSnapshot(
                extraction.ViewCtrl.UnitId,
                extraction.ViewCtrl.MapInstanceId,
                UnitType.ExtractionPoint,
                UnitSource.MapPlaced,
                extraction.Config.Id,
                extraction.ViewCtrl.Position,
                extraction.ViewCtrl.Rotation,
                UnitAuthorityMode.Local,
                0,
                true);

            if (!this.GetSystem<UnitSystem>().TryRegister(snapshot, out error))
            {
                return false;
            }

            var available = activeMapActive && this.GetModel<ExpeditionModel>().CanReturn;
            if (!this.GetSystem<ExtractionSystem>()
                    .TryRegisterExtraction(extraction.ViewCtrl.UnitId, available, out error))
            {
                this.GetSystem<UnitSystem>()
                    .Remove(extraction.ViewCtrl.UnitId, UnitRemovedReason.InvalidPresentation);
                return false;
            }

            return true;
        }

        private void OnRangeEntered(int extractionUnitId, Collider other)
        {
            if (!TryFindActive(extractionUnitId, out var extraction) || other == null) return;
            var playerUnitId = PlayerModel.LocalPlayerUnitId;
            if (!this.GetUtility<IPlayerRuntimePortProvider>()
                    .IsPlayerCollider(playerUnitId, extraction.ViewCtrl.MapInstanceId, other)) return;
            if (!extraction.PlayerColliderIds.Add(other.GetInstanceID()) || extraction.PlayerColliderIds.Count != 1) return;
            if (this.GetSystem<ExtractionSystem>().TrySetPlayerInRange(
                    extractionUnitId, playerUnitId, true, out _))
                extraction.ViewCtrl.SetPlayerInside(true);
            RefreshEntryPrompt(extraction);
        }

        private void OnRangeExited(int extractionUnitId, Collider other)
        {
            if (!TryFindActive(extractionUnitId, out var extraction) || other == null) return;
            var playerUnitId = PlayerModel.LocalPlayerUnitId;
            if (!this.GetUtility<IPlayerRuntimePortProvider>()
                    .IsPlayerCollider(playerUnitId, extraction.ViewCtrl.MapInstanceId, other)) return;
            if (!extraction.PlayerColliderIds.Remove(other.GetInstanceID()) || extraction.PlayerColliderIds.Count != 0) return;
            this.GetSystem<ExtractionSystem>().TrySetPlayerInRange(
                extractionUnitId, playerUnitId, false, out _);
            extraction.ViewCtrl.SetPlayerInside(false);
            RefreshEntryPrompt(extraction);
        }

        private void OnInteractionRequested(int extractionUnitId)
        {
            if (!activeMapActive || !TryFindActive(extractionUnitId, out _)) return;
            this.GetSystem<ExtractionSystem>().TryBeginExtraction(
                extractionUnitId, PlayerModel.LocalPlayerUnitId, out _, out _);
        }

        private bool TryFindActive(int unitId, out ExtractionRuntimeEntry entry)
        {
            for (var i = 0; i < active.Count; i++)
            {
                if (active[i].ViewCtrl.UnitId == unitId)
                {
                    entry = active[i];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        private void RefreshAvailability()
        {
            for (var i = 0; i < active.Count; i++)
            {
                RefreshEntryAvailability(active[i]);
            }
        }

        private void RefreshEntryAvailability(ExtractionRuntimeEntry extraction)
        {
            var available = activeMapActive && this.GetModel<ExpeditionModel>().CanReturn;
            this.GetSystem<ExtractionSystem>().TrySetAvailable(extraction.ViewCtrl.UnitId, available);
            extraction.ViewCtrl.SetAvailable(available);
            RefreshEntryPrompt(extraction);
        }

        private void OnExpeditionChanged(ExpeditionChangedEvent evt)
        {
            RefreshAvailability();
        }

        private void OnCountdownStarted(ExtractionCountdownStartedEvent evt)
        {
            if (!TryFindActive(evt.ExtractionUnitId, out var entry) ||
                entry.ViewCtrl.MapInstanceId != evt.MapInstanceId) return;
            entry.ActionId = evt.ActionId;
            entry.RemainingSeconds = Mathf.CeilToInt(evt.DurationSeconds);
            entry.ViewCtrl.SetCountdown(true, Texts.ExtractionPromptCountdown(entry.RemainingSeconds));
        }

        private void OnPlayerActionChanged(PlayerActionChangedEvent evt)
        {
            if (!evt.HasAction || evt.Snapshot.Kind != PlayerActionKind.Extraction) return;
            for (var i = 0; i < active.Count; i++)
            {
                var entry = active[i];
                if (entry.ActionId != evt.Snapshot.ActionId) continue;
                var seconds = Mathf.Max(0, Mathf.CeilToInt(evt.Snapshot.DurationSeconds - evt.Snapshot.ElapsedSeconds));
                if (seconds == entry.RemainingSeconds) return;
                entry.RemainingSeconds = seconds;
                entry.ViewCtrl.SetCountdown(true, Texts.ExtractionPromptCountdown(seconds));
                return;
            }
        }

        private void OnCountdownCanceled(ExtractionCountdownCanceledEvent evt)
        {
            if (!TryFindActive(evt.ExtractionUnitId, out var entry) || entry.ActionId != evt.ActionId ||
                entry.ViewCtrl.MapInstanceId != evt.MapInstanceId) return;
            entry.ActionId = 0;
            entry.RemainingSeconds = 0;
            entry.ViewCtrl.SetCountdown(false, Texts.ExtractionPromptStart);
            RefreshEntryPrompt(entry);
            switch (evt.Reason)
            {
                case PlayerActionCancelReason.UserCanceled:
                    Notify.ShowFloatTips(Texts.ExtractionCancelUser);
                    break;
                case PlayerActionCancelReason.Attack:
                    Notify.ShowFloatTips(Texts.ExtractionCancelAttack);
                    break;
                case PlayerActionCancelReason.Damaged:
                    Notify.ShowFloatTips(Texts.ExtractionCancelDamaged);
                    break;
                case PlayerActionCancelReason.LeftRange:
                    Notify.ShowFloatTips(Texts.ExtractionCancelLeftRange);
                    break;
            }
        }

        private void OnReturnStarted(ExtractionReturnStartedEvent evt)
        {
            if (!TryFindActive(evt.ExtractionUnitId, out var entry)) return;
            entry.ActionId = 0;
            entry.RemainingSeconds = 0;
            entry.ViewCtrl.SetCountdown(false, Texts.ExtractionPromptStart);
            entry.ViewCtrl.SetReturning(true);
        }

        private void OnReturnFailed(ExtractionReturnFailedEvent evt)
        {
            if (!TryFindActive(evt.ExtractionUnitId, out var entry) ||
                entry.ViewCtrl.MapInstanceId != evt.MapInstanceId) return;
            entry.ViewCtrl.SetReturning(false);
            RefreshEntryPrompt(entry);
            Notify.ShowFloatTips(Texts.ExtractionReturnFailed);
        }

        private void RefreshPromptTexts()
        {
            for (var i = 0; i < active.Count; i++) RefreshEntryPrompt(active[i]);
        }

        private void RefreshEntryPrompt(ExtractionRuntimeEntry entry)
        {
            var text = entry.ActionId != 0
                ? Texts.ExtractionPromptCountdown(entry.RemainingSeconds)
                : Texts.ExtractionPromptStart;
            if (entry.ActionId != 0) entry.ViewCtrl.SetCountdown(true, text);
            else entry.ViewCtrl.SetPromptText(text);
        }

        private void OnLateUpdate()
        {
            if (!activeMapActive || active.Count == 0) return;
            var cameras = this.GetSystem<PlayerCameraSystem>();
            if (cameras == null || !cameras.TryGetCameraPose(out var cameraPosition, out var cameraUp)) return;
            for (var i = 0; i < active.Count; i++) active[i].ViewCtrl.FacePrompt(cameraPosition, cameraUp);
        }

        private MapControllerResult FailPrepared(string error)
        {
            ReleaseStaged();
            ReleaseTemporaryPrefabs();
            return MapControllerResult.Fail(error);
        }

        private async UniTask<PrefabLoadResult> LoadExtractionPrefabAsync(
            ExtractionConfigDefinition config,
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

            if (loadingPrefabs.ContainsKey(config.Id))
            {
                return PrefabLoadResult.Fail("撤离点准备失败：Prefab 正在加载");
            }

            var state = new LoadingState();
            loadingPrefabs.Add(config.Id, state);
            state.Handle = ResourceLoader.LoadAssetAsync<GameObject>(config.PrefabAddress, asset =>
            {
                state.CallbackArrived = true;
                if (state.CancelledBeforeCallback || released)
                {
                    ReleaseHandleOnce(state);
                    loadingPrefabs.Remove(config.Id);
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
                    loadingPrefabs.Remove(config.Id);
                }

                return PrefabLoadResult.Fail("撤离点准备失败：请求已取消");
            }

            loadingPrefabs.Remove(config.Id);
            if (prefab == null)
            {
                ReleaseHandleOnce(state);
                return PrefabLoadResult.Fail("撤离点准备失败：Prefab 加载失败");
            }

            if (cancellationToken.IsCancellationRequested || released)
            {
                ReleaseHandleOnce(state);
                return PrefabLoadResult.Fail("撤离点准备失败：请求已取消");
            }

            temporaryPrefabs[config.Id] = new PrefabCacheEntry(prefab, state.Handle, config);
            return PrefabLoadResult.Ok(prefab);
        }

        private static bool ValidatePrefab(GameObject prefab, out TaskExitView view, out string error)
        {
            view = null;
            if (prefab == null)
            {
                error = "撤离点准备失败：Prefab 为空";
                return false;
            }

            view = prefab.GetComponent<TaskExitView>();
            if (view == null)
            {
                error = "撤离点准备失败：Prefab 根缺少 TaskExitView";
                return false;
            }

            var trigger = prefab.GetComponent<Collider>();
            if (trigger == null)
            {
                error = "撤离点准备失败：Prefab 根缺少 Collider";
                return false;
            }

            if (!trigger.isTrigger)
            {
                error = "撤离点准备失败：Collider 必须是 Trigger";
                return false;
            }

            return view.TryValidatePresentation(out error);
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

        private void ReleaseLoadingPrefabs()
        {
            var states = new List<LoadingState>(loadingPrefabs.Values);
            loadingPrefabs.Clear();
            for (var i = 0; i < states.Count; i++)
            {
                states[i].CancelledBeforeCallback = true;
                ReleaseHandleOnce(states[i]);
                states[i].Completion.TrySetResult(null);
            }
        }

        private void RollbackCommit(List<ExtractionRuntimeEntry> committedThisTurn)
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
        }

        private void ReleaseUnownedCandidateInstances()
        {
            for (var i = unownedCandidateInstances.Count - 1; i >= 0; i--)
            {
                this.GetUtility<IViewInstanceProvider>().Release(unownedCandidateInstances[i]);
            }

            unownedCandidateInstances.Clear();
        }

        private void RemoveDomains(ExtractionRuntimeEntry extraction, UnitRemovedReason reason)
        {
            extraction.PlayerColliderIds.Clear();
            extraction.ViewCtrl.SetPlayerInside(false);
            this.GetSystem<ExtractionSystem>().TryRemoveExtraction(extraction.ViewCtrl.UnitId);
            this.GetSystem<UnitSystem>().Remove(extraction.ViewCtrl.UnitId, reason);
        }

        private sealed class ExtractionRuntimeEntry
        {
            public ExtractionRuntimeEntry(ExtractionViewCtrl viewCtrl, ExtractionConfigDefinition config)
            {
                ViewCtrl = viewCtrl;
                Config = config;
            }

            public ExtractionViewCtrl ViewCtrl { get; }
            public ExtractionConfigDefinition Config { get; }

            // Collider 集合表达本地玩家在本次地图会话中的首进末出，多 Collider 时不会因其中一个离开而提前离区。
            public HashSet<int> PlayerColliderIds { get; } = new HashSet<int>();

            // ActionId 与剩余秒仅把持续动作事件绑定到当前表现，均不写入业务模型或存档。
            public long ActionId { get; set; }
            public int RemainingSeconds { get; set; }
        }

        private readonly struct PrefabCacheEntry
        {
            public PrefabCacheEntry(GameObject prefab, ResourceLoadHandle handle, ExtractionConfigDefinition config)
            {
                Prefab = prefab;
                Handle = handle;
                Config = config;
            }

            public GameObject Prefab { get; }
            public ResourceLoadHandle Handle { get; }
            public ExtractionConfigDefinition Config { get; }
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
