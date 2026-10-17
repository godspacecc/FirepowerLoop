using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
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
        /// <summary>每次显示或隐藏递增；旧确认框不能操作重新打开的菜单或另一局街机。</summary>
        private long confirmationGeneration;
        protected override string Path() => "Home/Pause";
        public override UILayer Layer() => UILayer.Dialog;

        protected override void OnInit()
        {
            Bind(UI.BtnContinue, Resume);
            Bind(UI.BtnOnline, OpenOnlineOrLeaveArcade);
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
                if (IsShow()) RefreshOnlineAction();
            }).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        /// <summary>每个 View 只绑定一次点击反馈；重复打开不会累计回调。</summary>
        private void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            button.onClick.AddListener(() => this.GetSystem<AudioSystem>()?.PlayUiSound("FoundationClick"));
            button.onClick.AddListener(action);
        }

        protected override void OnShow()
        {
            confirmationGeneration++;
            if (this.GetModel<AppStateModel>().CurrentState.Value != AppState.Paused)
            {
                Hide();
                return;
            }
            UI.BtnContinue.interactable = this.GetSystem<MapSceneSystem>().HasRecoverableCurrentWorld();

            RefreshOnlineAction();
            // 显隐变化后立即由 Prefab 布局计算标题、按钮组与背景高度，避免首帧保留上一张地图的尺寸。
            LayoutRebuilder.ForceRebuildLayoutImmediate(UI.PausePanel);
        }

        private bool IsArcadeMap => this.GetModel<MapSceneModel>().CurrentMapId == MapIds.Arcade;

        /// <summary>暂停菜单只呈现街机继续与离开意图；离开事务完成前拒绝重复请求。</summary>
        private void RefreshOnlineAction()
        {
            UI.OnlineActionText.text = Loc.T("ui.pause.main_menu");
            UI.BtnOnline.interactable = !leavingMap;
            UI.BtnContinue.interactable = !leavingMap && this.GetSystem<MapSceneSystem>().HasRecoverableCurrentWorld();
            UI.BtnReturnCamp.gameObject.SetActive(false);
            UI.BtnMainMenu.gameObject.SetActive(false);
        }

        /// <summary>根据领域地图身份提交意图，不以按钮显示文字决定行为。</summary>
        private void OpenOnlineOrLeaveArcade()
        {
            if (leavingMap) return;
            if (!IsArcadeMap) { UIHelper.ShowUI<OnlineLoginUICtrl>(); return; }
            var run = this.GetModel<ArcadeRunModel>()?.Current;
            if (run?.IslandRoundCheckpoint == null && run?.RoundCheckpoint == null) { LeaveArcadeAsync().Forget(); return; }
            long generation = ++confirmationGeneration;
            var checkpoint = run.RoundCheckpoint?.PlayerState ?? run.IslandRoundCheckpoint;
            var nameKey = run.RoundCheckpoint != null ? "ui.arcade.theme." + run.RoundCheckpoint.ArenaConfigNumber
                : ArcadeIslandLayout.GetNameKey(checkpoint.RoundNumber);
            Notify.ShowMessage(Loc.T("ui.arcade.island.leave.title"),
                Loc.T("ui.arcade.island.resume", checkpoint.RoundNumber, Loc.T(nameKey)),
                new List<MessageButtonConfig>
                {
                    new() { ButtonText = Loc.T("ui.common.confirm"), Role = MessageButtonRole.Confirm,
                        OnClick = () => {
                            if (generation == confirmationGeneration && IsShow() && IsArcadeMap &&
                                this.GetModel<ArcadeRunModel>().Current?.RunId == run.RunId) LeaveArcadeAsync().Forget();
                        } },
                    new() { ButtonText = Loc.T("ui.common.cancel"), Role = MessageButtonRole.Cancel }
                });
        }

        /// <summary>离开只返回主菜单并恢复已有安全节点，不结算或删除存档；失败可在原暂停菜单重试。</summary>
        private async UniTask LeaveArcadeAsync()
        {
            if (leavingMap || !IsArcadeMap) return;
            var owner = UI;
            leavingMap = true;
            RefreshOnlineAction();
            try
            {
                var result = await this.GetSystem<ArcadeSceneFlowSystem>().ReturnToMainMenuAsync();
                if (!result.Success)
                {
                    if (UI == owner && owner != null && IsArcadeMap &&
                        this.GetModel<AppStateModel>().CurrentState.Value == AppState.Paused) Show();
                    Notify.ShowFloatTips(result.Error);
                    return;
                }
                // 地图提交后才回滚瞬时战斗，加载失败仍可继续原局；此入口不修改落盘节点。
                var run = this.GetModel<ArcadeRunModel>();
                if (run != null && (run.Phase == ArcadeRunPhase.Arena || run.Phase == ArcadeRunPhase.Challenge) &&
                    !this.GetSystem<ArcadeRunSystem>().TryReturnToSafeNode(out _, out var error))
                    Notify.ShowFloatTips(error);
            }
            finally
            {
                leavingMap = false;
                // 异步完成时只刷新原 View，防止旧请求写入被卸载后重建的界面。
                if (owner != null && UI == owner && IsShow()) RefreshOnlineAction();
            }
        }

        /// <summary>仅恢复完整活动世界；恢复失败保留暂停状态与菜单，不隐式读档。</summary>
        private void Resume()
        {
            if (this.GetSystem<MapSceneSystem>().HasRecoverableCurrentWorld() &&
                this.GetSystem<AppStateSystem>().TryResume()) Hide();
        }

        protected override void OnHide()
        {
            confirmationGeneration++;
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(UI.transform))
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
