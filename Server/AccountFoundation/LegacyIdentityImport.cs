using System.Text.Json;
namespace FirepowerLoop.Accounts;

/// <summary>只解释当前目标schema4身份快照；先完整校验，显式导入时仅新增或确认一致，不覆盖目标记录。</summary>
public sealed class LegacyIdentityImport
{
    public long NextAccountId { get; private init; }
    public long NextPlayerId { get; private init; }
    public IReadOnlyList<IdentityAccount> Accounts { get; private init; } = Array.Empty<IdentityAccount>();
    public static LegacyIdentityImport Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        if (root.GetProperty("SchemaVersion").GetInt32() != 4) throw new InvalidDataException("只允许本项目schema4身份格式，不迁入其他项目数据。");
        long nextAccount = root.GetProperty("NextAccountId").GetInt64(), nextPlayer = root.GetProperty("NextPlayerId").GetInt64();
        var profiles = root.GetProperty("Players").EnumerateArray().ToDictionary(p => p.GetProperty("PlayerId").GetInt64());
        var rows = new List<IdentityAccount>(); var names = new HashSet<string>(StringComparer.Ordinal); var ids = new HashSet<long>(); var mapped = new HashSet<long>();
        foreach (var row in root.GetProperty("Accounts").EnumerateArray())
        {
            var account = JsonSerializer.Deserialize<IdentityAccount>(row.GetRawText()) ?? throw new InvalidDataException("账号记录为空。");
            if (account.AccountId <= 0 || !ids.Add(account.AccountId) || !AccountPolicy.UserName(account.DisplayUserName) ||
                account.NormalizedUserName != account.DisplayUserName.ToLowerInvariant() || !names.Add(account.NormalizedUserName) ||
                account.AlgorithmVersion != 1 || account.Iterations != 600000 || Convert.FromBase64String(account.SaltBase64).Length != 16 ||
                Convert.FromBase64String(account.HashBase64).Length != 32 || !DateTimeOffset.TryParse(account.CreatedAtUtc, out _))
                throw new InvalidDataException("旧账号身份或凭据格式无效。");
            account.Status = "active"; account.SessionEpoch = 0; account.LegacyShortPassword = true;
            if (account.PlayerId != 0)
            {
                if (account.PlayerId < 0 || !mapped.Add(account.PlayerId) || !profiles.TryGetValue(account.PlayerId, out var profile) ||
                    profile.GetProperty("AccountId").GetInt64() != account.AccountId) throw new InvalidDataException("旧账号与玩家映射不一致。");
                account.DisplayName = profile.GetProperty("DisplayName").GetString() ?? "";
                account.ProfileRevision = profile.GetProperty("Revision").GetInt64();
                account.UpdatedAtUtc = profile.GetProperty("UpdatedAtUtc").GetString() ?? "";
                if (account.ProfileRevision <= 0 || string.IsNullOrWhiteSpace(account.DisplayName) || !DateTimeOffset.TryParse(account.UpdatedAtUtc, out _))
                    throw new InvalidDataException("旧玩家资料格式无效。");
            }
            rows.Add(account);
        }
        if (profiles.Count != mapped.Count || nextAccount <= Math.Max(1000000, ids.DefaultIfEmpty(0).Max()) ||
            nextPlayer <= Math.Max(2000000, mapped.DefaultIfEmpty(0).Max())) throw new InvalidDataException("旧身份水位或孤立玩家记录无效。");
        return new LegacyIdentityImport { NextAccountId = nextAccount, NextPlayerId = nextPlayer, Accounts = rows };
    }
    public async Task ApplyAsync(MongoIdentityStore store, CancellationToken token)
    {
        // 非事务导入允许中断后重跑；必须离线执行，任何冲突都停下且不覆盖、删除已存在数据。
        await store.ReserveIdsAsync(NextAccountId, NextPlayerId, token);
        foreach (var account in Accounts)
        {
            if (await store.TryCreateAsync(account, token)) continue;
            var existing = await store.FindByIdAsync(account.AccountId, token);
            if (existing == null || existing.NormalizedUserName != account.NormalizedUserName || existing.PlayerId != account.PlayerId ||
                existing.AlgorithmVersion != account.AlgorithmVersion || existing.Iterations != account.Iterations ||
                existing.SaltBase64 != account.SaltBase64 || existing.HashBase64 != account.HashBase64 ||
                existing.DisplayUserName != account.DisplayUserName || existing.DisplayName != account.DisplayName ||
                existing.ProfileRevision != account.ProfileRevision || existing.CreatedAtUtc != account.CreatedAtUtc)
                throw new InvalidDataException("Mongo目标存在不同身份或凭据，导入停止；未覆盖冲突记录。");
        }
    }
}
