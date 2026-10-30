namespace ET.Server
{
    [MessageSessionHandler(SceneType.GameGate)]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public class C2G_Ping_GameGateHandler: MessageSessionHandler<C2G_Ping, G2C_Ping>
    {
        protected override async ETTask Run(Session session, C2G_Ping request, G2C_Ping response)
        {
            response.Time = TimeInfo.Instance.ServerNow();
            if (!OnlineAccountAdmission.Allow(session)) { response.Error=200201; return; }
            GatePlayerProxy proxy=session.GetComponent<OnlineSessionComponent>()?.Proxy;
            if(proxy==null) return; // 未登录探测只检查可达性，不建立身份。
            long instanceId=session.InstanceId, epoch=proxy.SessionEpoch;
            var validation=G2A_ValidateAccountSession.Create(); validation.AccountId=proxy.AccountId; validation.SessionEpoch=epoch;
            using var result=await session.Root().GetComponent<MessageSender>().Call(session.Root().GetOnlineSceneActorId("Account"),validation,false) as A2G_ValidateAccountSession;
            if(session.IsDisposed||session.InstanceId!=instanceId||proxy.IsDisposed||proxy.SessionEpoch!=epoch) return;
            response.Error=result.Error;
            if(result.Error!=0)
            {
                proxy.MarkDisconnected();
                proxy.ReplaceAndClose(result.Error,"账号会话已失效，请重新登录");
            }
        }
    }
}
