using System;
using System.Collections.Generic;
using System.Linq;

namespace ET.Server
{
    [EntitySystemOf(typeof(OnlineRoomManagerComponent))]
    [FriendOf(typeof(OnlineRoomManagerComponent))]
    [FriendOf(typeof(OnlineRoomActor))]
    public static partial class OnlineRoomManagerComponentSystem
    {
        [EntitySystem]
        private static void Awake(this OnlineRoomManagerComponent self)
        {
        }

        public static OnlineRoomActor Get(this OnlineRoomManagerComponent self, long roomId)
        {
            return self.Rooms.TryGetValue(roomId, out EntityRef<OnlineRoomActor> actorRef) ? actorRef : null;
        }

        public static void Add(this OnlineRoomManagerComponent self, OnlineRoomActor actor)
        {
            self.Rooms[actor.RoomId] = actor;
        }

        public static void Remove(this OnlineRoomManagerComponent self, long roomId)
        {
            self.Rooms.Remove(roomId);
        }

        // CursorRoomId=0 表示从最新 Room 开始；其余页只读取更小的 RoomId，保证降序翻页稳定。
        // 最多检查并保留 21 个合格项，第 21 项只用于判断 HasMore，不进入返回集合。
        public static List<PublicRoomSummary> GetPublicRoomList(
            this OnlineRoomManagerComponent self,
            long cursorRoomId,
            out long nextCursorRoomId,
            out bool hasMore)
        {
            var accepted = new List<(long RoomId, PublicRoomSummary Summary)>(21);
            EntityRef<OnlineRoomActor>[] actorRefs = self.Rooms.Values.ToArray();
            foreach (OnlineRoomActor room in actorRefs
                         .Select(actorRef => (OnlineRoomActor)actorRef)
                         .Where(room => room != null && !room.IsDisposed)
                         .OrderByDescending(room => room.RoomId))
            {
                if (cursorRoomId != 0 && room.RoomId >= cursorRoomId)
                {
                    continue;
                }

                if (!OnlineRoomSnapshotHelper.TryBuildPublicRoomSummary(room, out PublicRoomSummary summary))
                {
                    continue;
                }

                accepted.Add((room.RoomId, summary));
                if (accepted.Count == 21)
                {
                    break;
                }
            }

            hasMore = accepted.Count > 20;
            int resultCount = Math.Min(20, accepted.Count);
            var rooms = new List<PublicRoomSummary>(resultCount);
            for (int i = 0; i < resultCount; i++)
            {
                rooms.Add(accepted[i].Summary);
            }

            nextCursorRoomId = hasMore && resultCount > 0 ? accepted[resultCount - 1].RoomId : 0;
            return rooms;
        }
    }

    [EntitySystemOf(typeof(OnlineRoomActor))]
    [FriendOf(typeof(OnlineRoomManagerComponent))]
    [FriendOf(typeof(OnlineRoomActor))]
    public static partial class OnlineRoomActorSystem
    {
        [EntitySystem]
        private static void Awake(this OnlineRoomActor self)
        {
        }

        /// <summary>创建房间时一次锁定用途、容量与版本身份；后续成员操作不得改写。</summary>
        public static void Init(this OnlineRoomActor self, long roomId, string roomCode, long playerId, string displayName,
            long sessionEpoch, OnlineRoomPurpose purpose, OnlineRoomVisibility visibility, int capacity,
            string clientVersion, string arcadeConfigFingerprint)
        {
            self.RoomId = roomId;
            self.RoomCode = roomCode;
            self.OwnerPlayerId = playerId;
            self.MembershipRevision = 1;
            self.NextJoinSequence = 2;
            self.Purpose = purpose;
            self.Visibility = visibility;
            self.Capacity = capacity;
            self.ClientVersion = clientVersion ?? string.Empty;
            self.ArcadeConfigFingerprint = arcadeConfigFingerprint ?? string.Empty;
            self.Members[playerId] = new OnlineRoomMember
            {
                PlayerId = playerId,
                DisplayName = displayName ?? string.Empty,
                ConnectionState = OnlineRoomMemberConnectionState.Online,
                SessionEpoch = sessionEpoch,
                JoinSequence = 1,
            };
        }

        public static async ETTask<bool> EnsureChatReady(this OnlineRoomActor self, long playerId)
        {
            if (!self.Members.ContainsKey(playerId))
            {
                throw new RpcException(ErrorCode.ERR_OnlineNotInRoom, "当前不在房间中");
            }

            return await self.SyncChatMembers(true);
        }

        [EntitySystem]
        private static void Destroy(this OnlineRoomActor self)
        {
        }

        public static async ETTask<OnlineRoomSnapshot> Join(this OnlineRoomActor self, long playerId, string displayName,
            long sessionEpoch, int expectedPurpose, string clientVersion, string arcadeConfigFingerprint)
        {
            if (self.Closing)
            {
                throw new RpcException(ErrorCode.ERR_OnlineRoomClosing, "房间正在关闭");
            }

            if (self.Members.ContainsKey(playerId))
            {
                throw new RpcException(ErrorCode.ERR_OnlineAlreadyInRoom, "已在一个房间中");
            }

            if (self.MembershipLocked)
            {
                throw new RpcException(ErrorCode.ERR_OnlineRoomClosing, "房间成员已锁定");
            }

            if (!OnlineRoomSnapshotHelper.IsCompatible(self, expectedPurpose, clientVersion,
                    arcadeConfigFingerprint, out int compatibilityError))
            {
                throw new RpcException(compatibilityError, "房间用途或版本不一致");
            }

            if (self.Members.Count >= self.Capacity)
            {
                throw new RpcException(ErrorCode.ERR_OnlineRoomFull, "房间已满");
            }

            self.Members[playerId] = new OnlineRoomMember
            {
                PlayerId = playerId,
                DisplayName = displayName ?? string.Empty,
                ConnectionState = OnlineRoomMemberConnectionState.Online,
                SessionEpoch = sessionEpoch,
                JoinSequence = self.NextJoinSequence++,
            };
            self.MembershipRevision++;
            OnlineRoomSnapshot snapshot = OnlineRoomSnapshotHelper.BuildSnapshot(self);
            bool chatReady = await self.SyncChatMembers(true);
            await self.Broadcast(snapshot, true, 0);
            if (!chatReady)
            {
                throw new RpcException(ErrorCode.ERR_OnlineChatMembershipPending, "成员同步处理中，可重试");
            }
            return snapshot;
        }

        public static async ETTask Leave(this OnlineRoomActor self, long playerId)
        {
            OnlineRoomManagerComponent manager = self.Parent as OnlineRoomManagerComponent;
            if (manager != null && manager.FailedLeaveCount < Options.Instance.OnlineRoomFailLeaveCount)
            {
                manager.FailedLeaveCount++;
                throw new RpcException(ErrorCode.ERR_OnlineRoomServiceUnavailable, "injected room leave failure");
            }

            if (!self.Members.Remove(playerId))
            {
                throw new RpcException(ErrorCode.ERR_OnlineNotInRoom, "当前不在房间中");
            }

            self.SelectOwnerAfterRemoval();
            self.MembershipRevision++;

            if (self.Members.Count == 0)
            {
                await self.SendToPlayer(playerId, false, null, self.RoomId);
                await self.Close();
                return;
            }

            OnlineRoomSnapshot snapshot = OnlineRoomSnapshotHelper.BuildSnapshot(self);
            self.SyncChatMembersInBackground().Coroutine();
            await self.Broadcast(snapshot, true, 0, playerId);
            await self.SendToPlayer(playerId, false, null, self.RoomId);
        }

        public static async ETTask<OnlineRoomSnapshot> SetConnection(this OnlineRoomActor self, long playerId, long sessionEpoch, int connectionState)
        {
            OnlineRoomManagerComponent manager = self.Parent as OnlineRoomManagerComponent;
            if (manager != null && manager.FailedConnectionSyncCount < Options.Instance.OnlineRoomFailConnectionSyncCount)
            {
                manager.FailedConnectionSyncCount++;
                throw new RpcException(ErrorCode.ERR_OnlineRoomServiceUnavailable, "injected room connection sync failure");
            }

            if (!self.Members.TryGetValue(playerId, out OnlineRoomMember member))
            {
                throw new RpcException(ErrorCode.ERR_OnlineNotInRoom, "当前不在房间中");
            }

            if (sessionEpoch < member.SessionEpoch)
            {
                return OnlineRoomSnapshotHelper.BuildSnapshot(self);
            }

            OnlineRoomMemberConnectionState newState = connectionState == (int)OnlineRoomMemberConnectionState.DisconnectedGrace
                ? OnlineRoomMemberConnectionState.DisconnectedGrace
                : OnlineRoomMemberConnectionState.Online;
            if (member.ConnectionState == newState && member.SessionEpoch == sessionEpoch)
            {
                return OnlineRoomSnapshotHelper.BuildSnapshot(self);
            }

            member.ConnectionState = newState;
            member.SessionEpoch = sessionEpoch;
            self.MembershipRevision++;
            if (self.Parent is OnlineRoomManagerComponent roomManager && roomManager.Parent is Scene roomScene)
            {
                R2R_OnlineArcadeConnectionChanged changed = R2R_OnlineArcadeConnectionChanged.Create();
                changed.RoomId = self.RoomId;
                changed.PlayerId = playerId;
                changed.Connected = newState == OnlineRoomMemberConnectionState.Online;
                self.Root().GetComponent<MessageSender>().Send(roomScene.GetActorId(), changed);
            }
            OnlineRoomSnapshot snapshot = OnlineRoomSnapshotHelper.BuildSnapshot(self);
            self.SyncChatMembersInBackground().Coroutine();
            await self.Broadcast(snapshot, true, 0);
            return snapshot;
        }

        public static OnlineRoomSnapshot GetSnapshot(this OnlineRoomActor self, long playerId)
        {
            OnlineRoomManagerComponent manager = self.Parent as OnlineRoomManagerComponent;
            if (manager != null && manager.FailedGetCurrentCount < Options.Instance.OnlineRoomFailGetCurrentCount)
            {
                manager.FailedGetCurrentCount++;
                throw new RpcException(ErrorCode.ERR_OnlineRoomServiceUnavailable, "injected room get-current failure");
            }

            if (!self.Members.ContainsKey(playerId))
            {
                throw new RpcException(ErrorCode.ERR_OnlineNotInRoom, "当前不在房间中");
            }

            return OnlineRoomSnapshotHelper.BuildSnapshot(self);
        }

        /// <summary>安全节点已落盘后锁定原始成员；重复调用不增加 Revision。</summary>
        public static async ETTask LockForArcadeRun(this OnlineRoomActor self)
        {
            if (self.MembershipLocked) return;
            self.MembershipLocked = true;
            self.MembershipRevision++;
            await self.Broadcast(OnlineRoomSnapshotHelper.BuildSnapshot(self), true, 0);
        }

        /// <summary>联机街机局已终止后解除成员锁；重复调用不增加房间版本。</summary>
        public static async ETTask UnlockAfterArcadeRun(this OnlineRoomActor self)
        {
            if (!self.MembershipLocked) return;
            self.MembershipLocked = false;
            self.MembershipRevision++;
            await self.Broadcast(OnlineRoomSnapshotHelper.BuildSnapshot(self), true, 0);
        }

        private static async ETTask Close(this OnlineRoomActor self)
        {
            self.Closing = true;
            try
            {
                OnlineRoomManagerComponent manager = self.Parent as OnlineRoomManagerComponent;

                ActorId chat = ServerConfig.Instance.GetSceneByName(self.Zone(), "Chat").ActorId;
                R2C_CloseRoomChat closeChat = R2C_CloseRoomChat.Create();
                closeChat.RoomId = self.RoomId;
                closeChat.MembershipRevision = self.MembershipRevision;
                using C2R_CloseRoomChat chatResponse = await self.Root().GetComponent<MessageSender>().Call(chat, closeChat, false) as C2R_CloseRoomChat;
                if (chatResponse.Error != ErrorCode.ERR_Success)
                {
                    throw new RpcException(chatResponse.Error, chatResponse.Message);
                }

                if (manager != null && manager.FailedCloseCount < Options.Instance.OnlineRoomFailCloseCount)
                {
                    manager.FailedCloseCount++;
                    throw new RpcException(ErrorCode.ERR_OnlineRoomServiceUnavailable, "injected room close failure");
                }

                ActorId directory = ServerConfig.Instance.GetSceneByName(self.Zone(), "RoomDirectory").ActorId;
                R2D_CloseRoom close = R2D_CloseRoom.Create();
                close.RoomId = self.RoomId;
                close.RoomCode = self.RoomCode;
                using D2R_CloseRoom response = await self.Root().GetComponent<MessageSender>().Call(directory, close, false) as D2R_CloseRoom;
                if (response.Error != ErrorCode.ERR_Success)
                {
                    throw new RpcException(response.Error, response.Message);
                }

                await self.Root().GetComponent<LocationProxyComponent>().Remove(LocationType.OnlineRoom, self.RoomId);
                manager?.Remove(self.RoomId);
                self.Dispose();
            }
            catch (Exception e)
            {
                Log.Error($"online room close failed stage=Close roomId={self.RoomId} exception={e}");
                if (!self.CloseRetryPending)
                {
                    self.CloseRetryPending = true;
                    RetryCloseLater(self).Coroutine();
                }
            }
        }

        public static async ETTask RetryClose(this OnlineRoomActor self)
        {
            self.CloseRetryPending = false;
            if (self.Closing)
            {
                await self.Close();
            }
        }

        public static async ETTask RetryChatSync(this OnlineRoomActor self, long syncGeneration)
        {
            if (syncGeneration != self.ChatSyncGeneration)
            {
                return;
            }

            self.ChatSyncRetryPending = false;
            await self.SyncChatMembers(true);
        }

        public static async ETTask<bool> SyncChatMembers(this OnlineRoomActor self, bool scheduleRetry)
        {
            OnlineRoomSnapshot snapshot = OnlineRoomSnapshotHelper.BuildSnapshot(self);
            try
            {
                ActorId chat = ServerConfig.Instance.GetSceneByName(self.Zone(), "Chat").ActorId;
                R2C_SyncRoomChatMembers request = R2C_SyncRoomChatMembers.Create();
                request.RoomId = self.RoomId;
                request.MembershipRevision = snapshot.MembershipRevision;
                foreach (RoomMemberInfo roomMember in snapshot.Members)
                {
                    RoomChatMemberInfo member = RoomChatMemberInfo.Create();
                    member.PlayerId = roomMember.PlayerId;
                    member.DisplayName = roomMember.DisplayName;
                    member.ConnectionState = roomMember.ConnectionState;
                    request.Members.Add(member);
                }

                using C2R_SyncRoomChatMembers response = await self.Root().GetComponent<MessageSender>().Call(chat, request, false) as C2R_SyncRoomChatMembers;
                if (response.Error == ErrorCode.ERR_Success && response.ConfirmedRevision >= snapshot.MembershipRevision)
                {
                    self.ChatConfirmedRevision = Math.Max(self.ChatConfirmedRevision, response.ConfirmedRevision);
                    if (self.ChatConfirmedRevision >= self.MembershipRevision)
                    {
                        self.ChatSyncRetryPending = false;
                    }
                    return true;
                }

                Log.Warning($"online room chat sync pending stage=SyncMembers roomId={self.RoomId} revision={snapshot.MembershipRevision} error={response.Error}");
            }
            catch (Exception e)
            {
                Log.Warning($"online room chat sync pending stage=SyncMembers roomId={self.RoomId} revision={snapshot.MembershipRevision} error={e.GetType().Name}");
            }

            if (scheduleRetry)
            {
                self.QueueChatSyncRetry();
            }
            return false;
        }

        private static async ETTask SyncChatMembersInBackground(this OnlineRoomActor self)
        {
            await self.SyncChatMembers(true);
        }

        private static void QueueChatSyncRetry(this OnlineRoomActor self)
        {
            if (self.ChatSyncRetryPending || self.Closing)
            {
                return;
            }

            self.ChatSyncRetryPending = true;
            long generation = ++self.ChatSyncGeneration;
            RetryChatSyncLater(self, generation).Coroutine();
        }

        private static async ETTask RetryChatSyncLater(OnlineRoomActor self, long generation)
        {
            await self.Root().GetComponent<TimerComponent>().WaitAsync(2000);
            if (self.IsDisposed)
            {
                return;
            }

            R2R_RetryChatSync retry = R2R_RetryChatSync.Create();
            retry.RoomId = self.RoomId;
            retry.SyncGeneration = generation;
            self.Root().GetComponent<MessageSender>().Send(self.GetActorId(), retry);
        }

        private static async ETTask RetryCloseLater(OnlineRoomActor self)
        {
            await self.Root().GetComponent<TimerComponent>().WaitAsync(5000);
            if (self.IsDisposed)
            {
                return;
            }

            R2R_RetryClose retry = R2R_RetryClose.Create();
            retry.RoomId = self.RoomId;
            self.Root().GetComponent<MessageSender>().Send(self.GetActorId(), retry);
        }

        private static void SelectOwnerAfterRemoval(this OnlineRoomActor self)
        {
            if (self.Members.ContainsKey(self.OwnerPlayerId))
            {
                return;
            }

            OnlineRoomMember next = self.Members.Values
                .Where(m => m.ConnectionState == OnlineRoomMemberConnectionState.Online)
                .OrderBy(m => m.JoinSequence)
                .FirstOrDefault() ??
                self.Members.Values.OrderBy(m => m.JoinSequence).FirstOrDefault();
            self.OwnerPlayerId = next?.PlayerId ?? 0;
        }

        private static async ETTask Broadcast(this OnlineRoomActor self, OnlineRoomSnapshot snapshot, bool hasRoom, long clearedRoomId, long exceptPlayerId = 0)
        {
            foreach (long playerId in self.Members.Keys.ToArray())
            {
                if (playerId == exceptPlayerId)
                {
                    continue;
                }

                await self.SendToPlayer(playerId, hasRoom, snapshot, clearedRoomId);
            }
        }

        private static async ETTask SendToPlayer(this OnlineRoomActor self, long playerId, bool hasRoom, OnlineRoomSnapshot snapshot, long roomId)
        {
            try
            {
                ActorId playerActorId = await self.Root().GetComponent<LocationProxyComponent>().Get(LocationType.OnlinePlayer, playerId);
                R2P_RoomChanged changed = R2P_RoomChanged.Create();
                changed.PlayerId = playerId;
                changed.HasRoom = hasRoom;
                changed.Room = OnlineRoomSnapshotHelper.CloneSnapshot(snapshot);
                changed.RoomId = roomId == 0 ? self.RoomId : roomId;
                self.Root().GetComponent<MessageSender>().Send(playerActorId, changed);
            }
            catch (Exception e)
            {
                Log.Error($"online room push skipped stage=SendRoomChanged playerId={playerId} roomId={self.RoomId} exception={e}");
            }
        }
    }
}
