using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace Game
{
    /// <summary>
    /// 管理基地测试造型库的附加场景及其唯一 SceneHandle。只负责预览生命周期，
    /// 不切换正式地图、不写角色装备、存档或联机状态。
    /// </summary>
    public sealed class AppearanceGallerySceneFlowSystem : AbstractSystem
    {
        private const string SceneAddress = "Scenes/Maps/AppearanceGallery";
        private SceneHandle sceneHandle;
        private bool busy;
        private float previousTimeScale = 1f;
        // 只记录进入前确实显示的基地 HUD，卸载造型库后按原状态恢复。
        private readonly List<BaseUICtrl> hiddenBaseHud = new List<BaseUICtrl>();

        /// <summary>测试场景显示期间阻止正式玩法热键与玩家输入。</summary>
        public bool IsOpen => sceneHandle != null;

        protected override void OnInit() { }

        /// <summary>仅在离线基地响应测试指令；失败时释放候选场景并保留原基地。</summary>
        public async UniTask<(bool Success, string Message)> OpenAsync(CancellationToken cancellationToken = default)
        {
            if (!Application.isEditor && !Debug.isDebugBuild)
                return (false, "造型库仅在编辑器或开发版开放。");
            var map = this.GetModel<MapSceneModel>();
            if (map.CurrentMapId != MapIds.Base || map.CurrentMode != MapPreparationMode.OfflineFull ||
                map.Status.Value != MapLoadStatus.Loaded)
                return (false, "只能在离线基地打开造型库。");
            if (busy || sceneHandle != null)
                return (false, "造型库正在打开或已经打开。");

            var package = YooAssets.TryGetPackage("DefaultPackage");
            if (package == null) return (false, "造型库加载失败：资源包未初始化。");

            busy = true;
            SceneHandle candidate = null;
            try
            {
                candidate = package.LoadSceneAsync(SceneAddress, LoadSceneMode.Additive);
                while (!candidate.IsDone)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);
                    await UniTask.Yield();
                }
                if (candidate.Status != EOperationStatus.Succeed)
                    return (false, $"造型库场景加载失败：{candidate.LastError}");

                AppearanceGallerySceneView view = null;
                foreach (var root in candidate.SceneObject.GetRootGameObjects())
                {
                    view = root.GetComponentInChildren<AppearanceGallerySceneView>(true);
                    if (view != null) break;
                }
                if (view == null) return (false, "造型库场景缺少预览控制器。");

                view.Initialize(this, this.GetUtility<IRuntimeConfigProvider>());
                HideBaseHud();
                previousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                sceneHandle = candidate;
                candidate = null;
                return (true, "已打开造型库。");
            }
            catch (OperationCanceledException)
            {
                return (false, "打开造型库已取消。");
            }
            catch (Exception exception)
            {
                return (false, $"打开造型库失败：{exception.Message}");
            }
            finally
            {
                if (candidate != null && candidate.IsValid)
                    await candidate.UnloadAsync();
                if (candidate != null) RestoreBaseHud();
                busy = false;
            }
        }

        /// <summary>从预览场景返回基地；仅成功卸载后恢复进入前的时间缩放。</summary>
        public async UniTask<bool> CloseAsync()
        {
            if (busy || sceneHandle == null) return false;
            busy = true;
            try
            {
                var operation = sceneHandle.UnloadAsync();
                while (!operation.IsDone) await UniTask.Yield();
                if (operation.Status != EOperationStatus.Succeed)
                {
                    Debug.LogError($"[AppearanceGallery] 卸载失败：{operation.Error}");
                    return false;
                }
                sceneHandle = null;
                Time.timeScale = previousTimeScale;
                RestoreBaseHud();
                return true;
            }
            finally { busy = false; }
        }

        protected override void OnDeinit()
        {
            if (sceneHandle == null) return;
            var handle = sceneHandle;
            sceneHandle = null;
            Time.timeScale = previousTimeScale;
            if (handle.IsValid) handle.UnloadAsync();
            hiddenBaseHud.Clear();
        }

        private void HideBaseHud()
        {
            hiddenBaseHud.Clear();
            // 手机 HUD 同时承载背包入口和摇杆；整体隐藏会释放输入，退出时仅恢复原本可见的实例。
            HideIfVisible(UIHelper.GetUICtrl<MobileJoystickUICtrl>());
            HideIfVisible(UIHelper.GetUICtrl<PlayerConditionHudUICtrl>());
            HideIfVisible(UIHelper.GetUICtrl<GameplayGuideHudUICtrl>());
            HideIfVisible(UIHelper.GetUICtrl<TestCmdButtonUICtrl>());
            HideIfVisible(UIHelper.GetUICtrl<ExpeditionHudUICtrl>());
        }

        private void HideIfVisible(BaseUICtrl controller)
        {
            if (controller == null || !controller.IsShow()) return;
            hiddenBaseHud.Add(controller);
            controller.Hide();
        }

        /// <summary>仅回到同一离线基地时恢复先前可见的 HUD，避免地图已切换时误显示。</summary>
        private void RestoreBaseHud()
        {
            var map = this.GetModel<MapSceneModel>();
            var canRestore = map.CurrentMapId == MapIds.Base &&
                             map.CurrentMode == MapPreparationMode.OfflineFull &&
                             map.Status.Value == MapLoadStatus.Loaded;
            foreach (var controller in hiddenBaseHud)
                if (canRestore) controller.Show();
            hiddenBaseHud.Clear();
        }
    }
}
