namespace Game
{
    public readonly struct ArcadeSceneFlowResult
    {
        private ArcadeSceneFlowResult(bool success, string error)
        {
            Success = success;
            Error = error;
        }

        public bool Success { get; }
        public string Error { get; }

        public static ArcadeSceneFlowResult Ok() => new ArcadeSceneFlowResult(true, null);
        public static ArcadeSceneFlowResult Fail(string error) => new ArcadeSceneFlowResult(false, error);
    }

}
