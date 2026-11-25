using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace Game
{
    /// <summary>负责开发版造型库的附加场景加载、退出和主菜单恢复。</summary>
    public sealed class AppearanceGallerySceneFlowSystem : AbstractSystem
    {
        private const string SceneAddress = "Scenes/Maps/AppearanceGallery";
        // 串行化下载、加载与卸载。仅记录由本次打开操作隐藏的控件。
        private bool busy;
        private bool disposed;
        private SceneHandle sceneHandle;
        private StartUICtrl hiddenMenu;
        private TestCmdButtonUICtrl hiddenTestButton;

        protected override void OnInit() { }

        /// <summary>只在可操作的主菜单打开，资源或场景失败时继续保留菜单。</summary>
        internal async UniTask<(bool Success, string Message)> OpenAsync(CancellationToken cancellationToken)
        {
            if (!Application.isEditor && !Debug.isDebugBuild)
                return (false, "造型库仅在 Unity 编辑器和开发版开放。");
            if (busy || sceneHandle != null)
                return (false, "造型库正在打开或已经打开。");

            var menu = UIHelper.GetUICtrl<StartUICtrl>();
            if (menu?.CanOpenTestMode != true)
                return (false, "请先返回主菜单，并等待当前开局准备结束。");

            var package = YooAssets.TryGetPackage("DefaultPackage");
            if (package == null || !package.CheckLocationValid(SceneAddress))
                return (false, "当前资源版本没有造型库场景，请先更新资源。");

            busy = true;
            SceneHandle candidate = null;
            try
            {
                var download = package.CreateBundleDownloader(SceneAddress, true, 10, 3);
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
                candidate = package.LoadSceneAsync(SceneAddress, LoadSceneMode.Additive);
                // 场景加载完成后再做取消清理，避免遗留未接管的附加场景。
                while (!candidate.IsDone) await UniTask.Yield();
                if (candidate.Status != EOperationStatus.Succeed)
                    return (false, $"造型库场景加载失败：{candidate.LastError}");
                if (disposed || cancellationToken.IsCancellationRequested || !menu.CanOpenTestMode)
                    return (false, "打开造型库已取消。");

                AppearanceGallerySceneView view = null;
                foreach (var root in candidate.SceneObject.GetRootGameObjects())
                {
                    view = root.GetComponentInChildren<AppearanceGallerySceneView>(true);
                    if (view != null) break;
                }
                if (view == null) return (false, "造型库场景缺少预览控制器。");

                view.Initialize(Game.Interface, this.GetUtility<IRuntimeConfigProvider>());
                view.BindExit(() => CloseAsync().Forget());
                hiddenMenu = menu;
                hiddenMenu.Hide();
                var button = UIHelper.GetUICtrl<TestCmdButtonUICtrl>();
                if (button?.IsShow() == true)
                {
                    hiddenTestButton = button;
                    hiddenTestButton.Hide();
                }
                sceneHandle = candidate;
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
                    if (candidate != null && candidate.IsValid)
                    {
                        var scene = candidate.SceneObject;
                        if (!scene.IsValid() || !scene.isLoaded)
                        {
                            // 加载失败没有可卸载的场景，直接释放资源引用。
                            candidate.Release();
                        }
                        else
                        {
                            var unload = candidate.UnloadAsync();
                            while (!unload.IsDone) await UniTask.Yield();
                            if (unload.Status != EOperationStatus.Succeed)
                                Debug.LogError($"[AppearanceGallery] 清理候选场景失败：{unload.Error}");
                        }
                    }
                    if (sceneHandle == null) RestoreMenu();
                }
                finally { busy = false; }
            }
        }

        /// <summary>退出成功后还原先前可见的主菜单和测试按钮，卸载失败时允许重试。</summary>
        private async UniTask CloseAsync()
        {
            if (busy || sceneHandle == null) return;
            busy = true;
            try
            {
                var unload = sceneHandle.UnloadAsync();
                while (!unload.IsDone) await UniTask.Yield();
                if (unload.Status != EOperationStatus.Succeed)
                {
                    Notify.ShowFloatTips($"退出造型库失败：{unload.Error}");
                    return;
                }
                sceneHandle = null;
                RestoreMenu();
            }
            finally { busy = false; }
        }

        private void RestoreMenu()
        {
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
            if (!busy && sceneHandle != null && sceneHandle.IsValid) sceneHandle.UnloadAsync();
            sceneHandle = null;
            RestoreMenu();
        }
    }
}
