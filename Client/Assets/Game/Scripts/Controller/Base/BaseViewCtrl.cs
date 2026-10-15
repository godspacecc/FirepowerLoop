using System;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 管理一个 Unity 表现对象的通用生命周期，并保证重复释放无副作用。
    /// </summary>
    public abstract class BaseViewCtrl<TView> : IViewCtrl where TView : Component
    {
        private readonly ViewCtrlRegistry registry;
        private readonly IViewInstanceProvider instanceProvider;

        protected BaseViewCtrl(TView view, ViewCtrlRegistry registry, IViewInstanceProvider instanceProvider)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view), "ViewCtrl 创建失败：View 不能为空");
            }

            View = view;
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry), "ViewCtrl 创建失败：Registry 不能为空");
            this.instanceProvider = instanceProvider ?? throw new ArgumentNullException(nameof(instanceProvider), "ViewCtrl 创建失败：实例提供器不能为空");

            if (!registry.TryRegister(this, out var viewCtrlId, out var error))
            {
                throw new InvalidOperationException(error);
            }

            ViewCtrlId = viewCtrlId;
        }

        public int ViewCtrlId { get; }
        public bool IsDisposed { get; private set; }
        public bool IsReleased => IsDisposed;
        protected TView View { get; private set; }
        public GameObject GameObject => View != null ? View.gameObject : null;
        public Transform Transform => View != null ? View.transform : null;

        public IArchitecture GetArchitecture()
        {
            return Game.Interface;
        }

        public virtual void SetVisible(bool visible)
        {
            if (View != null && View.gameObject.activeSelf != visible)
            {
                View.gameObject.SetActive(visible);
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

            IsDisposed = true;
            OnDispose();
            registry.Unregister(ViewCtrlId);
            if (View != null)
            {
                instanceProvider.Release(View.gameObject);
            }

            View = null;
        }

        protected virtual void OnDispose()
        {
        }
    }
}
