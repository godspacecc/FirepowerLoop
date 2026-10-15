namespace Game
{
    public enum PlayerActionKind
    {
        None = 0,
        UseItem = 1,
        Build = 2,
        Search = 3,
        TemporaryBuildingReload = 4,
        Reload = 5,
        Extraction = 6,
        /// <summary>装备指定近战工具后执行的可中断砍树动作，不走战斗伤害结算。</summary>
        ChopTree = 7,
        /// <summary>装备镐头后执行的可中断采石动作；奖励由采集事务提交，不走战斗结算。</summary>
        MineStone = 8,
        /// <summary>河岸取水的可中断持续动作；只采集入包，不直接饮用或消耗水源。</summary>
        CollectWater = 9
    }

    public enum PlayerActionCancelReason
    {
        UserCanceled = 0,
        Replaced = 1,
        Interaction = 2,
        Attack = 3,
        ContextInvalid = 4,
        OwnerCanceled = 5,
        PlayerUnavailable = 6,
        ExternalInterrupted = 7,
        Damaged = 8,
        LeftRange = 9
    }

    public readonly struct PlayerActionRequest
    {
        public PlayerActionRequest(PlayerActionKind kind, float durationSeconds, string iconAddress)
        {
            Kind = kind;
            DurationSeconds = durationSeconds;
            IconAddress = iconAddress;
        }

        public PlayerActionKind Kind { get; }
        public float DurationSeconds { get; }
        public string IconAddress { get; }
    }

    public readonly struct PlayerActionSnapshot
    {
        public PlayerActionSnapshot(long actionId, PlayerActionKind kind, float elapsedSeconds, float durationSeconds, float progress01, string iconAddress)
        {
            ActionId = actionId;
            Kind = kind;
            ElapsedSeconds = elapsedSeconds;
            DurationSeconds = durationSeconds;
            Progress01 = progress01;
            IconAddress = iconAddress;
        }

        public long ActionId { get; }
        public PlayerActionKind Kind { get; }
        public float ElapsedSeconds { get; }
        public float DurationSeconds { get; }
        public float Progress01 { get; }
        public string IconAddress { get; }
    }

    public readonly struct PlayerActionChangedEvent
    {
        public PlayerActionChangedEvent(bool hasAction, PlayerActionSnapshot snapshot)
        {
            HasAction = hasAction;
            Snapshot = snapshot;
        }

        public bool HasAction { get; }
        public PlayerActionSnapshot Snapshot { get; }
    }

    public readonly struct PlayerActionEndedEvent
    {
        public PlayerActionEndedEvent(long actionId, PlayerActionKind kind, bool completed, PlayerActionCancelReason cancelReason)
        {
            ActionId = actionId;
            Kind = kind;
            Completed = completed;
            CancelReason = cancelReason;
        }

        public long ActionId { get; }
        public PlayerActionKind Kind { get; }
        public bool Completed { get; }
        public PlayerActionCancelReason CancelReason { get; }
    }
}
