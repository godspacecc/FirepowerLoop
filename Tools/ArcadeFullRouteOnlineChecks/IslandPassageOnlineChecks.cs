using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>仅覆盖海岛通道的双人权威链路，不启动服务器、不访问玩家存档。</summary>
internal static class IslandPassageOnlineChecks
{
    private static object Call(string name, params object[] values) => typeof(OnlineArcadeRunActorSystem)
        .GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, values)!;

    public static int Run(int maximum)
    {
        var checks = new (string Name, Action Check)[] {
            ("AllTwelvePassagesSpawnOutsideTheirRealEntry", SpawnEntries),
            ("AllTwelvePassagesApproachContinuously", Approach),
            ("ActiveTimeRandomizesOnlyAtSegmentBoundary", ActiveTime),
            ("LoadingAndTransitionFreezePassages", LoadingFreeze),
            ("RespawnAndTreasureFreezePassagesAndSpawning", RespawnFreeze),
            ("ThreeClearExitsUseTheirActualPassages", ExitContact)
        };
        if (maximum < checks.Length) throw new ArgumentException("Declared maximum smaller than IslandPassageOnline scope");
        int failed = 0;
        foreach (var test in checks)
            try { test.Check(); Console.WriteLine("PASS IslandPassageOnline." + test.Name); }
            catch (Exception e) { failed++; Console.WriteLine("FAIL IslandPassageOnline." + test.Name + ": " + (e.InnerException ?? e)); }
        Console.WriteLine($"{checks.Length - failed}/{checks.Length} passed"); return failed == 0 ? 0 : 1;
    }

    private static OnlineArcadeRunActor Create(int round = 1)
    {
        var rule = ServerConfig.Instance.ArcadeRule;
        var record = new OnlineArcadeRunRecord { RunId = 1, RoomId = 2, Revision = 1, NextArena = 1,
            ArenaConfigNumber = 1, RoundNumber = round, IsCasualRoute = true, ConfigFingerprint = rule.ConfigFingerprint,
            Players = new() { new() { PlayerId = 1, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId },
                new() { PlayerId = 2, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId } } };
        var actor = new OnlineArcadeRunActor { Record = record, Battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false) };
        foreach (var p in actor.Battle.Players.Values) { p.ReadyRoundGeneration = actor.Battle.RoundGeneration; p.InvulnerabilitySeconds = 1000; }
        actor.Battle.NextSpawnAt = 0; actor.Battle.LastTickAt = 1000;
        return actor;
    }

    private static OnlineArcadeEnemyRuntime Spawn(OnlineArcadeRunActor actor, int passage)
    {
        int seed = 0; while (new Random(seed).Next(3) != passage) seed++;
        var b = actor.Battle; b.IslandPassages = new ArcadePassageSchedule(12, 3, new Random(seed));
        Require((bool)Call("TrySpawnEnemy", b, 10000L), "Expected one spawn"); return b.Enemies.Values.Single();
    }

    private static void SpawnEntries()
    {
        for (int round = 1; round <= 4; round++) for (int passage = 0; passage < 3; passage++)
        {
            var actor = Create(round); var e = Spawn(actor, passage); var entry = actor.Battle.RoundLayout.Entries[passage];
            Require(Math.Max(Math.Abs(e.PositionX), Math.Abs(e.PositionZ)) > 19.5f, "Enemy appeared inside arena");
            Require((e.PositionX * entry.X + e.PositionZ * entry.Y) /
                Math.Sqrt((e.PositionX * e.PositionX + e.PositionZ * e.PositionZ) * (entry.X * entry.X + entry.Y * entry.Y)) > .995, "Wrong passage");
        }
    }

    private static void Approach()
    {
        var rule = ServerConfig.Instance.ArcadeRule;
        for (int round = 1; round <= 4; round++) for (int passage = 0; passage < 3; passage++)
        {
            var actor = Create(round); var e = Spawn(actor, passage); var region = new ArcadeIslandRegion(19.5f, 19.5f, round);
            foreach (var p in actor.Battle.Players.Values) { p.PositionX = p.PositionZ = 0; }
            for (int i = 0; i < 800 && !region.ContainsCircle(e.PositionX, e.PositionZ, rule.EnemyContactRadius); i++)
            {
                float x = e.PositionX, y = e.PositionZ; Call("TickEnemies", actor, actor.Battle, .05f);
                Require(Math.Sqrt(Math.Pow(e.PositionX - x, 2) + Math.Pow(e.PositionZ - y, 2)) <= e.MoveSpeed * .05f + .01f, "Passage entry teleported");
            }
            Require(region.ContainsCircle(e.PositionX, e.PositionZ, rule.EnemyContactRadius), "Enemy failed to enter combat area");
        }
    }

    private static void Clock(OnlineArcadeRunActor a)
    { a.Battle.IslandPassages = new ArcadePassageSchedule(.3f, 3, new CycleRandom()); a.Battle.IslandPassages.Advance(0); a.Battle.MaxAlive = 0; }
    private static void ActiveTime()
    {
        var a = Create(); Clock(a); a.TickBattle(1200); Require(a.Battle.IslandPassages.Current == 0, "Changed before segment ended");
        a.TickBattle(1400); Require(a.Battle.IslandPassages.Current == 1, "Active clock failed to select next random passage");
    }
    private static void LoadingFreeze()
    {
        foreach (bool loading in new[] { true, false })
        {
            var a = Create(); Clock(a);
            if (loading) a.Battle.Players[1].ReadyRoundGeneration = -1; else a.Battle.RoundTransitionPending = true;
            a.TickBattle(1200); a.TickBattle(1400);
            Require(a.Battle.IslandPassages.Current == 0, "Inactive map consumed passage time");
        }
    }
    private static void RespawnFreeze()
    {
        foreach (bool respawning in new[] { true, false })
        {
            var a = Create(); Clock(a); a.Battle.MaxAlive = 100;
            if (respawning) a.Battle.Players[1].RespawnSeconds = 100; else a.Battle.Players[1].InTreasureRoom = true;
            a.TickBattle(1200); a.TickBattle(1400);
            Require(a.Battle.IslandPassages.Current == 0, "Inactive players consumed passage time");
            Require(a.Battle.Enemies.Count == 0, "Spawn budget consumed while waiting");
        }
    }
    private static void ExitContact()
    {
        for (int round = 1; round <= 3; round++) for (int passage = 0; passage < 3; passage++)
        {
            var a = Create(round); var b = a.Battle; b.AwaitingExit = true; b.WaveClearAwarded = true;
            var exit = b.RoundLayout.GetExitPosition(passage, 19.5f, 19.5f, .55f);
            b.Players[1].PositionX = exit.X; b.Players[1].PositionZ = exit.Y; b.Players[1].ConfirmedExitTarget = 1;
            Require((bool)Call("TickRoundExit", b) && b.RoundTransitionPending, "Clear passage did not request next round");
        }
    }
    private sealed class CycleRandom : Random { private int sequence; public override int Next(int maxValue) => sequence++ % maxValue; }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
