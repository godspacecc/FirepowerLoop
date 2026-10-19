using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>双人权威弹道的定向回归：海岛越界继续飞行，其他场地保留边界。</summary>
internal static class IslandProjectileBoundaryOnlineChecks
{
    public static int Run(int maximum)
    {
        const int count = 6;
        if (maximum < count) throw new ArgumentException("Declared maximum smaller than IslandProjectileBoundaryOnline scope");
        int failed = 0;
        for (int index = 0; index < count; index++)
        {
            try { Check(index); Console.WriteLine($"PASS IslandProjectileBoundaryOnline.case{index}"); }
            catch (Exception e) { failed++; Console.WriteLine($"FAIL IslandProjectileBoundaryOnline.case{index}: {e.InnerException ?? e}"); }
        }
        Console.WriteLine($"{count - failed}/{count} passed");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>直接推进正式服务器弹丸，不连接服务器、不读取玩家数据。</summary>
    private static void Check(int index)
    {
        var config = ServerConfig.Instance;
        var rule = config.ArcadeRule;
        int arenaNumber = index == 4 ? 2 : 1;
        var record = new OnlineArcadeRunRecord
        {
            RunId = 1, RoomId = 2, Revision = 1, NextArena = arenaNumber, ArenaConfigNumber = arenaNumber,
            IsCasualRoute = true, ConfigFingerprint = rule.ConfigFingerprint,
            Players = new() { new() { PlayerId = 1, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId },
                new() { PlayerId = 2, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId } }
        };
        var battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false);
        var actor = new OnlineArcadeRunActor { Record = record, Battle = battle };
        battle.IsChallenge = index == 5;
        var weapon = config.Tables.TbArcadeWeapon.Get(rule.BaseWeaponId);
        float dx = index == 1 ? -1 : index is 2 or 3 ? 0 : 1;
        float dz = index == 2 ? 1 : index == 3 ? -1 : 0;
        var region = new ArcadeIslandRegion(rule.ArenaHalfWidth, rule.ArenaHalfHeight, 1);
        var origin = region.Constrain(dx * 100, dz * 100, rule.PlayerRadius);
        battle.Projectiles.Clear();
        battle.Projectiles.Add(901, new OnlineArcadeProjectileRuntime
        {
            RuntimeId = 901, OwnerPlayerId = 1, PositionX = origin.X, PositionZ = origin.Y,
            VelocityX = dx * weapon.ProjectileSpeed, VelocityZ = dz * weapon.ProjectileSpeed,
            RemainingSeconds = weapon.ProjectileLifetimeSeconds, Behavior = weapon.Behavior, RemainingHits = 1,
            SourceWeaponId = weapon.Id, FromPlayerWeapon = true
        });
        Tick(actor, .3f);
        if (index >= 4)
        {
            Require(!battle.Projectiles.ContainsKey(901), "Non-island projectile crossed arena boundary");
            return;
        }
        Require(battle.Projectiles.TryGetValue(901, out var projectile), "Island projectile removed at old boundary before its lifetime");
        Require(MathF.Max(MathF.Abs(projectile!.PositionX), MathF.Abs(projectile.PositionZ)) > 19.5f, "Fixture did not cross arena boundary");
        Tick(actor, weapon.ProjectileLifetimeSeconds - .4f);
        Require(battle.Projectiles.ContainsKey(901), "Island projectile expired before configured lifetime");
        float expected = weapon.ProjectileSpeed * (weapon.ProjectileLifetimeSeconds - .1f);
        Require(MathF.Abs((projectile.PositionX - origin.X) * dx + (projectile.PositionZ - origin.Y) * dz - expected) < .001f, "Projectile did not travel configured distance");
        Tick(actor, .11f);
        Require(!battle.Projectiles.ContainsKey(901), "Expired island projectile was not removed");
    }

    private static void Tick(OnlineArcadeRunActor actor, float seconds) => typeof(OnlineArcadeRunActorSystem)
        .GetMethod("TickProjectiles", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { actor, actor.Battle, seconds });
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
