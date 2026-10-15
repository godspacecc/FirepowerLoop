namespace ET.Server
{
    /// <summary>仅向当前在线会话转发本角色所属地图的权威弹体消息，不参与命中裁决。</summary>
    [MessageHandler(SceneType.Player)]
    [FriendOf(typeof(OnlinePlayerActor))]
    public sealed class W2P_WorldProjectilesChangedHandler : MessageHandler<OnlinePlayerActor,W2P_WorldProjectilesChanged>
    {
        protected override async ETTask Run(OnlinePlayerActor actor,W2P_WorldProjectilesChanged message)
        {
            if(message.PlayerId!=actor.PlayerId||message.WorldId!=actor.CurrentWorldId||message.MapId!=actor.CurrentWorldMapId||actor.GateProxyActorId==default||actor.State!=OnlinePlayerState.Online)return;
            var p=P2G_WorldProjectilesChanged.Create();p.PlayerId=actor.PlayerId;p.SessionEpoch=actor.SessionEpoch;p.WorldId=message.WorldId;p.MapId=message.MapId;p.WorldRevision=message.WorldRevision;p.ServerTime=message.ServerTime;
            foreach(var x in message.Projectiles)p.Projectiles.Add(OnlineWorldSnapshotHelper.Clone(x));foreach(var x in message.Terminations)p.Terminations.Add(OnlineWorldSnapshotHelper.Clone(x));
            actor.Root().GetComponent<MessageSender>().Send(actor.GateProxyActorId,p);await ETTask.CompletedTask;
        }
    }
}
