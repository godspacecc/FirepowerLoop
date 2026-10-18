using System.Collections;
using System.Reflection;
using ET;
using ET.Server;

// 只执行炸弹权威表现记录检查，不启动网络、不保存玩家数据。
if (args.Length != 3 || args[0] != "testClass=ArcadeBombChecks" || args[1] != "maxTests=5" || args[2] != "includePassingTests=true")
    throw new ArgumentException("必须声明 testClass=ArcadeBombChecks maxTests=5 includePassingTests=true");
var config = ServerConfig.CreateForValidation("OnlineLocal", name => File.ReadAllBytes(Path.Combine("Server/GameServer/Config/Luban", name + ".bytes")));
World.Instance.AddSingleton(config); World.Instance.AddSingleton<TimeInfo>(); World.Instance.AddSingleton<ObjectPool>();
object Call(string name, params object[] args) => typeof(OnlineArcadeRunActorSystem).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
object Read(object value, string name) => value.GetType().GetProperty(name)?.GetValue(value) ?? value.GetType().GetField(name)?.GetValue(value) ?? throw new Exception("缺少 " + name);
IList Receipts(object value) => (IList)Read(value, "BombBlasts");
void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
OnlineArcadeRunActor Actor()
{
    var record = new OnlineArcadeRunRecord { RunId=1, RoomId=2, Revision=1, NextArena=2, ArenaConfigNumber=2,
        Phase=OnlineArcadeRunPhase.SafeCheckpoint, ConfigFingerprint=config.ArcadeRule.ConfigFingerprint,
        Players=new() { new() {PlayerId=1,Lives=3,Bombs=2,Ready=true,WeaponId=config.ArcadeRule.BaseWeaponId},
                        new() {PlayerId=2,Lives=3,Bombs=2,Ready=true,WeaponId=config.ArcadeRule.BaseWeaponId} } };
    var battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false);
    return new OnlineArcadeRunActor {Record=record,Battle=battle};
}
void Bomb(OnlineArcadeRunActor a,long id) => Call("UseBomb",a,a.Battle,a.Battle.Players[id]);
int total=0,failed=0;
void Check(string name, Action test)
{
    if(++total>5)throw new Exception("超出声明数量");
    try{test();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+(e.InnerException??e).Message);}
}
Check("SuccessfulBombRecordsAuthoritativeOrigin",()=>{
    var a=Actor();a.Battle.Players[1].PositionX=3;a.Battle.Players[1].PositionZ=-4;Bomb(a,1);
    Require(a.Record.Players[0].Bombs==1,"消耗规则改变");
    var list=Receipts(Call("BuildBattleSnapshot",a.Battle));Require(list.Count==1,"成功使用缺少记录");
    Require((float)Read(list[0]!,"PositionX")==3&&(float)Read(list[0]!,"PositionZ")==-4,"爆心不是使用位置");
});
Check("RejectedBombHasNoReceipt",()=>{
    var a=Actor();a.Record.Players[0].Bombs=0;Bomb(a,1);
    Require(Receipts(Call("BuildBattleSnapshot",a.Battle)).Count==0,"空库存产生爆炸");
    a.Record.Players[0].Bombs=2;a.Battle.AwaitingExit=true;Bomb(a,1);
    Require(a.Record.Players[0].Bombs==2&&Receipts(Call("BuildBattleSnapshot",a.Battle)).Count==0,"出口阶段改变规则");
});
Check("TwoPlayersAndRapidBombsKeepDistinctReceipts",()=>{
    var a=Actor();Bomb(a,1);Bomb(a,2);Bomb(a,1);
    var list=Receipts(Call("BuildBattleSnapshot",a.Battle));Require(list.Count==3,"同帧使用被合并");
    Require(list.Cast<object>().Select(x=>(long)Read(x,"Sequence")).Distinct().Count()==3,"重复表现序列");
});
Check("CloneRetainsIndependentReceipt",()=>{
    var a=Actor();Bomb(a,1);var first=Call("BuildBattleSnapshot",a.Battle);var copy=Call("CloneBattleSnapshot",first);
    Receipts(first).Clear();Require(Receipts(copy).Count==1,"快照复制丢失记录");
});
Check("ReceiptHistoryIsBounded",()=>{
    var a=Actor();a.Record.Players[0].Bombs=100;for(int i=0;i<100;i++)Bomb(a,1);
    var list=Receipts(Call("BuildBattleSnapshot",a.Battle));Require(list.Count>0&&list.Count<=32,"历史无限增长");
    Require((long)Read(list[list.Count-1]!,"Sequence")==100,"最新记录丢失");
});
Console.WriteLine($"ArcadeBombChecks: {total-failed}/{total} passed");return failed==0&&total==5?0:1;
