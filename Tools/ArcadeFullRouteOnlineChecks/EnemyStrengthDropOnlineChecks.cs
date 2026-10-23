using System.Reflection;
using ET;
using ET.Server;

/// <summary>通过真实双人权威死亡入口验证强度掉率，逐项限额执行，不启动完整服务器。</summary>
internal static class EnemyStrengthDropOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks = new (string Name, Action Check)[]
        {
            ("StrongerEnemiesIncreaseAllThreeLootKinds", StrongerEnemiesIncreaseAllThreeLootKinds),
            ("EqualStatsKeepSameDistributionAcrossArenas", EqualStatsKeepSameDistributionAcrossArenas),
            ("ExtremeStatsStayCappedAndDeathsAwardOnce", ExtremeStatsStayCappedAndDeathsAwardOnce),
            ("ZeroChanceDisablesActualDeathDrops", ZeroChanceDisablesActualDeathDrops),
            ("InvalidStrengthConfigurationIsRejected", InvalidStrengthConfigurationIsRejected),
            ("HazardAndCapacityGuardsRemainEffective", HazardAndCapacityGuardsRemainEffective),
            ("LowTierSpawnsHave100Health", LowTierSpawnsHave100Health),
            ("LowTierNeverDropsPurpleAtAnyStrength", LowTierNeverDropsPurpleAtAnyStrength),
            ("BossReinforcementsNeverDropPurple", BossReinforcementsNeverDropPurple),
        };
        if (maximum < checks.Length) throw new ArgumentException("Declared maximum smaller than EnemyStrengthDropOnline scope");
        int failed = 0;
        foreach (var check in checks)
        {
            try { check.Check(); Console.WriteLine($"PASS EnemyStrengthDropOnline.{check.Name}"); }
            catch (Exception e) { failed++; Console.WriteLine($"FAIL EnemyStrengthDropOnline.{check.Name}: {e.InnerException ?? e}"); }
        }
        Console.WriteLine($"{checks.Length-failed}/{checks.Length} passed");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>固定本局身份与敌人身份，使强弱比较使用相同随机抽样。</summary>
    private static OnlineArcadeRunActor Create(int arena = 1)
    {
        var rule = ServerConfig.Instance.ArcadeRule;
        var record = new OnlineArcadeRunRecord { RunId = 310, RoomId = 2, Revision = 1, NextArena = arena,
            ArenaConfigNumber = arena, RoundNumber = 1, IsCasualRoute = true, ConfigFingerprint = rule.ConfigFingerprint,
            Players = new() { new() { PlayerId = 1, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId },
                new() { PlayerId = 2, Lives = 3, Ready = true, WeaponId = rule.BaseWeaponId } } };
        return new OnlineArcadeRunActor { Record = record, Battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false) };
    }

    /// <summary>返回各敌人实际掉落身份，不计算预期概率实现，避免测试照抄公式。</summary>
    private static Dictionary<int, string> Kills(int arena, int health, int damage, int count = 4096)
    {
        var actor = Create(arena); var result = new Dictionary<int, string>();
        for (int id = 1; id <= count; id++)
        {
            actor.Battle.Drops.Clear();
            var enemy = new OnlineArcadeEnemyRuntime { RuntimeId = id, EnemyConfigId = "enemy.arcade.brute", MaxHealth = health, Damage = damage,
                BlocksPurpleSkullDrop = ServerConfig.Instance.GetEnemy("enemy.arcade.brute").BlocksPurpleSkullDrop };
            actor.Battle.Enemies.Add(id, enemy); Call("KillEnemy", actor, actor.Battle, enemy, false);
            if (actor.Battle.Drops.Count > 0) result.Add(id, actor.Battle.Drops.Values.Single().DropConfigId);
            Call("KillEnemy", actor, actor.Battle, enemy, false);
            Require(actor.Battle.Drops.Count <= 1, "Repeated death created a second award");
        }
        return result;
    }

    private static void StrongerEnemiesIncreaseAllThreeLootKinds()
    {
        var weak = Kills(1, 20, 10); var strong = Kills(1, 100, 20);
        Require(weak.Count is > 300 and < 520, $"Weak enemy count {weak.Count} should be near ten percent");
        Require(strong.Count > weak.Count * 2 && strong.Count < 1550, "Strong enemies did not have a higher bounded chance");
        Require(weak.All(x => strong.TryGetValue(x.Key, out var kind) && kind == x.Value), "Increasing strength removed or changed an existing drop");
        foreach (var sample in new[] { weak, strong })
        {
            int gold = sample.Values.Count(x => x == "arcade.drop.gold.small"), red = sample.Values.Count(x => x == "arcade.drop.skull.small"),
                purple = sample.Values.Count(x => x == "arcade.drop.skull.large");
            Require(gold > red && red > purple && purple > 0 && gold + red + purple == sample.Count, "Loot rarity or source pool changed");
        }
    }

    /// <summary>从正式权威出生入口读取各普通小怪的生命，避免只校验CSV文字。</summary>
    private static void LowTierSpawnsHave100Health()
    {
        foreach (string enemyId in LowTierIds)
        {
            var actor = Create();
            var enemy = SpawnLowTier(actor, enemyId);
            Require(enemy.MaxHealth == 100 && enemy.CurrentHealth == 100,
                $"{enemyId} spawned with {enemy.CurrentHealth}/{enemy.MaxHealth} instead of 100/100");
        }
    }

    /// <summary>出生取得禁紫资格后施加极端成长，真实死亡仍只产生金币和红骷髅。</summary>
    private static void LowTierNeverDropsPurpleAtAnyStrength()
    {
        foreach (string enemyId in LowTierIds)
        {
            var actor = Create();
            var enemy = SpawnLowTier(actor, enemyId);
            actor.Battle.Enemies.Clear();
            enemy.MaxHealth = int.MaxValue; enemy.Damage = int.MaxValue;
            var kinds = new HashSet<string>();
            for (int id = 1; id <= 4096; id++)
            {
                actor.Battle.Drops.Clear(); enemy.RuntimeId = id;
                actor.Battle.Enemies.Add(id, enemy);
                Call("KillEnemy", actor, actor.Battle, enemy, false);
                foreach (var drop in actor.Battle.Drops.Values) kinds.Add(drop.DropConfigId);
                Call("KillEnemy", actor, actor.Battle, enemy, false);
                Require(actor.Battle.Drops.Count <= 1, "Repeated low-tier death awarded twice");
            }
            Require(kinds.SetEquals(new[] { "arcade.drop.gold.small", "arcade.drop.skull.small" }),
                enemyId + " dropped forbidden loot or lost gold/red: " + string.Join(",", kinds));
        }
    }

    private static readonly string[] LowTierIds = { "enemy.arcade.basic", "enemy.arcade.archer",
        "enemy.arcade.zombie.shambler", "enemy.arcade.zombie.gaunt" };

    /// <summary>独立Boss的普通增援不能绕过禁紫资格，验证实际增援与死亡入口。</summary>
    private static void BossReinforcementsNeverDropPurple()
    {
        var actor = Create();
        Call("SpawnContentReinforcements", actor.Battle, 1);
        var enemy = actor.Battle.Enemies.Values.Single();
        actor.Battle.Enemies.Clear();
        var kinds = new HashSet<string>();
        for (int id = 1; id <= 4096; id++)
        {
            actor.Battle.Drops.Clear(); enemy.RuntimeId = id;
            actor.Battle.Enemies.Add(id, enemy);
            Call("KillEnemy", actor, actor.Battle, enemy, false);
            foreach (var drop in actor.Battle.Drops.Values) kinds.Add(drop.DropConfigId);
        }
        Require(kinds.SetEquals(new[] { "arcade.drop.gold.small", "arcade.drop.skull.small" }),
            "Boss low-tier reinforcements dropped forbidden loot: " + string.Join(",", kinds));
    }

    /// <summary>保留正式关卡、玩家与波次，只指定本次要验证的普通怪身份。</summary>
    private static OnlineArcadeEnemyRuntime SpawnLowTier(OnlineArcadeRunActor actor, string enemyId)
    {
        var battle = actor.Battle;
        battle.SpawnQueue.Clear(); battle.SpawnQueue.Add(enemyId);
        battle.SpawnQueueIndex = 0; battle.NextSpawnAt = 0;
        Require((bool)Call("TrySpawnEnemy", battle, 10000L), "Low-tier enemy did not spawn");
        return battle.Enemies.Values.Single();
    }

    private static void EqualStatsKeepSameDistributionAcrossArenas()
    {
        int first = Kills(1, 20, 10).Count, late = Kills(20, 20, 10).Count;
        Require(late is > 300 and < 520 && Math.Abs(first-late) < 100, $"Arena number changed equal-stat chance: {first}/{late}");
    }

    private static void ExtremeStatsStayCappedAndDeathsAwardOnce()
    {
        int count = Kills(1, int.MaxValue, int.MaxValue).Count;
        Require(count is > 1800 and < 2300, $"Extreme health/damage overflowed or exceeded fifty percent: {count}");
    }

    private static void ZeroChanceDisablesActualDeathDrops()
    {
        WithRule("DropChance", 0f, () => Require(Kills(1, int.MaxValue, int.MaxValue, 512).Count == 0, "Zero chance still generated loot"));
    }

    /// <summary>损坏内存表行必须被正式配置校验拒绝，检查后恢复原表。</summary>
    private static void InvalidStrengthConfigurationIsRejected()
    {
        foreach (var pair in new[] { ("EnemyDropHealthUnit", 0f), ("EnemyDropDamageUnit", float.NaN),
            ("EnemyDropMaxChance", .01f), ("EnemyDropMaxChance", 1.1f), ("DropChance", float.PositiveInfinity) })
            WithRule(pair.Item1, pair.Item2, () =>
            {
                bool rejected = false;
                try { typeof(ServerConfig).GetMethod("ValidateArcadeConfig", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ServerConfig.Instance, null); }
                catch (TargetInvocationException e) when (e.InnerException?.Message.Contains("TbArcadeRule") == true) { rejected = true; }
                Require(rejected, "Malformed rule accepted: " + pair.Item1);
            });
    }

    private static void HazardAndCapacityGuardsRemainEffective()
    {
        foreach (bool hazard in new[] { true, false })
        {
            var actor = Create(); var battle = actor.Battle;
            if (!hazard) for (int i = 0; i < ServerConfig.Instance.ArcadeRule.MaxDrops; i++)
                battle.Drops.Add(10000+i, new OnlineArcadeDropRuntime { RuntimeId = 10000+i, DropConfigId = "arcade.drop.gold.small" });
            int before = battle.Drops.Count;
            for (int id = 1; id <= 128; id++)
            {
                var enemy = new OnlineArcadeEnemyRuntime { RuntimeId = id, MaxHealth = int.MaxValue, Damage = int.MaxValue };
                battle.Enemies.Add(id, enemy); Call("KillEnemy", actor, battle, enemy, hazard);
            }
            Require(battle.Drops.Count == before, "Hazard or full capacity awarded extra loot");
        }
    }

    private static void WithRule(string name, float value, Action check)
    {
        var row = ServerConfig.Instance.ArcadeRule; var field = typeof(ArcadeRuleConfig).GetField(name)!;
        var old = field.GetValue(row);
        try { field.SetValue(row, value); check(); }
        finally { field.SetValue(row, old); }
    }
    private static object Call(string name, params object[] args) => typeof(OnlineArcadeRunActorSystem)
        .GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, args)!;
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
