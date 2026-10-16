using System;
using System.Collections.Generic;
using System.Linq;

namespace ET.Server
{
    [EntitySystemOf(typeof(OnlinePlayerActor))]
    [FriendOf(typeof(OnlinePlayerActor))]
    public static partial class OnlinePlayerActorSystem
    {
        [EntitySystem]
        private static void Awake(this OnlinePlayerActor self, long accountId, long playerId, string displayName)
        {
            self.AccountId = accountId;
            self.PlayerId = playerId;
            self.DisplayName = displayName;
            self.Revision = 1;
            self.State = OnlinePlayerState.Online;
            self.SessionEpoch = 0;
        }

        [EntitySystem]
        private static void Destroy(this OnlinePlayerActor self)
        {
        }

        public static async ETTask<(int Error, string Message, ActorId PlayerActorId, long SessionEpoch, bool Reused)> BindGate(
                this OnlinePlayerActor self,
                ActorId gateProxyActorId)
        {
            // Offlining owns an unfinished save/commit transaction; rebinding would expose an old Unit or create two authorities for one player.
            if (self.State == OnlinePlayerState.Offlining)
                return (ErrorCode.ERR_OnlineServiceUnavailable, "角色正在完成离线事务，请稍后重试", default, self.SessionEpoch, true);
            bool reused = self.SessionEpoch > 0;
            self.TimerGeneration++;
            self.SaveRetryPending = false;

            ActorId oldProxy = self.GateProxyActorId;
            if (oldProxy != default && oldProxy != gateProxyActorId)
            {
                P2G_SessionReplaced replaced = P2G_SessionReplaced.Create();
                replaced.Error = ErrorCode.ERR_OnlineAccountLoggedInElsewhere;
                replaced.Reason = "账号已在别处登录";
                self.Root().GetComponent<MessageSender>().Send(oldProxy, replaced);
            }

            self.SessionEpoch++;
            self.GateProxyActorId = gateProxyActorId;
            self.State = OnlinePlayerState.Online;
            await self.AddOrRefreshLocation();
            self.RequestRoomConnectionSync((int)OnlineRoomMemberConnectionState.Online);
            return (ErrorCode.ERR_Success, string.Empty, self.GetActorId(), self.SessionEpoch, reused);
        }

        public static async ETTask HandleDisconnected(this OnlinePlayerActor self, long epoch)
        {
            if (epoch != self.SessionEpoch || self.State != OnlinePlayerState.Online)
            {
                return;
            }

            self.State = OnlinePlayerState.DisconnectedGrace;
            self.GateProxyActorId = default;
            long timerGeneration = ++self.TimerGeneration;
            WaitGraceTimeout(self, epoch, timerGeneration).Coroutine();
            self.RequestRoomConnectionSync((int)OnlineRoomMemberConnectionState.DisconnectedGrace);
            await ETTask.CompletedTask;
        }

        public static async ETTask HandleLogout(this OnlinePlayerActor self, long epoch)
        {
            if (epoch != self.SessionEpoch || self.State == OnlinePlayerState.Offlining)
            {
                return;
            }

            self.GateProxyActorId = default;
            await self.OfflineRoomAndDispose(self.MakeLifecycleOperationId("logout"));
        }

        public static async ETTask HandleGraceTimeout(this OnlinePlayerActor self, long epoch, long timerGeneration)
        {
            if (epoch != self.SessionEpoch || timerGeneration != self.TimerGeneration || self.State != OnlinePlayerState.DisconnectedGrace)
            {
                return;
            }

            await self.OfflineRoomAndDispose(self.MakeLifecycleOperationId("timeout"));
        }

        /// <summary>重试房间离线收口，不持久化已移除的开放世界状态。</summary>
        public static async ETTask RetrySave(this OnlinePlayerActor self, long epoch)
        {
            if (epoch != self.SessionEpoch) return;
            self.SaveRetryPending = false;
            if (self.State != OnlinePlayerState.Offlining) return;
            await self.OfflineRoomAndDispose(self.PendingOfflineRoomLeaveOperationId ?? self.MakeLifecycleOperationId("retry"));
        }

        public static async ETTask RetryRoomSync(this OnlinePlayerActor self, long epoch, long syncGeneration)
        {
            if (epoch != self.PendingRoomSyncEpoch || syncGeneration != self.RoomSyncGeneration || !self.PendingRoomSync)
            {
                return;
            }

            self.RoomSyncRetryQueued = false;
            await self.TrySyncRoomConnection(syncGeneration);
        }

        public static async ETTask<(int Error, string Message, bool HasRoom, OnlineRoomSnapshot Room)> CreateRoom(
            this OnlinePlayerActor self, long epoch, string operationId, int purpose, int visibility,
            string clientVersion, string arcadeConfigFingerprint)
        {
            if (epoch != self.SessionEpoch || self.State != OnlinePlayerState.Online)
            {
                return (ErrorCode.ERR_OnlineSessionNotLoggedIn, "未登录", false, null);
            }

            if (!OnlineRoomSnapshotHelper.IsValidOperationId(operationId))
            {
                return (ErrorCode.ERR_OnlineRoomOperationInvalid, "操作标识无效或冲突", false, null);
            }

            if (!string.IsNullOrEmpty(self.PendingRoomReadyOperationId))
            {
                return await self.ContinuePendingRoomReady("Create", operationId);
            }

            if (self.TryGetRoomOperation("Create", operationId, out OnlineRoomOperationRecord record))
            {
                return (record.Error, record.Message, record.HasRoom, OnlineRoomSnapshotHelper.CloneSnapshot(record.Room));
            }

            if (self.CurrentRoomId != 0)
            {
                return self.RecordRoomOperation("Create", operationId, ErrorCode.ERR_OnlineAlreadyInRoom, "已在一个房间中", true, await self.GetCurrentRoomSnapshotOrNull());
            }

            ActorId directory = ServerConfig.Instance.GetSceneByName(self.Zone(), "RoomDirectory").ActorId;
            P2D_CreateRoom request = P2D_CreateRoom.Create();
            request.PlayerId = self.PlayerId;
            request.DisplayName = self.DisplayName;
            request.SessionEpoch = self.SessionEpoch;
            request.Purpose = purpose;
            request.Visibility = visibility;
            request.ClientVersion = clientVersion ?? string.Empty;
            request.ArcadeConfigFingerprint = arcadeConfigFingerprint ?? string.Empty;
            using D2P_CreateRoom response = await self.Root().GetComponent<MessageSender>().Call(directory, request, false) as D2P_CreateRoom;
            if (response.Error == ErrorCode.ERR_Success || response.Error == ErrorCode.ERR_OnlineChatMembershipPending)
            {
                self.CurrentRoomId = response.RoomId;
            }

            if (response.Error == ErrorCode.ERR_OnlineChatMembershipPending)
            {
                self.SetPendingRoomReady("Create", operationId, response.RoomId);
                return (response.Error, response.Message, true, OnlineRoomSnapshotHelper.CloneSnapshot(response.Room));
            }

            return self.RecordRoomOperation("Create", operationId, response.Error, response.Message, response.Error == ErrorCode.ERR_Success, response.Room);
        }

        /// <summary>
        /// 快速匹配由玩家 Actor 保证会话与操作幂等，目录 Actor 原子领取等待房；加入瞬时失效时再尝试一次。
        /// 匹配只接受当前服务器配置，且不会把街机房暴露到普通公开房间列表。
        /// </summary>
        public static async ETTask<(int Error, string Message, bool HasRoom, OnlineRoomSnapshot Room)>
            MatchArcadeRoom(this OnlinePlayerActor self, long epoch, string operationId,
                string clientVersion, string arcadeConfigFingerprint)
        {
            const string operationType = "MatchArcade";
            if (epoch != self.SessionEpoch || self.State != OnlinePlayerState.Online)
            {
                return (ErrorCode.ERR_OnlineSessionNotLoggedIn, "未登录", false, null);
            }

            if (!OnlineRoomSnapshotHelper.IsValidOperationId(operationId))
            {
                return (ErrorCode.ERR_OnlineRoomOperationInvalid, "操作标识无效或冲突", false, null);
            }

            if (!string.IsNullOrEmpty(self.PendingRoomReadyOperationId))
            {
                return await self.ContinuePendingRoomReady(operationType, operationId);
            }

            if (self.TryGetRoomOperation(operationType, operationId, out OnlineRoomOperationRecord record))
            {
                return (record.Error, record.Message, record.HasRoom,
                    OnlineRoomSnapshotHelper.CloneSnapshot(record.Room));
            }

            if (self.CurrentRoomId != 0)
            {
                return self.RecordRoomOperation(operationType, operationId,
                    ErrorCode.ERR_OnlineAlreadyInRoom, "已在一个房间中", true,
                    await self.GetCurrentRoomSnapshotOrNull());
            }

            ActorId directory = ServerConfig.Instance.GetSceneByName(self.Zone(), "RoomDirectory").ActorId;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                P2D_MatchArcadeRoom match = P2D_MatchArcadeRoom.Create();
                match.PlayerId = self.PlayerId;
                match.DisplayName = self.DisplayName;
                match.SessionEpoch = self.SessionEpoch;
                match.ClientVersion = clientVersion ?? string.Empty;
                match.ArcadeConfigFingerprint = arcadeConfigFingerprint ?? string.Empty;
                using D2P_MatchArcadeRoom matched = await self.Root().GetComponent<MessageSender>()
                    .Call(directory, match, false) as D2P_MatchArcadeRoom;
                if (matched.Error != ErrorCode.ERR_Success &&
                    matched.Error != ErrorCode.ERR_OnlineChatMembershipPending)
                {
                    return self.RecordRoomOperation(operationType, operationId, matched.Error,
                        matched.Message, false, null);
                }

                if (matched.Created)
                {
                    self.CurrentRoomId = matched.RoomId;
                    if (matched.Error == ErrorCode.ERR_OnlineChatMembershipPending)
                    {
                        self.SetPendingRoomReady(operationType, operationId, matched.RoomId);
                        return (matched.Error, matched.Message, true,
                            OnlineRoomSnapshotHelper.CloneSnapshot(matched.Room));
                    }

                    return self.RecordRoomOperation(operationType, operationId, matched.Error,
                        matched.Message, true, matched.Room);
                }

                try
                {
                    ActorId roomActor = await self.Root().GetComponent<LocationProxyComponent>()
                        .Get(LocationType.OnlineRoom, matched.RoomId);
                    P2R_JoinRoom join = P2R_JoinRoom.Create();
                    join.PlayerId = self.PlayerId;
                    join.DisplayName = self.DisplayName;
                    join.SessionEpoch = self.SessionEpoch;
                    join.ExpectedPurpose = (int)OnlineRoomPurpose.Arcade;
                    join.ClientVersion = clientVersion ?? string.Empty;
                    join.ArcadeConfigFingerprint = arcadeConfigFingerprint ?? string.Empty;
                    using R2P_JoinRoom joined = await self.Root().GetComponent<MessageSender>()
                        .Call(roomActor, join, false) as R2P_JoinRoom;
                    if (joined.Error == ErrorCode.ERR_Success ||
                        joined.Error == ErrorCode.ERR_OnlineChatMembershipPending)
                    {
                        self.CurrentRoomId = matched.RoomId;
                        if (joined.Error == ErrorCode.ERR_OnlineChatMembershipPending)
                        {
                            self.SetPendingRoomReady(operationType, operationId, matched.RoomId);
                            return (joined.Error, joined.Message, true,
                                OnlineRoomSnapshotHelper.CloneSnapshot(joined.Room));
                        }

                        return self.RecordRoomOperation(operationType, operationId, joined.Error,
                            joined.Message, true, joined.Room);
                    }

                    if (attempt == 1)
                    {
                        return self.RecordRoomOperation(operationType, operationId, joined.Error,
                            joined.Message, false, joined.Room);
                    }
                }
                catch (RpcException exception) when (attempt == 0)
                {
                    Log.Warning($"online arcade match candidate expired playerId={self.PlayerId} roomId={matched.RoomId} error={exception.Error}");
                }
            }

            return self.RecordRoomOperation(operationType, operationId,
                ErrorCode.ERR_OnlineRoomServiceUnavailable, "匹配服务暂不可用", false, null);
        }

        public static async ETTask<(int Error, string Message, bool HasRoom, OnlineRoomSnapshot Room)> JoinRoom(
            this OnlinePlayerActor self,
            long epoch,
            string operationId,
            string roomCode,
            string sourceAddress,
            int expectedPurpose,
            string clientVersion,
            string arcadeConfigFingerprint)
        {
            if (epoch != self.SessionEpoch || self.State != OnlinePlayerState.Online)
            {
                return (ErrorCode.ERR_OnlineSessionNotLoggedIn, "未登录", false, null);
            }

            if (!OnlineRoomSnapshotHelper.IsValidOperationId(operationId))
            {
                return (ErrorCode.ERR_OnlineRoomOperationInvalid, "操作标识无效或冲突", false, null);
            }

            if (!string.IsNullOrEmpty(self.PendingRoomReadyOperationId))
            {
                return await self.ContinuePendingRoomReady("Join", operationId);
            }

            if (self.TryGetRoomOperation("Join", operationId, out OnlineRoomOperationRecord record))
            {
                return (record.Error, record.Message, record.HasRoom, OnlineRoomSnapshotHelper.CloneSnapshot(record.Room));
            }

            if (self.CurrentRoomId != 0)
            {
                return self.RecordRoomOperation("Join", operationId, ErrorCode.ERR_OnlineAlreadyInRoom, "已在一个房间中", true, await self.GetCurrentRoomSnapshotOrNull());
            }

            ActorId directory = ServerConfig.Instance.GetSceneByName(self.Zone(), "RoomDirectory").ActorId;
            P2D_ResolveRoom resolve = P2D_ResolveRoom.Create();
            resolve.PlayerId = self.PlayerId;
            resolve.RoomCode = roomCode;
            resolve.SourceAddress = sourceAddress;
            using D2P_ResolveRoom resolved = await self.Root().GetComponent<MessageSender>().Call(directory, resolve, false) as D2P_ResolveRoom;
            if (resolved.Error != ErrorCode.ERR_Success)
            {
                return self.RecordRoomOperation("Join", operationId, resolved.Error, resolved.Message, false, null);
            }

            ActorId roomActor = await self.Root().GetComponent<LocationProxyComponent>().Get(LocationType.OnlineRoom, resolved.RoomId);
            P2R_JoinRoom join = P2R_JoinRoom.Create();
            join.PlayerId = self.PlayerId;
            join.DisplayName = self.DisplayName;
            join.SessionEpoch = self.SessionEpoch;
            join.ExpectedPurpose = expectedPurpose;
            join.ClientVersion = clientVersion ?? string.Empty;
            join.ArcadeConfigFingerprint = arcadeConfigFingerprint ?? string.Empty;
            using R2P_JoinRoom joined = await self.Root().GetComponent<MessageSender>().Call(roomActor, join, false) as R2P_JoinRoom;
            if (joined.Error == ErrorCode.ERR_Success || joined.Error == ErrorCode.ERR_OnlineChatMembershipPending)
            {
                self.CurrentRoomId = resolved.RoomId;
            }

            if (joined.Error == ErrorCode.ERR_OnlineChatMembershipPending)
            {
                self.SetPendingRoomReady("Join", operationId, resolved.RoomId);
                return (joined.Error, joined.Message, true, OnlineRoomSnapshotHelper.CloneSnapshot(joined.Room));
            }

            return self.RecordRoomOperation("Join", operationId, joined.Error, joined.Message, joined.Error == ErrorCode.ERR_Success, joined.Room);
        }

        /// <summary>仅在已登录且仍位于街机房时请求创建；安全节点落盘后才记录 RunId。</summary>
        public static async ETTask<(int Error, string Message, bool HasRun, OnlineArcadeRunSnapshot Run)> StartOnlineArcadeRun(
            this OnlinePlayerActor self, long sessionEpoch, string operationId)
        {
            if (sessionEpoch != self.SessionEpoch || self.State != OnlinePlayerState.Online)
                return (ErrorCode.ERR_OnlineSessionNotLoggedIn, "未登录", false, null);
            if (self.CurrentRoomId == 0)
                return (ErrorCode.ERR_OnlineArcadeStateInvalid, "当前不在街机房间中", false, null);
            try
            {
                ActorId roomActor = await self.Root().GetComponent<LocationProxyComponent>().Get(LocationType.OnlineRoom, self.CurrentRoomId);
                P2R_StartOnlineArcadeRun request = P2R_StartOnlineArcadeRun.Create();
                request.PlayerId = self.PlayerId;
                request.OperationId = operationId ?? string.Empty;
                using R2P_StartOnlineArcadeRun response = await self.Root().GetComponent<MessageSender>().Call(roomActor, request, false) as R2P_StartOnlineArcadeRun;
                if (response.Error == ErrorCode.ERR_Success && response.HasRun && response.Run != null)
                    self.CurrentArcadeRunId = response.Run.RunId;
                return (response.Error, response.Message, response.HasRun, OnlineArcadeRunActorSystem.CloneSnapshot(response.Run));
            }
            catch (RpcException exception)
            {
                return (exception.Error, exception.Message, false, null);
            }
        }

        /// <summary>按原玩家身份恢复安全节点，不依赖临时 Room Actor 是否仍存活。</summary>
        public static async ETTask<(int Error, string Message, bool HasRun, OnlineArcadeRunSnapshot Run)> GetCurrentOnlineArcadeRun(
            this OnlinePlayerActor self, long sessionEpoch)
        {
            if (sessionEpoch != self.SessionEpoch || self.State != OnlinePlayerState.Online)
                return (ErrorCode.ERR_OnlineSessionNotLoggedIn, "未登录", false, null);
            try
            {
                ActorId roomScene = ServerConfig.Instance.GetSceneByName(self.Zone(), "OnlineRoom").ActorId;
                P2R_GetCurrentOnlineArcadeRun request = P2R_GetCurrentOnlineArcadeRun.Create();
                request.PlayerId = self.PlayerId;
                using R2P_GetCurrentOnlineArcadeRun response = await self.Root().GetComponent<MessageSender>().Call(roomScene, request, false) as R2P_GetCurrentOnlineArcadeRun;
                self.CurrentArcadeRunId = response.Error == ErrorCode.ERR_Success && response.HasRun && response.Run != null
                    ? response.Run.RunId
                    : 0;
                return (response.Error, response.Message, response.HasRun, OnlineArcadeRunActorSystem.CloneSnapshot(response.Run));
            }
            catch (RpcException exception)
            {
                return (exception.Error, exception.Message, false, null);
            }
        }

        /// <summary>转发安全节点写意图；玩家 Actor 只校验会话并维护 RunId 路由，不复制安全节点状态。</summary>
        public static async ETTask<(int Error, string Message, bool Accepted, OnlineArcadeRunSnapshot Run)>
            OperateOnlineArcadeSafeCheckpoint(this OnlinePlayerActor self, long sessionEpoch, string operationId,
                long expectedRevision, int actionKind, bool ready)
        {
            if (sessionEpoch != self.SessionEpoch || self.State != OnlinePlayerState.Online)
                return (ErrorCode.ERR_OnlineSessionNotLoggedIn, "未登录", false, null);
            if (self.CurrentArcadeRunId == 0)
                return (ErrorCode.ERR_OnlineArcadeStateInvalid, "没有可操作的联机街机局", false, null);
            try
            {
                ActorId roomScene = ServerConfig.Instance.GetSceneByName(self.Zone(), "OnlineRoom").ActorId;
                P2R_OperateOnlineArcadeSafeCheckpoint request = P2R_OperateOnlineArcadeSafeCheckpoint.Create();
                request.PlayerId = self.PlayerId;
                request.OperationId = operationId ?? string.Empty;
                request.ExpectedRevision = expectedRevision;
                request.ActionKind = actionKind;
                request.Ready = ready;
                using R2P_OperateOnlineArcadeSafeCheckpoint response = await self.Root().GetComponent<MessageSender>()
                    .Call(roomScene, request, false) as R2P_OperateOnlineArcadeSafeCheckpoint;
                return (response.Error, response.Message, response.Accepted,
                    OnlineArcadeRunActorSystem.CloneSnapshot(response.Run));
            }
            catch (RpcException exception)
            {
                return (exception.Error, exception.Message, false, null);
            }
        }

        /// <summary>接收街机 Actor 已落盘的新快照，只向当前在线会话推送。</summary>
        public static async ETTask ReceiveOnlineArcadeRunChanged(this OnlinePlayerActor self,
            R2P_OnlineArcadeRunChanged message)
        {
            if (message.PlayerId != self.PlayerId || message.HasRun && message.Run == null) return;
            self.CurrentArcadeRunId = message.HasRun ? message.Run.RunId : 0;
            if (self.GateProxyActorId == default || self.State != OnlinePlayerState.Online) return;
            P2G_OnlineArcadeRunChanged push = P2G_OnlineArcadeRunChanged.Create();
            push.PlayerId = self.PlayerId;
            push.SessionEpoch = self.SessionEpoch;
            push.HasRun = message.HasRun;
            push.Run = message.HasRun ? OnlineArcadeRunActorSystem.CloneSnapshot(message.Run) : null;
            self.Root().GetComponent<MessageSender>().Send(self.GateProxyActorId, push);
            await ETTask.CompletedTask;
        }

        /// <summary>转发递增序号的战斗输入；不接受客户端位置、生命或伤害结果。</summary>
        public static async ETTask<(int Error, string Message, bool Accepted, long LastAcceptedInputSequence,
            OnlineArcadeRunSnapshot Run)> SubmitOnlineArcadeInput(this OnlinePlayerActor self, long sessionEpoch,
            int arenaNumber, long inputSequence, float moveX, float moveZ, float aimX, float aimZ, bool firing,
            bool useBomb, bool useDash, int roundGeneration = 0, bool roundReady = false, int exitTarget = 0, bool confirmExit = false)
        {
            if (sessionEpoch != self.SessionEpoch || self.State != OnlinePlayerState.Online)
                return (ErrorCode.ERR_OnlineSessionNotLoggedIn, "未登录", false, 0, null);
            if (self.CurrentArcadeRunId == 0)
                return (ErrorCode.ERR_OnlineArcadeStateInvalid, "当前没有联机街机局", false, 0, null);
            try
            {
                ActorId roomScene = ServerConfig.Instance.GetSceneByName(self.Zone(), "OnlineRoom").ActorId;
                P2R_SubmitOnlineArcadeInput request = P2R_SubmitOnlineArcadeInput.Create();
                request.PlayerId = self.PlayerId;
                request.ArenaNumber = arenaNumber;
                request.InputSequence = inputSequence;
                request.MoveX = moveX;
                request.MoveZ = moveZ;
                request.AimX = aimX;
                request.AimZ = aimZ;
                request.Firing = firing;
                request.UseBomb = useBomb;
                request.UseDash = useDash;
                request.RoundGeneration=roundGeneration;request.RoundReady=roundReady;request.ExitTarget=exitTarget;request.ConfirmExit=confirmExit;
                using R2P_SubmitOnlineArcadeInput response = await self.Root().GetComponent<MessageSender>()
                    .Call(roomScene, request, false) as R2P_SubmitOnlineArcadeInput;
                return (response.Error, response.Message, response.Accepted, response.LastAcceptedInputSequence,
                    OnlineArcadeRunActorSystem.CloneSnapshot(response.Run));
            }
            catch (RpcException exception)
            {
                return (exception.Error, exception.Message, false, 0, null);
            }
        }

        public static async ETTask<(int Error, string Message)> LeaveRoom(this OnlinePlayerActor self, long epoch, string operationId)
        {
            if (epoch != self.SessionEpoch || self.State == OnlinePlayerState.Offlining)
            {
                return (ErrorCode.ERR_OnlineSessionNotLoggedIn, "未登录");
            }

            if (!OnlineRoomSnapshotHelper.IsValidOperationId(operationId))
            {
                return (ErrorCode.ERR_OnlineRoomOperationInvalid, "操作标识无效或冲突");
            }

            if (self.TryGetRoomOperation("Leave", operationId, out OnlineRoomOperationRecord record))
            {
                return (record.Error, record.Message);
            }

            if (self.CurrentRoomId == 0)
            {
                var notInRoom = self.RecordRoomOperation("Leave", operationId, ErrorCode.ERR_OnlineNotInRoom, "当前不在房间中", false, null);
                return (notInRoom.Error, notInRoom.Message);
            }

            long roomId = self.CurrentRoomId;
            ActorId roomActor = await self.Root().GetComponent<LocationProxyComponent>().Get(LocationType.OnlineRoom, roomId);
            P2R_LeaveRoom leave = P2R_LeaveRoom.Create();
            leave.PlayerId = self.PlayerId;
            leave.OperationId = operationId;
            using R2P_LeaveRoom left = await self.Root().GetComponent<MessageSender>().Call(roomActor, leave, false) as R2P_LeaveRoom;
            if (left.Error == ErrorCode.ERR_Success || left.Error == ErrorCode.ERR_OnlineNotInRoom)
            {
                self.CurrentRoomId = 0;
                self.ClearPendingRoomReady();
            }

            var result = self.RecordRoomOperation("Leave", operationId, left.Error, left.Message, false, null);
            return (result.Error, result.Message);
        }

        public static async ETTask<(int Error, string Message, bool HasRoom, OnlineRoomSnapshot Room)> GetCurrentRoom(this OnlinePlayerActor self, long epoch)
        {
            if (epoch != self.SessionEpoch || self.State == OnlinePlayerState.Offlining)
            {
                return (ErrorCode.ERR_OnlineSessionNotLoggedIn, "未登录", false, null);
            }

            var result = await self.GetCurrentRoomSnapshot();
            return (result.Error, result.Message, result.Room != null, result.Room);
        }

        public static async ETTask ReceiveRoomChanged(this OnlinePlayerActor self, R2P_RoomChanged message)
        {
            if (message.PlayerId != self.PlayerId)
            {
                return;
            }

            if (!message.HasRoom && message.RoomId == self.CurrentRoomId)
            {
                self.CurrentRoomId = 0;
                self.ClearPendingRoomReady();
            }
            else if (message.HasRoom && message.Room != null)
            {
                self.CurrentRoomId = message.Room.RoomId;
            }

            ActorId gate = self.GateProxyActorId;
            if (gate == default || self.State != OnlinePlayerState.Online)
            {
                return;
            }

            P2G_RoomChanged push = P2G_RoomChanged.Create();
            push.PlayerId = self.PlayerId;
            push.SessionEpoch = self.SessionEpoch;
            push.HasRoom = message.HasRoom;
            push.Room = OnlineRoomSnapshotHelper.CloneSnapshot(message.Room);
            self.Root().GetComponent<MessageSender>().Send(gate, push);
            await ETTask.CompletedTask;
        }

        private static async ETTask AddOrRefreshLocation(this OnlinePlayerActor self)
        {
            await self.Root().GetComponent<LocationProxyComponent>().Add(LocationType.OnlinePlayer, self.PlayerId, self.GetActorId());
        }

        private static async ETTask<(int Error, string Message, bool HasRoom, OnlineRoomSnapshot Room)> ContinuePendingRoomReady(
            this OnlinePlayerActor self,
            string operationType,
            string operationId)
        {
            if (self.PendingRoomReadyOperationType != operationType || self.PendingRoomReadyOperationId != operationId)
            {
                return (ErrorCode.ERR_OnlineChatOperationConflict, "操作标识无效或冲突", self.CurrentRoomId != 0, await self.GetCurrentRoomSnapshotOrNull());
            }

            try
            {
                ActorId roomActor = await self.Root().GetComponent<LocationProxyComponent>().Get(LocationType.OnlineRoom, self.PendingRoomReadyRoomId);
                P2R_EnsureRoomChatReady ensure = P2R_EnsureRoomChatReady.Create();
                ensure.PlayerId = self.PlayerId;
                using R2P_EnsureRoomChatReady response = await self.Root().GetComponent<MessageSender>().Call(roomActor, ensure, false) as R2P_EnsureRoomChatReady;
                if (response.Error == ErrorCode.ERR_Success)
                {
                    self.ClearPendingRoomReady();
                    return self.RecordRoomOperation(operationType, operationId, ErrorCode.ERR_Success, string.Empty, true, response.Room);
                }

                if (response.Error == ErrorCode.ERR_OnlineChatMembershipPending || response.Error == ErrorCode.ERR_OnlineChatServiceUnavailable)
                {
                    return (ErrorCode.ERR_OnlineChatMembershipPending, "成员同步处理中，可重试", true, OnlineRoomSnapshotHelper.CloneSnapshot(response.Room));
                }

                self.ClearPendingRoomReady();
                return self.RecordRoomOperation(operationType, operationId, response.Error, response.Message, self.CurrentRoomId != 0, response.Room);
            }
            catch (Exception e)
            {
                Log.Warning($"online room chat ready continuation pending playerId={self.PlayerId} roomId={self.PendingRoomReadyRoomId} error={e.GetType().Name}");
                return (ErrorCode.ERR_OnlineChatMembershipPending, "成员同步处理中，可重试", true, await self.GetCurrentRoomSnapshotOrNull());
            }
        }

        private static void SetPendingRoomReady(this OnlinePlayerActor self, string operationType, string operationId, long roomId)
        {
            self.PendingRoomReadyOperationType = operationType;
            self.PendingRoomReadyOperationId = operationId;
            self.PendingRoomReadyRoomId = roomId;
        }

        private static void ClearPendingRoomReady(this OnlinePlayerActor self)
        {
            self.PendingRoomReadyOperationType = null;
            self.PendingRoomReadyOperationId = null;
            self.PendingRoomReadyRoomId = 0;
        }

        private static async ETTask OfflineAndDispose(this OnlinePlayerActor self)
        {
            self.State = OnlinePlayerState.Offlining;
            try
            {
                await self.Root().GetComponent<LocationProxyComponent>().Remove(LocationType.OnlinePlayer, self.PlayerId);
                OnlinePlayerManagerComponent manager = self.Parent as OnlinePlayerManagerComponent;
                manager?.Remove(self.PlayerId);
                self.Dispose();
            }
            catch (Exception e)
            {
                Log.Error($"online player offline cleanup failed stage=RemoveLocation playerId={self.PlayerId} roomId={self.CurrentRoomId} exception={e}");
                if (!self.SaveRetryPending)
                {
                    self.SaveRetryPending = true;
                    RetrySaveLater(self, self.SessionEpoch).Coroutine();
                }
            }
        }

        private static async ETTask LeaveRoomForOffline(this OnlinePlayerActor self, string operationId)
        {
            if (self.CurrentRoomId == 0)
            {
                return;
            }

            if (!OnlineRoomSnapshotHelper.IsValidOperationId(operationId))
            {
                throw new RpcException(ErrorCode.ERR_OnlineRoomOperationInvalid, "room lifecycle operation id invalid");
            }

            long roomId = self.CurrentRoomId;
            ActorId roomActor = await self.Root().GetComponent<LocationProxyComponent>().Get(LocationType.OnlineRoom, roomId);
            P2R_LeaveRoom leave = P2R_LeaveRoom.Create();
            leave.PlayerId = self.PlayerId;
            leave.OperationId = operationId;
            using R2P_LeaveRoom left = await self.Root().GetComponent<MessageSender>().Call(roomActor, leave, false) as R2P_LeaveRoom;
            if (left.Error == ErrorCode.ERR_Success || left.Error == ErrorCode.ERR_OnlineNotInRoom)
            {
                self.CurrentRoomId = 0;
                self.PendingOfflineRoomLeave = false;
                self.PendingOfflineRoomLeaveOperationId = null;
                return;
            }

            throw new RpcException(left.Error, left.Message);
        }

        private static async ETTask OfflineRoomAndDispose(this OnlinePlayerActor self, string operationId)
        {
            try
            {
                await self.LeaveRoomForOffline(operationId);
                await self.OfflineAndDispose();
            }
            catch (Exception e)
            {
                Log.Error($"online player room leave before offline failed stage=LeaveRoomForOffline playerId={self.PlayerId} roomId={self.CurrentRoomId} exception={e}");
                self.State = OnlinePlayerState.Offlining;
                self.PendingOfflineRoomLeave = true;
                self.PendingOfflineRoomLeaveOperationId = operationId;
                if (!self.SaveRetryPending)
                {
                    self.SaveRetryPending = true;
                    RetrySaveLater(self, self.SessionEpoch).Coroutine();
                }
            }
        }

        private static void RequestRoomConnectionSync(this OnlinePlayerActor self, int connectionState)
        {
            if (self.CurrentRoomId == 0)
            {
                self.PendingRoomSync = false;
                self.RoomSyncRetryQueued = false;
                return;
            }

            self.PendingRoomSync = true;
            self.PendingRoomConnectionState = connectionState;
            self.PendingRoomSyncEpoch = self.SessionEpoch;
            self.RoomSyncGeneration++;
            self.RoomSyncRetryQueued = false;
            self.TrySyncRoomConnection(self.RoomSyncGeneration).Coroutine();
        }

        private static async ETTask TrySyncRoomConnection(this OnlinePlayerActor self, long syncGeneration)
        {
            if (!self.PendingRoomSync || syncGeneration != self.RoomSyncGeneration || self.CurrentRoomId == 0)
            {
                return;
            }

            try
            {
                ActorId roomActor = await self.Root().GetComponent<LocationProxyComponent>().Get(LocationType.OnlineRoom, self.CurrentRoomId);
                if (!self.PendingRoomSync || syncGeneration != self.RoomSyncGeneration)
                {
                    return;
                }

                P2R_SetMemberConnection set = P2R_SetMemberConnection.Create();
                set.PlayerId = self.PlayerId;
                set.SessionEpoch = self.PendingRoomSyncEpoch;
                set.ConnectionState = self.PendingRoomConnectionState;
                set.OperationId = self.MakeRoomSyncOperationId(syncGeneration);
                using R2P_SetMemberConnection response = await self.Root().GetComponent<MessageSender>().Call(roomActor, set, false) as R2P_SetMemberConnection;
                if (!self.PendingRoomSync || syncGeneration != self.RoomSyncGeneration)
                {
                    return;
                }

                if (response.Error == ErrorCode.ERR_Success)
                {
                    self.PendingRoomSync = false;
                    self.RoomSyncRetryQueued = false;
                    return;
                }

                if (response.Error == ErrorCode.ERR_OnlineNotInRoom || response.Error == ErrorCode.ERR_OnlineRoomNotFound)
                {
                    self.CurrentRoomId = 0;
                    self.PendingRoomSync = false;
                    self.RoomSyncRetryQueued = false;
                    return;
                }

                self.QueueRoomSyncRetry(syncGeneration);
            }
            catch (Exception e)
            {
                Log.Error($"online room connection sync failed stage=SetConnection playerId={self.PlayerId} roomId={self.CurrentRoomId} exception={e}");
                self.QueueRoomSyncRetry(syncGeneration);
            }
        }

        private static async ETTask<OnlineRoomSnapshot> GetCurrentRoomSnapshotOrNull(this OnlinePlayerActor self)
        {
            var result = await self.GetCurrentRoomSnapshot();
            return result.Room;
        }

        private static async ETTask<(int Error, string Message, OnlineRoomSnapshot Room)> GetCurrentRoomSnapshot(this OnlinePlayerActor self)
        {
            if (self.CurrentRoomId == 0)
            {
                return (ErrorCode.ERR_Success, string.Empty, null);
            }

            try
            {
                ActorId roomActor = await self.Root().GetComponent<LocationProxyComponent>().Get(LocationType.OnlineRoom, self.CurrentRoomId);
                P2R_GetRoom get = P2R_GetRoom.Create();
                get.PlayerId = self.PlayerId;
                using R2P_GetRoom response = await self.Root().GetComponent<MessageSender>().Call(roomActor, get, false) as R2P_GetRoom;
                if (response.Error == ErrorCode.ERR_Success)
                {
                    return (ErrorCode.ERR_Success, string.Empty, OnlineRoomSnapshotHelper.CloneSnapshot(response.Room));
                }

                if (response.Error == ErrorCode.ERR_OnlineNotInRoom || response.Error == ErrorCode.ERR_OnlineRoomNotFound)
                {
                    self.CurrentRoomId = 0;
                    return (ErrorCode.ERR_Success, string.Empty, null);
                }

                return (ErrorCode.ERR_OnlineRoomServiceUnavailable, "房间服务暂不可用", null);
            }
            catch (Exception e)
            {
                Log.Error($"online current room query failed stage=GetCurrentRoom playerId={self.PlayerId} roomId={self.CurrentRoomId} exception={e}");
                return (ErrorCode.ERR_OnlineRoomServiceUnavailable, "房间服务暂不可用", null);
            }
        }

        private static bool TryGetRoomOperation(this OnlinePlayerActor self, string operationType, string operationId, out OnlineRoomOperationRecord record)
        {
            long now = TimeInfo.Instance.ServerNow();
            while (self.RoomOperations.Count > 0 && (self.RoomOperations.Count > 64 || now - self.RoomOperations.Peek().CreatedAt > 10 * 60 * 1000))
            {
                self.RoomOperations.Dequeue();
            }

            record = self.RoomOperations.FirstOrDefault(r => r.OperationType == operationType && r.OperationId == operationId);
            return record != null;
        }

        private static (int Error, string Message, bool HasRoom, OnlineRoomSnapshot Room) RecordRoomOperation(
            this OnlinePlayerActor self,
            string operationType,
            string operationId,
            int error,
            string message,
            bool hasRoom,
            OnlineRoomSnapshot room)
        {
            OnlineRoomOperationRecord record = new()
            {
                OperationType = operationType,
                OperationId = operationId,
                CreatedAt = TimeInfo.Instance.ServerNow(),
                Error = error,
                Message = message ?? string.Empty,
                HasRoom = hasRoom,
                Room = OnlineRoomSnapshotHelper.CloneSnapshot(room),
            };
            self.RoomOperations.Enqueue(record);
            while (self.RoomOperations.Count > 64)
            {
                self.RoomOperations.Dequeue();
            }

            return (record.Error, record.Message, record.HasRoom, OnlineRoomSnapshotHelper.CloneSnapshot(record.Room));
        }

        private static string MakeLifecycleOperationId(this OnlinePlayerActor self, string prefix)
        {
            return BuildLifecycleOperationId(self.InstanceId, self.SessionEpoch, prefix);
        }

        /// <summary>
        /// 为 Player Actor 生命周期事务生成稳定的 32 位操作标识；同一 Actor 内可安全重试，Actor 重建后不得复用旧标识。
        /// </summary>
        internal static string BuildLifecycleOperationId(long actorInstanceId, long sessionEpoch, string prefix)
        {
            ulong salt = prefix switch
            {
                "connect" => 0x1000000000000001UL,
                "grace" => 0x1000000000000002UL,
                "logout" => 0x1000000000000003UL,
                "timeout" => 0x1000000000000004UL,
                "retry" => 0x1000000000000005UL,
                "save" => 0x1000000000000006UL,
                _ => 0,
            };
            return $"{(ulong)actorInstanceId:x16}{((ulong)sessionEpoch ^ salt):x16}";
        }

        private static string MakeRoomSyncOperationId(this OnlinePlayerActor self, long syncGeneration)
        {
            return $"{(ulong)self.PlayerId:x16}{(ulong)self.PendingRoomSyncEpoch:x16}{(ulong)syncGeneration:x16}".Substring(16, 32);
        }

        private static void QueueRoomSyncRetry(this OnlinePlayerActor self, long syncGeneration)
        {
            if (!self.PendingRoomSync || syncGeneration != self.RoomSyncGeneration || self.RoomSyncRetryQueued)
            {
                return;
            }

            self.RoomSyncRetryQueued = true;
            RetryRoomSyncLater(self, self.PendingRoomSyncEpoch, syncGeneration).Coroutine();
        }

        private static async ETTask RetryRoomSyncLater(OnlinePlayerActor self, long epoch, long syncGeneration)
        {
            await self.Root().GetComponent<TimerComponent>().WaitAsync(5000);
            if (self.IsDisposed)
            {
                return;
            }

            P2P_RetryRoomSync retry = P2P_RetryRoomSync.Create();
            retry.PlayerId = self.PlayerId;
            retry.SessionEpoch = epoch;
            retry.SyncGeneration = syncGeneration;
            self.Root().GetComponent<MessageSender>().Send(self.GetActorId(), retry);
        }

        private static async ETTask WaitGraceTimeout(OnlinePlayerActor self, long epoch, long timerGeneration)
        {
            await self.Root().GetComponent<TimerComponent>().WaitAsync(Math.Max(100, Options.Instance.OnlinePlayerGraceMilliseconds));
            if (self.IsDisposed)
            {
                return;
            }
            G2P_PlayerGraceTimeout timeout = G2P_PlayerGraceTimeout.Create();
            timeout.PlayerId = self.PlayerId;
            timeout.SessionEpoch = epoch;
            timeout.TimerGeneration = timerGeneration;
            self.Root().GetComponent<MessageSender>().Send(self.GetActorId(), timeout);
        }

        private static async ETTask RetrySaveLater(OnlinePlayerActor self, long epoch)
        {
            await self.Root().GetComponent<TimerComponent>().WaitAsync(5000);
            if (self.IsDisposed)
            {
                return;
            }
            G2P_PlayerRetrySave retry = G2P_PlayerRetrySave.Create();
            retry.PlayerId = self.PlayerId;
            retry.SessionEpoch = epoch;
            self.Root().GetComponent<MessageSender>().Send(self.GetActorId(), retry);
        }

    }
}
