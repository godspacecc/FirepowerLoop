namespace ET.Server
{
    [MessageHandler(SceneType.Player)]
    public class G2P_PlayerRetrySaveHandler: MessageHandler<OnlinePlayerActor, G2P_PlayerRetrySave>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, G2P_PlayerRetrySave message)
        {
            await actor.RetrySave(message.SessionEpoch);
        }
    }
}
