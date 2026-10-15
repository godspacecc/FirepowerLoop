using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 表示当前会话的手机输入模式发生变化；该状态只存在于本次运行，不进入设置或存档。
    /// </summary>
    public readonly struct MobileInputModeChangedEvent
    {
        public MobileInputModeChangedEvent(bool enabled)
        {
            Enabled = enabled;
        }

        public bool Enabled { get; }
    }

    /// <summary>
    /// 统一负责手机输入模式、HUD 门禁及攻击与加速意图；不裁决伤害、速度、体力、弹药或相机状态。
    /// </summary>
    public sealed class MobileInputSystem : AbstractSystem
    {
        private bool editorSimulationEnabled;
        /// <summary>只存到统一输入下一帧；多个 UI 回调合并为一次攻击，状态切换时丢弃。</summary>
        private bool pendingShoulderAttack;
        /// <summary>通用俯视地图的屏幕方向仅在触点有效时存在；最终世界朝向由统一输入持有。</summary>
        private Vector2 baseAim;
        /// <summary>仅当前有效按压周期；门禁关闭或装填请求后丢弃，不自动恢复。</summary>
        private bool baseFireHeld;
        /// <summary>两帧之间完成的短按边沿，下一帧消费一次。</summary>
        private bool pendingBaseAttack;
        /// <summary>装填按下边沿，统一输入消费后由既有武器事务校验。</summary>
        private bool pendingBaseReload;
        private bool focused = true;
        private bool applicationPaused;
        /// <summary>本次游玩期间的加速切换意图；体力耗尽或输入门禁关闭即释放，不保存到设备设置。</summary>
        private bool sprintRequested;

        /// <summary>统一移动与按钮读取同一意图；只释放过期输入，速度及体力仍由 PlayerConditionSystem 裁决。</summary>
        public bool SprintRequested
        {
            get
            {
                if (!CanAcceptSprint() || this.GetModel<PlayerConditionModel>().SprintReleaseRequired)
                    sprintRequested = false;
                return sprintRequested;
            }
        }

        /// <summary>一次点击切换加速；不满足玩法门禁或体力已耗尽时拒绝开启。</summary>
        public bool TryToggleSprint()
        {
            if (SprintRequested) { sprintRequested = false; return true; }
            if (!CanAcceptSprint() || !this.GetSystem<PlayerConditionSystem>().CanStartSprint) return false;
            sprintRequested = true;
            return true;
        }

        private bool CanAcceptSprint() => focused && !applicationPaused && CanAcceptMovement() &&
            !this.GetSystem<AppearanceGallerySceneFlowSystem>().IsOpen && HasLivingConnectedPlayer();

        public bool IsMobileInputMode =>
            Application.platform == RuntimePlatform.Android ||
            Application.isEditor && editorSimulationEnabled;

        protected override void OnInit()
        {
            this.RegisterEvent<AppStateChangedEvent>(_ => ClearShoulderInput());
            this.RegisterEvent<MapLoadingStartedEvent>(_ => ClearShoulderInput());
            this.RegisterEvent<MapCurrentReleasedEvent>(_ => ClearShoulderInput());
            UIHelper.BlockingUIChanged += OnBlockingUiChanged;
            LifeCycle.RegisterApplicationFocus(OnFocus);
            LifeCycle.RegisterApplicationPause(OnPause);
        }

        protected override void OnDeinit()
        {
            UIHelper.BlockingUIChanged -= OnBlockingUiChanged;
            LifeCycle.UnregisterApplicationFocus(OnFocus);
            LifeCycle.UnregisterApplicationPause(OnPause);
            ClearShoulderInput();
            editorSimulationEnabled = false;
        }

        /// <summary>手机越肩与第一人称控件共用玩法门禁；死亡、断线、搜索及失焦时不提交转向或攻击。</summary>
        public bool CanAcceptShoulderControls() => IsMobileInputMode && ShouldShowReticle();

        /// <summary>键鼠和手机共用中心准星显隐；仅有效第一人称或越肩游玩显示，不开启手机输入。</summary>
        public bool ShouldShowReticle()
        {
            if (!focused || applicationPaused || !CanAcceptGameplayMovement() || !CanAcceptBuildingCombatControls() ||
                this.GetModel<PlayerCameraModeModel>().Mode == PlayerCameraMode.TopDown ||
                this.GetSystem<AppearanceGallerySceneFlowSystem>().IsOpen ||
                !this.GetSystem<PlayerCameraSystem>().TryGetMovementYawDegrees(out _)) return false;
            return HasLivingConnectedPlayer();
        }

        /// <summary>基地与普通远征共用固定俯视三指操作；加载、UI、失焦、死亡和断线期间拒绝意图。</summary>
        public bool CanAcceptBaseFacingControls()
        {
            return focused && !applicationPaused && CanAcceptMovement() && CanAcceptBuildingCombatControls() &&
                   SupportsTopDownFacingForMap(this.GetModel<MapSceneModel>().CurrentMapId) &&
                   this.GetModel<PlayerCameraModeModel>().Mode == PlayerCameraMode.TopDown &&
                   !this.GetSystem<AppearanceGallerySceneFlowSystem>().IsOpen &&
                   this.GetSystem<PlayerCameraSystem>().TryGetMovementYawDegrees(out _) &&
                   HasLivingConnectedPlayer();
        }

        /// <summary>控件显示与统一输入共用地图范围；和家村、街机及未登记地图不接受通用俯视操作。</summary>
        internal static bool SupportsTopDownFacingForMap(string mapId)
        {
            if (string.IsNullOrWhiteSpace(mapId) || mapId == MapIds.HeJiaVillage ||
                !MapCatalog.TryGet(mapId, out var definition)) return false;
            return definition.Kind == MapKind.Base || definition.Kind == MapKind.Survival ||
                   definition.Kind == MapKind.Task;
        }

        /// <summary>建筑列表和预览保留移动摇杆，但不显示或消费战斗触控，避免选择位置时误攻击。</summary>
        private bool CanAcceptBuildingCombatControls() =>
            this.GetSystem<TemporaryBuildingSystem>().Mode == TemporaryBuildingMode.Normal &&
            UIHelper.GetUICtrl<TemporaryBuildingSelectUICtrl>()?.IsShow() != true;

        /// <summary>本地读取生命模型，联机读取服务器快照；断线或找不到本机角色时拒绝输入。</summary>
        private bool HasLivingConnectedPlayer()
        {
            if (this.GetModel<MapSceneModel>().CurrentMode != MapPreparationMode.OnlinePlayersOnly)
                return this.GetModel<CombatModel>().PlayerHP.Value > 0;
            if (this.GetSystem<OnlineWorldInventorySystem>().IsSearching) return false;
            var world = this.GetModel<OnlineWorldModel>().Snapshot.Value;
            if (world.ConnectionLost) return false;
            foreach (var player in world.Players)
                if (player.UnitId == world.SelfUnitId) return !player.IsDead && player.CurrentHP > 0;
            return false;
        }

        /// <summary>按钮每次点击提交一次攻击；失败不排队，冷却和弹药继续由武器系统判定。</summary>
        public bool RequestShoulderAttack()
        {
            if (!CanAcceptShoulderControls()) { ClearShoulderInput(); return false; }
            pendingShoulderAttack = true;
            return true;
        }

        /// <summary>统一输入每帧消费并清空，门禁变更后旧点击不得在恢复时重放。</summary>
        internal bool ConsumeShoulderAttack()
        {
            var requested = pendingShoulderAttack;
            pendingShoulderAttack = false;
            return requested && CanAcceptShoulderControls();
        }

        /// <summary>方向摇杆只提交屏幕方向，归零不改变人物最后朝向。</summary>
        public void SetBaseAim(Vector2 value)
        {
            baseAim = CanAcceptBaseFacingControls() &&
                      !float.IsNaN(value.x) && !float.IsNaN(value.y) &&
                      !float.IsInfinity(value.x) && !float.IsInfinity(value.y)
                ? Vector2.ClampMagnitude(value, 1f) : Vector2.zero;
        }

        /// <summary>独立开火按压；短按边沿保留到下一输入帧，持续按压由武器冷却裁决。</summary>
        public void SetBaseFire(bool held)
        {
            baseFireHeld = held && CanAcceptBaseFacingControls();
            if (baseFireHeld) pendingBaseAttack = true;
        }

        /// <summary>普通俯视、越肩与第一人称共用装填门禁；武器及库存条件由现有换弹事务裁决。</summary>
        public bool CanAcceptReloadControls() => CanAcceptBaseFacingControls() || CanAcceptShoulderControls();

        /// <summary>共用装填按钮只排队一次意图，并丢弃旧攻击及开火按压；失败不排队。</summary>
        public bool RequestBaseReload()
        {
            if (!CanAcceptReloadControls()) return false;
            pendingShoulderAttack = false;
            baseFireHeld = pendingBaseAttack = false;
            pendingBaseReload = true;
            return true;
        }

        internal Vector2 BaseAim => CanAcceptBaseFacingControls() ? baseAim : Vector2.zero;
        internal bool ConsumeBaseAttack()
        {
            var requested = pendingBaseAttack || baseFireHeld;
            pendingBaseAttack = false;
            // 已开始装填时旧按压不得取消动作，须松手重新按下。
            if (!CanAcceptBaseFacingControls() ||
                this.GetSystem<PlayerActionSystem>().TryGetSnapshot(out var action) && action.Kind == PlayerActionKind.Reload)
            { baseFireHeld = false; return false; }
            return requested;
        }
        internal bool ConsumeBaseReload()
        {
            var requested = pendingBaseReload;
            pendingBaseReload = false;
            return requested && CanAcceptReloadControls();
        }

        internal void ClearShoulderInput()
        {
            sprintRequested = false;
            ClearCombatInput();
        }

        /// <summary>视角没有对应战斗控件时只释放战斗输入，普通移动及加速切换继续有效。</summary>
        internal void ClearCombatInput()
        {
            pendingShoulderAttack = false;
            baseAim = Vector2.zero;
            baseFireHeld = pendingBaseAttack = pendingBaseReload = false;
        }
        private void OnBlockingUiChanged(bool blocked) { if (blocked) ClearShoulderInput(); }
        private void OnFocus(bool value) { focused = value; if (!value) ClearShoulderInput(); }
        private void OnPause(bool value) { applicationPaused = value; if (value) ClearShoulderInput(); }

        /// <summary>决定固定摇杆是否属于当前地图的可游玩 HUD；街机使用自己的双摇杆，不显示普通移动摇杆。</summary>
        public bool ShouldShowJoystick() => IsMobileInputMode && HasGameplayHudContext();

        /// <summary>普通游玩 HUD 的地图和应用门禁；与设备输入模式无关，街机继续使用独立 HUD。</summary>
        private bool HasGameplayHudContext()
        {
            var map = this.GetModel<MapSceneModel>();
            return this.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing &&
                   this.GetModel<ExpeditionModel>().Status != ExpeditionStatus.Defeated &&
                   map.CurrentMode != MapPreparationMode.ArcadePlayerOnly &&
                   !string.IsNullOrWhiteSpace(map.CurrentMapId);
        }

        /// <summary>复用玩法输入互斥规则；返回 false 时 UI 隐藏并主动归零摇杆，但保留虚拟设备。</summary>
        public bool CanAcceptMovement() => IsMobileInputMode && CanAcceptGameplayMovement();

        /// <summary>复用普通移动的玩法门禁供准星判定；不依赖手机模拟开关，不改变输入意图。</summary>
        private bool CanAcceptGameplayMovement()
        {
            if (!HasGameplayHudContext() || UIHelper.HasUIOpen || this.GetSystem<SearchContainerSystem>().IsSearching)
            {
                return false;
            }

            var mode = this.GetSystem<TemporaryBuildingSystem>().Mode;
            return mode != TemporaryBuildingMode.BuildProgress &&
                   mode != TemporaryBuildingMode.OperatingManualAttack;
        }

        /// <summary>
        /// Editor 测试指令只切换当前 Play 会话；Android 正式模式不能被该入口覆盖。
        /// </summary>
        public bool TrySetEditorSimulation(bool enabled)
        {
            if (!Application.isEditor)
            {
                return false;
            }

            if (editorSimulationEnabled == enabled)
            {
                return true;
            }

            editorSimulationEnabled = enabled;
            ClearShoulderInput();
            this.SendEvent(new MobileInputModeChangedEvent(IsMobileInputMode));
            return true;
        }
    }
}
