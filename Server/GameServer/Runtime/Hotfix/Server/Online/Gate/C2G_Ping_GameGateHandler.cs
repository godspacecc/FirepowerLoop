namespace ET.Server
{
    [MessageSessionHandler(SceneType.GameGate)]
    public class C2G_Ping_GameGateHandler: MessageSessionHandler<C2G_Ping, G2C_Ping>
    {
        protected override async ETTask Run(Session session, C2G_Ping request, G2C_Ping response)
        {
            response.Time = TimeInfo.Instance.ServerNow();
            await ETTask.CompletedTask;
        }
    }
}
