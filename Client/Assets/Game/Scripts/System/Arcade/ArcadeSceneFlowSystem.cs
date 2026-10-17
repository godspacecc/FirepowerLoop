using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    public readonly struct ArcadeSceneFlowResult
    {
        private ArcadeSceneFlowResult(bool success, string error)
        {
            Success = success;
            Error = error;
        }

        public bool Success { get; }
        public string Error { get; }

        public static ArcadeSceneFlowResult Ok() => new ArcadeSceneFlowResult(true, null);
        public static ArcadeSceneFlowResult Fail(string error) => new ArcadeSceneFlowResult(false, error);
    }

    /// <summary>
    /// 串行切换主界面与街机地图；只负责场景边界，不创建、结算或修改街机运行存档。
    /// </summary>
    public sealed class ArcadeSceneFlowSystem : AbstractSystem
    {
        private bool busy;

        protected override void OnInit()
        {
        }

        /// <summary>进入复用正式玩家和相机的街机地图；加载失败时保持原地图及街机存档不变。</summary>
        public async UniTask<ArcadeSceneFlowResult> EnterArenaMapAsync(CancellationToken cancellationToken = default)
        {
            var map = this.GetModel<MapSceneModel>();
            if (map.CurrentMapId == MapIds.Arcade && map.CurrentMode == MapPreparationMode.ArcadePlayerOnly &&
                map.CurrentHandle != null && map.CurrentHandle.IsValid)
            {
                return ArcadeSceneFlowResult.Ok();
            }

            if (busy) return ArcadeSceneFlowResult.Fail("街机场景正在切换");
            busy = true;
            try
            {
                // 先初始化订阅；前置检查拒绝时没有地图事件，不能留下无进度的遮罩。
                bool loadingWasVisible = UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow() == true;
                var loading = UIHelper.ShowUI<LoadingUICtrl>();
                if (!loadingWasVisible) loading.Hide();
                var result = await this.GetSystem<MapSceneSystem>().LoadArcadeMapAsync(cancellationToken);
                if (result.Success) UIHelper.HideUI<StartUICtrl>();
                return result.Success ? ArcadeSceneFlowResult.Ok() : ArcadeSceneFlowResult.Fail(result.Error);
            }
            finally
            {
                busy = false;
            }
        }

        /// <summary>释放街机地图并回到主界面；本局文件由街机事务独立管理。</summary>
        public async UniTask<ArcadeSceneFlowResult> ReturnToMainMenuAsync(CancellationToken cancellationToken = default)
        {
            if (busy) return ArcadeSceneFlowResult.Fail("街机场景正在切换");
            busy = true;
            try
            {
                var result = await this.GetSystem<MapSceneSystem>().UnloadCurrentMapAsync(cancellationToken);
                if (!result.Success)
                {
                    Notify.ShowMessage(Texts.SaveErrorLoadFailed, Loc.T("ui.arcade.menu.failed"),
                        new System.Collections.Generic.List<MessageButtonConfig> {
                            new MessageButtonConfig { ButtonText = Loc.T("ui.arcade.retry"),
                                Role = MessageButtonRole.Confirm, OnClick = () => ReturnToMainMenuAsync().Forget() } });
                    return ArcadeSceneFlowResult.Fail(result.Error);
                }
                this.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
                // 无活动句柄时卸载不会发送地图释放事件，返回成功仍须关闭旧暂停界面。
                UIHelper.HideUI<PauseUICtrl>();
                UIHelper.HideUI<ArcadeHudUICtrl>();
                UIHelper.HideUI<ArcadeResumeUICtrl>();
                UIHelper.ShowUI<StartUICtrl>();
                return ArcadeSceneFlowResult.Ok();
            }
            finally { busy = false; }
        }
    }
}
