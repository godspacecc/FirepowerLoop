using System.Reflection;
using System.Text.Json;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;
using MemoryPack;

// 必须精确匹配真实短类和固定上限；缺失、重复、未知过滤都退出2，不回退全套。
var options = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (string argument in args)
{
    var pair = argument.Split('=', 2);
    if (pair.Length != 2 || !options.TryAdd(pair[0], pair[1])) return 2;
}
if (options.Count != 3 || options.GetValueOrDefault("testClass") != "TenStageRouteOnline" ||
    options.GetValueOrDefault("maxTests") != "8" || options.GetValueOrDefault("includePassingTests") != "true")
{ Console.Error.WriteLine("Requires testClass=TenStageRouteOnline maxTests=8 includePassingTests=true"); return 2; }
World.Instance.AddSingleton<Logger>().Log = new TenStageRouteLogger();
World.Instance.AddSingleton(ServerConfig.CreateForValidation("OnlineLocal", n => File.ReadAllBytes(Path.Combine("Server/GameServer/Config/Luban", n + ".bytes"))));
World.Instance.AddSingleton<TimeInfo>(); World.Instance.AddSingleton<ObjectPool>();
return TenStageRouteOnline.Run();

/// <summary>只验证十关双人路线、四十普通轮与旧局恢复，不启动网络监听或读取用户数据。</summary>
internal static class TenStageRouteOnline
{
    private static readonly int[] Route = { 1, 21, 22, 23, 24, 25, 5, 7, 10, 20 };
    private const string ArtifactRoot = "Backup/Artifacts/TenStageRouteApplied-20261025";

    public static int Run()
    {
        var tests = new (string Name, Action Test)[] {
            ("NewOfficialRunUsesVersionTwo", NewOfficialRun), ("TenStagesPlayFortyRoundsRewardsAndFinalBoss", FortyRounds),
            ("LegacyZeroKeepsThreeBranches", () => LegacyBranches(0)), ("LegacyOneKeepsThreeBranches", () => LegacyBranches(1)),
            ("AllVersionsResumeRoundCheckpoint", RoundResume), ("VictoryCountsKeepSavedVersion", FinalCounts),
            ("KnownFingerprintsMigrateWithoutChangingRoute", Fingerprints), ("SnapshotCodecKeepsAllVersions", SnapshotVersions) };
        int failed = 0;
        foreach (var (name, test) in tests)
            try { test(); Console.WriteLine("PASS TenStageRouteOnline." + name); }
            catch (Exception e) { failed++; Console.WriteLine("FAIL TenStageRouteOnline." + name + ": " + (e.InnerException ?? e)); }
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed"); return failed == 0 ? 0 : 1;
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static object Call(Type owner, string name, params object[] args) => owner.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(null, args)!;
    private static object ActorCall(string name, params object[] args) => Call(typeof(OnlineArcadeRunActorSystem), name, args);
    private static object ManagerCall(string name, params object[] args) => Call(typeof(OnlineArcadeRunManagerSystem), name, args);

    private static void NewOfficialRun()
    {
        var room = new OnlineRoomActor { RoomId = 2 };
        room.Members[1] = new OnlineRoomMember { PlayerId = 1, JoinSequence = 1 };
        room.Members[2] = new OnlineRoomMember { PlayerId = 2, JoinSequence = 2 };
        var run = (OnlineArcadeRunRecord)ManagerCall("BuildInitialRecord", 1L, room, ServerConfig.Instance.ArcadeRule);
        Require(run.CasualRouteVersion == 2 && run.IsCasualRoute && run.ArenaConfigNumber == 1 && run.NextArena == 1, "official new run did not select confirmed ten-stage route");
        Require(OnlineArcadeRunActorSystem.PrepareBattle(run, false, false).CasualRouteVersion == 2, "battle preparation lost new route");
    }

    private static OnlineArcadeRunActor Actor(int version, int map, int order)
    {
        var rule = ServerConfig.Instance.ArcadeRule;
        var record = new OnlineArcadeRunRecord { RunId = 1, RoomId = 2, Revision = 1,
            Phase = OnlineArcadeRunPhase.SafeCheckpoint, IsCasualRoute = true, CasualRouteVersion = version,
            ArenaConfigNumber = map, NextArena = order, CompletedArena = order - 1, ConfigFingerprint = rule.ConfigFingerprint,
            CreatedAtUtc = DateTime.UtcNow.ToString("O"), UpdatedAtUtc = DateTime.UtcNow.ToString("O"),
            Players = new() { new() { PlayerId = 1, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId },
                new() { PlayerId = 2, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId } } };
        var actor = new OnlineArcadeRunActor { Record = record, CommittedRecord = OnlineArcadeRunManagerSystem.Clone(record),
            Battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false) };
        typeof(Entity).GetProperty("InstanceId")!.SetValue(actor, 1L); Ready(actor); return actor;
    }

    private static void Ready(OnlineArcadeRunActor actor)
    {
        foreach (var p in actor.Battle.Players.Values)
        { p.ReadyRoundGeneration = actor.Battle.RoundGeneration; p.PositionX = actor.Battle.RoundLayout.SpawnX; p.PositionZ = actor.Battle.RoundLayout.SpawnY; }
    }

    private static void WithStore(OnlineArcadeRunActor actor, Action<OnlineArcadeRunManagerComponent> action)
    {
        string parent = Path.GetFullPath(ArtifactRoot);
        string folder = Path.GetFullPath(Path.Combine(parent, "isolated-" + Guid.NewGuid().ToString("N")));
        Require(folder.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "temporary store escaped artifacts directory");
        Directory.CreateDirectory(folder);
        try { var manager = new OnlineArcadeRunManagerComponent { StorePath = Path.Combine(folder, "runs.json") }; manager.Runs[1] = actor; action(manager); }
        finally { Directory.Delete(folder, true); }
    }

    private static OnlineArcadeRunRecord ReadStore(OnlineArcadeRunManagerComponent manager)
    {
        var options = (JsonSerializerOptions)ManagerCall("JsonOptions");
        var saved = JsonSerializer.Deserialize<OnlineArcadeRunStoreSnapshot>(File.ReadAllText(manager.StorePath), options)!;
        ManagerCall("ValidateStore", saved); return saved.Runs.Single();
    }

    /// <summary>清除现场敌群作为战斗夹具，继续真实清轮、宝箱、轮前保存、Boss和出口事务。</summary>
    private static void FortyRounds()
    {
        var actor = Actor(2, 1, 1); int ordinaryRounds = 0;
        WithStore(actor, manager =>
        {
            for (int stage = 0; stage < Route.Length; stage++)
            {
                Require(actor.Record.ArenaConfigNumber == Route[stage], "route entered an unselected arena");
                if (stage > 0) { actor.Battle = OnlineArcadeRunActorSystem.PrepareBattle(actor.Record, false, false); Ready(actor); }
                for (int round = 0; round < 4; round++)
                {
                    var b = actor.Battle; Ready(actor);
                    Require(b.WaveIndex == round && b.RoundLayout.RoundNumber == round + 1, "battle prepared wrong round");
                    Require(b.SpawnQueue.Count == Math.Min(110 + ordinaryRounds * 10, 400) * 2, "coop ordinary budget reset at a stage boundary");
                    b.SpawnQueue.Clear(); b.SpawnQueueIndex = 0; b.Enemies.Clear(); ActorCall("TryAdvanceWave", actor, b, 0f); ordinaryRounds++;
                    if (round == 3 && b.RoundLayout.BossKind > 0)
                    {
                        Require(b.RoundReward == null && b.StagePortal == null && !b.FinalBossDefeated, "boss stage awarded victory before encounter");
                        EnterPassage(actor, Route[stage]);
                        Require(b.PendingBossEncounter && (bool)ManagerCall("TryCommitRoundTransition", manager, actor), "boss encounter checkpoint failed");
                        b = actor.Battle; Ready(actor);
                        Require(b.RoundBossEncounter && b.WaveIndex == 3 && b.RoundLayout.RoundNumber == 4, "boss added an ordinary round");
                        Require((bool)ActorCall("SpawnQueuedEnemy", b, 0L, -1), "boss did not spawn");
                        var boss = b.Enemies.Values.Single(); Require(boss.ContentBossKind > 0, "boss has no content identity");
                        ActorCall("ResolveEnemyDeath", actor, b, boss, true, false, boss.PositionX, boss.PositionZ, 0, true);
                        Require(b.RoundBossDefeated && b.FinalBossDefeated == (stage == 9), "boss completion changed total victory semantics");
                    }
                    CollectReward(actor, (stage * 4) + round + 1);
                    if (round < 3)
                    {
                        EnterPassage(actor, Route[stage]);
                        Require((bool)ManagerCall("TryCommitRoundTransition", manager, actor), "ordinary round checkpoint failed");
                        var savedRound = ReadStore(manager);
                        Require(savedRound.RoundNumber == round + 2 && savedRound.CasualRouteVersion == 2, "round save lost route or round identity");
                    }
                    else
                    {
                        int target = stage == 9 ? 0 : Route[stage + 1];
                        EnterPortal(actor, target);
                        Require((bool)ManagerCall("TryCommitArenaCompletion", manager, actor), "stage completion transaction failed");
                        var saved = ReadStore(manager);
                        Require(saved.CompletedArena == stage + 1 && saved.CasualRouteVersion == 2, "saved stage count lost route version");
                        Require(saved.Phase == (stage == 9 ? OnlineArcadeRunPhase.Victory : OnlineArcadeRunPhase.SafeCheckpoint), "saved phase ended early");
                        if (stage < 9) Require(saved.ArenaConfigNumber == target, "saved next arena diverged from confirmed list");
                    }
                }
            }
            Require(ordinaryRounds == 40 && actor.Record.CompletedArena == 10 && actor.Record.Players.Sum(p => p.Score) > 0,
                "ten-stage result did not preserve forty ordinary rounds and score");
        });
    }

    private static void EnterPassage(OnlineArcadeRunActor actor, int target)
    {
        var b = actor.Battle; var exit = b.RoundLayout.Entries[0]; var player = b.Players[1];
        player.PositionX = exit.X; player.PositionZ = exit.Y; player.ConfirmedExitTarget = target;
        Require((bool)ActorCall("TickRoundExit", b) && b.RoundTransitionPending, "round passage rejected saved route target");
    }

    private static void CollectReward(OnlineArcadeRunActor actor, int identity)
    {
        var b = actor.Battle; var reward = b.RoundReward;
        Require(reward != null && reward.RoundIdentity == identity, "round reward identity missing");
        ActorCall("StartRoundReward", b); Require(ReferenceEquals(reward, b.RoundReward), "same round duplicated reward box");
        var item = reward.Items[0]; var p = b.Players[1]; p.PositionX = item.X; p.PositionZ = item.Z;
        long before = actor.Record.Players.Sum(r => r.Score); ActorCall("TickRoundReward", actor, b, .1f); ActorCall("TickDrops", actor, b);
        Require(actor.Record.Players.Sum(r => r.Score) == before, "reward collected before effect ended");
        ActorCall("TickRoundReward", actor, b, 3.3f); ActorCall("TickDrops", actor, b);
        long after = actor.Record.Players.Sum(r => r.Score); Require(after > before, "reward could not be collected");
        ActorCall("TickDrops", actor, b); Require(actor.Record.Players.Sum(r => r.Score) == after, "same shared reward collected twice");
    }

    private static void EnterPortal(OnlineArcadeRunActor actor, int target)
    {
        var b = actor.Battle; ActorCall("TickRoundReward", actor, b, ServerConfig.Instance.RoundReward.PortalAppearSeconds + .01f);
        Require(b.StagePortal != null, "cleared stage has no portal"); var p = b.Players[1];
        p.PositionX = b.StagePortal.X + 4; p.PositionZ = b.StagePortal.Z; ActorCall("TickRoundReward", actor, b, .01f);
        p.PositionX = b.StagePortal.X; p.ConfirmedExitTarget = target;
        Require((bool)ActorCall("TickRoundExit", b) && b.ArenaCompletionReady && b.SelectedExitConfigNumber == target, "stage portal refused version-specific target");
    }

    private static void LegacyBranches(int version)
    {
        foreach (var item in new[] { (5, 6, version == 0 ? 4 : 7), (7, 9, version == 0 ? 6 : 9), (7, 10, version == 0 ? 6 : 9), (10, 11, version == 0 ? 7 : 10), (22, version == 0 ? 5 : 23, 3) })
        {
            var actor = Actor(version, item.Item1, item.Item3); actor.Battle.WaveIndex = 3; actor.Battle.AwaitingExit = true;
            // 老分支7有两个出口，轮布局入口继续按既有首目标选路；结算分别覆盖两个真实合法后继。
            if (item.Item1 == 7 && item.Item2 == 10) actor.Battle.SelectedExitConfigNumber = 10;
            else EnterPortal(actor, item.Item2);
            WithStore(actor, manager =>
            {
                Require((bool)ManagerCall("TryCommitArenaCompletion", manager, actor), "legacy branch completion refused");
                var saved = ReadStore(manager);
                Require(saved.CasualRouteVersion == version && saved.ArenaConfigNumber == item.Item2 && saved.CompletedArena == item.Item3, "legacy save was shortened or rerouted");
            });
        }
        var routes = version == 0 ? new[] { new[] { 7, 9, 11, 12, 20 }, new[] { 8, 10, 11, 12, 20 }, new[] { 13, 16, 18, 19, 20 } }
            : new[] { new[] { 7, 9, 11, 12, 20 }, new[] { 8, 10, 11, 12, 20 }, new[] { 14, 17, 18, 19, 20 } };
        foreach (int[] suffix in routes) for (int i = 0; i < suffix.Length - 1; i++)
            Require(ArcadeRouteRules.NextArenas(version, suffix[i], ServerConfig.Instance.Tables.TbArcadeArena.Get(suffix[i]).NextArenaNumbers).Contains(suffix[i + 1]), "old alternative branch edge disappeared");
    }

    private static void RoundResume()
    {
        foreach (var identity in new[] { (0, 6, 5), (1, 12, 12), (2, 10, 9) })
        {
            var actor = Actor(identity.Item1, identity.Item2, identity.Item3); var record = actor.Record;
            var layout = ServerConfig.Instance.Tables.TbArcadeRoundLayout.Get($"arena.{identity.Item2}.round.3");
            record.RoundNumber = 3; record.RoundLayoutId = layout.Id; record.RoundLayoutVersion = layout.Version;
            foreach (var p in record.Players) { p.RoundHealth = 73; p.RoundAlive = true; }
            var restored = OnlineArcadeRunActorSystem.PrepareBattle(OnlineArcadeRunManagerSystem.Clone(record), false);
            Require(restored.CasualRouteVersion == identity.Item1 && restored.ArenaConfigNumber == identity.Item2 && restored.WaveIndex == 2 && restored.Players.Values.All(p => p.CurrentHealth == 73), "round resume changed version/map/player identity");
        }
        // 正式环境目录新增5后，旧顺序局仍是原1—20循环，不能按视觉资源目录过滤掉巨石。
        foreach (var identity in new[] { (5, 5), (20, 20), (21, 1) })
        {
            var record = Actor(0, 5, identity.Item1).Record;
            record.IsCasualRoute = false; record.ArenaConfigNumber = 0;
            Require(OnlineArcadeRunActorSystem.PrepareBattle(record, false).ArenaConfigNumber == identity.Item2,
                "legacy sequential cycle changed after adding the Henge environment");
        }
    }

    private static void FinalCounts()
    {
        foreach (int version in new[] { 0, 1, 2 })
        {
            int count = version == 1 ? 13 : 10; var actor = Actor(version, 20, count); actor.Battle.WaveIndex = 3; actor.Battle.FinalBossDefeated = true;
            WithStore(actor, manager =>
            {
                Require((bool)ManagerCall("TryCommitArenaCompletion", manager, actor), "versioned victory refused"); var saved = ReadStore(manager);
                Require(saved.CasualRouteVersion == version && saved.CompletedArena == count && saved.NextArena == count + 1 && saved.Phase == OnlineArcadeRunPhase.Victory, "victory count reinterpreted old run");
            });
        }
    }

    private static void Fingerprints()
    {
        foreach (int version in new[] { 0, 1 }) foreach (string fingerprint in new[] {
            "firepowerloop-20261022-v20-purple-ray", "firepowerloop-20261022-v21-early-stage-data", "firepowerloop-20261022-v22-early-stage-herd",
            "firepowerloop-20261023-v23-low-tier-skull-open-projectiles", "firepowerloop-20261023-v24-melee-windup", "firepowerloop-20261023-v25-next-three-52",
            "firepowerloop-20261023-v25-next-three-52-contact-grab", "firepowerloop-20261023-v25-next-three-52-contact-grab-dock-sea-entry" })
        {
            var run = Actor(version, 22, 3).Record; run.ConfigFingerprint = fingerprint; ManagerCall("NormalizeLoadedCheckpoint", run);
            Require(run.CasualRouteVersion == version && run.ConfigFingerprint == ServerConfig.Instance.ArcadeRule.ConfigFingerprint, "known fingerprint migration dropped route version");
            OnlineArcadeRunActorSystem.PrepareBattle(run, false);
        }
        var unknown = Actor(1, 22, 3).Record; unknown.ConfigFingerprint = "firepowerloop-unknown-version"; ManagerCall("NormalizeLoadedCheckpoint", unknown);
        bool refused = false; try { OnlineArcadeRunActorSystem.PrepareBattle(unknown, false); } catch (RpcException) { refused = true; }
        Require(refused && unknown.ConfigFingerprint == "firepowerloop-unknown-version", "unknown fingerprint was silently accepted");
    }

    private static void SnapshotVersions()
    {
        foreach (int version in new[] { 0, 1, 2 })
        {
            var actor = Actor(version, 22, 3); using var original = OnlineArcadeRunActorSystem.BuildSnapshot(actor.Record);
            using var packet = MemoryPackSerializer.Deserialize<OnlineArcadeRunSnapshot>(MemoryPackSerializer.Serialize(original));
            using var clone = OnlineArcadeRunActorSystem.CloneSnapshot(packet!);
            Require(clone.CasualRouteVersion == version && clone.NextArena == 3, "codec or response clone lost route version");
        }
    }
}

/// <summary>独立工具没有服务器宿主，显式提供日志端口以保留原保存错误而非掩盖成空引用。</summary>
internal sealed class TenStageRouteLogger : ILog
{
    public void Trace(string message) { }
    public void Warning(string message) => Console.Error.WriteLine(message);
    public void Info(string message) { }
    public void Debug(string message) { }
    public void Error(string message) => Console.Error.WriteLine(message);
    public void Error(Exception error) => Console.Error.WriteLine(error);
    public void Trace(ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler message) { }
    public void Warning(ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler message) => Console.Error.WriteLine(message.ToStringAndClear());
    public void Info(ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler message) { }
    public void Debug(ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler message) { }
    public void Error(ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler message) => Console.Error.WriteLine(message.ToStringAndClear());
}
