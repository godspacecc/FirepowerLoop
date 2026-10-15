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
    }
}
