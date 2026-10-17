using System;
using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class OnlineArcadePresentationSystem
    {
        /// <summary>表现区域绑定服务器地图代次；进入和重试只重建表现，不动商店账本。</summary>
        private bool presentedSlayerMart;
        private ArcadeSlayerMartRegionLayout activeSlayerMartLayout;
        private void ApplySlayerMartPresentation(OnlineArcadeBattleSnapshot battle)
        {
            var shop=battle.SlayerMart;
            if(!shop.Prepared)
            {lastAppliedBattleRevision=battle.BattleRevision;return;}
            if(!presentedSlayerMart||activeRoundGeneration!=battle.RoundGeneration)
            {
                if(!this.GetUtility<IRuntimeConfigProvider>().TryGetSlayerMart(out var config,out var error))
                {Log.Error(error);return;}
                var layout=config.Regions.SingleOrDefault(r=>r.Id==shop.Visit);
                if(layout==null){Log.Error("商店区域配置不存在："+shop.Visit);return;}
                activeSlayerMartLayout=layout;activeRoundGeneration=battle.RoundGeneration;presentedSlayerMart=true;
                activeRoundLayout=null;predictionIsland=null;predictedIceVelocity=Vector2.zero;
                hasPrediction=hasLocalFireBaseline=false;presentedInTreasureRoom=false;presentedRoomId=null;
                roundExitContact=roundExitPrompt=roundLoadPrompt=false;StageNotices.Clear();
                enemyIds.Clear();projectileIds.Clear();dropIds.Clear();shieldChargesByPlayer.Clear();nextPresentationId=1;
                view.Begin(activeArena.ArenaNumber,layout.HalfWidth,layout.HalfHeight,Array.Empty<ArcadeObstacleDefinition>());
                view.SetExits(Array.Empty<int>(),layout.HalfHeight,false);
            }
            lastAppliedBattleRevision=battle.BattleRevision;
            view.SetSlayerMartRegion(activeSlayerMartLayout,shop.CashBoxIds);
            view.BeginEffectSync();view.EndEffectSync();
            long self=this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId;
            foreach(var player in battle.Players)
            {
                var position=new Vector2(player.PositionX,player.PositionZ);var facing=new Vector2(player.AimX,player.AimZ);
                if(player.PlayerId==self)
                {
                    if(!hasPrediction||!shop.Ready||shop.Closing||shop.Members.Any(m=>m.PlayerId==self&&m.MenuOpen)||Vector2.Distance(predictedLocalPosition,position)>SnapDistance)
                        predictedLocalPosition=position;
                    else predictedLocalPosition=Vector2.Lerp(predictedLocalPosition,position,.35f);
                    hasPrediction=true;view.SetPlayer(predictedLocalPosition,facing,player.Alive,false);view.SetPlayerHealth(player.CurrentHealth,player.MaxHealth);
                }
                else view.SetRemotePlayer(player.PlayerId,position,facing,player.Alive,false,player.CurrentHealth,player.MaxHealth);
            }
        }
        /// <summary>商店只预测可移动的本人；菜单与慢加载冻结预测，不推进战斗、支援或截止时钟。</summary>
        private void TickSlayerMartPresentation()
        {
            var shop=latest.Battle.SlayerMart;
            if(!presentedSlayerMart||!shop.Prepared)return;
            if(!string.IsNullOrEmpty(view.SlayerMartRegionLoadError)){ShowSlayerMartLoadRetry();return;}
            long selfId=this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId;
            if(!shop.Ready||shop.Closing||latest.Battle.RoundLoading||!view.IsSlayerMartRegionReady||shop.Members.Any(m=>m.PlayerId==selfId&&m.MenuOpen))return;
            var player=latest.Battle.Players.FirstOrDefault(p=>p.PlayerId==selfId);if(!player.Alive)return;
            var input=this.GetSystem<OnlineArcadeRunSystem>().LastSampledInput;
            predictedLocalPosition=PredictLocalPosition(predictedLocalPosition,input.Move,catalog.Rule.PlayerMoveSpeed,Mathf.Min(Time.deltaTime,.05f),
                catalog.Rule.PlayerRadius,activeSlayerMartLayout.HalfWidth,activeSlayerMartLayout.HalfHeight,Array.Empty<ArcadeObstacleDefinition>());
            var authoritative=new Vector2(player.PositionX,player.PositionZ);
            if(Vector2.Distance(predictedLocalPosition,authoritative)>SnapDistance)predictedLocalPosition=authoritative;
            view.SetPlayer(predictedLocalPosition,new Vector2(player.AimX,player.AimZ),true,false);
        }
        /// <summary>重试同一代次资源；不发交易、不重置服务器库存与剩余时间。</summary>
        private void ShowSlayerMartLoadRetry()
        {
            if(roundLoadPrompt)return;roundLoadPrompt=true;int generation=activeRoundGeneration;
            Notify.ShowMessage(Loc.T("ui.arcade.menu.failed"),view.SlayerMartRegionLoadError,new List<MessageButtonConfig>{
                new(){ButtonText=Loc.T("ui.common.confirm"),Role=MessageButtonRole.Confirm,OnClick=()=>{
                    roundLoadPrompt=false;if(active&&activeRoundGeneration==generation&&latest.Battle?.SlayerMart?.Active==true)
                    {presentedSlayerMart=false;ApplyAuthoritativeSnapshot(latest.Battle);}}},
                new(){ButtonText=Loc.T("ui.common.cancel"),Role=MessageButtonRole.Cancel,OnClick=()=>roundLoadPrompt=false}});
        }
    }
}
