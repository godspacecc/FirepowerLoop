namespace FirepowerLoop.Accounts;

/// <summary>认证结果只包含身份与状态，不包含密码派生值或可恢复连接的凭据。</summary>
public sealed record AccountResult(int Error, long AccountId = 0, long PlayerId = 0, string DisplayName = "",
    long ProfileRevision = 0, long SessionEpoch = 0, bool NeedsPasswordUpgrade = false);

/// <summary>默认显式注册，登录分配持久世代；仅由本项目迁移工具标记的旧凭据兼容短密码。</summary>
public sealed class AccountAuthenticationService(MongoIdentityStore store, AccountFailureGuard? failures = null)
{
    private readonly AccountFailureGuard guard = failures ?? new();
    public async Task<int> ValidateSessionAsync(long accountId, long epoch, CancellationToken token)
    {
        var account = await store.FindByIdAsync(accountId, token);
        if (account == null || epoch <= 0 || account.SessionEpoch != epoch) return 200105;
        return account.Status == "active" ? 0 : account.Status == "banned" ? 200204 : 200203;
    }
    public async Task<AccountResult> RegisterAsync(string userName, string password, CancellationToken token)
    {
        string display = (userName ?? "").Trim(), normalized = display.ToLowerInvariant();
        if (!AccountPolicy.UserName(display)) return new(200101);
        if (!AccountPolicy.NewPassword(password)) return new(200102);
        if (guard.IsLocked(normalized)) return new(200202);
        if (await store.FindAsync(normalized, token) != null) return new(200103);
        var account = new IdentityAccount { AccountId = await store.NextIdAsync("account", token), NormalizedUserName = normalized,
            DisplayUserName = display, CreatedAtUtc = DateTime.UtcNow.ToString("O") };
        AccountPolicy.SetPassword(account, password);
        if (!await store.TryCreateAsync(account, token)) return new(200103);
        return new(0, account.AccountId);
    }
    public async Task<AccountResult> LoginAsync(string userName, string password, CancellationToken token)
    {
        string display = (userName ?? "").Trim(), normalized = display.ToLowerInvariant();
        if (!AccountPolicy.UserName(display)) return new(200101);
        if (!AccountPolicy.NewPassword(password) && !AccountPolicy.LegacyPassword(password)) return new(200102);
        if (guard.IsLocked(normalized)) return new(200202);
        var account = await store.FindAsync(normalized, token);
        if (account == null || !AccountPolicy.Verify(account, password)) return new(guard.Failure(normalized) ? 200202 : 200104);
        if (account.Status != "active") return new(account.Status == "banned" ? 200204 : 200203);
        guard.Success(normalized);
        account = await store.EnsurePlayerAsync(account, token);
        long epoch = await store.NextEpochAsync(account.AccountId, token);
        return new(0, account.AccountId, account.PlayerId, account.DisplayName, account.ProfileRevision, epoch, account.LegacyShortPassword);
    }
}
