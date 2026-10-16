using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

// 只调用本项目正式服务器规则，不监听网络，不使用正式玩家文件。
if (args.Length != 3 || args[0] != "--testClass=ArcadeArmoryChecks" || args[1] != "--maxTests=10" || args[2] != "--includePassingTests=true")
    throw new ArgumentException("必须指定真实检查类、最大10项及通过项输出");
var config = ServerConfig.CreateForValidation("OnlineLocal", n => File.ReadAllBytes(Path.Combine("Server/GameServer/Config/Luban", n + ".bytes")));
World.Instance.AddSingleton(config); World.Instance.AddSingleton<TimeInfo>(); World.Instance.AddSingleton<ObjectPool>();
object Call(string method, params object[] values) => typeof(OnlineArcadeRunActorSystem).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, values)!;
void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
OnlineArcadeRunActor Actor(int map = 2, int ordinal = 2, bool casual = true)
{
    var record = new OnlineArcadeRunRecord { RunId = 101, RoomId = 202, Revision = 1, IsCasualRoute = casual, ArenaConfigNumber = map,
        NextArena = ordinal, ConfigFingerprint = config.ArcadeRule.ConfigFingerprint, Phase = OnlineArcadeRunPhase.Arena,
        Players = new() { new() { PlayerId = 1, Lives = 3, Ready = true, WeaponId = "arcade.weapon.basic" },
            new() { PlayerId = 2, Lives = 3, Ready = true, WeaponId = "arcade.weapon.basic" } } };
    var a = new OnlineArcadeRunActor { Record = record, Battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false) };
    a.Battle.WaveIndex = 2; a.Battle.SpawnQueue.Clear(); a.Battle.Enemies.Clear(); a.Battle.SpawnQueueIndex = 0;
    return a;
}
void Tick(OnlineArcadeRunActor a, float seconds = 0) => Call("TickTreasureRoom", a, a.Battle, seconds);
void Collect(OnlineArcadeRunActor a, long id, int reward)
{
    var r = a.Battle.TreasureRoom.Parameters.Rewards.Single(r => r.Id == reward);
    var p = a.Battle.Players[id]; p.PositionX = r.X; p.PositionZ = r.Y; Tick(a);
}
void Leave(OnlineArcadeRunActor a, long id)
{ var p = a.Battle.Players[id]; p.PositionX = 0; p.PositionZ = -5.5f; Tick(a); }
var tests = new (string Name, Action Test)[] {
 ("SecondArenaBothRoutesHaveSixWeapons", () => { foreach (int map in new[]{2,3}) { var a=Actor(map); Tick(a); Require(a.Battle.TreasureRoom.Parameters.IsArmory && a.Battle.TreasureRoom.RemainingRewards.Length==6 && a.Battle.Players.Values.All(p=>p.InTreasureRoom), "军械库缺失或队员未进入"); } }),
 ("UnclearedWaveCannotEnter", () => { var a=Actor(); a.Battle.SpawnQueue.Add("pending"); Tick(a); Require(!a.Battle.TreasureRoom.Started && a.Battle.Players.Values.All(p=>!p.InTreasureRoom), "未清场进入"); }),
 ("WeaponIsSharedAndCannotBeClaimedTwice", () => { var a=Actor(); Tick(a); Collect(a,1,1); Collect(a,2,1); Require(a.Battle.Players[1].TemporaryWeaponId=="arcade.weapon.rapid" && string.IsNullOrEmpty(a.Battle.Players[2].TemporaryWeaponId) && a.Battle.TreasureRoom.RemainingRewards.Length==5, "共享奖励重复领取"); }),
 ("SameFrameClaimUsesStablePlayerOrder", () => { var a=Actor(); Tick(a); var r=a.Battle.TreasureRoom.Parameters.Rewards[0]; foreach(var p in a.Battle.Players.Values){p.PositionX=r.X;p.PositionZ=r.Y;} Tick(a); Require(a.Battle.Players[1].TemporaryWeaponId=="arcade.weapon.rapid" && string.IsNullOrEmpty(a.Battle.Players[2].TemporaryWeaponId), "同帧仲裁不稳定"); }),
 ("ReplacingWeaponUsesConfiguredDurationAndDoesNotAwardScore", () => { var a=Actor(); Tick(a); Collect(a,1,1); Collect(a,1,2); Require(a.Battle.Players[1].TemporaryWeaponId=="arcade.weapon.spread" && a.Battle.Players[1].TemporaryWeaponSeconds==20 && a.Record.Players[0].Score==0 && a.Record.Players[0].WeaponId=="arcade.weapon.basic", "更换或分数异常"); }),
 ("WeaponClockWaitsForRoomAndLastTeammate", () => { var a=Actor(); Tick(a); Collect(a,1,1); Call("TickPlayers",a,a.Battle,5f,0L); Require(a.Battle.Players[1].TemporaryWeaponSeconds==25,"房内消耗武器"); Leave(a,1); Call("TickPlayers",a,a.Battle,5f,0L); Require(a.Battle.Players[1].TemporaryWeaponSeconds==25 && !a.Battle.RoundTransitionPending,"未等队友"); Leave(a,2); Require(a.Battle.RoundTransitionPending,"未接入轮级加载事务"); }),
 ("GroundExpiresThirtyRoomExpiresSixty", () => { var a=Actor(); Tick(a); Tick(a,29); Require(a.Battle.TreasureRoom.RemainingRewards.Length==6,"过早消失"); Tick(a,1); Require(a.Battle.TreasureRoom.RemainingRewards.Length==0 && !a.Battle.TreasureRoom.Expired,"物资和房间时限混淆"); Tick(a,30); Require(a.Battle.Players.Values.All(p=>!p.InTreasureRoom) && a.Battle.RoundTransitionPending,"超时未返回"); }),
 ("FourthRoundRestoreKeepsWeaponAndDoesNotReopenArmory", () => { var a=Actor(); Tick(a); Collect(a,1,1); typeof(OnlineArcadeRunManagerSystem).GetMethod("CaptureRoundCheckpoint",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{a.Record,a.Battle}); a.Record.RoundNumber=4; var layout=OnlineArcadeRunActorSystem.ResolveRoundLayout(a.Battle.ArenaConfigNumber,4); a.Record.RoundLayoutId=layout.Id; a.Record.RoundLayoutVersion=layout.Version; a.Battle=OnlineArcadeRunActorSystem.PrepareBattle(a.Record,false); Tick(a); Require(a.Battle.TreasureRoom==null && a.Battle.Players[1].TemporaryWeaponSeconds==25,"下一轮重发奖励或丢失武器"); Call("TickPlayers",a,a.Battle,1f,0L); Require(a.Battle.Players[1].TemporaryWeaponSeconds==25,"首敌生成前提前消耗"); Call("TrySpawnEnemy",a.Battle,TimeInfo.Instance.ServerNow()+100000L); Require(a.Battle.SpawnedInWave>0,"正式敌人生成失败"); Call("TickPlayers",a,a.Battle,1f,0L); Require(a.Battle.Players[1].TemporaryWeaponSeconds==24,"开战后未恢复消耗"); }),
 ("FirstArenaTreasureRemainsUnchanged", () => { var a=Actor(1,1); Tick(a); Require(!a.Battle.TreasureRoom.Parameters.IsArmory && a.Battle.TreasureRoom.RemainingRewards.Length==18,"第一场宝藏被覆盖"); }),
 ("OldSequentialRunDoesNotGainArmory", () => { var a=Actor(2,2,false); Tick(a); Require(a.Battle.TreasureRoom==null,"旧局规则扩大"); })
};
if(tests.Length>10)throw new InvalidOperationException("超出范围");
int failures=0;
foreach(var(name,test) in tests) try { test(); Console.WriteLine("PASS ArcadeArmoryChecks."+name); }
catch(Exception ex) { failures++; Console.WriteLine("FAIL ArcadeArmoryChecks."+name+": "+(ex.InnerException??ex)); }
Console.WriteLine($"{tests.Length-failures}/{tests.Length} passed"); Environment.ExitCode=failures==0?0:1;
