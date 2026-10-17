using System;
using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>正式出口只准备选择工作态，下一普通轮与轮前文件都保持原位。</summary>
        private bool TryBeginSlayerMart(ArcadeCombatRuntime runtime,int nextWave)
        {
            if(runtime.SlayerMartRulesVersion!=1||slayerMartConfig==null||runtime.IsChallenge||!UsesContentRounds(runtime)||
                !runtime.WaveClearAwarded||nextWave!=runtime.WaveIndex+1)return false;
            var run=this.GetModel<ArcadeRunModel>().Current;
            int completed=(runtime.ArenaNumber-1)*4+runtime.WaveIndex+1;
            var state=ArcadeSlayerMartRules.BeginVisit(run.RunId,completed,false,new long[]{1},
                (ulong)(uint)BuildCombatSeed(run.RunId,runtime.ArenaNumber)^(ulong)completed,slayerMartConfig,runtime.SlayerMart);
            if(state==null)return false;
            SyncSlayerMartInventory(runtime,state.Players[0]);runtime.SlayerMart=state;
            runtime.SlayerMartReturnWave=nextWave;runtime.SlayerMartRegionPrepared=false;ClearArcadeInput(runtime);return true;
        }
        /// <summary>本人快照单独克隆报价，只读界面不持有或修改个人能力账本。</summary>
        public ArcadeSlayerMartSnapshot GetSlayerMartSnapshot()
        {
            var runtime=this.GetModel<ArcadeCombatModel>().Runtime;
            if(runtime?.SlayerMart==null||runtime.SlayerMartReturnWave<=0||slayerMartConfig==null)return null;
            var state=runtime.SlayerMart.Clone();SyncSlayerMartInventory(runtime,state.Players[0]);
            return new ArcadeSlayerMartSnapshot(state,1,slayerMartConfig.Catalog,runtime.SlayerMartRegionError);
        }
        /// <summary>库存、支援与积分先算完整候选，重放只返回原成功，不再次应用现场效果。</summary>
        public bool TryOperateSlayerMart(ArcadeSlayerMartRequest request,out string error)
        {
            error="shop_not_active";var runtime=this.GetModel<ArcadeCombatModel>().Runtime;
            if(runtime?.SlayerMart==null||runtime.SlayerMartReturnWave<=0||slayerMartConfig==null||request==null)return false;
            SyncLiveSlayerMartClock(runtime,slayerMartRealtime());
            var current=runtime.SlayerMart.Clone();SyncSlayerMartInventory(runtime,current.Players[0]);
            bool receipt=current.Receipts.Any(p=>p.PlayerId==request.PlayerId&&p.OperationId==request.OperationId);
            if(!receipt&&current.Ready&&!view.IsSlayerMartRegionReady&&
                (request.Action is SlayerMartAction.Buy or SlayerMartAction.Reroll or SlayerMartAction.CollectCash))
            {error="not_ready";return false;}
            if(!receipt&&request.Action==SlayerMartAction.CollectCash)
            {
                var layout=SlayerMartLayout(runtime);int index=request.BoxId-1;
                if(!current.Ready||index<0||index>=layout.Boxes.Count||
                    (runtime.PlayerPosition-new Vector2(layout.Boxes[index].X,layout.Boxes[index].Y)).sqrMagnitude>layout.PickupRadius*layout.PickupRadius)
                {error="cash_out_of_range";return false;}
            }
            var result=ArcadeSlayerMartRules.Evaluate(current,request,slayerMartConfig.Catalog);
            if(!result.Success){error=result.Error;return false;}
            if(result.Replayed){error=null;return true;}
            var ordinary=SlayerMartOrdinarySnapshots(runtime,current.Players[0]);
            if(!ArcadeSlayerMartSupportRules.TryApplyGrants(result.Candidate,slayerMartConfig,
                new Dictionary<long,int>{{1,TryGetPowerup(runtime,global::ArcadePowerupBehavior.Chicken,out _)?1:0}},out var supports,out error,ordinary)||
                !TrySlayerMartBenefits(runtime,supports,false,out var candidate,out var score,out error))return false;
            runtime.SlayerMart=candidate;runtime.Bombs=candidate.Players[0].Bombs;runtime.Dashes=candidate.Players[0].Dashes;
            foreach(var kind in new[]{SlayerMartSupportKind.Saw,SlayerMartSupportKind.Barrel})
                if(candidate.Players[0].Supports.Any(p=>p.Kind==kind&&p.RemainingSeconds>0))
                    runtime.Powerups.Remove(kind==SlayerMartSupportKind.Saw?global::ArcadePowerupBehavior.Saw:global::ArcadePowerupBehavior.Barrel);
            this.GetModel<ArcadeRunModel>().SetTransientTreasureScore(score);error=null;return true;
        }
        /// <summary>普通库存的事实由战斗宿主同步，购物现金和券始终由商店账本唯一持有。</summary>
        private void SyncSlayerMartInventory(ArcadeCombatRuntime runtime,ArcadeSlayerMartPlayer player)
        {
            player.Bombs=runtime.Bombs;player.Dashes=runtime.Dashes;player.Lives=this.GetModel<ArcadeRunModel>().Current.RemainingLives;
            player.Chickens=player.Supports.Count(p=>p.Kind==SlayerMartSupportKind.Chicken&&p.RemainingSeconds>0)+
                (TryGetPowerup(runtime,global::ArcadePowerupBehavior.Chicken,out _)?1:0);
        }
        /// <summary>普通锯／桶只传递正式槽逻辑值，候选成功前不删除现场对象。</summary>
        private IReadOnlyDictionary<long,IReadOnlyList<ArcadeSlayerMartEffectInstance>> SlayerMartOrdinarySnapshots(ArcadeCombatRuntime runtime,ArcadeSlayerMartPlayer player)
        {
            var items=new List<ArcadeSlayerMartEffectInstance>();
            foreach(var kind in new[]{SlayerMartSupportKind.Saw,SlayerMartSupportKind.Barrel})
            {
                var behavior=kind==SlayerMartSupportKind.Saw?global::ArcadePowerupBehavior.Saw:global::ArcadePowerupBehavior.Barrel;
                if(!TryGetPowerup(runtime,behavior,out var powerup))continue;
                var item=ArcadeSlayerMartSupportRules.OrdinaryBody(player,kind,powerup.RemainingSeconds);
                item.ProductId=powerup.ProductId;item.CooldownSeconds=powerup.CooldownSeconds;item.Charges=powerup.Charges;items.Add(item);
            }
            return new Dictionary<long,IReadOnlyList<ArcadeSlayerMartEffectInstance>>{{1,items}};
        }
        /// <summary>区域坐标只从当前正式店次读取，不复用普通轮障碍。</summary>
        private ArcadeSlayerMartRegionLayout SlayerMartLayout(ArcadeCombatRuntime runtime)=>slayerMartConfig.Regions.Single(p=>p.Id==runtime.SlayerMart.Visit);
        /// <summary>一次候选贯穿正式积分规则，应用成功前不修改RunModel。</summary>
        private bool TrySlayerMartBenefits(ArcadeCombatRuntime runtime,ArcadeSlayerMartState state,bool nextRound,
            out ArcadeSlayerMartState candidate,out ArcadeTreasureScoreState score,out string error)
        {
            var run=this.GetModel<ArcadeRunModel>().Current;
            var treasure=catalog.TreasureRooms.First(p=>p.Rewards.Any(r=>r.Kind==FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Gem));
            bool ok=ArcadeSlayerMartBenefitRules.TryDeliver(state,slayerMartConfig,treasure,
                new Dictionary<long,ArcadeTreasureScoreState>{{1,new(run.Score,run.ScoreMultiplier,run.MultiplierProgress,run.RemainingLives)}},nextRound,out candidate,out var scores,out error);
            score=ok?scores[1]:default;return ok;
        }
        /// <summary>在下一普通轮资源全部就绪后消费加命，同一轮失败重试只保存同一已消费候选。</summary>
        private bool TryApplySlayerMartRoundBenefits(ArcadeCombatRuntime runtime,out string error)
        {
            error=null;if(!runtime.SlayerMartRoundBenefitsPending)return true;
            if(!TrySlayerMartBenefits(runtime,runtime.SlayerMart,true,out var candidate,out var score,out error))return false;
            runtime.SlayerMart=candidate;this.GetModel<ArcadeRunModel>().SetTransientTreasureScore(score);
            runtime.Bombs=ArcadeOpeningRules.ReplenishEmpty(runtime.Bombs);runtime.Dashes=ArcadeOpeningRules.ReplenishEmpty(runtime.Dashes);
            SyncSlayerMartInventory(runtime,runtime.SlayerMart.Players[0]);
            runtime.SlayerMartRoundBenefitsPending=false;return true;
        }
        /// <summary>只重试当前店次的场景准备，保留现金、卡牌、已接收交易和300秒剩余。</summary>
        public bool TryRetrySlayerMartPreparation()
        {
            var runtime=this.GetModel<ArcadeCombatModel>().Runtime;
            if(runtime?.SlayerMart==null||runtime.SlayerMartReturnWave<=0||!runtime.CompletionAwaitingRetry)return false;
            runtime.SlayerMartRegionPrepared=false;runtime.SlayerMartRegionError=null;runtime.CompletionAwaitingRetry=false;return true;
        }
        /// <summary>接在现有战斗生命周期；菜单只阻止移动，不能阻止区域硬截止。</summary>
        private bool TickSlayerMartRegion(ArcadeCombatRuntime runtime,float seconds)
            =>TickSlayerMartRegionFrame(runtime,seconds,seconds);
        /// <summary>期限与移动分开输入；实际长帧不能延长期限或放大位移。</summary>
        private bool TickSlayerMartRegionFrame(ArcadeCombatRuntime runtime,float seconds,float motionSeconds)
        {
            if(runtime.SlayerMartReturnWave<=0||runtime.SlayerMart==null)return false;
            if(runtime.CompletionAwaitingRetry){ClearArcadeInput(runtime);return true;}
            var state=runtime.SlayerMart;var app=this.GetModel<AppStateModel>();bool paused=app!=null&&app.CurrentState.Value!=AppState.Playing;
            var layout=SlayerMartLayout(runtime);
            if(!paused&&!state.Closing&&state.Players.All(p=>p.Choice==1)&&!runtime.SlayerMartRegionPrepared)
            {
                try
                {
                    SuspendIslandFirstPerson(runtime);view.Begin(arena.ArenaNumber,layout.HalfWidth,layout.HalfHeight,Array.Empty<ArcadeObstacleDefinition>());
                    view.SetSlayerMartRegion(layout,Enumerable.Range(1,layout.Boxes.Count).ToArray());runtime.PlayerPosition=new Vector2(layout.SpawnX,layout.SpawnY);
                    runtime.SlayerMartRegionPrepared=true;
                }
                catch(Exception exception){runtime.SlayerMartRegionError=exception.Message;FailIslandTransition(runtime,exception.Message);return true;}
            }
            if(runtime.SlayerMartRegionPrepared&&!string.IsNullOrEmpty(view.SlayerMartRegionLoadError))
            {runtime.SlayerMartRegionError=view.SlayerMartRegionLoadError;FailIslandTransition(runtime,runtime.SlayerMartRegionError);return true;}
            runtime.SlayerMart=ArcadeSlayerMartRules.AdvanceVisit(state,seconds,runtime.SlayerMartRegionPrepared&&view.IsSlayerMartRegionReady,paused);
            if(runtime.SlayerMart.Closing)
            {
                if(runtime.SlayerChallenge!=null){runtime.SlayerMartReturnWave=0;runtime.SlayerChallenge=ArcadeSlayerChallengeRules.Advance(runtime.SlayerChallenge,slayerChallengeConfig,slayerChallengeConfig.ShopSeconds,true);CompleteSlayerChallenge(runtime);return true;}
                int wave=runtime.SlayerMartReturnWave;runtime.SlayerMartReturnWave=0;runtime.SlayerMartRegionPrepared=false;
                runtime.SlayerMartRoundBenefitsPending=true;StartContentRound(runtime,wave);return true;
            }
            if(!runtime.SlayerMart.Ready||!view.IsSlayerMartRegionReady){ClearArcadeInput(runtime);return true;}
            if(!paused&&!UIHelper.HasUIOpen&&!runtime.SlayerMart.Players[0].MenuOpen)
            {
                if(view.TryGetPlayerPosition(out var actual))runtime.PlayerPosition=new Vector2(Mathf.Clamp(actual.x,-layout.HalfWidth+catalog.Rule.PlayerRadius,layout.HalfWidth-catalog.Rule.PlayerRadius),Mathf.Clamp(actual.y,-layout.HalfHeight+catalog.Rule.PlayerRadius,layout.HalfHeight-catalog.Rule.PlayerRadius));
                runtime.MoveDirection=input?.MoveInput??Vector2.zero;runtime.Firing=false;
                float speed=CalculatePlayerSpeed(catalog.Rule.PlayerMoveSpeed,UpgradeTotal(this.GetModel<ArcadeRunModel>().Current,global::ArcadeUpgradeKind.MoveSpeedPercent),false,0);
                runtime.PlayerPosition=ResolvePosition(runtime.PlayerPosition+runtime.MoveDirection*speed*motionSeconds,catalog.Rule.PlayerRadius,layout.HalfWidth,layout.HalfHeight,Array.Empty<ArcadeObstacleDefinition>());
            }
            else ClearArcadeInput(runtime);
            if(!paused)for(int index=0;index<layout.Boxes.Count;index++)
            {
                var current=runtime.SlayerMart;var point=layout.Boxes[index];if(current.CollectedBoxes.Contains(index+1)||
                    (runtime.PlayerPosition-new Vector2(point.X,point.Y)).sqrMagnitude>layout.PickupRadius*layout.PickupRadius)continue;
                TryOperateSlayerMart(new(){Action=SlayerMartAction.CollectCash,PlayerId=1,RegionInstanceId=current.RegionInstanceId,Generation=current.Generation,
                    ExpectedRevision=current.Revision,OperationId="cash:"+(index+1),BoxId=index+1},out _);
            }
            view.SetSlayerMartRegion(layout,Enumerable.Range(1,layout.Boxes.Count).Where(id=>!runtime.SlayerMart.CollectedBoxes.Contains(id)).ToArray());
            view.SetPlayer(runtime.PlayerPosition,runtime.AimDirection,true,false);view.BeginEffectSync();view.EndEffectSync();return true;
        }
    }
}
