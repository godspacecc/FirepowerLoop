namespace ET.Server
{
    /// <summary>账号入口共用来源限流，不信任协议内自报地址；角色和房间仍使用自身限流。</summary>
    [EnableClass]
    public static class OnlineAccountAdmission
    {
        [StaticField]
        private static readonly FirepowerLoop.Accounts.AccountRequestGuard Requests = new();
        public static bool Allow(Session session) => session.RemoteAddress != null && Requests.Allow(session.RemoteAddress.Address);
    }
}
