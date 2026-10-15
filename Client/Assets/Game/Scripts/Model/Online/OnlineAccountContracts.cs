using QFramework;

namespace Game
{
    public enum OnlineConnectionState
    {
        Disconnected,
        Connecting,
        AnonymousConnected,
        Authenticated
    }

    public enum OnlineOperationState
    {
        Idle,
        Registering,
        LoggingIn,
        LoggingOut
    }

    public enum OnlineAccountResultCode
    {
        None,
        Success,
        AccountCreated,
        InvalidUserName,
        InvalidPassword,
        InvalidNetworkConfig,
        ConnectFailed,
        ConnectTimeout,
        RequestTimeout,
        UserNameExists,
        AccountOrPasswordWrong,
        PasswordWrong,
        NotLoggedIn,
        SessionReplaced,
        ServiceUnavailable,
        SessionConflict,
        RegisterSuccessLoginFailed,
        Disconnected,
        PreferenceSaveFailed,
        OperationInProgress,
        UnknownFailure
    }

    public readonly struct OnlineAccountSnapshot
    {
        public OnlineAccountSnapshot(
            OnlineConnectionState connectionState,
            OnlineOperationState operationState,
            OnlineAccountResultCode lastResultCode,
            string userName,
            long accountId,
            long playerId,
            long sessionEpoch,
            bool isPlayerActorReused,
            string lastUserName,
            bool isNewAccount = false)
        {
            ConnectionState = connectionState;
            OperationState = operationState;
            LastResultCode = lastResultCode;
            UserName = userName ?? string.Empty;
            AccountId = accountId;
            PlayerId = playerId;
            SessionEpoch = sessionEpoch;
            IsPlayerActorReused = isPlayerActorReused;
            LastUserName = lastUserName ?? string.Empty;
            IsNewAccount = isNewAccount;
        }

        public OnlineConnectionState ConnectionState { get; }
        public OnlineOperationState OperationState { get; }
        public OnlineAccountResultCode LastResultCode { get; }
        public string UserName { get; }
        public long AccountId { get; }
        public long PlayerId { get; }
        public long SessionEpoch { get; }
        public bool IsPlayerActorReused { get; }
        public string LastUserName { get; }
        public bool IsNewAccount { get; }
        public bool IsAuthenticated => ConnectionState == OnlineConnectionState.Authenticated;

        public static OnlineAccountSnapshot Empty(string lastUserName = "")
        {
            return new OnlineAccountSnapshot(OnlineConnectionState.Disconnected, OnlineOperationState.Idle, OnlineAccountResultCode.None, string.Empty, 0, 0, 0, false, lastUserName);
        }
    }

    public readonly struct OnlineAccountOperationResult
    {
        public OnlineAccountOperationResult(OnlineAccountResultCode code, OnlineAccountSnapshot snapshot)
        {
            Code = code;
            Snapshot = snapshot;
        }

        public OnlineAccountResultCode Code { get; }
        public OnlineAccountSnapshot Snapshot { get; }
        public bool Success => Code == OnlineAccountResultCode.Success || Code == OnlineAccountResultCode.AccountCreated || Code == OnlineAccountResultCode.PreferenceSaveFailed;
    }

    public readonly struct OnlineAccountChangedEvent
    {
        public OnlineAccountChangedEvent(OnlineAccountSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public OnlineAccountSnapshot Snapshot { get; }
    }

    public readonly struct OnlineAccountAttentionEvent
    {
        public OnlineAccountAttentionEvent(OnlineAccountResultCode code)
        {
            Code = code;
        }

        public OnlineAccountResultCode Code { get; }
    }

    public sealed class OnlineAccountModel : AbstractModel
    {
        public BindableProperty<OnlineAccountSnapshot> Snapshot { get; } =
            new BindableProperty<OnlineAccountSnapshot>(OnlineAccountSnapshot.Empty());

        protected override void OnInit()
        {
        }

        public void Publish(OnlineAccountSnapshot snapshot)
        {
            Snapshot.Value = snapshot;
            this.SendEvent(new OnlineAccountChangedEvent(snapshot));
        }
    }
}
