using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game
{
    /// <summary>
    /// 呈现恢复、远征返回营地、基地返回主菜单、联机或离开街机、设置与退出意图；不裁决结算或存档。
    /// </summary>
    public sealed class PauseUICtrl : BaseUICtrl
    {
        private PauseUI UI => BaseUI as PauseUI;
        /// <summary>当前 View 的离开请求尚未完成时拒绝重复点击，隐藏菜单不会取消地图事务。</summary>
        private bool leavingMap;
        /// <summary>每次显示或隐藏递增；旧确认框不能操作重新打开的菜单或另一趟远征。</summary>
        private long confirmationGeneration;
        protected override string Path() => "Home/Pause";
        public override UILayer Layer() => UILayer.Dialog;

        protected override void OnInit()
        {
            Bind(UI.BtnContinue, Resume);
            Bind(UI.BtnOnline, OpenOnlineOrLeaveArcade);
            Bind(UI.BtnSetting, () => UIHelper.ShowUI<SettingUICtrl>());
            Bind(UI.BtnMainMenu, ReturnToMainMenu);
            Bind(UI.BtnReturnCamp, ConfirmReturnCamp);
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
            UI.BtnMainMenu.gameObject.SetActive(this.GetModel<MapSceneModel>().CurrentMapId == MapIds.Base);
            RefreshOnlineAction();
            // 显隐变化后立即由 Prefab 布局计算标题、按钮组与背景高度，避免首帧保留上一张地图的尺寸。
            LayoutRebuilder.ForceRebuildLayoutImmediate(UI.PausePanel);
        }

        /// <summary>仅在基地暂停时切换至主菜单；保留活动世界，继续游戏仍恢复原现场。</summary>
        private void ReturnToMainMenu()
        {
            if (!IsShow() || this.GetModel<AppStateModel>().CurrentState.Value != AppState.Paused ||
                this.GetModel<MapSceneModel>().CurrentMapId != MapIds.Base ||
                !this.GetSystem<MapSceneSystem>().HasRecoverableCurrentWorld()) return;
            UIHelper.ShowUI<StartUICtrl>();
            Hide();
        }

        private bool IsArcadeMap => this.GetModel<MapSceneModel>().CurrentMapId == MapIds.Arcade;

        /// <summary>每次打开及语言变化时从当前地图重绘按钮，避免街机文案残留到基地。</summary>
        private void RefreshOnlineAction()
        {
            UI.OnlineActionText.text = Loc.T(IsArcadeMap ? "ui.pause.leave_arcade" : "ui.start.online");
            UI.BtnOnline.interactable = !leavingMap;
            UI.BtnContinue.interactable = !leavingMap && this.GetSystem<MapSceneSystem>().HasRecoverableCurrentWorld();
            UI.BtnReturnCamp.gameObject.SetActive(this.GetSystem<ExpeditionFlowSystem>().CanReturnFromPause());
            UI.BtnReturnCamp.interactable = !leavingMap;
        }

        /// <summary>展示领域状态对应的损失提示；确认之前不暂停恢复、不标记失败、不修改物资。</summary>
        private void ConfirmReturnCamp()
        {
            if (leavingMap || !IsShow() || !this.GetSystem<ExpeditionFlowSystem>().CanReturnFromPause()) return;
            var expedition = this.GetModel<ExpeditionModel>();
            var mapId = expedition.CurrentMapId;
            var expeditionId = expedition.CurrentExpeditionId;
            var qualified = expedition.CanReturn;
            var owner = UI;
            var generation = confirmationGeneration;
            Notify.ShowMessage(Loc.T("ui.pause.return_camp"),
                Loc.T(qualified ? "ui.pause.return_camp_confirm" : "ui.pause.abandon_confirm"),
                new List<MessageButtonConfig>
                {
                    new MessageButtonConfig { ButtonText = Texts.CommonConfirm, Role = MessageButtonRole.Confirm,
                        OnClick = () =>
                        {
                            if (owner != null && UI == owner && IsShow() && confirmationGeneration == generation &&
                                this.GetModel<ExpeditionModel>().CurrentExpeditionId == expeditionId)
                                ReturnCampAsync(mapId, expeditionId, qualified).Forget();
                        } },
                    new MessageButtonConfig { ButtonText = Texts.CommonCancel, Role = MessageButtonRole.Cancel }
                });
        }

        /// <summary>单次提交已确认的返回意图；失败保留领域实际状态，并仅恢复仍属于当趟的暂停菜单。</summary>
        private async UniTask ReturnCampAsync(string mapId, string expeditionId, bool qualified)
        {
            if (leavingMap) return;
            var owner = UI;
            leavingMap = true;
            try
            {
                UI.BtnReturnCamp.interactable = false;
                UI.BtnContinue.interactable = false;
                var result = await this.GetSystem<ExpeditionFlowSystem>().ReturnFromPauseAsync(mapId, expeditionId, qualified);
                if (!result.Success)
                {
                    Log.Error($"[PauseUICtrl] 返回营地失败：{result.Error}");
                    Notify.ShowFloatTips(Loc.T("ui.pause.return_camp_failed"));
                }
            }
            catch (Exception exception)
            {
                Log.Error($"[PauseUICtrl] 返回营地异常：{exception}");
                Notify.ShowFloatTips(Loc.T("ui.pause.return_camp_failed"));
            }
            finally
            {
                leavingMap = false;
                // 地图事务可能已经隐藏或卸载旧 View，不把晚到结果写入新界面。
                if (owner != null && UI == owner &&
                    this.GetModel<ExpeditionModel>().CurrentExpeditionId == expeditionId &&
                    this.GetSystem<ExpeditionFlowSystem>().CanReturnFromPause())
                {
                    if (!IsShow()) Show();
                    RefreshOnlineAction();
                }
            }
        }

        /// <summary>根据领域地图身份提交意图，不以按钮显示文字决定行为。</summary>
        private void OpenOnlineOrLeaveArcade()
        {
            if (leavingMap) return;
            if (IsArcadeMap) LeaveArcadeAsync().Forget();
            else UIHelper.ShowUI<OnlineLoginUICtrl>();
        }

        /// <summary>离开只返回基地并恢复已有安全节点，不结算或删除存档；失败可在原暂停菜单重试。</summary>
        private async UniTask LeaveArcadeAsync()
        {
            if (leavingMap || !IsArcadeMap) return;
            var owner = UI;
            leavingMap = true;
            RefreshOnlineAction();
            try
            {
                var result = await this.GetSystem<ArcadeSceneFlowSystem>().ReturnToBaseAsync();
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

        /// <summary>仅恢复完整活动世界；恢复失败保留暂停状态与菜单，不隐式读档或进入基地。</summary>
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
