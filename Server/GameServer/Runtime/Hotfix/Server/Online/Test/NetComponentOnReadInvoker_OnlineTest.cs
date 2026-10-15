namespace ET.Server
{
    using System;

    [Invoke((long)SceneType.OnlineTest)]
    [FriendOf(typeof(OnlineTestComponent))]
    public class NetComponentOnReadInvoker_OnlineTest: AInvokeHandler<NetComponentOnRead>
    {
        public override void Handle(NetComponentOnRead args)
        {
            Session session = args.Session;
            switch (args.Message)
            {
                case IResponse response:
                    session.OnResponse(response);
                    break;
                case G2C_SessionReplaced replaced:
                    OnlineTestComponent component = session.Root().GetComponent<OnlineTestComponent>();
                    component.SessionReplacedSessionIds.Add(session.Id);
                    component.SessionReplacedErrors.Add(replaced.Error);
                    component.SessionReplacedReasons.Add(replaced.Reason ?? string.Empty);
                    Console.WriteLine($"SESSION_REPLACED_PUSH session={session.Id} error={replaced.Error} reason={replaced.Reason}");
                    break;
                case G2C_RoomChanged changed:
                    OnlineTestComponent roomComponent = session.Root().GetComponent<OnlineTestComponent>();
                    roomComponent.RoomChangedSessionIds.Add(session.Id);
                    roomComponent.RoomChangedHasRooms.Add(changed.HasRoom);
                    roomComponent.RoomChangedRooms.Add(OnlineRoomSnapshotHelper.CloneSnapshot(changed.Room));
                    Console.WriteLine($"ROOM_CHANGED_PUSH session={session.Id} hasRoom={changed.HasRoom} revision={changed.Room?.MembershipRevision ?? 0}");
                    break;
                case G2C_RoomChatMessage chat:
                    OnlineTestComponent chatComponent = session.Root().GetComponent<OnlineTestComponent>();
                    chatComponent.RoomChatSessionIds.Add(session.Id);
                    chatComponent.RoomChatRoomIds.Add(chat.RoomId);
                    chatComponent.RoomChatMessages.Add(OnlineChatComponentSystem.CloneProtocol(chat.ChatMessage));
                    Console.WriteLine($"ROOM_CHAT_PUSH session={session.Id} roomId={chat.RoomId} sequence={chat.ChatMessage?.ChatSequence ?? 0}");
                    break;
                case G2C_WorldPlayerEntered entered:
                    OnlineTestComponent enteredComponent = session.Root().GetComponent<OnlineTestComponent>();
                    enteredComponent.WorldEnteredSessionIds.Add(session.Id);
                    enteredComponent.WorldEnteredRevisions.Add(entered.WorldRevision);
                    enteredComponent.WorldEnteredPlayers.Add(CloneWorldPlayer(entered.Player));
                    break;
                case G2C_WorldPlayerTransformChanged transform:
                    OnlineTestComponent transformComponent = session.Root().GetComponent<OnlineTestComponent>();
                    transformComponent.WorldTransformSessionIds.Add(session.Id);
                    transformComponent.WorldTransformRevisions.Add(transform.WorldRevision);
                    transformComponent.WorldTransformPlayers.Add(CloneWorldPlayer(transform.Player));
                    break;
                case G2C_WorldPlayerLeft left:
                    OnlineTestComponent leftComponent = session.Root().GetComponent<OnlineTestComponent>();
                    leftComponent.WorldLeftSessionIds.Add(session.Id);
                    leftComponent.WorldLeftRevisions.Add(left.WorldRevision);
                    leftComponent.WorldLeftUnitIds.Add(left.UnitId);
                    break;
            }
        }

        private static WorldPlayerInfo CloneWorldPlayer(WorldPlayerInfo source)
        {
            if (source == null) return null;
            WorldPlayerInfo result = WorldPlayerInfo.Create();
            result.PlayerId = source.PlayerId; result.UnitId = source.UnitId; result.DisplayName = source.DisplayName;
            result.WorldId = source.WorldId; result.MapId = source.MapId; result.PlayerConfigId = source.PlayerConfigId;
            result.PositionX = source.PositionX; result.PositionY = source.PositionY; result.PositionZ = source.PositionZ;
            result.YawDegrees = source.YawDegrees; result.ConnectionState = source.ConnectionState; result.StateRevision = source.StateRevision;
            return result;
        }
    }
}
