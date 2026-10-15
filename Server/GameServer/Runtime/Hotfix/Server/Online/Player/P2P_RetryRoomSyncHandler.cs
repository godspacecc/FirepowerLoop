namespace ET.Server
{
    [MessageHandler(SceneType.Player)]
    public class P2P_RetryRoomSyncHandler: MessageHandler<OnlinePlayerActor, P2P_RetryRoomSync>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, P2P_RetryRoomSync message)
        {
            await actor.RetryRoomSync(message.SessionEpoch, message.SyncGeneration);
        }
    }
}
