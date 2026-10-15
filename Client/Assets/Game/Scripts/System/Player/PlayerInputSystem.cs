using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 玩家的一次武器攻击意图；水平朝向用于近战，视线瞄准远程另携带准星射线，不裁决弹药或伤害。
    /// </summary>
    public readonly struct PlayerAttackRequestedEvent
    {
        public PlayerAttackRequestedEvent(Vector3 aimDirection) : this(aimDirection, null) { }

        public PlayerAttackRequestedEvent(Vector3 aimDirection, Ray? projectileAimRay)
        {
            AimDirection = aimDirection;
            ProjectileAimRay = projectileAimRay;
        }

        public Vector3 AimDirection { get; }
        /// <summary>本帧视线瞄准的射线快照；空值沿用水平射击，近战不读取。</summary>
        public Ray? ProjectileAimRay { get; }
    }

    /// <summary>
    /// 表示玩家请求切换当前武器槽位，具体切换规则由装备和武器系统处理。
    /// </summary>
    public readonly struct WeaponSlotSwitchRequestedEvent
    {
        public WeaponSlotSwitchRequestedEvent(WeaponSlot slot)
        {
            Slot = slot;
        }

        public WeaponSlot Slot { get; }
    }

    /// <summary>
    /// 表示玩家请求执行一次当前焦点交互，由统一输入系统在可游玩状态下发布。
    /// </summary>
    public readonly struct PlayerInteractRequestedEvent
    {
    }

    /// <summary>
    /// 管理玩家移动、朝向、武器和交互意图；移动与战斗系统负责执行，不在输入层裁决位移或伤害。
    /// </summary>
    public sealed class PlayerInputSystem : AbstractSystem
    {
        private const float AimDeadZoneSqr = 0.001f * 0.001f;
        private static System.Func<IPlayerInputReader> inputReaderFactoryForTests;
        private static System.Func<Vector3, Vector3?> pointerGroundPositionResolverForTests;
        private static System.Func<Ray?> pointerRayResolverForTests;
        private IPlayerInputReader input;
        private bool hasCurrentFacingDirection;
        private Vector3 currentFacingDirection;
        private bool hasFrameAimRay;
        private Ray frameAimRay;

        public Vector2 MoveInput
        {
            get
            {
                if (!CanAcceptGameplayInput() ||
                    this.GetSystem<SearchContainerSystem>().IsSearching ||
                    TemporaryBuildingSuppressesMovement())
                {
                    return Vector2.zero;
                }

                return input != null ? input.MoveInput : Vector2.zero;
            }
        }

        public bool SprintHeld =>
            CanAcceptGameplayInput() &&
            !TemporaryBuildingSuppressesMovement() &&
            (input != null && input.SprintHeld || this.GetSystem<MobileInputSystem>().SprintRequested);

        protected override void OnInit()
        {
            input = inputReaderFactoryForTests != null
                ? inputReaderFactoryForTests()
                : new PlayerInputReaderAdapter(new PlayerInputCtrl());
            this.RegisterEvent<PlayerReadyEvent>(_ => ResetAimState());
            this.RegisterEvent<WeaponEquipmentChangedEvent>(_ => ResetAimState());
            this.RegisterEvent<MapLoadingStartedEvent>(_ => ResetAimState());
            this.RegisterEvent<AppStateChangedEvent>(_ => ResetMobileLook());
            this.RegisterEvent<MobileInputModeChangedEvent>(_ => ResetMobileLook());
            LifeCycle.RegisterUpdate(OnUpdate);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(OnUpdate);
            input?.Dispose();
            input = null;
            ResetAimState();
        }

        internal static void SetInputReaderFactoryForTests(System.Func<IPlayerInputReader> factory)
        {
            inputReaderFactoryForTests = factory;
        }

        internal static void SetPointerGroundPositionResolverForTests(System.Func<Vector3, Vector3?> resolver)
        {
            pointerGroundPositionResolverForTests = resolver;
        }

        internal static void SetPointerRayResolverForTests(System.Func<Ray?> resolver)
        {
            pointerRayResolverForTests = resolver;
        }

        private void ResetAimState()
        {
            ResetMobileLook();
            hasCurrentFacingDirection = false;
            currentFacingDirection = Vector3.zero;
            hasFrameAimRay = false;
            frameAimRay = default;
        }

        internal void ResetMobileLook() => input?.ResetMobileLook();

        private void OnUpdate()
        {
            if (input == null)
            {
                return;
            }

            var mobile = this.GetSystem<MobileInputSystem>();
            var baseFacingEnabled = mobile.CanAcceptBaseFacingControls();
            var mobileLook = input.ReadMobileLookDelta(mobile.CanAcceptShoulderControls());
            var mobileAttack = mobile.ConsumeShoulderAttack() | mobile.ConsumeBaseAttack();
            // 两路边沿都必须消费，避免键盘与触屏同帧装填把触屏请求遗留到下一帧。
            var reloadPressed = input.ReloadPressedThisFrame | mobile.ConsumeBaseReload();
            var shouldEnable = CanObserveInput();
            if (shouldEnable != input.IsEnabled)
            {
                if (shouldEnable)
                {
                    input.Enable();
                }
                else
                {
                    input.Disable();
                }
            }

            // 在提前返回前提交物理持有状态，门禁由相机系统裁决，禁用期间也能观察释放。
            this.GetSystem<PlayerCameraSystem>().TrySetShoulderAim(input.ShoulderAimHeld);

            if (!shouldEnable)
            {
                return;
            }

            // 建筑目录只保留移动读取；列表点击不同时提交攻击、交互或第二次建造意图。
            if (UIHelper.GetUICtrl<TemporaryBuildingSelectUICtrl>()?.IsShow() == true)
            {
                return;
            }

            // 街机使用独立的双摇杆输入控制其运行态；正式玩家输入只保留底层设备监听，不提交搜打撤指令。
            if (this.GetModel<MapSceneModel>().CurrentMode == MapPreparationMode.ArcadePlayerOnly)
            {
                ResetAimState();
                return;
            }

            var attackPressed = mobileAttack || (!mobile.IsMobileInputMode && input.AttackPressedThisFrame);

            hasFrameAimRay = false;
            if (CanAcceptGameplayInput())
            {
                // 基地方向摇杆只改变人物朝向，不向固定俯视镜头提交旋转意图。
                if (!baseFacingEnabled)
                    this.GetSystem<PlayerCameraSystem>()?.TryApplyLookDelta(
                        mobile.IsMobileInputMode ? mobileLook : input.LookDelta);
                UpdateCurrentFacingDirection(baseFacingEnabled ? mobile.BaseAim : Vector2.zero);
            }

            if (IsOnlinePlayersOnly())
            {
                OnlineWorldSnapshot world=this.GetModel<OnlineWorldModel>().Snapshot.Value;OnlineWorldPlayerSnapshot self=default;foreach(OnlineWorldPlayerSnapshot p in world.Players)if(p.UnitId==world.SelfUnitId){self=p;break;}if(self.UnitId==0||self.IsDead)return;
                OnlineWorldInventorySystem inventory=this.GetSystem<OnlineWorldInventorySystem>();PlayerActionSystem actions=this.GetSystem<PlayerActionSystem>();
                TemporaryBuildingSystem onlineTemporaryBuilding=this.GetSystem<TemporaryBuildingSystem>();TemporaryBuildingMode onlineTemporaryMode=onlineTemporaryBuilding.Mode;
                if(onlineTemporaryMode!=TemporaryBuildingMode.Normal&&input.CancelPressedThisFrame){onlineTemporaryBuilding.CancelCurrentModeAndReturnToSelection();return;}
                if(onlineTemporaryMode!=TemporaryBuildingMode.Normal){HandleTemporaryBuildingInput(onlineTemporaryBuilding,onlineTemporaryMode);return;}
                if(inventory.IsSearching&&(input.MoveInput.sqrMagnitude>0f||attackPressed))inventory.CancelSearch();
                if(input.CancelPressedThisFrame&&actions.TryGetSnapshot(out _)){inventory.CancelSearch();return;}
                if(input.InteractPressedThisFrame&&!UIHelper.HasUIOpen&&!inventory.IsSearching){actions.CancelActive(PlayerActionCancelReason.Interaction);this.SendEvent(new PlayerInteractRequestedEvent());return;}
                if(inventory.IsSearching||UIHelper.HasUIOpen)return;
                if(input.BuildPressedThisFrame){this.SendEvent(new TemporaryBuildRequestedEvent());return;}
                if(input.DestroyPressedThisFrame){this.SendEvent(new TemporaryDestroyRequestedEvent());return;}
                if(reloadPressed&&CanPublishWeaponInput(out _)){inventory.TryBeginRangedReload(out _);return;}
                if(attackPressed&&CanPublishWeaponInput(out _)&&TryGetCurrentFacingDirection(out var onlineAim))this.SendEvent(CreateAttackRequest(onlineAim));
                if(input.HolsterPressedThisFrame&&CanPublishWeaponInput(out _)){this.SendEvent(new WeaponSlotSwitchRequestedEvent(WeaponSlot.None));return;}
                if(input.MeleeSlotPressedThisFrame&&CanPublishWeaponInput(out _))this.SendEvent(new WeaponSlotSwitchRequestedEvent(WeaponSlot.Melee));
                if(input.RangedSlotPressedThisFrame&&CanPublishWeaponInput(out _))this.SendEvent(new WeaponSlotSwitchRequestedEvent(WeaponSlot.Ranged));
                return;
            }

            var searchSystem = this.GetSystem<SearchContainerSystem>();
            var temporaryBuildingSystem = this.GetSystem<TemporaryBuildingSystem>();
            var playerActionSystem = this.GetSystem<PlayerActionSystem>();
            var temporaryMode = temporaryBuildingSystem.Mode;
            if (input.CancelPressedThisFrame && playerActionSystem.TryGetSnapshot(out _))
            {
                playerActionSystem.CancelActive(PlayerActionCancelReason.UserCanceled);
                return;
            }
            if (temporaryMode != TemporaryBuildingMode.Normal && input.CancelPressedThisFrame)
            {
                temporaryBuildingSystem.CancelCurrentModeAndReturnToSelection();
                return;
            }

            if (temporaryMode != TemporaryBuildingMode.Normal)
            {
                HandleTemporaryBuildingInput(temporaryBuildingSystem, temporaryMode);
                return;
            }

            if (searchSystem.IsSearching &&
                (input.MoveInput.sqrMagnitude > 0f || attackPressed))
            {
                searchSystem.CancelSearch();
            }

            if (input.InteractPressedThisFrame)
            {
                if (playerActionSystem.TryGetSnapshot(out var activeAction) &&
                    activeAction.Kind == PlayerActionKind.Extraction)
                {
                    // 撤离中重复交互既不取消当前动作，也不再次发布交互；其它持续动作仍走原有交互替换路径。
                    return;
                }

                playerActionSystem.CancelActive(PlayerActionCancelReason.Interaction);
                var searchWindow = UIHelper.GetUICtrl<SearchContainerUICtrl>();
                if (searchWindow != null && searchWindow.IsShow())
                {
                    searchWindow.Hide();
                    return;
                }

                if (UIHelper.HasUIOpen || searchSystem.IsSearching)
                {
                    return;
                }

                this.SendEvent(new PlayerInteractRequestedEvent());
            }

            if (!CanAcceptGameplayInput() || searchSystem.IsSearching)
            {
                return;
            }

            if (input.BuildPressedThisFrame)
            {
                this.SendEvent(new TemporaryBuildRequestedEvent());
                return;
            }

            if (input.DestroyPressedThisFrame)
            {
                this.SendEvent(new TemporaryDestroyRequestedEvent());
                return;
            }

            if (reloadPressed && CanPublishWeaponInput(out _))
            {
                if (this.GetModel<WeaponEquipmentModel>().IsHoldingTorch) this.GetSystem<WeaponEquipmentSystem>().TryToggleTorch(out _);
                else this.SendEvent(new PlayerReloadRequestedEvent());
                return;
            }

            if (attackPressed &&
                CanPublishWeaponInput(out _) &&
                TryGetCurrentFacingDirection(out var aimDirection))
            {
                playerActionSystem.CancelActive(PlayerActionCancelReason.Attack);
                this.SendEvent(CreateAttackRequest(aimDirection));
            }

            if(input.HolsterPressedThisFrame&&CanPublishWeaponInput(out _)){this.SendEvent(new WeaponSlotSwitchRequestedEvent(WeaponSlot.None));return;}
            if (input.MeleeSlotPressedThisFrame && CanPublishWeaponInput(out _))
            {
                this.SendEvent(new WeaponSlotSwitchRequestedEvent(WeaponSlot.Melee));
            }

            if (input.RangedSlotPressedThisFrame && CanPublishWeaponInput(out _))
            {
                this.SendEvent(new WeaponSlotSwitchRequestedEvent(WeaponSlot.Ranged));
            }
        }

        private void HandleTemporaryBuildingInput(TemporaryBuildingSystem temporaryBuildingSystem, TemporaryBuildingMode temporaryMode)
        {
            if (!CanAcceptGameplayInput())
            {
                return;
            }

            if ((this.GetSystem<MobileInputSystem>()?.IsMobileInputMode ?? false) &&
                (temporaryMode == TemporaryBuildingMode.BuildPreview || temporaryMode == TemporaryBuildingMode.DestroyMode))
            {
                if (TryGetBuildingWorldTap(out var position)) temporaryBuildingSystem.UpdateTouchPlacement(position);
                return;
            }

            // 电脑端也显示旋转和确认按钮；鼠标落在 UI 上时不更新世界预览或消费场景左键。
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if ((temporaryMode == TemporaryBuildingMode.BuildPreview || temporaryMode == TemporaryBuildingMode.DestroyMode) &&
                mouse != null &&
                (UIHelper.GetUICtrl<TemporaryBuildingHudUICtrl>()?.CapturesWorldPointer(mouse.position.ReadValue()) == true ||
                 TouchHitsUi(mouse.position.ReadValue()))) return;

            switch (temporaryMode)
            {
                case TemporaryBuildingMode.BuildPreview:
                    if (CanPublishWeaponInput(out var previewPort) &&
                        TryGetGroundAimPosition(previewPort.Position, out var previewPosition))
                    {
                        // 指针只改变位置，朝向由旋转操作持有，避免移动鼠标覆盖已调整的角度。
                        var rotation = this.GetModel<TemporaryBuildingModel>().PreviewRotation;
                        temporaryBuildingSystem.UpdateBuildPreview(previewPosition, rotation);
                    }

                    if (input.AttackPressedThisFrame)
                    {
                        temporaryBuildingSystem.ConfirmBuild(out var ignored);
                    }

                    break;
                case TemporaryBuildingMode.DestroyMode:
                    if (TryGetAimRay(out var destroyRay))
                    {
                        temporaryBuildingSystem.RefreshDestroyTarget(destroyRay);
                    }

                    if (input.AttackPressedThisFrame)
                    {
                        temporaryBuildingSystem.ConfirmDestroy(out var ignored);
                    }

                    break;
                case TemporaryBuildingMode.OperatingManualAttack:
                    if (!CanPublishWeaponInput(out _))
                    {
                        break;
                    }

                    if (input.ReloadPressedThisFrame)
                    {
                        temporaryBuildingSystem.RequestManualReload(out var ignoredReload);
                    }

                    if (temporaryBuildingSystem.TryGetOperatingManualAttackPosition(out var towerPosition) &&
                        TryGetAimRay(out var operatingRay))
                    {
                        var keepDirection = this.GetModel<PlayerCameraModeModel>().Mode == PlayerCameraMode.TopDown &&
                                            temporaryBuildingSystem.IsPointerOverOperatingManualAttack(operatingRay);
                        if (!keepDirection && TryGetGroundAimPosition(towerPosition, operatingRay, out var targetPosition))
                        {
                            var aimOffset = targetPosition - towerPosition;
                            aimOffset.y = 0f;
                            if (aimOffset.sqrMagnitude > AimDeadZoneSqr)
                                temporaryBuildingSystem.SetOperatingAimDirection(aimOffset.normalized);
                        }
                    }

                    if (input.AttackPressedThisFrame)
                    {
                        temporaryBuildingSystem.RequestManualAttack(out var ignoredId, out var ignored);
                    }

                    break;
            }
        }

        /// <summary>建造和拆除每帧最多消费一个场景新触点；保留摇杆手指，不让 UI 点击穿透。</summary>
        private static bool TryGetBuildingWorldTap(out Vector2 position)
        {
            position = Vector2.zero;
            var hasNewTouch = false;
            var screen = UnityEngine.InputSystem.Touchscreen.current;
            if (screen != null)
            {
                foreach (var touch in screen.touches)
                {
                    if (!touch.press.wasPressedThisFrame) continue;
                    hasNewTouch = true;
                    var candidate = touch.position.ReadValue();
                    if (TouchHitsUi(candidate)) continue;
                    position = candidate;
                    return true;
                }
            }
            // 编辑器鼠标只是无新触点时的模拟入口，不能替代或重复消费真实 UI 触摸。
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (!hasNewTouch && Application.isEditor && mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                position = mouse.position.ReadValue();
                return !TouchHitsUi(position);
            }
            return false;
        }

        /// <summary>建造触摸优先交给可交互 UI，防止确认/旋转按钮的触点又改动世界位置。</summary>
        private static bool TouchHitsUi(Vector2 position)
        {
            var events = UnityEngine.EventSystems.EventSystem.current;
            if (events == null) return false;
            var pointer = new UnityEngine.EventSystems.PointerEventData(events) { position = position };
            var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            events.RaycastAll(pointer, hits);
            return hits.Exists(hit => hit.module is UnityEngine.UI.GraphicRaycaster);
        }

        private bool CanAcceptGameplayInput()
        {
            if (this.GetSystem<AppearanceGallerySceneFlowSystem>().IsOpen) return false;
            var appState = this.GetModel<AppStateModel>().CurrentState.Value;
            if (appState != AppState.Playing)
            {
                return false;
            }

            if (UIHelper.HasUIOpen)
            {
                return false;
            }

            var expedition = this.GetModel<ExpeditionModel>();
            return expedition.Status != ExpeditionStatus.Defeated;
        }

        private bool CanObserveInput()
        {
            return !this.GetSystem<AppearanceGallerySceneFlowSystem>().IsOpen &&
                   this.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing &&
                   this.GetModel<ExpeditionModel>().Status != ExpeditionStatus.Defeated;
        }

        /// <summary>预览只调整位置，不锁定玩家；实际施工和操作固定箭塔期间仍禁止移动。</summary>
        private bool TemporaryBuildingSuppressesMovement()
        {
            var mode = this.GetSystem<TemporaryBuildingSystem>().Mode;
            return mode == TemporaryBuildingMode.BuildProgress ||
                   mode == TemporaryBuildingMode.OperatingManualAttack;
        }

        private bool CanPublishWeaponInput(out ICharacterMotorPort playerPort)
        {
            playerPort = null;
            if (!CanAcceptGameplayInput() ||
                (!IsOnlinePlayersOnly()&&this.GetModel<CombatModel>().PlayerHP.Value <= 0) ||
                !this.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var player) ||
                !player.IsActive)
            {
                return false;
            }

            if (!this.GetUtility<IPlayerRuntimePortProvider>().TryGetMotorPort(
                    player.UnitId,
                    player.MapInstanceId,
                    out playerPort))
            {
                playerPort = null;
                return false;
            }

            return playerPort?.IsValid == true;
        }

        public static bool TryCalculateAimDirection(Vector3 playerPosition, out Vector3 aimDirection)
        {
            aimDirection = Vector3.zero;
            if (!TryCalculatePointerGroundPosition(playerPosition, out var targetPosition))
            {
                return false;
            }

            var offset = targetPosition - playerPosition;
            offset.y = 0f;
            if (offset.sqrMagnitude < AimDeadZoneSqr)
            {
                return false;
            }

            aimDirection = offset.normalized;
            return true;
        }

        public static bool TryCalculatePointerGroundPosition(Vector3 playerPosition, out Vector3 position)
        {
            position = Vector3.zero;
            var resolver = pointerGroundPositionResolverForTests;
            if (resolver != null)
            {
                var resolvedPosition = resolver(playerPosition);
                if (!resolvedPosition.HasValue)
                {
                    return false;
                }

                position = resolvedPosition.Value;
                return true;
            }

            return false;
        }

        private bool IsOnlinePlayersOnly()
        {
            return this.GetModel<MapSceneModel>().CurrentMode == MapPreparationMode.OnlinePlayersOnly;
        }

        public bool TryGetCurrentFacingDirection(out Vector3 direction)
        {
            direction = currentFacingDirection;
            return hasCurrentFacingDirection && IsFinite(direction);
        }

        internal bool TryGetAimRay(out Ray ray)
        {
            ray = default;
            if (hasFrameAimRay) { ray = frameAimRay; return true; }
            var resolver = pointerRayResolverForTests;
            if (resolver != null)
            {
                var resolved = resolver();
                if (!resolved.HasValue || !IsFinite(resolved.Value.origin) || !IsFinite(resolved.Value.direction))
                { ray = default; return false; }
                frameAimRay = resolved.Value;
                hasFrameAimRay = true;
                ray = frameAimRay;
                return true;
            }
            var pointerPosition = Vector2.zero;
            if (this.GetModel<PlayerCameraModeModel>().Mode == PlayerCameraMode.TopDown &&
                (input == null || !input.TryGetPointerScreenPosition(out pointerPosition)))
            { ray = default; return false; }
            var cameraSystem = this.GetSystem<PlayerCameraSystem>();
            if (cameraSystem == null || !cameraSystem.TryCreateAimRay(pointerPosition, out ray) ||
                !IsFinite(ray.origin) || !IsFinite(ray.direction)) return false;
            frameAimRay = ray;
            hasFrameAimRay = true;
            return true;
        }

        /// <summary>越肩和第一人称攻击复用转身已读取的同帧准星射线，俯视继续使用水平意图。</summary>
        private PlayerAttackRequestedEvent CreateAttackRequest(Vector3 facingDirection)
        {
            var mode = this.GetModel<PlayerCameraModeModel>().Mode;
            if ((mode == PlayerCameraMode.Shoulder || mode == PlayerCameraMode.FirstPerson) &&
                TryGetAimRay(out var ray))
                return new PlayerAttackRequestedEvent(facingDirection, ray);
            return new PlayerAttackRequestedEvent(facingDirection);
        }

        internal bool TryGetGroundAimPosition(Vector3 origin, out Vector3 position)
        {
            var resolver = pointerGroundPositionResolverForTests;
            if (resolver != null)
            {
                var resolved = resolver(origin);
                position = resolved ?? Vector3.zero;
                return resolved.HasValue && IsFinite(position);
            }
            if (!TryGetAimRay(out var ray)) { position = Vector3.zero; return false; }
            return TryGetGroundAimPosition(origin, ray, out position);
        }

        internal bool TryGetAimDirection(Vector3 origin, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (!TryGetGroundAimPosition(origin, out var target)) return false;
            var offset = target - origin;
            offset.y = 0f;
            if (!IsFinite(offset) || offset.sqrMagnitude < AimDeadZoneSqr) return false;
            direction = offset.normalized;
            return IsFinite(direction);
        }

        /// <summary>越肩身体跟随镜头水平角；俯视使用独立瞄准，第一人称保留原有射线转向规则。</summary>
        private void UpdateCurrentFacingDirection(Vector2 baseAim)
        {
            if (!TryGetCurrentPlayerMotor(out var motor)) return;
            if (this.GetModel<PlayerCameraModeModel>().Mode == PlayerCameraMode.Shoulder)
            {
                // 地面交点可能落到身体背后；身体朝向只取镜头 Yaw，攻击仍保留完整准星射线。
                if (this.GetSystem<PlayerCameraSystem>().TryGetMovementYawDegrees(out var yaw))
                {
                    currentFacingDirection = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    hasCurrentFacingDirection = true;
                }
                return;
            }
            var baseFacing = this.GetSystem<MobileInputSystem>().IsMobileInputMode &&
                                 this.GetModel<PlayerCameraModeModel>().Mode == PlayerCameraMode.TopDown &&
                                 MobileInputSystem.SupportsTopDownFacingForMap(this.GetModel<MapSceneModel>().CurrentMapId);
            if (baseFacing)
            {
                var facing = hasCurrentFacingDirection ? currentFacingDirection : motor.Rotation * Vector3.forward;
                facing.y = 0f;
                if (!IsFinite(facing) || facing.sqrMagnitude < AimDeadZoneSqr) return;
                if (baseAim.sqrMagnitude > .15f * .15f &&
                    this.GetSystem<PlayerCameraSystem>().TryGetMovementYawDegrees(out var yaw))
                    facing = Quaternion.Euler(0f, yaw, 0f) * new Vector3(baseAim.x, 0f, baseAim.y);
                currentFacingDirection = facing.normalized;
                hasCurrentFacingDirection = true;
                return;
            }
            else if (TryGetAimDirection(motor.Position, out var resolved))
            {
                currentFacingDirection = resolved;
                hasCurrentFacingDirection = true;
                return;
            }
            if (hasCurrentFacingDirection) return;
            var forward = motor.Rotation * Vector3.forward;
            forward.y = 0f;
            if (IsFinite(forward) && forward.sqrMagnitude >= AimDeadZoneSqr)
            {
                currentFacingDirection = forward.normalized;
                hasCurrentFacingDirection = true;
            }
        }

        private bool TryGetCurrentPlayerMotor(out ICharacterMotorPort motor)
        {
            motor = null;
            return this.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var player) &&
                   player.IsActive && player.MapInstanceId > 0 &&
                   this.GetUtility<IPlayerRuntimePortProvider>().TryGetMotorPort(player.UnitId, player.MapInstanceId, out motor) &&
                   motor != null && motor.IsValid;
        }

        private static bool TryGetGroundAimPosition(Vector3 origin, Ray ray, out Vector3 position)
        {
            position = Vector3.zero;
            var plane = new Plane(Vector3.up, origin);
            if (!plane.Raycast(ray, out var enter) || enter < 0f) return false;
            position = ray.GetPoint(enter);
            return IsFinite(position);
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private static bool TryGetPointerRay(out Ray ray)
        {
            ray = default;
            var resolver = pointerRayResolverForTests;
            if (resolver != null)
            {
                var resolved = resolver();
                if (!resolved.HasValue)
                {
                    return false;
                }

                ray = resolved.Value;
                return true;
            }

            return false;
        }

        internal interface IPlayerInputReader : System.IDisposable
        {
            Vector2 MoveInput { get; }
            bool IsEnabled { get; }
            bool AttackPressedThisFrame { get; }
            bool HolsterPressedThisFrame { get; }
            bool MeleeSlotPressedThisFrame { get; }
            bool RangedSlotPressedThisFrame { get; }
            bool InteractPressedThisFrame { get; }
            bool SprintHeld { get; }
            bool BuildPressedThisFrame { get; }
            bool DestroyPressedThisFrame { get; }
            bool CancelPressedThisFrame { get; }
            bool ReloadPressedThisFrame { get; }
            Vector2 LookDelta => Vector2.zero;
            bool ShoulderAimHeld => false;
            Vector2 ReadMobileLookDelta(bool enabled) => Vector2.zero;
            void ResetMobileLook() { }
            bool TryGetPointerScreenPosition(out Vector2 position)
            {
                position = Vector2.zero;
                return false;
            }
            void Enable();
            void Disable();
        }

        private sealed class PlayerInputReaderAdapter : IPlayerInputReader
        {
            private readonly PlayerInputCtrl inner;

            public PlayerInputReaderAdapter(PlayerInputCtrl inner)
            {
                this.inner = inner;
            }

            public Vector2 MoveInput => inner.MoveInput;
            public bool IsEnabled => inner.IsEnabled;
            public bool AttackPressedThisFrame => inner.AttackPressedThisFrame;
            public bool HolsterPressedThisFrame => inner.HolsterPressedThisFrame;
            public bool MeleeSlotPressedThisFrame => inner.MeleeSlotPressedThisFrame;
            public bool RangedSlotPressedThisFrame => inner.RangedSlotPressedThisFrame;
            public bool InteractPressedThisFrame => inner.InteractPressedThisFrame;
            public bool SprintHeld => inner.SprintHeld;
            public bool BuildPressedThisFrame => inner.BuildPressedThisFrame;
            public bool DestroyPressedThisFrame => inner.DestroyPressedThisFrame;
            public bool CancelPressedThisFrame => inner.CancelPressedThisFrame;
            public bool ReloadPressedThisFrame => inner.ReloadPressedThisFrame;
            public Vector2 LookDelta => inner.LookDelta;
            public bool ShoulderAimHeld => inner.ShoulderAimHeld;
            public Vector2 ReadMobileLookDelta(bool enabled) => inner.ReadMobileLookDelta(enabled);
            public void ResetMobileLook() => inner.ResetMobileLook();
            public bool TryGetPointerScreenPosition(out Vector2 position) => inner.TryGetPointerScreenPosition(out position);
            public void Enable() => inner.Enable();
            public void Disable() => inner.Disable();
            public void Dispose() => inner.Dispose();
        }
    }
}
