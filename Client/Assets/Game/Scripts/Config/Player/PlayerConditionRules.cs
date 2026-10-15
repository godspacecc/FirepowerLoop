namespace Game
{
    public static class PlayerConditionRules
    {
        public const float GameMinutesPerRealSecond = 1f;
        public const float GameMinutesPerDay = 1440f;
        public const float FullPowerDepletionGameMinutes = 2160f;
        public const float MaxPower = 100f;
        public const float BasePowerRecoveryPerRealSecond = 1f;
        public const int PowerDepletionDamagePerRealSecond = 5;
        public const float DefaultMaxWater = 100f;
        public const float WaterDrainPerGameMinute = 100f / GameMinutesPerDay;
        public const float WaterDepletionDamagePerRealSecond = 5f;
        public const float BaseWaterRecoveryPerRealSecond = 1f;
        public const float MinTemperature = 30f;
        public const float MaxTemperature = 45f;
        public const float NormalTemperature = 37f;
        public const float BaseTemperatureRecoveryPerRealSecond = 0.1f;
        public const float MaxTili = 100f;
        public const float SprintSpeedMultiplier = 1.5f;
        public const float SprintTiliCostPerRealSecond = 20f;
        public const float StandingTiliRecoveryPerRealSecond = 25f;
        public const float WalkingTiliRecoveryPerRealSecond = 15f;
        public const float SprintRestartThreshold = 20f;

        public static float PowerDrainPerGameMinute =>
            MaxPower / FullPowerDepletionGameMinutes;
    }
}
