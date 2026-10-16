using System.Reflection;
using System.Text.Json;
using ET;
using ET.Server;

/// <summary>正式服务器安全意图与强化道具的定向回归，不启动网络、不使用玩家文件。</summary>
internal static class ArcadeCheckpointChecks
{
    public static int Run(ServerConfig config, int maximum)
    {
        var checks = new (string Name, Action Check)[] {
            ("RetiredTradeIntentsCannotMutateResources", () => {
                foreach (int kind in new[] { 1, 2 }) {
                    var run = Make(config); var player = run.Players[0];
                    try { Intent(run, player, kind, true); throw new Exception("旧交易意图被接受"); }
                    catch (TargetInvocationException error) when (error.InnerException is RpcException) { }
                    Assert(player.RunGold == 99 && player.Lives == 3 && !player.Ready, "交易修改了状态");
                }
            }),
            ("ReadyAndCancelRemainMutuallyExclusiveWithChallenge", () => {
                var run = Make(config); run.ChallengeId = "challenge";
                var player = run.Players[0]; player.ChallengeReady = true;
                Intent(run, player, 3, true); Assert(player.Ready && !player.ChallengeReady, "普通准备未排除挑战");
                Intent(run, player, 3, false); Assert(!player.Ready, "不能取消准备");
                Intent(run, player, 7, true); Assert(player.ChallengeReady && !player.Ready, "挑战准备失效");
            }),
            ("DeadMemberDoesNotRequireLifePurchase", () => {
                var run = Make(config); run.Players[1].Lives = 0;
                Intent(run, run.Players[0], 3, true);
                var battle = OnlineArcadeRunActorSystem.PrepareBattle(run, false);
                Assert(battle.Players[1].Alive && !battle.Players[2].Alive, "零生命成员阻断存活成员开战");
                Assert(typeof(OnlineArcadePlayerRecord).GetProperty("ShopOffers") == null, "补售生命仍存在");
            }),
            ("SettlementAndAbandonStillClearReady", () => {
                var run = Make(config); var player = run.Players[0]; player.Ready = true;
                Intent(run, player, 4, false); Assert(player.SettlementConfirmed && !player.Ready && !player.AbandonConfirmed, "结算意图失效");
                Intent(run, player, 5, false); Assert(player.AbandonConfirmed && !player.SettlementConfirmed, "放弃意图失效");
            }),
            ("OldQuotesAreIgnoredAndOwnedEffectsRemain", () => {
                var run = Make(config); run.Players[0].UpgradeStacks["arcade.upgrade.damage"] = 2;
                var json = JsonSerializer.Serialize(run).Replace("\"PlayerId\":1,", "\"PlayerId\":1,\"ShopOffers\":[{\"OfferId\":\"old\"}],");
                var restored = JsonSerializer.Deserialize<OnlineArcadeRunRecord>(json)!;
                restored.ConfigFingerprint = "firepowerloop-20261014-v1";
                typeof(OnlineArcadeRunManagerSystem).GetMethod("NormalizeLoadedCheckpoint", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { restored });
                Assert(restored.ConfigFingerprint == config.ArcadeRule.ConfigFingerprint, "本项目旧局配置版本无法恢复");
                Assert(!JsonSerializer.Serialize(restored).Contains("ShopOffers"), "旧报价被重新写入");
                Assert(restored.Players[0].UpgradeStacks["arcade.upgrade.damage"] == 2 && restored.Players[0].RunGold == 99, "已持有效果丢失");
            }),
            ("PowerupsStillCoexistRefreshAndExpire", () => {
                var run = Make(config); foreach (var player in run.Players) player.Ready = true;
                var actor = new OnlineArcadeRunActor { Record = run, Battle = OnlineArcadeRunActorSystem.PrepareBattle(run, false) };
                var playerRuntime = actor.Battle.Players[1];
                var drops = config.Tables.TbArcadeDrop.DataList.Where(d => d.Kind == ArcadeDropKind.Powerup).Take(2).ToArray();
                Assert(drops.Length == 2, "正式强化道具不足");
                foreach (var drop in drops) Assert((bool)ActorCall("ApplyDrop", actor, playerRuntime, drop), "道具不能拾取");
                Assert(playerRuntime.Powerups.Count == 2, "不同道具不能并存");
                var effect = playerRuntime.Powerups.Values.First(p => p.ProductId == drops[0].ProductId);
                effect.RemainingSeconds = .1f; effect.Charges = 0;
                ActorCall("ApplyDrop", actor, playerRuntime, drops[0]);
                var refreshed = playerRuntime.Powerups.Values.First(p => p.ProductId == drops[0].ProductId);
                Assert(refreshed.RemainingSeconds == drops[0].DurationSeconds && playerRuntime.Powerups.Count == 2, "重复拾取未刷新同类状态");
                foreach (var powerup in playerRuntime.Powerups.Values) powerup.RemainingSeconds = .1f;
                ActorCall("TickPowerups", actor, actor.Battle, .2f);
                Assert(playerRuntime.Powerups.Count == 0, "到期道具未回收");
            })
        };
        if (maximum != checks.Length) throw new ArgumentException("ArcadeCheckpointChecks maximum must be 6");
        int failed = 0;
        foreach (var check in checks)
            try { check.Check(); Console.WriteLine("PASS ArcadeCheckpointChecks." + check.Name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL ArcadeCheckpointChecks." + check.Name + ": " + error.GetBaseException().Message); }
        return failed == 0 ? 0 : 1;
    }
    private static OnlineArcadeRunRecord Make(ServerConfig config) => new() {
        RunId = 1, RoomId = 2, Revision = 1, Phase = OnlineArcadeRunPhase.SafeCheckpoint, NextArena = 1,
        ConfigFingerprint = config.ArcadeRule.ConfigFingerprint,
        Players = new() { new() { PlayerId = 1, Lives = 3, RunGold = 99, WeaponId = config.ArcadeRule.BaseWeaponId }, new() { PlayerId = 2, Lives = 3, WeaponId = config.ArcadeRule.BaseWeaponId } }
    };
    private static void Intent(OnlineArcadeRunRecord run, OnlineArcadePlayerRecord player, int kind, bool ready) =>
        typeof(OnlineArcadeRunManagerSystem).GetMethod("ApplyCheckpointIntent", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { run, player, kind, ready });
    private static object ActorCall(string method, params object[] args) =>
        typeof(OnlineArcadeRunActorSystem).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
