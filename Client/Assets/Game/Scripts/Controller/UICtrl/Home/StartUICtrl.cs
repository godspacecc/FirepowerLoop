using System;
using System.Collections.Generic;
using Boot;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game
{
    /// <summary>
    /// 开始页控制器，绑定菜单意图、点击反馈与当前存档/更新展示；布局由 Prefab 保存，
    /// 不负责存档规则、联机状态或音频资源生命周期。
    /// </summary>
    public sealed class StartUICtrl : BaseUICtrl
    {
        /// <summary>复用正式 UI 点击资源名；播放与释放均交给 AudioSystem。</summary>
        private const string MenuClickSound = "FoundationClick";
        /// <summary>覆盖资源检查结束后的 APK 探测阶段，避免重复点击和按钮过早恢复。</summary>
        private bool manualChecking;
        /// <summary>界面隐藏或卸载时递增；旧请求不得向已离开的界面弹出更新提示。</summary>
        private int updatePresentationGeneration;
        /// <summary>单人开局资源准备的当前请求；隐藏和重开菜单后旧请求不能创建存档。</summary>
        private int startGeneration;
        /// <summary>开局准备到地图事务结束期间拒绝重复提交。</summary>
        private bool startingRun;
        /// <summary>当前开局的加载表现请求；离开菜单时释放，旧回调不得关闭新请求。</summary>
        private LoadingUICtrl startLoading;
        /// <summary>与当前加载控制器配对，只释放该次开局持有的表现请求。</summary>
        private int startLoadingRequestId;
        private StartUI UI => BaseUI as StartUI;

        public override UILayer Layer()
        {
            return UILayer.Dialog;
        }

        protected override string Path()
        {
            return "Home/Start";
        }

        protected override void OnInit()
        {
            ApplyReleaseVisibility(UI);
            BindMenuButton(UI.BtnNewGame, OnNewGameClick);
            BindMenuButton(UI.BtnSurvivor, () => UIHelper.ShowUI<SurvivorEntryUICtrl>().Configure(() => StartSurvivorAsync().Forget()));
            this.RegisterEvent<ArcadeRunChangedEvent>(_ => { if (IsShow()) RefreshSaveButtons(); })
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            BindMenuButton(UI.BtnContinue, OnContinueClick);
            if (UI.BtnOnline != null)
            {
                BindMenuButton(UI.BtnOnline, OnOnlineClick);
            }
            BindMenuButton(UI.BtnCheckUpdate, OnCheckUpdateClick);
            BindMenuButton(UI.BtnSetting, OnSettingClick);
            BindMenuButton(UI.BtnExit, OnExitClick);
            this.RegisterEvent<LanguageChangedEvent>(OnLanguageChanged)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<ResourceUpdateChangedEvent>(OnResourceUpdateChanged)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineAccountAttentionEvent>(OnOnlineAccountAttention)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            // 开始页显示前完成存档读取与恢复窗口预建；点击时复用已准备的内容。
            RefreshSaveButtons();
        }

        /// <summary>只按发布策略切换已有入口，布局继续由正式 Prefab 的布局组处理。</summary>
        internal static void ApplyReleaseVisibility(StartUI view)
        {
            bool online = !ClientReleasePolicy.OfflineSinglePlayer;
            if (view.BtnOnline != null) view.BtnOnline.gameObject.SetActive(online);
            view.BtnCheckUpdate.gameObject.SetActive(online);
            if (!online) view.UpdateStatusText.gameObject.SetActive(false);
            if (view.ButtonGroup != null) LayoutRebuilder.ForceRebuildLayoutImmediate(view.ButtonGroup);
        }

        /// <summary>每个 View 初始化时绑定一次；先请求点击声，再执行业务，隐藏/重显不重复绑定。</summary>
        private void BindMenuButton(Button button, UnityAction action)
        {
            button.onClick.AddListener(PlayMenuClick);
            button.onClick.AddListener(action);
        }

        /// <summary>只在有效点击事件触发时播放二维 UI 音效，悬停与状态刷新保持安静。</summary>
        private void PlayMenuClick()
        {
            this.GetSystem<AudioSystem>().PlayUiSound(MenuClickSound);
        }

        protected override void OnShow()
        {
            ApplyReleaseVisibility(UI);
            startGeneration++;
            startingRun = false;
            var appState = this.GetModel<AppStateModel>().CurrentState.Value;
            if (appState != AppState.Paused)
            {
                Log.Error("[StartUICtrl] Start UI 只能在暂停状态显示。");
                Hide();
                return;
            }

            RefreshSaveButtons();
            RefreshUpdateText();
            // 每次返回开始页都重新查询版本；在途请求由更新系统复用，不触发提示或安装。
            CheckUpdate(false).Forget();
        }

        protected override void OnHide()
        {
            ReleaseStartLoading();
            startGeneration++;
            startingRun = false;
            manualChecking = false;
            updatePresentationGeneration++;
        }

        protected override void OnUnload()
        {
            ReleaseStartLoading();
            startGeneration++;
            startingRun = false;
            manualChecking = false;
            updatePresentationGeneration++;
        }

        /// <summary>从正式存档快照决定单人入口并预备详情框；已有进度和结果都不能由新局按钮直接覆盖。</summary>
        private void RefreshSaveButtons()
        {
            var pending = this.GetSystem<ArcadeRunSystem>().PrepareMenuEntry();
            if (pending.IsValid)
                UIHelper.PreloadUI<ArcadeResumeUICtrl>().PrepareMenuContent(pending.Data);
            UI.BtnNewGame.gameObject.SetActive(pending.Status == SaveFileStatus.Missing);
            UI.BtnContinue.gameObject.SetActive(pending.Status != SaveFileStatus.Missing);
            UI.BtnSurvivor.interactable = !startingRun;
            UI.BtnNewGame.interactable = !startingRun;
            UI.BtnContinue.interactable = !startingRun;
            UI.BtnOnline.interactable = !startingRun;
            UI.BtnSetting.interactable = !startingRun;
            UI.BtnContinue.GetComponentInChildren<TMPro.TextMeshProUGUI>(true).text =
                Loc.T(pending.IsValid && pending.Data.HasResult ? "ui.start.run_result" : "ui.start.run_continue");
        }

        private void RefreshUpdateText()
        {
            if (UI == null)
            {
                return;
            }

            var snapshot = this.GetSystem<ResourceUpdateSystem>().Snapshot;
            var showUpdateStatus = !ClientReleasePolicy.OfflineSinglePlayer && (manualChecking || snapshot.Status == ResourceUpdateStatus.Checking ||
                                   snapshot.Status == ResourceUpdateStatus.UpdateAvailable ||
                                   snapshot.Status == ResourceUpdateStatus.CheckFailed);
            UI.UpdateStatusText.gameObject.SetActive(showUpdateStatus);
            UI.UpdateStatusText.text = manualChecking ? Loc.T("ui.start.update.game_checking") :
                showUpdateStatus ? FormatUpdateStatus(snapshot) : string.Empty;

            var showVersion = !string.IsNullOrWhiteSpace(snapshot.CurrentVersion);
            UI.VersionText.gameObject.SetActive(showVersion);
            UI.VersionText.text = showVersion ? $"v{snapshot.CurrentVersion}" : string.Empty;

            UI.BtnCheckUpdate.interactable = !manualChecking && snapshot.Status != ResourceUpdateStatus.Checking;
        }

        /// <summary>幸存者入口先准备资源；失败恢复菜单，不读取或覆盖闯关存档。</summary>
        private async UniTask StartSurvivorAsync()
        {
            if (startingRun) return;
            startingRun = true;
            RefreshSaveButtons();
            var owner = BaseUI;
            int generation = ++startGeneration;
            bool IsCurrent() => generation == startGeneration && owner != null && ReferenceEquals(owner, BaseUI) && IsShow();
            try
            {
                startLoading = UIHelper.ShowUI<LoadingUICtrl>();
                startLoadingRequestId = startLoading.BeginSurvivorEntryPreparation();
                var preparation = this.GetUtility<IGameplayResourcePreparation>();
                if (preparation != null && !await preparation.PrepareAsync(MapIds.Arcade, IsCurrent)) return;
                await UniTask.NextFrame();
                if (!IsCurrent()) return;
                var result = await this.GetSystem<SurvivorSceneFlowSystem>().StartAsync();
                if (!result.Success) Notify.ShowFloatTips(result.Error);
            }
            catch (Exception exception)
            {
                Log.Error($"[StartUICtrl] 幸存者开局失败：{exception}");
                if (IsCurrent()) Notify.ShowFloatTips(Loc.T("ui.survivor.error.open"));
            }
            finally
            {
                if (IsCurrent())
                {
                    ReleaseStartLoading();
                    startingRun = false;
                    RefreshSaveButtons();
                }
            }
        }
        private void OnNewGameClick()
        {
            StartSinglePlayerAsync().Forget();
        }

        /// <summary>无待处理存档时直接开局；先准备资源再落盘，失败保留菜单与已写入的安全节点。</summary>
        private async UniTask StartSinglePlayerAsync()
        {
            if (startingRun) return;
            var system = this.GetSystem<ArcadeRunSystem>();
            if (system.InspectPending().Status != SaveFileStatus.Missing)
            {
                OnContinueClick();
                return;
            }
            startingRun = true;
            RefreshSaveButtons();
            var owner = BaseUI;
            int generation = ++startGeneration;
            bool IsCurrent() => generation == startGeneration && owner != null && ReferenceEquals(owner, BaseUI) && IsShow();
            try
            {
                startLoading = UIHelper.ShowUI<LoadingUICtrl>();
                startLoadingRequestId = startLoading.BeginEntryPreparation(MapIds.Arcade);
                var preparation = this.GetUtility<IGameplayResourcePreparation>();
                if (preparation != null && !await preparation.PrepareAsync(MapIds.Arcade, IsCurrent)) return;
                // 先让加载界面完成一帧绘制，再执行可能同步耗时的落盘和场景准备。
                await UniTask.NextFrame();
                if (!IsCurrent()) return;
                if (!system.TryStartConfiguredRun(out _, out var error))
                {
                    Notify.ShowFloatTips(error);
                    return;
                }
                await ArcadeResumeUICtrl.EnterNewRunAsync(system);
            }
            catch (Exception exception)
            {
                Log.Error($"[StartUICtrl] 单人开局失败：{exception}");
                if (IsCurrent()) Notify.ShowFloatTips(Loc.T("ui.arcade.error.open"));
            }
            finally
            {
                if (IsCurrent())
                {
                    ReleaseStartLoading();
                    startingRun = false;
                    RefreshSaveButtons();
                }
            }
        }

        /// <summary>取消等待或入口结束时恢复加载显示；失败地图仍保留错误和返回按钮。</summary>
        private void ReleaseStartLoading()
        {
            startLoading?.EndEntryPreparation(startLoadingRequestId);
            startLoading = null;
            startLoadingRequestId = 0;
        }

        private void OnContinueClick()
        {
            if (startingRun) return;
            if (!ArcadeResumeUICtrl.TryOpenEntry(out var error)) Notify.ShowFloatTips(error);
        }

        /// <summary>Start 按钮和启动流程共用的恢复闸门；只有完整活动世界可以解除暂停。</summary>
        internal static bool TryResumeCurrentWorld(MapSceneSystem mapSceneSystem, AppStateSystem appStateSystem)
        {
            return mapSceneSystem != null && appStateSystem != null &&
                   mapSceneSystem.HasRecoverableCurrentWorld() && appStateSystem.TryResume();
        }

        private void OnCheckUpdateClick()
        {
            CheckUpdate(true).Forget();
        }

        private void OnOnlineClick()
        {
            if (ClientReleasePolicy.OfflineSinglePlayer) return;
            if (startingRun) return;
            OnlineLoginUICtrl.ShowForArcadeLobby();
        }

        private void OnSettingClick()
        {
            UIHelper.ShowUI<SettingUICtrl>();
        }

        private void OnExitClick()
        {
            this.SendCommand<QuitGameCommand>();
        }

        private void OnLanguageChanged(LanguageChangedEvent evt)
        {
            RefreshSaveButtons();
            RefreshUpdateText();
        }

        private void OnResourceUpdateChanged(ResourceUpdateChangedEvent evt)
        {
            if (evt.Snapshot.Status == ResourceUpdateStatus.NotChecked)
            {
                // 设置切服会重置检查上下文；旧服晚到的公告或安装包结果不得在首页弹出。
                updatePresentationGeneration++;
                manualChecking = false;
            }
            RefreshUpdateText();
        }

        private void OnOnlineAccountAttention(OnlineAccountAttentionEvent evt)
        {
            Notify.ShowFloatTips(OnlineAccountText.Result(evt.Code));
        }


        /// <summary>主动检查优先展示 APK 候选；隐藏后丢弃展示结果，检查期间拒绝重复点击。</summary>
        private async UniTask CheckUpdate(bool userInitiated)
        {
            if (ClientReleasePolicy.OfflineSinglePlayer) return;
            if (userInitiated && manualChecking) return;
            var requestView = UI;
            int requestGeneration = updatePresentationGeneration;
            if (userInitiated) { manualChecking = true; RefreshUpdateText(); }
            try
            {
                var result = await this.GetSystem<ResourceUpdateSystem>().CheckWithNoticeAsync(userInitiated);
                if (requestView != UI || !IsShow() || requestGeneration != updatePresentationGeneration) return;
                var snapshot = result.Snapshot;
                RefreshUpdateText();
                if (!userInitiated)
                {
                    return;
                }

                // 安装包版本独立于资源版本；资源已最新时仍必须优先呈现 APK 候选。
                if (result.ApplicationUpdate != null)
                {
                    ShowApplicationUpdate(result.ApplicationUpdate);
                    return;
                }

                if (result.WindowsUpdate != null)
                {
                    var manifest = result.WindowsUpdate;
                    UIHelper.ShowUI<ResourceUpdateMessageUICtrl>().SetContent(
                        Loc.T("ui.start.update.apk_available"),
                        Loc.T("ui.start.update.apk_content", WindowsUpdateManifest.ReadInstalled()?.versionName ?? "?",
                            manifest.versionName, (manifest.sizeBytes / 1048576d).ToString("F1",
                            System.Globalization.CultureInfo.CurrentCulture)), RestartForUpdate);
                    return;
                }

                switch (snapshot.Status)
                {
                    case ResourceUpdateStatus.NoUpdate:
                        ShowNoUpdate(snapshot, result.Notice);
                        break;
                    case ResourceUpdateStatus.UpdateAvailable:
                        ShowUpdateAvailable(snapshot, result.Notice);
                        break;
                    case ResourceUpdateStatus.CheckFailed:
                        Log.Error($"[StartUICtrl] 资源更新检查失败：{snapshot.Error}");
                        if (snapshot.ResourceSource == BootResourceSource.BuiltinFallback)
                        {
                            Notify.ShowFloatTips(Texts.StartUpdateServerUnavailable);
                        }
                        else
                        {
                            Notify.ShowFloatTips(Texts.StartUpdateCheckFailed);
                        }
                        break;
                }
            }
            finally
            {
                if (userInitiated && requestView == UI && requestGeneration == updatePresentationGeneration)
                {
                    manualChecking = false;
                    RefreshUpdateText();
                }
            }
        }

        /// <summary>只呈现安装包候选并提交重启意图，复用 Boot 的下载、校验与系统安装流程。</summary>
        private void ShowApplicationUpdate(ApkUpdateManifest manifest)
        {
            UIHelper.ShowUI<ResourceUpdateMessageUICtrl>().SetContent(
                Loc.T("ui.start.update.apk_available"),
                Loc.T("ui.start.update.apk_content", UnityEngine.Application.version, manifest.versionName,
                    (manifest.sizeBytes / 1048576d).ToString("F1", System.Globalization.CultureInfo.CurrentCulture)),
                RestartForUpdate);
        }

        private void ShowNoUpdate(
            ResourceUpdateSnapshot snapshot,
            ResourceUpdateNoticeResult notice)
        {
            if (snapshot.ResourceSource == BootResourceSource.BuiltinFallback)
            {
                Notify.ShowFloatTips(Texts.StartUpdateBuiltinNoRemoteUpdate);
                return;
            }

            if (!notice.HasNotice)
            {
                Notify.ShowFloatTips(Texts.StartUpdateNoUpdate);
                return;
            }

            Notify.ShowMessage(
                Texts.StartUpdateCurrentNoticeTitle,
                ResourceUpdateNoticeDocument.FormatForNotify(notice.Content, true),
                new List<MessageButtonConfig>
                {
                    new MessageButtonConfig { ButtonText = Texts.CommonClose }
                });
        }

        private void ShowUpdateAvailable(
            ResourceUpdateSnapshot snapshot,
            ResourceUpdateNoticeResult notice)
        {
            string standardTitle = snapshot.RequiresRestartToUpdate
                ? Texts.StartUpdateRestartRequired
                : Texts.StartUpdateAvailable;
            string title = notice.HasNotice && !string.IsNullOrWhiteSpace(notice.Content.Title)
                ? notice.Content.Title
                : standardTitle;
            // 零下载只表示文件齐全，仍须说明当前运行版本尚未切换；公告不能遮蔽这项提示。
            bool cachedUpdate = snapshot.HasKnownDownloadSize && snapshot.TotalDownloadCount == 0;
            string standardContent = cachedUpdate
                ? Texts.StartUpdateCachedContent(snapshot.CurrentVersion, snapshot.TargetVersion)
                : snapshot.RequiresRestartToUpdate
                ? Texts.StartUpdateRestartRequiredContent(snapshot.TargetVersion)
                : Texts.StartUpdateAvailableContent(snapshot.TargetVersion);
            string content = notice.HasNotice
                ? standardContent + "\n\n" + ResourceUpdateNoticeDocument.FormatForNotify(notice.Content, false)
                : standardContent;

            UIHelper.ShowUI<ResourceUpdateMessageUICtrl>().SetContent(title, content, RestartForUpdate);
        }

        private void RestartForUpdate()
        {
            if (!this.GetUtility<IApplicationRestartUtility>().TryRestart(out var error))
            {
                Log.Error($"[StartUICtrl] 重启应用失败：{error}");
                UIHelper.GetUICtrl<ResourceUpdateMessageUICtrl>()?.ShowRestartFailure();
            }
        }

        private static string FormatUpdateStatus(ResourceUpdateSnapshot snapshot)
        {
            if (snapshot.ResourceSource == BootResourceSource.BuiltinFallback)
            {
                switch (snapshot.Status)
                {
                    case ResourceUpdateStatus.Checking:
                        return Texts.StartUpdateServerChecking;
                    case ResourceUpdateStatus.UpdateAvailable:
                        return Texts.StartUpdateRestartRequiredContent(snapshot.TargetVersion);
                    case ResourceUpdateStatus.CheckFailed:
                        return Texts.StartUpdateServerUnavailable;
                    default:
                        return string.Empty;
                }
            }

            switch (snapshot.Status)
            {
                case ResourceUpdateStatus.Checking:
                    return Texts.StartUpdateChecking;
                case ResourceUpdateStatus.UpdateAvailable:
                    return Texts.StartUpdateAvailableSummary(snapshot.TargetVersion);
                case ResourceUpdateStatus.CheckFailed:
                    return Texts.StartUpdateCheckFailed;
                default:
                    return string.Empty;
            }
        }

    }
}
