using System.Reflection;
using ET;
using ET.Server;

// 只运行本类的服务端冲刺检查，不启动网络或读写玩家存档。
if (args.Length != 3 || args[0] != "testClass=ArcadeDashChecks" || args[1] != "maxTests=11" || args[2] != "includePassingTests=true")
    throw new ArgumentException("必须声明 testClass=ArcadeDashChecks maxTests=11 includePassingTests=true");
var config = ServerConfig.CreateForValidation("OnlineLocal", name => File.ReadAllBytes(Path.Combine("Server/GameServer/Config/Luban", name + ".bytes")));
World.Instance.AddSingleton(config);
World.Instance.AddSingleton<TimeInfo>();
World.Instance.AddSingleton<ObjectPool>();
object Call(string method, params object[] values) => typeof(OnlineArcadeRunActorSystem)
    .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, values)!;
OnlineArcadeRunActor Actor(bool casual = false)
{
    var record = new OnlineArcadeRunRecord { RunId = 1, RoomId = 2, Revision = 1, NextArena = 2, ArenaConfigNumber = 2,
        Phase = OnlineArcadeRunPhase.SafeCheckpoint, ConfigFingerprint = config.ArcadeRule.ConfigFingerprint,
        IsCasualRoute = casual,
        Players = new() { new() { PlayerId = 1, Lives = 3, Dashes = 2, Ready = true, WeaponId = config.ArcadeRule.BaseWeaponId },
            new() { PlayerId = 2, Lives = 3, Dashes = 2, Ready = true, WeaponId = config.ArcadeRule.BaseWeaponId } } };
    var battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false);
    foreach (var p in battle.Players.Values) { p.PositionX = p.PositionZ = 0; p.ReadyRoundGeneration = battle.RoundGeneration; }
    return new OnlineArcadeRunActor { Record = record, Battle = battle };
}
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
void Submit(OnlineArcadeRunActor a, long id, float mx, float mz, float ax, float az, bool dash = true) =>
    a.SubmitInput(id, a.Battle.ArenaNumber, a.Battle.Players[id].LastInputSequence + 1, mx, mz, ax, az, false, false, dash,
        a.Battle.RoundGeneration, true);
void Step(OnlineArcadeRunActor a) => Call("TickPlayers", a, a.Battle, .02f, TimeInfo.Instance.ServerNow());
int total = 0, failed = 0;
void Check(string name, Action test)
{
    if (++total > 11) throw new Exception("超出声明数量");
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + (e.InnerException ?? e).Message); }
}
foreach (var (x, z) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
    Check($"StationaryDash({x},{z})", () => {
        var a = Actor(); Submit(a, 1, 0, 0, x, z); Step(a); var p = a.Battle.Players[1];
        Require(p.PositionX * x + p.PositionZ * z > .01f, "静止冲刺没有沿朝向位移");
        Require(a.Record.Players[0].Dashes == 1 && p.DashSeconds > 0 && p.InvulnerabilitySeconds > 0, "库存或保护异常");
    });
Check("MoveInputWinsAtFullSpeed", () => {
    var a = Actor(); Submit(a, 1, .3f, .4f, -1, 0); Step(a); var p = a.Battle.Players[1];
    float distance = MathF.Sqrt(p.PositionX * p.PositionX + p.PositionZ * p.PositionZ);
    Require(MathF.Abs(p.PositionX / distance - .6f) < .001f && MathF.Abs(p.PositionZ / distance - .8f) < .001f, "方向未归一化");
    Require(MathF.Abs(distance - config.ArcadeRule.PlayerMoveSpeed * 2.4f * .02f) < .001f, "半推摇杆缩短冲刺");
});
Check("ReleaseAndAimCannotRedirectDash", () => {
    var a = Actor(); Submit(a, 1, 1, 0, 0, 1); Submit(a, 1, 0, 0, 0, -1, false); Step(a);
    Require(a.Battle.Players[1].PositionX > .01f, "松键中断冲刺");
    Submit(a, 1, -1, 0, 0, -1, false); float before = a.Battle.Players[1].PositionX; Step(a);
    Require(a.Battle.Players[1].PositionX > before, "后续输入改变冲刺方向");
});
Check("EachPlayerHasIndependentDashDirection", () => {
    var a = Actor(); Submit(a, 1, 0, 0, 1, 0); Submit(a, 2, 0, 0, 0, -1); Step(a);
    Require(a.Battle.Players[1].PositionX > 0 && a.Battle.Players[2].PositionZ < 0, "双人冲刺方向串用");
});
Check("RepeatedDashCannotRedirectOrConsume", () => {
    var a = Actor(); Submit(a, 1, 1, 0, 0, 1); Submit(a, 1, -1, 0, 0, 1); Step(a);
    Require(a.Record.Players[0].Dashes == 1 && a.Battle.Players[1].PositionX > 0, "重复请求改变冲刺");
});
Check("ExpiredDashUsesOrdinaryInput", () => {
    var a = Actor(); Submit(a, 1, 1, 0, 0, 1); a.Battle.Players[1].DashSeconds = 0;
    Submit(a, 1, 0, 0, 0, 1, false); Step(a);
    Require(a.Battle.Players[1].PositionX == 0 && a.Battle.Players[1].PositionZ == 0, "冲刺结束后继续滑动");
});
Check("SnapshotAndCloneRetainDashDirection", () => {
    var a = Actor(); Submit(a, 1, .3f, .4f, -1, 0);
    var snapshot = (OnlineArcadeBattleInfo)Call("BuildBattleSnapshot", a.Battle);
    var copy = (OnlineArcadeBattleInfo)Call("CloneBattleSnapshot", snapshot);
    snapshot.Players[0].DashDirectionX = -1;
    var player = copy.Players.Single(p => p.PlayerId == 1);
    Require(MathF.Abs(player.DashDirectionX - .6f) < .001f && MathF.Abs(player.DashDirectionZ - .8f) < .001f,
        "复制快照丢失冲刺方向或引用了可变消息");
});
Check("RoundCheckpointRetainsDashDirection", () => {
    var a = Actor(true); Submit(a, 1, 1, 0, 0, -1);
    typeof(OnlineArcadeRunManagerSystem).GetMethod("CaptureRoundCheckpoint", BindingFlags.Static | BindingFlags.NonPublic)!
        .Invoke(null, new object[] { a.Record, a.Battle });
    var restored = OnlineArcadeRunActorSystem.PrepareBattle(a.Record, false).Players[1];
    Require(restored.DashSeconds > 0 && restored.DashDirectionX == 1 && restored.DashDirectionZ == 0,
        "轮前恢复丢失锁定方向");
});
Console.WriteLine($"ArcadeDashChecks: {total - failed}/{total} passed");
return failed == 0 && total == 11 ? 0 : 1;
