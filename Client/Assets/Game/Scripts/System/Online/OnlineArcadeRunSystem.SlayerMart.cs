using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ET;
using FirepowerLoop.Arcade;
using Game.Network;
using QFramework;

namespace Game
{
    /// <summary>商店请求的传输结果；报价和库存始终以最新权威快照为准。</summary>
    public readonly struct OnlineSlayerMartOperationResult
    {
        public OnlineArcadeResultCode Code { get; }
        public bool Accepted { get; }
        public bool Replayed { get; }
        public string Error { get; }
        public OnlineSlayerMartOperationResult(OnlineArcadeResultCode code,bool accepted=false,bool replayed=false,string error=null)
        {Code=code;Accepted=accepted;Replayed=replayed;Error=error;}
    }

    public sealed partial class OnlineArcadeRunSystem
    {
        /// <summary>公共广播不携带报价；缓存仅属于当前账号、对局、区域和代次。</summary>
        private ArcadeSlayerMartSnapshot personalSlayerMart;
        private long personalSlayerMartRun;
        /// <summary>不确定响应保留原意图；客户端不预扣现金或应用能力。</summary>
        private ArcadeSlayerMartRequest pendingSlayerMart;
        private CancellationTokenSource slayerMartCancellation;
        public bool HasPendingSlayerMartOperation=>pendingSlayerMart!=null;
        public bool IsSlayerMartOperationInProgress=>slayerMartCancellation!=null;
        public ArcadeSlayerMartSnapshot GetSlayerMartSnapshot()=>personalSlayerMart;

        /// <summary>只合并当前身份的定向报价；公共时间和成员状态独立前进。</summary>
        private void ObserveSlayerMart(ET.OnlineArcadeRunSnapshot run)
        {
            var battle=run.Battle;
            if(battle==null||!battle.SlayerMartActive||battle.SlayerMartRulesVersion!=1)
            {ClearSlayerMart();return;}
            long self=this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId;
            string region=$"{run.RunId}:slayermart:{battle.SlayerMartVisit}";
            if(personalSlayerMart!=null&&(personalSlayerMartRun!=run.RunId||personalSlayerMart.PlayerId!=self||
                personalSlayerMart.RegionInstanceId!=region||personalSlayerMart.Generation!=battle.SlayerMartGeneration))ClearSlayerMart();
            var q=run.SelfSlayerMart;
            if(q!=null&&q.PlayerId==self&&q.RegionInstanceId==region&&q.Generation==battle.SlayerMartGeneration&&
                q.Visit==battle.SlayerMartVisit&&q.Revision<=battle.SlayerMartRevision&&
                (personalSlayerMart==null||q.Revision>=personalSlayerMart.Revision)&&
                this.GetUtility<IRuntimeConfigProvider>().TryGetSlayerMart(out var config,out _))
            {
                var offers=q.Offers.Select(o=>new {Quote=o,Item=config.Catalog.Items.SingleOrDefault(i=>i.Id==o.ItemId)}).ToArray();
                if(offers.All(o=>o.Item!=null)&&offers.Select(o=>o.Item.Id).Distinct(StringComparer.Ordinal).Count()==offers.Length)
                {
                    personalSlayerMart=new ArcadeSlayerMartSnapshot(region,q.Generation,q.Revision,q.Visit,self,
                        q.RemainingSeconds,q.Cash,q.Tokens,q.Bombs,q.Dashes,q.Lives,q.VestSlots,q.Plates,
                        Stage(battle),q.Error,offers.Select(o=>new ArcadeSlayerMartOfferSnapshot(o.Item,o.Quote.Error)),
                        q.Members.Select(Member),q.Owned);
                    personalSlayerMartRun=run.RunId;
                }
            }
            if(personalSlayerMart is { } old)
                personalSlayerMart=new ArcadeSlayerMartSnapshot(old.RegionInstanceId,old.Generation,old.Revision,old.Visit,old.PlayerId,
                    battle.SlayerMartSeconds,old.Cash,old.Tokens,old.Bombs,old.Dashes,old.Lives,old.VestSlots,old.Plates,
                    Stage(battle),old.Error,old.Offers,battle.SlayerMartMembers.Select(Member),old.Owned);
        }
        /// <summary>公共战斗先到时仍独立吸收更新的本人报价；公共时钟、成员及地图绝不回滚。</summary>
        private bool TryMergeLateSlayerMartQuote(ET.OnlineArcadeRunSnapshot run)
        {
            var current=lastSnapshot.Battle;var region=current?.SlayerMart;var oldBattle=run.Battle;var quote=run.SelfSlayerMart;
            if(!lastSnapshot.HasRun||run.RunId!=lastSnapshot.RunId||!MatchesLocalConfig(run.ConfigFingerprint)||
                region?.Active!=true||region.RulesVersion!=1||oldBattle==null||!oldBattle.SlayerMartActive||
                oldBattle.SlayerMartRulesVersion!=1||oldBattle.ArenaNumber!=current.ArenaNumber||
                oldBattle.RoundGeneration!=current.RoundGeneration||oldBattle.SlayerMartVisit!=region.Visit||
                oldBattle.SlayerMartGeneration!=region.Generation||quote==null||quote.Revision>region.Revision||
                personalSlayerMart!=null&&quote.Revision<=personalSlayerMart.Revision)return false;
            long previous=personalSlayerMart?.Revision??-1;
            ObserveSlayerMart(run);
            var value=personalSlayerMart;
            if(value==null)return false;
            var stage=region.Closing?ArcadeSlayerMartStage.Closing:region.Ready?ArcadeSlayerMartStage.Shopping:
                region.Members.Count>0&&region.Members.All(m=>m.Choice==1)?ArcadeSlayerMartStage.Loading:ArcadeSlayerMartStage.Choosing;
            personalSlayerMart=new ArcadeSlayerMartSnapshot(value.RegionInstanceId,value.Generation,value.Revision,value.Visit,value.PlayerId,
                region.RemainingSeconds,value.Cash,value.Tokens,value.Bombs,value.Dashes,value.Lives,value.VestSlots,value.Plates,
                stage,value.Error,value.Offers,region.Members,value.Owned);
            return value.Revision>previous;
        }
        private static ArcadeSlayerMartStage Stage(OnlineArcadeBattleInfo battle)=>battle.SlayerMartClosing?ArcadeSlayerMartStage.Closing:
            battle.SlayerMartReady?ArcadeSlayerMartStage.Shopping:battle.SlayerMartMembers.Count>0&&battle.SlayerMartMembers.All(m=>m.Choice==1)?ArcadeSlayerMartStage.Loading:ArcadeSlayerMartStage.Choosing;
        private static ArcadeSlayerMartMemberSnapshot Member(OnlineSlayerMartMemberInfo m)=>new(m.PlayerId,m.Choice,m.MenuOpen,m.Finished,m.Lives);
        private void ClearSlayerMart()
        {
            personalSlayerMart=null;personalSlayerMartRun=0;pendingSlayerMart=null;
            slayerMartCancellation?.Cancel();slayerMartCancellation=null;
        }
        /// <summary>只接受本人当前区域意图；提交前复制，防止调用方在超时后改写重试载荷。</summary>
        public Task<OnlineSlayerMartOperationResult> OperateSlayerMartAsync(ArcadeSlayerMartRequest request)
        {
            if(pendingSlayerMart!=null)return Task.FromResult(new OnlineSlayerMartOperationResult(OnlineArcadeResultCode.OperationInProgress));
            var q=personalSlayerMart;
            if(request==null||q==null||request.PlayerId!=q.PlayerId||request.RegionInstanceId!=q.RegionInstanceId||request.Generation!=q.Generation||
                string.IsNullOrWhiteSpace(request.OperationId))return Task.FromResult(new OnlineSlayerMartOperationResult(OnlineArcadeResultCode.InvalidState));
            pendingSlayerMart=new ArcadeSlayerMartRequest {Action=request.Action,PlayerId=request.PlayerId,RegionInstanceId=request.RegionInstanceId,
                Generation=request.Generation,ExpectedRevision=request.ExpectedRevision,OperationId=request.OperationId,ItemId=request.ItemId,
                BoxId=request.BoxId,ConfirmedAmount=request.ConfirmedAmount,Value=request.Value};
            return SendPendingSlayerMartAsync();
        }
        /// <summary>未知结果只重发原请求；不创建新ID，也不改用新报价版本。</summary>
        public Task<OnlineSlayerMartOperationResult> RetryPendingSlayerMartAsync()=>SendPendingSlayerMartAsync();
        private async Task<OnlineSlayerMartOperationResult> SendPendingSlayerMartAsync()
        {
            if(slayerMartCancellation!=null)return new(OnlineArcadeResultCode.OperationInProgress);
            var intent=pendingSlayerMart;if(intent==null)return new(OnlineArcadeResultCode.InvalidState);
            var account=this.GetModel<OnlineAccountModel>().Snapshot.Value;
            if(!account.IsAuthenticated||account.PlayerId!=intent.PlayerId||network?.ConnectionState!=NetworkConnectionState.Connected)
                return new(OnlineArcadeResultCode.Disconnected);
            var cancellation=new CancellationTokenSource();slayerMartCancellation=cancellation;long runId=lastSnapshot.RunId;
            try
            {
                var request=C2G_OperateOnlineSlayerMart.Create();request.ProtocolFingerprint=ArcadeProtocolFingerprint();
                request.RegionInstanceId=intent.RegionInstanceId;request.Generation=intent.Generation;request.ExpectedRevision=intent.ExpectedRevision;
                request.OperationId=intent.OperationId;request.ActionKind=(int)intent.Action;request.ItemId=intent.ItemId;request.BoxId=intent.BoxId;
                request.ConfirmedAmount=intent.ConfirmedAmount;request.Value=intent.Value;
                var response=await network.CallAsync<G2C_OperateOnlineSlayerMart>(request,RpcTimeoutMilliseconds,cancellation.Token);
                if(cancellation.IsCancellationRequested||!ReferenceEquals(pendingSlayerMart,intent)||lastSnapshot.RunId!=runId)
                    return new(OnlineArcadeResultCode.Disconnected);
                var code=MapServerError(response.Error);
                if(response.Run!=null&&response.Run.RunId==runId)TryPublishNewerSnapshot(response.Run);
                if(code!=OnlineArcadeResultCode.UnknownFailure)pendingSlayerMart=null;
                return new(code,response.Accepted,response.Replayed,response.Message);
            }
            catch(TimeoutException){return new(OnlineArcadeResultCode.Timeout);}
            catch(OperationCanceledException){return new(OnlineArcadeResultCode.Disconnected);}
            catch{return new(OnlineArcadeResultCode.UnknownFailure);}
            finally{if(ReferenceEquals(slayerMartCancellation,cancellation))slayerMartCancellation=null;cancellation.Dispose();}
        }
    }
}
