namespace ET.Server
{
    [MessageHandler(SceneType.Player)]
    [FriendOf(typeof(OnlinePlayerActor))]
    public class G2P_GetOrCreatePlayerHandler: MessageHandler<Scene, G2P_GetOrCreatePlayer, P2G_GetOrCreatePlayer>
    {
        protected override async ETTask Run(Scene scene, G2P_GetOrCreatePlayer request, P2G_GetOrCreatePlayer response)
        {
            if (request.SessionEpoch <= 0)
            {
                response.Error = ErrorCode.ERR_OnlineSessionStateConflict;
                return;
            }
            OnlinePlayerManagerComponent manager = scene.GetComponent<OnlinePlayerManagerComponent>();
            OnlinePlayerActor actor = manager.Get(request.PlayerId);
            if (actor == null)
            {
                actor = manager.AddChildWithId<OnlinePlayerActor, long, long, string>(
                    request.PlayerId,
                    request.AccountId,
                    request.PlayerId,
                    request.DisplayName);
                actor.AddComponent<MailBoxComponent, MailBoxType>(MailBoxType.OrderedMessage);
                actor.Revision = request.ProfileRevision;
                manager.Add(actor);
            }

            // 数据库约束之外再验证Actor归属，损坏或错误映射不能跨账号复用玩家。
            if (actor.AccountId != request.AccountId)
            {
                response.Error = ErrorCode.ERR_OnlineSessionStateConflict;
                return;
            }

            var result = await actor.BindGate(request.GateProxyActorId, request.SessionEpoch);
            response.Error = result.Error;
            response.Message = result.Message;
            response.PlayerActorId = result.PlayerActorId;
            response.SessionEpoch = result.SessionEpoch;
            response.IsPlayerActorReused = result.Reused;
        }
    }
}
