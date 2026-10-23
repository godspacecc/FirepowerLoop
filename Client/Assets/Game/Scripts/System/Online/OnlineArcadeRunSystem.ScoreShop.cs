using System;
using System.Linq;
using System.Threading.Tasks;
using ET;
using FirepowerLoop.Arcade;
using Newtonsoft.Json;
using QFramework;

namespace Game
{
    public sealed partial class OnlineArcadeRunSystem
    {
        /// <summary>个人商店是权威快照的副本；View无法修改服务器账本。</summary>
        private ArcadeScoreShopState personalScoreShop;
        private long personalScoreShopRun;
        /// <summary>请求结果不确定时保留原身份、版本与载荷，同一按钮重试复用操作Id。</summary>
        private string pendingScoreShopOperation, pendingScoreShopDrop;
        private ScoreShopAction pendingScoreShopAction;
        private long pendingScoreShopRun, pendingScoreShopPlayer, pendingScoreShopRevision;
        /// <summary>未知交易属于哪次整关访问；恢复到新关时旧意图不能锁住新报价。</summary>
        private int pendingScoreShopArena;

        public ArcadeScoreShopState GetScoreShopState()=>personalScoreShop?.Clone();
        public bool HasPendingScoreShopOperation=>pendingScoreShopOperation!=null;
        public ScoreShopAction PendingScoreShopAction=>pendingScoreShopAction;
        public string PendingScoreShopDropId=>pendingScoreShopDrop;
        /// <summary>未知结果只重发原意图，不能改买另一张卡。</summary>
        public Task<OnlineArcadeOperationResult> RetryPendingScoreShopAsync()=>pendingScoreShopOperation==null
            ? Task.FromResult(new OnlineArcadeOperationResult(OnlineArcadeResultCode.InvalidState,lastSnapshot))
            : OperateScoreShopAsync(pendingScoreShopAction,pendingScoreShopDrop);

        private void ObserveScoreShop(ET.OnlineArcadeRunSnapshot source)
        {
            long self=this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId;
            var player=source.Players.FirstOrDefault(p=>p.PlayerId==self);
            if(personalScoreShopRun!=0&&personalScoreShopRun!=source.RunId || pendingScoreShopOperation!=null &&
                (pendingScoreShopRun!=source.RunId || pendingScoreShopPlayer!=self))ClearScoreShop();
            personalScoreShopRun=source.RunId;
            try { personalScoreShop=string.IsNullOrEmpty(player?.ScoreShopJson)?null:JsonConvert.DeserializeObject<ArcadeScoreShopState>(player.ScoreShopJson); }
            catch { personalScoreShop=null; }
            // 完成推送已证实购买／跳过落定；即使RPC丢失，下一次访问也不能重放旧关动作。
            if(pendingScoreShopOperation!=null&&personalScoreShop!=null&&
                (personalScoreShop.Completed||personalScoreShop.NextArena!=pendingScoreShopArena))
            {pendingScoreShopOperation=null;pendingScoreShopDrop=null;}
        }

        /// <summary>只提交购买意图，不本地扣分；失败后再次点击同动作会重试原请求。</summary>
        public async Task<OnlineArcadeOperationResult> OperateScoreShopAsync(ScoreShopAction action,string dropId)
        {
            var account=this.GetModel<OnlineAccountModel>().Snapshot.Value;
            if(!account.IsAuthenticated || !lastSnapshot.HasRun)
                return new OnlineArcadeOperationResult(OnlineArcadeResultCode.Disconnected,lastSnapshot);
            long self=account.PlayerId;
            if(pendingScoreShopOperation!=null)
            {
                if(pendingScoreShopRun!=lastSnapshot.RunId||pendingScoreShopPlayer!=self)
                    ClearScoreShop();
                else if(pendingScoreShopAction!=action||pendingScoreShopDrop!=(dropId??string.Empty))
                    return new OnlineArcadeOperationResult(OnlineArcadeResultCode.OperationInProgress,lastSnapshot);
            }
            if(pendingScoreShopOperation==null)
            {
                if(personalScoreShop==null||personalScoreShop.Completed||lastSnapshot.Phase!=OnlineArcadeRunPhase.SafeCheckpoint||
                    action is not (ScoreShopAction.Buy or ScoreShopAction.Refresh or ScoreShopAction.Skip))
                    return new OnlineArcadeOperationResult(OnlineArcadeResultCode.InvalidState,lastSnapshot);
                pendingScoreShopOperation=Guid.NewGuid().ToString("N");pendingScoreShopRun=lastSnapshot.RunId;
                pendingScoreShopPlayer=self;pendingScoreShopRevision=lastSnapshot.Revision;
                pendingScoreShopArena=personalScoreShop.NextArena;
                pendingScoreShopAction=action;pendingScoreShopDrop=dropId??string.Empty;
            }
            string operation=pendingScoreShopOperation;long run=pendingScoreShopRun;
            var result=await Run(OnlineArcadeOperationState.SettingReady,async token=>
            {
                var request=C2G_OperateOnlineArcadeSafeCheckpoint.Create();request.ProtocolFingerprint=ArcadeProtocolFingerprint();
                request.OperationId=operation;request.ExpectedRevision=pendingScoreShopRevision;
                request.ActionKind=(int)pendingScoreShopAction+9;request.ScoreShopDropId=pendingScoreShopDrop;
                return await network.CallAsync<G2C_OperateOnlineArcadeSafeCheckpoint>(request,RpcTimeoutMilliseconds,token);
            });
            if(pendingScoreShopOperation==operation&&pendingScoreShopRun==run&&
                result.Code is not (OnlineArcadeResultCode.Timeout or OnlineArcadeResultCode.Disconnected or
                    OnlineArcadeResultCode.UnknownFailure or OnlineArcadeResultCode.OperationInProgress or OnlineArcadeResultCode.NotLoggedIn))
                pendingScoreShopOperation=null;
            return result;
        }

        /// <summary>临时断线只清画面，保留未知结果；明确结束或换账号/对局才丢弃请求身份。</summary>
        private void ClearScoreShop(bool clearPending=true)
        {
            personalScoreShop=null;personalScoreShopRun=0;
            if(clearPending){pendingScoreShopOperation=null;pendingScoreShopDrop=null;}
        }
    }
}
