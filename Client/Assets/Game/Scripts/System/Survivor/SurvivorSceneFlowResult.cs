namespace Game
{
    public readonly struct SurvivorSceneFlowResult
    {
        private SurvivorSceneFlowResult(bool success, string error)
        {
            Success = success;
            Error = error;
        }

        public bool Success { get; }
        public string Error { get; }

        public static SurvivorSceneFlowResult Ok() => new SurvivorSceneFlowResult(true, null);
        public static SurvivorSceneFlowResult Fail(string error) => new SurvivorSceneFlowResult(false, error);
    }

}
