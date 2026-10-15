namespace ET.Server
{
    [MessageHandler(SceneType.Player)]
    public class G2P_PlayerGraceTimeoutHandler: MessageHandler<OnlinePlayerActor, G2P_PlayerGraceTimeout>
    {
        protected override async ETTask Run(OnlinePlayerActor actor, G2P_PlayerGraceTimeout message)
        {
            await actor.HandleGraceTimeout(message.SessionEpoch, message.TimerGeneration);
        }
    }
}
