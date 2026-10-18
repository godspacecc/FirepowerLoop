using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>真实双人权威生成与移动回归：出现即追击，重新选目标及岸线约束不依赖固定登陆点。</summary>
internal static class IslandEnemyChaseOnlineChecks
{
    private static object Call(string name, params object[] values) => typeof(OnlineArcadeRunActorSystem)
        .GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, values)!;

    public static int Run(int maximum)
    {
        const int count = 5;
        if (maximum < count) throw new ArgumentException("Declared maximum smaller than IslandEnemyChaseOnline scope");
        int failed = 0;
        for (int caseIndex = 0; caseIndex < count; caseIndex++)
        {
            try { Chase(caseIndex % 4, caseIndex < 4); Console.WriteLine($"PASS IslandEnemyChaseOnline.case{caseIndex}"); }
            catch (Exception e) { failed++; Console.WriteLine($"FAIL IslandEnemyChaseOnline.case{caseIndex}: {e.InnerException ?? e}"); }
        }
        Console.WriteLine($"{count - failed}/{count} passed");
        return failed == 0 ? 0 : 1;
    }

    private static void Chase(int wave, bool casual)
    {
        var config = ServerConfig.Instance;
        var rule = config.ArcadeRule;
        var record = new OnlineArcadeRunRecord { RunId = 1, RoomId = 2, Revision = 1, NextArena = 1,
            ArenaConfigNumber = 1, IsCasualRoute = casual, ConfigFingerprint = rule.ConfigFingerprint,
            Players = new() { new() { PlayerId = 1, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId },
                new() { PlayerId = 2, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId } } };
        var battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false);
        var actor = new OnlineArcadeRunActor { Record = record, Battle = battle };
        Call("PrepareWave", battle, config.Tables.TbArcadeArena.Get(1), wave);
        battle.NextSpawnAt = 0;
        Require((bool)Call("TrySpawnEnemy", battle, 10000L), "Enemy was not spawned");
        var enemy = battle.Enemies.Values.Single();
        var region = new ArcadeIslandRegion(rule.ArenaHalfWidth, rule.ArenaHalfHeight, wave + 1);
        if (wave == 0 || wave == 3)
            Require(region.IsShallow(enemy.PositionX, enemy.PositionZ), "Fixture must start in shallow water");
        float length = MathF.Sqrt(enemy.PositionX * enemy.PositionX + enemy.PositionZ * enemy.PositionZ);
        float tangentX = -enemy.PositionZ / length, tangentZ = enemy.PositionX / length;
        foreach (var p in battle.Players.Values) p.InvulnerabilitySeconds = 1000;
        var player = battle.Players[1];
        battle.Players[2].PositionX = 0; battle.Players[2].PositionZ = 0;
        foreach (float side in new[] { 1f, -1f })
        {
            var target = region.Constrain(enemy.PositionX + tangentX * side * 4, enemy.PositionZ + tangentZ * side * 4, rule.PlayerRadius);
            player.PositionX = target.X; player.PositionZ = target.Y;
            float x = enemy.PositionX, z = enemy.PositionZ;
            Call("TickEnemies", actor, battle, .05f);
            float dx = enemy.PositionX - x, dz = enemy.PositionZ - z;
            float distance = MathF.Sqrt(dx * dx + dz * dz);
            float tx = target.X - x, tz = target.Y - z, targetDistance = MathF.Sqrt(tx * tx + tz * tz);
            Require(distance > 0 && (dx * tx + dz * tz) / (distance * targetDistance) > .9f,
                "Enemy must immediately chase and retarget the nearest player instead of a landing point");
            Require(enemy.TargetPlayerId == 1 && distance <= enemy.MoveSpeed * .05f + .001f, "Wrong player or discontinuous step");
        }
        var edge = region.Constrain(enemy.PositionX + tangentX * 30, enemy.PositionZ + tangentZ * 30, rule.PlayerRadius);
        foreach (var p in battle.Players.Values) { p.PositionX = edge.X; p.PositionZ = edge.Y; }
        for (int i = 0; i < 100; i++)
        {
            float x = enemy.PositionX, z = enemy.PositionZ;
            Call("TickEnemies", actor, battle, .05f);
            var legal = region.Constrain(enemy.PositionX, enemy.PositionZ, rule.EnemyContactRadius);
            Require(region.Contains(enemy.PositionX, enemy.PositionZ), "Enemy crossed shoreline into deep water");
            Require(MathF.Abs(legal.X - enemy.PositionX) < .01f && MathF.Abs(legal.Y - enemy.PositionZ) < .01f, "Enemy radius crossed shoreline");
            float dx = enemy.PositionX - x, dz = enemy.PositionZ - z;
            Require(MathF.Sqrt(dx * dx + dz * dz) <= enemy.MoveSpeed * .05f + .01f, "Enemy snapped along shoreline");
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
