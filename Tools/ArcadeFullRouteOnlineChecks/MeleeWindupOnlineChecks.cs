using System.Reflection;
using ET;
using ET.Server;

/// <summary>通过正式出生和双人权威敌人更新验证靠近后的近战前摇。</summary>
internal static class MeleeWindupOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks = new (string Name, Action Check)[]
        {
            ("EnteringRangeStartsFreshWindup", EnteringRangeStartsFreshWindup),
            ("LeavingRangeCancelsWindup", LeavingRangeCancelsWindup),
            ("CooldownRequiresAnotherWindup", CooldownRequiresAnotherWindup),
            ("ChangingTargetRestartsWindup", ChangingTargetRestartsWindup),
            ("BuffedContactStillWaits", BuffedContactStillWaits),
            ("ChargeNavigationDoesNotReplaceContactTarget", ChargeNavigationDoesNotReplaceContactTarget),
            ("InterruptedContactRestartsWindup", InterruptedContactRestartsWindup),
        };
        if (maximum < checks.Length) throw new ArgumentException("Declared maximum smaller than MeleeWindupOnline scope");
        int failed = 0;
        foreach (var check in checks)
        {
            try { check.Check(); Console.WriteLine($"PASS MeleeWindupOnline.{check.Name}"); }
            catch (Exception e) { failed++; Console.WriteLine($"FAIL MeleeWindupOnline.{check.Name}: {e.InnerException ?? e}"); }
        }
        Console.WriteLine($"{checks.Length - failed}/{checks.Length} passed");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>正式首场队列出生，第二名玩家初始远离，不伪造攻击计时状态。</summary>
    private static (OnlineArcadeRunActor Actor, OnlineArcadeEnemyRuntime Enemy) Create()
    {
        var rule = ServerConfig.Instance.ArcadeRule;
        var record = new OnlineArcadeRunRecord { RunId = 7310, RoomId = 2, Revision = 1,
            NextArena = 1, ArenaConfigNumber = 1, RoundNumber = 1, IsCasualRoute = true,
            ConfigFingerprint = rule.ConfigFingerprint,
            Players = new() { new() { PlayerId = 1, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId },
                new() { PlayerId = 2, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId } } };
        var actor = new OnlineArcadeRunActor { Record = record, Battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false) };
        var b = actor.Battle; b.SpawnQueue.Clear(); b.SpawnQueue.Add("enemy.arcade.basic"); b.SpawnQueueIndex = 0;
        Call("TrySpawnEnemy", b, 10000L);
        var enemy = b.Enemies.Values.Single(); enemy.PositionX = 1; enemy.PositionZ = 0; enemy.MoveSpeed = 0;
        enemy.Damage = 10; enemy.AttackCooldownSeconds = 0;
        var player = b.Players[1]; player.PositionX = player.PositionZ = 0; player.CurrentHealth = player.MaxHealth = 100;
        var other = b.Players[2]; other.PositionX = other.PositionZ = -10; other.CurrentHealth = other.MaxHealth = 100;
        return (actor, enemy);
    }

    private static void EnteringRangeStartsFreshWindup()
    {
        var (a, e) = Create(); e.PositionX = 10; Tick(a, 5);
        e.PositionX = 1; Tick(a, 0); Health(a, 100); Tick(a, .39f); Health(a, 100);
        Tick(a, .02f); Health(a, 90);
    }

    private static void LeavingRangeCancelsWindup()
    {
        var (a, e) = Create(); Tick(a, 0); Tick(a, .2f);
        e.PositionX = 10; Tick(a, .5f); e.PositionX = 1; Tick(a, 0);
        Tick(a, .25f); Health(a, 100); Tick(a, .16f); Health(a, 90);
    }

    private static void CooldownRequiresAnotherWindup()
    {
        var (a, _) = Create(); Tick(a, 0); Tick(a, .41f); Health(a, 90);
        a.Battle.Players[1].InvulnerabilitySeconds = 0;
        Tick(a, .5f); Health(a, 90); Tick(a, .51f); Tick(a, .39f); Health(a, 90);
        Tick(a, .02f); Health(a, 80);
    }

    private static void ChangingTargetRestartsWindup()
    {
        var (a, _) = Create(); Tick(a, 0); Tick(a, .3f);
        a.Battle.Players[1].PositionX = -10; a.Battle.Players[2].PositionX = 0; a.Battle.Players[2].PositionZ = 0;
        Tick(a, 0); Tick(a, .2f);
        Require(a.Battle.Players[2].CurrentHealth == 100, "Cannot transfer almost-complete windup to another player");
        Tick(a, .21f); Require(a.Battle.Players[2].CurrentHealth == 90, "New target did not receive a fresh full windup");
    }

    private static void BuffedContactStillWaits()
    {
        var (a, e) = Create(); e.SpecialKind = 1;
        Tick(a, 0); Health(a, 100); Tick(a, .41f); Health(a, 90);
    }

    /// <summary>冲锋导航点不能取代真实玩家位置，否则贴身敌人始终无法攻击。</summary>
    private static void ChargeNavigationDoesNotReplaceContactTarget()
    {
        var (a, e) = Create(); e.SpecialKind = 5;
        e.ContentElapsed = a.Battle.RoundLayout.SkillWarning + .1f; e.ContentLockedX = 1;
        Tick(a, 0); Health(a, 100); Tick(a, .41f); Health(a, 90);
    }

    /// <summary>人偶受击停步后不能继承受击前已经等待的0.3秒。</summary>
    private static void InterruptedContactRestartsWindup()
    {
        var (a, e) = Create(); e.SpecialKind = 3; e.ContentElapsed = 2;
        Tick(a, 0); Tick(a, .3f); e.LastContentHitAt = e.ContentElapsed;
        Tick(a, .1f); e.ContentElapsed += a.Battle.RoundLayout.SkillWarning + 1;
        Tick(a, 0); Tick(a, .2f); Health(a, 100); Tick(a, .21f); Health(a, 90);
    }

    private static void Tick(OnlineArcadeRunActor actor, float seconds) => Call("TickEnemies", actor, actor.Battle, seconds);
    private static object Call(string name, params object[] args) => typeof(OnlineArcadeRunActorSystem)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
    private static void Health(OnlineArcadeRunActor a, int expected) => Require(a.Battle.Players[1].CurrentHealth == expected,
        $"Expected health {expected}, actual {a.Battle.Players[1].CurrentHealth}");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
