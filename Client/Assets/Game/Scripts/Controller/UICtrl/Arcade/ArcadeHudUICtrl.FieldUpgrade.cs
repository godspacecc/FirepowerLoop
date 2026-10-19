using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeHudUICtrl
    {
        /// <summary>只读取正式配置的充能上限；HUD不累计击杀或扣除护甲。</summary>
        private ArcadeSlayerMartConfiguration fieldUpgradeConfig;
        private void SyncFieldUpgradeUi()
        {
            if(UI==null)return;
            bool owned,blocked,shop;int version,charge,slots,plates;float seconds;
            if(onlineMode)
            {
                var snapshot=this.GetSystem<OnlineArcadeRunSystem>()?.Snapshot??default;
                var battle=snapshot.Battle;
                long self=this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId;
                var player=battle?.Players.FirstOrDefault(p=>p.PlayerId==self)??default;
                version=battle?.SlayerMart?.RulesVersion??0;shop=battle?.SlayerMart?.Active==true;
                owned=player.HasStorm;charge=player.StormCharge;seconds=player.StormSeconds;slots=player.VestSlots;plates=player.Plates;
                blocked=battle==null||snapshot.BattlePaused||battle.RoundLoading||!player.Alive||player.RespawnSeconds>0||player.InTreasureRoom||battle.AwaitingExit;
            }
            else
            {
                version=combat.SlayerMartRulesVersion;owned=combat.HasStorm;charge=combat.StormCharge;seconds=combat.StormSeconds;slots=combat.VestSlots;plates=combat.Plates;
                shop=this.GetSystem<ArcadeCombatSystem>()?.GetSlayerMartSnapshot()!=null;
                blocked=combat.PlayerHealth<=0||combat.RespawnSeconds>0||combat.IslandTransitioning||combat.InTreasureRoom||combat.AwaitingExit||combat.CanRetryCompletion;
            }
            bool visible=version==1;
            UI.TextArmor.gameObject.SetActive(visible&&slots>0);
            bool mobile=this.GetSystem<MobileInputSystem>()?.IsMobileInputMode==true;
            UI.TextStormCharge.gameObject.SetActive(visible&&owned&&!mobile);
            UI.BtnFieldUpgrade.gameObject.SetActive(visible&&owned&&!shop);
            if(!visible)return;
            if(fieldUpgradeConfig==null)this.GetUtility<IRuntimeConfigProvider>().TryGetSlayerMart(out fieldUpgradeConfig,out _);
            int maximum=fieldUpgradeConfig==null?0:(int)fieldUpgradeConfig.Parameter("storm.kills");
            UI.TextArmor.text=Loc.T("ui.arcade.slayermart.hud_armor",plates,slots);
            UI.TextStormCharge.text=seconds>0?Loc.T("ui.arcade.slayermart.hud_storm_active",Mathf.CeilToInt(seconds)):
                Loc.T("ui.arcade.slayermart.hud_storm",charge,maximum);
            // 手机将充能放入技能按钮，百分比和进度均读取权威快照，不再占用顶部横排。
            UI.MobileStormFill.fillAmount=seconds>0?1:maximum>0?Mathf.Clamp01((float)charge/maximum):0;
            UI.MobileStormCharge.text=seconds>0?Mathf.CeilToInt(seconds).ToString():
                (maximum>0?Mathf.FloorToInt(100f*charge/maximum):0)+"%";
            string binding=HotKeyDisplayFormatter.FormatAction(this.GetModel<HotKeyModel>(),HotKeyActionIds.UseFieldUpgrade);
            if(string.IsNullOrEmpty(binding))binding=HotKeyDisplayFormatter.FormatPath(HotKeyDefaults.Create().First(b=>b.ActionId==HotKeyActionIds.UseFieldUpgrade).BindingPath);
            SetButton(UI.BtnFieldUpgrade,mobile?Loc.T("ui.arcade.slayermart.hud_upgrade"):Loc.T("ui.arcade.slayermart.hud_upgrade_pc",binding));
            var app=this.GetModel<AppStateModel>();
            blocked|=UIHelper.HasUIOpen||ArcadeShopUICtrl.IsShownForInput||app!=null&&app.CurrentState.Value!=AppState.Playing;
            UI.BtnFieldUpgrade.interactable=owned&&!shop&&!blocked&&maximum>0&&charge>=maximum&&seconds<=0;
        }
    }
}
