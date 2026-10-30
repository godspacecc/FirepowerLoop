using ET;
using ET.Server;
using FirepowerLoop.Arcade;

// 仅运行整关清场射击的服务端权威回归，不启动服务器、不连接网络、不保存玩家数据。
if (args.Length != 3 || args[0] != "--testClass=PortalExitServerChecks" || args[1] != "--maxTests=8" || args[2] != "--includePassingTests=true")
    throw new ArgumentException("必须声明测试类、数量上限及通过项输出");
var config = ServerConfig.CreateForValidation("OnlineLocal", name => File.ReadAllBytes(Path.Combine("Server/GameServer/Config/Luban", name + ".bytes")));
World.Instance.AddSingleton(config); World.Instance.AddSingleton<TimeInfo>(); World.Instance.AddSingleton<ObjectPool>();

// 使用实际配置的第一关第四轮；传送门尚未接受离场，两个成员已经加载就绪。
OnlineArcadeRunActor Actor()
{
    var actor = new OnlineArcadeRunActor {
        Record = new OnlineArcadeRunRecord { RunId = 1, IsCasualRoute = true, Players = new() {
            new() { PlayerId = 1, Lives = 3, WeaponId = config.ArcadeRule.BaseWeaponId },
            new() { PlayerId = 2, Lives = 3, WeaponId = config.ArcadeRule.BaseWeaponId } } },
        Battle = new OnlineArcadeBattleRuntime {
            ArenaNumber = 1, ArenaConfigNumber = 1, WaveIndex = 3, WaveCount = 4,
            IsCasualRoute = true, AwaitingExit = true, WaveClearAwarded = true, RoundBossDefeated = true,
            LastTickAt = 1000, TreasureInitialized = true,
            RoundLayout = ArcadeRoundLayoutParameters.FromConfig(config.Tables.TbArcadeRoundLayout.Get("arena.1.round.4")) }
    };
    for (long id = 1; id <= 2; id++) actor.Battle.Players.Add(id, new OnlineArcadeBattlePlayerRuntime {
        PlayerId = id, PositionX = id * 2, PositionZ = -2, CurrentHealth = 100, MaxHealth = 100,
        Firing = true, LastInputAt = 1000 });
    return actor;
}
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
int failed = 0, executed = 0;
void Check(string name, Action test) {
    if (++executed > 8) throw new Exception("超出声明范围");
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e); }
}
Check("BothPlayersCanFireWhileAwaitingPortal", () => {
    var a = Actor(); a.TickBattle(1100);
    Require(a.Battle.Players.Values.All(p => p.FireSequence == 1), "清场等门时两名玩家都应成功射击");
    Require(!a.Battle.ArenaCompletionReady, "射击不能直接触发离场");
});
Check("PortalWaitingProjectilesMoveAndExpire", () => {
    var a = Actor(); foreach (var p in a.Battle.Players.Values) p.Firing = false;
    a.Battle.Projectiles.Add(1, new OnlineArcadeProjectileRuntime { RuntimeId = 1, OwnerPlayerId = 1,
        PositionX = 2, PositionZ = -2, VelocityZ = 1, RemainingSeconds = .15f, RemainingHits = 1 });
    a.TickBattle(1100);
    Require(a.Battle.Projectiles.TryGetValue(1, out var shot) && shot.PositionZ > -2 && shot.RemainingSeconds < .15f,
        "等待出口时弹丸应移动并消耗寿命");
    a.TickBattle(1200); Require(a.Battle.Projectiles.Count == 0, "弹丸到期必须回收");
});
Check("TransitionCompletionLoadingAndDisconnectBlockFire", () => {
    foreach (var gate in new Action<OnlineArcadeRunActor>[] {
        a => a.Battle.RoundTransitionPending = true, a => a.Battle.ArenaCompletionReady = true,
        a => a.Battle.ChallengeCompletionReady = true, a => a.Battle.AllPlayersDefeated = true,
        a => a.Battle.Players[2].ReadyRoundGeneration = -1, a => a.DisconnectedAtByPlayer.Add(2, 1000) }) {
        var a = Actor(); gate(a); a.TickBattle(1100);
        Require(a.Battle.Players.Values.All(p => p.FireSequence == 0), "传送、结算、加载或断线时不得射击");
    }
});
Check("DeadPlayerCannotFireWhilePartnerCan", () => {
    var a = Actor(); a.Battle.Players[1].Alive = false; a.TickBattle(1100);
    Require(a.Battle.Players[1].FireSequence == 0 && a.Battle.Players[2].FireSequence == 1, "死亡玩家不能射击，存活队友不受影响");
});
Check("TreasureRoomPlayerCannotFireWhilePartnerCan", () => {
    var a = Actor(); a.Battle.Players[1].InTreasureRoom = true; a.TickBattle(1100);
    Require(a.Battle.Players[1].FireSequence == 0 && a.Battle.Players[2].FireSequence == 1, "房间玩家不能射击，场内队友可以");
});
Check("ShopRegionCannotFire", () => {
    var a = Actor(); a.Battle.SlayerMartReturnWave = 1; a.TickBattle(1100);
    Require(a.Battle.Players.Values.All(p => p.FireSequence == 0), "商店区域不能射击");
});
Check("ExpiredInputAndCooldownStillBlockFire", () => {
    var a = Actor(); a.Battle.Players[1].LastInputAt = -10000; a.Battle.Players[2].FireCooldownSeconds = 1;
    a.TickBattle(1100); Require(a.Battle.Players.Values.All(p => p.FireSequence == 0), "过期输入与未结束冷却不能射击");
});
Check("WaitingDoesNotResumeSpawnsOrHazardClock", () => {
    var a = Actor(); a.Battle.SpawnQueue.Add("must-not-spawn"); a.Battle.NextSpawnAt = 0;
    a.TickBattle(1100);
    Require(a.Battle.SpawnQueueIndex == 0 && a.Battle.Enemies.Count == 0 && a.Battle.HazardElapsedSeconds == 0 &&
        a.Battle.ContentClock.Elapsed == 0, "清场射击不应恢复敌人生成或机关时钟");
});
Console.WriteLine($"PortalExitServerChecks: {executed - failed}/{executed} passed");
return failed == 0 ? 0 : 1;
