using System;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 接管掉落物表现对象，并把碰撞触发转交给 LootCtrl。
    /// </summary>
    public sealed class LootViewCtrl : IViewCtrl, ILootPresentationPort
    {
        private readonly ViewCtrlRegistry registry;
        private readonly IViewInstanceProvider instanceProvider;
        private ResourcePickupView view;
        private readonly bool ownsInstance;
        private readonly PlayerInteractionSystem interactionSystem;

        public LootViewCtrl(
            ResourcePickupView view,
            int unitId,
            long mapInstanceId,
            ViewCtrlRegistry registry,
            IViewInstanceProvider instanceProvider,
            bool ownsInstance,
            Action<int, Collider> triggerHandler,
            Action<int> interactionHandler = null,
            Action<int, Collider> exitHandler = null,
            PlayerInteractionSystem interactionSystem = null)
        {
            this.view = view != null
                ? view
                : throw new ArgumentNullException(nameof(view), "掉落表现控制器创建失败：View 不能为空");
            UnitId = unitId > 0
                ? unitId
                : throw new ArgumentOutOfRangeException(nameof(unitId), "掉落表现控制器创建失败：UnitId 必须大于 0");
            MapInstanceId = mapInstanceId > 0
                ? mapInstanceId
                : throw new ArgumentOutOfRangeException(nameof(mapInstanceId), "掉落表现控制器创建失败：MapInstanceId 必须大于 0");
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry), "掉落表现控制器创建失败：Registry 不能为空");
            this.instanceProvider = instanceProvider ?? throw new ArgumentNullException(nameof(instanceProvider), "掉落表现控制器创建失败：实例提供器不能为空");
            this.ownsInstance = ownsInstance;
            this.interactionSystem = interactionSystem;
            TriggerHandler = triggerHandler;
            InteractionHandler = interactionHandler;
            ExitHandler = exitHandler;
            if (!registry.TryRegister(this, out var viewCtrlId, out var error))
            {
                throw new InvalidOperationException(error);
            }

            ViewCtrlId = viewCtrlId;
            view.Triggered += OnTriggered;
            view.RangeExited += OnRangeExited;
            view.InteractionRequested += OnInteractionRequested;
        }

        public int ViewCtrlId { get; }
        public int UnitId { get; }
        public long MapInstanceId { get; }
        public bool IsDisposed { get; private set; }
        public bool IsReleased => IsDisposed;
        public bool IsVisibleTargetActive => TryGetLiveView(out var liveView) && liveView.gameObject.activeInHierarchy;
        public Vector3 Position => TryGetLiveView(out var liveView) ? liveView.transform.position : Vector3.zero;
        public Quaternion Rotation => TryGetLiveView(out var liveView) ? liveView.transform.rotation : Quaternion.identity;
        private Action<int, Collider> TriggerHandler { get; }
        private Action<int> InteractionHandler { get; }
        private Action<int, Collider> ExitHandler { get; }
        internal ResourcePickupView InteractionTarget => view;
        public void SetInteractionAvailable(bool available)
        {
            if (TryGetLiveView(out var liveView)) liveView.SetInteractionAvailable(available);
        }

        public IArchitecture GetArchitecture()
        {
            return Game.Interface;
        }

        public void SetVisible(bool visible)
        {
            if (TryGetLiveView(out var liveView) && liveView.gameObject.activeSelf != visible)
            {
                liveView.gameObject.SetActive(visible);
            }
        }

        public void SetNameVisible(bool visible)
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.SetNameVisible(visible);
            }
        }

        public void RefreshDisplay(string itemDisplayName, int amount)
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.RefreshDisplay(itemDisplayName, amount);
            }
        }

        /// <summary>将活动相机的屏幕方向转交给掉落名称；不调整拾取物模型朝向。</summary>
        public void FaceName(Vector3 cameraForward, Vector3 cameraUp)
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.FaceName(cameraForward, cameraUp);
            }
        }

        public void SetCollected()
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.gameObject.SetActive(false);
            }
        }

        public void Release()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            var currentView = view;
            view = null;
            IsDisposed = true;
            registry.Unregister(ViewCtrlId);
            if (currentView != null)
            {
                currentView.Triggered -= OnTriggered;
                currentView.RangeExited -= OnRangeExited;
                currentView.InteractionRequested -= OnInteractionRequested;
                interactionSystem?.Blur(currentView);
                if (ownsInstance)
                {
                    instanceProvider.Release(currentView.gameObject);
                }
            }
        }

        private void OnTriggered(ResourcePickupView pickup, Collider other)
        {
            TriggerHandler?.Invoke(UnitId, other);
        }

        private void OnRangeExited(ResourcePickupView pickup, Collider other) => ExitHandler?.Invoke(UnitId, other);
        private void OnInteractionRequested() => InteractionHandler?.Invoke(UnitId);

        private bool TryGetLiveView(out ResourcePickupView liveView)
        {
            liveView = view;
            return !IsDisposed && liveView != null;
        }
    }
}
