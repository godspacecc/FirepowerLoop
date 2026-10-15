namespace ET.Server
{
    [EntitySystemOf(typeof(OnlinePlayerManagerComponent))]
    [FriendOf(typeof(OnlinePlayerManagerComponent))]
    [FriendOf(typeof(OnlinePlayerActor))]
    public static partial class OnlinePlayerManagerComponentSystem
    {
        [EntitySystem]
        private static void Awake(this OnlinePlayerManagerComponent self)
        {
        }

        public static OnlinePlayerActor Get(this OnlinePlayerManagerComponent self, long playerId)
        {
            return self.Players.TryGetValue(playerId, out EntityRef<OnlinePlayerActor> actorRef) ? actorRef : null;
        }

        public static void Add(this OnlinePlayerManagerComponent self, OnlinePlayerActor actor)
        {
            self.Players[actor.PlayerId] = actor;
        }

        public static void Remove(this OnlinePlayerManagerComponent self, long playerId)
        {
            self.Players.Remove(playerId);
        }
    }
}
