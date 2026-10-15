using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>拥有地图建筑实例及资源生命周期，提供预览、碰撞和拾取端口；不裁决材料或存档规则。</summary>
    public sealed class TemporaryBuildingCtrl : IController, ITemporaryBuildingViewPort
    {
        private readonly Dictionary<int, ViewEntry> buildings = new Dictionary<int, ViewEntry>();
        private readonly List<PreparedBuilding> preparedBuildings = new List<PreparedBuilding>();
        private readonly Dictionary<int, PromptState> promptStates = new Dictionary<int, PromptState>();
        private readonly IArchitecture architecture;
        private Func<float> promptRealtimeProvider = DefaultPromptRealtime;
        private Func<float> presentationUnscaledDeltaProvider = DefaultPresentationUnscaledDelta;
        private TemporaryBuildingView preview;
        private long previewMapInstanceId;
        private IUnRegister buildRequestedUnregister;
        private IUnRegister buildingChangedUnregister;
        private IUnRegister promptUnregister;
        private bool lateUpdateRegistered;
        private readonly RaycastHit[] committedBuildingHits = new RaycastHit[32];
        private bool committedBuildingHitOverflowWarned;
        /// <summary>当前地图的玩家操作圈及专属材质；建造与拆除共用，退出模式或释放地图时销毁。</summary>
        private LineRenderer operationRangeRing;
        private Material operationRangeMaterial;
        private long operationRangeMapInstanceId;
        private readonly Vector3[] operationRangePoints = new Vector3[64];

        public TemporaryBuildingCtrl(IArchitecture architecture)
        {
            this.architecture = architecture;
            buildRequestedUnregister = architecture.RegisterEvent<TemporaryBuildRequestedEvent>(_ =>
            {
                if (!TemporaryBuildingSelectUICtrl.TryShow(out var error) && !string.IsNullOrWhiteSpace(error))
                {
                    Log.Warning($"[TemporaryBuildingCtrl] 无法打开建筑选择：{error}");
                }
            });
            buildingChangedUnregister = architecture.RegisterEvent<TemporaryBuildingChangedEvent>(evt =>
            {
                RefreshOperationRange();
                if (evt.Snapshot.Mode == TemporaryBuildingMode.Normal ||
                    evt.Snapshot.Mode == TemporaryBuildingMode.OperatingManualAttack)
                {
                    UIHelper.HideUI<TemporaryBuildingHudUICtrl>();
                }
                else if (UnityEngine.Object.FindObjectOfType<UIRoot>() != null)
                {
                    UIHelper.ShowUI<TemporaryBuildingHudUICtrl>();
                }
            });
            promptUnregister = architecture.RegisterEvent<TemporaryBuildingPromptEvent>(OnPrompt);
            LifeCycle.RegisterLateUpdate(OnLateUpdate);
            lateUpdateRegistered = true;
        }

        public IArchitecture GetArchitecture()
        {
            return architecture;
        }

        public bool TryCreatePreview(long mapInstanceId, TempBuildingDefinition definition, bool requiresManualAttack, Vector3 position, Quaternion rotation, out string error)
        {
            ReleasePreview();
            if (!TryGetRuntimeRoot(mapInstanceId, out var runtimeRoot, out error) ||
                !TryLoadView(definition.PrefabAddress, runtimeRoot, out var view, out error))
            {
                return false;
            }

            TemporaryManualAttackView manualAttack = null;
            if (requiresManualAttack)
            {
                if (!view.TryGetComponent(out manualAttack) || !manualAttack.ValidateReferences(out error))
                {
                    this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
                    return false;
                }
            }

            preview = view;
            previewMapInstanceId = mapInstanceId;
            preview.transform.SetPositionAndRotation(position, rotation);
            preview.SetPreviewValidity(true);
            error = null;
            return true;
        }

        public void UpdatePreview(Vector3 position, Quaternion rotation, bool valid)
        {
            if (preview == null)
            {
                return;
            }

            preview.transform.SetPositionAndRotation(position, rotation);
            preview.SetPreviewValidity(valid);
        }

        public void ReleasePreview()
        {
            if (preview == null)
            {
                return;
            }

            this.GetUtility<IViewInstanceProvider>().Release(preview.gameObject);
            preview = null;
            previewMapInstanceId = 0;
        }

        public bool TryGetPlacementBox(Vector3 position, Quaternion rotation, out Vector3 center, out Vector3 halfSize)
        {
            center = default; halfSize = default;
            if (preview == null || !preview.SeparatePhysicalColliders || !(preview.FootprintCollider is BoxCollider box)) return false;
            var scale = preview.transform.lossyScale;
            center = position + rotation * Vector3.Scale(box.center, scale);
            halfSize = Vector3.Scale(box.size * .5f, scale);
            return true;
        }

        public bool TryCreateBuilding(
            TemporaryBuildingRuntimeState building,
            TempBuildingDefinition definition,
            bool requiresManualAttack,
            out string error)
        {
            if (buildings.ContainsKey(building.RuntimeId) ||
                this.GetModel<TemporaryBuildingModel>().Buildings.ContainsKey(building.RuntimeId))
            {
                error = "临时建筑创建失败：运行 ID 已存在";
                return false;
            }

            if (!TryGetRuntimeRoot(building.MapInstanceId, out var runtimeRoot, out error) ||
                !TryLoadView(definition.PrefabAddress, runtimeRoot, out var view, out error))
            {
                return false;
            }

            TemporaryManualAttackView manualAttack = null;
            if (requiresManualAttack && (!view.TryGetComponent(out manualAttack) || !manualAttack.ValidateReferences(out error)))
            {
                this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
                return false;
            }
            view.transform.localScale *= building.UniformScale;
            view.transform.SetPositionAndRotation(building.Position, building.Rotation);
            if (!view.SetCommitted(definition.BlocksNavigation, out error))
            {
                this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
                return false;
            }
            buildings[building.RuntimeId] = new ViewEntry(building.MapInstanceId, view, manualAttack);
            error = null;
            return true;
        }

        /// <summary>为地图候选构造临时建筑视图；此阶段不写入活动 Model 或已提交 View 集合。</summary>
        internal MapControllerResult PrepareMap(MapPreparationContext context)
        {
            RollbackPreparedMap();
            if (context?.Scene?.DynamicObjectRoot == null)
            {
                return MapControllerResult.Fail("临时建筑准备失败：缺少候选地图上下文");
            }

            if (context.Mode == MapPreparationMode.OnlinePlayersOnly)
            {
                return PrepareOnlineMap(context);
            }

            var snapshots = context.ExpeditionSnapshot?.World?.TemporaryBuildings ?? context.BaseSnapshot?.World?.TemporaryBuildings;
            if (snapshots == null || snapshots.Count == 0)
            {
                return MapControllerResult.Ok();
            }

            var ids = new HashSet<int>();
            for (var i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                if (snapshot == null || snapshot.RuntimeId <= 0 || string.IsNullOrWhiteSpace(snapshot.ConfigId) ||
                    snapshot.Pose == null || snapshot.LoadedAmmo < 0 || snapshot.UniformScale <= 0 || float.IsNaN(snapshot.UniformScale) || float.IsInfinity(snapshot.UniformScale) || !ids.Add(snapshot.RuntimeId))
                {
                    RollbackPreparedMap();
                    return MapControllerResult.Fail("临时建筑准备失败：快照字段无效");
                }

                if (!this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuilding(snapshot.ConfigId, out var definition, out var configError))
                {
                    RollbackPreparedMap();
                    return MapControllerResult.Fail("临时建筑准备失败：" + configError);
                }

                // 恢复候选还未成为当前地图，必须使用候选上下文核对离线例外，不能读取旧地图状态。
                var expeditionException = context.Mode == MapPreparationMode.OfflineFull && context.ExpeditionSnapshot != null &&
                    !string.IsNullOrEmpty(definition.ExpeditionMapId) &&
                    string.Equals(context.MapId, definition.ExpeditionMapId, StringComparison.Ordinal) &&
                    string.Equals(context.ExpeditionSnapshot.MapId, context.MapId, StringComparison.Ordinal);
                if (definition.BaseOnly && context.BaseSnapshot == null && !expeditionException)
                {
                    RollbackPreparedMap();
                    return MapControllerResult.Fail("临时建筑准备失败：基地摆件不能恢复到远征地图");
                }
                var position = new Vector3(snapshot.Pose.X, snapshot.Pose.Y, snapshot.Pose.Z);
                var rotation = Quaternion.Euler(0f, snapshot.Pose.RotationY, 0f);
                if (!TryLoadView(definition.PrefabAddress, context.Scene.DynamicObjectRoot, out var view, out var viewError))
                {
                    RollbackPreparedMap();
                    return MapControllerResult.Fail("临时建筑准备失败：" + viewError);
                }

                var requiresManualAttack = this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuildingManualAttack(definition.Id, out _);
                TemporaryManualAttackView manualAttack = null;
                if (requiresManualAttack && (!view.TryGetComponent(out manualAttack) || !manualAttack.ValidateReferences(out viewError)))
                {
                    this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
                    RollbackPreparedMap();
                    return MapControllerResult.Fail("临时建筑准备失败：" + viewError);
                }

                view.transform.localScale *= snapshot.UniformScale;
                view.transform.SetPositionAndRotation(position, rotation);
                preparedBuildings.Add(new PreparedBuilding(
                    new TemporaryBuildingRuntimeState(snapshot.RuntimeId, definition.Id, context.MapInstanceId, position, rotation, snapshot.UniformScale),
                    definition,
                    manualAttack,
                    snapshot.LoadedAmmo,
                    view));
            }

            return MapControllerResult.Ok();
        }

        private MapControllerResult PrepareOnlineMap(MapPreparationContext context)
        {
            var snapshots = context.OnlineWorld?.TemporaryBuildings;
            if (snapshots == null || snapshots.Count == 0) return MapControllerResult.Ok();
            var ids = new HashSet<int>();
            foreach (var snapshot in snapshots)
            {
                if (snapshot.RuntimeId <= 0 || string.IsNullOrWhiteSpace(snapshot.ConfigId) || snapshot.LoadedAmmo < 0 || !ids.Add(snapshot.RuntimeId))
                {
                    RollbackPreparedMap();
                    return MapControllerResult.Fail("联机临时建筑准备失败：快照字段无效");
                }
                if (!this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuilding(snapshot.ConfigId, out var definition, out var error) ||
                    !TryLoadView(definition.PrefabAddress, context.Scene.DynamicObjectRoot, out var view, out error))
                {
                    RollbackPreparedMap();
                    return MapControllerResult.Fail("联机临时建筑准备失败：" + error);
                }
                var requiresManualAttack = this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuildingManualAttack(definition.Id, out _);
                TemporaryManualAttackView manualAttack = null;
                if (requiresManualAttack && (!view.TryGetComponent(out manualAttack) || !manualAttack.ValidateReferences(out error)))
                {
                    this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
                    RollbackPreparedMap();
                    return MapControllerResult.Fail("联机临时建筑准备失败：" + error);
                }
                var rotation = Quaternion.Euler(0f, snapshot.YawDegrees, 0f);
                view.transform.SetPositionAndRotation(snapshot.Position, rotation);
                var remainingCooldown = Mathf.Max(0f, (snapshot.NextShootAtServerTime - context.OnlineWorld.ServerTime) / 1000f);
                preparedBuildings.Add(new PreparedBuilding(new TemporaryBuildingRuntimeState(snapshot.RuntimeId, snapshot.ConfigId, context.MapInstanceId, snapshot.Position, rotation), definition, manualAttack, snapshot.LoadedAmmo, view, Time.time + remainingCooldown));
            }
            return MapControllerResult.Ok();
        }

        /// <summary>将全部已准备建筑一次性提交到新地图；瞬态交互状态统一重置为空闲。</summary>
        internal MapControllerResult CommitPreparedMap()
        {
            var model = this.GetModel<TemporaryBuildingModel>();
            for (var i = 0; i < preparedBuildings.Count; i++)
            {
                var candidate = preparedBuildings[i];
                if (buildings.ContainsKey(candidate.State.RuntimeId) || model.Buildings.ContainsKey(candidate.State.RuntimeId))
                {
                    RollbackPreparedMap();
                    return MapControllerResult.Fail("临时建筑提交失败：运行 ID 重复");
                }
            }

            for (var i = 0; i < preparedBuildings.Count; i++)
            {
                var candidate = preparedBuildings[i];
                if (!candidate.View.SetCommitted(candidate.Definition.BlocksNavigation, out var error))
                {
                    RollbackPreparedMap();
                    return MapControllerResult.Fail("临时建筑提交失败：" + error);
                }
            }

            for (var i = 0; i < preparedBuildings.Count; i++)
            {
                var candidate = preparedBuildings[i];
                buildings.Add(candidate.State.RuntimeId, new ViewEntry(candidate.State.MapInstanceId, candidate.View, candidate.ManualAttack));
                if (!model.AddBuilding(candidate.State,
                        candidate.ManualAttack == null ? (TemporaryManualAttackRuntimeState?)null : new TemporaryManualAttackRuntimeState(candidate.NextShootTime, candidate.LoadedAmmo)))
                {
                    RollbackPreparedMap();
                    return MapControllerResult.Fail("临时建筑提交失败：运行 ID 已存在");
                }
            }

            preparedBuildings.Clear();
            model.ResetMode();
            return MapControllerResult.Ok();
        }

        /// <summary>释放尚未提交的候选 View；活动建筑及其 Model 记录保持不变。</summary>
        internal void RollbackPreparedMap()
        {
            for (var i = 0; i < preparedBuildings.Count; i++)
            {
                var view = preparedBuildings[i].View;
                if (view != null) this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
            }

            preparedBuildings.Clear();
        }

        public bool TryGetManualAttackPose(int runtimeId, out Vector3 origin, out Vector3 forward)
        {
            origin = Vector3.zero;
            forward = Vector3.zero;
            return buildings.TryGetValue(runtimeId, out var entry) &&
                   entry.ManualAttack != null &&
                   entry.ManualAttack.TryGetPose(out origin, out forward);
        }

        public void SetManualAttackAimDirection(int runtimeId, Vector3 direction)
        {
            if (buildings.TryGetValue(runtimeId, out var entry) && entry.ManualAttack != null)
            {
                entry.ManualAttack.SetAimDirection(direction);
            }
        }

        public void SetManualAttackHud(int runtimeId, bool operating, int ammoItemId, int loadedAmmo, int capacity, PlayerActionSnapshot? reloadSnapshot)
        {
            if (!buildings.TryGetValue(runtimeId, out var entry) || entry.ManualAttack == null)
            {
                return;
            }

            var ammoName = ammoItemId.ToString();
            if (this.GetUtility<IRuntimeConfigProvider>().TryGetItem(ammoItemId, out var item, out _) &&
                !string.IsNullOrWhiteSpace(item.NameKey))
            {
                ammoName = Loc.T(item.NameKey);
            }

            entry.ManualAttack.SetHud(operating, ammoName, loadedAmmo, capacity, reloadSnapshot);
        }

        public void PlayManualAttackEmptyAmmoFeedback(int runtimeId)
        {
            if (buildings.TryGetValue(runtimeId, out var entry) && entry.ManualAttack != null)
            {
                entry.ManualAttack.PlayEmptyAmmoFeedback();
            }
        }

        public void SetPromptRealtimeProviderForTests(Func<float> provider)
        {
            promptRealtimeProvider = provider ?? DefaultPromptRealtime;
        }

        public void SetPresentationUnscaledDeltaProviderForTests(Func<float> provider)
        {
            presentationUnscaledDeltaProvider = provider ?? DefaultPresentationUnscaledDelta;
        }

        public bool TryPickCommittedBuilding(Ray pointerRay, float maxDistance, out int runtimeId)
        {
            runtimeId = 0;
            if (maxDistance <= 0f)
            {
                return false;
            }

            var count = Physics.RaycastNonAlloc(
                pointerRay,
                committedBuildingHits,
                maxDistance,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide);
            if (count >= committedBuildingHits.Length)
            {
                if (!committedBuildingHitOverflowWarned)
                {
                    committedBuildingHitOverflowWarned = true;
                    Log.Warning("[TemporaryBuildingCtrl] 正式建筑射线命中缓存已满，本帧安全放弃拾取");
                }
                return false;
            }

            var nearestIndex = -1;
            var nearestDistance = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                var hit = committedBuildingHits[i];
                if (hit.collider == null || IsLocalPlayerCollider(hit.collider) || hit.distance >= nearestDistance)
                {
                    continue;
                }
                nearestIndex = i;
                nearestDistance = hit.distance;
            }

            if (nearestIndex < 0) return false;
            var nearestCollider = committedBuildingHits[nearestIndex].collider;
            foreach (var pair in buildings)
            {
                var view = pair.Value.View;
                if (view != null && nearestCollider.transform.IsChildOf(view.transform))
                {
                    runtimeId = pair.Key;
                    return true;
                }
            }

            // 最近的非玩家物体不是建筑时，它会遮挡后方建筑。
            return false;
        }

        private bool IsLocalPlayerCollider(Collider collider)
        {
            if (collider == null ||
                !this.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var player) ||
                !player.IsActive)
            {
                return false;
            }

            return this.GetUtility<IPlayerRuntimePortProvider>().IsPlayerCollider(
                player.UnitId,
                player.MapInstanceId,
                collider);
        }

        public void FaceManualAttackWorldHuds(Vector3 cameraPosition, Vector3 cameraUp)
        {
            foreach (var entry in buildings.Values)
            {
                entry.ManualAttack?.FaceWorldHuds(cameraPosition, cameraUp);
            }
        }

        public void SetOperating(int runtimeId, bool active)
        {
            if (buildings.TryGetValue(runtimeId, out var entry) && entry.ManualAttack != null)
            {
                entry.ManualAttack.SetOperating(active);
            }

            if (!active)
            {
                promptStates.Remove(runtimeId);
            }
        }

        public void SetDestroyHighlight(int runtimeId, bool active)
        {
            if (buildings.TryGetValue(runtimeId, out var entry) && entry.View != null)
            {
                entry.View.SetDestroyHighlight(active);
            }
        }

        public void ReleaseBuilding(int runtimeId)
        {
            promptStates.Remove(runtimeId);
            if (!buildings.TryGetValue(runtimeId, out var entry))
            {
                return;
            }

            buildings.Remove(runtimeId);
            if (entry.View != null)
            {
                entry.View.SetDestroyHighlight(false);
                this.GetUtility<IViewInstanceProvider>().Release(entry.View.gameObject);
            }
        }

        public void ReleaseMap(long mapInstanceId)
        {
            if (operationRangeMapInstanceId == mapInstanceId) ReleaseOperationRange();
            if (preview != null && previewMapInstanceId == mapInstanceId)
            {
                ReleasePreview();
            }

            var ids = new List<int>();
            foreach (var pair in buildings)
            {
                if (pair.Value.MapInstanceId == mapInstanceId)
                {
                    ids.Add(pair.Key);
                }
            }
            for (var i = 0; i < ids.Count; i++)
            {
                ReleaseBuilding(ids[i]);
            }

            this.GetModel<TemporaryBuildingModel>().ClearMap(mapInstanceId);
        }

        public void ReleaseAll()
        {
            ReleaseOperationRange();
            ReleasePreview();
            RollbackPreparedMap();
            var ids = new List<int>(buildings.Keys);
            for (var i = 0; i < ids.Count; i++)
            {
                ReleaseBuilding(ids[i]);
            }
            promptStates.Clear();
            promptRealtimeProvider = DefaultPromptRealtime;
            presentationUnscaledDeltaProvider = DefaultPresentationUnscaledDelta;
            buildRequestedUnregister?.UnRegister();
            buildRequestedUnregister = null;
            buildingChangedUnregister?.UnRegister();
            buildingChangedUnregister = null;
            promptUnregister?.UnRegister();
            promptUnregister = null;
            if (lateUpdateRegistered)
            {
                LifeCycle.UnregisterLateUpdate(OnLateUpdate);
                lateUpdateRegistered = false;
            }
        }

        private void OnLateUpdate()
        {
            RefreshOperationRange();
            var unscaledDeltaTime = Mathf.Max(0f, presentationUnscaledDeltaProvider());
            foreach (var entry in buildings.Values)
            {
                entry.ManualAttack?.TickEmptyAmmoFeedback(unscaledDeltaTime);
            }

            if (!this.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var player) ||
                !player.IsActive ||
                !this.GetUtility<IPlayerRuntimePortProvider>().TryGetCameraPort(player.UnitId, player.MapInstanceId, out var camera) ||
                camera == null || !camera.IsValid ||
                !camera.TryGetCameraPose(out var position, out var up))
            {
                return;
            }

            FaceManualAttackWorldHuds(position, up);
        }

        /// <summary>沿用共享 LateUpdate 跟随权威玩家位置；两种模式共用水平参考圈，不改变距离判定或场景碰撞。</summary>
        private void RefreshOperationRange()
        {
            var system = this.GetSystem<TemporaryBuildingSystem>();
            if (system == null || (system.Mode != TemporaryBuildingMode.DestroyMode && system.Mode != TemporaryBuildingMode.BuildPreview && system.Mode != TemporaryBuildingMode.BuildProgress) ||
                !this.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var player) || !player.IsActive ||
                !TryGetRuntimeRoot(player.MapInstanceId, out var root, out _))
            {
                ReleaseOperationRange();
                return;
            }
            var radius = system.GetOperationRange();
            if (radius <= 0f) { ReleaseOperationRange(); return; }
            if (operationRangeRing == null || operationRangeMapInstanceId != player.MapInstanceId)
            {
                ReleaseOperationRange();
                // Shader 在正式管线中已有使用，材质由本 Controller 独占，避免污染建筑共享材质。
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if (shader == null) { Log.Error("建筑操作范围圈缺少 Unlit Shader"); return; }
                operationRangeMaterial = new Material(shader) { name = "BuildingOperationRange_Runtime" };
                if (operationRangeMaterial.HasProperty("_Surface"))
                {
                    operationRangeMaterial.SetFloat("_Surface", 1f);
                    operationRangeMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    operationRangeMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    operationRangeMaterial.SetFloat("_ZWrite", 0f);
                    operationRangeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    operationRangeMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                operationRangeRing = new GameObject("BuildingOperationRange").AddComponent<LineRenderer>();
                operationRangeRing.transform.SetParent(root, false);
                operationRangeRing.useWorldSpace = false;
                operationRangeRing.loop = true;
                operationRangeRing.positionCount = operationRangePoints.Length;
                operationRangeRing.widthMultiplier = .05f;
                operationRangeRing.sharedMaterial = operationRangeMaterial;
                operationRangeRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                operationRangeRing.receiveShadows = false;
                operationRangeMapInstanceId = player.MapInstanceId;
            }
            var color = system.Mode == TemporaryBuildingMode.DestroyMode ? new Color(1f, .62f, .22f, .75f) : new Color(.25f, .75f, 1f, .75f);
            if (operationRangeMaterial.HasProperty("_BaseColor")) operationRangeMaterial.SetColor("_BaseColor", color);
            if (operationRangeMaterial.HasProperty("_Color")) operationRangeMaterial.SetColor("_Color", color);
            operationRangeRing.transform.SetPositionAndRotation(player.Position, Quaternion.identity);
            for (var i = 0; i < operationRangePoints.Length; i++)
            {
                var angle = i * Mathf.PI * 2f / operationRangePoints.Length;
                operationRangePoints[i] = new Vector3(Mathf.Cos(angle) * radius, .06f, Mathf.Sin(angle) * radius);
            }
            operationRangeRing.SetPositions(operationRangePoints);
        }

        /// <summary>先隐藏再销毁，运行时延迟销毁也不会残留可见圈；重复释放安全。</summary>
        private void ReleaseOperationRange()
        {
            if (operationRangeRing != null)
            {
                operationRangeRing.gameObject.SetActive(false);
                DestroyOperationRangeObject(operationRangeRing.gameObject);
            }
            if (operationRangeMaterial != null) DestroyOperationRangeObject(operationRangeMaterial);
            operationRangeRing = null;
            operationRangeMaterial = null;
            operationRangeMapInstanceId = 0;
        }

        private static void DestroyOperationRangeObject(UnityEngine.Object target)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }

        private void OnPrompt(TemporaryBuildingPromptEvent evt)
        {
            var now = promptRealtimeProvider();
            if (promptStates.TryGetValue(evt.BuildingRuntimeId, out var previous) &&
                previous.Reason == evt.Reason &&
                previous.AmmoItemId == evt.AmmoItemId &&
                now - previous.DisplayTime < 2f)
            {
                return;
            }

            promptStates[evt.BuildingRuntimeId] = new PromptState(evt.Reason, evt.AmmoItemId, now);
            if (evt.Reason == TemporaryBuildingPromptReason.PressReload)
            {
                Notify.ShowFloatTips(Texts.CombatPressReload);
                return;
            }

            var name = evt.AmmoItemId.ToString();
            if (this.GetUtility<IRuntimeConfigProvider>().TryGetItem(evt.AmmoItemId, out var item, out _) &&
                !string.IsNullOrWhiteSpace(item.NameKey))
            {
                name = Loc.T(item.NameKey);
            }

            Notify.ShowFloatTips(Texts.CombatAmmoInsufficient(name));
        }

        private static float DefaultPromptRealtime()
        {
            return Time.realtimeSinceStartup;
        }

        private static float DefaultPresentationUnscaledDelta()
        {
            return Time.unscaledDeltaTime;
        }

        private bool TryLoadView(string prefabAddress, Transform runtimeRoot, out TemporaryBuildingView view, out string error)
        {
            view = null;
            var prefab = ResourceLoader.LoadAsset<GameObject>(prefabAddress);
            if (prefab == null)
            {
                error = $"临时建筑 Prefab 不存在: {prefabAddress}";
                return false;
            }

            var prefabView = prefab.GetComponent<TemporaryBuildingView>();
            if (prefabView == null)
            {
                error = $"临时建筑 缺少 Prefab view: {prefabAddress}";
                return false;
            }

            view = this.GetUtility<IViewInstanceProvider>().Instantiate(
                prefabView,
                runtimeRoot,
                Vector3.zero,
                Quaternion.identity);
            if (!view.ValidateReferences(out error))
            {
                this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
                view = null;
                return false;
            }

            return true;
        }

        private bool TryGetRuntimeRoot(long mapInstanceId, out Transform runtimeRoot, out string error)
        {
            runtimeRoot = null;
            if (!this.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var player) ||
                !player.IsActive || player.MapInstanceId != mapInstanceId)
            {
                error = "临时建筑地图与本地玩家不匹配";
                return false;
            }

            var handle = this.GetModel<MapSceneModel>().CurrentHandle;
            if (handle == null || !handle.IsValid || handle.Context == null || handle.Context.DynamicObjectRoot == null)
            {
                error = "临时建筑当前地图上下文不可用";
                return false;
            }

            runtimeRoot = handle.Context.DynamicObjectRoot;
            error = null;
            return true;
        }

        private readonly struct ViewEntry
        {
            public ViewEntry(long mapInstanceId, TemporaryBuildingView view, TemporaryManualAttackView manualAttack)
            {
                MapInstanceId = mapInstanceId;
                View = view;
                ManualAttack = manualAttack;
            }

            public long MapInstanceId { get; }
            public TemporaryBuildingView View { get; }
            public TemporaryManualAttackView ManualAttack { get; }
        }

        private readonly struct PreparedBuilding
        {
            public PreparedBuilding(TemporaryBuildingRuntimeState state, TempBuildingDefinition definition, TemporaryManualAttackView manualAttack, int loadedAmmo, TemporaryBuildingView view, float nextShootTime = 0f)
            {
                State = state;
                Definition = definition;
                ManualAttack = manualAttack;
                LoadedAmmo = loadedAmmo;
                View = view;
                NextShootTime = nextShootTime;
            }

            public TemporaryBuildingRuntimeState State { get; }
            public TempBuildingDefinition Definition { get; }
            public TemporaryManualAttackView ManualAttack { get; }
            public int LoadedAmmo { get; }
            public TemporaryBuildingView View { get; }
            public float NextShootTime { get; }
        }

        private readonly struct PromptState
        {
            public PromptState(TemporaryBuildingPromptReason reason, int ammoItemId, float displayTime)
            {
                Reason = reason;
                AmmoItemId = ammoItemId;
                DisplayTime = displayTime;
            }

            public TemporaryBuildingPromptReason Reason { get; }
            public int AmmoItemId { get; }
            public float DisplayTime { get; }
        }
    }
}
