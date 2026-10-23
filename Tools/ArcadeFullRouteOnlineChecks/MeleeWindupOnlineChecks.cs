using System.Reflection;
using ET;
using ET.Server;
using MemoryPack;

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
            ("ZombieClawsUseConfiguredReach", ZombieClawsUseConfiguredReach),
            ("ZombieClawStopsAtReach", ZombieClawStopsAtReach),
            ("ZombieClawCatchingEscapeRestarts", ZombieClawCatchingEscapeRestarts),
            ("GrabSnapshotCloneAndCodec", GrabSnapshotCloneAndCodec),
            ("ZombieRecoveryStopsAndInterruptClears", ZombieRecoveryStopsAndInterruptClears),
            ("ZombieCrowdingKeepsAttackPosition", ZombieCrowdingKeepsAttackPosition),
            ("ShadowTransitionClearsOldRecovery", ShadowTransitionClearsOldRecovery),
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
    private static (OnlineArcadeRunActor Actor, OnlineArcadeEnemyRuntime Enemy) Create(string enemyId = "enemy.arcade.basic")
    {
        var rule = ServerConfig.Instance.ArcadeRule;
        var record = new OnlineArcadeRunRecord { RunId = 7310, RoomId = 2, Revision = 1,
            NextArena = 1, ArenaConfigNumber = 1, RoundNumber = 1, IsCasualRoute = true,
            ConfigFingerprint = rule.ConfigFingerprint,
            Players = new() { new() { PlayerId = 1, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId },
                new() { PlayerId = 2, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId } } };
        var actor = new OnlineArcadeRunActor { Record = record, Battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false) };
        var b = actor.Battle; b.SpawnQueue.Clear(); b.SpawnQueue.Add(enemyId); b.SpawnQueueIndex = 0;
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

    private static void ZombieClawsUseConfiguredReach()
    {
        foreach (var id in new[] { "enemy.arcade.zombie.shambler", "enemy.arcade.zombie.gaunt", "enemy.arcade.zombie.bloated" })
        {
            var (a,e)=Create(id); Require(Math.Abs(e.AttackRange-.9f)<.0001f,"Claw reach must be .9: "+id);
            e.PositionX=1.2f; Tick(a,0);Tick(a,.5f);Health(a,100);
            Require(e.MeleeWindupTargetId==0,"Out of claw reach cannot wind up");
            e.PositionX=.9f;Tick(a,0);Tick(a,.39f);Health(a,100);Tick(a,.02f);Health(a,90);
        }
    }

    private static void ZombieClawStopsAtReach()
    {
        var (a,e)=Create("enemy.arcade.zombie.shambler");e.PositionX=2.9f;e.MoveSpeed=2.2f;
        Tick(a,1);Require(Math.Abs(e.PositionX-.9f)<.0001f,"Must stop at claw reach");
        Tick(a,.39f);Health(a,100);Require(Math.Abs(e.PositionX-.9f)<.0001f,"Windup cannot walk into player");
        Tick(a,.02f);Health(a,90);
    }

    private static void ZombieClawCatchingEscapeRestarts()
    {
        var (a,e)=Create("enemy.arcade.zombie.shambler");e.PositionX=.9f;
        Tick(a,0);Tick(a,.3f);a.Battle.Players[1].PositionX=-.2f;e.MoveSpeed=20;
        Tick(a,.02f);Tick(a,.11f);Health(a,100);
    }

    /// <summary>从真实起手和命中生成快照，再验证复制和协议往返不丢阶段。</summary>
    private static void GrabSnapshotCloneAndCodec()
    {
        var (a,e)=Create("enemy.arcade.zombie.shambler");e.PositionX=.9f;
        Tick(a,0);Tick(a,.2f);
        void Verify()
        {
            using var source=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);
            using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",source);
            using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));
            var enemy=decoded.Enemies.Single();
            Require(Math.Abs(enemy.MeleeWindupSeconds-.4f)<.0001f,"Missing windup duration");
            Require(Math.Abs(enemy.MeleeWindupRemaining-e.MeleeWindupRemaining)<.0001f,"Missing current windup");
            Require(Math.Abs(enemy.MeleeRecoveryRemaining-e.MeleeRecoveryRemaining)<.0001f,"Missing recovery");
            Require(enemy.TargetPlayerId==1,"Missing authoritative target");
        }
        Verify();Tick(a,.21f);Health(a,90);Verify();
    }

    /// <summary>命中后停步收招；牛撞中断时不能在受控后补播旧挥击。</summary>
    private static void ZombieRecoveryStopsAndInterruptClears()
    {
        var (a,e)=Create("enemy.arcade.zombie.shambler");e.PositionX=.9f;e.MoveSpeed=2.2f;
        Tick(a,0);Tick(a,.41f);Health(a,90);
        Require(e.MeleeRecoveryRemaining>.54f,"No recovery after hit");
        a.Battle.Players[1].PositionX=-2;Tick(a,.2f);
        Require(Math.Abs(e.PositionX-.9f)<.0001f,"Recovery must stop pursuit");
        Call("CancelMeleeWindup",e,true);
        Require(e.MeleeRecoveryRemaining==0&&e.MeleeWindupRemaining==0,"Interruption left old attack state");
    }

    private static void ZombieCrowdingKeepsAttackPosition()
    {
        var(a,e)=Create("enemy.arcade.zombie.shambler");e.PositionX=.9f;
        a.Battle.Enemies.Add(999,new OnlineArcadeEnemyRuntime{RuntimeId=999,EnemyConfigId="enemy.arcade.basic",PositionX=1.9f,CurrentHealth=100,MaxHealth=100,AttackCooldownSeconds=100});
        Tick(a,0);Require(Math.Abs(e.PositionX-.9f)<.0001f,"Crowding moved windup");
        Tick(a,.41f);Health(a,90);Require(Math.Abs(e.PositionX-.9f)<.0001f,"Crowding moved contact");
        a.Battle.Enemies[999].PositionX=1.9f;Tick(a,.1f);Require(Math.Abs(e.PositionX-.9f)<.0001f,"Crowding moved recovery");
    }
    /// <summary>真实暗影出生、完成抓挠、首次击倒换位后不能带着旧收招落地。</summary>
    private static void ShadowTransitionClearsOldRecovery()
    {
        var rule=ServerConfig.Instance.ArcadeRule;
        var record=new OnlineArcadeRunRecord{RunId=7311,RoomId=2,Revision=1,NextArena=5,ArenaConfigNumber=24,RoundNumber=1,IsCasualRoute=true,CasualRouteVersion=1,ConfigFingerprint=rule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
        var a=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};var b=a.Battle;
        foreach(var p in b.Players.Values){p.ReadyRoundGeneration=b.RoundGeneration;p.PositionX=0;p.PositionZ=-6;p.CurrentHealth=p.MaxHealth=100;}
        b.Players[2].PositionX=-10;b.SpawnQueue.Clear();b.SpawnQueue.Add("enemy.arcade.zombie.shambler");b.SpawnQueueIndex=0;
        b.SpawnedInWave=(int)Math.Ceiling(1/b.RoundLayout.SpecialRatio)-1;Require((bool)Call("SpawnQueuedEnemy",b,10000L,0),"No real shadow spawn");
        var e=b.Enemies.Values.Single();Require(e.SpecialKind==19&&e.StageEnemy.Phase==2,"No shadow arrival");
        e.PositionX=.9f;e.PositionZ=-6;e.MoveSpeed=0;e.Damage=10;e.AttackCooldownSeconds=0;
        Tick(a,1.26f);Tick(a,0);Tick(a,.41f);Health(a,90);Require(e.MeleeRecoveryRemaining>0,"No real recovery");
        Call("DamageEnemy",b,e,e.MaxHealth);Call("KillEnemy",a,b,e,false);
        Require(e.StageEnemy.Phase==3&&e.MeleeRecoveryRemaining==0,"Shadow kept recovery during travel");
        Tick(a,b.RoundLayout.EventWarning+.01f);Require(e.StageEnemy.Phase==0&&e.MeleeRecoveryRemaining==0,"Shadow landed with old recovery");
    }

    private static void Tick(OnlineArcadeRunActor actor, float seconds) => Call("TickEnemies", actor, actor.Battle, seconds);
    private static object Call(string name, params object[] args) => typeof(OnlineArcadeRunActorSystem)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
    private static void Health(OnlineArcadeRunActor a, int expected) => Require(a.Battle.Players[1].CurrentHealth == expected,
        $"Expected health {expected}, actual {a.Battle.Players[1].CurrentHealth}");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
