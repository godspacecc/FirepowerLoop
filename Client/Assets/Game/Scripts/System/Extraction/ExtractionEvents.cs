namespace Game
{
    public readonly struct ExtractionCreatedEvent
    {
        public ExtractionCreatedEvent(int extractionUnitId, long mapInstanceId, bool available)
        {
            ExtractionUnitId = extractionUnitId;
            MapInstanceId = mapInstanceId;
            Available = available;
        }

        public int ExtractionUnitId { get; }
        public long MapInstanceId { get; }
        public bool Available { get; }
    }

    public readonly struct ExtractionAvailabilityChangedEvent
    {
        public ExtractionAvailabilityChangedEvent(int extractionUnitId, bool available)
        {
            ExtractionUnitId = extractionUnitId;
            Available = available;
        }

        public int ExtractionUnitId { get; }
        public bool Available { get; }
    }

    public readonly struct ExtractionReturnStartedEvent
    {
        public ExtractionReturnStartedEvent(int extractionUnitId, int playerUnitId)
        {
            ExtractionUnitId = extractionUnitId;
            PlayerUnitId = playerUnitId;
        }

        public int ExtractionUnitId { get; }
        public int PlayerUnitId { get; }
    }

    public readonly struct ExtractionRemovedEvent
    {
        public ExtractionRemovedEvent(int extractionUnitId, long mapInstanceId)
        {
            ExtractionUnitId = extractionUnitId;
            MapInstanceId = mapInstanceId;
        }

        public int ExtractionUnitId { get; }
        public long MapInstanceId { get; }
    }

    public readonly struct ExtractionCountdownStartedEvent
    {
        public ExtractionCountdownStartedEvent(int extractionUnitId, int playerUnitId, long mapInstanceId, long actionId, float durationSeconds)
        {
            ExtractionUnitId = extractionUnitId;
            PlayerUnitId = playerUnitId;
            MapInstanceId = mapInstanceId;
            ActionId = actionId;
            DurationSeconds = durationSeconds;
        }

        public int ExtractionUnitId { get; }
        public int PlayerUnitId { get; }
        public long MapInstanceId { get; }
        public long ActionId { get; }
        public float DurationSeconds { get; }
    }

    public readonly struct ExtractionCountdownCanceledEvent
    {
        public ExtractionCountdownCanceledEvent(int extractionUnitId, int playerUnitId, long mapInstanceId, long actionId, PlayerActionCancelReason reason)
        {
            ExtractionUnitId = extractionUnitId;
            PlayerUnitId = playerUnitId;
            MapInstanceId = mapInstanceId;
            ActionId = actionId;
            Reason = reason;
        }

        public int ExtractionUnitId { get; }
        public int PlayerUnitId { get; }
        public long MapInstanceId { get; }
        public long ActionId { get; }
        public PlayerActionCancelReason Reason { get; }
    }

    public readonly struct ExtractionReturnFailedEvent
    {
        public ExtractionReturnFailedEvent(int extractionUnitId, int playerUnitId, long mapInstanceId)
        {
            ExtractionUnitId = extractionUnitId;
            PlayerUnitId = playerUnitId;
            MapInstanceId = mapInstanceId;
        }

        public int ExtractionUnitId { get; }
        public int PlayerUnitId { get; }
        public long MapInstanceId { get; }
    }
}
