using System;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>绑定 NPC 表现与交互回调并释放实例；不决定交互规则或 Unit 权威状态。</summary>
    public sealed class NpcViewCtrl : IViewCtrl
    {
        private readonly ViewCtrlRegistry registry;
        private readonly IViewInstanceProvider instanceProvider;
        private readonly Action<int> interactionHandler;
        private NpcView view;

        public NpcViewCtrl(
            NpcView view,
            int unitId,
            long mapInstanceId,
            ViewCtrlRegistry registry,
            IViewInstanceProvider instanceProvider,
            Action<int> interactionHandler)
        {
            this.view = view != null
                ? view
                : throw new ArgumentNullException(nameof(view), "NPC 表现控制器创建失败：View 不能为空");
            UnitId = unitId > 0
                ? unitId
                : throw new ArgumentOutOfRangeException(nameof(unitId), "NPC 表现控制器创建失败：UnitId 必须大于 0");
            MapInstanceId = mapInstanceId > 0
                ? mapInstanceId
                : throw new ArgumentOutOfRangeException(nameof(mapInstanceId), "NPC 表现控制器创建失败：MapInstanceId 必须大于 0");
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry), "NPC 表现控制器创建失败：Registry 不能为空");
            this.instanceProvider = instanceProvider ?? throw new ArgumentNullException(nameof(instanceProvider), "NPC 表现控制器创建失败：实例提供器不能为空");
            this.interactionHandler = interactionHandler;
            if (!registry.TryRegister(this, out var viewCtrlId, out var error))
            {
                throw new InvalidOperationException(error);
            }

            ViewCtrlId = viewCtrlId;
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

        public void SetInteractable(bool interactable)
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.enabled = interactable;
                var collider = liveView.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = interactable;
                }
            }
        }

        public void SetPromptText(string text)
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.SetPromptText(text);
            }
        }

        /// <summary>仅对仍存活的实例应用双端提示；释放后调用不再写入表现。</summary>
        public void SetPromptText(string text, string mobileName)
        {
            if (TryGetLiveView(out var liveView)) liveView.SetPromptText(text, mobileName);
        }

        public void FacePrompt(Vector3 cameraPosition, Vector3 cameraUp)
        {
            if (TryGetLiveView(out var liveView))
            {
                liveView.FacePrompt(cameraPosition, cameraUp);
            }
        }

        /// <summary>应用控制器已确认的世界朝向；释放后的实例不再写入。</summary>
        public void SetRotation(Quaternion rotation)
        {
            if (TryGetLiveView(out var liveView)) liveView.transform.rotation = rotation;
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
                currentView.InteractionRequested -= OnInteractionRequested;
                instanceProvider.Release(currentView.gameObject);
            }
        }

        private void OnInteractionRequested(NpcView source)
        {
            interactionHandler?.Invoke(UnitId);
        }

        private bool TryGetLiveView(out NpcView liveView)
        {
            liveView = view;
            return !IsDisposed && liveView != null;
        }
    }
}
