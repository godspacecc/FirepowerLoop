using System;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>呈现联机账号快照并提交登录和街机房间操作；账号权威及连接状态由现有系统持有，不负责静态布局。</summary>
    public sealed class OnlineLoginUICtrl : BaseUICtrl
    {
        private OnlineLoginUI UI => BaseUI as OnlineLoginUI;
        /// <summary>每次操作或隐藏时递增，使旧异步回调失效。</summary>
        private int operationGeneration;
        private bool isVisible;
        private OnlineOperationState lastNoticeOperationState;
        private OnlineAccountResultCode lastNoticeResultCode;
        private static bool requestArcadeRoomMode;
        private bool arcadeRoomMode;
        /// <summary>从主菜单双人入口发起的登录，认证成功后自动交还大厅导航。</summary>
        private bool enterLobbyAfterLogin;

        /// <summary>双人入口：已认证直接进入大厅；未认证只补登录步骤，成功后自动进入大厅。</summary>
        public static void ShowForArcadeLobby()
        {
            if (Game.Interface.GetModel<OnlineAccountModel>().Snapshot.Value.IsAuthenticated)
            {
                UIHelper.HideUI<OnlineLoginUICtrl>();
                OnlineRoomUICtrl.ShowArcadeLobby();
                return;
            }
            requestArcadeRoomMode = true;
            OnlineLoginUICtrl controller = UIHelper.ShowUI<OnlineLoginUICtrl>();
            // Show 已可见时不会重进 OnShow，因此同步写入当前实例，保证入口意图不丢失。
            if (controller != null)
            {
                controller.arcadeRoomMode = true;
                controller.enterLobbyAfterLogin = true;
            }
        }

        public override UILayer Layer()
        {
            return UILayer.Dialog;
        }

        protected override string Path()
        {
            return "Online/OnlineLogin";
        }

        protected override void OnInit()
        {
            UI.ResultText.gameObject.SetActive(false);
            UI.LoginButton.onClick.AddListener(() => SubmitLogin().Forget());
            UI.LogoutButton.onClick.AddListener(() => SubmitLogout().Forget());
            if (UI.RoomButton != null)
            {
                UI.RoomButton.onClick.AddListener(OpenRoom);
            }
            UI.WorldButton.gameObject.SetActive(false);
            UI.CloseButton.onClick.AddListener(Hide);
            this.RegisterEvent<OnlineAccountChangedEvent>(evt => Render(evt.Snapshot))
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ => Render(this.GetSystem<OnlineAccountSystem>().Snapshot))
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            isVisible = true;
            arcadeRoomMode = true;
            requestArcadeRoomMode = false;
            var snapshot = this.GetSystem<OnlineAccountSystem>().Snapshot;
            lastNoticeOperationState = snapshot.OperationState;
            lastNoticeResultCode = snapshot.LastResultCode;
            if (!snapshot.IsAuthenticated)
            {
                UI.UserNameInput.text = snapshot.LastUserName;
                UI.PasswordInput.text = string.Empty;
            }
            Render(snapshot);
        }

        protected override void OnHide()
        {
            enterLobbyAfterLogin = false;
            isVisible = false;
            arcadeRoomMode = false;
            operationGeneration++;
            ClearPasswordAndFocus();
            this.GetSystem<OnlineAccountSystem>().CloseAnonymousSession();
        }

        /// <summary>登录入口只提交导航意图；房间 Controller 决定普通或街机房间的实际呈现。</summary>
        private void OpenRoom()
        {
            if (arcadeRoomMode)
            {
                OnlineRoomUICtrl.ShowArcadeLobby();
                return;
            }

            UIHelper.ShowUI<OnlineRoomUICtrl>();
        }

        /// <summary>提交账号登录；账号系统读取网络设置，本弹窗不保存或覆盖服务器地址。</summary>
        private UniTask SubmitLogin()
        {
            return RunOperation((system, user, password) => system.LoginAsync(user, password));
        }

        private async UniTask SubmitLogout()
        {
            int generation = ++operationGeneration;
            try
            {
                SetInputsEnabled(false);
                await this.GetSystem<OnlineAccountSystem>().LogoutAsync();
                if (generation == operationGeneration && UI != null)
                {
                    ClearPasswordAndFocus();
                }
            }
            catch (OperationCanceledException)
            {
                RestoreAfterCancellation(generation);
            }
            catch (Exception exception)
            {
                Log.Error($"[OnlineLoginUICtrl] 注销异常：generation={generation} exception={exception}");
                RestoreAfterUnexpectedFailure(generation);
            }
        }

        private async UniTask RunOperation(System.Func<OnlineAccountSystem, string, string, System.Threading.Tasks.Task<OnlineAccountOperationResult>> operation)
        {
            int generation = ++operationGeneration;
            try
            {
                SetInputsEnabled(false);
                string user = UI.UserNameInput.text;
                string password = UI.PasswordInput.text;
                await operation(this.GetSystem<OnlineAccountSystem>(), user, password);
                if (generation == operationGeneration && UI != null)
                {
                    ClearPasswordAndFocus();
                }
            }
            catch (OperationCanceledException)
            {
                RestoreAfterCancellation(generation);
            }
            catch (Exception exception)
            {
                Log.Error($"[OnlineLoginUICtrl] 账号操作异常：generation={generation} exception={exception}");
                RestoreAfterUnexpectedFailure(generation);
            }
        }

        private void RestoreAfterUnexpectedFailure(int generation)
        {
            if (generation != operationGeneration || UI == null)
            {
                return;
            }

            SetInputsEnabled(true);
            ClearPasswordAndFocus();
            Notify.ShowFloatTips(OnlineAccountText.Result(OnlineAccountResultCode.UnknownFailure));
        }

        private void RestoreAfterCancellation(int generation)
        {
            if (generation == operationGeneration && UI != null)
            {
                SetInputsEnabled(true);
            }
        }

        private void Render(OnlineAccountSnapshot snapshot)
        {
            if (UI == null)
            {
                return;
            }

            if (isVisible && enterLobbyAfterLogin && snapshot.IsAuthenticated)
            {
                enterLobbyAfterLogin = false;
                Hide();
                OnlineRoomUICtrl.ShowArcadeLobby();
                return;
            }

            bool anonymous = !snapshot.IsAuthenticated;
            UI.AnonymousRoot.SetActive(anonymous);
            UI.AuthenticatedRoot.SetActive(!anonymous);
            SetInputsEnabled(snapshot.OperationState == OnlineOperationState.Idle);

            if (snapshot.IsAuthenticated)
            {
                UI.AuthenticatedUserText.text = snapshot.UserName;
                UI.AuthenticatedPlayerText.text = string.Format(Loc.T("ui.online.player_id"), snapshot.PlayerId);
            }

            ShowSnapshotNotice(snapshot);
        }

        private void ShowSnapshotNotice(OnlineAccountSnapshot snapshot)
        {
            bool changed = snapshot.OperationState != lastNoticeOperationState ||
                           snapshot.LastResultCode != lastNoticeResultCode;
            lastNoticeOperationState = snapshot.OperationState;
            lastNoticeResultCode = snapshot.LastResultCode;

            if (!isVisible || !changed)
            {
                return;
            }

            string message = snapshot.OperationState == OnlineOperationState.Idle
                ? OnlineAccountText.Result(snapshot.LastResultCode)
                : OnlineAccountText.Operation(snapshot.OperationState);
            Notify.ShowFloatTips(message);
        }

        private void SetInputsEnabled(bool enabled)
        {
            if (UI == null)
            {
                return;
            }

            bool anonymousInputsEnabled = enabled;
            UI.UserNameInput.interactable = anonymousInputsEnabled;
            UI.PasswordInput.interactable = anonymousInputsEnabled;
            UI.LoginButton.interactable = anonymousInputsEnabled;
            UI.LogoutButton.interactable = enabled;
            if (UI.RoomButton != null)
            {
                UI.RoomButton.interactable = enabled && this.GetSystem<OnlineAccountSystem>().Snapshot.IsAuthenticated;
            }
        }

        private void ClearPasswordAndFocus()
        {
            if (UI?.PasswordInput != null)
            {
                UI.PasswordInput.text = string.Empty;
            }

            EventSystem.current?.SetSelectedGameObject(null);
        }
    }


    public static class OnlineAccountText
    {
        public static string Result(OnlineAccountResultCode code)
        {
            switch (code)
            {
                case OnlineAccountResultCode.AccountCreated: return Loc.T("ui.online.account_created");
                case OnlineAccountResultCode.InvalidUserName: return Loc.T("ui.online.error.username");
                case OnlineAccountResultCode.InvalidPassword: return Loc.T("ui.online.error.password");
                case OnlineAccountResultCode.InvalidNetworkConfig: return Loc.T("ui.online.error.config");
                case OnlineAccountResultCode.ConnectFailed: return Loc.T("ui.online.error.connect_failed");
                case OnlineAccountResultCode.ConnectTimeout: return Loc.T("ui.online.error.connect_timeout");
                case OnlineAccountResultCode.RequestTimeout: return Loc.T("ui.online.error.request_timeout");
                case OnlineAccountResultCode.UserNameExists: return Loc.T("ui.online.error.username_exists");
                case OnlineAccountResultCode.AccountOrPasswordWrong: return Loc.T("ui.online.error.account_or_password");
                case OnlineAccountResultCode.PasswordWrong: return Loc.T("ui.online.error.password_wrong");
                case OnlineAccountResultCode.NotLoggedIn: return Loc.T("ui.online.error.not_logged_in");
                case OnlineAccountResultCode.ServiceUnavailable: return Loc.T("ui.online.error.service_unavailable");
                case OnlineAccountResultCode.SessionConflict: return Loc.T("ui.online.error.session_conflict");
                case OnlineAccountResultCode.RegisterSuccessLoginFailed: return Loc.T("ui.online.error.register_login_failed");
                case OnlineAccountResultCode.Disconnected: return Loc.T("ui.online.error.disconnected");
                case OnlineAccountResultCode.SessionReplaced: return Loc.T("ui.online.error.session_replaced");
                case OnlineAccountResultCode.PreferenceSaveFailed: return Loc.T("ui.online.error.preference_save");
                case OnlineAccountResultCode.OperationInProgress: return Loc.T("ui.online.error.busy");
                case OnlineAccountResultCode.Success: return Loc.T("ui.online.success");
                case OnlineAccountResultCode.None: return string.Empty;
                default: return Loc.T("ui.online.error.unknown");
            }
        }

        public static string Operation(OnlineOperationState state)
        {
            switch (state)
            {
                case OnlineOperationState.LoggingIn: return Loc.T("ui.online.logging_in");
                case OnlineOperationState.LoggingOut: return Loc.T("ui.online.logging_out");
                default: return string.Empty;
            }
        }
    }
}
