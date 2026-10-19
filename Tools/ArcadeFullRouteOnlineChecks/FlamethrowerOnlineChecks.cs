using System.Reflection;
using ET;
using ET.Server;
using MemoryPack;

/// <summary>喷火器的服务端定向回归：开火身份、快照传输、穿透命中与寿命。</summary>
internal static class FlamethrowerOnlineChecks
{
    private const string WeaponId = "arcade.weapon.flamethrower";

    /// <summary>只运行明确声明的七项检查，禁止扩大到完整套件。</summary>
    public static int Run(int maximum)
    {
        var checks = new (string Name, Action Check)[]
        {
            ("ShotIdentitySurvivesWeaponChange", ShotIdentitySurvivesWeaponChange),
            ("SnapshotCloneAndProtocolPreserveIdentity", SnapshotCloneAndProtocolPreserveIdentity),
            ("FlamesPierceWithoutRepeatDamageAndExpire", FlamesPierceWithoutRepeatDamageAndExpire),
            ("FlameHitEffectSurvivesProtocolAndExpiresWithoutDamage", FlameHitEffectSurvivesProtocolAndExpiresWithoutDamage),
            ("LethalFlameProducesOneTypedDeathReceipt", LethalFlameProducesOneTypedDeathReceipt),
            ("OtherFinalDamageAndImmuneHitsDoNotClaimFlameDeaths", OtherFinalDamageAndImmuneHitsDoNotClaimFlameDeaths),
            ("IslandDashDoesNotInheritEarlierFlameHit", IslandDashDoesNotInheritEarlierFlameHit),
        };
        if (maximum < checks.Length) throw new ArgumentException("Declared maximum smaller than FlamethrowerOnline scope");
        int failures = 0;
        foreach (var check in checks)
        {
            try { check.Check(); Console.WriteLine($"PASS FlamethrowerOnline.{check.Name}"); }
            catch (Exception e) { failures++; Console.WriteLine($"FAIL FlamethrowerOnline.{check.Name}: {e.InnerException ?? e}"); }
        }
        Console.WriteLine($"{checks.Length - failures}/{checks.Length} passed");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>读取生成后的真实配置构造双人现场，并由正式开火路径创建火焰。</summary>
    private static OnlineArcadeRunActor CreateFiringActor()
    {
        var rule = ServerConfig.Instance.ArcadeRule;
        var record = new OnlineArcadeRunRecord
        {
            RunId = 1, RoomId = 2, Revision = 1, NextArena = 1, ArenaConfigNumber = 1,
            IsCasualRoute = true, ConfigFingerprint = rule.ConfigFingerprint,
            Players = new() { new() { PlayerId = 1, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId },
                new() { PlayerId = 2, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId } },
        };
        var actor = new OnlineArcadeRunActor { Record = record, Battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false) };
        var player = actor.Battle.Players[1];
        player.PositionX = 0; player.PositionZ = 0; player.AimX = 0; player.AimZ = 1;
        player.TemporaryWeaponId = WeaponId; player.TemporaryWeaponSeconds = 20;
        Require((bool)Call("Fire", actor, actor.Battle, player, record.Players[0]), "Flamethrower did not fire");
        Require(actor.Battle.Projectiles.Count == 3, "Expected three flame lanes");
        return actor;
    }

    /// <summary>临时武器切回手枪后，已发射火焰仍保留发射时的身份。</summary>
    private static void ShotIdentitySurvivesWeaponChange()
    {
        var actor = CreateFiringActor();
        var flames = actor.Battle.Projectiles.Values.ToArray();
        var player = actor.Battle.Players[1];
        player.TemporaryWeaponId = string.Empty; player.TemporaryWeaponSeconds = 0; player.FireCooldownSeconds = 0;
        Require((bool)Call("Fire", actor, actor.Battle, player, actor.Record.Players[0]), "Basic weapon did not fire");
        Require(flames.All(p => p.SourceWeaponId == WeaponId && p.FromPlayerWeapon), "Existing flame identity changed");
        Require(actor.Battle.Projectiles.Values.Except(flames).All(p => p.SourceWeaponId == actor.Record.Players[0].WeaponId), "New bullet retained flame identity");
    }

    /// <summary>完整验证权威投影、快照复制及生成协议的序列化往返。</summary>
    private static void SnapshotCloneAndProtocolPreserveIdentity()
    {
        var actor = CreateFiringActor();
        using var snapshot = (OnlineArcadeBattleInfo)Call("BuildBattleSnapshot", actor.Battle);
        using var clone = (OnlineArcadeBattleInfo)Call("CloneBattleSnapshot", snapshot);
        using var decoded = MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));
        Require(decoded.Projectiles.Count == 3 && decoded.Projectiles.All(p => p.SourceWeaponId == WeaponId), "Protocol lost shot identity");
    }

    /// <summary>同束火焰穿过两个敌人，每个敌人只命中一次，并在短寿命结束后销毁。</summary>
    private static void FlamesPierceWithoutRepeatDamageAndExpire()
    {
        var actor = CreateFiringActor();
        var battle = actor.Battle;
        foreach (long id in new long[] { 1001, 1002 })
        {
            battle.Enemies.Add(id, new OnlineArcadeEnemyRuntime { RuntimeId = id,
                EnemyConfigId = ServerConfig.Instance.Tables.TbEnemy.DataList[0].Id,
                PositionX = 0, PositionZ = id == 1001 ? .85f : .95f, CurrentHealth = 100, MaxHealth = 100 });
            battle.EnemyIterationIds.Add(id);
        }
        Call("TickProjectiles", actor, battle, .05f);
        Require(battle.Enemies.Values.All(e => e.CurrentHealth < 100), "Flames failed to pierce both enemies");
        var health = battle.Enemies.ToDictionary(e => e.Key, e => e.Value.CurrentHealth);
        Call("TickProjectiles", actor, battle, 0f);
        Require(battle.Enemies.All(e => health[e.Key] == e.Value.CurrentHealth), "Same flame hit an enemy twice");
        Call("TickProjectiles", actor, battle, 1f);
        Require(battle.Projectiles.Count == 0, "Expired flames survived");
    }

    /// <summary>创建可承受一次火焰命中的真实普通目标。</summary>
    private static OnlineArcadeEnemyRuntime AddTarget(OnlineArcadeRunActor actor,int health)
    {
        var enemy=new OnlineArcadeEnemyRuntime {RuntimeId=1001,EnemyConfigId="enemy.arcade.basic",PositionZ=.95f,CurrentHealth=health,MaxHealth=100};
        actor.Battle.Enemies.Add(enemy.RuntimeId,enemy);actor.Battle.EnemyIterationIds.Add(enemy.RuntimeId);return enemy;
    }

    private static void FlameHitEffectSurvivesProtocolAndExpiresWithoutDamage()
    {
        var actor=CreateFiringActor();var battle=actor.Battle;var enemy=AddTarget(actor,100);
        Call("TickProjectiles",actor,battle,.05f);
        Require(enemy.CurrentHealth==85&&enemy.FlameHitRemainingSeconds>0&&enemy.LastDamageWasFlame,"Real flame hit did not mark effect");
        using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",battle);
        using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);
        using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));
        Require(decoded.Enemies.Single().FlameHitRemainingSeconds==enemy.FlameHitRemainingSeconds,"Burn state lost in snapshot or codec");
        battle.Projectiles.Clear();battle.SpawnQueueIndex=battle.SpawnQueue.Count;
        foreach(var player in battle.Players.Values)player.ReadyRoundGeneration=battle.RoundGeneration;
        long now=battle.LastTickAt;
        for(int i=0;i<4;i++)actor.TickBattle(now+=200);
        Require(enemy.FlameHitRemainingSeconds==0&&enemy.CurrentHealth==85,"Visual burn added damage or failed to expire");
    }

    private static void LethalFlameProducesOneTypedDeathReceipt()
    {
        var actor=CreateFiringActor();var enemy=AddTarget(actor,1);Call("TickProjectiles",actor,actor.Battle,.05f);
        Require(actor.Battle.EnemyDeaths.Count==1&&actor.Battle.EnemyDeaths[0].FlameKill,"Lethal flame lost death type");
        Call("KillEnemy",actor,actor.Battle,enemy,false);Require(actor.Battle.EnemyDeaths.Count==1,"Death receipt duplicated");
        using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",actor.Battle);
        using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);
        using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));
        Require(decoded.EnemyDeaths.Single().FlameKill,"Flame death lost across copies or codec");
    }

    private static void OtherFinalDamageAndImmuneHitsDoNotClaimFlameDeaths()
    {
        var actor=CreateFiringActor();var enemy=AddTarget(actor,2);
        Call("DamageEnemyFromFlame",actor.Battle,enemy,1);Require(enemy.LastDamageWasFlame,"Flame hit missing");
        Call("DamageEnemy",actor.Battle,enemy,1);Call("KillEnemy",actor,actor.Battle,enemy,false);
        Require(!actor.Battle.EnemyDeaths.Single().FlameKill,"Ordinary final hit inherited old flame source");
        var immune=new OnlineArcadeEnemyRuntime {CurrentHealth=100,MaxHealth=100,SpecialKind=13};
        Call("DamageEnemyFromFlame",actor.Battle,immune,5);
        Require(immune.CurrentHealth==100&&!immune.LastDamageWasFlame&&immune.FlameHitRemainingSeconds==0,"Immune hit created false burn");
    }

    /// <summary>真实海岛冲刺路径击杀先前受火敌人，死亡回执不得误标火焰。</summary>
    private static void IslandDashDoesNotInheritEarlierFlameHit()
    {
        var actor=CreateFiringActor();var enemy=AddTarget(actor,100);enemy.PositionX=enemy.PositionZ=0;
        Call("DamageEnemyFromFlame",actor.Battle,enemy,1);enemy.FlameHitRemainingSeconds=0;
        var player=actor.Battle.Players[1];player.DashSeconds=1;
        actor.Battle.IslandMovementPath.Add(new FirepowerLoop.Arcade.ArcadeRegionPoint(-1,0));
        actor.Battle.IslandMovementPath.Add(new FirepowerLoop.Arcade.ArcadeRegionPoint(1,0));
        Call("KillAlongIslandDash",actor,actor.Battle,player);
        Require(actor.Battle.Enemies.Count==0&&!actor.Battle.EnemyDeaths.Single().FlameKill,"Dash inherited earlier flame death source");
    }

    /// <summary>测试直接调用现有权威路径，不复制伤害或快照实现。</summary>
    private static object Call(string name, params object[] values) => typeof(OnlineArcadeRunActorSystem)
        .GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, values)!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
