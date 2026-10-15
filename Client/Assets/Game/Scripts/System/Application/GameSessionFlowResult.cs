namespace Game
{
    /// <summary>
    /// 表示进入或退出游戏会话的流程结果，失败原因用于日志诊断而不直接面向玩家。
    /// </summary>
    public readonly struct GameSessionFlowResult
    {
        public GameSessionFlowResult(bool success, string error, bool rollbackSucceeded)
        {
            Success = success;
            Error = error ?? string.Empty;
            RollbackSucceeded = rollbackSucceeded;
        }

        public bool Success { get; }
        public string Error { get; }
        public bool RollbackSucceeded { get; }

        public static GameSessionFlowResult Ok()
        {
            return new GameSessionFlowResult(true, string.Empty, true);
        }

        public static GameSessionFlowResult Fail(string error, bool rollbackSucceeded)
        {
            return new GameSessionFlowResult(false, error, rollbackSucceeded);
        }
    }
}
