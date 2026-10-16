using System.Reflection;
using ET;
using ET.Server;

// 使用正式配置及服务器拾取裁决；不启动网络、不读写玩家存档。
var repository = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var config = ServerConfig.CreateForValidation("OnlineLocal", name => File.ReadAllBytes(
    Path.Combine(repository, "Server/GameServer/Config/Luban", name + ".bytes")));
World.Instance.AddSingleton(config); World.Instance.AddSingleton<TimeInfo>();
World.Instance.AddSingleton<ObjectPool>();
// 定向检查必须显式声明真实类、最大数量和通过项输出，错误过滤绝不回退到历史全集。
if (args.Length > 1)
{
    if (args.Length == 7 && args[1] == "--testClass" && args[2] == nameof(ArcadeCheckpointChecks) &&
        args[3] == "--maxTests" && args[4] == "6" && args[5] == "--includePassingTests" && args[6] == "true")
    {
        Environment.ExitCode = ArcadeCheckpointChecks.Run(config, 6);
        return;
    }
    if (args.Length != 7 || args[1] != "--testClass" || args[2] != nameof(ArcadeDropLifetimeChecks) ||
        args[3] != "--maxTests" || !int.TryParse(args[4], out int maximum) || maximum < 4 ||
        args[5] != "--includePassingTests" || args[6] != "true")
        throw new ArgumentException("Expected repository --testClass ArcadeDropLifetimeChecks --maxTests 4 --includePassingTests true");
    Environment.ExitCode = ArcadeDropLifetimeChecks.Run(config, maximum);
    return;
}
void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
object Call(string method, params object[] values) => typeof(OnlineArcadeRunActorSystem)
    .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, values)!;
int passed = 0;
void Check(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
OnlineArcadeRunActor Actor()
{
    var record = new OnlineArcadeRunRecord { RunId = 1, RoomId = 2, NextArena = 1, ArenaConfigNumber = 1,
        Phase = OnlineArcadeRunPhase.SafeCheckpoint, ConfigFingerprint = config.ArcadeRule.ConfigFingerprint,
        Players = new() { new() { PlayerId = 1, Lives = 3, Ready = true, WeaponId = config.ArcadeRule.BaseWeaponId },
            new() { PlayerId = 2, Lives = 3, Ready = true, WeaponId = config.ArcadeRule.BaseWeaponId } } };
    var battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false);
    foreach (var player in battle.Players.Values) { player.PositionX = 20; player.PositionZ = 20; }
    return new OnlineArcadeRunActor { Record = record, Battle = battle };
}
void Add(OnlineArcadeRunActor actor, long id, ArcadeDropConfig definition) => actor.Battle.Drops.Add(id,
    new OnlineArcadeDropRuntime { RuntimeId = id, DropConfigId = definition.Id, PositionX = 0, PositionZ = 0 });
foreach (var kind in new[] { ArcadeDropKind.Bomb, ArcadeDropKind.Dash })
{
    Check(kind + " exit wait retains full supply then collects once through TickBattle", () =>
    {
        var actor = Actor(); var battle = actor.Battle; var record = actor.Record.Players[0];
        battle.AwaitingExit = true; battle.WaveClearAwarded = true;
        battle.Players[1].PositionX = battle.Players[1].PositionZ = 0;
        var supply = config.Tables.TbArcadeDrop.DataList.First(d => d.Kind == kind);
        if (kind == ArcadeDropKind.Bomb) record.Bombs = supply.MaxOwned; else record.Dashes = supply.MaxOwned;
        Add(actor, 1, supply);
        long now = battle.LastTickAt;
        actor.TickBattle(now + 20);
        Require(battle.Drops.ContainsKey(1) && battle.SupplyPickups.Count == 0, "full supply not retained");
        if (kind == ArcadeDropKind.Bomb) record.Bombs--; else record.Dashes--;
        long revision = battle.BattleRevision;
        Require(actor.TickBattle(now + 40), "exit wait pickup not published");
        actor.TickBattle(now + 60);
        Require(battle.AwaitingExit && !battle.ArenaCompletionReady, "pickup ended exit wait");
        Require(battle.Drops.Count == 0, "exit wait skipped pickup");
        Require((kind == ArcadeDropKind.Bomb ? record.Bombs : record.Dashes) == supply.MaxOwned, "wrong exit wait inventory");
        Require(battle.BattleRevision > revision, "pickup did not advance snapshot revision");
        Require(battle.SupplyPickups.Count == 1 && battle.SupplyPickups[0].CollectedByPlayerId == 1 &&
            battle.SupplyPickups[0].AddedAmount == 1, "exit wait duplicated receipt");
    });
    Check(kind + " exit wait full nearer player allows eligible teammate through TickBattle", () =>
    {
        var actor = Actor(); var battle = actor.Battle;
        battle.AwaitingExit = true; battle.WaveClearAwarded = true;
        var supply = config.Tables.TbArcadeDrop.DataList.First(d => d.Kind == kind);
        var full = actor.Record.Players[0]; var recipient = actor.Record.Players[1];
        if (kind == ArcadeDropKind.Bomb) { full.Bombs = supply.MaxOwned; recipient.Bombs = 0; }
        else { full.Dashes = supply.MaxOwned; recipient.Dashes = 0; }
        battle.Players[1].PositionX = battle.Players[1].PositionZ = 0;
        battle.Players[2].PositionX = .5f; battle.Players[2].PositionZ = 0;
        Add(actor, 1, supply);
        actor.TickBattle(battle.LastTickAt + 20);
        Require((kind == ArcadeDropKind.Bomb ? recipient.Bombs : recipient.Dashes) == supply.Amount, "exit wait blocked teammate");
        Require((kind == ArcadeDropKind.Bomb ? full.Bombs : full.Dashes) == supply.MaxOwned, "full player changed");
        Require(battle.Drops.Count == 0 && battle.SupplyPickups.Single().CollectedByPlayerId == 2, "wrong exit wait recipient");
    });
    Check(kind + " stays at cap then is collected once after real use", () =>
    {
        var actor = Actor(); var battle = actor.Battle; var player = battle.Players[1];
        var record = actor.Record.Players[0];
        var supply = config.Tables.TbArcadeDrop.DataList.First(d => d.Kind == kind);
        if (kind == ArcadeDropKind.Bomb) record.Bombs = supply.MaxOwned; else record.Dashes = supply.MaxOwned;
        Add(actor, 1, supply); var original = battle.Drops[1];
        for (int i = 0; i < 3; i++)
        {
            player.PositionX = player.PositionZ = 0; Require(!(bool)Call("TickDrops", actor, battle), "full pickup changed battle");
            player.PositionX = player.PositionZ = 20; Call("TickDrops", actor, battle);
        }
        Require(battle.Drops.TryGetValue(1, out var retained) && ReferenceEquals(original, retained), "full supply disappeared");
        Require(battle.SupplyPickups.Count == 0, "rejection published success feedback");
        if (kind == ArcadeDropKind.Bomb) Call("UseBomb", actor, battle, player); else Call("UseDash", actor, player);
        player.PositionX = player.PositionZ = 0;
        Require((bool)Call("TickDrops", actor, battle), "available supply rejected");
        Require(!(bool)Call("TickDrops", actor, battle), "same drop awarded twice");
        Require(battle.Drops.Count == 0, "collected drop remains");
        Require(battle.SupplyPickups.Count == 1 && battle.SupplyPickups[0].CollectedByPlayerId == 1 &&
            battle.SupplyPickups[0].AddedAmount == 1, "duplicate or incorrect receipt");
        Require((kind == ArcadeDropKind.Bomb ? record.Bombs : record.Dashes) == supply.MaxOwned, "wrong inventory");
    });
    Check(kind + " full nearer player does not block eligible teammate", () =>
    {
        var actor = Actor(); var battle = actor.Battle;
        var supply = config.Tables.TbArcadeDrop.DataList.First(d => d.Kind == kind);
        var full = actor.Record.Players[0]; var recipient = actor.Record.Players[1];
        if (kind == ArcadeDropKind.Bomb) { full.Bombs = supply.MaxOwned; recipient.Bombs = 0; }
        else { full.Dashes = supply.MaxOwned; recipient.Dashes = 0; }
        battle.Players[1].PositionX = battle.Players[1].PositionZ = 0;
        battle.Players[2].PositionX = .5f; battle.Players[2].PositionZ = 0;
        Add(actor, 1, supply); Call("TickDrops", actor, battle);
        Require((kind == ArcadeDropKind.Bomb ? recipient.Bombs : recipient.Dashes) == supply.Amount, "eligible teammate blocked");
        Require((kind == ArcadeDropKind.Bomb ? full.Bombs : full.Dashes) == supply.MaxOwned, "full player changed");
        Require(battle.SupplyPickups.Single().CollectedByPlayerId == 2, "feedback attributed to wrong player");
    });
    Check(kind + " partial addition survives cloned and serialized snapshots", () =>
    {
        var actor = Actor(); var battle = actor.Battle; var record = actor.Record.Players[0];
        var supply = config.Tables.TbArcadeDrop.DataList.First(d => d.Kind == kind);
        // 仅临时扩大进程内奖励输入，检验奖励被上限截断；不改 CSV 或生成配置。
        var amountField = typeof(ArcadeDropConfig).GetField("Amount")!;
        int configuredAmount = supply.Amount;
        try
        {
            amountField.SetValue(supply, 3);
            if (kind == ArcadeDropKind.Bomb) record.Bombs = supply.MaxOwned - 1;
            else record.Dashes = supply.MaxOwned - 1;
            battle.Players[1].PositionX = battle.Players[1].PositionZ = 0;
            Add(actor, 1, supply); Call("TickDrops", actor, battle);
            Require((kind == ArcadeDropKind.Bomb ? record.Bombs : record.Dashes) == supply.MaxOwned, "partial pickup exceeded cap");
            using var snapshot = (OnlineArcadeBattleInfo)Call("BuildBattleSnapshot", battle);
            using var clone = (OnlineArcadeBattleInfo)Call("CloneBattleSnapshot", snapshot);
            var bytes = MemoryPack.MemoryPackSerializer.Serialize(clone);
            using var restored = MemoryPack.MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(bytes)!;
            Require(restored.Drops.Count == 0 && restored.SupplyPickups.Count == 1, "snapshot lost pickup/removal");
            var receipt = restored.SupplyPickups.Single();
            Require(receipt.RuntimeId == 1 && receipt.CollectedByPlayerId == 1 && receipt.AddedAmount == 1 &&
                receipt.DropConfigId == supply.Id, "receipt did not preserve actual amount and identity");
        }
        finally { amountField.SetValue(supply, configuredAmount); }
    });
}
Check("exit wait collects gold and weapon beside rejected supply through TickBattle", () =>
{
    var actor = Actor(); var battle = actor.Battle; var record = actor.Record.Players[0];
    battle.AwaitingExit = true; battle.WaveClearAwarded = true;
    battle.Players[1].PositionX = battle.Players[1].PositionZ = 0;
    var bomb = config.Tables.TbArcadeDrop.DataList.First(d => d.Kind == ArcadeDropKind.Bomb);
    var gold = config.Tables.TbArcadeDrop.DataList.First(d => d.Kind == ArcadeDropKind.RunGold);
    var weapon = config.Tables.TbArcadeDrop.DataList.First(d => d.Kind == ArcadeDropKind.TemporaryWeapon);
    record.Bombs = bomb.MaxOwned; Add(actor, 1, bomb); Add(actor, 2, gold); Add(actor, 3, weapon);
    long now = battle.LastTickAt;
    actor.TickBattle(now + 20); actor.TickBattle(now + 40);
    Require(battle.Drops.Keys.SequenceEqual(new long[] { 1 }), "exit wait retained wrong drops");
    Require(record.RunGold == gold.Amount && battle.Players[1].TemporaryWeaponId == weapon.ProductId, "exit wait skipped gold or weapon");
});
Check("rejected supplies do not block gold or weapon", () =>
{
    var actor = Actor(); var battle = actor.Battle; var record = actor.Record.Players[0];
    battle.Players[1].PositionX = battle.Players[1].PositionZ = 0;
    var bomb = config.Tables.TbArcadeDrop.DataList.First(d => d.Kind == ArcadeDropKind.Bomb);
    var gold = config.Tables.TbArcadeDrop.DataList.First(d => d.Kind == ArcadeDropKind.RunGold);
    var weapon = config.Tables.TbArcadeDrop.DataList.First(d => d.Kind == ArcadeDropKind.TemporaryWeapon);
    record.Bombs = bomb.MaxOwned; Add(actor, 1, bomb); Add(actor, 2, gold); Add(actor, 3, weapon);
    Call("TickDrops", actor, battle);
    Require(battle.Drops.Keys.SequenceEqual(new long[] { 1 }), "wrong retained drops");
    Require(record.RunGold == gold.Amount && battle.Players[1].TemporaryWeaponId == weapon.ProductId, "other reward blocked");
});
Console.WriteLine($"Passed {passed} scoped server supply checks.");
