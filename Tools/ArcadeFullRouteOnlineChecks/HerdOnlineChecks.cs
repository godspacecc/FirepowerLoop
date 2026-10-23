using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;
using MemoryPack;
internal static class HerdOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks=new (string,Action)[]{("SingleFileAndSpeed",OneRow),("TailClearsBeforeNextBatch",TailClears),("SweepAndSingleDeath",Sweep),("Protections",Protections),("ClearAndLoadingFreeze",Freeze),("FreshRound",FreshRound),("SnapshotCloneAndCodec",Snapshot),("LethalMiddleCowKeepsRow",Lethal)};
        if(maximum<checks.Length)throw new ArgumentException("HerdOnline maximum too small");int failed=0;
        foreach(var(name,test) in checks)try{test();Console.WriteLine("PASS HerdOnline."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL HerdOnline."+name+": "+(e.InnerException??e).Message);}
        Console.WriteLine($"{checks.Length-failed}/{checks.Length} passed");return failed==0?0:1;
    }
    private static OnlineArcadeRunActor Create()
    {
        var rule=ServerConfig.Instance.ArcadeRule;
        var record=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,NextArena=3,ArenaConfigNumber=22,IsCasualRoute=true,ConfigFingerprint=rule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
        var actor=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};
        foreach(var p in actor.Battle.Players.Values){p.PositionX=-26;p.PositionZ=-14;p.CurrentHealth=p.MaxHealth=100;p.ReadyRoundGeneration=actor.Battle.RoundGeneration;}
        return actor;
    }
    private static List<ArcadeHerdState> Cows(OnlineArcadeRunActor a)=>typeof(OnlineArcadeBattleRuntime).GetField("HerdCows")?.GetValue(a.Battle) as List<ArcadeHerdState>??new();
    private static void Tick(OnlineArcadeRunActor a,float seconds)=>Call("TickRoundContent",a,a.Battle,seconds);
    private static void OneRow(){var a=Create();Tick(a,8);Require(Cows(a).Count==5,"missing cow batch");Require(Cows(a).Select(c=>c.X).Distinct().Count()==5&&Cows(a).Select(c=>c.Z).Distinct().Count()==1,"not a single file");Tick(a,.4f);Require(a.Battle.Players.Values.All(p=>p.CurrentHealth==100),"windup hurt player");float start=Cows(a)[0].X;Tick(a,1.4f);Require(Math.Abs(Cows(a)[0].X-start-ServerConfig.Instance.ArcadeRule.PlayerMoveSpeed*1.1f)<.001f,"cow speed is not 1.1 times player speed");}
    private static void Sweep(){var a=Create();Tick(a,8);a.Battle.Enemies[12]=new OnlineArcadeEnemyRuntime{RuntimeId=12,PositionX=-26,PositionZ=-14,CurrentHealth=10,MaxHealth=10};Tick(a,2);Require(a.Battle.Players.Values.All(p=>p.CurrentHealth==75),"two players did not share authoritative sweep");Require(!a.Battle.Enemies.ContainsKey(12)&&a.Battle.EnemyDeaths.Count==1&&a.Battle.Drops.Count==0,"environment death settlement wrong");foreach(var p in a.Battle.Players.Values)p.InvulnerabilitySeconds=0;Tick(a,.01f);Require(a.Battle.EnemyDeaths.Count==1&&a.Battle.Players.Values.All(p=>p.CurrentHealth==75),"duplicate contact");}
    private static void Protections(){for(int mode=1;mode<=3;mode++){var a=Create();Tick(a,8);Require(Cows(a).Count==5,"missing cow batch");var p=a.Battle.Players[1];if(mode==1)p.InvulnerabilitySeconds=1;if(mode==2)p.DashSeconds=1;if(mode==3)p.Powerups[ArcadePowerupBehavior.Shield]=new OnlineArcadePowerupRuntime{Behavior=ArcadePowerupBehavior.Shield,Charges=1,RemainingSeconds=10};Tick(a,2);Require(p.CurrentHealth==100,"protection bypassed");if(mode==3)Require(!p.Powerups.ContainsKey(ArcadePowerupBehavior.Shield),"shield not consumed");}}
    private static void Freeze(){var a=Create();a.Battle.Players[2].ReadyRoundGeneration=0;Tick(a,8);Require(Cows(a).Count==0,"loading advanced herd");a.Battle.Players[2].ReadyRoundGeneration=a.Battle.RoundGeneration;Tick(a,8);Require(Cows(a).Count==5,"ready cow batch missing");a.Battle.AwaitingExit=true;Tick(a,5);Require(Cows(a).All(c=>c.ElapsedSeconds==0),"clear advanced charge");}
    private static void FreshRound(){var a=Create();Tick(a,8);Require(Cows(a).Count==5,"missing cow batch");a.Record.RoundNumber=2;a.Record.RoundGeneration=a.Battle.RoundGeneration;a.Battle=OnlineArcadeRunActorSystem.PrepareBattle(a.Record,false);Require(Cows(a).Count==0,"half charge persisted");}
    /// <summary>双人权威循环也必须让尾牛完整离场，之后才生成反向纵队。</summary>
    private static void TailClears(){var a=Create();Tick(a,8);foreach(var p in a.Battle.Players.Values){p.PositionX=0;p.PositionZ=0;}var old=Cows(a).ToArray();Tick(a,11.8f);Require(Cows(a).Count==0&&old.All(c=>c.X>30.5f),"tail recycled inside map");Tick(a,6.3f);Require(Cows(a).Count==5&&Cows(a).Select((c,i)=>Math.Abs(c.X-33-i*3)<.001f&&c.Z==-14&&c.DirectionX==-1).All(v=>v),"reverse batch is not a single file");}
    private static void Lethal(){var a=Create();Tick(a,8);a.Battle.Players[1].CurrentHealth=1;Tick(a,2);Require(a.Battle.Players[1].RespawnSeconds>0,"lethal contact did not enter actual respawn");Require(Cows(a).Select((c,i)=>Math.Abs(c.X-Cows(a)[0].X+i*3)<.001f&&c.Z==-14).All(v=>v),"lethal contact split single file");}
    private static void Snapshot(){var a=Create();Tick(a,8);using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);var prop=snapshot.GetType().GetProperty("Herds");Require(prop!=null,"herd protocol absent");using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));var items=((System.Collections.IEnumerable)prop.GetValue(decoded)!).Cast<object>().ToArray();Require(items.Length==5,"clone/codec lost herd");var first=items[0];Require((long)first.GetType().GetProperty("MapInstanceId")!.GetValue(first)! ==a.Battle.RoundGeneration,"map identity lost");}
    private static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,args)!;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
