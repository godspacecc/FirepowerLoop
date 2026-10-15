using System;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 拥有动态撤离 View 的注册、范围进入/离开与交互请求三类原始事件转发，以及对应的释放配对。
    /// Dispose 后不再向已释放 View 写入表现，也不再转发其事件。
    /// </summary>
    public sealed class ExtractionViewCtrl : IViewCtrl
    {
        private readonly ViewCtrlRegistry registry;
        private readonly IViewInstanceProvider instanceProvider;
        private readonly Action<int, Collider> rangeEnteredHandler;
        private readonly Action<int, Collider> rangeExitedHandler;
        private readonly Action<int> interactionHandler;
        private TaskExitView view;

        public ExtractionViewCtrl(
            TaskExitView view,
            int unitId,
            long mapInstanceId,
            ViewCtrlRegistry registry,
            IViewInstanceProvider instanceProvider,
            Action<int, Collider> triggerHandler)
            : this(view, unitId, mapInstanceId, registry, instanceProvider, triggerHandler, null, null)
        {
        }

        public ExtractionViewCtrl(
            TaskExitView view,
            int unitId,
            long mapInstanceId,
            ViewCtrlRegistry registry,
            IViewInstanceProvider instanceProvider,
            Action<int, Collider> rangeEnteredHandler,
            Action<int, Collider> rangeExitedHandler,
            Action<int> interactionHandler)
        {
            this.view = view != null
                ? view
                : throw new ArgumentNullException(nameof(view), "撤离点表现控制器创建失败：View 不能为空");
            UnitId = unitId > 0
                ? unitId
                : throw new ArgumentOutOfRangeException(nameof(unitId), "撤离点表现控制器创建失败：UnitId 必须大于 0");
            MapInstanceId = mapInstanceId > 0
                ? mapInstanceId
                : throw new ArgumentOutOfRangeException(nameof(mapInstanceId), "撤离点表现控制器创建失败：MapInstanceId 必须大于 0");
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry), "撤离点表现控制器创建失败：Registry 不能为空");
            this.instanceProvider = instanceProvider ?? throw new ArgumentNullException(nameof(instanceProvider), "撤离点表现控制器创建失败：实例提供器不能为空");
            this.rangeEnteredHandler = rangeEnteredHandler;
            this.rangeExitedHandler = rangeExitedHandler;
            this.interactionHandler = interactionHandler;
            if (!registry.TryRegister(this, out var viewCtrlId, out var error))
            {
                throw new InvalidOperationException(error);
            }

            ViewCtrlId = viewCtrlId;
            view.RangeEntered += OnRangeEntered;
            view.RangeExited += OnRangeExited;
            view.InteractionRequested += OnInteractionRequested;
        }

        public int ViewCtrlId { get; }
        public int UnitId { get; }
        public long MapInstanceId { get; }
        public bool IsDisposed { get; private set; }
        public bool IsReleased => IsDisposed;
        public Vector3 Position => TryGetLiveView(out var liveView) ? liveView.transform.position : Vector3.zero;
        public Quaternion Rotation => TryGetLiveView(out var liveView) ? liveView.transform.rotation : Quaternion.identity;

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

        public void SetAvailable(bool available)
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.SetAvailable(available);
            }
        }

        public void SetPlayerInside(bool inside)
        {
            if (TryGetLiveView(out var liveView)) liveView.SetPlayerInside(inside);
        }

        public void SetCountdown(bool active, string text)
        {
            if (!TryGetLiveView(out var liveView)) return;
            liveView.SetPromptText(text);
            liveView.SetCountdownActive(active);
        }

        public void SetPromptText(string text)
        {
            if (TryGetLiveView(out var liveView)) liveView.SetPromptText(text);
        }

        public void SetReturning(bool returning)
        {
            if (TryGetLiveView(out var liveView)) liveView.SetReturning(returning);
        }

        public void FacePrompt(Vector3 cameraPosition, Vector3 cameraUp)
        {
            if (TryGetLiveView(out var liveView)) liveView.FacePrompt(cameraPosition, cameraUp);
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
                currentView.RangeEntered -= OnRangeEntered;
                currentView.RangeExited -= OnRangeExited;
                currentView.InteractionRequested -= OnInteractionRequested;
                currentView.SetPlayerInside(false);
                currentView.SetCountdownActive(false);
                currentView.SetReturning(true);
                currentView.SetAvailable(false);
                instanceProvider.Release(currentView.gameObject);
            }
        }

        private void OnRangeEntered(TaskExitView exit, Collider other) => rangeEnteredHandler?.Invoke(UnitId, other);
        private void OnRangeExited(TaskExitView exit, Collider other) => rangeExitedHandler?.Invoke(UnitId, other);
        private void OnInteractionRequested(TaskExitView exit) => interactionHandler?.Invoke(UnitId);

        private bool TryGetLiveView(out TaskExitView liveView)
        {
            liveView = view;
            return !IsDisposed && liveView != null;
        }
    }
}
