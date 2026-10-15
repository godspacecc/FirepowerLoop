namespace Game
{
    public readonly struct MapControllerResult
    {
        public MapControllerResult(bool success, string error)
        {
            Success = success;
            Error = error;
        }

        public bool Success { get; }
        public string Error { get; }

        public static MapControllerResult Ok()
        {
            return new MapControllerResult(true, null);
        }

        public static MapControllerResult Fail(string error)
        {
            return new MapControllerResult(false, string.IsNullOrWhiteSpace(error) ? "地图控制器操作失败" : error);
        }
    }
}
