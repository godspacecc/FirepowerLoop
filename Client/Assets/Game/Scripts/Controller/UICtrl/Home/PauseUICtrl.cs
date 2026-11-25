using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game
{
    /// <summary>
    /// 呈现街机继续、返回主菜单、设置与退出意图；不裁决结算或存档。
    /// </summary>
    public sealed class PauseUICtrl : BaseUICtrl
    {
        private PauseUI UI => BaseUI as PauseUI;
        /// <summary>当前 View 的离开请求尚未完成时拒绝重复点击，隐藏菜单不会取消地图事务。</summary>
        private bool leavingMap;
        protected override string Path() => "Home/Pause";
        public override UILayer Layer() => UILayer.Dialog;

        protected override void OnInit()
        {
            Bind(UI.BtnContinue, Resume);
            Bind(UI.BtnOnline, ReturnToMenu);
            Bind(UI.BtnSetting, () => UIHelper.ShowUI<SettingUICtrl>());

            Bind(UI.BtnExit, () => this.SendCommand<QuitGameCommand>());
            this.RegisterEvent<AppStateChangedEvent>(_ =>
            {
                if (this.GetModel<AppStateModel>().CurrentState.Value != AppState.Paused) Hide();
            }).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapLoadingStartedEvent>(_ => Hide()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapCurrentReleasedEvent>(_ => Hide()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ =>
            {
                if (IsShow()) RefreshMenuActions();
            }).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        /// <summary>每个 View 只绑定一次点击反馈；重复打开不会累计回调。</summary>
        private void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            button.onClick.AddListener(() =>
            {
                var audio = this.GetSystem<AudioSystem>();
                // 只降低暂停菜单的这次提示声，保留用户音量设置和其他界面音量。
                if (audio != null) audio.SetSoundGain(audio.PlayUiSound("FoundationClick"), .25f);
            });
            button.onClick.AddListener(action);
        }

        protected override void OnShow()
        {
            if (this.GetModel<AppStateModel>().CurrentState.Value != AppState.Paused)
            {
                Hide();
                return;
            }
            UI.BtnContinue.interactable = HasRecoverableWorld;

            RefreshMenuActions();
            // 显隐变化后立即由 Prefab 布局计算标题、按钮组与背景高度，避免首帧保留上一张地图的尺寸。
            LayoutRebuilder.ForceRebuildLayoutImmediate(UI.PausePanel);
        }

        private bool HasRecoverableWorld => this.GetSystem<MapSceneSystem>().HasRecoverableCurrentWorld();

        /// <summary>暂停菜单只呈现街机继续与离开意图；离开事务完成前拒绝重复请求。</summary>
        private void RefreshMenuActions()
        {
            UI.OnlineActionText.text = Loc.T("ui.pause.main_menu");
            UI.BtnOnline.interactable = !leavingMap;
            UI.BtnContinue.interactable = !leavingMap && HasRecoverableWorld;
        }

        /// <summary>活动的幸存者先完成退场，再由场景流程显示主菜单。</summary>
        private void ReturnToMenu()
        {
            if (leavingMap) return;
            if (this.GetModel<SurvivorModel>().Snapshot.Active)
            {
                LeaveSurvivorAsync().Forget();
                return;
            }

            Hide();
            UIHelper.ShowUI<StartUICtrl>();
        }
        /// <summary>幸存者退出走独立地图事务，绝不回滚或保存闯关安全节点。</summary>
        private async UniTask LeaveSurvivorAsync()
        {
            if (leavingMap) return;
            var owner = UI;
            leavingMap = true;
            RefreshMenuActions();
            try
            {
                var result = await this.GetSystem<SurvivorSceneFlowSystem>().ReturnToMenuAsync();
                if (!result.Success)
                {
                    if (owner != null && owner == UI && this.GetModel<SurvivorModel>().Snapshot.Active) Show();
                    Notify.ShowFloatTips(result.Error);
                }
            }
            finally
            {
                leavingMap = false;
                if (owner != null && owner == UI && IsShow()) RefreshMenuActions();
            }
        }

        /// <summary>仅恢复完整活动世界；恢复失败保留暂停状态与菜单，不隐式读档。</summary>
        private void Resume()
        {
            if (HasRecoverableWorld &&
                this.GetSystem<AppStateSystem>().TryResume()) Hide();
        }

        protected override void OnHide()
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(UI.transform))
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
