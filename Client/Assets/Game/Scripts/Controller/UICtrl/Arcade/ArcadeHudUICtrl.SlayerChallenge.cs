using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeHudUICtrl
    {
        /// <summary>只阻止重复提交，阶段、票数和时间均取权威快照。</summary>
        private bool slayerIntentPending;
        /// <summary>只读取当前宿主阶段；普通战斗为零，不借倒计时猜测阶段。</summary>
        private int SlayerPhase()=>onlineMode?this.GetSystem<OnlineArcadeRunSystem>()?.Snapshot.Battle?.SlayerChallenge?.Phase??0:
            (int)(this.GetSystem<ArcadeCombatSystem>()?.GetSlayerChallengeSnapshot()?.Phase??0);
        /// <summary>对话框只能作用于打开时的本局和现场代次，旧窗口不能替新挑战投票。</summary>
        private string SlayerIntentScope()
        {
            if(onlineMode){var s=this.GetSystem<OnlineArcadeRunSystem>().Snapshot;return s.RunId+":"+s.Battle?.RoundGeneration;}
            var local=this.GetSystem<ArcadeCombatSystem>().GetSlayerChallengeSnapshot();return local==null?string.Empty:local.RunId+":"+local.Generation;
        }
        /// <summary>准备、收币、Boss、购物和保存分别显示，不把Boss剩余零秒误报为挑战失败。</summary>
        private void SyncSlayerChallengeUi()
        {
            if(UI==null||!IsShow())return;
            int phase=SlayerPhase();bool choice=phase==1||phase==2||phase==3;
            UI.BtnSlayerChallenge.gameObject.SetActive(choice);
            UI.BtnSlayerChallenge.interactable=!slayerIntentPending;
            if(phase==0)return;
            var online=this.GetSystem<OnlineArcadeRunSystem>()?.Snapshot??OnlineArcadeRunClientSnapshot.Empty();
            var remote=onlineMode?online.Battle?.SlayerChallenge:null;
            var local=onlineMode?null:this.GetSystem<ArcadeCombatSystem>()?.GetSlayerChallengeSnapshot();
            int downs=remote?.Downs??local?.TeamDowns??0;
            int limit=remote?.DownLimit??(this.GetUtility<IRuntimeConfigProvider>().TryGetSlayerChallenge(out var c,out _)?c.DownLimit:0);
            int members=onlineMode?online.Players.Count:1;
            long cash=remote?.CashCollected??local?.CashCollected??0;
            int exits=remote?.ExitVotes.Count??local?.ExitVotes.Count??0;
            float seconds=onlineMode?online.Battle.ChallengeSeconds:(float)(local?.RemainingSeconds??0);
            Set(UI.TextArena,Loc.T("ui.arcade.slayer.phase."+phase,Mathf.CeilToInt(seconds)));
            Set(UI.TextWave,phase==1?Loc.T("ui.arcade.slayer.preparing",remote?.EnterReady.Count??0,remote?.SkipReady.Count??0,members):
                Loc.T("ui.arcade.slayer.progress",downs,limit,cash,exits,members));
            SetButton(UI.BtnSlayerChallenge,Loc.T(phase==1?"ui.arcade.slayer.options":"ui.arcade.slayer.exit_options"));
            if(onlineMode)UI.BtnSlayerChallenge.interactable&=!online.BattlePaused&&!online.Battle.RoundLoading&&online.OperationState==OnlineArcadeOperationState.Idle;
            if(phase==1||phase>=4)UI.BtnBomb.interactable=UI.BtnDash.interactable=UI.BtnFieldUpgrade.interactable=false;
        }
        /// <summary>复用正式对话框提交意图，取消或窗口过期不代替玩家选择。</summary>
        private void OpenSlayerChallengeOptions()
        {
            int phase=SlayerPhase();if(slayerIntentPending||phase<1||phase>3)return;string scope=SlayerIntentScope();
            if(phase==1)
                Notify.ShowMessage(Loc.T("ui.arcade.slayer.options"),Loc.T("ui.arcade.slayer.prepare_body"),new System.Collections.Generic.List<MessageButtonConfig>{
                    new MessageButtonConfig{ButtonText=Loc.T("ui.arcade.slayer.skip"),Role=MessageButtonRole.Cancel,OnClick=()=>SubmitSlayerIntent(phase,false,scope).Forget()},
                    new MessageButtonConfig{ButtonText=Loc.T("ui.arcade.slayer.enter"),Role=MessageButtonRole.Confirm,OnClick=()=>SubmitSlayerIntent(phase,true,scope).Forget()}});
            else
                Notify.ShowMessage(Loc.T("ui.arcade.slayer.exit_options"),Loc.T("ui.arcade.slayer.exit_body"),new System.Collections.Generic.List<MessageButtonConfig>{
                    new MessageButtonConfig{ButtonText=Loc.T("ui.arcade.slayer.withdraw"),Role=MessageButtonRole.Cancel,OnClick=()=>SubmitSlayerIntent(phase,false,scope).Forget()},
                    new MessageButtonConfig{ButtonText=Loc.T("ui.arcade.slayer.vote_exit"),Role=MessageButtonRole.Confirm,OnClick=()=>SubmitSlayerIntent(phase,true,scope).Forget()}});
        }
        /// <summary>只提交有效窗口的选择；事务失败保留权威现场并允许重新操作。</summary>
        private async UniTask SubmitSlayerIntent(int phase,bool value,string scope)
        {
            if(!IsShow()||slayerIntentPending||phase!=SlayerPhase()||scope!=SlayerIntentScope())return;
            slayerIntentPending=true;
            try
            {
                if(onlineMode)
                {
                    var system=this.GetSystem<OnlineArcadeRunSystem>();
                    var result=phase==1?(value?await system.SetChallengeReadyAsync(true):await system.SetReadyAsync(true)):await system.SetSlayerExitAsync(value);
                    if(!result.Success&&IsShow())Notify.ShowFloatTips(ArcadeUiFormatter.OnlineResult(result.Code));
                }
                else
                {
                    var system=this.GetSystem<ArcadeCombatSystem>();string error;
                    bool accepted=phase==1?system.TrySelectSlayerEntry(value,out error):system.TrySetSlayerExit(value,out error);
                    if(!accepted)Notify.ShowFloatTips(error);
                }
            }
            catch(Exception error){if(IsShow())Notify.ShowFloatTips(error.Message);}
            finally{slayerIntentPending=false;if(IsShow())SyncSlayerChallengeUi();}
        }
    }
}
