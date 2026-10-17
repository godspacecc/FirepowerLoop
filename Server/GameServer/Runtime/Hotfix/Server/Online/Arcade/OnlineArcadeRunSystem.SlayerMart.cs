using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>真实成员库存只同步到候选，商店现金和券不从旧RunGold导入。</summary>
        private static void SyncSlayerMartInventory(OnlineArcadeRunActor actor,ArcadeSlayerMartState state)
        {
            foreach(var member in state.Players)
            {
                var record=actor.Record.Players.Single(p=>p.PlayerId==member.PlayerId);
                var player=actor.Battle.Players[member.PlayerId];member.Bombs=record.Bombs;member.Dashes=record.Dashes;member.Lives=record.Lives;
                member.Chickens=member.Supports.Count(p=>p.Kind==SlayerMartSupportKind.Chicken&&p.RemainingSeconds>0)+
                    (TryGetPowerup(player,ArcadePowerupBehavior.Chicken,out _)?1:0);
            }
        }
        /// <summary>权威Actor串行一次发布扣款、全部支援与积分候选；回执重放优先于截止和离区门禁。</summary>
        public static ArcadeSlayerMartResult OperateSlayerMart(this OnlineArcadeRunActor actor,long authenticatedPlayer,ArcadeSlayerMartRequest request)
        {
            var battle=actor.Battle;
            if(battle?.SlayerMart==null||battle.SlayerMartRulesVersion!=1||request==null||request.PlayerId!=authenticatedPlayer||
                !battle.Players.ContainsKey(authenticatedPlayer))return SlayerMartFail("invalid_player");
            var config=ServerConfig.Instance.SlayerMart;var current=battle.SlayerMart.Clone();SyncSlayerMartInventory(actor,current);
            var evaluated=ArcadeSlayerMartRules.Evaluate(current,request,config.Catalog);
            if(!evaluated.Success||evaluated.Replayed)return evaluated;
            if(battle.SlayerMartReturnWave<=0||actor.DisconnectedAtByPlayer.Count>0||battle.RoundTransitionPending)return SlayerMartFail("shop_not_active");
            if(request.Action==SlayerMartAction.CollectCash)
            {
                var layout=config.Regions.Single(p=>p.Id==current.Visit);int index=request.BoxId-1;var player=battle.Players[authenticatedPlayer];
                if(index<0||index>=layout.Boxes.Count||!player.Alive||DistanceSquared(player.PositionX,player.PositionZ,layout.Boxes[index].X,layout.Boxes[index].Y)>layout.PickupRadius*layout.PickupRadius)
                    return SlayerMartFail("cash_out_of_range");
            }
            var ordinary=new Dictionary<long,IReadOnlyList<ArcadeSlayerMartEffectInstance>>();var chickens=new Dictionary<long,int>();
            foreach(var owner in current.Players)
            {
                var player=battle.Players[owner.PlayerId];var items=new List<ArcadeSlayerMartEffectInstance>();
                chickens[owner.PlayerId]=TryGetPowerup(player,ArcadePowerupBehavior.Chicken,out _)?1:0;
                foreach(var kind in new[]{SlayerMartSupportKind.Saw,SlayerMartSupportKind.Barrel})
                {
                    var behavior=kind==SlayerMartSupportKind.Saw?ArcadePowerupBehavior.Saw:ArcadePowerupBehavior.Barrel;
                    if(!TryGetPowerup(player,behavior,out var powerup)||kind==SlayerMartSupportKind.Barrel&&powerup.Charges<=0)continue;
                    var item=ArcadeSlayerMartSupportRules.OrdinaryBody(owner,kind,powerup.RemainingSeconds);
                    item.ProductId=powerup.ProductId;item.Charges=powerup.Charges;item.CooldownSeconds=powerup.CooldownSeconds;items.Add(item);
                }
                ordinary[owner.PlayerId]=items;
            }
            if(!ArcadeSlayerMartSupportRules.TryApplyGrants(evaluated.Candidate,config,chickens,out var supports,out var error,ordinary)||
                !TrySlayerMartBenefits(actor.Record,supports,false,out var candidate,out var scores,out error))return SlayerMartFail(error);
            battle.SlayerMart=candidate;
            foreach(var owner in candidate.Players)
            {
                var record=actor.Record.Players.Single(p=>p.PlayerId==owner.PlayerId);record.Bombs=owner.Bombs;record.Dashes=owner.Dashes;
                ApplySlayerMartScore(record,scores[owner.PlayerId]);var player=battle.Players[owner.PlayerId];
                foreach(var kind in new[]{SlayerMartSupportKind.Saw,SlayerMartSupportKind.Barrel})
                    if(owner.Supports.Any(p=>p.Kind==kind&&p.RemainingSeconds>0))
                        player.Powerups.Remove(kind==SlayerMartSupportKind.Saw?ArcadePowerupBehavior.Saw:ArcadePowerupBehavior.Barrel);
            }
            battle.BattleRevision++;return new ArcadeSlayerMartResult{Success=true,Candidate=candidate.Clone()};
        }
        /// <summary>只返回本人经济与个人报价；其他成员只公开选择、菜单、完成状态和既有命数。</summary>
        public static OnlineSlayerMartSnapshotInfo BuildPersonalSlayerMart(this OnlineArcadeRunActor actor,long playerId)
        {
            var battle=actor.Battle;if(battle?.SlayerMart==null||battle.SlayerMartReturnWave<=0||!battle.Players.ContainsKey(playerId))return null;
            var state=battle.SlayerMart.Clone();SyncSlayerMartInventory(actor,state);var owner=state.Players.SingleOrDefault(p=>p.PlayerId==playerId);if(owner==null)return null;
            var info=OnlineSlayerMartSnapshotInfo.Create();info.RegionInstanceId=state.RegionInstanceId;info.Generation=state.Generation;info.Revision=state.Revision;
            info.Visit=state.Visit;info.PlayerId=playerId;info.Cash=owner.Cash;info.Tokens=owner.Tokens;info.RemainingSeconds=state.RemainingSeconds;
            info.Ready=state.Ready;info.Closing=state.Closing;info.Bombs=owner.Bombs;info.Dashes=owner.Dashes;info.Lives=owner.Lives;
            info.VestSlots=owner.VestSlots;info.Plates=owner.Plates;info.Owned.AddRange(owner.Owned.OrderBy(p=>p,StringComparer.Ordinal));info.Error=battle.RoundLoadError;
            var catalog=ServerConfig.Instance.SlayerMart.Catalog;
            foreach(string id in owner.Offers)
            {
                var item=catalog.Items.Single(p=>p.Id==id);var offer=OnlineSlayerMartOfferInfo.Create();offer.ItemId=id;
                offer.Error=ArcadeSlayerMartRules.PurchaseError(state,owner,item,catalog)??(owner.Cash<item.Price?"insufficient_cash":string.Empty);info.Offers.Add(offer);
            }
            foreach(var member in state.Players)info.Members.Add(BuildSlayerMartMember(member));return info;
        }
        /// <summary>所有广播继续调用无个人参数的BuildSnapshot，只有定向响应使用此包装。</summary>
        public static OnlineArcadeRunSnapshot BuildSnapshotForPlayer(this OnlineArcadeRunActor actor,long playerId)
        {var snapshot=actor.BuildSnapshot();if(snapshot!=null)snapshot.SelfSlayerMart=actor.BuildPersonalSlayerMart(playerId);return snapshot;}
        private static OnlineSlayerMartMemberInfo BuildSlayerMartMember(ArcadeSlayerMartPlayer member)
        {var info=OnlineSlayerMartMemberInfo.Create();info.PlayerId=member.PlayerId;info.Choice=member.Choice;info.MenuOpen=member.MenuOpen;info.Finished=member.Finished;info.Lives=member.Lives;return info;}
        /// <summary>积分候选与已有正式Gem参数共享，下一普通轮标记由管理器写盘事务调用。</summary>
        private static bool TrySlayerMartBenefits(OnlineArcadeRunRecord record,ArcadeSlayerMartState state,bool nextRound,
            out ArcadeSlayerMartState candidate,out Dictionary<long,ArcadeTreasureScoreState> scores,out string error)
        {
            var treasure=ServerConfig.Instance.Tables.TbArcadeTreasureRoom.DataList.Select(ArcadeTreasureRoomParameters.FromConfig)
                .First(p=>p.Rewards.Any(r=>r.Kind==FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Gem));
            var current=record.Players.ToDictionary(p=>p.PlayerId,p=>new ArcadeTreasureScoreState(p.Score,p.ScoreMultiplier,p.MultiplierProgress,p.Lives));
            return ArcadeSlayerMartBenefitRules.TryDeliver(state,ServerConfig.Instance.SlayerMart,treasure,current,nextRound,out candidate,out scores,out error);
        }
        private static void ApplySlayerMartScore(OnlineArcadePlayerRecord record,ArcadeTreasureScoreState score)
        {record.Score=score.Score;record.ScoreMultiplier=score.Multiplier;record.MultiplierProgress=score.Progress;record.Lives=score.Lives;}
        private static ArcadeSlayerMartResult SlayerMartFail(string error)=>new(){Error=error};
        /// <summary>店次活动在普通战斗帧之前处理；双方资源就绪前和断线期间不扣购物时长。</summary>
        private static bool TickSlayerMartRegion(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime battle,float seconds)
        {
            if(battle.SlayerMartReturnWave<=0||battle.SlayerMart==null)return false;
            if(actor.DisconnectedAtByPlayer.Count>0)return true;
            var config=ServerConfig.Instance.SlayerMart;var state=battle.SlayerMart;var layout=config.Regions.Single(p=>p.Id==state.Visit);
            if(!state.Ready&&!state.Closing&&!battle.SlayerMartRegionPrepared&&state.Players.All(p=>p.Choice==1))
            {
                battle.SlayerMartRegionPrepared=true;battle.RoundGeneration=checked(battle.RoundGeneration+1);
                int index=0;foreach(var player in battle.Players.Values.OrderBy(p=>p.PlayerId))
                {
                    player.ReadyRoundGeneration=-1;player.MoveX=player.MoveZ=0;player.Firing=false;
                    player.PositionX=layout.SpawnX+(index++-(battle.Players.Count-1)*.5f)*ServerConfig.Instance.ArcadeRule.PlayerRadius*2;
                    player.PositionZ=layout.SpawnY;
                }
            }
            battle.SlayerMart=ArcadeSlayerMartRules.AdvanceVisit(state,seconds,battle.SlayerMartRegionPrepared&&RoundPlayersReady(battle),false);
            if(battle.SlayerMart.Closing)
            {
                battle.SlayerMartReturnWave=0;battle.SlayerMartRegionPrepared=false;battle.RoundTransitionPending=true;battle.PendingBossEncounter=false;
                foreach(var player in battle.Players.Values){player.MoveX=player.MoveZ=0;player.Firing=false;}return true;
            }
            if(!battle.SlayerMart.Ready)return true;
            foreach(var player in battle.Players.Values.OrderBy(p=>p.PlayerId))
            {
                if(!player.Alive)continue;var owner=battle.SlayerMart.Players.Single(p=>p.PlayerId==player.PlayerId);
                var record=actor.Record.Players.Single(p=>p.PlayerId==player.PlayerId);
                if(!owner.MenuOpen&&TimeInfo.Instance.ServerNow()-player.LastInputAt<=InputExpiryMilliseconds)
                {
                    float speed=ServerConfig.Instance.ArcadeRule.PlayerMoveSpeed*(1+UpgradeTotal(record,ArcadeUpgradeKind.MoveSpeedPercent));
                    float radius=ServerConfig.Instance.ArcadeRule.PlayerRadius;
                    player.PositionX=Math.Clamp(player.PositionX+player.MoveX*speed*seconds,-layout.HalfWidth+radius,layout.HalfWidth-radius);
                    player.PositionZ=Math.Clamp(player.PositionZ+player.MoveZ*speed*seconds,-layout.HalfHeight+radius,layout.HalfHeight-radius);
                }
                for(int index=0;index<layout.Boxes.Count;index++)
                {
                    var current=battle.SlayerMart;if(current.CollectedBoxes.Contains(index+1)||
                        DistanceSquared(player.PositionX,player.PositionZ,layout.Boxes[index].X,layout.Boxes[index].Y)>layout.PickupRadius*layout.PickupRadius)continue;
                    actor.OperateSlayerMart(player.PlayerId,new(){Action=SlayerMartAction.CollectCash,PlayerId=player.PlayerId,RegionInstanceId=current.RegionInstanceId,
                        Generation=current.Generation,ExpectedRevision=current.Revision,OperationId="cash:"+(index+1).ToString(CultureInfo.InvariantCulture),BoxId=index+1});
                }
            }
            return true;
        }
    }
}
