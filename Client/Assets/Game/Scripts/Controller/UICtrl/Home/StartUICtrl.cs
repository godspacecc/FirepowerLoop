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
        private static bool silentCheckStarted;
        /// <summary>覆盖资源检查结束后的 APK 探测阶段，避免重复点击和按钮过早恢复。</summary>
        private bool manualChecking;
        /// <summary>界面隐藏或卸载时递增；旧请求不得向已离开的界面弹出更新提示。</summary>
        private int updatePresentationGeneration;
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
            BindMenuButton(UI.BtnNewGame, OnNewGameClick);
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
            var appState = this.GetModel<AppStateModel>().CurrentState.Value;
            if (appState != AppState.Paused)
            {
                Log.Error("[StartUICtrl] Start UI 只能在暂停状态显示。");
                Hide();
                return;
            }

            RefreshSaveButtons();
            RefreshUpdateText();
            var snapshot = this.GetSystem<ResourceUpdateSystem>().Snapshot;
            if (!silentCheckStarted &&
                snapshot.ResourceSource != BootResourceSource.BuiltinFallback)
            {
                silentCheckStarted = true;
                CheckUpdate(false).Forget();
            }
        }

        protected override void OnHide()
        {
            manualChecking = false;
            updatePresentationGeneration++;
        }

        protected override void OnUnload()
        {
            manualChecking = false;
            updatePresentationGeneration++;
        }

        private void RefreshSaveButtons()
        {
            var pending = this.GetSystem<ArcadeRunSystem>().InspectPending();
            UI.BtnNewGame.gameObject.SetActive(pending.Status == SaveFileStatus.Missing);
            UI.BtnContinue.gameObject.SetActive(pending.Status != SaveFileStatus.Missing);
            UI.BtnContinue.interactable = true;
        }

        private void RefreshUpdateText()
        {
            if (UI == null)
            {
                return;
            }

            var snapshot = this.GetSystem<ResourceUpdateSystem>().Snapshot;
            var showUpdateStatus = manualChecking || snapshot.Status == ResourceUpdateStatus.Checking ||
                                   snapshot.Status == ResourceUpdateStatus.UpdateAvailable ||
                                   snapshot.Status == ResourceUpdateStatus.CheckFailed;
            UI.UpdateStatusText.gameObject.SetActive(showUpdateStatus);
            UI.UpdateStatusText.text = manualChecking ? Loc.T("ui.start.update.game_checking") :
                showUpdateStatus ? FormatUpdateStatus(snapshot) : string.Empty;

            var showVersion = !string.IsNullOrWhiteSpace(snapshot.CurrentVersion);
            UI.VersionText.gameObject.SetActive(showVersion);
            UI.VersionText.text = showVersion ? $"v{snapshot.CurrentVersion}" : string.Empty;

            UI.BtnCheckUpdate.interactable = !manualChecking && snapshot.Status != ResourceUpdateStatus.Checking;
        }

        private void OnNewGameClick()
        {
            if (!ArcadeResumeUICtrl.TryOpenEntry(out var error)) Notify.ShowFloatTips(error);
        }

        private void OnContinueClick()
        {
            if (!ArcadeResumeUICtrl.TryOpenEntry(out var error)) Notify.ShowFloatTips(error);
        }

        /// <summary>Start 按钮和启动流程共用的恢复闸门；只有完整活动世界可以解除暂停。</summary>
        internal static bool TryResumeCurrentWorld(MapSceneSystem mapSceneSystem, AppStateSystem appStateSystem)
        {
            return mapSceneSystem != null && appStateSystem != null &&
                   mapSceneSystem.HasRecoverableCurrentWorld() && appStateSystem.TryResume();
        }

        /// <summary>供启动恢复流程复用的受控基地进入动作，不接触副本文件。</summary>
        public async UniTask<bool> EnterBaseFromStartupAsync(BaseSaveData data)
        {
            return await EnterBase(data, false);
        }

        /// <summary>损坏副本的“进入基地”共用入口；只接受已验证的基地 DTO，绝不改写副本域。</summary>
        internal static UniTask<GameSessionFlowResult> EnterBaseRuntimeAsync(BaseSaveData data)
        {
            return Game.Interface.GetSystem<GameSessionFlowSystem>().EnterBaseAsync(data);
        }

        private async UniTask<bool> EnterBase(BaseSaveData data, bool showPlayerHud = true)
        {
            UIHelper.ShowUI<LoadingUICtrl>();
            var result = await EnterBaseRuntimeAsync(data);
            if (!result.Success)
            {
                Log.Error($"[StartUICtrl] 进入基地失败：{result.Error}");
                UIHelper.GetUICtrl<LoadingUICtrl>()?.ShowError(Texts.SaveErrorLoadFailed);
                return false;
            }

            UIHelper.HideUI<LoadingUICtrl>();
            Hide();
            if (showPlayerHud)
            {
                UIHelper.ShowUI<PlayerConditionHudUICtrl>();
                UIHelper.ShowUI<GameplayGuideHudUICtrl>();
            }
            return true;
        }

        public async UniTask ResumePendingExpeditionAsync()
        {
            UIHelper.ShowUI<LoadingUICtrl>();
            var result = await this.GetSystem<ExpeditionFlowSystem>().ResumePendingExpeditionAsync();
            if (!result.Success)
            {
                Log.Error($"[StartUICtrl] 恢复远征失败：{result.Error}");
                UIHelper.GetUICtrl<LoadingUICtrl>()?.ShowError(Texts.SaveErrorLoadFailed);
                RefreshSaveButtons();
                return;
            }

            UIHelper.HideUI<LoadingUICtrl>();
            Hide();
            UIHelper.ShowUI<PlayerConditionHudUICtrl>();
            UIHelper.ShowUI<ExpeditionHudUICtrl>();
            UIHelper.ShowUI<GameplayGuideHudUICtrl>();
        }

        private void OnCheckUpdateClick()
        {
            CheckUpdate(true).Forget();
        }

        private void OnOnlineClick()
        {
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
            RefreshUpdateText();
        }

        private void OnResourceUpdateChanged(ResourceUpdateChangedEvent evt)
        {
            RefreshUpdateText();
        }

        private void OnOnlineAccountAttention(OnlineAccountAttentionEvent evt)
        {
            Notify.ShowFloatTips(OnlineAccountText.Result(evt.Code));
        }


        /// <summary>主动检查优先展示 APK 候选；隐藏后丢弃展示结果，检查期间拒绝重复点击。</summary>
        private async UniTask CheckUpdate(bool userInitiated)
        {
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
                if (requestView == UI && requestGeneration == updatePresentationGeneration)
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
