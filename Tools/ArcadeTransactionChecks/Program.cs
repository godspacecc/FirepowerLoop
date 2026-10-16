using System.Reflection;
using System.Text.Json;
using ET;
using ET.Server;

// 独立进程只检查正式服务器事务方法；所有保存使用临时目录，不启动服务器或监听网络。
string repository = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var config = ServerConfig.CreateForValidation("OnlineLocal", name =>
    File.ReadAllBytes(Path.Combine(repository, "Server/GameServer/Config/Luban", name + ".bytes")));
World.Instance.AddSingleton(config);
World.Instance.AddSingleton<TimeInfo>();
World.Instance.AddSingleton<ObjectPool>();
World.Instance.AddSingleton<Logger>().Log = new CheckLogger();
int passed = 0, failed = 0;
void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
object Call(string method, params object[] values) => typeof(OnlineArcadeRunManagerSystem)
    .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, values)!;
void Check(string name, Action<OnlineArcadeRunManagerComponent, OnlineArcadeRunActor> body)
{
    string root = Path.Combine(Path.GetTempPath(), "AnotherDayArcadeTransactionChecks", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var manager = new OnlineArcadeRunManagerComponent { StorePath = Path.Combine(root, "runs.json") };
        var actor = Actor(Record());
        Call("CommitWithCandidate", manager, actor.Record, 0L);
        manager.Runs.Add(actor.Record.RunId, actor);
        foreach (var member in actor.Record.Players) manager.RunIdsByPlayer.Add(member.PlayerId, actor.Record.RunId);
        body(manager, actor);
        passed++; Console.WriteLine("PASS " + name);
    }
    catch (Exception error)
    {
        failed++; Console.WriteLine("FAIL " + name + ": " + (error.InnerException ?? error));
    }
    finally { Directory.Delete(root, true); }
}
OnlineArcadeRunRecord Record(long id = 101) => new()
{
    RunId = id, RoomId = id + 1000, Revision = 4, Phase = OnlineArcadeRunPhase.SafeCheckpoint,
    CompletedArena = 2, NextArena = 3,
    ConfigFingerprint = config.ArcadeRule.ConfigFingerprint, ClientVersion = "transaction-check",
    CreatedAtUtc = DateTime.UtcNow.ToString("O"), UpdatedAtUtc = DateTime.UtcNow.ToString("O"),
    Players = new() { Player(id * 10 + 1), Player(id * 10 + 2) },
    Operations = new() { new() { OperationId = "old-purchase", PlayerId = id * 10 + 1,
        ActionKind = 1, Fingerprint = "old-fingerprint", CompletedAtUtc = DateTime.UtcNow.ToString("O") } }
};
OnlineArcadePlayerRecord Player(long id) => new()
{
    PlayerId = id, Lives = 3, RunGold = 100, Bombs = 2, Dashes = 2, WeaponId = "arcade.weapon.basic",
    UpgradeStacks = new() { ["arcade.upgrade.damage"] = 1 }, Consumables = new() { ["old-item"] = 1 },
    EquipmentBySlot = new() { ["support"] = "old-support" },
};
OnlineArcadeRunActor Actor(OnlineArcadeRunRecord record)
{
    var actor = new OnlineArcadeRunActor { Record = OnlineArcadeRunManagerSystem.Clone(record) };
    // 隔离夹具赋予 EntityRef 有效实例身份，避免启动完整 ET 场景和服务。
    typeof(Entity).GetProperty("InstanceId")!.SetValue(actor, record.RunId);
    typeof(OnlineArcadeRunActor).GetField("CommittedRecord")?.SetValue(actor, OnlineArcadeRunManagerSystem.Clone(record));
    return actor;
}
OnlineArcadeRunStoreSnapshot Read(OnlineArcadeRunManagerComponent manager) =>
    JsonSerializer.Deserialize<OnlineArcadeRunStoreSnapshot>(File.ReadAllText(manager.StorePath))!;
OnlineArcadeRunRecord DiskRun(OnlineArcadeRunManagerComponent manager) => Read(manager).Runs.Single(x => x.RunId == 101);
OnlineArcadeRunRecord Committed(OnlineArcadeRunActor actor) => (OnlineArcadeRunRecord)
    typeof(OnlineArcadeRunActor).GetField("CommittedRecord")!.GetValue(actor)!;
void Dirty(OnlineArcadeRunActor actor)
{
    foreach (var member in actor.Record.Players) member.Ready = true;
    actor.Battle = OnlineArcadeRunActorSystem.PrepareBattle(actor.Record, false);
    var player = actor.Record.Players[0];
    player.RunGold = 175; player.Bombs = 0; player.Dashes = 0; player.Lives = 2;
    player.WeaponId = "arcade.weapon.rapid"; player.UpgradeStacks["arcade.upgrade.damage"] = 3;
    player.Consumables["old-item"] = 4; player.EquipmentBySlot["support"] = "new-support";
    actor.Record.Operations.Add(new() { OperationId = "transient", PlayerId = player.PlayerId, Fingerprint = "transient" });
    actor.Record.Revision = 9;
}
void RequireBaseline(OnlineArcadeRunRecord record)
{
    var player = record.Players[0];
    Require(player.RunGold == 100 && player.Bombs == 2 && player.Dashes == 2 && player.Lives == 3,
        $"resources leaked: gold={player.RunGold} bombs={player.Bombs} dashes={player.Dashes} lives={player.Lives}");
    Require(player.WeaponId == "arcade.weapon.basic" && player.UpgradeStacks["arcade.upgrade.damage"] == 1 &&
        player.Consumables["old-item"] == 1 && player.EquipmentBySlot["support"] == "old-support", "loadout leaked");
    Require(record.Operations.Count == 1 && record.Operations[0].OperationId == "old-purchase", "shop purchases or operations leaked");
    Require(record.NextArena == 3 && record.CompletedArena == 2, "arena progress changed");
}
Check("Disconnect restores all resources, loadout and operation ledger", (manager, actor) =>
{
    Dirty(actor);
    Require((bool)Call("TryRollbackDisconnectedBattle", manager, actor), "rollback rejected");
    RequireBaseline(actor.Record); RequireBaseline(DiskRun(manager));
    Require(actor.Record.Revision == 10 && actor.Battle == null, "rollback revision or battle cleanup invalid");
    RequireBaseline(Committed(actor));
});
Check("Failed rollback preserves battle, disk and committed snapshot; retry succeeds", (manager, actor) =>
{
    string before = File.ReadAllText(manager.StorePath); Dirty(actor); var battle = actor.Battle;
    manager.RemainingFailSaveCount = 1;
    Require(!(bool)Call("TryRollbackDisconnectedBattle", manager, actor), "failure injection ignored");
    Require(actor.Battle == battle && actor.Record.Players[0].RunGold == 175, "failed rollback changed runtime");
    Require(File.ReadAllText(manager.StorePath) == before, "failed rollback changed disk");
    RequireBaseline(Committed(actor));
    Require((bool)Call("TryRollbackDisconnectedBattle", manager, actor), "retry rejected");
    RequireBaseline(actor.Record);
});
foreach (string entry in new[] { "CommitWithCandidate", "CommitWithoutRun" })
{
    Check(entry + " isolates another active run", (manager, actor) =>
    {
        Dirty(actor);
        var other = Record(202);
        switch (entry)
        {
            case "CommitWithCandidate": Call(entry, manager, other, 0L); break;
            case "CommitWithoutRun": Call(entry, manager, 999L); break;
            default: throw new InvalidOperationException(entry);
        }
        var persisted = DiskRun(manager); RequireBaseline(persisted);
        Require(persisted.Revision == 4 && actor.Record.Revision == 9 && actor.Battle != null, "other save changed live state");
        Call("ValidateStore", Read(manager));
    });
}
Check("Arena completion failure keeps old checkpoint; retry commits once", (manager, actor) =>
{
    Dirty(actor); actor.Record.Operations.RemoveAt(1);
    manager.RemainingFailSaveCount = 1;
    Require(!(bool)Call("TryCommitArenaCompletion", manager, actor), "completion failure ignored");
    RequireBaseline(Committed(actor)); RequireBaseline(DiskRun(manager));
    Require(actor.Battle != null && actor.Record.NextArena == 3, "completion failure released battle");
    Require((bool)Call("TryCommitArenaCompletion", manager, actor), "completion retry rejected");
    Require(actor.Record.NextArena == 4 && actor.Battle == null, "arena completion failed");
    Require(Committed(actor).Players[0].RunGold == actor.Record.Players[0].RunGold, "checkpoint not advanced");
    long gold = actor.Record.Players[0].RunGold;
    Call("CommitWithoutRun", manager, 999L);
    Require(DiskRun(manager).Players[0].RunGold == gold, "later save repeated completion reward");
});
Check("Committed checkpoint and live record do not alias nested values", (manager, actor) =>
{
    Dirty(actor); RequireBaseline(Committed(actor));
    Call("TryRollbackDisconnectedBattle", manager, actor);
    actor.Record.Players[0].RunGold = 999;
    RequireBaseline(Committed(actor));
});
Check("Missing committed checkpoint fails closed instead of saving runtime", (manager, actor) =>
{
    Dirty(actor); typeof(OnlineArcadeRunActor).GetField("CommittedRecord")?.SetValue(actor, null);
    string before = File.ReadAllText(manager.StorePath);
    bool rejected = false;
    try { Call("CommitWithoutRun", manager, 999L); }
    catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { rejected = true; }
    Require(rejected && File.ReadAllText(manager.StorePath) == before, "missing checkpoint saved runtime");
});
foreach (bool success in new[] { false, true })
{
    Check("Challenge completion preserves attempted checkpoint; success=" + success, (manager, actor) =>
    {
        actor.Record.ChallengeAttempted = true;
        actor.Record.ChallengeId = config.Tables.TbArcadeChallenge.DataList[0].Id;
        typeof(OnlineArcadeRunActor).GetField("CommittedRecord")!.SetValue(actor, OnlineArcadeRunManagerSystem.Clone(actor.Record));
        Dirty(actor); actor.Battle.ChallengeSucceeded = success;
        Require((bool)Call("TryCommitChallengeCompletion", manager, actor), "challenge completion rejected");
        var player = actor.Record.Players[0];
        Require(player.RunGold == 100 && player.Bombs == 2 && player.Lives == 3 && player.UpgradeStacks["arcade.upgrade.damage"] == 1,
            "challenge leaked battle resources");
        Require(actor.Record.ChallengeAttempted && actor.Record.Revision == 10 && actor.Record.NextArena == 3,
            "challenge reset attempt, revision or arena");
        Require(actor.Record.Operations.Count == 1 && actor.Battle == null, "challenge leaked transient ledger");
        Require(Committed(actor).Players[0].RunGold == 100, "challenge checkpoint not updated");
    });
}
Check("Actual file replacement failure leaves both checkpoint and live battle intact", (manager, actor) =>
{
    string before = File.ReadAllText(manager.StorePath); Dirty(actor); var battle = actor.Battle;
    using (var locked = new FileStream(manager.StorePath, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        Require(!(bool)Call("TryRollbackDisconnectedBattle", manager, actor), "locked store unexpectedly replaced");
        Require(actor.Battle == battle && actor.Record.Revision == 9, "failed file replacement changed runtime");
        RequireBaseline(Committed(actor));
    }
    Require(File.ReadAllText(manager.StorePath) == before, "failed file replacement changed disk");
    Require(Directory.GetFiles(Path.GetDirectoryName(manager.StorePath)!, "*.tmp").Length == 0, "temporary file leaked");
    Require((bool)Call("TryRollbackDisconnectedBattle", manager, actor), "file retry rejected");
    RequireBaseline(DiskRun(manager));
});
Check("Ready advances checkpoint once; duplicate and stale requests remain safe", (manager, actor) =>
{
    long playerId = actor.Record.Players[0].PlayerId;
    async Task<(bool Accepted, OnlineArcadeRunSnapshot Snapshot)> Ready(string id, long revision) =>
        await manager.OperateSafeCheckpoint(playerId, id, revision, (int)OnlineArcadeSafeCheckpointActionKind.SetReady, true);
    manager.RemainingFailSaveCount = 1;
    bool failedSave = false;
    try { Ready("ready", 4).GetAwaiter().GetResult(); }
    catch (RpcException error) when (error.Error == ErrorCode.ERR_OnlineArcadeSaveFailed) { failedSave = true; }
    Require(failedSave && !actor.Record.Players[0].Ready, "failed ready changed state");
    RequireBaseline(Committed(actor));
    Require(Ready("ready", 4).GetAwaiter().GetResult().Accepted, "ready rejected");
    Require(actor.Record.Players[0].RunGold == 100 && actor.Record.Players[0].Bombs == 2 && actor.Record.Players[0].Ready && actor.Record.Revision == 5,
        "ready result invalid");
    Require(Committed(actor).Players[0].Ready && DiskRun(manager).Players[0].Ready, "ready checkpoint missing");
    Ready("ready", 4).GetAwaiter().GetResult();
    Require(actor.Record.Players[0].RunGold == 100 && actor.Record.Revision == 5, "duplicate ready changed resources or revision");
    bool stale = false;
    try { Ready("stale", 4).GetAwaiter().GetResult(); }
    catch (RpcException error) when (error.Error == ErrorCode.ERR_OnlineArcadeStateInvalid) { stale = true; }
    Require(stale, "stale request accepted");
});
Check("Settlement preserves scores without creating wallets and is idempotent", (manager, actor) =>
{
    long playerId = actor.Record.Players[0].PlayerId;
    actor.Record.Players[0].Score = 12345;
    manager.RemainingFailSaveCount = 1;
    bool rejected = false;
    try { Call("CommitPlayerSettlement", manager, actor, playerId); }
    catch (TargetInvocationException error) when (error.InnerException is IOException) { rejected = true; }
    Require(rejected && actor.Record.Players[0].SettlementState == 0,
        "failed settlement changed memory or created wallet");
    Call("CommitPlayerSettlement", manager, actor, playerId);
    Require(Committed(actor).Players[0].Score == 12345 && Committed(actor).Players[0].SettlementState == 1,
        "settlement lost score or checkpoint");
    string before = File.ReadAllText(manager.StorePath);
    Call("CommitPlayerSettlement", manager, actor, playerId);
    Require(File.ReadAllText(manager.StorePath) == before && !before.Contains("RewardWallets") && !before.Contains("PendingRewards"),
        "settlement duplicated writes or persisted removed wallet fields");
});
Check("Schema 1 and 2 records initialize isolated checkpoints without new persisted fields", (manager, actor) =>
{
    foreach (int schema in new[] { 1, 2 })
    {
        var oldStore = new OnlineArcadeRunStoreSnapshot { SchemaVersion = schema, Runs = new() { Record() } };
        string json = JsonSerializer.Serialize(oldStore);
        Require(!json.Contains("CommittedRecord"), "checkpoint became a persisted field");
        var read = JsonSerializer.Deserialize<OnlineArcadeRunStoreSnapshot>(json)!;
        Call("ValidateStore", read);
        Call("CommitActorRecord", actor, read.Runs[0]);
        read.Runs[0].Players[0].RunGold = 999;
        actor.Record.Players[0].RunGold = 175;
        Require(Committed(actor).Players[0].RunGold == 100 && actor.Record.NextArena == 3, "legacy checkpoint aliases source");
    }
});
Check("Mismatched committed run identity refuses persistence", (manager, actor) =>
{
    Committed(actor).RunId = 999;
    string before = File.ReadAllText(manager.StorePath);
    bool rejected = false;
    try { Call("CommitWithoutRun", manager, 202L); }
    catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { rejected = true; }
    Require(rejected && File.ReadAllText(manager.StorePath) == before, "mismatched identity saved");
});
Console.WriteLine($"Server transaction checks: {passed}/{passed + failed} passed.");
World.Instance.Dispose();
Environment.ExitCode = failed == 0 ? 0 : 1;

// 失败注入会走正式日志入口；夹具不创建 NLog 文件或外部服务。
sealed class CheckLogger : ILog
{
    public void Trace(string message) { }
    public void Warning(string message) { }
    public void Info(string message) { }
    public void Debug(string message) { }
    public void Error(string message) { }
    public void Error(Exception error) { }
    public void Trace(ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler message) { }
    public void Warning(ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler message) { }
    public void Info(ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler message) { }
    public void Debug(ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler message) { }
    public void Error(ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler message) { }
}
