using System.Reflection;
using ET;
using ET.Server;
using MemoryPack;

// 此工具只运行明确选择的四项真实服务端入口检查；无效参数拒绝，不回退整套。
var options=args.Select(a=>a.Split('=',2)).ToDictionary(a=>a[0],a=>a.Length==2?a[1]:"");
if(options.GetValueOrDefault("testClass")!="CreatureOnline"||options.GetValueOrDefault("maxTests")!="4"||options.GetValueOrDefault("includePassingTests")!="true")
{Console.Error.WriteLine("Requires testClass=CreatureOnline maxTests=4 includePassingTests=true");return 2;}
World.Instance.AddSingleton(ServerConfig.CreateForValidation("OnlineLocal",n=>File.ReadAllBytes(Path.Combine("Server/GameServer/Config/Luban",n+".bytes"))));
World.Instance.AddSingleton<TimeInfo>();World.Instance.AddSingleton<ObjectPool>();
int failures=0;
foreach(var(name,check)in new(string,Action)[]{("SnapshotUsesCurrentAuthority",Snapshot),("CloneKeepsIndependentAuthority",Clone),("CodecPreservesFacingAndAge",Codec),("UndergroundStillRejectsRealProjectile",Damage)})
try{check();Console.WriteLine("PASS CreatureOnline."+name);}catch(Exception e){failures++;Console.WriteLine("FAIL CreatureOnline."+name+": "+(e.InnerException??e));}
Console.WriteLine($"{4-failures}/4 passed");return failures==0?0:1;

(OnlineArcadeRunActor,OnlineArcadeEnemyRuntime) Fixture()
{
    var rule=ServerConfig.Instance.ArcadeRule;
    var record=new OnlineArcadeRunRecord{RunId=9925,RoomId=2,Revision=1,NextArena=7,ArenaConfigNumber=5,RoundNumber=1,IsCasualRoute=true,CasualRouteVersion=1,ConfigFingerprint=rule.ConfigFingerprint,
        Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
    var a=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};
    foreach(var p in a.Battle.Players.Values){p.ReadyRoundGeneration=a.Battle.RoundGeneration;p.CurrentHealth=p.MaxHealth=100;}
    var e=new OnlineArcadeEnemyRuntime{RuntimeId=77,EnemyConfigId="enemy.arcade.basic",CurrentHealth=1000,MaxHealth=1000,PositionX=3,PositionZ=-2,SpecialKind=5,TargetPlayerId=2,ContentElapsed=3.4f,ContentLockedX=.6f,ContentLockedZ=.8f};
    a.Battle.Enemies.Add(77,e);return(a,e);
}
void Snapshot()
{
    var(a,e)=Fixture();var wire=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);var w=wire.Enemies.Single();
    Near(w.ContentAge,3.4f);Near(w.ContentFacingX,.6f);Near(w.ContentFacingZ,.8f);Require(w.SpecialKind==5&&w.CurrentHealth==1000,"visual snapshot changed identity or health");
    e.SpecialKind=4;a.Battle.Players[2].PositionX=7;a.Battle.Players[2].PositionZ=1;
    w=((OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle)).Enemies.Single();Near(w.ContentFacingX,4);Near(w.ContentFacingZ,3);
}
void Clone()
{
    var(a,e)=Fixture();var wire=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",wire);
    wire.Enemies[0].ContentAge=7;wire.Enemies[0].ContentFacingX=-1;var w=clone.Enemies.Single();Near(w.ContentAge,3.4f);Near(w.ContentFacingX,.6f);Near(w.ContentFacingZ,.8f);
}
void Codec()
{
    var(a,e)=Fixture();var wire=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);
    var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(wire));var w=decoded.Enemies.Single();Near(w.ContentAge,3.4f);Near(w.ContentFacingX,.6f);Near(w.ContentFacingZ,.8f);
}
void Damage()
{
    var(a,e)=Fixture();e.SpecialKind=4;e.ContentElapsed=.55f;
    a.Battle.EnemyIterationIds.Add(77);a.Battle.Projectiles.Add(900,new OnlineArcadeProjectileRuntime{RuntimeId=900,OwnerPlayerId=1,FromPlayerWeapon=true,PositionX=2,PositionZ=-2,VelocityX=1,RemainingSeconds=3,RemainingHits=1,Damage=100});
    Call("TickProjectiles",a,a.Battle,1f);Require(e.CurrentHealth==1000,"underground visual integration changed real immunity");
}
static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,args)!;
static void Near(float a,float b){Require(Math.Abs(a-b)<.00001,$"expected {b}, got {a}");}
static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
