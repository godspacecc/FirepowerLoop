namespace ET.Server
{
    [MessageHandler(SceneType.GameGate)]
    public class P2G_SessionReplacedHandler: MessageHandler<GatePlayerProxy, P2G_SessionReplaced>
    {
        protected override async ETTask Run(GatePlayerProxy proxy, P2G_SessionReplaced message)
        {
            proxy.ReplaceAndClose(message.Error, message.Reason);
            await ETTask.CompletedTask;
        }
    }
}
