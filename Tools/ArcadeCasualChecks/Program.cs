using System.Reflection;
using ET;
using ET.Server;

// 独立进程验证正式服务端规则；只使用临时文件，不启动服务器、监听网络或改写玩家档案。
string repository = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var config = ServerConfig.CreateForValidation("OnlineLocal", name =>
    File.ReadAllBytes(Path.Combine(repository, "Server/GameServer/Config/Luban", name + ".bytes")));
World.Instance.AddSingleton(config);
World.Instance.AddSingleton<TimeInfo>();
World.Instance.AddSingleton<ObjectPool>();
int passed = 0;
void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
object Invoke(Type type, string method, params object[] values) =>
    type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, values)!;
object BattleCall(string method, params object[] values) => Invoke(typeof(OnlineArcadeRunActorSystem), method, values);
void Check(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
OnlineArcadeRunRecord Record(int ordinal = 10, int map = 20) => new()
{
    RunId = 101, RoomId = 202, Revision = 1, Phase = OnlineArcadeRunPhase.SafeCheckpoint,
    IsCasualRoute = true, ArenaConfigNumber = map, CompletedArena = ordinal - 1, NextArena = ordinal,
    ConfigFingerprint = config.ArcadeRule.ConfigFingerprint, CreatedAtUtc = DateTime.UtcNow.ToString("O"),
    UpdatedAtUtc = DateTime.UtcNow.ToString("O"),
    Players = new() { new() { PlayerId = 1, Lives = 3, Bombs = 2, Ready = true, WeaponId = "arcade.weapon.basic" },
        new() { PlayerId = 2, Lives = 3, Bombs = 2, Ready = true, WeaponId = "arcade.weapon.basic" } },
};
OnlineArcadeBattleRuntime FinalBattle()
{
    var battle = OnlineArcadeRunActorSystem.PrepareBattle(Record(), false);
    battle.SpawnQueue.Clear(); battle.SpawnQueueIndex = 0; battle.WaveIndex = 3;
    BattleCall("BeginFinalBoss", battle, config.Tables.TbArcadeArena.Get(20));
    BattleCall("TrySpawnEnemy", battle, TimeInfo.Instance.ServerNow());
    return battle;
}

Check("All routes have ten arenas and forty main rounds", () =>
{
    var path = new HashSet<int>();
    void Visit(int id, int depth)
    {
        Require(path.Add(id), "route cycle");
        var arena = config.Tables.TbArcadeArena.Get(id);
        Require(arena.WaveGroupIds.Length == 4, "wave count");
        if (arena.NextArenaNumbers.Length == 0)
            Require(depth == 10 && arena.FinalBossId == "arcade.boss.final", "terminal depth");
        else foreach (int next in arena.NextArenaNumbers) Visit(next, depth + 1);
        path.Remove(id);
    }
    Visit(1, 1);
});
Check("Any alive member can select a route", () =>
{
    var battle = OnlineArcadeRunActorSystem.PrepareBattle(Record(1, 1), false);
    battle.AwaitingExit = true;
    battle.Players[2].PositionX = 2.5f; battle.Players[2].PositionZ = config.ArcadeRule.ArenaHalfHeight - 3f;
    Require((bool)BattleCall("TickExit", battle), "member exit rejected");
    Require(battle.SelectedExitConfigNumber == 3 && battle.ArenaCompletionReady, "wrong member route");
});
Check("Simultaneous exits have a stable single result; dead members cannot select", () =>
{
    var battle = OnlineArcadeRunActorSystem.PrepareBattle(Record(1, 1), false);
    battle.AwaitingExit = true;
    battle.Players[1].PositionX = -2.5f; battle.Players[2].PositionX = 2.5f;
    foreach (var player in battle.Players.Values) player.PositionZ = config.ArcadeRule.ArenaHalfHeight - 3f;
    BattleCall("TickExit", battle);
    Require(battle.SelectedExitConfigNumber == 2, "unstable simultaneous arbitration");
    battle.ArenaCompletionReady = false; battle.Players[1].Alive = false;
    BattleCall("TickExit", battle);
    Require(battle.SelectedExitConfigNumber == 3, "dead member selected route");
});
Check("Final main wave starts a separate Boss without advancing progress", () =>
{
    var record = Record(); var actor = new OnlineArcadeRunActor { Record = record };
    var battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false);
    battle.SpawnQueue.Clear(); battle.SpawnQueueIndex = 0; battle.WaveIndex = 3;
    battle.WaveClearAwarded = true;
    BattleCall("TryAdvanceWave", actor, battle, 2f);
    Require(battle.IsFinalBoss && battle.WaveIndex == 3 && !battle.ArenaCompletionReady, "Boss replaced round 40");
    BattleCall("TrySpawnEnemy", battle, TimeInfo.Instance.ServerNow());
    Require(battle.Enemies.Count == 1 && battle.FinalBossEnemyId != 0, "Boss must stay singular in co-op");
});
Check("Ray telegraph locks direction and respects projectile cap", () =>
{
    var battle = FinalBattle(); var enemy = battle.Enemies.Values.Single();
    var actor = new OnlineArcadeRunActor { Record = Record() };
    enemy.BossSkillCooldownSeconds = 100f;
    BattleCall("TickBoss", actor, battle, enemy, battle.Players[1], 1f, 0f, 3f, 3f, config.ArcadeRule,
        config.Tables.TbArcadeArena.Get(20).Obstacles);
    Require(enemy.BossPhase == OnlineArcadeBossPhase.RayTelegraph && battle.Projectiles.Count == 0, "no telegraph");
    BattleCall("TickBoss", actor, battle, enemy, battle.Players[1], 0f, 1f, 1f, 1f, config.ArcadeRule,
        config.Tables.TbArcadeArena.Get(20).Obstacles);
    Require(battle.Projectiles.Values.Single().VelocityZ == 0f, "ray tracked after lock");
    while (battle.Projectiles.Count < config.ArcadeRule.MaxProjectiles)
    {
        long id = battle.NextProjectileRuntimeId++;
        battle.Projectiles.Add(id, new OnlineArcadeProjectileRuntime());
    }
    BattleCall("TickBoss", actor, battle, enemy, battle.Players[1], 1f, 0f, 3f, 3f, config.ArcadeRule,
        config.Tables.TbArcadeArena.Get(20).Obstacles);
    BattleCall("TickBoss", actor, battle, enemy, battle.Players[1], 1f, 0f, 1f, 1f, config.ArcadeRule,
        config.Tables.TbArcadeArena.Get(20).Obstacles);
    Require(battle.Projectiles.Count == config.ArcadeRule.MaxProjectiles, "projectile overflow");
});
Check("Bomb cannot remove the final Boss", () =>
{
    var battle = FinalBattle(); var actor = new OnlineArcadeRunActor { Record = Record() };
    BattleCall("UseBomb", actor, battle, battle.Players[1]);
    Require(battle.Enemies.ContainsKey(battle.FinalBossEnemyId), "bomb cleared Boss");
});
Check("Boss death stops reinforcements and opens only the victory exit", () =>
{
    var battle = FinalBattle(); var actor = new OnlineArcadeRunActor { Record = Record() };
    BattleCall("TickFinalBossReinforcements", battle, 5f, TimeInfo.Instance.ServerNow());
    Require(battle.SpawnQueue.Count > 0, "reinforcements missing");
    BattleCall("KillEnemy", actor, battle, battle.Enemies.Values.Single(), true);
    BattleCall("TickFinalBossReinforcements", battle, 5f, TimeInfo.Instance.ServerNow());
    Require(battle.FinalBossDefeated && battle.AwaitingExit && battle.SpawnQueue.Count == 0 && !battle.ArenaCompletionReady,
        "victory committed before exit");
    battle.Players[2].PositionX = 0; battle.Players[2].PositionZ = config.ArcadeRule.ArenaHalfHeight - 3f;
    BattleCall("TickExit", battle);
    Require(battle.ArenaCompletionReady && battle.SelectedExitConfigNumber == 0, "victory exit invalid");
});
Check("Branch and victory transactions persist distinct states", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "FirepowerLoopArcadeServerChecks", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var manager = new OnlineArcadeRunManagerComponent { StorePath = Path.Combine(root, "runs.json") };
        var actor = new OnlineArcadeRunActor { Record = Record(1, 1), Battle = new() { SelectedExitConfigNumber = 3 } };
        Require((bool)Invoke(typeof(OnlineArcadeRunManagerSystem), "TryCommitArenaCompletion", manager, actor), "branch save failed");
        Require(actor.Record.NextArena == 2 && actor.Record.ArenaConfigNumber == 3, "map number changed difficulty");
        actor.Record = Record(); actor.Battle = new() { FinalBossDefeated = true };
        Require((bool)Invoke(typeof(OnlineArcadeRunManagerSystem), "TryCommitArenaCompletion", manager, actor), "victory save failed");
        Require(actor.Record.Phase == OnlineArcadeRunPhase.Victory && actor.Record.CompletedArena == 10 && actor.Battle == null,
            "victory advanced to another arena");
        Require(typeof(OnlineArcadePlayerRecord).GetProperty("ShopOffers") == null, "trade records remain");
        Require(File.ReadAllText(manager.StorePath).Contains("ArenaConfigNumber"), "route omitted from disk");
    }
    finally { Directory.Delete(root, true); }
});
Check("Both early branches use cumulative cooperative totals with exact mixed allocation", () =>
{
    foreach (int map in new[] { 2, 3 })
    {
        var battle = OnlineArcadeRunActorSystem.PrepareBattle(Record(2, map), false);
        var arena = config.Tables.TbArcadeArena.Get(map);
        for (int wave = 0; wave < 4; wave++)
        {
            BattleCall("PrepareWave", battle, arena, wave);
            Require(battle.TotalInWave == 200 + wave * 10 && battle.SpawnQueue.Count == battle.TotalInWave, "wrong branch total");
            var group = config.Tables.TbArcadeEnemyGroup.Get(arena.WaveGroupIds[wave]);
            Require(battle.SpawnQueue.Distinct().Order().SequenceEqual(group.Members.Select(m => m.EnemyId).Order()), "enemy composition lost");
            Require(battle.MaxAlive <= config.ArcadeRule.MaxAliveEnemies, "total used as concurrent budget");
        }
    }
});
Check("Legacy queues and challenge rest stay outside early casual rules", () =>
{
    var old = Record(2, 2); old.IsCasualRoute = false;
    var battle = OnlineArcadeRunActorSystem.PrepareBattle(old, false);
    Require(battle.TotalInWave == 12, "legacy total migrated");
    Require(!(bool)BattleCall("UsesEarlyRest", battle), "legacy rest migrated");
    battle.IsCasualRoute = true; battle.IsChallenge = true;
    Require(!(bool)BattleCall("UsesEarlyRounds", battle) && !(bool)BattleCall("UsesEarlyRest", battle), "challenge rules contaminated");
});
Check("Early branch clear pauses, accepts a living member fire, then opens final exits", () =>
{
    var record = Record(2, 2); var actor = new OnlineArcadeRunActor { Record = record };
    var battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false);
    void Clear() { battle.SpawnQueue.Clear(); battle.SpawnQueueIndex = 0; battle.Enemies.Clear(); }
    Clear(); BattleCall("TryAdvanceWave", actor, battle, 0f);
    BattleCall("TryAdvanceWave", actor, battle, 2f);
    Require(battle.WaveIndex == 0 && record.Players.All(p => p.RunGold == 0), "short transition or cash leaked");
    battle.Players[2].Firing = true;
    BattleCall("TryAdvanceWave", actor, battle, .1f);
    Require(battle.WaveIndex == 1 && battle.TotalInWave == 210, "non-host fire rejected");
    BattleCall("PrepareWave", battle, config.Tables.TbArcadeArena.Get(2), 3);
    Clear(); BattleCall("TryAdvanceWave", actor, battle, 0f);
    Require(battle.AwaitingExit && !battle.ArenaCompletionReady, "final clear committed early");
});
Check("Early arena commit preserves old wallet without issuing clear cash", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "FirepowerLoopArcadeServerChecks", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        foreach (int map in new[] { 1, 2, 3 })
        {
            var record = Record(map == 1 ? 1 : 2, map);
            foreach (var player in record.Players) player.RunGold = 123;
            var actor = new OnlineArcadeRunActor { Record = record,
                Battle = new() { SelectedExitConfigNumber = config.Tables.TbArcadeArena.Get(map).NextArenaNumbers[0] } };
            var manager = new OnlineArcadeRunManagerComponent { StorePath = Path.Combine(root, "runs.json") };
            Require((bool)Invoke(typeof(OnlineArcadeRunManagerSystem), "TryCommitArenaCompletion", manager, actor), "commit failed");
            Require(actor.Record.Players.All(p => p.RunGold == 123), "arena clear cash leaked or old wallet erased");
        }
    }
    finally { Directory.Delete(root, true); }
});
Check("Babylon crates persist across waves, honor drop budget and issue only one shared pickup", () =>
{
    var record = Record(2, 2); var actor = new OnlineArcadeRunActor { Record = record };
    var battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false);
    Require(battle.Crates.Count == 8, "crates not initialized");
    BattleCall("DamageCrate", actor, battle, 1, 100); BattleCall("DamageCrate", actor, battle, 1, 100);
    Require(battle.Crates.Count == 7 && battle.Drops.Count == 1 && record.Players.All(p => p.RunGold == 0), "duplicate pickup or cash");
    BattleCall("PrepareWave", battle, config.Tables.TbArcadeArena.Get(2), 1);
    Require(battle.Crates.Count == 7, "crates respawned");
    while (battle.Drops.Count < config.ArcadeRule.MaxDrops) battle.Drops.Add(battle.NextDropRuntimeId++, new());
    BattleCall("DamageCrate", actor, battle, 2, 100); Require(battle.Crates.ContainsKey(2), "reward lost at budget");
});
Check("Babylon first round armor is independent from health and disappears in following rounds", () =>
{
    var battle = OnlineArcadeRunActorSystem.PrepareBattle(Record(2, 2), false);
    BattleCall("TrySpawnEnemy", battle, TimeInfo.Instance.ServerNow());
    var enemy = battle.Enemies.Values.Single(); int health = enemy.CurrentHealth;
    Require(enemy.ArmorHealth == 40, "armor absent");
    BattleCall("DamageEnemy", enemy, 30); Require(enemy.CurrentHealth == health, "armor ignored");
    BattleCall("DamageEnemy", enemy, 15); Require(enemy.ArmorHealth == 0 && enemy.CurrentHealth == health - 5, "overflow lost");
    battle.Enemies.Clear(); BattleCall("PrepareWave", battle, config.Tables.TbArcadeArena.Get(2), 1);
    BattleCall("TrySpawnEnemy", battle, TimeInfo.Instance.ServerNow()); Require(battle.Enemies.Values.Single().ArmorHealth == 0, "armor leaked");
});
Check("Babylon snapshots and cloned responses preserve authoritative crates and armor", () =>
{
    var battle = OnlineArcadeRunActorSystem.PrepareBattle(Record(2, 2), false);
    BattleCall("TrySpawnEnemy", battle, TimeInfo.Instance.ServerNow());
    var packet = (OnlineArcadeBattleInfo)BattleCall("BuildBattleSnapshot", battle);
    var clone = (OnlineArcadeBattleInfo)BattleCall("CloneBattleSnapshot", packet);
    Require(packet.CrateIds.Count == 8 && clone.CrateIds.SequenceEqual(packet.CrateIds), "crate snapshot lost");
    Require(clone.Enemies.Single().ArmorHealth == 40, "armor snapshot lost");
    clone.Dispose(); packet.Dispose();
});
Console.WriteLine($"Server arcade checks: {passed}/15 passed.");
World.Instance.Dispose();
