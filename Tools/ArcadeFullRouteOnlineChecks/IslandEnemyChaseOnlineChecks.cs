using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>真实双人权威生成与移动回归：从深海连续接近，完整入岸后沿原岸线追击。</summary>
internal static class IslandEnemyChaseOnlineChecks
{
    private static object Call(string name, params object[] values) => typeof(OnlineArcadeRunActorSystem)
        .GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, values)!;

    public static int Run(int maximum)
    {
        const int count = 7;
        if (maximum < count) throw new ArgumentException("Declared maximum smaller than IslandEnemyChaseOnline scope");
        int failed = 0;
        for (int caseIndex = 0; caseIndex < count; caseIndex++)
        {
            try
            {
                if (caseIndex < 5) Chase(caseIndex % 4, caseIndex < 4);
                else if (caseIndex == 5) ShoreContact();
                else SeaDisplacement();
                Console.WriteLine($"PASS IslandEnemyChaseOnline.case{caseIndex}");
            }
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
        var region = new ArcadeIslandRegion(rule.ArenaHalfWidth, rule.ArenaHalfHeight, casual ? wave + 1 : 0);
        Require(!region.Contains(enemy.PositionX, enemy.PositionZ), "Enemy must spawn in deep sea instead of shallow water");
        foreach (var p in battle.Players.Values) { p.InvulnerabilitySeconds = 1000; p.PositionX = p.PositionZ = 0; }
        bool sawWater = false;
        for (int i = 0; i < 800 && !region.ContainsCircle(enemy.PositionX, enemy.PositionZ, rule.EnemyContactRadius); i++)
        {
            float beforeX = enemy.PositionX, beforeZ = enemy.PositionZ;
            Call("TickEnemies", actor, battle, .05f);
            float dx = enemy.PositionX - beforeX, dz = enemy.PositionZ - beforeZ;
            Require(MathF.Sqrt(dx * dx + dz * dz) <= enemy.MoveSpeed * .05f + .01f, "Sea approach snapped to shore");
            sawWater |= region.IsShallow(enemy.PositionX, enemy.PositionZ);
        }
        Require((casual || sawWater) && region.ContainsCircle(enemy.PositionX, enemy.PositionZ, rule.EnemyContactRadius), "Enemy failed continuous entry (legacy route must still cross water)");
        // 新通道两旁是实体山脚，横向追击样本须先进入中央活动区，不能把目标放进山内。
        if (casual)
            for (int i = 0; i < 800 && Math.Max(Math.Abs(enemy.PositionX), Math.Abs(enemy.PositionZ)) > 15; i++)
                Call("TickEnemies", actor, battle, .05f);
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

    /// <summary>使用正式权威战斗、配置和生成入口准备海敌。</summary>
    private static OnlineArcadeRunActor SeaActor()
    {
        var rule = ServerConfig.Instance.ArcadeRule;
        var record = new OnlineArcadeRunRecord { RunId = 1, RoomId = 2, Revision = 1, NextArena = 1,
            ArenaConfigNumber = 1, IsCasualRoute = true, ConfigFingerprint = rule.ConfigFingerprint,
            Players = new() { new() { PlayerId = 1, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId },
                new() { PlayerId = 2, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId } } };
        var battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false);
        Call("PrepareWave", battle, ServerConfig.Instance.Tables.TbArcadeArena.Get(1), 0);
        battle.NextSpawnAt = 0;
        Require((bool)Call("TrySpawnEnemy", battle, 10000L), "Enemy was not spawned");
        return new OnlineArcadeRunActor { Record = record, Battle = battle };
    }

    /// <summary>合法岸边玩家仍受海敌接触伤害，不能因圆半径差形成安全点。</summary>
    private static void ShoreContact()
    {
        var actor = SeaActor(); var battle = actor.Battle;
        var rule = ServerConfig.Instance.ArcadeRule;
        var enemy = battle.Enemies.Values.Single();
        var region = new ArcadeIslandRegion(rule.ArenaHalfWidth, rule.ArenaHalfHeight, 1);
        var shore = region.Constrain(enemy.PositionX, enemy.PositionZ, rule.PlayerRadius);
        foreach (var p in battle.Players.Values) { p.PositionX = shore.X; p.PositionZ = shore.Y; p.InvulnerabilitySeconds = 0; }
        enemy.AttackCooldownSeconds = 0;
        int health = battle.Players[1].CurrentHealth + battle.Players[2].CurrentHealth;
        for (int i = 0; i < 800 && battle.Players[1].CurrentHealth + battle.Players[2].CurrentHealth == health; i++)
            Call("TickEnemies", actor, battle, .05f);
        Require(battle.Players[1].CurrentHealth + battle.Players[2].CurrentHealth < health, "Shore player became immune to sea enemy contact");
    }

    /// <summary>拥挤与普通击退都只做有限位移，不把海敌强制投影上岸。</summary>
    private static void SeaDisplacement()
    {
        var actor = SeaActor(); var battle = actor.Battle;
        var enemy = battle.Enemies.Values.Single();
        float x = enemy.PositionX, z = enemy.PositionZ;
        battle.Enemies.Add(999, new OnlineArcadeEnemyRuntime { RuntimeId = 999, PositionX = x, PositionZ = z,
            MoveSpeed = enemy.MoveSpeed, MaxHealth = 100, CurrentHealth = 100 });
        foreach (var p in battle.Players.Values) p.InvulnerabilitySeconds = 1000;
        Call("TickEnemies", actor, battle, .01f);
        foreach (var e in battle.Enemies.Values)
            Require(MathF.Sqrt((e.PositionX-x)*(e.PositionX-x)+(e.PositionZ-z)*(e.PositionZ-z)) < 2, "Crowding snapped enemy to shore");
        x = enemy.PositionX; z = enemy.PositionZ;
        Call("DamageAndKnockbackEnemies", actor, battle, x + .2f, z, 1f, 1, 1f, 1L);
        Require(MathF.Sqrt((enemy.PositionX-x)*(enemy.PositionX-x)+(enemy.PositionZ-z)*(enemy.PositionZ-z)) <= 1.01f,
            "Knockback snapped enemy to shore");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
