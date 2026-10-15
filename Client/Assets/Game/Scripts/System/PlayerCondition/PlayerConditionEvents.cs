namespace Game
{
    public readonly struct PlayerConditionChangedEvent
    {
    }

    public readonly struct PlayerConditionAdvanceResult
    {
        public PlayerConditionAdvanceResult(
            bool applied,
            float advancedGameMinutes,
            float powerDelta,
            float waterDelta,
            int powerDepletionDamage,
            int waterDepletionDamage)
        {
            Applied = applied;
            AdvancedGameMinutes = advancedGameMinutes;
            PowerDelta = powerDelta;
            WaterDelta = waterDelta;
            PowerDepletionDamage = powerDepletionDamage;
            WaterDepletionDamage = waterDepletionDamage;
        }

        public bool Applied { get; }
        public float AdvancedGameMinutes { get; }
        public float PowerDelta { get; }
        public float WaterDelta { get; }
        public int PowerDepletionDamage { get; }
        public int WaterDepletionDamage { get; }
    }

    public readonly struct SprintEvaluation
    {
        public SprintEvaluation(bool isSprinting, float speedMultiplier)
        {
            IsSprinting = isSprinting;
            SpeedMultiplier = speedMultiplier;
        }

        public bool IsSprinting { get; }
        public float SpeedMultiplier { get; }
    }
}
