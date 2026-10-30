namespace ET.Server
{
    /// <summary>当前工程的账号认证与玩家身份存储，不承载库存或成长状态。</summary>
    [ComponentOf(typeof(Scene))]
    public class OnlineAccountComponent: Entity, IAwake
    {
        public OnlineIdentityStoreSnapshot Snapshot = new();
        public string StorePath;
        /// <summary>生产账号认证入口；JSON快照只保留给旧格式检查和显式迁移。</summary>
        public FirepowerLoop.Accounts.AccountAuthenticationService Authentication;
    }

    [EnableClass]
    public class OnlineIdentityStoreSnapshot
    {
        /// <summary>街机身份格式；拒绝旧业务格式，不迁移或覆盖旧数据。</summary>
        public int SchemaVersion { get; set; } = 4;
        public long NextAccountId { get; set; } = 1000001;
        public long NextPlayerId { get; set; } = 2000001;
        public System.Collections.Generic.List<OnlineAccountRecord> Accounts { get; set; } = new();
        public System.Collections.Generic.List<OnlinePlayerProfileRecord> Players { get; set; } = new();
    }

    [EnableClass]
    public class OnlineAccountRecord
    {
        public long AccountId { get; set; }
        public string NormalizedUserName { get; set; }
        public string DisplayUserName { get; set; }
        public int AlgorithmVersion { get; set; }
        public int Iterations { get; set; }
        public string SaltBase64 { get; set; }
        public string HashBase64 { get; set; }
        public long PlayerId { get; set; }
        public string CreatedAtUtc { get; set; }
    }

    [EnableClass]
    public class OnlinePlayerProfileRecord
    {
        public long PlayerId { get; set; }
        public long AccountId { get; set; }
        public string DisplayName { get; set; }
        public long Revision { get; set; }
        public string CreatedAtUtc { get; set; }
        public string UpdatedAtUtc { get; set; }
    }
}
