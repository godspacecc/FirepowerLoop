using System.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeHudUICtrl
    {
        /// <summary>只显示权威剩余秒数与救援倍率，禁止本地倒计时或虚构复起。</summary>
        private static string CoopDownedText(OnlineArcadeBattlePlayerSnapshot player)
            =>Loc.T(player.RescueRate>1?"ui.arcade.coop.helped":"ui.arcade.coop.downed",Mathf.CeilToInt(player.DownedSeconds),player.RescueRate);

        private void SyncCoopRescueUi()
        {
            bool visible=onlineMode&&this.GetSystem<OnlineArcadeRunSystem>()?.Snapshot.Battle?.Players.Any(p=>p.DownedSeconds>0)==true;
            UI.BtnDonateLife.gameObject.SetActive(visible);
            if(!visible)return;
            bool mobile=this.GetSystem<MobileInputSystem>()?.IsMobileInputMode==true;
            string binding=HotKeyDisplayFormatter.FormatAction(this.GetModel<HotKeyModel>(),HotKeyActionIds.DonateLife);
            SetButton(UI.BtnDonateLife,mobile?Loc.T("ui.arcade.coop.donate"):Loc.T("ui.arcade.coop.donate_pc",binding));
            UI.BtnDonateLife.interactable=this.GetSystem<OnlineArcadeRunSystem>().TryGetDonationTarget(out _);
        }
    }
}
