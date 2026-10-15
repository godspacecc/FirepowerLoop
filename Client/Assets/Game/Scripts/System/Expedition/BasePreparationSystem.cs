using System;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>推进离线基地日历并提交休息事务；不接管远征生存时钟、商店交易或天气。</summary>
    public sealed class BasePreparationSystem : AbstractSystem
    {
        private double fractionalMilliseconds;
        private bool resting;
        protected override void OnInit()
        {
            var saves=this.GetSystem<SaveSystem>();
            foreach(var section in new IScopedSaveSectionProvider[]{this.GetModel<BasePreparationModel>(),this.GetModel<ShopUnlockModel>(),this.GetModel<ExpeditionPreparationModel>()})
                if(!saves.RegisterSection(section,out var error))throw new InvalidOperationException(error);
            LifeCycle.RegisterUpdate(Tick);
        }
        protected override void OnDeinit()=>LifeCycle.UnregisterUpdate(Tick);
        public bool IsStableBase=>this.GetModel<AppStateModel>().CurrentState.Value==AppState.Playing&&this.GetModel<SaveModel>().CurrentDomain==OfflineSaveDomain.Base&&this.GetModel<MapSceneModel>().CurrentMapId==MapIds.Base&&this.GetModel<MapSceneModel>().Status.Value==MapLoadStatus.Loaded&&this.GetModel<MapSceneModel>().CurrentMode==MapPreparationMode.OfflineFull&&this.GetModel<BasePreparationModel>().Initialized&&this.GetModel<PlayerBagModel>().RiskState==PlayerBagRiskState.BaseSafe;
        private void Tick()
        {
            if(!IsStableBase||resting||!this.GetUtility<IRuntimeConfigProvider>().TryGetPreparationRule(out var rule,out _))return;
            var model=this.GetModel<BasePreparationModel>();
            var delta=Time.deltaTime*rule.MinutesPerSecond*60000+fractionalMilliseconds;
            if(delta<=0||double.IsNaN(delta)||double.IsInfinity(delta))return;
            var whole=(long)delta;fractionalMilliseconds=delta-whole;
            if(whole==0)return;
            var old=model.TotalMilliseconds;var oldDay=model.LastGrantedDay;
            var currency=this.GetModel<CurrencyModel>();var oldGold=currency.Gold;
            try
            {
                var next=checked(old+whole);var credit=PreparationMath.DailyCredit(next,oldDay,rule.DailyGold);
                model.TotalMilliseconds=next;model.LastGrantedDay=PreparationMath.Day(next);
                if(credit>0&&!currency.TrySetGold(checked(oldGold+credit)))throw new InvalidOperationException("金币溢出");
                if(credit>0||old/60000!=next/60000)
                {
                    var save=this.GetSystem<SaveSystem>();save.MarkDirty("base calendar advanced");
                    if(credit>0&&!save.Flush(out var error))throw new InvalidOperationException(error);
                    PublishChanged();
                }
            }
            catch(Exception e){model.TotalMilliseconds=old;model.LastGrantedDay=oldDay;currency.RestoreGold(oldGold);Log.Error("基地日历提交失败："+e.Message);}
        }
        public string DateText
        {
            get
            {
                var model=this.GetModel<BasePreparationModel>();
                if(!model.Initialized)return string.Empty;
                long clock=model.TotalMilliseconds;
                var expedition=this.GetModel<ExpeditionModel>();var ledger=this.GetModel<ExpeditionPreparationModel>();
                if(expedition.IsActive&&ledger.ExpeditionId==expedition.CurrentExpeditionId)clock=ledger.Project(expedition.ElapsedGameMinutes);
                var minute=clock/60000%1440;
                return Loc.T("ui.base_prep.date",PreparationMath.Day(clock),$"{minute/60:00}:{minute%60:00}");
            }
        }
        /// <summary>只在稳定基地执行；时间、资金和身体状态同次保存，失败完整回滚。</summary>
        public bool TryRest(out string error)
        {
            error=null;if(resting||!IsStableBase){error=Loc.T("ui.base_prep.unavailable");return false;}
            if(!this.GetUtility<IRuntimeConfigProvider>().TryGetPreparationRule(out var rule,out error))return false;
            var combat=this.GetModel<CombatModel>();if(combat.PlayerMaxHP<=0){error=Loc.T("ui.base_prep.unavailable");return false;}
            var save=this.GetSystem<SaveSystem>();var before=save.CaptureRuntimeSnapshot();
            var restoreCondition=this.GetSystem<PlayerConditionSystem>().CaptureRestRuntimeRollback();
            resting=true;
            try
            {
                var model=this.GetModel<BasePreparationModel>();var currency=this.GetModel<CurrencyModel>();
                var next=PreparationMath.NextRestTime(model.TotalMilliseconds,rule.RestMinute);
                if(!currency.TrySetGold(checked(currency.Gold+PreparationMath.DailyCredit(next,model.LastGrantedDay,rule.DailyGold))))throw new InvalidOperationException("金币无效");
                model.TotalMilliseconds=next;model.LastGrantedDay=PreparationMath.Day(next);
                this.GetSystem<CombatSystem>().PrepareBaseRestHealth();
                this.GetSystem<PlayerConditionSystem>().RestoreForBaseRest();
                save.MarkDirty("base rested");if(!save.Flush(out error))throw new InvalidOperationException(error);
                fractionalMilliseconds=0;
                try{this.GetSystem<CombatSystem>().CompleteBaseRestHealth();this.SendEvent(new PlayerConditionChangedEvent());}catch(Exception e){Log.Error("休息状态监听失败："+e.Message);}
                PublishChanged();return true;
            }
            catch(Exception e){save.RestoreRuntimeSnapshot(before,out var restoreError);restoreCondition();error=e.Message+(string.IsNullOrEmpty(restoreError)?"":"；"+restoreError);PublishChanged();return false;}
            finally{resting=false;}
        }
        private void PublishChanged(){try{this.SendEvent(new BasePreparationChangedEvent());}catch(Exception e){Log.Error("基地准备监听失败："+e.Message);}}
    }
}
