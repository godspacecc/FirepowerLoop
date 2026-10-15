namespace Game
{
    public readonly struct PlayerAttributeChange
    {
        public PlayerAttributeChange(string attributeId, double before, double after, bool changed)
        {
            AttributeId = attributeId;
            Before = before;
            After = after;
            Changed = changed;
        }

        public string AttributeId { get; }
        public double Before { get; }
        public double After { get; }
        public bool Changed { get; }
    }

    internal enum PlayerAttributeFailureKind
    {
        None,
        StateRejected,
        OutOfRange,
        ExecutionFailed
    }

    public readonly struct PlayerMoveSpeedChangedEvent
    {
        public PlayerMoveSpeedChangedEvent(float moveSpeed)
        {
            MoveSpeed = moveSpeed;
        }

        public float MoveSpeed { get; }
    }
}
