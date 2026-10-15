namespace ET.Server
{
    public static class OnlineGateHelper
    {
        public static ActorId GetOnlineSceneActorId(this Scene root, string sceneName)
        {
            return ServerConfig.Instance.GetSceneByName(root.Zone(), sceneName).ActorId;
        }
    }
}

