using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ET;
using Game.Network;
using QFramework;

namespace Game
{
    public sealed class OnlineRoomChatSystem : AbstractSystem
    {
        private const int RpcTimeoutMilliseconds = 10000;
        private const int ClientMessageLimit = 200;
        private INetworkRuntime network;
        private IDisposable disconnectedSubscription;
        private IDisposable messageSubscription;
        private CancellationTokenSource historyCancellation;
        private CancellationTokenSource sendCancellation;
        private int roomGeneration;
        private int historyGeneration;
        private int sendGeneration;
        private bool accountAuthenticated;
        private string pendingClientMessageId;
        private string pendingContent = string.Empty;
        private OnlineRoomChatSnapshot lastSnapshot = OnlineRoomChatSnapshot.Empty();

        public OnlineRoomChatSnapshot Snapshot => lastSnapshot;

        protected override void OnInit()
        {
            network = this.GetUtility<INetworkRuntime>();
            Publish(OnlineRoomChatSnapshot.Empty());
            if (network != null)
            {
                disconnectedSubscription = network.RegisterDisconnectedHandler(_ => OnNetworkDisconnected());
                messageSubscription = network.RegisterPushHandler<G2C_RoomChatMessage>(OnRoomChatMessage);
            }

            this.RegisterEvent<OnlineAccountChangedEvent>(OnAccountChanged);
            this.RegisterEvent<OnlineRoomChangedEvent>(OnRoomChanged);
        }

        protected override void OnDeinit()
        {
            CancelHistory();
            CancelSend();
            messageSubscription?.Dispose();
            disconnectedSubscription?.Dispose();
            messageSubscription = null;
            disconnectedSubscription = null;
        }

        public async Task<OnlineRoomChatSendResult> SendAsync(string content)
        {
            if (!TryNormalizeContent(content, out string normalized))
            {
                Publish(Clone(lastSnapshot, sendState: OnlineRoomChatSendState.Idle,
                    result: OnlineRoomChatResultCode.ClientInputInvalid, pendingContentOverride: content ?? string.Empty));
                return new OnlineRoomChatSendResult(OnlineRoomChatResultCode.ClientInputInvalid, lastSnapshot, false);
            }

            if (lastSnapshot.SendState == OnlineRoomChatSendState.Sending)
            {
                return new OnlineRoomChatSendResult(OnlineRoomChatResultCode.OperationInProgress, lastSnapshot, false);
            }

            if (lastSnapshot.RoomId == 0)
            {
                Publish(Clone(lastSnapshot, result: OnlineRoomChatResultCode.NotInRoom, pendingContentOverride: normalized));
                return new OnlineRoomChatSendResult(OnlineRoomChatResultCode.NotInRoom, lastSnapshot, false);
            }

            if (!accountAuthenticated || network == null || network.ConnectionState != NetworkConnectionState.Connected)
            {
                Publish(Clone(lastSnapshot, result: OnlineRoomChatResultCode.Disconnected, pendingContentOverride: normalized));
                return new OnlineRoomChatSendResult(OnlineRoomChatResultCode.Disconnected, lastSnapshot, false);
            }

            if (pendingClientMessageId == null || pendingContent != normalized)
            {
                pendingClientMessageId = Guid.NewGuid().ToString("N");
            }
            pendingContent = normalized;

            int generation = StartSend();
            int observedRoomGeneration = roomGeneration;
            long roomId = lastSnapshot.RoomId;
            Publish(Clone(lastSnapshot, sendState: OnlineRoomChatSendState.Sending,
                result: OnlineRoomChatResultCode.None, pendingContentOverride: normalized));
            OnlineRoomChatResultCode code;
            bool clearInput = false;
            try
            {
                C2G_SendRoomChat request = C2G_SendRoomChat.Create();
                request.ClientMessageId = pendingClientMessageId;
                request.Content = normalized;
                G2C_SendRoomChat response = await network.CallAsync<G2C_SendRoomChat>(request, RpcTimeoutMilliseconds, sendCancellation.Token);
                if (!IsCurrentRoom(observedRoomGeneration, roomId) || generation != sendGeneration)
                {
                    return new OnlineRoomChatSendResult(OnlineRoomChatResultCode.OperationInProgress, lastSnapshot, false);
                }

                code = MapServerError(response.Error);
                if (code == OnlineRoomChatResultCode.Success && response.Accepted && response.RoomId == roomId)
                {
                    MergeMessage(CopyMessage(response.ChatMessage));
                    pendingClientMessageId = null;
                    pendingContent = string.Empty;
                    clearInput = true;
                    Publish(Clone(lastSnapshot, sendState: OnlineRoomChatSendState.Idle,
                        result: OnlineRoomChatResultCode.Success, pendingContentOverride: string.Empty));
                }
                else
                {
                    pendingClientMessageId = null;
                    Publish(Clone(lastSnapshot, sendState: OnlineRoomChatSendState.Idle,
                        result: code, pendingContentOverride: normalized));
                }
            }
            catch (TimeoutException)
            {
                code = OnlineRoomChatResultCode.Timeout;
                if (generation == sendGeneration && IsCurrentRoom(observedRoomGeneration, roomId))
                {
                    Publish(Clone(lastSnapshot, sendState: OnlineRoomChatSendState.Idle,
                        result: code, pendingContentOverride: normalized));
                }
            }
            catch (OperationCanceledException)
            {
                code = OnlineRoomChatResultCode.OperationInProgress;
            }
            catch (Exception exception)
            {
                code = network == null || network.ConnectionState != NetworkConnectionState.Connected
                    ? OnlineRoomChatResultCode.Disconnected
                    : OnlineRoomChatResultCode.UnknownFailure;
                if (code == OnlineRoomChatResultCode.UnknownFailure)
                {
                    pendingClientMessageId = null;
                    Log.Error($"[OnlineRoomChatSystem] 发送失败 roomId={roomId} exception={exception}");
                }
                if (generation == sendGeneration && IsCurrentRoom(observedRoomGeneration, roomId))
                {
                    Publish(Clone(lastSnapshot, sendState: OnlineRoomChatSendState.Idle,
                        result: code, pendingContentOverride: normalized));
                }
            }
            finally
            {
                EndSend(generation);
            }

            return new OnlineRoomChatSendResult(code, lastSnapshot, clearInput);
        }

        public Task RefreshHistoryAsync()
        {
            return RefreshHistoryAsync(roomGeneration, lastSnapshot.RoomId);
        }

        public static bool TryNormalizeContent(string content, out string normalized)
        {
            normalized = (content ?? string.Empty).Trim();
            if (normalized.Length == 0 || Encoding.UTF8.GetByteCount(normalized) > 800 || normalized.Any(char.IsControl))
            {
                return false;
            }

            TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(normalized);
            int count = 0;
            while (elements.MoveNext())
            {
                if (++count > 200)
                {
                    return false;
                }
            }
            return true;
        }

        internal static OnlineRoomChatResultCode MapServerError(int error)
        {
            switch (error)
            {
                case 0: return OnlineRoomChatResultCode.Success;
                case 200105: return OnlineRoomChatResultCode.NotLoggedIn;
                case 200113: return OnlineRoomChatResultCode.NotInRoom;
                case 200119: return OnlineRoomChatResultCode.MembershipPending;
                case 200120: return OnlineRoomChatResultCode.ClientInputInvalid;
                case 200121: return OnlineRoomChatResultCode.RateLimited;
                case 200114:
                case 200122: return OnlineRoomChatResultCode.OperationConflict;
                case 200107:
                case 200117:
                case 200118: return OnlineRoomChatResultCode.ServiceUnavailable;
                default: return OnlineRoomChatResultCode.UnknownFailure;
            }
        }

        private async Task RefreshHistoryAsync(int observedRoomGeneration, long roomId)
        {
            if (roomId == 0 || !accountAuthenticated || network == null || network.ConnectionState != NetworkConnectionState.Connected)
            {
                return;
            }

            CancelHistory();
            historyCancellation = new CancellationTokenSource();
            int observedHistoryGeneration = historyGeneration;
            CancellationToken token = historyCancellation.Token;
            Publish(Clone(lastSnapshot, historyState: OnlineRoomChatHistoryState.Loading,
                result: OnlineRoomChatResultCode.None, connectionLost: false));
            try
            {
                G2C_GetRoomChatHistory response = await network.CallAsync<G2C_GetRoomChatHistory>(
                    C2G_GetRoomChatHistory.Create(), RpcTimeoutMilliseconds, token);
                if (!IsCurrentHistory(observedHistoryGeneration, observedRoomGeneration, roomId))
                {
                    return;
                }

                OnlineRoomChatResultCode code = MapServerError(response.Error);
                if (code != OnlineRoomChatResultCode.Success || response.RoomId != roomId)
                {
                    Publish(Clone(lastSnapshot, historyState: OnlineRoomChatHistoryState.Failed, result: code));
                    return;
                }

                List<OnlineRoomChatMessageSnapshot> merged = new List<OnlineRoomChatMessageSnapshot>(lastSnapshot.Messages);
                foreach (RoomChatMessageInfo message in response.Messages)
                {
                    merged.Add(CopyMessage(message));
                }
                Publish(BuildMerged(roomId, merged, Math.Max(lastSnapshot.MaxConfirmedSequence, response.MaxChatSequence),
                    OnlineRoomChatHistoryState.Ready, lastSnapshot.SendState, OnlineRoomChatResultCode.Success, false, lastSnapshot.PendingContent));
            }
            catch (TimeoutException)
            {
                if (IsCurrentHistory(observedHistoryGeneration, observedRoomGeneration, roomId))
                {
                    Publish(Clone(lastSnapshot, historyState: OnlineRoomChatHistoryState.Failed,
                        result: OnlineRoomChatResultCode.Timeout));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                if (IsCurrentHistory(observedHistoryGeneration, observedRoomGeneration, roomId))
                {
                    OnlineRoomChatResultCode code = network == null || network.ConnectionState != NetworkConnectionState.Connected
                        ? OnlineRoomChatResultCode.Disconnected
                        : OnlineRoomChatResultCode.UnknownFailure;
                    Publish(Clone(lastSnapshot, historyState: OnlineRoomChatHistoryState.Failed, result: code));
                    if (code == OnlineRoomChatResultCode.UnknownFailure)
                    {
                        Log.Error($"[OnlineRoomChatSystem] 历史记录失败 roomId={roomId} exception={exception}");
                    }
                }
            }
        }

        private void OnRoomChanged(OnlineRoomChangedEvent evt)
        {
            long incomingRoomId = evt.Snapshot.HasRoom ? evt.Snapshot.RoomId : 0;
            if (incomingRoomId == lastSnapshot.RoomId)
            {
                if (incomingRoomId != 0 && evt.Snapshot.ConnectionLost != lastSnapshot.ConnectionLost)
                {
                    Publish(Clone(lastSnapshot, connectionLost: evt.Snapshot.ConnectionLost));
                }
                return;
            }

            CancelHistory();
            CancelSend();
            roomGeneration++;
            pendingClientMessageId = null;
            pendingContent = string.Empty;
            if (incomingRoomId == 0)
            {
                Publish(OnlineRoomChatSnapshot.Empty());
                return;
            }

            Publish(new OnlineRoomChatSnapshot(incomingRoomId, Array.Empty<OnlineRoomChatMessageSnapshot>(), 0,
                OnlineRoomChatHistoryState.None, OnlineRoomChatSendState.Idle, OnlineRoomChatResultCode.None,
                evt.Snapshot.ConnectionLost, string.Empty));
            _ = RefreshHistoryAsync(roomGeneration, incomingRoomId);
        }

        private void OnAccountChanged(OnlineAccountChangedEvent evt)
        {
            bool wasAuthenticated = accountAuthenticated;
            accountAuthenticated = evt.Snapshot.IsAuthenticated && evt.Snapshot.OperationState != OnlineOperationState.LoggingOut;
            if (!accountAuthenticated)
            {
                bool ordinaryDisconnect = evt.Snapshot.ConnectionState == OnlineConnectionState.Disconnected &&
                                          evt.Snapshot.LastResultCode == OnlineAccountResultCode.Disconnected;
                if (ordinaryDisconnect)
                {
                    CancelHistory();
                    CancelSend();
                    if (lastSnapshot.RoomId != 0)
                    {
                        Publish(Clone(lastSnapshot, historyState: OnlineRoomChatHistoryState.Failed,
                            sendState: OnlineRoomChatSendState.Idle, result: OnlineRoomChatResultCode.Disconnected,
                            connectionLost: true));
                    }
                }
                else if (wasAuthenticated || evt.Snapshot.OperationState == OnlineOperationState.LoggingOut ||
                         evt.Snapshot.LastResultCode == OnlineAccountResultCode.SessionReplaced)
                {
                    CancelHistory();
                    CancelSend();
                    roomGeneration++;
                    pendingClientMessageId = null;
                    pendingContent = string.Empty;
                    Publish(OnlineRoomChatSnapshot.Empty(
                        evt.Snapshot.LastResultCode == OnlineAccountResultCode.SessionReplaced
                            ? OnlineRoomChatResultCode.NotLoggedIn
                            : OnlineRoomChatResultCode.Disconnected));
                }
                return;
            }

            if (!wasAuthenticated && lastSnapshot.RoomId != 0)
            {
                Publish(Clone(lastSnapshot, connectionLost: false));
                _ = RefreshHistoryAsync(roomGeneration, lastSnapshot.RoomId);
            }
        }

        private void OnNetworkDisconnected()
        {
            accountAuthenticated = false;
            CancelHistory();
            CancelSend();
            if (lastSnapshot.RoomId != 0)
            {
                Publish(Clone(lastSnapshot, historyState: OnlineRoomChatHistoryState.Failed,
                    sendState: OnlineRoomChatSendState.Idle, result: OnlineRoomChatResultCode.Disconnected,
                    connectionLost: true));
            }
        }

        private void OnRoomChatMessage(G2C_RoomChatMessage message)
        {
            if (message == null || message.RoomId == 0 || message.RoomId != lastSnapshot.RoomId)
            {
                return;
            }
            MergeMessage(CopyMessage(message.ChatMessage));
        }

        private void MergeMessage(OnlineRoomChatMessageSnapshot message)
        {
            if (message.ChatSequence <= 0 || string.IsNullOrEmpty(message.ChatMessageId) || lastSnapshot.RoomId == 0)
            {
                return;
            }

            List<OnlineRoomChatMessageSnapshot> merged = new List<OnlineRoomChatMessageSnapshot>(lastSnapshot.Messages) { message };
            Publish(BuildMerged(lastSnapshot.RoomId, merged, Math.Max(lastSnapshot.MaxConfirmedSequence, message.ChatSequence),
                lastSnapshot.HistoryState, lastSnapshot.SendState, lastSnapshot.LastResultCode,
                lastSnapshot.ConnectionLost, lastSnapshot.PendingContent));
        }

        private static OnlineRoomChatSnapshot BuildMerged(
            long roomId,
            IEnumerable<OnlineRoomChatMessageSnapshot> messages,
            long maxSequence,
            OnlineRoomChatHistoryState historyState,
            OnlineRoomChatSendState sendState,
            OnlineRoomChatResultCode result,
            bool connectionLost,
            string pendingContent)
        {
            OnlineRoomChatMessageSnapshot[] ordered = messages
                .Where(message => message.ChatSequence > 0 && !string.IsNullOrEmpty(message.ChatMessageId))
                .GroupBy(message => message.ChatMessageId, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(message => message.ChatSequence).First())
                .OrderBy(message => message.ChatSequence)
                .ThenBy(message => message.ChatMessageId, StringComparer.Ordinal)
                .ToArray();
            if (ordered.Length > ClientMessageLimit)
            {
                ordered = ordered.Skip(ordered.Length - ClientMessageLimit).ToArray();
            }
            long actualMax = ordered.Length == 0 ? 0 : ordered[ordered.Length - 1].ChatSequence;
            return new OnlineRoomChatSnapshot(roomId, ordered, Math.Max(maxSequence, actualMax), historyState,
                sendState, result, connectionLost, pendingContent);
        }

        private static OnlineRoomChatMessageSnapshot CopyMessage(RoomChatMessageInfo message)
        {
            return message == null
                ? default
                : new OnlineRoomChatMessageSnapshot(message.ChatMessageId, message.ChatSequence, message.SenderPlayerId,
                    message.SenderDisplayName, message.ServerTime, message.Content);
        }

        private static OnlineRoomChatSnapshot Clone(
            OnlineRoomChatSnapshot source,
            OnlineRoomChatHistoryState? historyState = null,
            OnlineRoomChatSendState? sendState = null,
            OnlineRoomChatResultCode? result = null,
            bool? connectionLost = null,
            string pendingContentOverride = null)
        {
            return new OnlineRoomChatSnapshot(source.RoomId, source.Messages, source.MaxConfirmedSequence,
                historyState ?? source.HistoryState, sendState ?? source.SendState, result ?? source.LastResultCode,
                connectionLost ?? source.ConnectionLost, pendingContentOverride ?? source.PendingContent);
        }

        private void Publish(OnlineRoomChatSnapshot snapshot)
        {
            lastSnapshot = snapshot;
            this.GetModel<OnlineRoomChatModel>().Publish(snapshot);
        }

        private bool IsCurrentRoom(int observedGeneration, long roomId)
        {
            return observedGeneration == roomGeneration && roomId != 0 && roomId == lastSnapshot.RoomId;
        }

        private bool IsCurrentHistory(int observedHistoryGeneration, int observedRoomGeneration, long roomId)
        {
            return observedHistoryGeneration == historyGeneration &&
                   IsCurrentRoom(observedRoomGeneration, roomId);
        }

        private int StartSend()
        {
            CancelSend();
            sendCancellation = new CancellationTokenSource();
            return ++sendGeneration;
        }

        private void EndSend(int generation)
        {
            if (generation == sendGeneration)
            {
                sendCancellation?.Dispose();
                sendCancellation = null;
            }
        }

        private void CancelSend()
        {
            sendCancellation?.Cancel();
            sendCancellation?.Dispose();
            sendCancellation = null;
            sendGeneration++;
        }

        private void CancelHistory()
        {
            historyCancellation?.Cancel();
            historyCancellation?.Dispose();
            historyCancellation = null;
            historyGeneration++;
        }
    }
}
