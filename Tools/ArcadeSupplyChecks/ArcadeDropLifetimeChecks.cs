using System.Reflection;
using ET;
using ET.Server;

/// <summary>真实服务器 Tick 与协议拷贝的定向掉落寿命检查，不启动网络或玩家存档。</summary>
internal static class ArcadeDropLifetimeChecks
{
    public static int Run(ServerConfig config, int maximum)
    {
        var checks = new (string Name, Action Check)[]
        {
            (nameof(ExpiresWithoutReward), () => ExpiresWithoutReward(config)),
            (nameof(FadingDropRemainsCollectible), () => FadingDropRemainsCollectible(config)),
            (nameof(ExitWaitTicksAndPausedTimeFreezes), () => ExitWaitTicksAndPausedTimeFreezes(config)),
            (nameof(SnapshotPreservesRemainingSeconds), () => SnapshotPreservesRemainingSeconds(config))
        };
        if (maximum < checks.Length) throw new ArgumentException("Maximum is smaller than the selected class.");
        int failed = 0;
        foreach (var check in checks)
        {
            try { check.Check(); Console.WriteLine("PASS ArcadeDropLifetimeChecks." + check.Name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL ArcadeDropLifetimeChecks." + check.Name + ": " + error.GetBaseException().Message); }
        }
        Console.WriteLine($"ArcadeDropLifetimeChecks: {checks.Length - failed} passed, {failed} failed, maximum {maximum}");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>构造使用正式配置的双人等待出口现场，隔离生成、移动战斗与落盘副作用。</summary>
    private static OnlineArcadeRunActor Actor(ServerConfig config)
    {
        var record = new OnlineArcadeRunRecord { RunId = 1, RoomId = 2, NextArena = 1, ArenaConfigNumber = 1,
            Phase = OnlineArcadeRunPhase.SafeShop, ConfigFingerprint = config.ArcadeRule.ConfigFingerprint,
            Players = new() { new() { PlayerId = 1, Lives = 3, Ready = true, WeaponId = config.ArcadeRule.BaseWeaponId },
                new() { PlayerId = 2, Lives = 3, Ready = true, WeaponId = config.ArcadeRule.BaseWeaponId } } };
        var battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false);
        battle.AwaitingExit = true; battle.WaveClearAwarded = true;
        foreach (var player in battle.Players.Values) { player.PositionX = 20; player.PositionZ = 20; }
        var definition = config.Tables.TbArcadeDrop.DataList.First(d => d.Kind == ArcadeDropKind.RunGold);
        battle.Drops.Add(1, new OnlineArcadeDropRuntime { RuntimeId = 1, DropConfigId = definition.Id });
        return new OnlineArcadeRunActor { Record = record, Battle = battle };
    }

    /// <summary>以正式服务器单帧间隔推进指定数量的活动帧。</summary>
    private static void Advance(OnlineArcadeRunActor actor, int ticks)
    {
        for (int i = 0; i < ticks; i++) actor.TickBattle(actor.Battle.LastTickAt + 100);
    }

    /// <summary>到期只删除现场，不产生奖励或已领取记录。</summary>
    private static void ExpiresWithoutReward(ServerConfig config)
    {
        var actor = Actor(config); var player = actor.Record.Players[0];
        long score = player.Score; int gold = player.RunGold;
        Advance(actor, 301);
        Require(actor.Battle.Drops.Count == 0, "30-second drop must expire during exit wait");
        Require(player.Score == score && player.RunGold == gold && actor.Battle.SupplyPickups.Count == 0,
            "expiry must not award gold, score or a pickup receipt");
    }

    /// <summary>最后三秒属于可领取的有效期。</summary>
    private static void FadingDropRemainsCollectible(ServerConfig config)
    {
        var actor = Actor(config); Advance(actor, 280);
        float seconds = Seconds(actor.Battle.Drops[1]);
        Require(seconds > 0 && seconds < 3, "drop must enter its final three seconds");
        actor.Battle.Players[1].PositionX = actor.Battle.Players[1].PositionZ = 0;
        Advance(actor, 1);
        Require(actor.Battle.Drops.Count == 0 && actor.Battle.SupplyPickups.Count == 1,
            "fading drop must still be collectible exactly once");
    }

    /// <summary>出口等待继续计时，暂停跳帧并重置活动时间基准后恢复。</summary>
    private static void ExitWaitTicksAndPausedTimeFreezes(ServerConfig config)
    {
        var actor = Actor(config); Advance(actor, 10);
        float before = Seconds(actor.Battle.Drops[1]);
        Require(Math.Abs(before - 29f) < .01f, "exit wait must advance drop lifetime");
        // 服务器暂停路径跳过 Tick 并重置 LastTickAt；恢复后的首帧只计算活动时间。
        actor.Battle.LastTickAt += 60000;
        Require(Seconds(actor.Battle.Drops[1]) == before, "paused wall time must not mutate lifetime");
        Advance(actor, 1);
        Require(Math.Abs(Seconds(actor.Battle.Drops[1]) - (before - .1f)) < .01f,
            "resume must consume only its active frame delta");
    }

    /// <summary>现场投影、深拷贝和协议序列化保留两种独立奖励寿命。</summary>
    private static void SnapshotPreservesRemainingSeconds(ServerConfig config)
    {
        var actor = Actor(config); Advance(actor, 280);
        var parameters = AnotherDay.Arcade.ArcadeTreasureRoomParameters.FromConfig(
            config.Tables.TbArcadeTreasureRoom.DataList.First());
        var room = new AnotherDay.Arcade.ArcadeTreasureRoomState("lifetime-test", parameters);
        Require(room.TryEnter(room.InstanceId, 1, true, true, out _), "treasure fixture must start");
        room.Advance(12f); actor.Battle.TreasureRoom = room;
        using var snapshot = (OnlineArcadeBattleInfo)Call("BuildBattleSnapshot", actor.Battle);
        using var clone = (OnlineArcadeBattleInfo)Call("CloneBattleSnapshot", snapshot);
        using var decoded = MemoryPack.MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(
            MemoryPack.MemoryPackSerializer.Serialize(clone))!;
        var property = typeof(OnlineArcadeDropInfo).GetProperty("RemainingSeconds");
        Require(property != null, "drop protocol requires RemainingSeconds");
        Require(Math.Abs((float)property!.GetValue(decoded.Drops.Single())! - Seconds(actor.Battle.Drops[1])) < .001f,
            "snapshot clone and serialization must retain lifetime");
        var treasureProperty = typeof(OnlineArcadeBattleInfo).GetProperty("TreasureRewardSeconds");
        Require(treasureProperty != null,
            "treasure reward protocol requires its independent lifetime");
        Require(Math.Abs((float)treasureProperty!.GetValue(decoded)! - 18f) < .001f,
            "treasure reward lifetime must survive snapshot clone and serialization");
    }

    private static float Seconds(OnlineArcadeDropRuntime drop)
    {
        var field = typeof(OnlineArcadeDropRuntime).GetField("RemainingSeconds");
        Require(field != null, "server drop runtime requires RemainingSeconds");
        return (float)field!.GetValue(drop)!;
    }

    private static object Call(string name, params object[] values) => typeof(OnlineArcadeRunActorSystem)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, values)!;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
