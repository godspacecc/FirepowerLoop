using QFramework;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 维护范围候选与当前交互焦点，第一、第三人称按准星选取；只路由请求，不裁决拾取、搜索或 NPC 的业务结果。
    /// </summary>
    public sealed class PlayerInteractionSystem : AbstractSystem
    {
        private IPlayerInteractable current;
        /// <summary>已有范围回调提供的候选，按进入顺序保存；不扩大业务交互距离。</summary>
        private readonly List<IPlayerInteractable> candidates = new List<IPlayerInteractable>();
        /// <summary>架构释放标记，拒绝已经排队的帧末刷新。</summary>
        private bool released;
        /// <summary>第一、第三人称共用准星交互；缺少相机模式时按俯视角处理，保留原范围行为。</summary>
        public bool UsesCrosshairTargeting =>
            (this.GetModel<PlayerCameraModeModel>()?.Mode ?? PlayerCameraMode.TopDown) != PlayerCameraMode.TopDown;
        /// <summary>供屏幕 HUD 消费的当前名字；准星模式无可操作目标时为空。</summary>
        public string CrosshairTargetName => UsesCrosshairTargeting && IsActionable(current) ?
            current is EnemyView ? Loc.T("ui.interaction.corpse") : current.DisplayName : string.Empty;
        public string CrosshairActionKey => current is ResourcePickupView ? "ui.interaction.pickup" :
            current is IPlayerSearchable searchable && searchable.CanSearch ? "ui.interaction.search" : "ui.interaction.use";
        /// <summary>仅供表现 Controller 刷新已聚焦对象；交互权威仍由本 System 和目标规则持有。</summary>
        internal IPlayerInteractable FocusedTarget => current;

        /// <summary>
        /// 当前焦点是否为仍可交互的 NPC；仅向 HUD 暴露布尔快照，不泄漏运行时 View。
        /// </summary>
        public bool CanInteractWithFocusedNpc => current is NpcView && current.CanInteract;
        /// <summary>手机通用交互入口的可用快照，包含 NPC 和环境物件；不向 UI 暴露 View 身份。</summary>
        public bool CanInteractWithFocusedTarget => current?.CanInteract == true;

        protected override void OnInit()
        {
            released = false;
            this.RegisterEvent<PlayerInteractRequestedEvent>(_ => RequestInteract());
            this.RegisterEvent<MapLoadingStartedEvent>(_ => ClearTargets());
            this.RegisterEvent<MapCurrentReleasedEvent>(_ => ClearTargets());
            LifeCycle.RegisterLateUpdate(QueueTargetRefresh);
        }

        protected override void OnDeinit()
        {
            released = true;
            LifeCycle.UnregisterLateUpdate(QueueTargetRefresh);
            ClearTargets();
        }

        private void QueueTargetRefresh() => LifeCycle.ScheduleFrameEnd(this, RefreshLookTarget);

        /// <summary>相机更新后重选准星目标；暂停、阻断、无相机或移开视线均清空操作焦点。</summary>
        private void RefreshLookTarget()
        {
            if (released) return;
            candidates.RemoveAll(target => target == null || target is Component component && component == null);
            if (current is Component currentComponent && currentComponent == null) SetCurrent(null);
            if (!UsesCrosshairTargeting)
            {
                if (current == null && candidates.Count > 0) SetCurrent(candidates[candidates.Count - 1]);
                current?.SetFocused(true);
                return;
            }
            var app = this.GetModel<AppStateModel>();
            var camera = this.GetSystem<PlayerCameraSystem>();
            if (app == null || app.CurrentState.Value != AppState.Playing || UIHelper.HasUIOpen ||
                camera == null || !camera.TryCreateAimRay(Vector2.zero, out var ray))
            { SetCurrent(null); return; }
            SetCurrent(FirstPersonInteractionTargeting.Resolve(ray, candidates));
            current?.SetFocused(true);
        }

        private static bool IsActionable(IPlayerInteractable target) => target != null &&
            (!(target is Component component) || component != null) &&
            (target.CanInteract || target is IPlayerSearchable searchable && searchable.CanSearch);

        private void ClearTargets() { SetCurrent(null); candidates.Clear(); }

        private void SetCurrent(IPlayerInteractable target)
        {
            if (ReferenceEquals(current, target)) return;
            if (!(current is Component component) || component != null) current?.SetFocused(false);
            current = target;
            current?.SetFocused(true);
        }

        public void Focus(IPlayerInteractable target)
        {
            if (target == null) return;
            if (!candidates.Contains(target)) candidates.Add(target);
            if (!UsesCrosshairTargeting) SetCurrent(target);
        }

        public void Blur(IPlayerInteractable target)
        {
            candidates.Remove(target);
            if (!ReferenceEquals(current, target))
            {
                return;
            }

            SetCurrent(null);
        }

        public void RequestInteract()
        {
            // 按键时重新查询，避免同帧转头后仍操作上一帧 HUD 指向的对象。
            if (UsesCrosshairTargeting) RefreshLookTarget();
            if (current is IPlayerSearchable searchable && searchable.CanSearch)
            {
                searchable.Search();
                return;
            }

            if (current?.CanInteract == true)
            {
                current.Interact();
            }
        }
    }
}
