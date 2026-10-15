namespace ET.Server
{
    [MessageHandler(SceneType.GameGate)] [FriendOf(typeof(GatePlayerProxy))]
    public sealed class P2G_WorldProjectilesChangedHandler : MessageHandler<GatePlayerProxy,P2G_WorldProjectilesChanged>
    {
        protected override async ETTask Run(GatePlayerProxy actor,P2G_WorldProjectilesChanged message)
        {
            Session session=actor.Session;if(!actor.IsAuthenticated||actor.PlayerId!=message.PlayerId||actor.SessionEpoch!=message.SessionEpoch||session==null||session.IsDisposed)return;
            var p=G2C_WorldProjectilesChanged.Create();p.WorldId=message.WorldId;p.MapId=message.MapId;p.WorldRevision=message.WorldRevision;p.ServerTime=message.ServerTime;
            foreach(var x in message.Projectiles)p.Projectiles.Add(OnlineWorldSnapshotHelper.Clone(x));foreach(var x in message.Terminations)p.Terminations.Add(OnlineWorldSnapshotHelper.Clone(x));session.Send(p);await ETTask.CompletedTask;
        }
    }
}
