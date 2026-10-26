using System.Reflection;
using ET;
using ET.Server;
using MemoryPack;

// 此工具只运行明确选择的四项真实服务端入口检查；无效参数拒绝，不回退整套。
var options=args.Select(a=>a.Split('=',2)).ToDictionary(a=>a[0],a=>a.Length==2?a[1]:"");
if(options.GetValueOrDefault("testClass")!="TenStageCreatureOnline"||options.GetValueOrDefault("maxTests")!="4"||options.GetValueOrDefault("includePassingTests")!="true")
{Console.Error.WriteLine("Requires testClass=TenStageCreatureOnline maxTests=4 includePassingTests=true");return 2;}
World.Instance.AddSingleton(ServerConfig.CreateForValidation("OnlineLocal",n=>File.ReadAllBytes(Path.Combine("Server/GameServer/Config/Luban",n+".bytes"))));
World.Instance.AddSingleton<TimeInfo>();World.Instance.AddSingleton<ObjectPool>();
int failures=0;
foreach(var(name,check)in new(string,Action)[]{("LockedCreatureActionsKeepDirection",Locked),("SpiderAndCannonUseAuthorityTarget",Idle),("CloneKeepsIndependentAuthority",Clone),("CodecPreservesFacingAndAge",Codec)})
try{check();Console.WriteLine("PASS TenStageCreatureOnline."+name);}catch(Exception e){failures++;Console.WriteLine("FAIL TenStageCreatureOnline."+name+": "+(e.InnerException??e));}
Console.WriteLine($"{4-failures}/4 passed");return failures==0?0:1;

(OnlineArcadeRunActor,OnlineArcadeEnemyRuntime) Fixture()
{
    var rule=ServerConfig.Instance.ArcadeRule;
    var record=new OnlineArcadeRunRecord{RunId=9925,RoomId=2,Revision=1,NextArena=7,ArenaConfigNumber=5,RoundNumber=1,IsCasualRoute=true,CasualRouteVersion=1,ConfigFingerprint=rule.ConfigFingerprint,
        Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
    var a=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};
    foreach(var p in a.Battle.Players.Values){p.ReadyRoundGeneration=a.Battle.RoundGeneration;p.CurrentHealth=p.MaxHealth=100;}
    var e=new OnlineArcadeEnemyRuntime{RuntimeId=77,EnemyConfigId="enemy.arcade.basic",CurrentHealth=1000,MaxHealth=1000,PositionX=3,PositionZ=-2,SpecialKind=0,ContentBossKind=4,BossPhase=OnlineArcadeBossPhase.Telegraph,BossChargeDirectionX=.6f,BossChargeDirectionZ=.8f,TargetPlayerId=2,ContentElapsed=3.4f,ContentLockedX=.6f,ContentLockedZ=.8f};
    a.Battle.Enemies.Add(77,e);return(a,e);
}
void Locked()
{
    var(a,e)=Fixture();a.Battle.Players[2].PositionX=-10;a.Battle.Players[2].PositionZ=12;
    foreach(var kind in new[]{10,2})
    {
        e.ContentBossKind=kind==2?2:0;e.SpecialKind=kind==2?0:kind;e.ContentElapsed=a.Battle.RoundLayout.SkillWarning+.1;
        var w=((OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle)).Enemies.Single();
        Near(w.ContentFacingX,.6f);Near(w.ContentFacingZ,.8f);Require(w.CurrentHealth==1000,"projection changed authority");
    }
}
void Idle()
{
    var(a,e)=Fixture();e.ContentBossKind=0;e.SpecialKind=6;a.Battle.Players[2].PositionX=7;a.Battle.Players[2].PositionZ=1;
    foreach(int kind in new[]{6,17}){e.SpecialKind=kind;var w=((OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle)).Enemies.Single();Near(w.ContentFacingX,4);Near(w.ContentFacingZ,3);}
    // 食尸鬼真实近战和预警追击仍指向目标，不能被冲进锁向覆盖。
    e.SpecialKind=10;e.MeleeWindupRemaining=.2f;
    var melee=((OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle)).Enemies.Single();Near(melee.ContentFacingX,4);Near(melee.ContentFacingZ,3);
    e.MeleeWindupRemaining=0;e.ContentWarning=true;
    var warning=((OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle)).Enemies.Single();Near(warning.ContentFacingX,4);Near(warning.ContentFacingZ,3);
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
static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,args)!;
static void Near(float a,float b){Require(Math.Abs(a-b)<.00001,$"expected {b}, got {a}");}
static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
