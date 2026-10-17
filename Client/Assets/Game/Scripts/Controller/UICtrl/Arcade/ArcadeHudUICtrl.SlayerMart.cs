using QFramework;
using TMPro;

namespace Game
{
    public sealed partial class ArcadeHudUICtrl
    {
        /// <summary>选择和加载自动显示窗口；取得区域控制后由玩家手动开菜单收集现金。</summary>
        private void SyncSlayerMartUi()
        {
            if(UI==null||!IsShow())return;
            SyncFieldUpgradeUi();SyncSlayerChallengeUi();
            var online=this.GetSystem<OnlineArcadeRunSystem>();
            var quote=online?.Snapshot.HasRun==true?online.GetSlayerMartSnapshot():this.GetSystem<ArcadeCombatSystem>()?.GetSlayerMartSnapshot();
            var window=UIHelper.GetUICtrl<ArcadeShopUICtrl>();
            UI.BtnSlayerMart.gameObject.SetActive(quote?.Stage==ArcadeSlayerMartStage.Shopping&&window?.IsShow()!=true);
            UI.BtnSlayerMart.interactable=online?.HasPendingSlayerMartOperation!=true;
            UI.BtnSlayerMart.GetComponentInChildren<TextMeshProUGUI>(true).text=Loc.T("ui.arcade.slayermart.hud_open");
            if(quote==null)return;
            UI.BtnBomb.interactable=UI.BtnDash.interactable=false;
            if(quote.Stage is ArcadeSlayerMartStage.Choosing or ArcadeSlayerMartStage.Loading)
            {if(window?.IsShow()!=true)UIHelper.ShowUI<ArcadeShopUICtrl>();}
            else if(quote.Stage==ArcadeSlayerMartStage.Closing)window?.Hide();
        }
        /// <summary>HUD只请求打开窗口；真正菜单状态由该窗口向领域提交。</summary>
        private void OpenSlayerMart()
        {
            var online=this.GetSystem<OnlineArcadeRunSystem>();
            var quote=online?.Snapshot.HasRun==true?online.GetSlayerMartSnapshot():this.GetSystem<ArcadeCombatSystem>()?.GetSlayerMartSnapshot();
            if(quote?.Stage!=ArcadeSlayerMartStage.Shopping)return;
            UIHelper.ShowUI<ArcadeShopUICtrl>();SyncSlayerMartUi();
        }
    }
}
