using System.Reflection;
using ET;
using ET.Server;

/// <summary>限定验证新增三关路线与旧局保存合同，不运行完整战斗套件。</summary>
internal static class NextRouteOnlineChecks
{
    public static int Run(int maximum)
    {
        var tests = new (string, Action)[] { ("RouteVersionPersists", VersionPersists), ("NewRoutesHave52Rounds", NewRoutes), ("LegacyRouteKeeps40Rounds", LegacyRoute), ("NewLayoutsAreConnected", Layouts), ("GraveWallsFollowCircle", GraveWalls), ("FarmPortalPersistsCorrectRoute", FarmPortal), ("BothFinalCountsPersist", FinalCounts), ("KnownFingerprintsOnly", Fingerprints), ("EnemyMouthsCannotExitBounds", EnemyMouths), ("RunSnapshotRetainsVersion", SnapshotVersion) };
        if (maximum < tests.Length) throw new ArgumentException("NextRouteOnline maximum too small");
        int failed = 0;
        foreach (var (name, test) in tests) try { test(); Console.WriteLine("PASS NextRouteOnline." + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL NextRouteOnline." + name + ": " + (e.InnerException ?? e).Message); }
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed"); return failed == 0 ? 0 : 1;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void VersionPersists()
    {
        var property = typeof(OnlineArcadeRunRecord).GetProperty("CasualRouteVersion");
        Require(property != null, "route version is missing from persisted run");
        var record = new OnlineArcadeRunRecord(); Require((int)property!.GetValue(record)! == 0, "old records must default to legacy route");
        property.SetValue(record, 1); Require((int)property.GetValue(OnlineArcadeRunManagerSystem.Clone(record))! == 1, "clone discarded route version");
        var room=new OnlineRoomActor {RoomId=2};room.Members[1]=new OnlineRoomMember {PlayerId=1,JoinSequence=1};room.Members[2]=new OnlineRoomMember {PlayerId=2,JoinSequence=2};
        var initial=(OnlineArcadeRunRecord)typeof(OnlineArcadeRunManagerSystem).GetMethod("BuildInitialRecord",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{1L,room,ServerConfig.Instance.ArcadeRule})!;
        Require(initial.CasualRouteVersion==1&&initial.IsCasualRoute&&initial.NextArena==1&&initial.ArenaConfigNumber==1,"new official run did not select 52-round route");
    }
    private static void NewRoutes()
    {
        void Visit(int map, int depth)
        {
            var row = ServerConfig.Instance.Tables.TbArcadeArena.Get(map);
            if (row.NextArenaNumbers.Length == 0) { Require(depth == 13 && row.FinalBossId == "arcade.boss.final", "final boss must follow round 52"); return; }
            Require(depth < 13, "route too long"); foreach (int next in row.NextArenaNumbers) Visit(next, depth + 1);
        }
        Visit(1, 1); int[] prefix = { 1, 21, 22, 23, 24, 25, 5, 6 };
        for (int i = 0; i < prefix.Length - 1; i++) Require(ServerConfig.Instance.Tables.TbArcadeArena.Get(prefix[i]).NextArenaNumbers.SequenceEqual(new[] { prefix[i + 1] }), "confirmed prefix changed");
    }
    private static void LegacyRoute()
    {
        var rules = typeof(ServerConfig).Assembly.GetType("FirepowerLoop.Arcade.ArcadeRouteRules");
        Require(rules != null, "legacy route adapter is missing");
        int[] Next(int version, int map) => ((IEnumerable<int>)rules!.GetMethod("NextArenas")!.Invoke(null, new object[] { version, map, ServerConfig.Instance.Tables.TbArcadeArena.Get(map).NextArenaNumbers })!).ToArray();
        Require(Next(0, 22).SequenceEqual(new[] { 5 }), "old run inserted the three new stages");
        Require(Next(1, 22).SequenceEqual(new[] { 23 }), "new run skipped new stages");
        Require((int)rules!.GetMethod("ArenaCount")!.Invoke(null, new object[] { 0 })! == 10, "old completion no longer means 40 rounds");
    }
    private static void Layouts()
    {
        foreach (int map in new[] { 23, 24, 25 })
        {
            Require(ServerConfig.Instance.EarlyStages.TryGetValue(map, out var stage), "new stage boundary missing");
            Require(stage!.HerdCount == 0, "new stage inherited farm herd");
            var layouts = ServerConfig.Instance.Tables.TbArcadeRoundLayout.DataList.Where(p => p.ArenaConfigNumber == map).Select(FirepowerLoop.Arcade.ArcadeRoundLayoutParameters.FromConfig).ToArray();
            Require(layouts.Length == 4 && layouts.All(p => p.EventKind == 0 && p.BossKind == 0 && p.EnemyHealthScale == 1 && p.EnemySpeedScale == 1), "new stage altered base combat budget");
            Require(layouts.All(p => p.FitsArena(stage.HalfWidth, stage.HalfHeight, ServerConfig.Instance.ArcadeRule.PlayerRadius)), "new layout is disconnected");
            Require(layouts.Select(p => string.Join(";", p.Obstacles.Select(o => $"{o.X},{o.Y},{o.HalfWidth},{o.HalfHeight}"))).Distinct().Count() == 1, "four rounds must retain one fixed layout");
        }
    }
    private static void GraveWalls()
    {
        var layout = FirepowerLoop.Arcade.ArcadeRoundLayoutParameters.FromConfig(ServerConfig.Instance.Tables.TbArcadeRoundLayout.Get("arena.23.round.1"));
        Require(layout.Obstacles.Count > 40, "graveyard enclosure was reduced to square walls");
        foreach (int sx in new[] { -1, 1 }) foreach (int sy in new[] { -1, 1 })
            Require(!layout.IsWalkable(sx * 18.4f, sy * 18.4f, .55f, 28, 28), "diagonal graveyard wall leaked");
    }
    private static void EnemyMouths()
    {
        foreach(int map in new[]{23,24,25})
        {
            var layout=FirepowerLoop.Arcade.ArcadeRoundLayoutParameters.FromConfig(ServerConfig.Instance.Tables.TbArcadeRoundLayout.Get($"arena.{map}.round.1"));
            var stage=ServerConfig.Instance.EarlyStages[map];
            foreach(var entry in layout.Entries)
            {
                float x=entry.X,y=entry.Y;
                if(Math.Abs(x)>Math.Abs(y))x=Math.Sign(x)*(map==23?26.9f:map==24?27.9f:25);
                else y=Math.Sign(y)*(map==23?26.9f:map==24?27.9f:23);
                Require(!layout.IsWalkable(x,y,.55f,stage.HalfWidth,stage.HalfHeight),"enemy mouth allows player outside visible enclosure");
                Require(layout.Obstacles.Any(o=>!o.BlocksProjectiles&&Math.Abs(x-o.X)<o.HalfWidth&&Math.Abs(y-o.Y)<o.HalfHeight),"enemy mouth cap must only block walking");
            }
        }
    }
    private static object Call(string name, params object[] arguments) => typeof(OnlineArcadeRunActorSystem).GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, arguments)!;
    private static OnlineArcadeRunActor Actor(int version, int map, int order)
    {
        var rule = ServerConfig.Instance.ArcadeRule;
        var record = new OnlineArcadeRunRecord { RunId = 1, RoomId = 2, Revision = 1, Phase = OnlineArcadeRunPhase.SafeCheckpoint,
            IsCasualRoute = true, CasualRouteVersion = version, NextArena = order, CompletedArena = order - 1, ArenaConfigNumber = map,
            ConfigFingerprint = rule.ConfigFingerprint, CreatedAtUtc = DateTime.UtcNow.ToString("O"), UpdatedAtUtc = DateTime.UtcNow.ToString("O"),
            Players = new() { new() { PlayerId = 1, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId }, new() { PlayerId = 2, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId } } };
        var actor = new OnlineArcadeRunActor { Record = record, CommittedRecord = OnlineArcadeRunManagerSystem.Clone(record), Battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false) };
        typeof(Entity).GetProperty("InstanceId")!.SetValue(actor, 1L);
        foreach (var player in actor.Battle.Players.Values) player.ReadyRoundGeneration = actor.Battle.RoundGeneration;
        return actor;
    }
    private static void CommitAndRead(OnlineArcadeRunActor actor, Action<OnlineArcadeRunRecord> verify)
    {
        string parent = Path.GetFullPath("Tools/ArcadeFullRouteOnlineChecks/.test-output");
        string folder = Path.GetFullPath(Path.Combine(parent, Guid.NewGuid().ToString("N")));
        Require(folder.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "test directory escaped");
        Directory.CreateDirectory(folder);
        try
        {
            var manager = new OnlineArcadeRunManagerComponent { StorePath = Path.Combine(folder, "runs.json") }; manager.Runs[1] = actor;
            var method = typeof(OnlineArcadeRunManagerSystem).GetMethod("TryCommitArenaCompletion", BindingFlags.Static | BindingFlags.NonPublic)!;
            Require((bool)method.Invoke(null, new object[] { manager, actor })!, "completion transaction rejected");
            var options = (System.Text.Json.JsonSerializerOptions)typeof(OnlineArcadeRunManagerSystem).GetMethod("JsonOptions", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
            var saved = System.Text.Json.JsonSerializer.Deserialize<OnlineArcadeRunStoreSnapshot>(File.ReadAllText(manager.StorePath), options)!;
            typeof(OnlineArcadeRunManagerSystem).GetMethod("ValidateStore", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { saved });
            verify(saved.Runs.Single());
        }
        finally { Directory.Delete(folder, true); }
    }
    private static void FarmPortal()
    {
        foreach (int version in new[] { 0, 1 })
        {
            var actor = Actor(version, 22, 3); var battle = actor.Battle; battle.WaveIndex = 3; battle.AwaitingExit = true;
            Call("TickRoundReward", actor, battle, ServerConfig.Instance.RoundReward.PortalAppearSeconds + .01f); Require(battle.StagePortal != null, "farm stage portal missing");
            int target = version == 0 ? 5 : 23; var player = battle.Players[1]; player.PositionX = battle.StagePortal.X; player.PositionZ = battle.StagePortal.Z; player.ConfirmedExitTarget = target;
            Require((bool)Call("TickRoundExit", battle) && battle.ArenaCompletionReady, "farm portal refused version-specific exit");
            CommitAndRead(actor, record => Require(record.CasualRouteVersion == version && record.ArenaConfigNumber == target && record.CompletedArena == 3, "saved farm exit changed route contract"));
        }
    }
    private static void FinalCounts()
    {
        foreach (int version in new[] { 0, 1 })
        {
            int count = version == 0 ? 10 : 13; var actor = Actor(version, 20, count); actor.Battle.WaveIndex = 3; actor.Battle.FinalBossDefeated = true;
            CommitAndRead(actor, record => Require(record.CasualRouteVersion == version && record.CompletedArena == count && record.NextArena == count + 1 && record.Phase == OnlineArcadeRunPhase.Victory, "saved victory lost 40/52-round meaning"));
        }
    }
    private static void Fingerprints()
    {
        var normalize = typeof(OnlineArcadeRunManagerSystem).GetMethod("NormalizeLoadedCheckpoint", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (string fingerprint in new[] { "firepowerloop-20261022-v20-purple-ray", "firepowerloop-20261022-v21-early-stage-data", "firepowerloop-20261022-v22-early-stage-herd", "firepowerloop-20261023-v23-low-tier-skull-open-projectiles", "firepowerloop-20261023-v24-melee-windup" })
        {
            var record = Actor(0, 22, 3).Record; record.ConfigFingerprint = fingerprint; normalize.Invoke(null, new object[] { record });
            Require(record.CasualRouteVersion == 0 && record.ConfigFingerprint == ServerConfig.Instance.ArcadeRule.ConfigFingerprint, "known migration changed legacy route");
            OnlineArcadeRunActorSystem.PrepareBattle(record, false);
        }
        var unknown = Actor(0, 22, 3).Record; unknown.ConfigFingerprint = "firepowerloop-unknown-version"; normalize.Invoke(null, new object[] { unknown });
        bool rejected = false; try { OnlineArcadeRunActorSystem.PrepareBattle(unknown, false); } catch (RpcException) { rejected = true; }
        Require(rejected && unknown.ConfigFingerprint == "firepowerloop-unknown-version", "unknown fingerprint silently migrated");
    }
    private static void SnapshotVersion()
    {
        foreach(int version in new[]{0,1})
        {
            var actor=Actor(version,22,3);var snapshot=OnlineArcadeRunActorSystem.BuildSnapshot(actor.Record);
            Require(snapshot.CasualRouteVersion==version,"snapshot dropped saved route version");
            var packet=MemoryPack.MemoryPackSerializer.Deserialize<OnlineArcadeRunSnapshot>(MemoryPack.MemoryPackSerializer.Serialize(snapshot));
            Require(packet.CasualRouteVersion==version,"generated codec dropped route version");
            Require(OnlineArcadeRunActorSystem.CloneSnapshot(packet).CasualRouteVersion==version,"response cache lost route version");
        }
    }
}
