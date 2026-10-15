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
        /// <summary>候选加载失败时恢复旧地图的第一人称租约。</summary>
        private long previousArcadeFirstPersonMapInstanceId;
        private bool arcadeCamera;
        /// <summary>临时街机视角所属地图；0 为无租约，解绑或其他视角接管后立即失效。</summary>
        private long arcadeFirstPersonMapInstanceId;
        public PlayerCameraMode Mode => this.GetModel<PlayerCameraModeModel>().Mode;

        public bool IsArcadeFirstPerson(long expectedMapInstanceId) => expectedMapInstanceId > 0 && arcadeFirstPersonMapInstanceId == expectedMapInstanceId && mapInstanceId == expectedMapInstanceId && !mapLoading && arcadeCamera && Mode == PlayerCameraMode.FirstPerson && IsCurrentUnitActive();
        /// <summary>单人街机请求临时第一人称；实际资源或构建失败时恢复俯视，不授予租约。</summary>
        public bool TryBeginArcadeFirstPerson(long expectedMapInstanceId, out string error)
        {
            error = null;
            if (IsArcadeFirstPerson(expectedMapInstanceId))
                return true;
            if (expectedMapInstanceId != mapInstanceId || !arcadeCamera || mapLoading || this.GetModel<MapSceneModel>().CurrentMode != MapPreparationMode.ArcadePlayerOnly || Mode != PlayerCameraMode.TopDown || !HasCurrentMapAndPlayer(out _) || this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing || !PlayerAliveAndConnected())
            {
                error = "当前地图或玩家不能进入街机第一人称";
                return false;
            }

            try
            {
                if (!TrySetMode(PlayerCameraMode.FirstPerson, out error))
                    return false;
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
            if (expectedMapInstanceId == 0 || arcadeFirstPersonMapInstanceId != expectedMapInstanceId)
                return;
            arcadeFirstPersonMapInstanceId = 0;
            if (mapInstanceId == expectedMapInstanceId && !mapLoading && arcadeCamera && Mode == PlayerCameraMode.FirstPerson && IsCurrentUnitActive())
                TrySetMode(PlayerCameraMode.TopDown, out _);
        }

        protected override void OnInit()
        {
            this.RegisterEvent<ArcadePlayerReadyEvent>(evt => BindPlayer(evt.UnitId, evt.MapInstanceId, true));
            this.RegisterEvent<UnitChangedEvent>(OnUnitChanged);
            this.RegisterEvent<UnitRemovedEvent>(OnUnitRemoved);
            this.RegisterEvent<MapLoadingStartedEvent>(OnMapLoadingStarted);
            this.RegisterEvent<MapLoadingCompletedEvent>(_ => ClearMapLoadingState());
            this.RegisterEvent<MapLoadingFailedEvent>(OnMapLoadingFailed);
            this.RegisterEvent<MapCurrentReleasedEvent>(evt =>
            {
                if (evt.MapInstanceId == mapInstanceId)
                    ClearBinding();
            });
            this.RegisterEvent<AppStateChangedEvent>(evt =>
            {
                if (evt.Current != AppState.Playing)
                    ClearShoulderAim(true);
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
            initializedMapInstanceId = 0;
        }

        public bool TrySetMode(PlayerCameraMode mode, out string error)
        {
            var model = this.GetModel<PlayerCameraModeModel>();
            if (!HasCurrentMapAndPlayer(out error) || cameraPort == null || !cameraPort.IsValid)
            {
                if (string.IsNullOrWhiteSpace(error))
                    error = "当前玩家相机不可用";
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

            ClearShoulderAim(true);
            model.Mode = mode;
            if (mode != PlayerCameraMode.FirstPerson)
                arcadeFirstPersonMapInstanceId = 0;
            cameraPort.ApplyMode(mode, model.ShoulderYawDegrees, model.ShoulderPitchDegrees);
            RefreshCursorLock();
            error = null;
            return true;
        }

        public bool TryApplyLookDelta(Vector2 delta)
        {
            var model = this.GetModel<PlayerCameraModeModel>();
            if (model.Mode == PlayerCameraMode.TopDown || !CanAcceptLookInput() || !IsFinite(delta))
                return false;
            if (delta.sqrMagnitude <= 0f)
                return true;
            model.ShoulderYawDegrees = NormalizeYaw(model.ShoulderYawDegrees + delta.x * ShoulderLookSensitivity);
            // 第一人称允许抬头和低头观察；越肩保持既有俯仰范围。
            var firstPerson = model.Mode == PlayerCameraMode.FirstPerson;
            model.ShoulderPitchDegrees = IsArcadeFirstPerson(mapInstanceId) ? 0 : Mathf.Clamp(model.ShoulderPitchDegrees - delta.y * ShoulderLookSensitivity, firstPerson ? -80f : ShoulderMinPitch, firstPerson ? 80f : ShoulderMaxPitch);
            cameraPort?.LateUpdate(model.Mode, model.ShoulderYawDegrees, model.ShoulderPitchDegrees);
            return true;
        }

        public bool TryCreateAimRay(Vector2 pointerScreenPosition, out Ray ray)
        {
            ray = default;
            if (cameraPort == null || !cameraPort.IsValid || !IsCurrentUnitActive())
                return false;
            return Mode != PlayerCameraMode.TopDown ? cameraPort.TryGetViewportCenterRay(out ray) : cameraPort.TryGetScreenPointRay(pointerScreenPosition, out ray);
        }

        public bool TryGetMovementYawDegrees(out float yawDegrees)
        {
            yawDegrees = 0f;
            if (cameraPort == null || !cameraPort.IsValid || !IsCurrentUnitActive())
                return false;
            yawDegrees = Mode != PlayerCameraMode.TopDown ? this.GetModel<PlayerCameraModeModel>().ShoulderYawDegrees : cameraPort.TopDownYawDegrees;
            return IsFinite(yawDegrees);
        }

        public bool TryGetCameraPose(out Vector3 position, out Vector3 up)
        {
            if (cameraPort != null && cameraPort.IsValid && IsCurrentUnitActive())
                return cameraPort.TryGetCameraPose(out position, out up);
            position = Vector3.zero;
            up = Vector3.up;
            return false;
        }

        /// <summary>读取活动相机的屏幕法线与上方向，供名称 HUD 保持屏幕平行；无有效玩家相机时返回 false。</summary>
        public bool TryGetScreenOrientation(out Vector3 forward, out Vector3 up)
        {
            forward = Vector3.forward;
            up = Vector3.up;
            if (cameraPort == null || !cameraPort.IsValid || !IsCurrentUnitActive() || !cameraPort.TryGetCameraPose(out _, out up) || !cameraPort.TryGetViewportCenterRay(out var ray))
                return false;
            forward = ray.direction;
            return forward.sqrMagnitude > 0.0001f && up.sqrMagnitude > 0.0001f;
        }

        /// <summary>街机提交与失败恢复绑定正式玩家端口；同一地图实例保持用户选择的模式。</summary>
        private void BindPlayer(int readyUnitId, long readyMapInstanceId, bool arcadeMode)
        {
            if (mapLoading && readyUnitId == previousMapUnitId && readyMapInstanceId == previousMapInstanceId)
                return;
            if (!this.GetSystem<UnitSystem>().TryGet(readyUnitId, out var unit) || !unit.IsActive || unit.MapInstanceId != readyMapInstanceId)
                return;
            unitId = readyUnitId;
            mapInstanceId = readyMapInstanceId;
            var provider = this.GetUtility<IPlayerRuntimePortProvider>();
            if (!provider.TryGetCameraPort(unitId, mapInstanceId, out cameraPort) || !provider.TryGetMotorPort(unitId, mapInstanceId, out motorPort) || cameraPort == null || motorPort == null || !cameraPort.IsValid)
            {
                Log.Error("[PlayerCameraSystem] 玩家相机端口未绑定");
                ClearBinding();
                return;
            }

            cameraPort.SetArcadeMode(true);
            arcadeCamera = true;
            var model = this.GetModel<PlayerCameraModeModel>();
            if (initializedMapInstanceId != readyMapInstanceId)
                model.Mode = PlayerCameraMode.TopDown;
            if (!cameraPort.SupportsMode(model.Mode, out var error))
            {
                Log.Error($"[PlayerCameraSystem] 当前相机模式无法应用：{error}");
                ClearBinding();
                return;
            }

            cameraPort.ApplyMode(model.Mode, model.ShoulderYawDegrees, model.ShoulderPitchDegrees);
            initializedMapInstanceId = readyMapInstanceId;
            RefreshCursorLock();
        }

        private void OnMapLoadingStarted(MapLoadingStartedEvent evt)
        {
            previousArcadeFirstPersonMapInstanceId = arcadeFirstPersonMapInstanceId;
            previousMapUnitId = unitId;
            previousMapInstanceId = mapInstanceId;
            mapLoading = true;
            ClearBinding();
        }

        private void OnMapLoadingFailed(MapLoadingFailedEvent evt)
        {
            var restoreUnitId = previousMapUnitId;
            var restoreMapInstanceId = previousMapInstanceId;
            var restoreFirstPerson = previousArcadeFirstPersonMapInstanceId;
            ClearMapLoadingState();
            if (restoreUnitId > 0 && restoreMapInstanceId > 0)
            {
                BindPlayer(restoreUnitId, restoreMapInstanceId, true);
                if (mapInstanceId == restoreFirstPerson && Mode == PlayerCameraMode.FirstPerson)
                    arcadeFirstPersonMapInstanceId = restoreFirstPerson;
            }
        }

        private void ClearMapLoadingState()
        {
            previousArcadeFirstPersonMapInstanceId = 0;
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
            cameraPort?.LateUpdate(model.Mode, model.ShoulderYawDegrees, model.ShoulderPitchDegrees);
            if (motorPort != null && TryGetCameraPose(out var position, out var up))
                motorPort.FaceWorldHud(position, up);
            RefreshCursorLock();
        }

        private void OnUnitChanged(UnitChangedEvent evt)
        {
            if (evt.Current.UnitId == unitId && evt.Current.MapInstanceId == mapInstanceId && (evt.ChangeMask & UnitChangeMask.Active) != 0 && !evt.Current.IsActive)
                ClearBinding();
        }

        private void OnUnitRemoved(UnitRemovedEvent evt)
        {
            if (evt.Snapshot.UnitId == unitId && evt.Snapshot.MapInstanceId == mapInstanceId)
                ClearBinding();
        }

        private bool HasCurrentMapAndPlayer(out string error)
        {
            if (string.IsNullOrWhiteSpace(this.GetModel<MapSceneModel>().CurrentMapId))
            {
                error = "没有活动地图";
                return false;
            }

            if (!IsCurrentUnitActive() || motorPort == null || !motorPort.IsValid)
            {
                error = "没有活动玩家";
                return false;
            }

            error = null;
            return true;
        }

        private bool CanAcceptLookInput()
        {
            return HasCurrentMapAndPlayer(out _) && this.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing && !UIHelper.HasUIOpen && PlayerAliveAndConnected();
        }

        /// <summary>取消旧临时构图，确保切图、失焦或阻断界面不留下相机偏移。</summary>
        private void ClearShoulderAim(bool requireRelease)
        {
            this.GetModel<PlayerCameraModeModel>().IsShoulderAiming = false;
            cameraPort?.TrySetShoulderAim(false);
        }

        private void OnBlockingUiChanged(bool blocked)
        {
            if (blocked)
                ClearShoulderAim(true);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
                ClearShoulderAim(true);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                ClearShoulderAim(true);
        }

        /// <summary>单人读取本局运行态，双人读取服务器本机快照与连接状态。</summary>
        private bool PlayerAliveAndConnected()
        {
            var runtime = this.GetModel<ArcadeCombatModel>().Runtime;
            if (runtime != null)
                return runtime.PlayerHealth > 0 && runtime.RespawnRemaining <= 0;
            var online = this.GetModel<OnlineArcadeRunModel>().Snapshot.Value;
            if (online.HasRun)
            {
                var account = this.GetModel<OnlineAccountModel>().Snapshot.Value;
                if (!account.IsAuthenticated || online.BattlePaused || online.Battle == null)
                    return false;
                foreach (var connection in online.Connections)
                    if (connection.PlayerId == account.PlayerId && !connection.Connected)
                        return false;
                foreach (var player in online.Battle.Players)
                    if (player.PlayerId == account.PlayerId)
                        return player.Alive && player.CurrentHealth > 0 && player.RespawnSeconds <= 0;
                return false;
            }

            return false;
        }

        private bool IsCurrentUnitActive()
        {
            return unitId > 0 && mapInstanceId > 0 && cameraPort != null && this.GetSystem<UnitSystem>().TryGet(unitId, out var snapshot) && snapshot.MapInstanceId == mapInstanceId && snapshot.IsActive;
        }

        private void RefreshCursorLock()
        {
            var shouldLock = Mode != PlayerCameraMode.TopDown && CanAcceptLookInput() && !this.GetSystem<MobileInputSystem>().IsMobileInputMode;
            if (shouldLock == cursorLocked)
                return;
            cursorLocked = shouldLock;
            this.GetUtility<ICursorLockPort>()?.SetLocked(shouldLock);
        }

        private void UnlockCursor()
        {
            if (!cursorLocked)
                return;
            cursorLocked = false;
            this.GetUtility<ICursorLockPort>()?.SetLocked(false);
        }

        private void ClearBinding()
        {
            arcadeFirstPersonMapInstanceId = 0;
            ClearShoulderAim(true);
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
