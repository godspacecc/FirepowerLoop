using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;
using YooAsset;

namespace Game
{
    /// <summary>负责开发版造型库入口、预制件句柄和主菜单恢复。</summary>
    public sealed class AppearanceGallerySceneFlowSystem : AbstractSystem
    {
        // 打开期间持有独立句柄，避免每次进入都通过全局加载器留下引用。
        private bool busy;
        private bool disposed;
        private AssetHandle galleryAsset;
        private AppearanceGalleryUICtrl gallery;
        private StartUICtrl hiddenMenu;
        private TestCmdButtonUICtrl hiddenTestButton;

        protected override void OnInit() { }

        /// <summary>仅从主菜单打开，下载或初始化失败时保留主菜单。</summary>
        internal async UniTask<(bool Success, string Message)> OpenAsync(CancellationToken cancellationToken)
        {
            if (!Application.isEditor && !Debug.isDebugBuild)
                return (false, "造型库仅在 Unity 编辑器和开发版开放。");
            if (busy || gallery != null || UIHelper.GetUICtrl<AppearanceGalleryUICtrl>()?.HasInstance == true)
                return (false, "造型库正在打开或已经打开。");

            var menu = UIHelper.GetUICtrl<StartUICtrl>();
            if (menu?.CanOpenTestMode != true)
                return (false, "请先返回主菜单，并等待当前开局准备结束。");

            if (!ResourceAddress.TryFromRelativePath(ResourceAddressKind.UiPrefab, AppearanceGalleryUICtrl.RelativePath,
                    out var address, out var addressError)) return (false, addressError);
            var package = YooAssets.TryGetPackage("DefaultPackage");
            if (package == null || !package.CheckLocationValid(address))
                return (false, "当前资源版本没有造型库界面，请先更新资源。");

            busy = true;
            AssetHandle candidate = null;
            try
            {
                var download = package.CreateBundleDownloader(address, true, 10, 3);
                if (download.TotalDownloadCount > 0)
                {
                    download.BeginDownload();
                    while (!download.IsDone)
                    {
                        if (disposed || cancellationToken.IsCancellationRequested)
                        {
                            download.CancelDownload();
                            return (false, "打开造型库已取消。");
                        }
                        await UniTask.Yield();
                    }
                    if (download.Status != EOperationStatus.Succeed)
                        return (false, $"造型库资源下载失败：{download.Error}");
                }

                if (disposed || cancellationToken.IsCancellationRequested || !menu.CanOpenTestMode)
                    return (false, "打开造型库已取消。");
                candidate = package.LoadAssetAsync<GameObject>(address);
                while (!candidate.IsDone) await UniTask.Yield();
                if (candidate.Status != EOperationStatus.Succeed || candidate.AssetObject is not GameObject prefab)
                    return (false, $"造型库界面加载失败：{candidate.LastError}");
                if (disposed || cancellationToken.IsCancellationRequested || !menu.CanOpenTestMode)
                    return (false, "打开造型库已取消。");

                gallery = UIHelper.GetOrCreateUI<AppearanceGalleryUICtrl>();
                gallery.Open(prefab, Close);
                if (!gallery.IsShow()) return (false, "造型库界面未能显示。");
                hiddenMenu = menu;
                hiddenMenu.Hide();
                var button = UIHelper.GetUICtrl<TestCmdButtonUICtrl>();
                if (button?.IsShow() == true)
                {
                    hiddenTestButton = button;
                    hiddenTestButton.Hide();
                }
                galleryAsset = candidate;
                candidate = null;
                return (true, "已打开造型库。");
            }
            catch (Exception error)
            {
                return (false, $"打开造型库失败：{error.Message}");
            }
            finally
            {
                try
                {
                    if (galleryAsset == null)
                    {
                        Close();
                        if (candidate != null && candidate.IsValid) candidate.Release();
                    }
                }
                finally { busy = false; }
            }
        }

        /// <summary>先停止相机与 UI，再归还资源引用并恢复菜单。</summary>
        private void Close()
        {
            gallery?.Close();
            gallery = null;
            if (galleryAsset != null && galleryAsset.IsValid) galleryAsset.Release();
            galleryAsset = null;
            if (!disposed)
            {
                hiddenMenu?.Show();
                hiddenTestButton?.Show();
            }
            hiddenMenu = null;
            hiddenTestButton = null;
        }

        protected override void OnDeinit()
        {
            disposed = true;
            Close();
        }
    }
}
