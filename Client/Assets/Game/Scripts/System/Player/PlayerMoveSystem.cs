using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 玩家移动系统，按相机水平角转换位移，越肩身体同步镜头、其它视角按瞄准转向；将真实变换写回 UnitSystem。
    /// 不拥有镜头输入、动画或战斗命中规则。
    /// </summary>
    public sealed class PlayerMoveSystem : AbstractSystem
    {
        private const float GroundedDownSpeed = -1f;

        private ICharacterMotorPort motorPort;
        private int unitId;
        private long mapInstanceId;
        private float moveSpeed;
        private float turnSpeed;
        private float verticalVelocity;
        /// <summary>当前本地玩家的水陆迟滞状态，切图清理；恢复由玩家创建事务写入。</summary>
        private bool swimming;
        public bool IsSwimming
        {
            get
            {
                var layout=(this.GetModel<MapSceneModel>().CurrentHandle as GeneratedValleyContentHandle)?.Layout;
                return motorPort!=null && motorPort.IsValid && ValleySwimmingRules.TrySample(layout,motorPort.Position,out _,out var depth) &&
                    ValleySwimmingRules.ShouldSwim(depth,swimming);
            }
        }
        internal void RestoreSwimming(bool value) { swimming=value;verticalVelocity=0; }

        protected override void OnInit()
        {
            this.RegisterEvent<PlayerReadyEvent>(OnPlayerReady);
            this.RegisterEvent<PlayerMoveSpeedChangedEvent>(OnPlayerMoveSpeedChanged);
            this.RegisterEvent<UnitChangedEvent>(OnUnitChanged);
            this.RegisterEvent<UnitRemovedEvent>(OnUnitRemoved);
            LifeCycle.RegisterUpdate(OnUpdate);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(OnUpdate);
            motorPort = null;
        }

        public static bool CanProcessMovement(AppState state, bool hasController, bool controllerEnabled, bool unitActive)
        {
            return state == AppState.Playing && hasController && controllerEnabled && unitActive;
        }

        public static Vector3 CalculateHorizontalMotion(Vector2 normalizedInput, float yawDegrees, float speed, float deltaTime)
        {
            if (speed <= 0f || deltaTime <= 0f)
            {
                return Vector3.zero;
            }

            var value = Vector2.ClampMagnitude(normalizedInput, 1f);
            var worldDirection = Quaternion.Euler(0f, yawDegrees, 0f) * new Vector3(value.x, 0f, value.y);
            return worldDirection * speed * deltaTime;
        }

        internal float MoveSpeedCache => moveSpeed;

        internal void RestoreMoveSpeedCache(float value)
        {
            moveSpeed = value;
        }

        private void OnPlayerReady(PlayerReadyEvent evt)
        {
            if(mapInstanceId!=evt.MapInstanceId)swimming=false;
            unitId = evt.UnitId;
            mapInstanceId = evt.MapInstanceId;
            verticalVelocity = 0f;
            RefreshPlayerMovementConfig(unitId);
            if (!this.GetUtility<IPlayerRuntimePortProvider>().TryGetMotorPort(unitId, mapInstanceId, out motorPort))
            {
                Log.Error("[PlayerMoveSystem] 玩家移动端口未绑定");
                return;
            }

        }

        private void OnUpdate()
            => AdvanceMovement(Time.deltaTime);

        /// <summary>共享生命周期以现实秒推进移动与水陆状态；显式步长保证暂停和确定性验证使用同一规则。</summary>
        internal void AdvanceMovement(float deltaTime)
        {
            var unitActive = this.GetSystem<UnitSystem>().TryGet(unitId, out var snapshot) && snapshot.MapInstanceId == mapInstanceId && snapshot.IsActive;
            // 受击颜色和持枪压制由表现端自行计时；即使本帧不能移动，活动玩家仍需推进恢复。
            if (unitActive && motorPort != null && motorPort.IsValid)
            {
                motorPort.TickCombatFeedback(deltaTime);
            }

            if (this.GetModel<MapSceneModel>().CurrentMode == MapPreparationMode.OnlinePlayersOnly &&
                (this.GetModel<OnlineWorldModel>().Snapshot.Value.ConnectionLost||IsOnlineDead()))
            {
                verticalVelocity = 0f;
                return;
            }
            var canMove = CanProcessMovement(
                this.GetModel<AppStateModel>().CurrentState.Value,
                motorPort != null && motorPort.IsValid,
                motorPort != null && motorPort.IsEnabled,
                unitActive);

            if (!canMove)
            {
                // 暂停或候选加载暂时停用玩家时保留水陆迟滞；实体移除事件才释放绑定。
                verticalVelocity = 0f;
                return;
            }

            var moveInput = this.GetSystem<PlayerInputSystem>().MoveInput;
            var layout=(this.GetModel<MapSceneModel>().CurrentHandle as GeneratedValleyContentHandle)?.Layout;
            var wasSwimming=swimming;
            swimming=IsSwimming;
            if(swimming && !wasSwimming && this.GetSystem<PlayerActionSystem>().TryGetSnapshot(out var action) && ValleySwimmingRules.BlocksAction(action.Kind))
                this.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.ContextInvalid);
            var sprint = this.GetSystem<PlayerConditionSystem>().TickTili(
                deltaTime,
                moveInput.sqrMagnitude > 0.0001f,
                !swimming && this.GetSystem<PlayerInputSystem>().SprintHeld);
            var cameraSystem = this.GetSystem<PlayerCameraSystem>();
            // 玩家 Rotation 会跟随鼠标瞄准，不能用来解释移动输入，否则鼠标会间接旋转移动坐标系。
            var movementYawDegrees = motorPort.MovementYawDegrees;
            if (cameraSystem.TryGetMovementYawDegrees(out var cameraYawDegrees))
            {
                movementYawDegrees = cameraYawDegrees;
            }
            var horizontal = CalculateHorizontalMotion(
                moveInput,
                movementYawDegrees,
                moveSpeed * (swimming ? ValleySwimmingRules.SpeedMultiplier : sprint.SpeedMultiplier) * (float)(this.GetSystem<CarryLoadSystem>().AppliesInCurrentMap ? this.GetSystem<CarryLoadSystem>().Current.Speed : 1),
                deltaTime);
            var vertical = CalculateVerticalMotion(deltaTime);
            if(layout?.GenerationVersion==3)
            {
                // 整个地图边界使用实体墙，此处再约束本机位移，水面也不能从河口游出。
                var next=motorPort.Position+horizontal;
                horizontal.x=Mathf.Clamp(next.x,-511,511)-motorPort.Position.x;
                horizontal.z=Mathf.Clamp(next.z,-511,511)-motorPort.Position.z;
                next=motorPort.Position+horizontal;
                if(ValleySwimmingRules.TrySample(layout,next,out var water,out var depth) && ValleySwimmingRules.ShouldSwim(depth,swimming))
                {
                    swimming=true;verticalVelocity=0;
                    var target=Mathf.Max(layout.HeightAt(next.x,next.z)+.03f,water-ValleySwimmingRules.SurfaceFootDepth);
                    vertical=Vector3.up*Mathf.Clamp(target-motorPort.Position.y,-2f*deltaTime,2f*deltaTime);
                }
            }
            motorPort.SetSwimmingPresentation(swimming);
            motorPort.Move(horizontal + vertical);
            RotateTowardAim(deltaTime);
            this.GetSystem<UnitSystem>().TryUpdateTransform(unitId, motorPort.Position, motorPort.Rotation);
        }

        private bool IsOnlineDead(){OnlineWorldSnapshot world=this.GetModel<OnlineWorldModel>().Snapshot.Value;foreach(OnlineWorldPlayerSnapshot p in world.Players)if(p.UnitId==world.SelfUnitId)return p.IsDead||p.CurrentHP<=0;return true;}

        private Vector3 CalculateVerticalMotion(float deltaTime)
        {
            if (motorPort.IsGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = GroundedDownSpeed;
            }
            else
            {
                verticalVelocity += Physics.gravity.y * deltaTime;
            }

            return new Vector3(0f, verticalVelocity * deltaTime, 0f);
        }

        /// <summary>移动后刷新身体朝向；越肩立即同步镜头以保持背对视线，其它模式沿用配置转速。</summary>
        private void RotateTowardAim(float deltaTime)
        {
            if (this.GetSystem<TemporaryBuildingSystem>().Mode == TemporaryBuildingMode.OperatingManualAttack ||
                !this.GetSystem<PlayerInputSystem>().TryGetCurrentFacingDirection(out var direction))
            {
                return;
            }
            if (this.GetModel<PlayerCameraModeModel>().Mode == PlayerCameraMode.Shoulder)
            {
                motorPort.FaceDirection(direction);
                return;
            }
            var target = Quaternion.LookRotation(direction, Vector3.up);
            motorPort.RotateTowards(target, turnSpeed * deltaTime);
        }

        private void OnUnitChanged(UnitChangedEvent evt)
        {
            if (evt.Current.UnitId == unitId &&
                evt.Current.MapInstanceId == mapInstanceId &&
                (evt.ChangeMask & UnitChangeMask.Active) != 0 &&
                !evt.Current.IsActive)
            {
                verticalVelocity=0;
            }
        }

        private void OnUnitRemoved(UnitRemovedEvent evt)
        {
            if (evt.Snapshot.UnitId == unitId && evt.Snapshot.MapInstanceId == mapInstanceId)
            {
                ClearBinding();
            }
        }

        private void ClearBinding()
        {
            motorPort?.SetSwimmingPresentation(false);
            motorPort = null;
            unitId = 0;
            mapInstanceId = 0;
            verticalVelocity = 0f;
            swimming = false;
        }

        private void RefreshPlayerMovementConfig(int playerUnitId)
        {
            var error = string.Empty;
            if (!this.GetSystem<UnitSystem>().TryGet(playerUnitId, out var snapshot) ||
                string.IsNullOrWhiteSpace(snapshot.ConfigId) ||
                !this.GetUtility<IRuntimeConfigProvider>().TryGetPlayer(snapshot.ConfigId, out var config, out error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Log.Error($"[PlayerMoveSystem] 玩家移动配置读取失败：{error}");
                }

                return;
            }

            moveSpeed = this.GetModel<PlayerAttributeModel>().MoveSpeedOverride ?? config.MoveSpeed;
            turnSpeed = config.TurnSpeed;
        }

        private void OnPlayerMoveSpeedChanged(PlayerMoveSpeedChangedEvent evt)
        {
            moveSpeed = evt.MoveSpeed;
        }
    }
}
