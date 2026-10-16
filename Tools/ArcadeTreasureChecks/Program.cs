using System.Reflection;
using System.Text.Json;
using ET;
using ET.Server;

// 独立进程调用正式权威入口，临时文件验证事务，不启动服务或监听网络。
string repo = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var config = ServerConfig.CreateForValidation("OnlineLocal", name => File.ReadAllBytes(Path.Combine(repo, "Server/GameServer/Config/Luban", name + ".bytes")));
World.Instance.AddSingleton(config); World.Instance.AddSingleton<TimeInfo>(); World.Instance.AddSingleton<ObjectPool>();
World.Instance.AddSingleton<Logger>().Log = new CheckLogger();
int passed = 0, failed = 0;
void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
object Call(string name, params object[] values) => typeof(OnlineArcadeRunActorSystem).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, values)!;
object Store(string name, params object[] values) => typeof(OnlineArcadeRunManagerSystem).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, values)!;
void Check(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + (e.InnerException ?? e)); }
}
OnlineArcadeRunActor Actor(bool casual = true, int map = 1)
{
    var record = new OnlineArcadeRunRecord { RunId = 101, RoomId = 202, Revision = 1, Phase = OnlineArcadeRunPhase.SafeCheckpoint,
        IsCasualRoute = casual, ArenaConfigNumber = map, NextArena = 1, ConfigFingerprint = config.ArcadeRule.ConfigFingerprint,
        ClientVersion = "treasure-check", CreatedAtUtc = DateTime.UtcNow.ToString("O"), UpdatedAtUtc = DateTime.UtcNow.ToString("O"),
        Players = new() { new() { PlayerId = 1, Lives = 3, Bombs = 2, Dashes = 2, Ready = true, WeaponId = "arcade.weapon.basic" },
            new() { PlayerId = 2, Lives = 3, Bombs = 2, Dashes = 2, Ready = true, WeaponId = "arcade.weapon.basic" } } };
    var a = new OnlineArcadeRunActor { Record = record, CommittedRecord = OnlineArcadeRunManagerSystem.Clone(record), BattleGeneration = 1 };
    typeof(Entity).GetProperty("InstanceId")!.SetValue(a, 101L);
    a.Battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false);
    a.Battle.WaveIndex = 2; a.Battle.SpawnQueue.Clear(); a.Battle.SpawnQueueIndex = 0; a.Record.Phase = OnlineArcadeRunPhase.Arena;
    return a;
}
void Tick(OnlineArcadeRunActor a, float seconds = 0) => Call("TickTreasureRoom", a, a.Battle, seconds);
void Enter(OnlineArcadeRunActor a, long id) { var p = a.Battle.Players[id]; p.PositionX = p.PositionZ = -15; Tick(a); Require(p.InTreasureRoom, "entry rejected"); }
void Leave(OnlineArcadeRunActor a, long id) { var p = a.Battle.Players[id]; p.PositionX = 0; p.PositionZ = -5.5f; Tick(a); Require(!p.InTreasureRoom, "exit rejected"); }
void Collect(OnlineArcadeRunActor a, long id, int rewardId) { var r = a.Battle.TreasureRoom.Parameters.Rewards.Single(r => r.Id == rewardId); var p = a.Battle.Players[id]; p.PositionX = r.X; p.PositionZ = r.Y; Tick(a); }
void Advance(OnlineArcadeRunActor a) { Call("TryAdvanceWave", a, a.Battle, 0f); Call("TryAdvanceWave", a, a.Battle, 2f); }
Check("Uncleared wave reveals without entering", () => { var a = Actor(); a.Battle.SpawnQueue.Add("pending"); a.Battle.Players[1].PositionX = a.Battle.Players[1].PositionZ = -15; Tick(a); Require(a.Battle.TreasureDiscovered && !a.Battle.Players[1].InTreasureRoom, "unlock bypass"); });
// 第三轮清场会自动传送全部存活成员；最后一名返回后才准备第四轮。
Check("Cleared wave enters all members and waits for last return", () =>
{
    var a = Actor(); Enter(a, 1);
    Require(a.Battle.Players.Values.All(p => p.InTreasureRoom && p.TreasureVisited), "clear did not enter all living members");
    Advance(a); Require(a.Battle.WaveIndex == 2, "wave advanced with members inside");
    Leave(a, 1); Require(a.Battle.WaveIndex == 2 && !a.Battle.TreasureCompleted, "first return completed room");
    Leave(a, 2); Require(a.Battle.WaveIndex == 3 && a.Battle.TreasureCompleted && a.Battle.SpawnQueue.Count > 0,
        "last return did not prepare fourth wave");
});
Check("Returning member cannot advance while teammate remains inside", () =>
{
    var a = Actor(); Enter(a, 1); Leave(a, 2); Advance(a);
    Require(a.Battle.WaveIndex == 2 && a.Battle.Players[1].InTreasureRoom && !a.Battle.Players[2].InTreasureRoom,
        "returning member bypassed treasure pause");
    Leave(a, 1); Require(a.Battle.WaveIndex == 3 && a.Battle.TreasureRoom.Members.Length == 0, "last return blocked");
});
Check("Shared reward personal and once", () => { var a = Actor(); Enter(a, 1); Enter(a, 2); Collect(a, 1, 1); Collect(a, 2, 1); Require(a.Record.Players[0].Score == 100 && a.Record.Players[1].Score == 0 && a.Record.Players[0].RunGold == 0, "duplicate or cash"); });
Check("Same-frame stable arbitration", () => { var a = Actor(); Enter(a, 2); Enter(a, 1); foreach (var p in a.Battle.Players.Values) { p.PositionX = -6; p.PositionZ = 0; } Tick(a); Require(a.Record.Players[0].Score == 100 && a.Record.Players[1].Score == 0, "unstable"); });
Check("Members share clock and visited members cannot reenter", () =>
{
    var a = Actor(); Enter(a, 1); Tick(a, 20);
    Require(a.Battle.Players[2].InTreasureRoom && a.Battle.TreasureRoom.RemainingSeconds == 40, "shared clock reset");
    Leave(a, 1); Tick(a, 10);
    Require(a.Battle.TreasureRoom.RemainingSeconds == 30 && a.Battle.Players[2].InTreasureRoom, "return reset teammate clock");
    var returned = a.Battle.Players[1]; returned.PositionX = returned.PositionZ = -15; Tick(a);
    Require(!returned.InTreasureRoom && a.Battle.TreasureRoom.RemainingSeconds == 30, "visited member reentered or reset clock");
    Leave(a, 2); Require(a.Battle.TreasureCompleted && a.Battle.WaveIndex == 3, "empty room did not finish");
});
Check("Stale instance rejected", () => { var a = Actor(); Enter(a, 1); var r = a.Battle.TreasureRoom; Require(!r.TryEnter("stale", 2, true, true, out _) && !r.TryCollect("stale", 1, 1, _ => true, out _), "stale accepted"); });
Check("Full supply stays for teammate", () => { var a = Actor(); Enter(a, 1); Enter(a, 2); a.Record.Players[0].Bombs = 9; Collect(a, 1, 17); Require(a.Battle.TreasureRoom.RemainingRewards.Any(r => r.Id == 17), "full consumed"); Collect(a, 2, 17); Require(a.Record.Players[1].Bombs == 3, "teammate blocked"); });
Check("Overflow has no partial mutation", () => { var a = Actor(); Enter(a, 1); a.Record.Players[0].Score = long.MaxValue; Collect(a, 1, 1); Require(a.Record.Players[0].Score == long.MaxValue && a.Record.Players[0].Lives == 3 && a.Battle.TreasureRoom.RemainingRewards.Any(r => r.Id == 1), "overflow mutated"); });
Check("Threshold personal life old multiplier", () => { var a = Actor(); Enter(a, 1); var r = a.Record.Players[0]; r.Score = 199800; r.ScoreMultiplier = 2; r.MultiplierProgress = 900; Collect(a, 1, 1); Require(r.Score == 200000 && r.ScoreMultiplier == 3 && r.MultiplierProgress == 0 && r.Lives == 4 && a.Record.Players[1].Lives == 3, "threshold wrong"); });
Check("Inside member delays arena exit", () =>
{
    var a = Actor(); Enter(a, 1); Leave(a, 2); a.Battle.AwaitingExit = true;
    void AtExit()
    {
        var p = a.Battle.Players[2]; int exits = config.Tables.TbArcadeArena.Get(1).NextArenaNumbers.Length;
        p.PositionX = -(exits - 1) * 2.5f; p.PositionZ = config.ArcadeRule.ArenaHalfHeight - 3;
    }
    AtExit(); Require(!(bool)Call("TickExit", a.Battle), "inside teammate did not block exit");
    Leave(a, 1); AtExit(); Require((bool)Call("TickExit", a.Battle) && a.Battle.ArenaCompletionReady,
        "empty treasure room blocked valid arena exit");
});
Check("Expiry returns only members locks reentry", () => { var a = Actor(); Enter(a, 1); Enter(a, 2); Leave(a, 2); a.Battle.Players[2].PositionX = 7; Tick(a, 60); Require(!a.Battle.Players[1].InTreasureRoom && a.Battle.Players[1].PositionX == -12 && a.Battle.Players[2].PositionX == 7, "wrong return"); a.Battle.Players[1].PositionX = a.Battle.Players[1].PositionZ = -15; Tick(a); Require(!a.Battle.Players[1].InTreasureRoom, "expired entry"); });
Check("Inside immune and enemy target excluded", () => { var a = Actor(); Enter(a, 1); var p = a.Battle.Players[1]; int hp = p.CurrentHealth; Call("DamagePlayer", a, a.Battle, p, hp + 1); Require(p.Alive && p.CurrentHealth == hp, "room hit"); Advance(a); Call("TrySpawnEnemy", a.Battle, TimeInfo.Instance.ServerNow()); Require(a.Battle.Enemies.Values.All(e => e.TargetPlayerId != 1), "room targeted"); });
Check("Returned member death resets multiplier without team defeat", () =>
{
    var a = Actor(); Enter(a, 1); Leave(a, 2); var p = a.Battle.Players[2]; var r = a.Record.Players[1];
    r.Score = 1234; r.ScoreMultiplier = 4; r.MultiplierProgress = 500;
    Require(!p.InTreasureRoom && p.Alive && p.InvulnerabilitySeconds == 0, "death fixture not vulnerable in arena");
    Call("DamagePlayer", a, a.Battle, p, p.CurrentHealth + 1);
    Require(!p.Alive && p.CurrentHealth == 0 && r.Lives == 2 && r.Score == 1234 && r.ScoreMultiplier == 1 &&
        r.MultiplierProgress == 0 && !a.Battle.AllPlayersDefeated && a.Battle.Players[1].InTreasureRoom,
        "death lost score, failed multiplier reset, or defeated inside teammate");
});
Check("Old route other map challenge excluded", () => { foreach (var a in new[] { Actor(false), Actor(true, 2), Actor() }) { if (a.Record.IsCasualRoute && a.Record.ArenaConfigNumber == 1) a.Battle.IsChallenge = true; Tick(a); Require(a.Battle.TreasureRoom == null, "wrong identity"); } });
Check("Projection and record clones", () => { var a = Actor(); Enter(a, 1); Collect(a, 1, 1); var b = (OnlineArcadeBattleInfo)Call("BuildBattleSnapshot", a.Battle); var c = (OnlineArcadeBattleInfo)Call("CloneBattleSnapshot", b); Require(c.TreasureRewardIds.Count == 17 && c.Players.Single(p => p.PlayerId == 1).InTreasureRoom && c.TreasureRoomId == b.TreasureRoomId && OnlineArcadeRunManagerSystem.Clone(a.Record).Players[0].Score == 100, "clone lost"); });
Check("Old JSON defaults preserve old lives", () => { var r = JsonSerializer.Deserialize<OnlineArcadePlayerRecord>("{\"PlayerId\":1,\"Lives\":12}")!; Require(r.Score == 0 && r.ScoreMultiplier == 1 && r.MultiplierProgress == 0 && r.Lives == 12, "old default"); });
Check("Failed completion and disconnect rollback", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "FirepowerLoopTreasureChecks", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
    try {
        var a = Actor(); var m = new OnlineArcadeRunManagerComponent { StorePath = Path.Combine(root, "runs.json") };
        Store("CommitWithCandidate", m, a.CommittedRecord, 0L); m.Runs.Add(101, a); Enter(a, 1); Collect(a, 1, 1); a.Battle.SelectedExitConfigNumber = 2; m.RemainingFailSaveCount = 1;
        Require(!(bool)Store("TryCommitArenaCompletion", m, a) && a.Battle != null && a.Record.Players[0].Score == 100 && a.CommittedRecord.Players[0].Score == 0, "failed save leaked");
        Require((bool)Store("TryRollbackDisconnectedBattle", m, a) && a.Battle == null && a.Record.Players[0].Score == 0, "rollback leaked");
        Require(JsonSerializer.Deserialize<OnlineArcadeRunStoreSnapshot>(File.ReadAllText(m.StorePath))!.Runs.Single().Players[0].Score == 0, "disk leaked");
    } finally { Directory.Delete(root, true); }
});
Check("Entering removes own arena projectiles", () =>
{
    var a = Actor(); a.Battle.Projectiles.Add(99, new OnlineArcadeProjectileRuntime { RuntimeId = 99, OwnerPlayerId = 1 });
    Enter(a, 1); Require(!a.Battle.Projectiles.ContainsKey(99), "inside owner projectile can still damage arena");
});
Check("Score completion failure retries and commits exactly once", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "FirepowerLoopTreasureChecks", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
    try
    {
        var a = Actor(); var m = new OnlineArcadeRunManagerComponent { StorePath = Path.Combine(root, "runs.json") };
        Store("CommitWithCandidate", m, a.CommittedRecord, 0L); m.Runs.Add(101, a); Enter(a, 1); Collect(a, 1, 1); Leave(a, 1);
        a.Battle.SelectedExitConfigNumber = 2; m.RemainingFailSaveCount = 1;
        Require(!(bool)Store("TryCommitArenaCompletion", m, a), "failure not injected");
        Require((bool)Store("TryCommitArenaCompletion", m, a), "retry rejected");
        Require(a.Battle == null && a.CommittedRecord.Players[0].Score == 100 && a.Record.NextArena == 2, "retry score or checkpoint wrong");
        Store("CommitWithoutRun", m, 999L);
        Require(JsonSerializer.Deserialize<OnlineArcadeRunStoreSnapshot>(File.ReadAllText(m.StorePath))!.Runs.Single().Players[0].Score == 100, "subsequent save changed score");
    }
    finally { Directory.Delete(root, true); }
});
Check("Inside timed powerup expires without cross-region damage", () =>
{
    var a = Actor(); Enter(a, 1); var p = a.Battle.Players[1];
    p.Powerups.Add(ArcadePowerupBehavior.Decoy, new OnlineArcadePowerupRuntime { Behavior = ArcadePowerupBehavior.Decoy, RemainingSeconds = .1f, PositionX = 0, PositionZ = 0, Radius = 5, Damage = 10 });
    var enemy = new OnlineArcadeEnemyRuntime { RuntimeId = 88, CurrentHealth = 100, PositionX = 0, PositionZ = 0 };
    a.Battle.Enemies.Add(88, enemy); a.Battle.EnemyIterationIds.Add(88); Call("TickPowerups", a, a.Battle, 1f);
    Require(enemy.CurrentHealth == 100 && p.Powerups.Count == 0, "timer froze or cross-region decoy exploded");
});
Console.WriteLine($"Treasure checks: {passed} PASS, {failed} FAIL");
return failed == 0 ? 0 : 1;


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
