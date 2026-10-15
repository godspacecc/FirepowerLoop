using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>拥有地图进入时的相机模式、活动玩家相机绑定和鼠标锁定生命周期；不负责加载地图或保存视角。</summary>
    public sealed class PlayerCameraSystem : AbstractSystem
    {
        private const float ShoulderLookSensitivity = 0.12f;
        private const float ShoulderMinPitch = -20f;
        private const float ShoulderMaxPitch = 60f;
        private IPlayerCameraPort cameraPort;
        private ICharacterMotorPort motorPort;
        private int unitId;
        private long mapInstanceId;
        private bool cursorLocked;
        private bool mapLoading;
        /// <summary>已应用普通地图默认视角的实例身份；清理端口时保留，避免恢复或重复就绪覆盖手动视角。</summary>
        private long initializedMapInstanceId;
        private int previousMapUnitId;
        private long previousMapInstanceId;
        private PlayerCameraMode? modeBeforeArcade;
        /// <summary>交谈镜头所有者代次；0 表示普通镜头，关闭旧会话不会释放新会话。</summary>
        private long dialogueCameraOwner;
        private int dialogueNpcUnitId;
        /// <summary>跨门禁变化保留按压周期，不保存到设置；旧按压必须释放后才能重新进入瞄准。</summary>
        private bool shoulderAimHeld;
        private bool shoulderAimRequiresRelease;
        private bool arcadeCamera;
        /// <summary>临时街机视角所属地图；0 为无租约，解绑或其他视角接管后立即失效。</summary>
        private long arcadeFirstPersonMapInstanceId;
        /// <summary>开启瞄准时的联机手持身份；弹药和世界 Revision 变化不代表换武器。</summary>
        private int aimedOnlineItemId;
        private int aimedOnlineSourceSlotIndex = -1;
        /// <summary>本地手持代次不随发射或装填改变，用于区分装备事件中的身份变化。</summary>
        private long aimedLocalHeldRevision;

        /// <summary>会话进入时隐藏本机表现，仅越肩模式接管临时构图；失败不建立所有权。</summary>
        public bool TryBeginDialogueCamera(long sessionId, int npcUnitId, long expectedMapInstanceId)
        {
            if (dialogueCameraOwner != 0 || sessionId <= 0 || expectedMapInstanceId != mapInstanceId || !IsCurrentUnitActive()) return false;
            if (!this.GetSystem<UnitSystem>().TryGet(npcUnitId, out var npc) || !npc.IsActive || npc.MapInstanceId != mapInstanceId) return false;
            if (Mode == PlayerCameraMode.Shoulder && !cameraPort.TrySetDialogueFocus(npc.Position)) return false;
            ClearShoulderAim(true);
            dialogueCameraOwner = sessionId; dialogueNpcUnitId = npcUnitId;
            cameraPort.SetDialoguePresentationHidden(true);
            return true;
        }

        /// <summary>结束匹配会话时恢复原模式与原视角；不会写入已换图的玩家端口。</summary>
        public void EndDialogueCamera(long sessionId)
        {
            if (dialogueCameraOwner != sessionId || sessionId == 0) return;
            dialogueCameraOwner = 0; dialogueNpcUnitId = 0;
            cameraPort?.SetDialoguePresentationHidden(false);
            cameraPort?.TrySetDialogueFocus(null);
            if (IsCurrentUnitActive())
            {
                var model = this.GetModel<PlayerCameraModeModel>();
                cameraPort.ApplyMode(model.Mode, model.ShoulderYawDegrees, model.ShoulderPitchDegrees);
            }
        }

        public PlayerCameraMode Mode => this.GetModel<PlayerCameraModeModel>().Mode;

        public bool IsArcadeFirstPerson(long expectedMapInstanceId) => expectedMapInstanceId > 0 &&
            arcadeFirstPersonMapInstanceId == expectedMapInstanceId && mapInstanceId == expectedMapInstanceId &&
            !mapLoading && arcadeCamera && Mode == PlayerCameraMode.FirstPerson && IsCurrentUnitActive();

        /// <summary>单人街机请求临时第一人称；实际资源或构建失败时恢复俯视，不授予租约。</summary>
        public bool TryBeginArcadeFirstPerson(long expectedMapInstanceId, out string error)
        {
            error = null;
            if (IsArcadeFirstPerson(expectedMapInstanceId)) return true;
            if (expectedMapInstanceId != mapInstanceId || !arcadeCamera || mapLoading ||
                this.GetModel<MapSceneModel>().CurrentMode != MapPreparationMode.ArcadePlayerOnly ||
                Mode != PlayerCameraMode.TopDown || !HasCurrentMapAndPlayer(out _) ||
                this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing || !PlayerAliveAndConnected())
            { error = "当前地图或玩家不能进入街机第一人称"; return false; }
            try
            {
                if (!TrySetMode(PlayerCameraMode.FirstPerson, out error)) return false;
                this.GetModel<PlayerCameraModeModel>().ShoulderPitchDegrees = 0;
                arcadeFirstPersonMapInstanceId = expectedMapInstanceId;
                return true;
            }
            catch (System.Exception exception)
            {
                // 构建失败时回收隐藏状态与镜头子对象；领域层仍保有地面物品。
                arcadeFirstPersonMapInstanceId = 0;
                this.GetModel<PlayerCameraModeModel>().Mode = PlayerCameraMode.TopDown;
                cameraPort?.ApplyMode(PlayerCameraMode.TopDown, 0, 0);
                UnlockCursor();
                error = "第一人称准备失败：" + exception.Message;
                return false;
            }
        }

        /// <summary>仅释放同一地图的街机租约；旧场景恢复请求不能覆盖后续相机。</summary>
        public void EndArcadeFirstPerson(long expectedMapInstanceId)
        {
            if (expectedMapInstanceId == 0 || arcadeFirstPersonMapInstanceId != expectedMapInstanceId) return;
            arcadeFirstPersonMapInstanceId = 0;
            if (mapInstanceId == expectedMapInstanceId && !mapLoading && arcadeCamera &&
                Mode == PlayerCameraMode.FirstPerson && IsCurrentUnitActive())
                TrySetMode(PlayerCameraMode.TopDown, out _);
        }

        protected override void OnInit()
        {
            this.RegisterEvent<PlayerReadyEvent>(OnPlayerReady);
            this.RegisterEvent<ArcadePlayerReadyEvent>(evt => BindPlayer(evt.UnitId, evt.MapInstanceId, true));
            this.RegisterEvent<UnitChangedEvent>(OnUnitChanged);
            this.RegisterEvent<UnitRemovedEvent>(OnUnitRemoved);
            this.RegisterEvent<MapLoadingStartedEvent>(OnMapLoadingStarted);
            this.RegisterEvent<MapLoadingCompletedEvent>(_ => ClearMapLoadingState());
            this.RegisterEvent<MapLoadingFailedEvent>(OnMapLoadingFailed);
            this.RegisterEvent<MapCurrentReleasedEvent>(evt => { if (evt.MapInstanceId == mapInstanceId) ClearBinding(); });
            this.RegisterEvent<AppStateChangedEvent>(evt => { if (evt.Current != AppState.Playing) ClearShoulderAim(true); });
            this.RegisterEvent<WeaponEquipmentChangedEvent>(_ =>
            {
                if (this.GetModel<PlayerCameraModeModel>().IsShoulderAiming && !CanAcceptShoulderAim()) ClearShoulderAim(true);
            });
            this.RegisterEvent<MobileInputModeChangedEvent>(_ => ClearShoulderAim(true));
            UIHelper.BlockingUIChanged += OnBlockingUiChanged;
            LifeCycle.RegisterApplicationFocus(OnApplicationFocus);
            LifeCycle.RegisterApplicationPause(OnApplicationPause);
            LifeCycle.RegisterLateUpdate(OnLateUpdate);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterLateUpdate(OnLateUpdate);
            LifeCycle.UnregisterApplicationFocus(OnApplicationFocus);
            LifeCycle.UnregisterApplicationPause(OnApplicationPause);
            UIHelper.BlockingUIChanged -= OnBlockingUiChanged;
            ClearBinding();
            ClearMapLoadingState();
            var model = this.GetModel<PlayerCameraModeModel>();
            model.Mode = PlayerCameraMode.Shoulder;
            model.ShoulderYawDegrees = 0f;
            model.ShoulderPitchDegrees = 15f;
            modeBeforeArcade = null;
            initializedMapInstanceId = 0;
            shoulderAimHeld = false;
            shoulderAimRequiresRelease = false;
        }

        public bool TrySetMode(PlayerCameraMode mode, out string error)
        {
            if (dialogueCameraOwner != 0) this.GetSystem<DialogueSystem>()?.Close(dialogueCameraOwner);
            var model = this.GetModel<PlayerCameraModeModel>();
            if (!HasCurrentMapAndPlayer(out error) || cameraPort == null || !cameraPort.IsValid)
            {
                if (string.IsNullOrWhiteSpace(error)) error = "当前玩家相机不可用";
                UnlockCursor();
                return false;
            }
            if (model.Mode == mode)
            {
                error = null;
                RefreshCursorLock();
                return true;
            }
            if (!cameraPort.SupportsMode(mode, out error))
            {
                UnlockCursor();
                return false;
            }
            if (mode != PlayerCameraMode.TopDown && model.Mode == PlayerCameraMode.TopDown)
            {
                model.ShoulderYawDegrees = NormalizeYaw(motorPort.Rotation.eulerAngles.y);
                model.ShoulderPitchDegrees = mode == PlayerCameraMode.FirstPerson ? 0f : 15f;
            }
            else if (mode == PlayerCameraMode.Shoulder)
            {
                // 从第一人称返回越肩时保留水平朝向，并同步收回越肩允许的俯仰范围。
                model.ShoulderPitchDegrees = Mathf.Clamp(model.ShoulderPitchDegrees, ShoulderMinPitch, ShoulderMaxPitch);
            }
            this.GetSystem<MobileInputSystem>()?.ClearShoulderInput();
            this.GetSystem<PlayerInputSystem>()?.ResetMobileLook();
            ClearShoulderAim(true);
            model.Mode = mode;
            if (mode != PlayerCameraMode.FirstPerson) arcadeFirstPersonMapInstanceId = 0;
            cameraPort.ApplyMode(mode, model.ShoulderYawDegrees, model.ShoulderPitchDegrees);
            RefreshCursorLock();
            error = null;
            return true;
        }

        /// <summary>统一输入每帧提交右键持有；仅有效桌面越肩远程武器允许开启，释放始终幂等接受。</summary>
        public bool TrySetShoulderAim(bool held)
        {
            shoulderAimHeld = held;
            if (!held)
            {
                shoulderAimRequiresRelease = false;
                ClearShoulderAim(false);
                return true;
            }
            if (shoulderAimRequiresRelease || !CanAcceptShoulderAim())
            {
                ClearShoulderAim(true);
                return false;
            }
            var model = this.GetModel<PlayerCameraModeModel>();
            if (model.IsShoulderAiming) return true;
            if (!cameraPort.TrySetShoulderAim(true))
            {
                ClearShoulderAim(true);
                return false;
            }
            model.IsShoulderAiming = true;
            if (this.GetModel<MapSceneModel>().CurrentMode == MapPreparationMode.OnlinePlayersOnly)
            {
                var equipment = this.GetModel<OnlineWorldModel>().Snapshot.Value.SelfInventory.Equipment;
                aimedOnlineItemId = equipment.RangedWeaponItemId;
                aimedOnlineSourceSlotIndex = equipment.SourceSlotIndex;
            }
            else aimedLocalHeldRevision = this.GetModel<WeaponEquipmentModel>().Revision;
            return true;
        }

        public bool TryApplyLookDelta(Vector2 delta)
        {
            var model = this.GetModel<PlayerCameraModeModel>();
            if (model.Mode == PlayerCameraMode.TopDown || !CanAcceptLookInput() || !IsFinite(delta)) return false;
            if (delta.sqrMagnitude <= 0f) return true;
            model.ShoulderYawDegrees = NormalizeYaw(model.ShoulderYawDegrees + delta.x * ShoulderLookSensitivity);
            // 第一人称允许抬头和低头观察；越肩保持既有俯仰范围。
            var firstPerson = model.Mode == PlayerCameraMode.FirstPerson;
            model.ShoulderPitchDegrees = IsArcadeFirstPerson(mapInstanceId) ? 0 :
                Mathf.Clamp(model.ShoulderPitchDegrees - delta.y * ShoulderLookSensitivity,
                    firstPerson ? -80f : ShoulderMinPitch, firstPerson ? 80f : ShoulderMaxPitch);
            cameraPort?.LateUpdate(model.Mode, model.ShoulderYawDegrees, model.ShoulderPitchDegrees);
            return true;
        }

        public bool TryCreateAimRay(Vector2 pointerScreenPosition, out Ray ray)
        {
            ray = default;
            if (cameraPort == null || !cameraPort.IsValid || !IsCurrentUnitActive()) return false;
            return Mode != PlayerCameraMode.TopDown
                ? cameraPort.TryGetViewportCenterRay(out ray)
                : cameraPort.TryGetScreenPointRay(pointerScreenPosition, out ray);
        }

        public bool TryGetMovementYawDegrees(out float yawDegrees)
        {
            yawDegrees = 0f;
            if (cameraPort == null || !cameraPort.IsValid || !IsCurrentUnitActive()) return false;
            yawDegrees = Mode != PlayerCameraMode.TopDown
                ? this.GetModel<PlayerCameraModeModel>().ShoulderYawDegrees
                : cameraPort.TopDownYawDegrees;
            return IsFinite(yawDegrees);
        }

        public bool TryGetCameraPose(out Vector3 position, out Vector3 up)
        {
            if (cameraPort != null && cameraPort.IsValid && IsCurrentUnitActive()) return cameraPort.TryGetCameraPose(out position, out up);
            position = Vector3.zero;
            up = Vector3.up;
            return false;
        }

        /// <summary>读取活动相机的屏幕法线与上方向，供名称 HUD 保持屏幕平行；无有效玩家相机时返回 false。</summary>
        public bool TryGetScreenOrientation(out Vector3 forward, out Vector3 up)
        {
            forward = Vector3.forward;
            up = Vector3.up;
            if (cameraPort == null || !cameraPort.IsValid || !IsCurrentUnitActive() ||
                !cameraPort.TryGetCameraPose(out _, out up) ||
                !cameraPort.TryGetViewportCenterRay(out var ray)) return false;
            forward = ray.direction;
            return forward.sqrMagnitude > 0.0001f && up.sqrMagnitude > 0.0001f;
        }

        private void OnPlayerReady(PlayerReadyEvent evt)
        {
            BindPlayer(evt.UnitId, evt.MapInstanceId, false);
        }

        /// <summary>玩家提交或恢复时绑定端口；普通新实例默认第三人称，街机使用俯视，重复就绪与失败恢复保留原模式。</summary>
        private void BindPlayer(int readyUnitId, long readyMapInstanceId, bool arcadeMode)
        {
            if (mapLoading && readyUnitId == previousMapUnitId && readyMapInstanceId == previousMapInstanceId)
            {
                return;
            }

            if (!this.GetSystem<UnitSystem>().TryGet(readyUnitId, out var unit) ||
                !unit.IsActive || unit.MapInstanceId != readyMapInstanceId)
            {
                return;
            }
            if (unitId != readyUnitId || mapInstanceId != readyMapInstanceId || arcadeMode) ClearShoulderAim(true);
            unitId = readyUnitId;
            mapInstanceId = readyMapInstanceId;
            var provider = this.GetUtility<IPlayerRuntimePortProvider>();
            if (!provider.TryGetCameraPort(unitId, mapInstanceId, out cameraPort) ||
                !provider.TryGetMotorPort(unitId, mapInstanceId, out motorPort) ||
                cameraPort == null || motorPort == null || !cameraPort.IsValid)
            {
                Log.Error("[PlayerCameraSystem] 玩家相机端口未绑定");
                ClearBinding();
                return;
            }
            cameraPort.SetArcadeMode(arcadeMode);
            arcadeCamera = arcadeMode;
            var model = this.GetModel<PlayerCameraModeModel>();
            if (arcadeMode)
            {
                if (!modeBeforeArcade.HasValue) modeBeforeArcade = model.Mode;
                model.Mode = PlayerCameraMode.TopDown;
            }
            else if (initializedMapInstanceId != readyMapInstanceId)
            {
                // 玩家提交先于地图模型提交；统一普通地图默认视角，不依赖仍可能指向旧地图的 CurrentMapId。
                model.Mode = PlayerCameraMode.Shoulder;
                model.ShoulderYawDegrees = NormalizeYaw(motorPort.Rotation.eulerAngles.y);
                model.ShoulderPitchDegrees = 15f;
                modeBeforeArcade = null;
                this.GetSystem<MobileInputSystem>()?.ClearShoulderInput();
                this.GetSystem<PlayerInputSystem>()?.ResetMobileLook();
            }
            else if (modeBeforeArcade.HasValue)
            {
                model.Mode = modeBeforeArcade.Value;
                modeBeforeArcade = null;
            }
            if (!cameraPort.SupportsMode(model.Mode, out var error))
            {
                Log.Error($"[PlayerCameraSystem] 当前相机模式无法应用：{error}");
                ClearBinding();
                return;
            }
            cameraPort.ApplyMode(model.Mode, model.ShoulderYawDegrees, model.ShoulderPitchDegrees);
            if (!arcadeMode) initializedMapInstanceId = readyMapInstanceId;
            RefreshCursorLock();
        }

        private void OnMapLoadingStarted(MapLoadingStartedEvent evt)
        {
            previousMapUnitId = unitId;
            previousMapInstanceId = mapInstanceId;
            mapLoading = true;
            ClearBinding();
        }

        private void OnMapLoadingFailed(MapLoadingFailedEvent evt)
        {
            var restoreUnitId = previousMapUnitId;
            var restoreMapInstanceId = previousMapInstanceId;
            var shouldRestore = restoreUnitId > 0 && restoreMapInstanceId > 0;
            ClearMapLoadingState();
            if (shouldRestore)
            {
                OnPlayerReady(new PlayerReadyEvent(restoreUnitId, restoreMapInstanceId));
            }
        }

        private void ClearMapLoadingState()
        {
            mapLoading = false;
            previousMapUnitId = 0;
            previousMapInstanceId = 0;
        }

        private void OnLateUpdate()
        {
            if (!IsCurrentUnitActive())
            {
                ClearBinding();
                return;
            }
            var model = this.GetModel<PlayerCameraModeModel>();
            if (model.IsShoulderAiming && !CanAcceptShoulderAim()) ClearShoulderAim(true);
            if (dialogueCameraOwner != 0 && Mode == PlayerCameraMode.Shoulder &&
                this.GetSystem<UnitSystem>().TryGet(dialogueNpcUnitId, out var npc) && npc.IsActive && npc.MapInstanceId == mapInstanceId)
                cameraPort.TrySetDialogueFocus(npc.Position);
            cameraPort?.LateUpdate(model.Mode, model.ShoulderYawDegrees, model.ShoulderPitchDegrees);
            if (motorPort != null && TryGetCameraPose(out var position, out var up)) motorPort.FaceWorldHud(position, up);
            RefreshCursorLock();
        }

        private void OnUnitChanged(UnitChangedEvent evt)
        {
            if (evt.Current.UnitId == unitId && evt.Current.MapInstanceId == mapInstanceId &&
                (evt.ChangeMask & UnitChangeMask.Active) != 0 && !evt.Current.IsActive) ClearBinding();
        }

        private void OnUnitRemoved(UnitRemovedEvent evt)
        {
            if (evt.Snapshot.UnitId == unitId && evt.Snapshot.MapInstanceId == mapInstanceId) ClearBinding();
        }

        private bool HasCurrentMapAndPlayer(out string error)
        {
            if (string.IsNullOrWhiteSpace(this.GetModel<MapSceneModel>().CurrentMapId)) { error = "没有活动地图"; return false; }
            if (!IsCurrentUnitActive() || motorPort == null || !motorPort.IsValid) { error = "没有活动玩家"; return false; }
            error = null;
            return true;
        }

        private bool CanAcceptLookInput()
        {
            return HasCurrentMapAndPlayer(out _) &&
                   this.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing &&
                   !UIHelper.HasUIOpen &&
                   PlayerAliveAndConnected();
        }

        private bool CanAcceptShoulderAim()
        {
            var mobile = this.GetSystem<MobileInputSystem>();
            if (mapLoading || arcadeCamera || dialogueCameraOwner != 0 || Mode != PlayerCameraMode.Shoulder ||
                cameraPort == null || !cameraPort.IsValid || !CanAcceptLookInput() ||
                mobile.IsMobileInputMode || !mobile.ShouldShowReticle()) return false;
            // 联机只读服务器手持快照；不能借离线装备绕过联机空手或换武器。
            if (this.GetModel<MapSceneModel>().CurrentMode == MapPreparationMode.OnlinePlayersOnly)
            {
                var equipment = this.GetModel<OnlineWorldModel>().Snapshot.Value.SelfInventory.Equipment;
                return equipment.ActiveSlot == (int)WeaponSlot.Ranged && equipment.RangedWeaponItemId > 0 &&
                    (!this.GetModel<PlayerCameraModeModel>().IsShoulderAiming ||
                     equipment.RangedWeaponItemId == aimedOnlineItemId && equipment.SourceSlotIndex == aimedOnlineSourceSlotIndex);
            }
            var local = this.GetModel<WeaponEquipmentModel>();
            return local.ActiveSlot == WeaponSlot.Ranged && local.RangedWeaponItemId > 0 &&
                (!this.GetModel<PlayerCameraModeModel>().IsShoulderAiming || local.Revision == aimedLocalHeldRevision);
        }

        /// <summary>撤销临时镜头但不改变模式；被中断的按压不能在恢复游玩后自动重放。</summary>
        private void ClearShoulderAim(bool requireRelease)
        {
            var model = this.GetModel<PlayerCameraModeModel>();
            if (requireRelease && (shoulderAimHeld || model.IsShoulderAiming)) shoulderAimRequiresRelease = true;
            model.IsShoulderAiming = false;
            aimedOnlineItemId = 0;
            aimedOnlineSourceSlotIndex = -1;
            aimedLocalHeldRevision = 0;
            cameraPort?.TrySetShoulderAim(false);
        }

        private void OnBlockingUiChanged(bool blocked) { if (blocked) ClearShoulderAim(true); }
        private void OnApplicationFocus(bool focused) { if (!focused) ClearShoulderAim(true); }
        private void OnApplicationPause(bool paused) { if (paused) ClearShoulderAim(true); }

        private bool PlayerAliveAndConnected()
        {
            if (this.GetModel<MapSceneModel>().CurrentMode == MapPreparationMode.ArcadePlayerOnly)
            {
                var runtime = this.GetModel<ArcadeCombatModel>().Runtime;
                return runtime != null && runtime.PlayerHealth > 0 && runtime.RespawnRemaining <= 0;
            }
            if (this.GetModel<MapSceneModel>().CurrentMode != MapPreparationMode.OnlinePlayersOnly)
                return this.GetModel<CombatModel>().PlayerHP.Value > 0;
            var world = this.GetModel<OnlineWorldModel>().Snapshot.Value;
            if (world.ConnectionLost) return false;
            foreach (var player in world.Players)
                if (player.UnitId == world.SelfUnitId) return !player.IsDead && player.CurrentHP > 0;
            return false;
        }

        private bool IsCurrentUnitActive()
        {
            return unitId > 0 && mapInstanceId > 0 && cameraPort != null &&
                   this.GetSystem<UnitSystem>().TryGet(unitId, out var snapshot) &&
                   snapshot.MapInstanceId == mapInstanceId && snapshot.IsActive;
        }

        private void RefreshCursorLock()
        {
            var shouldLock = Mode != PlayerCameraMode.TopDown && CanAcceptLookInput() &&
                             !this.GetSystem<MobileInputSystem>().IsMobileInputMode;
            if (shouldLock == cursorLocked) return;
            cursorLocked = shouldLock;
            this.GetUtility<ICursorLockPort>()?.SetLocked(shouldLock);
        }

        private void UnlockCursor()
        {
            if (!cursorLocked) return;
            cursorLocked = false;
            this.GetUtility<ICursorLockPort>()?.SetLocked(false);
        }

        private void ClearBinding()
        {
            arcadeFirstPersonMapInstanceId = 0;
            ClearShoulderAim(true);
            cameraPort?.SetDialoguePresentationHidden(false);
            cameraPort?.TrySetDialogueFocus(null);
            dialogueCameraOwner = 0; dialogueNpcUnitId = 0;
            UnlockCursor();
            cameraPort = null;
            motorPort = null;
            unitId = 0;
            mapInstanceId = 0;
            arcadeCamera = false;
        }

        private static float NormalizeYaw(float yaw)
        {
            yaw %= 360f;
            return yaw < 0f ? yaw + 360f : yaw;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);
    }
}
