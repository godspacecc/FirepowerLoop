namespace ET.Server
{
    using System.Collections.Generic;

    [ComponentOf(typeof(Scene))]
    public class OnlineTestComponent: Entity, IAwake
    {
        public readonly List<string> SessionReplacedReasons = new();
        public readonly List<long> SessionReplacedSessionIds = new();
        public readonly List<int> SessionReplacedErrors = new();
        public readonly List<long> RoomChangedSessionIds = new();
        public readonly List<bool> RoomChangedHasRooms = new();
        public readonly List<OnlineRoomSnapshot> RoomChangedRooms = new();
        public readonly List<long> RoomChatSessionIds = new();
        public readonly List<long> RoomChatRoomIds = new();
        public readonly List<RoomChatMessageInfo> RoomChatMessages = new();
        public readonly List<long> WorldEnteredSessionIds = new();
        public readonly List<long> WorldEnteredRevisions = new();
        public readonly List<WorldPlayerInfo> WorldEnteredPlayers = new();
        public readonly List<long> WorldTransformSessionIds = new();
        public readonly List<long> WorldTransformRevisions = new();
        public readonly List<WorldPlayerInfo> WorldTransformPlayers = new();
        public readonly List<long> WorldLeftSessionIds = new();
        public readonly List<long> WorldLeftRevisions = new();
        public readonly List<long> WorldLeftUnitIds = new();
    }
}
