using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>管理地图 NPC 的实例、交互分发和资源准备；领域状态与交易规则由对应 System 持有。</summary>
    public sealed class NpcCtrl : IController
    {
        private readonly IArchitecture architecture;
        private readonly List<NpcRuntimeEntry> staged = new List<NpcRuntimeEntry>();
        private readonly List<NpcRuntimeEntry> active = new List<NpcRuntimeEntry>();
        private readonly Dictionary<string, PrefabCacheEntry> committedPrefabs =
            new Dictionary<string, PrefabCacheEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, PrefabCacheEntry> temporaryPrefabs =
            new Dictionary<string, PrefabCacheEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, LoadingState> loadingPrefabs =
            new Dictionary<string, LoadingState>(StringComparer.Ordinal);
        private IUnRegister languageChangedUnregister;
        private IUnRegister dialogueFunctionUnregister;
        private long stagedMapInstanceId;
        private bool stagedPrepared;
        private bool activeMapActive;
        private bool released;
        private bool lateUpdateRegistered;
        // 远征地图入口在联网检查、确认和下载期间只允许一个请求；释放地图后代次失效。

        public NpcCtrl(IArchitecture architecture)
        {
            this.architecture = architecture ?? throw new ArgumentNullException(nameof(architecture));
            languageChangedUnregister = architecture.RegisterEvent<LanguageChangedEvent>(_ => RefreshActivePrompts());
            dialogueFunctionUnregister = architecture.RegisterEvent<DialogueNpcFunctionRequestedEvent>(OnDialogueFunctionRequested);
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
                return MapControllerResult.Fail("NPC 准备失败：已有候选 NPC");
            }

            if (context?.Scene == null)
            {
                return MapControllerResult.Fail("NPC 准备失败：缺少 MapContext");
            }

            stagedPrepared = true;
            stagedMapInstanceId = context.MapInstanceId;
            try
            {
                for (var i = 0; i < context.Spawns.Count; i++)
                {
                    var spawn = context.Spawns[i];
                    if (spawn.ObjectType != MapSpawnObjectType.Npc)
                    {
                        continue;
                    }

                    if (!this.GetUtility<IRuntimeConfigProvider>()
                            .TryGetNpc(spawn.ConfigId, out var config, out var configError))
                    {
                        return FailPrepared("NPC 准备失败：" + configError);
                    }

                    var prefabResult = await LoadNpcPrefabAsync(config, cancellationToken);
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
                        return FailPrepared("NPC 准备失败：Prefab 实例化返回空");
                    }

                    view.gameObject.SetActive(false);
                    var viewCtrl = new NpcViewCtrl(
                        view,
                        this.GetSystem<UnitSystem>().AllocateUnitId(),
                        context.MapInstanceId,
                        this.GetUtility<ViewCtrlRegistry>(),
                        this.GetUtility<IViewInstanceProvider>(),
                        OnNpcInteractionRequested);
                    staged.Add(new NpcRuntimeEntry(viewCtrl, config));
                }

                return MapControllerResult.Ok();
            }
            catch (OperationCanceledException)
            {
                return FailPrepared("NPC 准备失败：请求已取消");
            }
            catch (Exception exception)
            {
                return FailPrepared("NPC 准备失败：" + exception.Message);
            }
        }

        public MapControllerResult CommitPreparedMap()
        {
            if (active.Count > 0)
            {
                return MapControllerResult.Fail("NPC 提交失败：已有活动 NPC");
            }

            if (!stagedPrepared)
            {
                return MapControllerResult.Fail("NPC 提交失败：没有候选 NPC");
            }

            var committedThisTurn = new List<NpcRuntimeEntry>();
            for (var i = 0; i < staged.Count; i++)
            {
                var npc = staged[i];
                var snapshot = new UnitSnapshot(
                    npc.ViewCtrl.UnitId,
                    npc.ViewCtrl.MapInstanceId,
                    UnitType.Npc,
                    UnitSource.MapPlaced,
                    npc.Config.Id,
                    npc.ViewCtrl.Position,
                    npc.ViewCtrl.Rotation,
                    UnitAuthorityMode.Local,
                    0,
                    true);
                if (!this.GetSystem<UnitSystem>().TryRegister(snapshot, out var error))
                {
                    RollbackCommit(committedThisTurn);
                    return FailPrepared("NPC 提交失败：" + error);
                }

                npc.ViewCtrl.SetPromptText(BuildPrompt(npc.Config), Loc.T(npc.Config.NameKey));
                npc.ViewCtrl.SetInteractable(true);
                npc.ViewCtrl.SetVisible(true);
                active.Add(npc);
                committedThisTurn.Add(npc);
            }

            staged.Clear();
            stagedPrepared = false;
            stagedMapInstanceId = 0;
            activeMapActive = true;
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
            for (var i = 0; i < active.Count; i++)
            {
                var npc = active[i];
                if (!activeState)
                {
                    this.GetSystem<ShopSystem>()?.CloseForNpc(npc.ViewCtrl.UnitId);
                }
                npc.ViewCtrl.SetInteractable(activeState);
                npc.ViewCtrl.SetVisible(activeState);
                this.GetSystem<UnitSystem>().TrySetActive(npc.ViewCtrl.UnitId, activeState);
            }
        }

        public void ReleaseCurrentMap()
        {
            ReleaseCommitted();
        }

        public void ReleaseAll()
        {
            released = true;
            ReleaseStaged();
            ReleaseCommitted();
            ReleaseTemporaryPrefabs();
            ReleaseCommittedPrefabs();
            foreach (var pair in loadingPrefabs)
            {
                pair.Value.CancelledBeforeCallback = !pair.Value.CallbackArrived;
            }

            loadingPrefabs.Clear();
            languageChangedUnregister?.UnRegister();
            languageChangedUnregister = null;
            dialogueFunctionUnregister?.UnRegister();
            dialogueFunctionUnregister = null;
            if (lateUpdateRegistered)
            {
                LifeCycle.UnregisterLateUpdate(OnLateUpdate);
                lateUpdateRegistered = false;
            }
        }

        private void OnLateUpdate()
        {
            if (!activeMapActive || active.Count == 0)
            {
                return;
            }

            var cameraSystem = this.GetSystem<PlayerCameraSystem>();
            if (cameraSystem == null ||
                !cameraSystem.TryGetCameraPose(out var cameraPosition, out var cameraUp))
            {
                return;
            }

            for (var i = 0; i < active.Count; i++)
            {
                active[i].ViewCtrl.FacePrompt(cameraPosition, cameraUp);
            }
        }

        private void OnNpcInteractionRequested(int unitId)
        {
            if (!activeMapActive || !TryFindActive(unitId, out var entry))
            {
                return;
            }

            FaceInteractingPlayer(entry);
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetNpcDialogue(entry.Config.Id, out var dialogue, out var dialogueError))
            {
                Notify.ShowFloatTips(Loc.T("dialogue.failed"));
                Log.Error("[NpcCtrl] 对话配置读取失败：" + dialogueError);
                return;
            }
            if (dialogue != null)
            {
                if (!this.GetSystem<DialogueSystem>().TryBegin(dialogue, unitId, entry.ViewCtrl.MapInstanceId, entry.Config.NameKey, out dialogueError) ||
                    !DialogueUICtrl.TryShow(out dialogueError))
                {
                    Notify.ShowFloatTips(Loc.T("dialogue.failed"));
                    Log.Warning("[NpcCtrl] 对话打开失败：" + dialogueError);
                }
                return;
            }
            switch (entry.Config.InteractionKind)
            {
                case global::NpcInteractionKind.MapSelection:
                    TryOpenNpcInterface(entry);
                    break;
                case global::NpcInteractionKind.Warehouse:
                case global::NpcInteractionKind.Craft:
                case global::NpcInteractionKind.Shop:
                case global::NpcInteractionKind.Arcade:
                    // 基地功能资源在启动阶段准备，不因未下载远征地图而阻断交互。
                    TryOpenNpcInterface(entry);
                    break;
            }
        }

        /// <summary>接收已确认且只执行一次的功能意图；再次核对原 NPC/地图身份后复用旧业务入口。</summary>
        private void OnDialogueFunctionRequested(DialogueNpcFunctionRequestedEvent e)
        {
            if (!released && activeMapActive && TryFindActive(e.NpcUnitId, out var entry) && entry.ViewCtrl.MapInstanceId == e.MapInstanceId)
                TryOpenNpcInterface(entry);
        }

        /// <summary>交互时原地转向同地图的本地玩家；水平重合或玩家失效时保留原朝向。</summary>
        private void FaceInteractingPlayer(NpcRuntimeEntry entry)
        {
            var units = this.GetSystem<UnitSystem>();
            if (units == null || !units.TryGet(PlayerModel.LocalPlayerUnitId, out var player) ||
                !player.IsActive || player.MapInstanceId != entry.ViewCtrl.MapInstanceId) return;
            var direction = player.Position - entry.ViewCtrl.Position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            var rotation = Quaternion.LookRotation(direction, Vector3.up);
            // 先更新 Unit 权威姿态，再同步表现，避免后续快照仍保留出生朝向。
            units.TryUpdateTransform(entry.ViewCtrl.UnitId, entry.ViewCtrl.Position, rotation);
            entry.ViewCtrl.SetRotation(rotation);
        }

        /// <summary>资源就绪后打开本次交互的界面；加载失败必须给出反馈。</summary>
        private static void TryOpenNpcInterface(NpcRuntimeEntry entry)
        {
            switch (entry.Config.InteractionKind)
            {
                case global::NpcInteractionKind.MapSelection:
                    TryOpenMapSelection(entry);
                    break;
                case global::NpcInteractionKind.Warehouse:
                    if (!WarehouseUICtrl.TryShow(out _))
                    {
                        Notify.ShowFloatTips(Loc.T("ui.inventory.operation_failed"));
                    }

                    break;
                case global::NpcInteractionKind.Craft:
                    try
                    {
                        if (!CraftUICtrl.TryShow(out var error))
                        {
                            Notify.ShowFloatTips(Loc.T("ui.craft.failed"));
                            Log.Error($"[NpcCtrl] 打造界面打开失败：{error}");
                        }
                    }
                    catch (Exception exception)
                    {
                        Notify.ShowFloatTips(Loc.T("ui.craft.failed"));
                        Log.Error($"[NpcCtrl] 打造交互异常：{exception}");
                    }

                    break;
                case global::NpcInteractionKind.Shop:
                    if (!ShopUICtrl.TryShow(entry.Config.Id, entry.ViewCtrl.UnitId, entry.Config.InteractionParam, out var shopError))
                    {
                        Notify.ShowFloatTips(Loc.T("ui.shop.error.open"));
                        Log.Error($"[NpcCtrl] 商店界面打开失败：npc={entry.Config.Id} reason={shopError}");
                    }

                    break;
                case global::NpcInteractionKind.Arcade:
                    if (!ArcadeResumeUICtrl.TryOpenEntry(out var arcadeError))
                    {
                        Notify.ShowFloatTips(Loc.T("ui.arcade.error.open"));
                        Log.Error($"[NpcCtrl] 街机入口打开失败：npc={entry.Config.Id} reason={arcadeError}");
                    }

                    break;
            }
        }

        private static void TryOpenMapSelection(NpcRuntimeEntry entry)
        {
            if (!MapSelectionUICtrl.TryShowOffline(out var error))
            {
                Notify.ShowFloatTips(Loc.T("ui.map_selection.error.open"));
                Log.Error($"[NpcCtrl] 地图选择界面打开失败：npc={entry.Config.Id} reason={error}");
            }
        }

        private bool TryFindActive(int unitId, out NpcRuntimeEntry entry)
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

        private void RefreshActivePrompts()
        {
            for (var i = 0; i < active.Count; i++)
            {
                active[i].ViewCtrl.SetPromptText(BuildPrompt(active[i].Config), Loc.T(active[i].Config.NameKey));
            }
        }

        private static string BuildPrompt(NpcConfigDefinition config)
        {
            return Texts.NpcInteractionPrompt(Loc.T(config.NameKey));
        }

        private MapControllerResult FailPrepared(string error)
        {
            ReleaseStaged();
            ReleaseTemporaryPrefabs();
            return MapControllerResult.Fail(error);
        }

        private async UniTask<PrefabLoadResult> LoadNpcPrefabAsync(
            NpcConfigDefinition config,
            CancellationToken cancellationToken)
        {
            if (TryFindPrefab(committedPrefabs, config.PrefabAddress, out var committed))
            {
                return PrefabLoadResult.Ok(committed.Prefab);
            }

            if (TryFindPrefab(temporaryPrefabs, config.PrefabAddress, out var temporary))
            {
                return PrefabLoadResult.Ok(temporary.Prefab);
            }

            if (loadingPrefabs.ContainsKey(config.PrefabAddress))
            {
                return PrefabLoadResult.Fail("NPC 准备失败：Prefab 正在加载");
            }

            var state = new LoadingState();
            loadingPrefabs.Add(config.PrefabAddress, state);
            state.Handle = ResourceLoader.LoadAssetAsync<GameObject>(config.PrefabAddress, asset =>
            {
                state.CallbackArrived = true;
                loadingPrefabs.Remove(config.PrefabAddress);
                if (state.CancelledBeforeCallback || released)
                {
                    ReleaseHandleOnce(state);
                    state.Completion.TrySetResult(null);
                    return;
                }

                state.Completion.TrySetResult(asset);
            });

            using var registration = cancellationToken.Register(() =>
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

                return PrefabLoadResult.Fail("NPC 准备失败：请求已取消");
            }

            if (prefab == null)
            {
                ReleaseHandleOnce(state);
                return PrefabLoadResult.Fail("NPC 准备失败：Prefab 加载失败");
            }

            if (cancellationToken.IsCancellationRequested || released)
            {
                ReleaseHandleOnce(state);
                return PrefabLoadResult.Fail("NPC 准备失败：请求已取消");
            }

            temporaryPrefabs[config.PrefabAddress] = new PrefabCacheEntry(prefab, state.Handle);
            return PrefabLoadResult.Ok(prefab);
        }

        private static bool TryFindPrefab(
            Dictionary<string, PrefabCacheEntry> prefabs,
            string address,
            out PrefabCacheEntry entry)
        {
            return prefabs.TryGetValue(address, out entry) && entry.Prefab != null;
        }

        private static bool ValidatePrefab(GameObject prefab, out NpcView view, out string error)
        {
            view = null;
            if (prefab == null)
            {
                error = "NPC 准备失败：Prefab 为空";
                return false;
            }

            var views = prefab.GetComponentsInChildren<NpcView>(true);
            if (views == null || views.Length != 1)
            {
                error = "NPC 准备失败：Prefab 必须且只能包含一个 NpcView";
                return false;
            }

            view = views[0];
            if (view.transform != prefab.transform)
            {
                error = "NPC 准备失败：NpcView 必须位于 Prefab 根节点";
                return false;
            }

            var trigger = prefab.GetComponent<Collider>();
            if (trigger == null || !trigger.enabled || !trigger.isTrigger)
            {
                error = "NPC 准备失败：Prefab 根节点缺少启用的 Trigger Collider";
                return false;
            }

            return view.TryValidatePresentation(out error);
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

        private static void ReleaseHandleOnce(LoadingState state)
        {
            if (state.Handle.IsValid && !state.HandleReleased)
            {
                ResourceLoader.Release(state.Handle);
                state.HandleReleased = true;
            }
        }

        private void RollbackCommit(List<NpcRuntimeEntry> committedThisTurn)
        {
            for (var i = committedThisTurn.Count - 1; i >= 0; i--)
            {
                this.GetSystem<UnitSystem>().Remove(committedThisTurn[i].ViewCtrl.UnitId, UnitRemovedReason.InvalidPresentation);
                committedThisTurn[i].ViewCtrl.Dispose();
                active.Remove(committedThisTurn[i]);
            }
        }

        private void ReleaseCommitted()
        {
            for (var i = active.Count - 1; i >= 0; i--)
            {
                // 先关闭商店会话，确保随后 Unit 移除与 UI 晚回调都无法继续成交。
                this.GetSystem<ShopSystem>()?.CloseForNpc(active[i].ViewCtrl.UnitId);
                this.GetSystem<UnitSystem>().Remove(active[i].ViewCtrl.UnitId, UnitRemovedReason.MapInstanceReleased);
                active[i].ViewCtrl.Dispose();
            }

            active.Clear();
            activeMapActive = false;
        }

        private void ReleaseStaged()
        {
            for (var i = staged.Count - 1; i >= 0; i--)
            {
                staged[i].ViewCtrl.Dispose();
            }

            staged.Clear();
            stagedPrepared = false;
            stagedMapInstanceId = 0;
        }

        private sealed class NpcRuntimeEntry
        {
            public NpcRuntimeEntry(NpcViewCtrl viewCtrl, NpcConfigDefinition config)
            {
                ViewCtrl = viewCtrl;
                Config = config;
            }

            public NpcViewCtrl ViewCtrl { get; }
            public NpcConfigDefinition Config { get; }
        }

        private readonly struct PrefabCacheEntry
        {
            public PrefabCacheEntry(GameObject prefab, ResourceLoadHandle handle)
            {
                Prefab = prefab;
                Handle = handle;
            }

            public GameObject Prefab { get; }
            public ResourceLoadHandle Handle { get; }
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
