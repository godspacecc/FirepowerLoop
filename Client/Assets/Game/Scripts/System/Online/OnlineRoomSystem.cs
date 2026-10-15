using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using ET;
using Game.Network;
using QFramework;

namespace Game
{
    public sealed class OnlineRoomSystem : AbstractSystem
    {
        private const int RpcTimeoutMilliseconds = 10000;
        private static readonly HashSet<char> RoomCodeAlphabet = new HashSet<char>("23456789ABCDEFGHJKLMNPQRSTUVWXYZ");
        private INetworkRuntime network;
        private IDisposable disconnectedSubscription;
        private IDisposable roomChangedSubscription;
        private CancellationTokenSource operationCancellation;
        private int operationGeneration;
        // System 持有当前公开列表请求的取消源和代次：刷新可替换它，加载更多只能在空闲时取得它；
        // 完成、替换、账号意图结束或释放时由同一 System 回收，旧代次因此不能再发布快照。
        private CancellationTokenSource publicRoomListCancellation;
        private int publicRoomListGeneration;
        private string pendingCreateOperationId;
        private string pendingMatchOperationId;
        private string pendingJoinOperationId;
        private string pendingLeaveOperationId;
        private long authenticatedAccountId;
        private long authenticatedPlayerId;
        private long authenticatedSessionEpoch;
        private int accountGeneration;
        private bool accountAllowsRoomOperations;
        private OnlineRoomSnapshot lastSnapshot = OnlineRoomSnapshot.Empty();
        private OnlinePublicRoomListSnapshot publicRooms = OnlinePublicRoomListSnapshot.Empty();

        public OnlineRoomSnapshot Snapshot => lastSnapshot;
        public OnlinePublicRoomListSnapshot PublicRooms => publicRooms;

        protected override void OnInit()
        {
            network = this.GetUtility<INetworkRuntime>();
            Publish(OnlineRoomSnapshot.Empty());
            PublishPublicRooms(OnlinePublicRoomListSnapshot.Empty());
            if (network != null)
            {
                disconnectedSubscription = network.RegisterDisconnectedHandler(_ => OnNetworkDisconnected());
                roomChangedSubscription = network.RegisterPushHandler<G2C_RoomChanged>(OnRoomChanged);
            }

            this.RegisterEvent<OnlineAccountChangedEvent>(OnAccountChanged);
        }

        protected override void OnDeinit()
        {
            CancelOperation();
            CancelPublicRoomListOperation();
            roomChangedSubscription?.Dispose();
            disconnectedSubscription?.Dispose();
            roomChangedSubscription = null;
            disconnectedSubscription = null;
        }

        public Task<OnlineRoomOperationResult> CreateRoomAsync()
        {
            return CreateRoomAsync(OnlineRoomPurpose.General, OnlineRoomVisibility.Public, string.Empty, string.Empty);
        }

        /// <summary>创建私有双人街机房；版本身份只用于房间兼容性，战斗状态仍由后续街机 Actor 提供。</summary>
        public Task<OnlineRoomOperationResult> CreateArcadeRoomAsync(string clientVersion, string arcadeConfigFingerprint)
        {
            if (!IsValidArcadeIdentity(clientVersion, arcadeConfigFingerprint))
                return RejectClientInput();
            return CreateRoomAsync(OnlineRoomPurpose.Arcade, OnlineRoomVisibility.Private, clientVersion,
                arcadeConfigFingerprint);
        }

        /// <summary>请求服务器原子匹配同版本、同配置指纹的街机队友；无候选时服务端创建等待房。</summary>
        public Task<OnlineRoomOperationResult> MatchArcadeRoomAsync(string clientVersion,
            string arcadeConfigFingerprint)
        {
            if (!IsValidArcadeIdentity(clientVersion, arcadeConfigFingerprint))
                return RejectClientInput();

            return RunRoomOperation<G2C_MatchArcadeRoom>(OnlineRoomOperationState.Matching, () =>
            {
                pendingMatchOperationId ??= NewOperationId();
                C2G_MatchArcadeRoom request = C2G_MatchArcadeRoom.Create();
                request.OperationId = pendingMatchOperationId;
                request.ClientVersion = clientVersion;
                request.ArcadeConfigFingerprint = arcadeConfigFingerprint;
                return request;
            }, response =>
            {
                var typed = (G2C_MatchArcadeRoom)response;
                OnlineRoomResultCode code = ApplyRoomResponse(typed.Error, typed.HasRoom, typed.Room);
                if (code != OnlineRoomResultCode.MembershipPending)
                {
                    pendingMatchOperationId = null;
                }
                return code;
            }, "MatchArcadeRoom", () => pendingMatchOperationId = null);
        }

        private Task<OnlineRoomOperationResult> CreateRoomAsync(OnlineRoomPurpose purpose,
            OnlineRoomVisibility visibility, string clientVersion, string arcadeConfigFingerprint)
        {
            return RunRoomOperation<G2C_CreateRoom>(OnlineRoomOperationState.Creating, () =>
            {
                pendingCreateOperationId ??= NewOperationId();
                C2G_CreateRoom request = C2G_CreateRoom.Create();
                request.OperationId = pendingCreateOperationId;
                request.Purpose = (int)purpose;
                request.Visibility = (int)visibility;
                request.ClientVersion = clientVersion ?? string.Empty;
                request.ArcadeConfigFingerprint = arcadeConfigFingerprint ?? string.Empty;
                return request;
            }, response =>
            {
                var typed = (G2C_CreateRoom)response;
                OnlineRoomResultCode code = ApplyRoomResponse(typed.Error, typed.HasRoom, typed.Room);
                if (code != OnlineRoomResultCode.MembershipPending)
                {
                    pendingCreateOperationId = null;
                }
                return code;
            }, "CreateRoom", () => pendingCreateOperationId = null);
        }

        public Task<OnlineRoomOperationResult> JoinRoomAsync(string roomCode)
        {
            return JoinRoomAsync(roomCode, OnlineRoomPurpose.General, string.Empty, string.Empty);
        }

        /// <summary>按房间码加入街机房；用途或版本不一致由服务端在提交成员前拒绝。</summary>
        public Task<OnlineRoomOperationResult> JoinArcadeRoomAsync(string roomCode, string clientVersion,
            string arcadeConfigFingerprint)
        {
            if (!IsValidArcadeIdentity(clientVersion, arcadeConfigFingerprint))
                return RejectClientInput();
            return JoinRoomAsync(roomCode, OnlineRoomPurpose.Arcade, clientVersion, arcadeConfigFingerprint);
        }

        private Task<OnlineRoomOperationResult> JoinRoomAsync(string roomCode, OnlineRoomPurpose expectedPurpose,
            string clientVersion, string arcadeConfigFingerprint)
        {
            string normalized = NormalizeRoomCode(roomCode);
            if (!IsValidRoomCode(normalized))
            {
                Publish(lastSnapshot.WithOperation(OnlineRoomOperationState.Idle, OnlineRoomResultCode.ClientInputInvalid));
                return Task.FromResult(new OnlineRoomOperationResult(OnlineRoomResultCode.ClientInputInvalid, lastSnapshot));
            }

            return RunRoomOperation<G2C_JoinRoom>(OnlineRoomOperationState.Joining, () =>
            {
                pendingJoinOperationId ??= NewOperationId();
                C2G_JoinRoom request = C2G_JoinRoom.Create();
                request.OperationId = pendingJoinOperationId;
                request.RoomCode = normalized;
                request.ExpectedPurpose = (int)expectedPurpose;
                request.ClientVersion = clientVersion ?? string.Empty;
                request.ArcadeConfigFingerprint = arcadeConfigFingerprint ?? string.Empty;
                return request;
            }, response =>
            {
                var typed = (G2C_JoinRoom)response;
                OnlineRoomResultCode code = ApplyRoomResponse(typed.Error, typed.HasRoom, typed.Room);
                if (code != OnlineRoomResultCode.MembershipPending)
                {
                    pendingJoinOperationId = null;
                }
                return code;
            }, "JoinRoom", () => pendingJoinOperationId = null);
        }

        public async Task<OnlineRoomOperationResult> LeaveRoomAsync()
        {
            if (lastSnapshot.OperationState != OnlineRoomOperationState.Idle)
            {
                return new OnlineRoomOperationResult(OnlineRoomResultCode.OperationInProgress, lastSnapshot);
            }

            int generation = StartOperation();
            CancellationToken token = operationCancellation.Token;
            Publish(lastSnapshot.WithOperation(OnlineRoomOperationState.Leaving, OnlineRoomResultCode.None));
            OnlineRoomResultCode code;
            try
            {
                pendingLeaveOperationId ??= NewOperationId();
                C2G_LeaveRoom request = C2G_LeaveRoom.Create();
                request.OperationId = pendingLeaveOperationId;
                G2C_LeaveRoom response = await network.CallAsync<G2C_LeaveRoom>(request, RpcTimeoutMilliseconds, token);
                if (generation != operationGeneration)
                {
                    return new OnlineRoomOperationResult(OnlineRoomResultCode.OperationInProgress, lastSnapshot);
                }

                pendingLeaveOperationId = null;
                code = MapServerError(response.Error);
                if (code == OnlineRoomResultCode.Success || code == OnlineRoomResultCode.NotInRoom)
                {
                    Publish(OnlineRoomSnapshot.Empty(code == OnlineRoomResultCode.NotInRoom ? OnlineRoomResultCode.NotInRoom : OnlineRoomResultCode.Success));
                }
                else
                {
                    Publish(lastSnapshot.WithOperation(OnlineRoomOperationState.Idle, code));
                }
            }
            catch (TimeoutException)
            {
                code = OnlineRoomResultCode.Timeout;
                if (generation == operationGeneration)
                {
                    Publish(lastSnapshot.WithOperation(OnlineRoomOperationState.Idle, code));
                }
            }
            catch (OperationCanceledException)
            {
                code = OnlineRoomResultCode.OperationInProgress;
            }
            catch (Exception exception)
            {
                code = network == null || network.ConnectionState != NetworkConnectionState.Connected
                    ? OnlineRoomResultCode.Disconnected
                    : OnlineRoomResultCode.UnknownFailure;
                if (generation == operationGeneration)
                {
                    if (code == OnlineRoomResultCode.UnknownFailure)
                    {
                        pendingLeaveOperationId = null;
                        Log.Error($"[OnlineRoomSystem] 操作失败 stage=LeaveRoom exception={exception}");
                    }

                    Publish(lastSnapshot.WithOperation(OnlineRoomOperationState.Idle, code));
                }
            }
            finally
            {
                EndOperation(generation);
            }

            return new OnlineRoomOperationResult(code, lastSnapshot);
        }

        public Task<OnlineRoomOperationResult> RestoreCurrentRoomAsync()
        {
            return RestoreCurrentRoomAsync(accountGeneration);
        }

        // 刷新拥有独立 generation：它会取消并替换任何列表请求，旧响应只能返回忙碌结果，不能发布状态。
        // 失败保留已有行和游标；断线快照只有成功刷新后才解除加入入口的过期阻断。
        public Task<OnlinePublicRoomListResult> RefreshPublicRoomsAsync()
        {
            return RunPublicRoomListRequest(true);
        }

        // 加载更多不会抢占刷新或另一个加载请求；无下一页时不发 RPC，也不改写最近操作来源。
        public Task<OnlinePublicRoomListResult> LoadMorePublicRoomsAsync()
        {
            if (publicRooms.State != OnlinePublicRoomListState.Idle)
            {
                return Task.FromResult(new OnlinePublicRoomListResult(OnlineRoomResultCode.OperationInProgress, publicRooms));
            }

            if (publicRooms.LastResultCode == OnlineRoomResultCode.Disconnected)
            {
                return Task.FromResult(new OnlinePublicRoomListResult(OnlineRoomResultCode.Disconnected, publicRooms));
            }

            if (!publicRooms.HasMore || publicRooms.NextCursorRoomId == 0)
            {
                return Task.FromResult(new OnlinePublicRoomListResult(OnlineRoomResultCode.Success, publicRooms));
            }

            return RunPublicRoomListRequest(false);
        }

        private async Task<OnlinePublicRoomListResult> RunPublicRoomListRequest(bool refresh)
        {
            if (!refresh && publicRooms.State != OnlinePublicRoomListState.Idle)
            {
                return new OnlinePublicRoomListResult(OnlineRoomResultCode.OperationInProgress, publicRooms);
            }

            int accountGenerationAtStart = accountGeneration;
            int generation;
            if (refresh)
            {
                CancelPublicRoomListOperation();
                publicRoomListCancellation = new CancellationTokenSource();
                generation = ++publicRoomListGeneration;
            }
            else
            {
                publicRoomListCancellation = new CancellationTokenSource();
                generation = ++publicRoomListGeneration;
            }

            OnlinePublicRoomListSnapshot before = publicRooms;
            OnlinePublicRoomListOperation operation = refresh
                ? OnlinePublicRoomListOperation.Refresh
                : OnlinePublicRoomListOperation.LoadMore;
            OnlineRoomResultCode pendingResult = refresh && before.LastResultCode == OnlineRoomResultCode.Disconnected
                ? OnlineRoomResultCode.Disconnected
                : OnlineRoomResultCode.None;
            PublishPublicRooms(new OnlinePublicRoomListSnapshot(
                before.Items,
                before.NextCursorRoomId,
                before.HasMore,
                refresh ? OnlinePublicRoomListState.Refreshing : OnlinePublicRoomListState.LoadingMore,
                operation,
                pendingResult));

            OnlineRoomResultCode code;
            try
            {
                if (!IsAccountGenerationCurrent(accountGenerationAtStart) || network == null ||
                    network.ConnectionState != NetworkConnectionState.Connected)
                {
                    code = OnlineRoomResultCode.Disconnected;
                    PublishPublicRoomListFailure(generation, before, operation, code);
                    return new OnlinePublicRoomListResult(code, publicRooms);
                }

                C2G_GetPublicRoomList request = C2G_GetPublicRoomList.Create();
                request.CursorRoomId = refresh ? 0 : before.NextCursorRoomId;
                G2C_GetPublicRoomList response = await network.CallAsync<G2C_GetPublicRoomList>(
                    request, RpcTimeoutMilliseconds, publicRoomListCancellation.Token);
                if (generation != publicRoomListGeneration)
                {
                    return new OnlinePublicRoomListResult(OnlineRoomResultCode.OperationInProgress, publicRooms);
                }

                if (!IsAccountGenerationCurrent(accountGenerationAtStart))
                {
                    return new OnlinePublicRoomListResult(OnlineRoomResultCode.Disconnected, publicRooms);
                }

                code = MapServerError(response.Error);
                if (code != OnlineRoomResultCode.Success)
                {
                    PublishPublicRoomListFailure(generation, before, operation, code);
                    return new OnlinePublicRoomListResult(code, publicRooms);
                }

                IReadOnlyList<OnlinePublicRoomItem> incoming = CopyPublicRooms(response.Rooms);
                IReadOnlyList<OnlinePublicRoomItem> items = refresh
                    ? incoming
                    : AppendDistinctPublicRooms(before.Items, incoming);
                PublishPublicRooms(new OnlinePublicRoomListSnapshot(
                    items,
                    response.NextCursorRoomId,
                    response.HasMore,
                    OnlinePublicRoomListState.Idle,
                    operation,
                    OnlineRoomResultCode.Success));
                return new OnlinePublicRoomListResult(code, publicRooms);
            }
            catch (TimeoutException)
            {
                code = OnlineRoomResultCode.Timeout;
                PublishPublicRoomListFailure(generation, before, operation, code);
            }
            catch (OperationCanceledException)
            {
                code = OnlineRoomResultCode.OperationInProgress;
            }
            catch (Exception exception)
            {
                code = network == null || network.ConnectionState != NetworkConnectionState.Connected
                    ? OnlineRoomResultCode.Disconnected
                    : OnlineRoomResultCode.UnknownFailure;
                if (generation == publicRoomListGeneration && IsAccountGenerationCurrent(accountGenerationAtStart))
                {
                    if (code == OnlineRoomResultCode.UnknownFailure)
                    {
                        Log.Error($"[OnlineRoomSystem] 公开房间列表失败 stage={operation} exception={exception}");
                    }

                    PublishPublicRoomListFailure(generation, before, operation, code);
                }
            }
            finally
            {
                EndPublicRoomListOperation(generation);
            }

            return new OnlinePublicRoomListResult(code, publicRooms);
        }

        private void PublishPublicRoomListFailure(
            int generation,
            OnlinePublicRoomListSnapshot before,
            OnlinePublicRoomListOperation operation,
            OnlineRoomResultCode code)
        {
            if (generation != publicRoomListGeneration)
            {
                return;
            }

            OnlineRoomResultCode snapshotCode = operation == OnlinePublicRoomListOperation.Refresh &&
                                                before.LastResultCode == OnlineRoomResultCode.Disconnected
                ? OnlineRoomResultCode.Disconnected
                : code;
            PublishPublicRooms(new OnlinePublicRoomListSnapshot(
                before.Items,
                before.NextCursorRoomId,
                before.HasMore,
                OnlinePublicRoomListState.Idle,
                operation,
                snapshotCode));
        }

        private static IReadOnlyList<OnlinePublicRoomItem> CopyPublicRooms(IReadOnlyList<PublicRoomSummary> rooms)
        {
            if (rooms == null || rooms.Count == 0)
            {
                return Array.Empty<OnlinePublicRoomItem>();
            }

            var copy = new OnlinePublicRoomItem[rooms.Count];
            for (int i = 0; i < rooms.Count; i++)
            {
                PublicRoomSummary room = rooms[i];
                copy[i] = new OnlinePublicRoomItem(
                    NormalizeRoomCode(room.RoomCode), room.OwnerDisplayName, room.MemberCount, room.Capacity);
            }

            return Array.AsReadOnly(copy);
        }

        private static IReadOnlyList<OnlinePublicRoomItem> AppendDistinctPublicRooms(
            IReadOnlyList<OnlinePublicRoomItem> existing,
            IReadOnlyList<OnlinePublicRoomItem> incoming)
        {
            var merged = new List<OnlinePublicRoomItem>(existing.Count + incoming.Count);
            var roomCodes = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < existing.Count; i++)
            {
                OnlinePublicRoomItem item = existing[i];
                merged.Add(item);
                roomCodes.Add(NormalizeRoomCode(item.RoomCode));
            }

            for (int i = 0; i < incoming.Count; i++)
            {
                OnlinePublicRoomItem item = incoming[i];
                if (roomCodes.Add(NormalizeRoomCode(item.RoomCode)))
                {
                    merged.Add(item);
                }
            }

            return merged;
        }

        private Task<OnlineRoomOperationResult> RestoreCurrentRoomAsync(int observedAccountGeneration)
        {
            return RunRoomOperation<G2C_GetCurrentRoom>(OnlineRoomOperationState.Restoring, () => C2G_GetCurrentRoom.Create(), response =>
            {
                var typed = (G2C_GetCurrentRoom)response;
                return ApplyRoomResponse(typed.Error, typed.HasRoom, typed.Room);
            }, "GetCurrentRoom", null, observedAccountGeneration);
        }

        public static string NormalizeRoomCode(string roomCode)
        {
            return (roomCode ?? string.Empty).Trim().ToUpperInvariant();
        }

        public static bool IsValidRoomCode(string roomCode)
        {
            string normalized = NormalizeRoomCode(roomCode);
            if (normalized.Length != 6)
            {
                return false;
            }

            for (int i = 0; i < normalized.Length; i++)
            {
                if (!RoomCodeAlphabet.Contains(normalized[i]))
                {
                    return false;
                }
            }

            return true;
        }

        internal static OnlineRoomResultCode MapServerError(int error)
        {
            switch (error)
            {
                case 0: return OnlineRoomResultCode.Success;
                case 200105: return OnlineRoomResultCode.NotLoggedIn;
                case 200109: return OnlineRoomResultCode.RoomCodeInvalid;
                case 200110: return OnlineRoomResultCode.RoomNotFound;
                case 200111: return OnlineRoomResultCode.RoomFull;
                case 200112: return OnlineRoomResultCode.AlreadyInRoom;
                case 200113: return OnlineRoomResultCode.NotInRoom;
                case 200114: return OnlineRoomResultCode.OperationInvalid;
                case 200115: return OnlineRoomResultCode.RateLimited;
                case 200116: return OnlineRoomResultCode.RoomClosing;
                case 200119: return OnlineRoomResultCode.MembershipPending;
                case 200122: return OnlineRoomResultCode.OperationInvalid;
                case 200133: return OnlineRoomResultCode.PurposeMismatch;
                case 200134: return OnlineRoomResultCode.VersionMismatch;
                case 200118:
                case 200117:
                case 200107: return OnlineRoomResultCode.ServiceUnavailable;
                default: return OnlineRoomResultCode.UnknownFailure;
            }
        }

        private async Task<OnlineRoomOperationResult> RunRoomOperation<TResponse>(
            OnlineRoomOperationState operation,
            Func<IRequest> requestFactory,
            Func<IResponse, OnlineRoomResultCode> apply,
            string stage,
            Action clearPendingOperationId,
            int observedAccountGeneration = -1)
            where TResponse : class, IResponse
        {
            int accountGenerationAtStart = observedAccountGeneration >= 0 ? observedAccountGeneration : accountGeneration;
            if (lastSnapshot.OperationState != OnlineRoomOperationState.Idle)
            {
                return new OnlineRoomOperationResult(OnlineRoomResultCode.OperationInProgress, lastSnapshot);
            }

            if (!IsAccountGenerationCurrent(accountGenerationAtStart))
            {
                return new OnlineRoomOperationResult(OnlineRoomResultCode.Disconnected, lastSnapshot);
            }

            if (network == null || network.ConnectionState != NetworkConnectionState.Connected)
            {
                Publish(lastSnapshot.WithOperation(OnlineRoomOperationState.Idle, OnlineRoomResultCode.Disconnected));
                return new OnlineRoomOperationResult(OnlineRoomResultCode.Disconnected, lastSnapshot);
            }

            int generation = StartOperation();
            CancellationToken token = operationCancellation.Token;
            Publish(lastSnapshot.WithOperation(operation, OnlineRoomResultCode.None));
            OnlineRoomResultCode code;
            try
            {
                TResponse response = await network.CallAsync<TResponse>(requestFactory(), RpcTimeoutMilliseconds, token);
                if (generation != operationGeneration)
                {
                    return new OnlineRoomOperationResult(OnlineRoomResultCode.OperationInProgress, lastSnapshot);
                }

                if (!IsAccountGenerationCurrent(accountGenerationAtStart))
                {
                    code = OnlineRoomResultCode.Disconnected;
                    return new OnlineRoomOperationResult(code, lastSnapshot);
                }

                code = apply(response);
            }
            catch (TimeoutException)
            {
                bool accountCurrent = IsAccountGenerationCurrent(accountGenerationAtStart);
                code = accountCurrent ? OnlineRoomResultCode.Timeout : OnlineRoomResultCode.Disconnected;
                if (generation == operationGeneration && accountCurrent)
                {
                    Publish(lastSnapshot.WithOperation(OnlineRoomOperationState.Idle, code));
                }
            }
            catch (OperationCanceledException)
            {
                code = OnlineRoomResultCode.OperationInProgress;
            }
            catch (Exception exception)
            {
                code = network == null || network.ConnectionState != NetworkConnectionState.Connected
                    ? OnlineRoomResultCode.Disconnected
                    : OnlineRoomResultCode.UnknownFailure;
                bool accountCurrent = IsAccountGenerationCurrent(accountGenerationAtStart);
                if (generation == operationGeneration && accountCurrent)
                {
                    if (code == OnlineRoomResultCode.UnknownFailure)
                    {
                        clearPendingOperationId?.Invoke();
                        Log.Error($"[OnlineRoomSystem] 操作失败 stage={stage} exception={exception}");
                    }

                    Publish(lastSnapshot.WithOperation(OnlineRoomOperationState.Idle, code));
                }
            }
            finally
            {
                EndOperation(generation);
            }

            return new OnlineRoomOperationResult(code, lastSnapshot);
        }

        private OnlineRoomResultCode ApplyRoomResponse(int error, bool hasRoom, ET.OnlineRoomSnapshot room)
        {
            OnlineRoomResultCode code = MapServerError(error);
            if (code == OnlineRoomResultCode.Success)
            {
                OnlineRoomSnapshot snapshot = hasRoom ? CopySnapshot(room, false, OnlineRoomOperationState.Idle, code) : OnlineRoomSnapshot.Empty(OnlineRoomResultCode.Success);
                Publish(snapshot);
            }
            else if (code == OnlineRoomResultCode.MembershipPending && hasRoom && room != null)
            {
                Publish(CopySnapshot(room, false, OnlineRoomOperationState.Idle, code));
            }
            else if (code == OnlineRoomResultCode.ServiceUnavailable)
            {
                Publish(lastSnapshot.WithOperation(OnlineRoomOperationState.Idle, code));
            }
            else
            {
                Publish(lastSnapshot.WithOperation(OnlineRoomOperationState.Idle, code));
            }

            return code;
        }

        private void OnRoomChanged(G2C_RoomChanged message)
        {
            if (!accountAllowsRoomOperations)
            {
                return;
            }

            if (!message.HasRoom)
            {
                Publish(OnlineRoomSnapshot.Empty(OnlineRoomResultCode.Success));
                return;
            }

            OnlineRoomSnapshot incoming = CopySnapshot(message.Room, false, lastSnapshot.OperationState, lastSnapshot.LastResultCode);
            if (incoming.RoomId == lastSnapshot.RoomId && incoming.MembershipRevision <= lastSnapshot.MembershipRevision)
            {
                return;
            }

            Publish(incoming);
        }

        private void OnNetworkDisconnected()
        {
            CancelOperation();
            // 普通传输断线保留可见旧列表并标为过期，供玩家重连后辨识；主动登出/账号意图结束则由
            // EndAccountIntent 清空列表，防止上一账号的房间数据跨账号保留。
            MarkPublicRoomsDisconnected();
            if (lastSnapshot.HasRoom)
            {
                Publish(lastSnapshot.WithConnectionLost(true).WithOperation(OnlineRoomOperationState.Idle, OnlineRoomResultCode.Disconnected));
            }
        }

        private void OnAccountChanged(OnlineAccountChangedEvent evt)
        {
            if (IsAccountReadyForRoomOperations(evt.Snapshot))
            {
                if (accountAllowsRoomOperations &&
                    authenticatedAccountId == evt.Snapshot.AccountId &&
                    authenticatedPlayerId == evt.Snapshot.PlayerId &&
                    authenticatedSessionEpoch == evt.Snapshot.SessionEpoch)
                {
                    return;
                }

                int generation = ++accountGeneration;
                CancelPublicRoomListOperation();
                if (authenticatedAccountId != 0 &&
                    (authenticatedAccountId != evt.Snapshot.AccountId || authenticatedPlayerId != evt.Snapshot.PlayerId))
                {
                    EndAccountIntent(OnlineRoomResultCode.Disconnected);
                }

                accountAllowsRoomOperations = true;
                authenticatedAccountId = evt.Snapshot.AccountId;
                authenticatedPlayerId = evt.Snapshot.PlayerId;
                authenticatedSessionEpoch = evt.Snapshot.SessionEpoch;
                ObserveRestoreCurrentRoomAsync(generation).Forget();
            }
            else if (evt.Snapshot.ConnectionState == OnlineConnectionState.Authenticated)
            {
                accountGeneration++;
                accountAllowsRoomOperations = false;
                authenticatedSessionEpoch = 0;
                EndAccountIntent(OnlineRoomResultCode.None);
            }
            else if (evt.Snapshot.ConnectionState == OnlineConnectionState.Disconnected)
            {
                accountGeneration++;
                accountAllowsRoomOperations = false;
                if (evt.Snapshot.LastResultCode == OnlineAccountResultCode.Disconnected)
                {
                    CancelOperation();
                    MarkPublicRoomsDisconnected();
                    if (lastSnapshot.HasRoom)
                    {
                        Publish(lastSnapshot.WithConnectionLost(true)
                            .WithOperation(OnlineRoomOperationState.Idle, OnlineRoomResultCode.Disconnected));
                    }
                    else
                    {
                        Publish(lastSnapshot.WithOperation(OnlineRoomOperationState.Idle, OnlineRoomResultCode.Disconnected));
                    }
                }
                else
                {
                    EndAccountIntent(OnlineRoomResultCode.None);
                }
            }
            else
            {
                accountGeneration++;
                accountAllowsRoomOperations = false;
                authenticatedSessionEpoch = 0;
                EndAccountIntent(OnlineRoomResultCode.None);
            }
        }

        private async UniTask ObserveRestoreCurrentRoomAsync(int observedAccountGeneration)
        {
            try
            {
                await RestoreCurrentRoomAsync(observedAccountGeneration);
                if (!IsAccountGenerationCurrent(observedAccountGeneration))
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                if (!IsAccountGenerationCurrent(observedAccountGeneration))
                {
                    return;
                }

                Log.Error($"[OnlineRoomSystem] 恢复观察失败 stage=RestoreCurrentRoom exception={exception}");
            }
        }

        private void Publish(OnlineRoomSnapshot snapshot)
        {
            lastSnapshot = snapshot;
            this.GetModel<OnlineRoomModel>().Publish(snapshot);
        }

        private void PublishPublicRooms(OnlinePublicRoomListSnapshot snapshot)
        {
            publicRooms = snapshot;
            this.GetModel<OnlineRoomModel>().PublishPublicRooms(snapshot);
        }

        private void MarkPublicRoomsDisconnected()
        {
            CancelPublicRoomListOperation();
            PublishPublicRooms(new OnlinePublicRoomListSnapshot(
                publicRooms.Items,
                publicRooms.NextCursorRoomId,
                publicRooms.HasMore,
                OnlinePublicRoomListState.Idle,
                OnlinePublicRoomListOperation.None,
                OnlineRoomResultCode.Disconnected));
        }

        private int StartOperation()
        {
            CancelOperation();
            operationCancellation = new CancellationTokenSource();
            return ++operationGeneration;
        }

        private void EndOperation(int generation)
        {
            if (generation == operationGeneration)
            {
                operationCancellation?.Dispose();
                operationCancellation = null;
            }
        }

        private void CancelOperation()
        {
            operationCancellation?.Cancel();
            operationCancellation?.Dispose();
            operationCancellation = null;
            operationGeneration++;
        }

        private void EndPublicRoomListOperation(int generation)
        {
            if (generation == publicRoomListGeneration)
            {
                publicRoomListCancellation?.Dispose();
                publicRoomListCancellation = null;
            }
        }

        private void CancelPublicRoomListOperation()
        {
            publicRoomListCancellation?.Cancel();
            publicRoomListCancellation?.Dispose();
            publicRoomListCancellation = null;
            publicRoomListGeneration++;
        }

        private void EndAccountIntent(OnlineRoomResultCode result)
        {
            CancelOperation();
            CancelPublicRoomListOperation();
            pendingCreateOperationId = null;
            pendingMatchOperationId = null;
            pendingJoinOperationId = null;
            pendingLeaveOperationId = null;
            authenticatedAccountId = 0;
            authenticatedPlayerId = 0;
            authenticatedSessionEpoch = 0;
            Publish(OnlineRoomSnapshot.Empty(result));
            PublishPublicRooms(OnlinePublicRoomListSnapshot.Empty());
        }

        private bool IsAccountGenerationCurrent(int observedAccountGeneration)
        {
            return observedAccountGeneration == 0
                ? accountGeneration == 0
                : accountAllowsRoomOperations && observedAccountGeneration == accountGeneration;
        }

        private static bool IsAccountReadyForRoomOperations(OnlineAccountSnapshot snapshot)
        {
            return snapshot.ConnectionState == OnlineConnectionState.Authenticated &&
                   snapshot.OperationState != OnlineOperationState.LoggingOut;
        }

        private static string NewOperationId()
        {
            return Guid.NewGuid().ToString("N");
        }

        private Task<OnlineRoomOperationResult> RejectClientInput()
        {
            Publish(lastSnapshot.WithOperation(OnlineRoomOperationState.Idle, OnlineRoomResultCode.ClientInputInvalid));
            return Task.FromResult(new OnlineRoomOperationResult(OnlineRoomResultCode.ClientInputInvalid, lastSnapshot));
        }

        internal static bool IsValidArcadeIdentity(string clientVersion, string arcadeConfigFingerprint) =>
            !string.IsNullOrWhiteSpace(clientVersion) && clientVersion.Length <= 64 &&
            !string.IsNullOrWhiteSpace(arcadeConfigFingerprint) && arcadeConfigFingerprint.Length <= 128;

        private static OnlineRoomSnapshot CopySnapshot(ET.OnlineRoomSnapshot room, bool connectionLost, OnlineRoomOperationState operation, OnlineRoomResultCode result)
        {
            if (room == null)
            {
                return OnlineRoomSnapshot.Empty(result);
            }

            List<OnlineRoomMemberSnapshot> members = new List<OnlineRoomMemberSnapshot>();
            if (room.Members != null)
            {
                foreach (RoomMemberInfo member in room.Members)
                {
                    OnlineRoomMemberConnectionState state = member.ConnectionState == 2
                        ? OnlineRoomMemberConnectionState.DisconnectedGrace
                        : OnlineRoomMemberConnectionState.Online;
                    members.Add(new OnlineRoomMemberSnapshot(member.PlayerId, member.DisplayName, member.IsOwner, state, member.JoinSequence));
                }
            }

            return new OnlineRoomSnapshot(true, room.RoomId, room.RoomCode, room.OwnerPlayerId,
                room.MembershipRevision, members, connectionLost, operation, result,
                (OnlineRoomPurpose)room.Purpose, (OnlineRoomVisibility)room.Visibility, room.Capacity,
                room.MembershipLocked, room.ClientVersion, room.ArcadeConfigFingerprint);
        }
    }
}
