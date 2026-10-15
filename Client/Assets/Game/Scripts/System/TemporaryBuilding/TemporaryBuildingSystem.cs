using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>裁决建筑资格、摆放、消费和拆除事务；表现由视口负责，持久化由存档域捕获。</summary>
    public sealed class TemporaryBuildingSystem : AbstractSystem
    {
        private const float PlacementRayHeight = 2f;
        private const float PlacementRayDistance = 4f;
        private const float AimDeadZoneSqr = 0.001f * 0.001f;
        private Vector3 currentAimDirection = Vector3.forward;
        private Vector3 desiredAimDirection = Vector3.forward;
        private Vector3 firstAlignmentTargetDirection = Vector3.forward;
        private int focusedBuildingId;
        private long pendingBuildActionId;
        private bool operatingFirstAlignment;
        private float operatingRadius;
        private ReloadTransaction pendingReload;
        private bool onlineOperationInFlight;
        private bool deinitialized;

        private readonly struct ReloadTransaction
        {
            public ReloadTransaction(long actionId, int runtimeId, long mapInstanceId, int ammoItemId, int requestedAmount)
            {
                ActionId = actionId;
                RuntimeId = runtimeId;
                MapInstanceId = mapInstanceId;
                AmmoItemId = ammoItemId;
                RequestedAmount = requestedAmount;
            }

            public long ActionId { get; }
            public int RuntimeId { get; }
            public long MapInstanceId { get; }
            public int AmmoItemId { get; }
            public int RequestedAmount { get; }
            public bool IsActive => ActionId > 0;
        }

        public TemporaryBuildingMode Mode => this.GetModel<TemporaryBuildingModel>().Mode;

        /// <summary>移动端独立旋转保留已选位置，不跟手指指向自动转向。</summary>
        public void RotatePreview(float degrees)
        {
            var model = this.GetModel<TemporaryBuildingModel>();
            if (model.Mode != TemporaryBuildingMode.BuildPreview || !CanBuildInCurrentMap(out _)) return;
            UpdateBuildPreview(model.PreviewPosition, model.PreviewRotation * Quaternion.Euler(0, degrees, 0));
        }

        /// <summary>触摸世界只更新摆放位置或拆除目标；确认由显式按钮提交，不能触摸即消费。</summary>
        internal void UpdateTouchPlacement(Vector2 screenPosition)
        {
            if (!CanBuildInCurrentMap(out _) || !this.GetSystem<PlayerCameraSystem>().TryCreateAimRay(screenPosition, out var ray)) return;
            var model = this.GetModel<TemporaryBuildingModel>();
            if (model.Mode == TemporaryBuildingMode.DestroyMode) { RefreshDestroyTarget(ray); return; }
            if (model.Mode != TemporaryBuildingMode.BuildPreview) return;
            var plane = new Plane(Vector3.up, GetPlayer().Position);
            if (plane.Raycast(ray, out var distance) && distance >= 0)
                UpdateBuildPreview(ray.GetPoint(distance), model.PreviewRotation);
        }

        protected override void OnInit()
        {
            deinitialized = false;
            LifeCycle.RegisterUpdate(OnUpdate);
            this.RegisterEvent<PlayerActionEndedEvent>(OnPlayerActionEnded);
            this.RegisterEvent<PlayerActionChangedEvent>(OnPlayerActionChanged);
            this.RegisterEvent<TemporaryDestroyRequestedEvent>(_ =>
            {
                BeginDestroyMode(out var ignored);
            });
            this.RegisterEvent<TemporaryCancelRequestedEvent>(_ => CancelCurrentModeAndReturnToSelection());
            this.RegisterEvent<PlayerInteractRequestedEvent>(_ =>
            {
                if (this.GetModel<TemporaryBuildingModel>().Mode == TemporaryBuildingMode.Normal)
                {
                    TryEnterFocusedManualAttack(out var ignored);
                }
            });
            this.RegisterEvent<MapCurrentReleasedEvent>(evt => ClearForMap(evt.MapInstanceId));
            this.RegisterEvent<CombatantDiedEvent>(OnCombatantDied);
        }

        protected override void OnDeinit()
        {
            if (deinitialized)
            {
                return;
            }

            deinitialized = true;
            LifeCycle.UnregisterUpdate(OnUpdate);
            CancelPendingReload(PlayerActionCancelReason.PlayerUnavailable);
            var model = this.GetModel<TemporaryBuildingModel>();
            if (model.OperatingManualAttackId > 0)
            {
                GetViewPortOrNull()?.SetOperating(model.OperatingManualAttackId, false);
                GetViewPortOrNull()?.SetManualAttackHud(model.OperatingManualAttackId, false, 0, 0, 0, null);
            }

            if (model.DestroyTargetBuildingId > 0)
            {
                GetViewPortOrNull()?.SetDestroyHighlight(model.DestroyTargetBuildingId, false);
            }

            GetViewPortOrNull()?.ReleasePreview();
            model.ResetMode();
            ClearOperatingSession();
            GetViewPortOrNull()?.ReleaseAll();
        }

        /// <summary>目录与提交共用离线地图门禁；基地摆件仅允许配置指定的远征例外，联机一律不开放例外。</summary>
        public bool CanBuildDefinition(TempBuildingDefinition definition)
        {
            if (definition == null) return false;
            if (!definition.BaseOnly) return true;
            if (IsOnlineMode) return false;
            var mapId = this.GetModel<MapSceneModel>().CurrentMapId;
            var domain = this.GetModel<SaveModel>().CurrentDomain;
            return (domain == OfflineSaveDomain.Base && string.Equals(mapId, MapIds.Base, StringComparison.Ordinal)) ||
                (domain == OfflineSaveDomain.Expedition && !string.IsNullOrEmpty(definition.ExpeditionMapId) &&
                 string.Equals(mapId, definition.ExpeditionMapId, StringComparison.Ordinal));
        }

        public bool BeginBuildPreview(string configId, out string error)
        {
            if (!CanOpenBuildSelection(out error))
            {
                return false;
            }

            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuilding(configId, out var definition, out error))
            {
                return false;
            }

            if (!CanBuildDefinition(definition)) { error = "该摆件不能在当前地图建造"; return false; }
            var model = this.GetModel<TemporaryBuildingModel>();
            var requiresManualAttack = this.GetUtility<IRuntimeConfigProvider>()
                .TryGetTempBuildingManualAttack(configId, out _);

            var player = GetPlayer();
            if (GetOperationRange() <= 0f) { error = "玩家建筑操作距离配置不可用"; return false; }
            var position = player.Position + player.Rotation * Vector3.forward * Mathf.Min(2f, GetOperationRange());
            var rotation = Quaternion.LookRotation(Flatten(player.Rotation * Vector3.forward), Vector3.up);
            var viewPort = GetViewPortOrNull();
            if (viewPort == null)
            {
                error = "临时建筑视口不可用";
                return false;
            }

            if (!viewPort.TryCreatePreview(player.MapInstanceId, definition, requiresManualAttack, position, rotation, out error))
            {
                return false;
            }

            model.ResetMode();
            model.Mode = TemporaryBuildingMode.BuildPreview;
            model.PreviewConfigId = configId;
            model.PreviewMapInstanceId = player.MapInstanceId;
            UpdateBuildPreview(position, rotation);
            PublishChanged();
            return true;
        }

        public bool CanOpenBuildSelection(out string error)
        {
            if (this.GetModel<TemporaryBuildingModel>().Mode != TemporaryBuildingMode.Normal)
            {
                error = "临时建筑模式正忙";
                return false;
            }

            return CanBuildInCurrentMap(out error);
        }

        public bool UpdateBuildPreview(Vector3 position, Quaternion rotation)
        {
            var model = this.GetModel<TemporaryBuildingModel>();
            if (model.Mode != TemporaryBuildingMode.BuildPreview &&
                model.Mode != TemporaryBuildingMode.BuildProgress)
            {
                return false;
            }

            model.PreviewPosition = position;
            model.PreviewRotation = rotation;
            model.PreviewValid = ValidatePlacement(model.PreviewConfigId, position, rotation, out var reason);
            model.PreviewReason = reason;
            GetViewPortOrNull()?.UpdatePreview(position, rotation, model.PreviewValid);
            PublishChanged();
            return true;
        }

        public bool ConfirmBuild(out string error)
        {
            error = null;
            this.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.Replaced);
            pendingBuildActionId = 0;
            var model = this.GetModel<TemporaryBuildingModel>();
            if (model.Mode != TemporaryBuildingMode.BuildPreview)
            {
                error = "建造预览未激活";
                return false;
            }

            if (!model.PreviewValid)
            {
                error = model.PreviewReason ?? "放置位置无效";
                return false;
            }

            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuilding(model.PreviewConfigId, out var definition, out error))
            {
                return false;
            }

            if (onlineOperationInFlight)
            {
                error = "联机临时建筑操作进行中";
                return false;
            }

            if (!HasEnoughBuildMaterials(definition, out error))
            {
                return false;
            }

            model.Mode = TemporaryBuildingMode.BuildProgress;
            model.BuildElapsedSeconds = 0f;
            if (!this.GetSystem<PlayerActionSystem>().TryBegin(new PlayerActionRequest(PlayerActionKind.Build, definition.BuildSeconds, definition.IconAddress), out pendingBuildActionId, out error))
            {
                model.Mode = TemporaryBuildingMode.BuildPreview;
                return false;
            }
            PublishChanged();
            return true;
        }

        public void TickForTests(float deltaSeconds)
        {
            this.GetSystem<PlayerActionSystem>().TickForTests(deltaSeconds);
            AdvanceManualAttackOperation(Mathf.Max(0f, deltaSeconds));
        }

        public bool TryEnterManualAttackOperation(int runtimeId, out string error)
        {
            error = null;
            if (!CanBuildInCurrentMap(out error))
            {
                return false;
            }

            var model = this.GetModel<TemporaryBuildingModel>();
            if (model.Mode != TemporaryBuildingMode.Normal)
            {
                error = "临时建筑模式正忙";
                return false;
            }

            if (!model.TryGetBuilding(runtimeId, out var building) || !model.ManualAttacks.ContainsKey(runtimeId) ||
                building.MapInstanceId != GetPlayer().MapInstanceId ||
                !this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuildingManualAttack(building.ConfigId, out var attack))
            {
                error = "临时建筑手动攻击不可用";
                return false;
            }

            if (!TryGetCurrentPlayerMotor(out var motor) || motor.MapInstanceId != building.MapInstanceId)
            {
                error = "临时建筑玩家移动组件不可用";
                return false;
            }

            var radiusOffset = building.Position - motor.Position;
            radiusOffset.y = 0f;
            if (radiusOffset.sqrMagnitude < AimDeadZoneSqr ||
                GetViewPortOrNull()?.TryGetManualAttackPose(runtimeId, out _, out var viewForward) != true)
            {
                error = "临时建筑操作姿态无效";
                return false;
            }

            currentAimDirection = Flatten(viewForward);
            firstAlignmentTargetDirection = Flatten(radiusOffset);
            desiredAimDirection = firstAlignmentTargetDirection;
            operatingRadius = radiusOffset.magnitude;
            operatingFirstAlignment = true;

            model.Mode = TemporaryBuildingMode.OperatingManualAttack;
            model.OperatingManualAttackId = runtimeId;
            GetViewPortOrNull()?.SetOperating(runtimeId, true);
            RefreshManualAttackHud(building, model.ManualAttacks[runtimeId], attack);
            PublishChanged();
            return true;
        }

        public bool BeginDestroyMode(out string error)
        {
            error = null;
            if (!CanBuildInCurrentMap(out error)) return false;
            if (GetOperationRange() <= 0f) { error = "玩家建筑操作距离配置不可用"; return false; }
            var model = this.GetModel<TemporaryBuildingModel>();
            if (model.Mode == TemporaryBuildingMode.OperatingManualAttack)
            {
                error = "操作手动攻击时不能进入销毁模式";
                return false;
            }

            if (model.Mode != TemporaryBuildingMode.Normal)
            {
                error = "临时建筑模式正忙";
                return false;
            }

            model.Mode = TemporaryBuildingMode.DestroyMode;
            model.DestroyTargetBuildingId = 0;
            PublishChanged();
            return true;
        }

        public bool ConfirmDestroy(out string error)
        {
            var model = this.GetModel<TemporaryBuildingModel>();
            // 确认前重新核对距离，避免上一帧选中后玩家移动仍沿用旧资格。
            if (model.Mode == TemporaryBuildingMode.DestroyMode) RefreshDestroyCandidate(model.DestroyCandidateBuildingId);
            if (model.Mode != TemporaryBuildingMode.DestroyMode || model.DestroyTargetBuildingId <= 0)
            {
                error = "销毁模式未激活";
                return false;
            }

            var runtimeId = model.DestroyTargetBuildingId;
            if (!model.TryGetBuilding(runtimeId, out var building))
            {
                ClearDestroyTarget(model);
                error = "临时建筑 不可用";
                PublishChanged();
                return false;
            }

            if (!CanBuildInCurrentMap(out error) || building.MapInstanceId != GetPlayer().MapInstanceId)
            {
                return false;
            }

            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuilding(building.ConfigId, out var definition, out error))
            {
                return false;
            }

            if (IsOnlineMode)
            {
                if (onlineOperationInFlight)
                {
                    error = "联机临时建筑操作进行中";
                    return false;
                }
                DestroyOnlineAsync(building);
                error = null;
                return true;
            }

            var hasManualState = model.ManualAttacks.TryGetValue(runtimeId, out var manual);
            var refunds = BuildDestroyRefunds(definition, hasManualState ? manual.LoadedAmmo : 0);
            var refundPort = this.GetUtility<IBuildingRefundDropPort>();
            var receipt = BuildingRefundDropReceipt.Empty;
            if (refundPort == null)
            {
                error = "建筑返还掉落端口不可用";
                return false;
            }

            if (!refundPort.TryCreateBuildingRefundDrops(building.MapInstanceId, building.Position, building.Rotation, refunds, out receipt, out error))
            {
                return false;
            }

            if (!model.RemoveBuilding(runtimeId))
            {
                refundPort.RollbackBuildingRefundDrops(receipt);
                error = "临时建筑移除失败";
                return false;
            }

            ClearDestroyTarget(model);
            try
            {
                GetViewPortOrNull()?.ReleaseBuilding(runtimeId);
            }
            catch (Exception exception)
            {
                // 领域数据与返还均已提交；不要恢复一个没有有效表现的建筑，也不能重复返还。
                Log.Error("[TemporaryBuildingSystem] 建筑表现移除失败：" + exception.Message);
            }
            this.SendEvent(new TemporaryBuildingDestroyedEvent(runtimeId, building.MapInstanceId));
            this.GetSystem<SaveSystem>().MarkDirty("temporary building destroyed");
            PublishChanged();
            error = null;
            return true;
        }

        public bool RequestManualAttack(out int projectileRuntimeId, out string error)
        {
            projectileRuntimeId = 0;
            var model = this.GetModel<TemporaryBuildingModel>();
            if (model.Mode != TemporaryBuildingMode.OperatingManualAttack ||
                !model.TryGetBuilding(model.OperatingManualAttackId, out var building) || !model.ManualAttacks.TryGetValue(model.OperatingManualAttackId, out var manualState))
            {
                error = "当前没有正在操作的临时建筑手动攻击";
                return false;
            }

            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuilding(building.ConfigId, out _, out error) ||
                !this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuildingManualAttack(building.ConfigId, out var manualAttack))
            {
                error = "临时建筑没有手动攻击能力";
                return false;
            }

            if (pendingReload.IsActive)
            {
                CancelPendingReload(PlayerActionCancelReason.Attack);
                if (!model.ManualAttacks.TryGetValue(building.RuntimeId, out manualState))
                {
                    error = "临时建筑手动攻击不可用";
                    return false;
                }
            }

            var now = Time.time;
            if (now < manualState.NextShootTime)
            {
                error = "临时建筑手动攻击正在冷却";
                return false;
            }

            if (manualState.LoadedAmmo < manualAttack.AmmoAmount)
            {
                var reason = GetOwnedAmount(manualAttack.AmmoItemId) > 0
                    ? TemporaryBuildingPromptReason.PressReload
                    : TemporaryBuildingPromptReason.AmmoInsufficient;
                GetViewPortOrNull()?.PlayManualAttackEmptyAmmoFeedback(building.RuntimeId);
                this.SendEvent(new TemporaryBuildingPromptEvent(reason, manualAttack.AmmoItemId, building.RuntimeId));
                error = "临时建筑弹匣弹药不足";
                return false;
            }

            var direction = currentAimDirection;
            var origin = building.Position + Vector3.up;
            if (GetViewPortOrNull()?.TryGetManualAttackPose(building.RuntimeId, out origin, out var resolvedDirection) != true)
            {
                error = "临时建筑手动攻击姿态不可用";
                return false;
            }

            direction = Flatten(currentAimDirection.sqrMagnitude > AimDeadZoneSqr ? currentAimDirection : resolvedDirection);

            if (IsOnlineMode)
            {
                if (onlineOperationInFlight)
                {
                    error = "联机临时建筑操作进行中";
                    return false;
                }
                FireOnlineAsync(building, direction);
                error = null;
                return true;
            }

            var request = new ProjectileSpawnRequest(
                PlayerModel.LocalPlayerUnitId,
                CombatFaction.Player,
                building.MapInstanceId,
                manualAttack.Damage,
                origin,
                direction,
                manualAttack.ProjectileConfigId);
            if (!this.GetSystem<ProjectileSystem>().TrySpawn(request, out projectileRuntimeId, out error))
            {
                return false;
            }

            model.TrySetManualAttack(building.RuntimeId, manualState
                .WithLoadedAmmo(manualState.LoadedAmmo - manualAttack.AmmoAmount)
                .WithNextShootTime(now + manualAttack.ShootCooldownSeconds));
            this.GetSystem<SaveSystem>().MarkDirty("temporary building fired");
            RefreshManualAttackHud(building, model.ManualAttacks[building.RuntimeId], manualAttack);
            PublishChanged();
            return true;
        }

        /// <summary>指针变化时拾取候选；超距候选保留用于提示及移动后的重新判定，不授权拆除。</summary>
        public void RefreshDestroyTarget(Ray pointerRay)
        {
            var model = this.GetModel<TemporaryBuildingModel>();
            if (model.Mode != TemporaryBuildingMode.DestroyMode) return;
            var candidate = 0;
            if (GetViewPortOrNull()?.TryPickCommittedBuilding(pointerRay, 1000f, out var pickedId) == true)
                candidate = pickedId;
            RefreshDestroyCandidate(candidate);
        }

        /// <summary>复用三维距离规则刷新候选；只在目标或超距状态变化时发布 HUD，避免每帧重绘。</summary>
        private void RefreshDestroyCandidate(int candidate)
        {
            var model = this.GetModel<TemporaryBuildingModel>();
            var nextTarget = 0;
            var tooFar = false;
            var player = GetPlayer();
            if (player.IsActive &&
                model.TryGetBuilding(candidate, out var building) &&
                building.MapInstanceId == player.MapInstanceId &&
                this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuilding(building.ConfigId, out var definition, out _))
            {
                tooFar = !IsWithinOperationRange(player.Position, building.Position, definition.FootprintRadius * building.UniformScale);
                if (!tooFar) nextTarget = candidate;
            }
            else candidate = 0;

            if (model.DestroyTargetBuildingId == nextTarget &&
                model.DestroyCandidateBuildingId == candidate && model.DestroyTargetTooFar == tooFar)
            {
                return;
            }

            if (model.DestroyTargetBuildingId > 0)
            {
                GetViewPortOrNull()?.SetDestroyHighlight(model.DestroyTargetBuildingId, false);
            }

            model.DestroyTargetBuildingId = nextTarget;
            model.DestroyCandidateBuildingId = candidate;
            model.DestroyTargetTooFar = tooFar;
            if (nextTarget > 0)
            {
                GetViewPortOrNull()?.SetDestroyHighlight(nextTarget, true);
            }

            PublishChanged();
        }

        /// <summary>玩家配置拥有唯一操作距离；建造、拆除和范围圈共用，配置缺失时返回 0 并拒绝操作。</summary>
        public float GetOperationRange()
        {
            var player = GetPlayer();
            return player.IsActive && this.GetUtility<IRuntimeConfigProvider>().TryGetPlayer(player.ConfigId, out var definition, out _)
                ? definition.BuildingOperateDistance : 0f;
        }

        /// <summary>到水平占地圆最近边缘的三维距离；半径只补偿水平尺寸，高低差仍占用玩家操作距离。</summary>
        private bool IsWithinOperationRange(Vector3 player, Vector3 building, float footprintRadius)
        {
            var range = GetOperationRange();
            if (range <= 0f) return false;
            var offset = player - building;
            var edgeDistance = Math.Max(0d, Math.Sqrt((double)offset.x * offset.x + (double)offset.z * offset.z) - footprintRadius);
            return edgeDistance * edgeDistance + (double)offset.y * offset.y <= (double)range * range + 0.00001d;
        }

        public bool IsPointerOverOperatingManualAttack(Ray pointerRay)
        {
            var model = this.GetModel<TemporaryBuildingModel>();
            if (model.Mode != TemporaryBuildingMode.OperatingManualAttack ||
                model.OperatingManualAttackId <= 0 ||
                !model.TryGetBuilding(model.OperatingManualAttackId, out var building))
            {
                return false;
            }

            var player = GetPlayer();
            if (!player.IsActive || player.MapInstanceId <= 0 || building.MapInstanceId != player.MapInstanceId)
            {
                return false;
            }

            return GetViewPortOrNull()?.TryPickCommittedBuilding(pointerRay, Mathf.Infinity, out var pickedId) == true &&
                   pickedId == model.OperatingManualAttackId;
        }

        public bool RequestManualReload(out string error)
        {
            error = null;
            var model = this.GetModel<TemporaryBuildingModel>();
            if (pendingReload.IsActive ||
                model.Mode != TemporaryBuildingMode.OperatingManualAttack ||
                !model.TryGetBuilding(model.OperatingManualAttackId, out var building) ||
                !model.ManualAttacks.TryGetValue(building.RuntimeId, out var state) ||
                !this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuildingManualAttack(building.ConfigId, out var attack))
            {
                error = "临时建筑 装填 不可用";
                return false;
            }

            var empty = attack.MagazineCapacity - state.LoadedAmmo;
            var available = GetOwnedAmount(attack.AmmoItemId);
            var requested = Mathf.Min(empty, available);
            if (requested <= 0)
            {
                error = empty <= 0 ? "临时建筑弹匣已满" : "临时建筑装填弹药不足";
                return false;
            }

            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetItem(attack.AmmoItemId, out var item, out error))
            {
                return false;
            }

            if (!this.GetSystem<PlayerActionSystem>().TryBegin(
                    new PlayerActionRequest(PlayerActionKind.TemporaryBuildingReload, attack.ReloadSeconds, item.IconAddress),
                    out var actionId,
                    out error))
            {
                return false;
            }

            pendingReload = new ReloadTransaction(actionId, building.RuntimeId, building.MapInstanceId, attack.AmmoItemId, requested);
            RefreshManualAttackHud(building, state, attack);
            PublishChanged();
            return true;
        }

        public void SetFocusedBuildingForTests(int runtimeId)
        {
            focusedBuildingId = runtimeId;
        }

        public void SetAimDirectionForTests(Vector3 direction)
        {
            SetOperatingAimDirection(direction);
        }

        public void SetOperatingAimDirection(Vector3 direction)
        {
            if (!operatingFirstAlignment && direction.sqrMagnitude >= AimDeadZoneSqr)
            {
                desiredAimDirection = Flatten(direction);
            }
        }

        public bool TryGetOperatingManualAttackPosition(out Vector3 position)
        {
            position = Vector3.zero;
            var model = this.GetModel<TemporaryBuildingModel>();
            if (model.Mode != TemporaryBuildingMode.OperatingManualAttack ||
                !model.TryGetBuilding(model.OperatingManualAttackId, out var building))
            {
                return false;
            }

            position = building.Position;
            return true;
        }

        /// <summary>玩家主动取消建造或拆除后回到目录；地图释放等被动清理仍使用 CancelCurrentMode。</summary>
        public void CancelCurrentModeAndReturnToSelection()
        {
            var mode = Mode;
            CancelCurrentMode();
            if (mode == TemporaryBuildingMode.BuildPreview || mode == TemporaryBuildingMode.BuildProgress ||
                mode == TemporaryBuildingMode.DestroyMode)
            {
                this.SendEvent(new TemporaryBuildRequestedEvent());
            }
        }

        public void CancelCurrentMode()
        {
            var model = this.GetModel<TemporaryBuildingModel>();
            if (model.Mode == TemporaryBuildingMode.Normal)
            {
                return;
            }

            var buildActionId = pendingBuildActionId;
            pendingBuildActionId = 0;
            if (buildActionId > 0) this.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.OwnerCanceled);
            CancelPendingReload(PlayerActionCancelReason.OwnerCanceled);

            if (model.OperatingManualAttackId > 0)
            {
                GetViewPortOrNull()?.SetOperating(model.OperatingManualAttackId, false);
                GetViewPortOrNull()?.SetManualAttackHud(model.OperatingManualAttackId, false, 0, 0, 0, null);
            }

            if (model.DestroyTargetBuildingId > 0)
            {
                GetViewPortOrNull()?.SetDestroyHighlight(model.DestroyTargetBuildingId, false);
            }

            GetViewPortOrNull()?.ReleasePreview();
            model.ResetMode();
            ClearOperatingSession();
            PublishChanged();
        }

        public void ClearForMap(long mapInstanceId)
        {
            if (mapInstanceId <= 0)
            {
                return;
            }

            var model = this.GetModel<TemporaryBuildingModel>();
            if (pendingReload.IsActive && pendingReload.MapInstanceId == mapInstanceId)
            {
                CancelPendingReload(PlayerActionCancelReason.ContextInvalid);
            }
            if (model.PreviewMapInstanceId == mapInstanceId)
            {
                GetViewPortOrNull()?.ReleasePreview();
            }
            GetViewPortOrNull()?.ReleaseMap(mapInstanceId);
            model.ClearMap(mapInstanceId);
            if (model.PreviewMapInstanceId == mapInstanceId ||
                (model.Mode == TemporaryBuildingMode.DestroyMode && GetPlayer().MapInstanceId == mapInstanceId) ||
                (model.OperatingManualAttackId > 0 && !model.Buildings.ContainsKey(model.OperatingManualAttackId)) ||
                (model.DestroyTargetBuildingId > 0 && !model.Buildings.ContainsKey(model.DestroyTargetBuildingId)))
            {
                if (model.OperatingManualAttackId > 0)
                {
                    GetViewPortOrNull()?.SetManualAttackHud(model.OperatingManualAttackId, false, 0, 0, 0, null);
                }
                model.ResetMode();
                ClearOperatingSession();
            }
            PublishChanged();
        }

        public void ApplyOnlineSnapshot(OnlineWorldSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.HasWorld || !IsOnlineMode) return;
            var map = this.GetModel<MapSceneModel>();
            if (map.Status.Value != MapLoadStatus.Loaded || map.CurrentHandle == null || !map.CurrentHandle.IsValid || !string.Equals(map.CurrentMapId, snapshot.MapId, StringComparison.Ordinal)) return;
            var player = GetPlayer();
            if (!player.IsActive || player.MapInstanceId <= 0) return;
            var model = this.GetModel<TemporaryBuildingModel>();
            var incomingIds = new HashSet<int>(snapshot.TemporaryBuildings.Select(building => building.RuntimeId));
            foreach (var runtimeId in model.Buildings.Values.Where(building => building.MapInstanceId == player.MapInstanceId && !incomingIds.Contains(building.RuntimeId)).Select(building => building.RuntimeId).ToArray())
            {
                if (model.OperatingManualAttackId == runtimeId || model.DestroyTargetBuildingId == runtimeId) CancelCurrentMode();
                model.RemoveBuilding(runtimeId);
                GetViewPortOrNull()?.ReleaseBuilding(runtimeId);
            }
            foreach (var online in snapshot.TemporaryBuildings)
            {
                if (model.TryGetBuilding(online.RuntimeId, out _))
                {
                    if (model.ManualAttacks.ContainsKey(online.RuntimeId))
                    {
                        var remaining = Mathf.Max(0f, (online.NextShootAtServerTime - snapshot.ServerTime) / 1000f);
                        model.TrySetManualAttack(online.RuntimeId, new TemporaryManualAttackRuntimeState(Time.time + remaining, online.LoadedAmmo));
                        GetViewPortOrNull()?.SetManualAttackAimDirection(online.RuntimeId, Quaternion.Euler(0f, online.YawDegrees, 0f) * Vector3.forward);
                    }
                    continue;
                }
                if (!this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuilding(online.ConfigId, out var definition, out var error))
                {
                    Log.Warning($"[TemporaryBuildingSystem] 联机建筑配置无效：{online.ConfigId} {error}");
                    continue;
                }
                var state = new TemporaryBuildingRuntimeState(online.RuntimeId, online.ConfigId, player.MapInstanceId, online.Position, Quaternion.Euler(0f, online.YawDegrees, 0f));
                var manual = this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuildingManualAttack(online.ConfigId, out _);
                var remainingCooldown = Mathf.Max(0f, (online.NextShootAtServerTime - snapshot.ServerTime) / 1000f);
                if (GetViewPortOrNull()?.TryCreateBuilding(state, definition, manual, out error) != true ||
                    !model.AddBuilding(state, manual ? new TemporaryManualAttackRuntimeState(Time.time + remainingCooldown, online.LoadedAmmo) : (TemporaryManualAttackRuntimeState?)null))
                {
                    GetViewPortOrNull()?.ReleaseBuilding(online.RuntimeId);
                    Log.Warning($"[TemporaryBuildingSystem] 联机建筑表现创建失败：{online.ConfigId} {error}");
                }
                else if (manual)
                {
                    GetViewPortOrNull()?.SetManualAttackAimDirection(online.RuntimeId, Quaternion.Euler(0f, online.YawDegrees, 0f) * Vector3.forward);
                }
            }
            RefreshCurrentManualAttackHud();
            PublishChanged();
        }

        private async void BuildOnlineAsync(TempBuildingDefinition definition, Vector3 position, Quaternion rotation)
        {
            if (onlineOperationInFlight) return;
            var world = this.GetSystem<OnlineWorldSystem>().Snapshot;
            var mapInstanceId = GetPlayer().MapInstanceId;
            onlineOperationInFlight = true;
            var accepted = await this.GetSystem<OnlineWorldSystem>().OperateTemporaryBuildingAsync(OnlineTemporaryBuildingActionKind.Build, 0, definition.Id, position, rotation.eulerAngles.y, Vector3.zero, 0);
            onlineOperationInFlight = false;
            if (!IsCurrentOnlineContext(world.WorldId, world.MapId, mapInstanceId)) return;
            var model = this.GetModel<TemporaryBuildingModel>();
            if (accepted)
            {
                GetViewPortOrNull()?.ReleasePreview();
                model.ResetMode();
            }
            else if (model.Mode == TemporaryBuildingMode.BuildProgress)
            {
                model.Mode = TemporaryBuildingMode.BuildPreview;
                model.BuildElapsedSeconds = 0f;
                Log.Warning("[TemporaryBuildingSystem] 联机建造请求被拒绝");
            }
            PublishChanged();
        }

        private async void DestroyOnlineAsync(TemporaryBuildingRuntimeState building)
        {
            var world = this.GetSystem<OnlineWorldSystem>().Snapshot;
            var mapInstanceId = building.MapInstanceId;
            onlineOperationInFlight = true;
            var accepted = await this.GetSystem<OnlineWorldSystem>().OperateTemporaryBuildingAsync(OnlineTemporaryBuildingActionKind.Destroy, building.RuntimeId, building.ConfigId, building.Position, building.Rotation.eulerAngles.y, Vector3.zero, GetOnlineBuildingRevision(building.RuntimeId));
            onlineOperationInFlight = false;
            if (!IsCurrentOnlineContext(world.WorldId, world.MapId, mapInstanceId)) return;
            if (accepted) this.GetModel<TemporaryBuildingModel>().ResetMode();
            else Log.Warning("[TemporaryBuildingSystem] 联机拆除请求被拒绝");
            PublishChanged();
        }

        private async void ReloadOnlineAsync(TemporaryBuildingRuntimeState building)
        {
            if (onlineOperationInFlight) return;
            var world = this.GetSystem<OnlineWorldSystem>().Snapshot;
            var mapInstanceId = building.MapInstanceId;
            onlineOperationInFlight = true;
            var accepted = await this.GetSystem<OnlineWorldSystem>().OperateTemporaryBuildingAsync(OnlineTemporaryBuildingActionKind.Reload, building.RuntimeId, building.ConfigId, building.Position, building.Rotation.eulerAngles.y, Vector3.zero, GetOnlineBuildingRevision(building.RuntimeId));
            onlineOperationInFlight = false;
            if (!IsCurrentOnlineContext(world.WorldId, world.MapId, mapInstanceId)) return;
            if (!accepted) Log.Warning("[TemporaryBuildingSystem] 联机建筑装填请求被拒绝");
            RefreshCurrentManualAttackHud();
            PublishChanged();
        }

        private async void FireOnlineAsync(TemporaryBuildingRuntimeState building, Vector3 direction)
        {
            var world = this.GetSystem<OnlineWorldSystem>().Snapshot;
            var mapInstanceId = building.MapInstanceId;
            onlineOperationInFlight = true;
            var accepted = await this.GetSystem<OnlineWorldSystem>().OperateTemporaryBuildingAsync(OnlineTemporaryBuildingActionKind.Fire, building.RuntimeId, building.ConfigId, building.Position, building.Rotation.eulerAngles.y, direction, GetOnlineBuildingRevision(building.RuntimeId));
            onlineOperationInFlight = false;
            if (!IsCurrentOnlineContext(world.WorldId, world.MapId, mapInstanceId)) return;
            if (!accepted) Log.Warning("[TemporaryBuildingSystem] 联机建筑射击请求被拒绝");
            RefreshCurrentManualAttackHud();
            PublishChanged();
        }

        private long GetOnlineBuildingRevision(int runtimeId)
        {
            return this.GetSystem<OnlineWorldSystem>()?.Snapshot.TemporaryBuildings.FirstOrDefault(building => building.RuntimeId == runtimeId).StateRevision ?? 0;
        }

        private bool IsCurrentOnlineContext(long worldId, string mapId, long mapInstanceId)
        {
            if (deinitialized || !IsOnlineMode) return false;
            var world = this.GetSystem<OnlineWorldSystem>()?.Snapshot;
            var player = GetPlayer();
            return world != null && world.HasWorld && !world.ConnectionLost && world.WorldId == worldId &&
                   string.Equals(world.MapId, mapId, System.StringComparison.Ordinal) && player.IsActive &&
                   player.MapInstanceId == mapInstanceId;
        }

        public TemporaryBuildingHudSnapshot GetHudSnapshot()
        {
            var model = this.GetModel<TemporaryBuildingModel>();
            var arrows = GetOwnedAmount(ItemIds.Arrow);
            var cooldown = 0f;
            if (model.Mode == TemporaryBuildingMode.OperatingManualAttack &&
                model.ManualAttacks.TryGetValue(model.OperatingManualAttackId, out var manualState))
            {
                cooldown = manualState.NextShootTime - Time.time;
            }

            return new TemporaryBuildingHudSnapshot(
                model.Mode,
                model.PreviewValid,
                model.PreviewReason,
                GetBuildProgress01(),
                ResolveSelectedBuildingName(model),
                model.Mode == TemporaryBuildingMode.OperatingManualAttack ? arrows : 0,
                cooldown,
                BuildMaterialSnapshots(model.PreviewConfigId),
                model.Mode == TemporaryBuildingMode.DestroyMode && model.TryGetBuilding(model.DestroyTargetBuildingId, out _)
                    ? model.DestroyTargetBuildingId : 0,
                model.DestroyTargetTooFar,
                GetOperationRange());
        }

        private void OnUpdate()
        {
            if (this.GetModel<TemporaryBuildingModel>().Mode != TemporaryBuildingMode.Normal &&
                !CanBuildInCurrentMap(out _))
            {
                CancelCurrentMode();
                return;
            }

            var model = this.GetModel<TemporaryBuildingModel>();
            if (model.Mode == TemporaryBuildingMode.DestroyMode) RefreshDestroyCandidate(model.DestroyCandidateBuildingId);
            if (model.Mode == TemporaryBuildingMode.BuildPreview)
            {
                // 移动端预览位置可保持不动，玩家移动后仍须刷新资格；状态未变时不重复发布 HUD。
                var valid = ValidatePlacement(model.PreviewConfigId, model.PreviewPosition, model.PreviewRotation, out var reason);
                if (model.PreviewValid != valid || model.PreviewReason != reason)
                    UpdateBuildPreview(model.PreviewPosition, model.PreviewRotation);
            }
            AdvanceManualAttackOperation(Time.deltaTime);
        }

        private void OnPlayerActionEnded(PlayerActionEndedEvent evt)
        {
            if (evt.Kind == PlayerActionKind.TemporaryBuildingReload && evt.ActionId == pendingReload.ActionId)
            {
                var reload = pendingReload;
                pendingReload = default;
                if (evt.Completed)
                {
                    CompleteManualReload(reload);
                }
                else
                {
                    RefreshCurrentManualAttackHud();
                    PublishChanged();
                }

                return;
            }

            if (evt.Kind != PlayerActionKind.Build || evt.ActionId != pendingBuildActionId) return;
            pendingBuildActionId = 0;
            if (!evt.Completed) return;
            var model = this.GetModel<TemporaryBuildingModel>();
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuilding(model.PreviewConfigId, out var definition, out var error))
            {
                Log.Error($"[TemporaryBuildingSystem] 建造配置失效：{error}");
                CancelCurrentMode();
                return;
            }

            CommitBuild(definition);
        }

        private void OnPlayerActionChanged(PlayerActionChangedEvent evt)
        {
            if (!pendingReload.IsActive)
            {
                return;
            }

            if (!evt.HasAction ||
                (evt.Snapshot.ActionId == pendingReload.ActionId && evt.Snapshot.Kind == PlayerActionKind.TemporaryBuildingReload))
            {
                RefreshCurrentManualAttackHud();
            }
        }

        private void AdvanceManualAttackOperation(float deltaSeconds)
        {
            var model = this.GetModel<TemporaryBuildingModel>();
            if (model.Mode != TemporaryBuildingMode.OperatingManualAttack ||
                !model.TryGetBuilding(model.OperatingManualAttackId, out var building) ||
                !this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuildingManualAttack(building.ConfigId, out var attack) ||
                !TryGetCurrentPlayerMotor(out var motor) ||
                motor.MapInstanceId != building.MapInstanceId ||
                GetViewPortOrNull()?.TryGetManualAttackPose(building.RuntimeId, out _, out _) != true)
            {
                if (model.Mode == TemporaryBuildingMode.OperatingManualAttack)
                {
                    CancelCurrentMode();
                }

                return;
            }

            var step = attack.OperateRotateDegreesPerSecond * Mathf.Max(0f, deltaSeconds);
            var targetDirection = operatingFirstAlignment ? firstAlignmentTargetDirection : desiredAimDirection;
            var targetRotation = Quaternion.LookRotation(targetDirection, Vector3.up);
            var candidateDirection = Quaternion.RotateTowards(
                Quaternion.LookRotation(currentAimDirection, Vector3.up),
                targetRotation,
                step) * Vector3.forward;
            candidateDirection = Flatten(candidateDirection);

            if (operatingFirstAlignment)
            {
                currentAimDirection = candidateDirection;
                GetViewPortOrNull()?.SetManualAttackAimDirection(building.RuntimeId, currentAimDirection);
                motor.RotateTowards(Quaternion.LookRotation(currentAimDirection, Vector3.up), step);
                this.GetSystem<UnitSystem>().TryUpdateTransform(motor.UnitId, motor.Position, motor.Rotation);
                if (Quaternion.Angle(Quaternion.LookRotation(currentAimDirection, Vector3.up), targetRotation) <= 0.01f)
                {
                    operatingFirstAlignment = false;
                }

                return;
            }

            var desiredPosition = building.Position - candidateDirection * operatingRadius;
            var motion = desiredPosition - motor.Position;
            motion.y = 0f;
            motor.Move(motion);

            var actualOffset = building.Position - motor.Position;
            actualOffset.y = 0f;
            if (actualOffset.sqrMagnitude >= AimDeadZoneSqr)
            {
                currentAimDirection = Flatten(actualOffset);
            }

            GetViewPortOrNull()?.SetManualAttackAimDirection(building.RuntimeId, currentAimDirection);
            motor.RotateTowards(Quaternion.LookRotation(currentAimDirection, Vector3.up), step);
            this.GetSystem<UnitSystem>().TryUpdateTransform(motor.UnitId, motor.Position, motor.Rotation);
        }

        private void CompleteManualReload(ReloadTransaction reload)
        {
            var model = this.GetModel<TemporaryBuildingModel>();
            if (!CanBuildInCurrentMap(out _) ||
                model.Mode != TemporaryBuildingMode.OperatingManualAttack ||
                model.OperatingManualAttackId != reload.RuntimeId ||
                !model.TryGetBuilding(reload.RuntimeId, out var building) ||
                building.MapInstanceId != reload.MapInstanceId ||
                !model.ManualAttacks.TryGetValue(reload.RuntimeId, out var state) ||
                !this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuildingManualAttack(building.ConfigId, out var attack) ||
                attack.AmmoItemId != reload.AmmoItemId)
            {
                RefreshCurrentManualAttackHud();
                return;
            }

            if (IsOnlineMode)
            {
                ReloadOnlineAsync(building);
                return;
            }

            var actual = Mathf.Min(
                reload.RequestedAmount,
                attack.MagazineCapacity - state.LoadedAmmo,
                this.GetSystem<InventorySystem>().GetCarriedItemAmount(reload.AmmoItemId));
            if (actual <= 0)
            {
                RefreshManualAttackHud(building, state, attack);
                PublishChanged();
                return;
            }

            var bag = this.GetModel<PlayerBagModel>().Container;
            var bagSnapshot = bag.CaptureSnapshot();
            var pocket = this.GetModel<PlayerBagModel>().Pocket;
            var pocketSnapshot = pocket.CaptureSnapshot();
            var manualSnapshot = state;
            if (!this.GetSystem<InventorySystem>().CanModifyPlayerBag(out _) ||
                !this.GetSystem<InventorySystem>().TryRemoveCarriedItemAmount(reload.AmmoItemId, actual, out _) ||
                !model.TrySetManualAttack(reload.RuntimeId, state.WithLoadedAmmo(state.LoadedAmmo + actual)))
            {
                bag.RestoreSnapshot(bagSnapshot);
                pocket.RestoreSnapshot(pocketSnapshot);
                model.TrySetManualAttack(reload.RuntimeId, manualSnapshot);
                RefreshManualAttackHud(building, manualSnapshot, attack);
                return;
            }

            RefreshManualAttackHud(building, model.ManualAttacks[reload.RuntimeId], attack);
            this.GetSystem<InventorySystem>().CommitPlayerBagChanged("temporary building reloaded");
            this.GetSystem<SaveSystem>().MarkDirty("temporary building reloaded");
            PublishChanged();
        }

        private void CancelPendingReload(PlayerActionCancelReason reason)
        {
            var reload = pendingReload;
            pendingReload = default;
            if (reload.ActionId > 0 && this.GetSystem<PlayerActionSystem>().TryGetSnapshot(out var snapshot) && snapshot.ActionId == reload.ActionId)
            {
                this.GetSystem<PlayerActionSystem>().CancelActive(reason);
            }
        }

        private void RefreshCurrentManualAttackHud()
        {
            var model = this.GetModel<TemporaryBuildingModel>();
            if (model.Mode == TemporaryBuildingMode.OperatingManualAttack &&
                model.TryGetBuilding(model.OperatingManualAttackId, out var building) &&
                model.ManualAttacks.TryGetValue(building.RuntimeId, out var state) &&
                this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuildingManualAttack(building.ConfigId, out var attack))
            {
                RefreshManualAttackHud(building, state, attack);
            }
        }

        private void RefreshManualAttackHud(TemporaryBuildingRuntimeState building, TemporaryManualAttackRuntimeState state, TempBuildingManualAttackDefinition attack)
        {
            PlayerActionSnapshot? reloadSnapshot = null;
            if (pendingReload.IsActive &&
                pendingReload.RuntimeId == building.RuntimeId &&
                this.GetSystem<PlayerActionSystem>().TryGetSnapshot(out var snapshot) &&
                snapshot.ActionId == pendingReload.ActionId &&
                snapshot.Kind == PlayerActionKind.TemporaryBuildingReload)
            {
                reloadSnapshot = snapshot;
            }

            GetViewPortOrNull()?.SetManualAttackHud(
                building.RuntimeId,
                true,
                attack.AmmoItemId,
                state.LoadedAmmo,
                attack.MagazineCapacity,
                reloadSnapshot);
        }

        private bool TryGetCurrentPlayerMotor(out ICharacterMotorPort motor)
        {
            motor = null;
            var player = GetPlayer();
            return player.IsActive && player.MapInstanceId > 0 &&
                   this.GetUtility<IPlayerRuntimePortProvider>().TryGetMotorPort(player.UnitId, player.MapInstanceId, out motor) &&
                   motor != null && motor.IsValid;
        }

        private void ClearOperatingSession()
        {
            operatingFirstAlignment = false;
            operatingRadius = 0f;
            currentAimDirection = Vector3.forward;
            desiredAimDirection = Vector3.forward;
            firstAlignmentTargetDirection = Vector3.forward;
        }

        private void ClearDestroyTarget(TemporaryBuildingModel model)
        {
            if (model.DestroyTargetBuildingId > 0)
            {
                GetViewPortOrNull()?.SetDestroyHighlight(model.DestroyTargetBuildingId, false);
            }

            model.DestroyTargetBuildingId = 0;
            model.DestroyCandidateBuildingId = 0;
            model.DestroyTargetTooFar = false;
        }

        private void CommitBuild(TempBuildingDefinition definition)
        {
            var model = this.GetModel<TemporaryBuildingModel>();
            if (!CanBuildInCurrentMap(out var error) ||
                model.PreviewMapInstanceId != GetPlayer().MapInstanceId ||
                !ValidatePlacement(definition.Id, model.PreviewPosition, model.PreviewRotation, out error))
            {
                Log.Error($"[TemporaryBuildingSystem] 建造完成校验失败：{error}");
                CancelCurrentMode();
                return;
            }

            if (IsOnlineMode)
            {
                BuildOnlineAsync(definition, model.PreviewPosition, model.PreviewRotation);
                return;
            }

            var bag = this.GetModel<PlayerBagModel>().Container;
            var bagSnapshot = bag.CaptureSnapshot();
            var pocket = this.GetModel<PlayerBagModel>().Pocket;
            var pocketSnapshot = pocket.CaptureSnapshot();
            if (!this.GetSystem<InventorySystem>().TrySpendCarriedItems(definition.Materials, false, out error))
            {
                Log.Warning($"[TemporaryBuildingSystem] 建造扣料失败：{error}");
                CancelCurrentMode();
                return;
            }

            var runtimeId = model.AllocateRuntimeId();
            var building = new TemporaryBuildingRuntimeState(
                runtimeId,
                definition.Id,
                GetPlayer().MapInstanceId,
                model.PreviewPosition,
                model.PreviewRotation);
            if (GetViewPortOrNull()?.TryCreateBuilding(building, definition, this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuildingManualAttack(definition.Id, out _), out error) != true)
            {
                bag.RestoreSnapshot(bagSnapshot);
                // 表现创建失败必须一并退还口袋材料，避免跨容器扣除留下半次建造。
                pocket.RestoreSnapshot(pocketSnapshot);
                GetViewPortOrNull()?.ReleaseBuilding(runtimeId);
                Log.Error($"[TemporaryBuildingSystem] 建造表现创建失败：{error}");
                model.Mode = TemporaryBuildingMode.BuildPreview;
                model.BuildElapsedSeconds = 0f;
                PublishChanged();
                return;
            }

            GetViewPortOrNull()?.ReleasePreview();
            model.AddBuilding(building, this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuildingManualAttack(definition.Id, out _) ? new TemporaryManualAttackRuntimeState(0f) : (TemporaryManualAttackRuntimeState?)null);
            model.ResetMode();
            this.GetSystem<InventorySystem>().CommitPlayerBagChanged("temporary building created");
            this.SendEvent(new TemporaryBuildingCreatedEvent(runtimeId, building.MapInstanceId, building.ConfigId, building.Position));
            this.GetSystem<SaveSystem>().MarkDirty("temporary building created");
            PublishChanged();
        }

        private bool TryEnterFocusedManualAttack(out string error)
        {
            if (focusedBuildingId > 0)
            {
                return TryEnterManualAttackOperation(focusedBuildingId, out error);
            }

            var target = FindNearestManualAttackBuilding(GetPlayer());
            return target > 0
                ? TryEnterManualAttackOperation(target, out error)
                : Fail("没有聚焦的临时建筑手动攻击目标", out error);
        }

        private int FindNearestManualAttackBuilding(UnitSnapshot player)
        {
            var model = this.GetModel<TemporaryBuildingModel>();
            var bestId = 0;
            var bestDistance = float.MaxValue;
            foreach (var pair in model.Buildings)
            {
                if (pair.Value.MapInstanceId != player.MapInstanceId ||
                    !this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuilding(pair.Value.ConfigId, out var definition, out _))
                {
                    continue;
                }

                var distance = Vector3.Distance(player.Position, pair.Value.Position);
                if (!this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuildingManualAttack(pair.Value.ConfigId, out var manualAttack))
                {
                    continue;
                }

                if (distance > manualAttack.InteractDistance)
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestId = pair.Key;
                    bestDistance = distance;
                }
            }

            return bestId;
        }

        private int FindNearestDestroyableBuilding(UnitSnapshot player)
        {
            var model = this.GetModel<TemporaryBuildingModel>();
            var bestId = 0;
            var bestDistance = float.MaxValue;
            foreach (var pair in model.Buildings)
            {
                if (pair.Value.MapInstanceId != player.MapInstanceId ||
                    !this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuilding(pair.Value.ConfigId, out var definition, out _))
                {
                    continue;
                }

                var distance = Vector3.Distance(player.Position, pair.Value.Position);
                if (IsWithinOperationRange(player.Position, pair.Value.Position, definition.FootprintRadius * pair.Value.UniformScale) && distance < bestDistance)
                {
                    bestId = pair.Key;
                    bestDistance = distance;
                }
            }

            return bestId;
        }

        private bool ValidatePlacement(string configId, Vector3 position, Quaternion rotation, out string reason)
        {
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuilding(configId, out var definition, out reason))
            {
                return false;
            }

            if (!CanBuildDefinition(definition)) { reason = "该摆件只能在离线基地建造"; return false; }
            var player = GetPlayer();
            if (!IsWithinOperationRange(player.Position, position, definition.FootprintRadius))
            {
                reason = "放置位置距离过远";
                return false;
            }

            if (!Physics.Raycast(position + Vector3.up * PlacementRayHeight, Vector3.down, out var hit, PlacementRayDistance))
            {
                reason = "放置位置需要地面";
                return false;
            }

            if (Vector3.Angle(hit.normal, Vector3.up) > definition.MaxSlopeDegrees)
            {
                reason = "放置地面坡度过大";
                return false;
            }

            Vector3 center = default, halfSize = default;
            var viewPort = GetViewPortOrNull();
            var hasBox = viewPort != null && viewPort.TryGetPlacementBox(position, rotation, out center, out halfSize);
            if (!hasBox) { center = position; halfSize = Vector3.one * definition.FootprintRadius; }
            var overlapping = hasBox ? Physics.OverlapBox(center, halfSize * .98f, rotation, ~0, QueryTriggerInteraction.Ignore)
                .Any(c => c != null && !c.name.Contains("Ground")) : HasBlockingOverlap(position, definition.FootprintRadius);
            if (overlapping)
            {
                reason = "放置位置与障碍物重叠";
                return false;
            }
            if (definition.BaseOnly && this.GetModel<MapSceneModel>().CurrentMapId == MapIds.Base && !BasePlacementClear(center, halfSize, rotation))
            { reason = "请保留出生点和 NPC 前方的通路"; return false; }

            if (definition.BlocksNavigation && IsNarrowPassage(position, definition.NarrowPassageRadius))
            {
                reason = "放置后会阻断狭窄通道";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>基地新增摆件保护出生点和五处 NPC 接近空间；固定入口以配置出生记录为权威。</summary>
        private bool BasePlacementClear(Vector3 center, Vector3 halfSize, Quaternion rotation)
        {
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetMapSpawns(MapIds.Base, out var spawns, out _)) return false;
            foreach (var spawn in spawns)
            {
                if (spawn.ObjectType != MapSpawnObjectType.Npc && spawn.ObjectType != MapSpawnObjectType.Player) continue;
                var point = new Vector3(spawn.PositionX, spawn.PositionY, spawn.PositionZ);
                if (spawn.ObjectType == MapSpawnObjectType.Npc)
                    point += Quaternion.Euler(0, spawn.RotationY, 0) * Vector3.forward * .65f;
                var offset = Quaternion.Inverse(rotation) * (point - center);
                if (Mathf.Abs(offset.x) < halfSize.x + .8f && Mathf.Abs(offset.z) < halfSize.z + .8f) return false;
            }
            // 出生到南侧入口保持 1.5 米宽主路。
            var right = rotation * Vector3.right;
            var forward = rotation * Vector3.forward;
            var extentX = Mathf.Abs(right.x) * halfSize.x + Mathf.Abs(forward.x) * halfSize.z;
            var extentZ = Mathf.Abs(right.z) * halfSize.x + Mathf.Abs(forward.z) * halfSize.z;
            if (center.z - extentZ < 0 && center.z + extentZ > -14 && Mathf.Abs(center.x) < .75f + extentX) return false;
            return true;
        }

        private bool HasBlockingOverlap(Vector3 position, float radius)
        {
            var colliders = Physics.OverlapSphere(position, radius);
            foreach (var collider in colliders)
            {
                if (collider == null || collider.isTrigger || collider.name.Contains("Ground"))
                {
                    continue;
                }

                return true;
            }

            return this.GetModel<TemporaryBuildingModel>().Buildings.Values.Any(building =>
                Vector3.Distance(building.Position, position) <= radius * 2f);
        }

        private bool IsNarrowPassage(Vector3 position, float radius)
        {
            var blockedX = HasBlockingOverlap(position + Vector3.right * radius, radius * 0.35f) &&
                           HasBlockingOverlap(position + Vector3.left * radius, radius * 0.35f);
            var blockedZ = HasBlockingOverlap(position + Vector3.forward * radius, radius * 0.35f) &&
                           HasBlockingOverlap(position + Vector3.back * radius, radius * 0.35f);
            return blockedX || blockedZ;
        }

        private bool CanBuildInCurrentMap(out string error)
        {
            if(this.GetSystem<PlayerMoveSystem>()?.IsSwimming == true) { error="游泳时不能建造";return false; }
            if (this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing)
            {
                error = "临时建筑 需要 游戏中状态";
                return false;
            }

            var mapId = this.GetModel<MapSceneModel>().CurrentMapId;
            if (IsOnlineMode)
            {
                var world = this.GetSystem<OnlineWorldSystem>()?.Snapshot;
                var self = world?.Players.FirstOrDefault(player => player.UnitId == world.SelfUnitId) ?? default;
                if (world == null || !world.HasWorld || world.ConnectionLost || !string.Equals(world.MapId, mapId, StringComparison.Ordinal) || self.UnitId == 0 || self.IsDead)
                {
                    error = "联机临时建筑需要有效且存活的世界角色";
                    return false;
                }
                if (!this.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var onlinePlayer) || !onlinePlayer.IsActive || onlinePlayer.MapInstanceId <= 0)
                {
                    error = "临时建筑需要活动玩家和当前地图";
                    return false;
                }
                error = null;
                return true;
            }
            var saveDomain = this.GetModel<SaveModel>().CurrentDomain;
            var risk = this.GetModel<PlayerBagModel>().RiskState;
            var isBase = string.Equals(mapId, MapIds.Base, StringComparison.Ordinal) &&
                         saveDomain == OfflineSaveDomain.Base && risk == PlayerBagRiskState.BaseSafe;
            var expedition = this.GetModel<ExpeditionModel>();
            var isExpedition = saveDomain == OfflineSaveDomain.Expedition &&
                               (expedition.Status == ExpeditionStatus.Playing || expedition.Status == ExpeditionStatus.ReturnQualified) &&
                               string.Equals(mapId, expedition.CurrentMapId, StringComparison.Ordinal) &&
                               risk == PlayerBagRiskState.InExpedition;
            if (!isBase && !isExpedition)
            {
                error = "临时建筑需要有效的基地或远征地图和背包状态";
                return false;
            }

            if (this.GetModel<CombatModel>().PlayerHP.Value <= 0)
            {
                error = "临时建筑需要存活玩家";
                return false;
            }

            if (!this.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var player) || !player.IsActive)
            {
                error = "临时建筑 需要 活动玩家";
                return false;
            }

            if (player.MapInstanceId <= 0 || string.IsNullOrWhiteSpace(this.GetModel<MapSceneModel>().CurrentMapId))
            {
                error = "临时建筑 需要 当前地图";
                return false;
            }

            error = null;
            return true;
        }

        private bool HasEnoughBuildMaterials(TempBuildingDefinition definition, out string error)
        {
            var missing = BuildMaterialSnapshots(definition.Id)
                .Where(material => !material.IsEnough)
                .Select(material => $"{material.Name}不足{material.RequiredAmount - material.OwnedAmount}个")
                .ToArray();
            if (missing.Length == 0)
            {
                error = null;
                return true;
            }

            error = string.Join("，", missing);
            return false;
        }

        private IReadOnlyList<TemporaryBuildingMaterialSnapshot> BuildMaterialSnapshots(string configId)
        {
            if (string.IsNullOrWhiteSpace(configId) ||
                !this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuilding(configId, out var definition, out _))
            {
                return Array.Empty<TemporaryBuildingMaterialSnapshot>();
            }

            return definition.Materials
                .Select(material => new TemporaryBuildingMaterialSnapshot(
                    material.ItemId,
                    BuildItemDisplayName(material.ItemId),
                    GetOwnedAmount(material.ItemId),
                    material.Amount))
                .ToArray();
        }

        private int GetOwnedAmount(int itemId)
        {
            if (IsOnlineMode)
            {
                var inventory = this.GetSystem<OnlineWorldSystem>()?.Snapshot.SelfInventory ?? OnlineInventorySnapshot.Empty;
                return inventory.Slots.Where(slot => slot.ItemId == itemId).Sum(slot => slot.Amount);
            }
            return this.GetSystem<InventorySystem>().GetCarriedItemAmount(itemId);
        }

        private bool IsOnlineMode => this.GetModel<MapSceneModel>().CurrentMode == MapPreparationMode.OnlinePlayersOnly;

        private string BuildItemDisplayName(int itemId)
        {
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out var item, out _) ||
                string.IsNullOrWhiteSpace(item.NameKey))
            {
                return itemId.ToString();
            }

            return Loc.T(item.NameKey);
        }

        private UnitSnapshot GetPlayer()
        {
            this.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var player);
            return player;
        }

        private float GetBuildProgress01()
        {
            return pendingBuildActionId > 0 && this.GetSystem<PlayerActionSystem>().TryGetSnapshot(out var snapshot) && snapshot.ActionId == pendingBuildActionId
                ? snapshot.Progress01
                : 0f;
        }

        private static IReadOnlyList<BuildingRefundDrop> BuildDestroyRefunds(TempBuildingDefinition definition, int loadedAmmo)
        {
            var amounts = new Dictionary<int, int>();
            foreach (var material in definition.Materials)
            {
                if (material.ItemId > 0 && material.Amount > 0)
                {
                    amounts[material.ItemId] = amounts.TryGetValue(material.ItemId, out var old) ? old + material.Amount : material.Amount;
                }
            }
            if (loadedAmmo > 0)
            {
                amounts[ItemIds.Arrow] = amounts.TryGetValue(ItemIds.Arrow, out var old) ? old + loadedAmmo : loadedAmmo;
            }

            var offsets = new[] { new Vector3(0.35f, 0f, 0.15f), new Vector3(-0.3f, 0f, 0.22f), new Vector3(0f, 0f, -0.35f) };
            var result = new List<BuildingRefundDrop>();
            var index = 0;
            foreach (var pair in amounts.OrderBy(pair => pair.Key))
            {
                if (pair.Value > 0) result.Add(new BuildingRefundDrop(pair.Key, pair.Value, offsets[index++ % offsets.Length]));
            }
            return result;
        }

        private string ResolveSelectedBuildingName(TemporaryBuildingModel model)
        {
            var configId = model.PreviewConfigId;
            if (model.Mode == TemporaryBuildingMode.DestroyMode && model.TryGetBuilding(model.DestroyTargetBuildingId, out var target))
            {
                configId = target.ConfigId;
            }
            if (string.IsNullOrWhiteSpace(configId) &&
                model.OperatingManualAttackId > 0 &&
                model.TryGetBuilding(model.OperatingManualAttackId, out var operating))
            {
                configId = operating.ConfigId;
            }

            if (string.IsNullOrWhiteSpace(configId) ||
                !this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuilding(configId, out var definition, out _))
            {
                return string.Empty;
            }

            var localized = Loc.T(definition.NameKey);
            return string.IsNullOrWhiteSpace(localized) || string.Equals(localized, definition.NameKey, StringComparison.Ordinal)
                ? definition.Name
                : localized;
        }

        private void OnCombatantDied(CombatantDiedEvent evt)
        {
            if (evt.UnitId == PlayerModel.LocalPlayerUnitId)
            {
                CancelCurrentMode();
            }
        }

        private void PublishChanged()
        {
            this.SendEvent(new TemporaryBuildingChangedEvent(GetHudSnapshot()));
        }

        private ITemporaryBuildingViewPort GetViewPortOrNull()
        {
            try
            {
                return this.GetUtility<ITemporaryBuildingViewPort>();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

        private static Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value.sqrMagnitude < AimDeadZoneSqr ? Vector3.forward : value.normalized;
        }
    }
}
