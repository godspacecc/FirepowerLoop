using MongoDB.Bson;
using MongoDB.Driver;

namespace FirepowerLoop.Accounts;

/// <summary>单账号原子持久化和独立编号计数；不依赖Mongo副本集事务、不读取其它项目数据。</summary>
public sealed class MongoIdentityStore
{
    private readonly IMongoCollection<IdentityAccount> accounts;
    private readonly IMongoCollection<BsonDocument> counters;
    public MongoIdentityStore(IMongoDatabase database)
    {
        accounts = database.GetCollection<IdentityAccount>("accounts");
        counters = database.GetCollection<BsonDocument>("identity_counters");
    }
    /// <summary>用户名与非零玩家映射均唯一；未创建玩家的多个账号允许PlayerId=0。</summary>
    public Task InitializeAsync(CancellationToken token) => accounts.Indexes.CreateManyAsync(new[]
    {
        new CreateIndexModel<IdentityAccount>(Builders<IdentityAccount>.IndexKeys.Ascending(a => a.NormalizedUserName),
            new CreateIndexOptions { Unique = true, Name = "unique_account_name" }),
        new CreateIndexModel<IdentityAccount>(Builders<IdentityAccount>.IndexKeys.Ascending(a => a.PlayerId),
            new CreateIndexOptions<IdentityAccount> { Unique = true, Name = "unique_nonzero_player",
                PartialFilterExpression = Builders<IdentityAccount>.Filter.Gt(a => a.PlayerId, 0) })
    }, cancellationToken: token);
    public Task<IdentityAccount?> FindAsync(string normalized, CancellationToken token) =>
        accounts.Find(a => a.NormalizedUserName == normalized).FirstOrDefaultAsync(token)!;
    public Task<IdentityAccount?> FindByIdAsync(long id, CancellationToken token) =>
        accounts.Find(a => a.AccountId == id).FirstOrDefaultAsync(token)!;
    public async Task<long> NextIdAsync(string kind, CancellationToken token)
    {
        long first = kind switch { "account" => 1000000, "player" => 2000000, _ => throw new ArgumentException("未知身份编号种类。") };
        try
        {
            await counters.UpdateOneAsync(new BsonDocument("_id", kind), new BsonDocument("$setOnInsert", new BsonDocument("value", first)),
                new UpdateOptions { IsUpsert = true }, token);
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey) { }
        var row = await counters.FindOneAndUpdateAsync(new BsonDocument("_id", kind), new BsonDocument("$inc", new BsonDocument("value", 1L)),
            new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After }, token);
        return row["value"].AsInt64;
    }
    public async Task<bool> TryCreateAsync(IdentityAccount account, CancellationToken token)
    {
        try { await accounts.InsertOneAsync(account, cancellationToken: token); return true; }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
    }
    /// <summary>只为未建玩家的账号提交映射；并发调用复用获胜映射，编号允许空洞但不可回收。</summary>
    public async Task<IdentityAccount> EnsurePlayerAsync(IdentityAccount account, CancellationToken token)
    {
        if (account.PlayerId != 0) return account;
        long id = await NextIdAsync("player", token);
        var updated = await accounts.FindOneAndUpdateAsync(a => a.AccountId == account.AccountId && a.PlayerId == 0,
            Builders<IdentityAccount>.Update.Set(a => a.PlayerId, id).Set(a => a.DisplayName, account.DisplayUserName)
                .Set(a => a.ProfileRevision, 1).Set(a => a.UpdatedAtUtc, DateTime.UtcNow.ToString("O")),
            new FindOneAndUpdateOptions<IdentityAccount> { ReturnDocument = ReturnDocument.After }, token);
        return updated ?? await FindByIdAsync(account.AccountId, token) ?? throw new IOException("账号在创建玩家期间消失。");
    }
    public async Task<long> NextEpochAsync(long accountId, CancellationToken token)
    {
        var updated = await accounts.FindOneAndUpdateAsync(a => a.AccountId == accountId && a.Status == "active",
            Builders<IdentityAccount>.Update.Inc(a => a.SessionEpoch, 1),
            new FindOneAndUpdateOptions<IdentityAccount> { ReturnDocument = ReturnDocument.After }, token);
        return updated?.SessionEpoch ?? throw new IOException("账号不存在，不能分配会话世代。");
    }

    /// <summary>导入前只提高编号水位，保留旧库已预留的空洞，不回收编号。</summary>
    public Task ReserveIdsAsync(long nextAccount, long nextPlayer, CancellationToken token) => Task.WhenAll(
        counters.UpdateOneAsync(new BsonDocument("_id", "account"), new BsonDocument("$max", new BsonDocument("value", nextAccount - 1)),
            new UpdateOptions { IsUpsert = true }, token),
        counters.UpdateOneAsync(new BsonDocument("_id", "player"), new BsonDocument("$max", new BsonDocument("value", nextPlayer - 1)),
            new UpdateOptions { IsUpsert = true }, token));
}
