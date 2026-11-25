using System;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>显示造型库并将界面和 3D 预览实例一起回收。</summary>
    public sealed class AppearanceGalleryUICtrl : BaseUICtrl
    {
        internal const string RelativePath = "Diagnostics/Gallery/AppearanceGallery";
        private GameObject preparedPrefab;
        private Action exitRequested;
        internal bool HasInstance => BaseUI != null;
        private AppearanceGalleryUI UI => BaseUI as AppearanceGalleryUI;

        public override UILayer Layer() => UILayer.Top;
        protected override string Path() => RelativePath;
        protected override GameObject LoadPrefab(string address) => preparedPrefab;

        /// <summary>预制件的资产句柄由入口事务持有，窗口只负责实例。</summary>
        internal void Open(GameObject prefab, Action onExit)
        {
            preparedPrefab = prefab;
            exitRequested = onExit;
            try { Show(); }
            finally { preparedPrefab = null; }
        }

        protected override void OnInit() { }

        protected override void OnShow()
        {
            var view = UI?.Preview;
            if (view == null) throw new InvalidOperationException("造型库界面缺少预览视图。");
            view.BeginEmbeddedPreview();
            view.Initialize(GetArchitecture(), this.GetUtility<IRuntimeConfigProvider>());
            view.BindExit(exitRequested);
        }

        protected override void OnHide()
        {
            UI?.Preview?.EndEmbeddedPreview();
        }

        /// <summary>立即停止画面和输入，随后销毁整套界面。</summary>
        internal void Close()
        {
            var ui = BaseUI;
            exitRequested = null;
            if (ui == null) return;
            Hide();
            if (Application.isPlaying) UnityEngine.Object.Destroy(ui.gameObject);
            else UnityEngine.Object.DestroyImmediate(ui.gameObject);
        }
    }
}
