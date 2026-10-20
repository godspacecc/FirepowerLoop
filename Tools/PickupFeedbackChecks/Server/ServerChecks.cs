using System.Reflection;
using ET;
using ET.Server;

// 只运行声明的服务端拾取检查；不启动服务器、不连接网络、不读写玩家存档。
if (args.Length != 3 || args[0] != "--testClass=PickupFeedbackServerChecks" || args[1] != "--maxTests=7" || args[2] != "--includePassingTests=true")
    throw new ArgumentException("必须声明测试类、数量上限及通过项输出");
var config = ServerConfig.CreateForValidation("OnlineLocal", name => File.ReadAllBytes(Path.Combine("Server/GameServer/Config/Luban", name + ".bytes")));
World.Instance.AddSingleton(config); World.Instance.AddSingleton<TimeInfo>(); World.Instance.AddSingleton<ObjectPool>();
object Call(string name, params object[] values) => typeof(OnlineArcadeRunActorSystem)
    .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, values)!;
OnlineArcadeRunActor Actor(bool casual = true) => new()
{
    Record = new OnlineArcadeRunRecord { IsCasualRoute = casual, RunId = 1, Players = new() {
        new() { PlayerId = 1, Lives = 3, ScoreMultiplier = 3 }, new() { PlayerId = 2, Lives = 3 } } },
    Battle = new OnlineArcadeBattleRuntime(),
};
void Drop(OnlineArcadeRunActor actor, string id)
{
    if (actor.Battle.Players.Count == 0) actor.Battle.Players.Add(1, new OnlineArcadeBattlePlayerRuntime { PlayerId = 1 });
    var runtimeId = actor.Battle.NextDropRuntimeId++;
    actor.Battle.Drops.Add(runtimeId, new OnlineArcadeDropRuntime { RuntimeId = runtimeId, DropConfigId = id });
    Call("TickDrops", actor, actor.Battle);
}
void Require(bool condition) { if (!condition) throw new Exception("结果不符合预期"); }
int failed = 0;
void Check(string name, Action test) {
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e); }
}
Check("CasualScoreReceiptUsesMultiplier", () => {
    var a = Actor(); Drop(a, "arcade.drop.gold.small");
    Require(a.Record.Players[0].Score == 300 && a.Record.Players[0].RunGold == 0);
    Require(a.Battle.SupplyPickups.Count == 1 && a.Battle.SupplyPickups[0].ScoreAdded == 300 && a.Battle.SupplyPickups[0].GoldAdded == 0);
});
Check("GoldReceiptStaysSeparate", () => {
    var a = Actor(false); Drop(a, "arcade.drop.gold.small");
    Require(a.Record.Players[0].RunGold == 100 && a.Battle.SupplyPickups[0].GoldAdded == 100 && a.Battle.SupplyPickups[0].ScoreAdded == 0);
});
Check("FullSupplyHasNoReceiptThenPartialAddsOnce", () => {
    var a = Actor(); int maximum = config.Tables.TbArcadeDrop.Get("arcade.drop.bomb").MaxOwned;
    a.Record.Players[0].Bombs = maximum; Drop(a, "arcade.drop.bomb");
    Require(a.Battle.Drops.Count == 1 && a.Battle.SupplyPickups.Count == 0);
    a.Record.Players[0].Bombs--; Call("TickDrops", a, a.Battle); Call("TickDrops", a, a.Battle);
    Require(a.Battle.Drops.Count == 0 && a.Battle.SupplyPickups.Count == 1 && a.Battle.SupplyPickups[0].AddedAmount == 1);
});
Check("OverflowRejectedWithoutReceipt", () => {
    var a = Actor(); a.Record.Players[0].Score = long.MaxValue - 1; Drop(a, "arcade.drop.gold.small");
    Require(a.Battle.Drops.Count == 1 && a.Battle.SupplyPickups.Count == 0 && a.Record.Players[0].Score == long.MaxValue - 1);
});
Check("WeaponAndPowerupHaveIndependentReceipts", () => {
    var a = Actor(); Drop(a, "arcade.drop.weapon.spread"); Drop(a, "arcade.drop.powerup.shield");
    Require(a.Battle.SupplyPickups.Count == 2 && a.Battle.SupplyPickups.Select(x => x.RuntimeId).Distinct().Count() == 2);
    Require(a.Battle.Players[1].TemporaryWeaponId == "arcade.weapon.spread" && a.Battle.Players[1].Powerups.Count == 1);
});
Check("SnapshotAndClonePreserveActualDeltas", () => {
    var a = Actor(); Drop(a, "arcade.drop.gold.small");
    using var snapshot = (OnlineArcadeBattleInfo)Call("BuildBattleSnapshot", a.Battle);
    using var clone = (OnlineArcadeBattleInfo)Call("CloneBattleSnapshot", snapshot);
    Require(clone.SupplyPickups.Count == 1 && clone.SupplyPickups[0].ScoreAdded == 300 && clone.SupplyPickups[0].GoldAdded == 0);
});
Check("TreasureRoomRecordsActualRewardsAndRejectsFullSupply", () => {
    var a = Actor();
    var rules = FirepowerLoop.Arcade.ArcadeTreasureRoomParameters.FromConfig(config.Tables.TbArcadeTreasureRoom.DataList.First());
    int maximum = config.Tables.TbArcadeDrop.Get("arcade.drop.bomb").MaxOwned;
    a.Record.Players[0].Bombs = maximum - 1;
    var bomb = new FirepowerLoop.Arcade.ArcadeTreasureRewardParameters(1, FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Bomb, 0, 0, 0, 0, 3);
    Require((bool)Call("ApplyTreasureReward", a, 1L, bomb, rules, true));
    Require(!(bool)Call("ApplyTreasureReward", a, 1L, bomb, rules, true));
    Require(a.Battle.SupplyPickups.Count == 1 && a.Battle.SupplyPickups[0].AddedAmount == 1);
    var gem = new FirepowerLoop.Arcade.ArcadeTreasureRewardParameters(2, FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Gem, 7, -4, 200, 0, 1);
    Require((bool)Call("ApplyTreasureReward", a, 1L, gem, rules, true));
    Require(a.Battle.SupplyPickups.Count == 2 && a.Battle.SupplyPickups[1].ScoreAdded == 600 && a.Battle.SupplyPickups[1].GoldAdded == 0);
    using var snapshot = (OnlineArcadeBattleInfo)Call("BuildBattleSnapshot", a.Battle);
    using var clone = (OnlineArcadeBattleInfo)Call("CloneBattleSnapshot", snapshot);
    Require(clone.SupplyPickups[1].PositionX == 7 && clone.SupplyPickups[1].PositionZ == -4);
});
Console.WriteLine($"PickupFeedbackServerChecks: {7 - failed}/7 passed");
return failed == 0 ? 0 : 1;
