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
            self.CurrentWorldMapId = string.Empty;
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
            self.RequestWorldConnectionSync((int)OnlineWorldConnectionState.Online);
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
            self.RequestWorldConnectionSync((int)OnlineWorldConnectionState.DisconnectedGrace);
            await ETTask.CompletedTask;
        }

        public static async ETTask HandleLogout(this OnlinePlayerActor self, long epoch)
        {
            if (epoch != self.SessionEpoch || self.State == OnlinePlayerState.Offlining)
            {
                return;
            }

            self.GateProxyActorId = default;
            self.OfflineWorldAsCorpse = false;
            await self.OfflineRoomAndDispose(self.MakeLifecycleOperationId("logout"));
        }

        public static async ETTask HandleGraceTimeout(this OnlinePlayerActor self, long epoch, long timerGeneration)
        {
            if (epoch != self.SessionEpoch || timerGeneration != self.TimerGeneration || self.State != OnlinePlayerState.DisconnectedGrace)
            {
                return;
            }

            self.OfflineWorldAsCorpse = true;
            await self.OfflineRoomAndDispose(self.MakeLifecycleOperationId("timeout"));
        }

        public static async ETTask RetrySave(this OnlinePlayerActor self, long epoch)
        {
            if (epoch != self.SessionEpoch)
            {
                return;
            }

            self.SaveRetryPending = false;
            if (self.PendingWorldLeavePhase != OnlineWorldLeavePhase.None)
            {
                bool continueOffline = self.PendingWorldLeaveContinueOffline;
                var leaveResult = await self.ResumePendingWorldLeave();
                if (self.PendingWorldLeavePhase != OnlineWorldLeavePhase.None) return;
                if (continueOffline)
                {
                    await self.OfflineRoomAndDispose(self.PendingOfflineRoomLeaveOperationId ?? self.MakeLifecycleOperationId("retry"));
                }
                return;
            }
            if (self.State == OnlinePlayerState.Online && !string.IsNullOrEmpty(self.PendingProfileSaveOperationId))
            {
                try { await self.SaveProfileCandidate(self.PendingProfileSaveOperationId, self.OnlineInventory, self.HasReceivedOnlineStarterSupplies); }
                catch (Exception e) { Log.Warning($"online checkpoint retry failed playerId={self.PlayerId} error={e.GetType().Name}"); self.QueueProfileRetry(); }
                return;
            }
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

        /// <summary>转发安全商店写意图；玩家 Actor 只校验会话并维护 RunId 路由，不复制商店状态。</summary>
        public static async ETTask<(int Error, string Message, bool Accepted, OnlineArcadeRunSnapshot Run)>
            OperateOnlineArcadeSafeShop(this OnlinePlayerActor self, long sessionEpoch, string operationId,
                long expectedRevision, int actionKind, string offerId, int goldAmount, bool ready)
        {
            if (sessionEpoch != self.SessionEpoch || self.State != OnlinePlayerState.Online)
                return (ErrorCode.ERR_OnlineSessionNotLoggedIn, "未登录", false, null);
            // 离线单独结算会先关闭团队运行局，待领取奖励因此必须允许在没有 CurrentArcadeRunId 时提交。
            bool claimingPendingRewards = actionKind == (int)OnlineArcadeSafeShopActionKind.ClaimPendingRewards;
            if (self.CurrentArcadeRunId == 0 && !claimingPendingRewards)
                return (ErrorCode.ERR_OnlineArcadeStateInvalid, "没有可操作的联机街机局", false, null);
            try
            {
                ActorId roomScene = ServerConfig.Instance.GetSceneByName(self.Zone(), "OnlineRoom").ActorId;
                P2R_OperateOnlineArcadeSafeShop request = P2R_OperateOnlineArcadeSafeShop.Create();
                request.PlayerId = self.PlayerId;
                request.OperationId = operationId ?? string.Empty;
                request.ExpectedRevision = expectedRevision;
                request.ActionKind = actionKind;
                request.OfferId = offerId ?? string.Empty;
                request.GoldAmount = goldAmount;
                request.Ready = ready;
                using R2P_OperateOnlineArcadeSafeShop response = await self.Root().GetComponent<MessageSender>()
                    .Call(roomScene, request, false) as R2P_OperateOnlineArcadeSafeShop;
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
            bool useBomb, bool useDash)
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
                if (!self.ProfileAlreadySavedForOffline)
                {
                    self.PendingProfileSaveOperationId ??= self.MakeLifecycleOperationId("save");
                    await self.SaveProfileCandidate(self.PendingProfileSaveOperationId, self.OnlineInventory, self.HasReceivedOnlineStarterSupplies);
                }
                await self.Root().GetComponent<LocationProxyComponent>().Remove(LocationType.OnlinePlayer, self.PlayerId);
                OnlinePlayerManagerComponent manager = self.Parent as OnlinePlayerManagerComponent;
                manager?.Remove(self.PlayerId);
                self.Dispose();
            }
            catch (Exception e)
            {
                Log.Error($"online player offline save failed stage=SaveProfile playerId={self.PlayerId} roomId={self.CurrentRoomId} exception={e}");
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
                await self.LeaveWorldForOffline();
                await self.LeaveRoomForOffline(operationId);
                await self.OfflineAndDispose();
            }
            catch (Exception e)
            {
                Log.Error($"online player room leave before offline failed stage=LeaveRoomForOffline playerId={self.PlayerId} roomId={self.CurrentRoomId} exception={e}");
                self.State = OnlinePlayerState.Offlining;
                self.PendingOfflineRoomLeave = true;
                self.PendingOfflineWorldLeave = !string.IsNullOrEmpty(self.CurrentWorldMapId);
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

        private static async ETTask SaveProfileCandidate(this OnlinePlayerActor self, string operationId, OnlineInventoryRecord inventory, bool receivedStarter)
        {
            Log.Info($"online player save attempt playerId={self.PlayerId} epoch={self.SessionEpoch}");
            ActorId accountActorId = ServerConfig.Instance.GetSceneByName(self.Zone(), "Account").ActorId;
            P2A_SavePlayerProfile save = P2A_SavePlayerProfile.Create();
            save.AccountId = self.AccountId;
            save.PlayerId = self.PlayerId;
            save.DisplayName = self.DisplayName;
            save.Revision = self.Revision;
            save.OperationId = operationId;
            save.ExpectedRevision = self.Revision;
            save.OnlineInventory = OnlineInventoryRecordHelper.ToMessage(inventory);
            save.HasReceivedOnlineStarterSupplies = receivedStarter;
            using A2P_SavePlayerProfile response = await self.Root().GetComponent<MessageSender>().Call(accountActorId, save) as A2P_SavePlayerProfile;
            if (response.Error != ErrorCode.ERR_Success)
            {
                throw new RpcException(response.Error, response.Message);
            }
            self.Revision = response.Revision;
            self.OnlineInventory = OnlineInventoryRecordHelper.Clone(inventory);
            self.HasReceivedOnlineStarterSupplies = receivedStarter;
            self.PendingProfileSaveOperationId = null;
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

        private static string NextWorldLeaveOperationId(this OnlinePlayerActor self)
        {
            return $"{(ulong)self.PlayerId:x16}{(ulong)++self.WorldLeaveGeneration:x16}";
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

        public static async ETTask<(int Error, string Message, bool HasWorld, OnlineWorldSnapshot World)> EnterWorld(this OnlinePlayerActor self, long epoch, string operationId, string mapId)
        {
            if (epoch != self.SessionEpoch || self.State != OnlinePlayerState.Online) return (ErrorCode.ERR_OnlineSessionNotLoggedIn, "未登录", false, null);
            if (!OnlineRoomSnapshotHelper.IsValidOperationId(operationId)) return (ErrorCode.ERR_OnlineWorldOperationInvalid, "操作标识无效", false, null);
            if (string.IsNullOrWhiteSpace(mapId) || !ServerConfig.Instance.OnlineWorldMapIds.Contains(mapId, StringComparer.Ordinal))
                return (ErrorCode.ERR_OnlineWorldOperationInvalid, "地图标识无效", false, null);
            if (!string.IsNullOrEmpty(self.CurrentWorldMapId) && !string.Equals(self.CurrentWorldMapId, mapId, StringComparison.Ordinal)) return (ErrorCode.ERR_OnlineWorldStateConflict, "当前已在其它地图中", false, null);
            if (string.IsNullOrEmpty(self.CurrentWorldMapId) && self.CurrentRoomId != 0)
            {
                var left = await self.LeaveRoom(epoch, operationId);
                if (left.Error != ErrorCode.ERR_Success && left.Error != ErrorCode.ERR_OnlineNotInRoom) return (left.Error, left.Message, false, null);
            }
            if (!self.HasReceivedOnlineStarterSupplies)
            {
                OnlineInventoryRecord starter = OnlineInventoryRecordHelper.Clone(self.OnlineInventory);
                OnlineWorldRuleConfig starterRules = ServerConfig.Instance.OnlineWorldRule;
                starter.Slots.Add(new OnlineItemSlotRecord { SlotIndex = 0, ItemId = starterRules.StarterMeleeWeaponItemId, Amount = 1 });
                starter.Equipment.ItemId = starterRules.StarterMeleeWeaponItemId;
                starter.Equipment.SourceSlotIndex = 0;
                if (!TryAddStarterItem(starter, starterRules.StarterRangedWeaponItemId, 1))
                    return (ErrorCode.ERR_OnlineProfileSaveFailed, "首次武器无法放入联机背包", false, null);
                WeaponConfig rangedWeapon = ServerConfig.Instance.GetWeapon(starterRules.StarterRangedWeaponItemId);
                if (!TryAddStarterItem(starter, rangedWeapon.AmmoItemId, starterRules.StarterRangedAmmoAmount))
                    return (ErrorCode.ERR_OnlineProfileSaveFailed, "首次物资无法放入联机背包", false, null);
                starter.Revision++;
                try { await self.SaveProfileCandidate("starter:" + operationId, starter, true); }
                catch (RpcException e) { return (e.Error, "首次物资保存失败，请重试", false, null); }
            }
            ActorId worldActor = ServerConfig.Instance.GetSceneByName(self.Zone(), "OnlineWorld").ActorId;
            P2W_EnterWorld request = P2W_EnterWorld.Create(); request.PlayerId = self.PlayerId; request.DisplayName = self.DisplayName; request.PlayerActorId = self.GetActorId(); request.MapId = mapId;request.OnlineInventory=OnlineInventoryRecordHelper.ToMessage(self.OnlineInventory);
            using W2P_EnterWorld response = await self.Root().GetComponent<MessageSender>().Call(worldActor, request, false) as W2P_EnterWorld;
            if (response.Error == ErrorCode.ERR_Success && response.World != null) { self.CurrentWorldId = response.World.WorldId; self.CurrentWorldMapId = response.World.MapId; self.CurrentWorldUnitId = response.World.SelfUnitId;self.ProfileAlreadySavedForOffline=false; }
            return (response.Error, response.Message, response.World != null, OnlineWorldSnapshotHelper.Clone(response.World));
        }

        private static bool TryAddStarterItem(OnlineInventoryRecord inventory, int itemId, int amount)
        {
            Item item = ServerConfig.Instance.GetItem(itemId);
            foreach (OnlineItemSlotRecord slot in inventory.Slots.Where(x => x.ItemId == itemId && x.Amount < item.MaxStackAmount).OrderBy(x => x.SlotIndex))
            {
                int accepted = Math.Min(item.MaxStackAmount - slot.Amount, amount);
                slot.Amount += accepted;
                amount -= accepted;
                if (amount == 0) return true;
            }
            for (int slotIndex = 0; slotIndex < inventory.Capacity && amount > 0; slotIndex++)
            {
                if (inventory.Slots.Any(x => x.SlotIndex == slotIndex)) continue;
                int accepted = Math.Min(item.MaxStackAmount, amount);
                inventory.Slots.Add(new OnlineItemSlotRecord { SlotIndex = slotIndex, ItemId = itemId, Amount = accepted });
                amount -= accepted;
            }
            return amount == 0;
        }

        public static async ETTask<(int Error, string Message, bool HasWorld, OnlineWorldSnapshot World)> GetCurrentWorld(this OnlinePlayerActor self, long epoch)
        {
            if (epoch != self.SessionEpoch || self.State == OnlinePlayerState.Offlining) return (ErrorCode.ERR_OnlineSessionNotLoggedIn, "未登录", false, null);
            if (string.IsNullOrEmpty(self.CurrentWorldMapId)) return (ErrorCode.ERR_Success, string.Empty, false, null);
            ActorId actor = ServerConfig.Instance.GetSceneByName(self.Zone(), "OnlineWorld").ActorId;
            P2W_GetCurrentWorld request = P2W_GetCurrentWorld.Create(); request.PlayerId = self.PlayerId; request.WorldId = self.CurrentWorldId; request.MapId = self.CurrentWorldMapId; request.UnitId = self.CurrentWorldUnitId;
            using W2P_GetCurrentWorld response = await self.Root().GetComponent<MessageSender>().Call(actor, request, false) as W2P_GetCurrentWorld;
            if (response.Error == ErrorCode.ERR_Success && !response.HasWorld) self.ClearWorldIdentity();
            else if (response.Error == ErrorCode.ERR_Success && response.World != null) { self.CurrentWorldId = response.World.WorldId; self.CurrentWorldMapId = response.World.MapId; self.CurrentWorldUnitId = response.World.SelfUnitId; }
            return (response.Error, response.Message, response.HasWorld, OnlineWorldSnapshotHelper.Clone(response.World));
        }

        public static async ETTask<(int Error, string Message)> LeaveWorld(this OnlinePlayerActor self, long epoch, string operationId)
        {
            if (epoch != self.SessionEpoch || self.State == OnlinePlayerState.Offlining) return (ErrorCode.ERR_OnlineSessionNotLoggedIn, "未登录");
            if (!OnlineRoomSnapshotHelper.IsValidOperationId(operationId)) return (ErrorCode.ERR_OnlineWorldOperationInvalid, "操作标识无效");
            if (string.IsNullOrEmpty(self.CurrentWorldMapId)) return (ErrorCode.ERR_Success, string.Empty);
            return await self.LeaveWorldCore(operationId, false);
        }

        public static async ETTask<(int Error, string Message, long WorldId, string MapId, long UnitId, long StateRevision, long LifecycleRevision)> ReportWorldTransform(this OnlinePlayerActor self, long epoch, float x, float y, float z, float yaw, long lifecycleRevision)
        {
            if (epoch != self.SessionEpoch || self.State != OnlinePlayerState.Online) return (ErrorCode.ERR_OnlineSessionNotLoggedIn, "未登录", 0, null, 0, 0, 0);
            if (self.CurrentWorldUnitId == 0) return (ErrorCode.ERR_OnlineNotInWorld, "当前不在世界中", 0, null, 0, 0, 0);
            ActorId actor = ServerConfig.Instance.GetSceneByName(self.Zone(), "OnlineWorld").ActorId;
            P2W_ReportWorldTransform request = P2W_ReportWorldTransform.Create(); request.PlayerId = self.PlayerId; request.WorldId = self.CurrentWorldId; request.MapId = self.CurrentWorldMapId; request.UnitId = self.CurrentWorldUnitId; request.PositionX = x; request.PositionY = y; request.PositionZ = z; request.YawDegrees = yaw;request.LifecycleRevision=lifecycleRevision;
            using W2P_ReportWorldTransform response = await self.Root().GetComponent<MessageSender>().Call(actor, request, false) as W2P_ReportWorldTransform;
            return (response.Error, response.Message, response.WorldId, response.MapId, response.UnitId, response.StateRevision,response.LifecycleRevision);
        }

        public static async ETTask<(int Error,string Message,long WorldId,long UnitId,long AttackSequence,bool Accepted,long WorldRevision,long LifecycleRevision)> WorldMeleeAttack(this OnlinePlayerActor self,long epoch,long sequence,int weapon,float aimX,float aimZ,long lifecycle,float aimY=0,float originX=0,float originY=0,float originZ=0,bool hasProjectileOrigin=false,string collisionContentHash=null)
        {if(epoch!=self.SessionEpoch||self.State!=OnlinePlayerState.Online)return(ErrorCode.ERR_OnlineSessionNotLoggedIn,"未登录",0,0,sequence,false,0,lifecycle);if(self.CurrentWorldUnitId==0)return(ErrorCode.ERR_OnlineNotInWorld,"当前不在世界中",0,0,sequence,false,0,lifecycle);ActorId actor=ServerConfig.Instance.GetSceneByName(self.Zone(),"OnlineWorld").ActorId;P2W_WorldMeleeAttack q=P2W_WorldMeleeAttack.Create();q.PlayerId=self.PlayerId;q.WorldId=self.CurrentWorldId;q.MapId=self.CurrentWorldMapId;q.UnitId=self.CurrentWorldUnitId;q.AttackSequence=sequence;q.WeaponItemId=weapon;q.AimX=aimX;q.AimZ=aimZ;q.LifecycleRevision=lifecycle;q.AimY=aimY;q.OriginX=originX;q.OriginY=originY;q.OriginZ=originZ;q.HasProjectileOrigin=hasProjectileOrigin;q.CollisionContentHash=collisionContentHash;using W2P_WorldMeleeAttack r=await self.Root().GetComponent<MessageSender>().Call(actor,q,false) as W2P_WorldMeleeAttack;return(r.Error,r.Message,r.WorldId,r.UnitId,r.AttackSequence,r.Accepted,r.WorldRevision,r.LifecycleRevision);}

        public static async ETTask<(int Error,string Message,bool Accepted,long LeaseRevision,long LastSequence,long WorldRevision)> ReportWorldEnemyNavigation(this OnlinePlayerActor self,long epoch,long lease,long sequence,IReadOnlyList<WorldEnemyNavigationReport> reports)
        {if(epoch!=self.SessionEpoch||self.State!=OnlinePlayerState.Online)return(ErrorCode.ERR_OnlineSessionNotLoggedIn,"未登录",false,0,0,0);if(self.CurrentWorldUnitId==0)return(ErrorCode.ERR_OnlineNotInWorld,"当前不在世界中",false,0,0,0);ActorId actor=ServerConfig.Instance.GetSceneByName(self.Zone(),"OnlineWorld").ActorId;P2W_ReportWorldEnemyNavigation q=P2W_ReportWorldEnemyNavigation.Create();q.PlayerId=self.PlayerId;q.WorldId=self.CurrentWorldId;q.MapId=self.CurrentWorldMapId;q.UnitId=self.CurrentWorldUnitId;q.NavigationLeaseRevision=lease;q.ReportSequence=sequence;foreach(WorldEnemyNavigationReport s in reports){WorldEnemyNavigationReport t=WorldEnemyNavigationReport.Create();t.UnitId=s.UnitId;t.LifecycleRevision=s.LifecycleRevision;t.PositionX=s.PositionX;t.PositionY=s.PositionY;t.PositionZ=s.PositionZ;t.YawDegrees=s.YawDegrees;q.Reports.Add(t);}using W2P_ReportWorldEnemyNavigation r=await self.Root().GetComponent<MessageSender>().Call(actor,q,false) as W2P_ReportWorldEnemyNavigation;return(r.Error,r.Message,r.Accepted,r.CurrentLeaseRevision,r.LastAcceptedReportSequence,r.WorldRevision);}

        public static async ETTask<(int Error,string Message,OnlineInventoryInfo Inventory,WorldCorpseContentsInfo Corpse)> OpenWorldCorpse(this OnlinePlayerActor self,long epoch,long corpseId,long lifecycle)
        {if(epoch!=self.SessionEpoch||self.State!=OnlinePlayerState.Online)return(ErrorCode.ERR_OnlineSessionNotLoggedIn,"未登录",null,null);if(self.CurrentWorldUnitId==0)return(ErrorCode.ERR_OnlineNotInWorld,"当前没有可操作角色",null,null);ActorId actor=ServerConfig.Instance.GetSceneByName(self.Zone(),"OnlineWorld").ActorId;P2W_OpenWorldCorpse q=P2W_OpenWorldCorpse.Create();q.PlayerId=self.PlayerId;q.WorldId=self.CurrentWorldId;q.MapId=self.CurrentWorldMapId;q.UnitId=self.CurrentWorldUnitId;q.CorpseId=corpseId;q.LifecycleRevision=lifecycle;using W2P_OpenWorldCorpse r=await self.Root().GetComponent<MessageSender>().Call(actor,q,false) as W2P_OpenWorldCorpse;return(r.Error,r.Message,OnlineWorldSnapshotHelper.Clone(r.SelfInventory),OnlineWorldSnapshotHelper.Clone(r.CorpseContents));}

        public static async ETTask<(int Error,string Message,bool Accepted,OnlineInventoryInfo Inventory,bool HasCorpse,WorldCorpseContentsInfo Corpse)> OperateWorldInventory(this OnlinePlayerActor self,G2P_OperateWorldInventory input)
        {if(input.SessionEpoch!=self.SessionEpoch||self.State!=OnlinePlayerState.Online)return(ErrorCode.ERR_OnlineSessionNotLoggedIn,"未登录",false,null,false,null);if(self.CurrentWorldUnitId==0)return(ErrorCode.ERR_OnlineNotInWorld,"当前没有可操作角色",false,null,false,null);ActorId actor=ServerConfig.Instance.GetSceneByName(self.Zone(),"OnlineWorld").ActorId;P2W_OperateWorldInventory q=P2W_OperateWorldInventory.Create();q.PlayerId=self.PlayerId;q.WorldId=self.CurrentWorldId;q.MapId=self.CurrentWorldMapId;q.UnitId=self.CurrentWorldUnitId;q.OperationId=input.OperationId;q.ExpectedInventoryRevision=input.ExpectedInventoryRevision;q.CorpseId=input.CorpseId;q.ExpectedCorpseRevision=input.ExpectedCorpseRevision;q.ActionKind=input.ActionKind;q.SourceContainerKind=input.SourceContainerKind;q.SourceSlotIndex=input.SourceSlotIndex;q.TargetContainerKind=input.TargetContainerKind;q.TargetSlotIndex=input.TargetSlotIndex;q.LifecycleRevision=input.LifecycleRevision;q.InventorySchemaVersion=input.InventorySchemaVersion;q.ExpectedItemId=input.ExpectedItemId;using W2P_OperateWorldInventory r=await self.Root().GetComponent<MessageSender>().Call(actor,q,false) as W2P_OperateWorldInventory;return(r.Error,r.Message,r.Accepted,OnlineWorldSnapshotHelper.Clone(r.SelfInventory),r.HasCorpse,OnlineWorldSnapshotHelper.Clone(r.CorpseContents));}

        public static async ETTask<(int Error,string Message,bool Accepted,long WorldRevision,WorldTemporaryBuildingInfo Building,int RemovedRuntimeId,OnlineInventoryInfo Inventory,long ServerTime)> OperateWorldTemporaryBuilding(this OnlinePlayerActor self,G2P_OperateWorldTemporaryBuilding input)
        {if(input.SessionEpoch!=self.SessionEpoch||self.State!=OnlinePlayerState.Online)return(ErrorCode.ERR_OnlineSessionNotLoggedIn,"未登录",false,0,null,0,null,0);if(self.CurrentWorldUnitId==0)return(ErrorCode.ERR_OnlineNotInWorld,"当前没有可操作角色",false,0,null,0,null,0);ActorId actor=ServerConfig.Instance.GetSceneByName(self.Zone(),"OnlineWorld").ActorId;P2W_OperateWorldTemporaryBuilding q=P2W_OperateWorldTemporaryBuilding.Create();q.PlayerId=self.PlayerId;q.WorldId=self.CurrentWorldId;q.MapId=self.CurrentWorldMapId;q.UnitId=self.CurrentWorldUnitId;q.OperationId=input.OperationId;q.ActionKind=input.ActionKind;q.RuntimeId=input.RuntimeId;q.ConfigId=input.ConfigId;q.PositionX=input.PositionX;q.PositionY=input.PositionY;q.PositionZ=input.PositionZ;q.YawDegrees=input.YawDegrees;q.AimX=input.AimX;q.AimZ=input.AimZ;q.ExpectedInventoryRevision=input.ExpectedInventoryRevision;q.ExpectedBuildingRevision=input.ExpectedBuildingRevision;q.LifecycleRevision=input.LifecycleRevision;using W2P_OperateWorldTemporaryBuilding r=await self.Root().GetComponent<MessageSender>().Call(actor,q,false) as W2P_OperateWorldTemporaryBuilding;return(r.Error,r.Message,r.Accepted,r.WorldRevision,OnlineWorldSnapshotHelper.Clone(r.Building),r.RemovedRuntimeId,OnlineWorldSnapshotHelper.Clone(r.SelfInventory),r.ServerTime);}

        private static async ETTask<(int Error, string Message)> LeaveWorldCore(this OnlinePlayerActor self, string operationId, bool continueOffline)
        {
            if (self.PendingWorldLeavePhase != OnlineWorldLeavePhase.None)
            {
                if (!string.Equals(self.PendingWorldLeaveOperationId, operationId, StringComparison.Ordinal))
                    return (ErrorCode.ERR_OnlineWorldOperationConflict, "已有离开事务正在收口");
                return await self.ResumePendingWorldLeave();
            }
            if (!string.IsNullOrEmpty(self.PendingWorldLeaveOperationId) &&
                !string.Equals(self.PendingWorldLeaveOperationId, operationId, StringComparison.Ordinal))
                return (ErrorCode.ERR_OnlineWorldOperationConflict, "已有离开事务正在收口");

            ActorId actor = ServerConfig.Instance.GetSceneByName(self.Zone(), "OnlineWorld").ActorId;
            P2W_PrepareWorldLeave prepare = P2W_PrepareWorldLeave.Create();prepare.PlayerId=self.PlayerId;prepare.WorldId=self.CurrentWorldId;prepare.MapId=self.CurrentWorldMapId;prepare.UnitId=self.CurrentWorldUnitId;prepare.OperationId=operationId;
            using W2P_PrepareWorldLeave prepared=await self.Root().GetComponent<MessageSender>().Call(actor,prepare,false) as W2P_PrepareWorldLeave;
            if(prepared.Error!=ErrorCode.ERR_Success||!prepared.Prepared)return(prepared.Error,prepared.Message);
            self.PendingWorldLeaveOperationId=operationId;
            self.PendingWorldLeaveResumeState=self.State;
            self.PendingWorldLeaveContinueOffline=continueOffline;
            OnlineInventoryRecord candidate=OnlineInventoryRecordHelper.FromMessage(prepared.OnlineInventory);
            bool alreadySaved=self.ProfileAlreadySavedForOffline&&string.Equals(self.PendingWorldLeaveOperationId,operationId,StringComparison.Ordinal);
            try{if(!alreadySaved)await self.SaveProfileCandidate("leave:"+operationId,candidate,self.HasReceivedOnlineStarterSupplies);self.ProfileAlreadySavedForOffline=true;}
            catch(Exception e)
            {
                self.ProfileAlreadySavedForOffline=false;
                self.PendingWorldLeaveFailureError=e is RpcException rpc ? rpc.Error : ErrorCode.ERR_OnlineProfileSaveFailed;
                self.PendingWorldLeavePhase=OnlineWorldLeavePhase.Aborting;
                return await self.ResumePendingWorldLeave();
            }
            self.PendingWorldLeavePhase=OnlineWorldLeavePhase.Committing;
            return await self.ResumePendingWorldLeave();
        }

        private static async ETTask<(int Error, string Message)> ResumePendingWorldLeave(this OnlinePlayerActor self)
        {
            string operationId=self.PendingWorldLeaveOperationId;
            ActorId actor=ServerConfig.Instance.GetSceneByName(self.Zone(),"OnlineWorld").ActorId;
            if(self.PendingWorldLeavePhase==OnlineWorldLeavePhase.Aborting)
            {
                try
                {
                    P2W_AbortWorldLeave abort=P2W_AbortWorldLeave.Create();abort.PlayerId=self.PlayerId;abort.WorldId=self.CurrentWorldId;abort.MapId=self.CurrentWorldMapId;abort.OperationId=operationId;
                    using W2P_AbortWorldLeave aborted=await self.Root().GetComponent<MessageSender>().Call(actor,abort,false) as W2P_AbortWorldLeave;
                    if(aborted.Error==ErrorCode.ERR_Success&&aborted.Completed)
                    {
                        int failure=self.PendingWorldLeaveFailureError==0?ErrorCode.ERR_OnlineProfileSaveFailed:self.PendingWorldLeaveFailureError;
                        bool continueOffline=self.PendingWorldLeaveContinueOffline;OnlinePlayerState resume=self.PendingWorldLeaveResumeState;
                        self.ClearPendingWorldLeave();self.ProfileAlreadySavedForOffline=false;self.State=continueOffline?OnlinePlayerState.Offlining:resume;
                        return(failure,"在线物资保存失败，离开已取消");
                    }
                    if(aborted.Error==ErrorCode.ERR_OnlineWorldOperationConflict)
                    {
                        bool continueOffline=self.PendingWorldLeaveContinueOffline;OnlinePlayerState resume=self.PendingWorldLeaveResumeState;
                        self.ClearWorldIdentity();self.ClearPendingWorldLeave();self.ProfileAlreadySavedForOffline=true;self.State=continueOffline?OnlinePlayerState.Offlining:resume;
                        return(ErrorCode.ERR_Success,string.Empty);
                    }
                }
                catch(Exception e){Log.Warning($"online world leave abort retry playerId={self.PlayerId} operationId={operationId} error={e.GetType().Name}");}
                self.QueuePendingWorldLeaveRetry();
                return(ErrorCode.ERR_OnlineWorldServiceUnavailable,"离开取消结果尚未确认，请稍后重试");
            }

            if(self.PendingWorldLeavePhase==OnlineWorldLeavePhase.Committing)
            {
                for(int attempt=0;attempt<2;attempt++)
                {
                    try
                    {
                        P2W_CommitWorldLeave commit=P2W_CommitWorldLeave.Create();commit.PlayerId=self.PlayerId;commit.WorldId=self.CurrentWorldId;commit.MapId=self.CurrentWorldMapId;commit.OperationId=operationId;
                        using W2P_CommitWorldLeave committed=await self.Root().GetComponent<MessageSender>().Call(actor,commit,false) as W2P_CommitWorldLeave;
                        if(committed.Error==ErrorCode.ERR_Success&&committed.Completed)
                        {
                            bool continueOffline=self.PendingWorldLeaveContinueOffline;OnlinePlayerState resume=self.PendingWorldLeaveResumeState;
                            self.ClearWorldIdentity();self.ClearPendingWorldLeave();self.PendingOfflineWorldLeave=false;self.ProfileAlreadySavedForOffline=true;self.State=continueOffline?OnlinePlayerState.Offlining:resume;
                            return(ErrorCode.ERR_Success,string.Empty);
                        }
                    }
                    catch(Exception e){Log.Warning($"online world leave commit retry playerId={self.PlayerId} operationId={operationId} error={e.GetType().Name}");}
                }
                self.QueuePendingWorldLeaveRetry();
                return(ErrorCode.ERR_OnlineWorldServiceUnavailable,"离开提交结果尚未确认，请稍后重试");
            }
            return(ErrorCode.ERR_OnlineWorldOperationConflict,"没有待处理的离开事务");
        }

        private static async ETTask LeaveWorldForOffline(this OnlinePlayerActor self)
        {
            if (string.IsNullOrEmpty(self.CurrentWorldMapId)) return;
            if(self.OfflineWorldAsCorpse)
            {
                ActorId actor=ServerConfig.Instance.GetSceneByName(self.Zone(),"OnlineWorld").ActorId;P2W_LeaveWorld request=P2W_LeaveWorld.Create();request.PlayerId=self.PlayerId;request.WorldId=self.CurrentWorldId;request.MapId=self.CurrentWorldMapId;request.UnitId=self.CurrentWorldUnitId;
                using W2P_LeaveWorld response=await self.Root().GetComponent<MessageSender>().Call(actor,request,false) as W2P_LeaveWorld;if(response.Error!=ErrorCode.ERR_Success&&response.Error!=ErrorCode.ERR_OnlineNotInWorld)throw new RpcException(response.Error,response.Message);
                OnlineInventoryRecord empty=OnlineInventoryRecordHelper.Empty(ServerConfig.Instance.OnlineWorldRule.BagCapacity);empty.Revision=Math.Max(1,(self.OnlineInventory?.Revision??0)+1);self.OnlineInventory=empty;self.ClearWorldIdentity();return;
            }
            string operationId=self.PendingWorldLeaveOperationId??self.NextWorldLeaveOperationId();
            var result = await self.LeaveWorldCore(operationId, true);
            if (result.Error != ErrorCode.ERR_Success) throw new RpcException(result.Error, result.Message);
        }

        private static void QueuePendingWorldLeaveRetry(this OnlinePlayerActor self)
        {
            self.State=OnlinePlayerState.Offlining;
            if(self.SaveRetryPending)return;
            self.SaveRetryPending=true;
            RetrySaveLater(self,self.SessionEpoch).Coroutine();
        }

        private static void ClearPendingWorldLeave(this OnlinePlayerActor self)
        {
            self.PendingWorldLeaveOperationId=null;
            self.PendingWorldLeavePhase=OnlineWorldLeavePhase.None;
            self.PendingWorldLeaveResumeState=default;
            self.PendingWorldLeaveContinueOffline=false;
            self.PendingWorldLeaveFailureError=0;
        }

        private static void ClearWorldIdentity(this OnlinePlayerActor self)
        {
            self.CurrentWorldId = 0;
            self.CurrentWorldMapId = string.Empty;
            self.CurrentWorldUnitId = 0;
        }

        private static void RequestWorldConnectionSync(this OnlinePlayerActor self, int state)
        {
            if (!string.IsNullOrEmpty(self.CurrentWorldMapId)) self.SyncWorldConnection(state).Coroutine();
        }

        private static async ETTask SyncWorldConnection(this OnlinePlayerActor self, int state)
        {
            try
            {
                ActorId actor = ServerConfig.Instance.GetSceneByName(self.Zone(), "OnlineWorld").ActorId;
                P2W_SetWorldConnection request = P2W_SetWorldConnection.Create(); request.PlayerId = self.PlayerId; request.WorldId = self.CurrentWorldId; request.MapId = self.CurrentWorldMapId; request.UnitId = self.CurrentWorldUnitId; request.PlayerActorId = self.GetActorId(); request.ConnectionState = state;
                using W2P_SetWorldConnection _ = await self.Root().GetComponent<MessageSender>().Call(actor, request, false) as W2P_SetWorldConnection;
            }
            catch (Exception e) { Log.Warning($"online world connection sync failed playerId={self.PlayerId} error={e.GetType().Name}"); }
        }

        public static async ETTask ReceiveWorldEntered(this OnlinePlayerActor self, W2P_WorldPlayerEntered message)
        {
            if (message.PlayerId != self.PlayerId || message.Player == null || !string.Equals(message.Player.MapId, self.CurrentWorldMapId, StringComparison.Ordinal) || self.GateProxyActorId == default || self.State != OnlinePlayerState.Online) return;
            P2G_WorldPlayerEntered push = P2G_WorldPlayerEntered.Create(); push.PlayerId = self.PlayerId; push.SessionEpoch = self.SessionEpoch; push.WorldRevision = message.WorldRevision; push.Player = OnlineWorldSnapshotHelper.Clone(message.Player);
            self.Root().GetComponent<MessageSender>().Send(self.GateProxyActorId, push); await ETTask.CompletedTask;
        }

        public static async ETTask ReceiveWorldTransform(this OnlinePlayerActor self, W2P_WorldPlayerTransformChanged message)
        {
            if (message.PlayerId != self.PlayerId || !string.Equals(message.MapId, self.CurrentWorldMapId, StringComparison.Ordinal) || self.GateProxyActorId == default || self.State != OnlinePlayerState.Online) return;
            P2G_WorldPlayerTransformChanged push = P2G_WorldPlayerTransformChanged.Create(); push.PlayerId = self.PlayerId; push.SessionEpoch = self.SessionEpoch; push.WorldId = message.WorldId; push.MapId = message.MapId; push.WorldRevision = message.WorldRevision; push.Player = OnlineWorldSnapshotHelper.Clone(message.Player);
            self.Root().GetComponent<MessageSender>().Send(self.GateProxyActorId, push); await ETTask.CompletedTask;
        }

        public static async ETTask ReceiveWorldLeft(this OnlinePlayerActor self, W2P_WorldPlayerLeft message)
        {
            if (message.PlayerId != self.PlayerId || !string.Equals(message.MapId, self.CurrentWorldMapId, StringComparison.Ordinal) || self.GateProxyActorId == default || self.State != OnlinePlayerState.Online) return;
            P2G_WorldPlayerLeft push = P2G_WorldPlayerLeft.Create(); push.PlayerId = self.PlayerId; push.SessionEpoch = self.SessionEpoch; push.WorldId = message.WorldId; push.MapId = message.MapId; push.WorldRevision = message.WorldRevision; push.UnitId = message.UnitId;
            self.Root().GetComponent<MessageSender>().Send(self.GateProxyActorId, push); await ETTask.CompletedTask;
        }

        public static async ETTask ReceiveWorldCombat(this OnlinePlayerActor self,W2P_WorldCombatChanged m)
        {if(m.PlayerId!=self.PlayerId||!string.Equals(m.MapId,self.CurrentWorldMapId,StringComparison.Ordinal)||self.GateProxyActorId==default||self.State!=OnlinePlayerState.Online)return;P2G_WorldCombatChanged p=P2G_WorldCombatChanged.Create();p.PlayerId=self.PlayerId;p.SessionEpoch=self.SessionEpoch;p.WorldId=m.WorldId;p.MapId=m.MapId;p.WorldRevision=m.WorldRevision;p.ServerTime=m.ServerTime;p.CombatSequence=m.CombatSequence;p.ActionKind=m.ActionKind;p.ActorUnitId=m.ActorUnitId;p.WeaponItemId=m.WeaponItemId;foreach(long x in m.TargetUnitIds)p.TargetUnitIds.Add(x);foreach(WorldPlayerInfo x in m.Players)p.Players.Add(OnlineWorldSnapshotHelper.Clone(x));foreach(WorldEnemyInfo x in m.Enemies)p.Enemies.Add(OnlineWorldSnapshotHelper.Clone(x));self.Root().GetComponent<MessageSender>().Send(self.GateProxyActorId,p);await ETTask.CompletedTask;}

        public static async ETTask ReceiveEnemyTransforms(this OnlinePlayerActor self,W2P_WorldEnemiesTransformChanged m)
        {if(m.PlayerId!=self.PlayerId||!string.Equals(m.MapId,self.CurrentWorldMapId,StringComparison.Ordinal)||self.GateProxyActorId==default||self.State!=OnlinePlayerState.Online)return;P2G_WorldEnemiesTransformChanged p=P2G_WorldEnemiesTransformChanged.Create();p.PlayerId=self.PlayerId;p.SessionEpoch=self.SessionEpoch;p.WorldId=m.WorldId;p.MapId=m.MapId;p.WorldRevision=m.WorldRevision;foreach(WorldEnemyTransformInfo x in m.Enemies)p.Enemies.Add(OnlineWorldSnapshotHelper.Clone(x));self.Root().GetComponent<MessageSender>().Send(self.GateProxyActorId,p);await ETTask.CompletedTask;}

        public static async ETTask ReceiveNavigationLease(this OnlinePlayerActor self,W2P_WorldNavigationLeaseChanged m)
        {if(m.PlayerId!=self.PlayerId||!string.Equals(m.MapId,self.CurrentWorldMapId,StringComparison.Ordinal)||self.GateProxyActorId==default||self.State!=OnlinePlayerState.Online)return;P2G_WorldNavigationLeaseChanged p=P2G_WorldNavigationLeaseChanged.Create();p.PlayerId=self.PlayerId;p.SessionEpoch=self.SessionEpoch;p.WorldId=m.WorldId;p.MapId=m.MapId;p.WorldRevision=m.WorldRevision;p.NavigationExecutorUnitId=m.NavigationExecutorUnitId;p.NavigationLeaseRevision=m.NavigationLeaseRevision;p.ServerTime=m.ServerTime;self.Root().GetComponent<MessageSender>().Send(self.GateProxyActorId,p);await ETTask.CompletedTask;}

        public static async ETTask ReceiveWorldCorpse(this OnlinePlayerActor self,W2P_WorldCorpseChanged m)
        {if(m.PlayerId!=self.PlayerId||!string.Equals(m.MapId,self.CurrentWorldMapId,StringComparison.Ordinal)||self.GateProxyActorId==default||self.State!=OnlinePlayerState.Online)return;P2G_WorldCorpseChanged p=P2G_WorldCorpseChanged.Create();p.PlayerId=self.PlayerId;p.SessionEpoch=self.SessionEpoch;p.WorldId=m.WorldId;p.MapId=m.MapId;p.WorldRevision=m.WorldRevision;p.HasCorpse=m.HasCorpse;p.Corpse=OnlineWorldSnapshotHelper.Clone(m.Corpse);p.RemovedCorpseId=m.RemovedCorpseId;self.Root().GetComponent<MessageSender>().Send(self.GateProxyActorId,p);await ETTask.CompletedTask;}

        public static async ETTask ReceiveSelfInventory(this OnlinePlayerActor self,W2P_WorldSelfInventoryChanged m)
        {if(m.PlayerId!=self.PlayerId||!string.Equals(m.MapId,self.CurrentWorldMapId,StringComparison.Ordinal))return;self.OnlineInventory=OnlineInventoryRecordHelper.FromMessage(m.SelfInventory);if(self.GateProxyActorId!=default&&self.State==OnlinePlayerState.Online){P2G_WorldSelfInventoryChanged p=P2G_WorldSelfInventoryChanged.Create();p.PlayerId=self.PlayerId;p.SessionEpoch=self.SessionEpoch;p.WorldId=m.WorldId;p.MapId=m.MapId;p.SelfInventory=OnlineWorldSnapshotHelper.Clone(m.SelfInventory);self.Root().GetComponent<MessageSender>().Send(self.GateProxyActorId,p);}await ETTask.CompletedTask;}

        public static async ETTask ReceiveSelfUnit(this OnlinePlayerActor self,W2P_WorldSelfUnitChanged m)
        {if(m.PlayerId!=self.PlayerId||!string.Equals(m.MapId,self.CurrentWorldMapId,StringComparison.Ordinal))return;self.CurrentWorldUnitId=m.SelfUnitId;if(self.GateProxyActorId!=default&&self.State==OnlinePlayerState.Online){P2G_WorldSelfUnitChanged p=P2G_WorldSelfUnitChanged.Create();p.PlayerId=self.PlayerId;p.SessionEpoch=self.SessionEpoch;p.WorldId=m.WorldId;p.MapId=m.MapId;p.PreviousUnitId=m.PreviousUnitId;p.SelfUnitId=m.SelfUnitId;p.WorldRevision=m.WorldRevision;self.Root().GetComponent<MessageSender>().Send(self.GateProxyActorId,p);}await ETTask.CompletedTask;}

        public static async ETTask ReceiveTemporaryBuilding(this OnlinePlayerActor self,W2P_WorldTemporaryBuildingChanged m)
        {if(m.PlayerId!=self.PlayerId||!string.Equals(m.MapId,self.CurrentWorldMapId,StringComparison.Ordinal)||self.GateProxyActorId==default||self.State!=OnlinePlayerState.Online)return;P2G_WorldTemporaryBuildingChanged p=P2G_WorldTemporaryBuildingChanged.Create();p.PlayerId=self.PlayerId;p.SessionEpoch=self.SessionEpoch;p.WorldId=m.WorldId;p.MapId=m.MapId;p.WorldRevision=m.WorldRevision;p.HasBuilding=m.HasBuilding;p.Building=OnlineWorldSnapshotHelper.Clone(m.Building);p.RemovedRuntimeId=m.RemovedRuntimeId;p.ServerTime=m.ServerTime;self.Root().GetComponent<MessageSender>().Send(self.GateProxyActorId,p);await ETTask.CompletedTask;}

        public static async ETTask ReceiveInventoryCheckpoint(this OnlinePlayerActor self,W2P_WorldInventoryCheckpoint m)
        {if(m.PlayerId!=self.PlayerId||!string.Equals(m.MapId,self.CurrentWorldMapId,StringComparison.Ordinal))return;OnlineInventoryRecord candidate=OnlineInventoryRecordHelper.FromMessage(m.OnlineInventory);if(self.OnlineInventory!=null&&candidate.Revision<self.OnlineInventory.Revision)return;self.OnlineInventory=candidate;self.PendingProfileSaveOperationId=m.OperationId;try{await self.SaveProfileCandidate(m.OperationId,candidate,self.HasReceivedOnlineStarterSupplies);}catch(Exception e){Log.Warning($"online death checkpoint save pending playerId={self.PlayerId} error={e.GetType().Name}");self.QueueProfileRetry();}}

        private static void QueueProfileRetry(this OnlinePlayerActor self){if(self.SaveRetryPending)return;self.SaveRetryPending=true;RetrySaveLater(self,self.SessionEpoch).Coroutine();}
    }
}
