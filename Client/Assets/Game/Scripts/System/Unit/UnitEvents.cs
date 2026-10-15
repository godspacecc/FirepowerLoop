namespace Game
{
    /// <summary>
    /// 表示一个 Unit 已进入当前活动会话。
    /// </summary>
    public readonly struct UnitAddedEvent
    {
        public UnitAddedEvent(UnitSnapshot snapshot) { Snapshot = snapshot; }
        public UnitSnapshot Snapshot { get; }
    }

    /// <summary>
    /// 表示一个 Unit 的公共快照发生变化。
    /// </summary>
    public readonly struct UnitChangedEvent
    {
        public UnitChangedEvent(UnitSnapshot previous, UnitSnapshot current, UnitChangeMask changeMask)
        {
            Previous = previous;
            Current = current;
            ChangeMask = changeMask;
        }

        public UnitSnapshot Previous { get; }
        public UnitSnapshot Current { get; }
        public UnitChangeMask ChangeMask { get; }
    }

    /// <summary>
    /// 表示一个 Unit 已离开当前活动会话。
    /// </summary>
    public readonly struct UnitRemovedEvent
    {
        public UnitRemovedEvent(UnitSnapshot snapshot, UnitRemovedReason reason)
        {
            Snapshot = snapshot;
            Reason = reason;
        }

        public UnitSnapshot Snapshot { get; }
        public UnitRemovedReason Reason { get; }
    }
}
