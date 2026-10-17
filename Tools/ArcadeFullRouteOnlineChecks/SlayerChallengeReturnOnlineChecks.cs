using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>挑战经济、独立购物与真实返回文件事务。</summary>
internal static class SlayerChallengeReturnOnlineChecks
{
    static object Call(Type t,string name,params object[] args){var m=t.GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);Require(m!=null,"missing Slayer return hook: "+name);return m!.Invoke(null,args)!;}
    static object Actor(string name,params object[] args)=>Call(typeof(OnlineArcadeRunActorSystem),name,args);
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static OnlineArcadeRunActor Shop()
    {
        var a=SlayerChallengeOnlineChecks.Actor();SlayerChallengeOnlineChecks.Start(a);Actor("TickSlayerChallenge",a,a.Battle,90f);
        a.Battle.SlayerChallenge.CashCollected=100;Actor("KillEnemy",a,a.Battle,a.Battle.Enemies.Values.Single(),false);return a;
    }
    static void FileTest(Action<OnlineArcadeRunManagerComponent,OnlineArcadeRunActor> test,bool won=true)
    {
        string prefix=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"FirepowerSlayerReturn"));string root=Path.Combine(prefix,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{var a=won?Shop():SlayerChallengeOnlineChecks.Actor();if(!won){SlayerChallengeOnlineChecks.Start(a);a.Battle.SlayerChallenge.CashCollected=100;foreach(long id in a.Battle.SlayerChallenge.Members.ToArray())a.Battle.SlayerChallenge=ArcadeSlayerChallengeRules.SetExitVote(a.Battle.SlayerChallenge,id,true);}typeof(Entity).GetProperty("InstanceId")!.SetValue(a,1L);var m=new OnlineArcadeRunManagerComponent{StorePath=Path.Combine(root,"runs.json")};Call(typeof(OnlineArcadeRunManagerSystem),"CommitWithCandidate",m,a.CommittedRecord,0L);m.Runs.Add(1,a);test(m,a);}
        finally{Require(Path.GetFullPath(root).StartsWith(prefix+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"unsafe root");Directory.Delete(root,true);}
    }
    static bool Commit(OnlineArcadeRunManagerComponent m,OnlineArcadeRunActor a)=>(bool)Call(typeof(OnlineArcadeRunManagerSystem),"TryCommitChallengeCompletion",m,a);
    public static int Run(int maximum)
    {
        World.Instance.AddSingleton<Logger>().Log=new ReturnLogger();
        var tests=new (string,Action)[]{
            ("Victory opens fresh shop generation and credits each original member",()=>{var a=Shop();Require(a.Battle.SlayerMartReturnWave>0&&a.Battle.SlayerMart.Generation==2&&!a.Battle.SlayerMart.Closing&&a.Battle.SlayerMart.Players.All(p=>p.Cash==5100),"victory shop identity or shared cash missing");}),
            ("Challenge shop has no repeat tomb cash boxes",()=>{var a=Shop();Require(a.Battle.SlayerMart.CollectedBoxes.Count==9&&a.Battle.SlayerMart.Players.All(p=>p.Offers.Count==16),"challenge duplicated cash boxes or lost cards");}),
            ("Old shop generation cannot buy in challenge",()=>{var a=Shop();var s=a.Battle.SlayerMart;var result=ArcadeSlayerMartRules.Evaluate(s,new(){PlayerId=1,OperationId="old",ExpectedRevision=s.Revision,Generation=1,RegionInstanceId=s.RegionInstanceId,Action=SlayerMartAction.SetMenuOpen,Value=true},ServerConfig.Instance.SlayerMart.Catalog);Require(!result.Success&&result.Error=="invalid_identity","old generation accepted");}),
            ("Shop close requests challenge return without next ordinary round",()=>{var a=Shop();Actor("CloseSlayerMartRegion",a.Battle);Require(a.Battle.ChallengeCompletionReady&&!a.Battle.RoundTransitionPending&&a.Battle.SlayerChallenge.Phase==ArcadeSlayerChallengePhase.Returning,"shop jumped ordinary round");}),
            ("Guaranteed Slayer coin does not enter RunGold",()=>{var a=SlayerChallengeOnlineChecks.Actor();SlayerChallengeOnlineChecks.Start(a);Actor("TrySpawnEnemy",a.Battle,ET.TimeInfo.Instance.ServerNow()+100000L);var e=a.Battle.Enemies.Values.First();e.PositionX=a.Battle.Players[1].PositionX;e.PositionZ=a.Battle.Players[1].PositionZ;long gold=a.Record.Players[0].RunGold;Actor("KillEnemy",a,a.Battle,e,false);Require(a.Battle.Drops.Count==1,"not guaranteed coin");Actor("TickDrops",a,a.Battle);Require(a.Battle.SlayerChallenge.CashCollected==100&&a.Record.Players[0].RunGold==gold,"coin missing or credited RunGold");}),
            ("Return keeps spent supplies and cash but preserves main life and map",()=>FileTest((m,a)=>{a.Record.Players[0].Bombs=0;Actor("CloseSlayerMartRegion",a.Battle);Require(Commit(m,a),"return failed");Require(a.Battle==null&&a.Record.ArenaConfigNumber==5&&a.Record.NextArena==4&&a.Record.CompletedArena==3&&a.Record.Players[0].Bombs==0&&a.Record.Players[0].Lives==3&&a.Record.SlayerMart.Players[0].Cash==5100&&a.Record.ChallengeAttempted&&string.IsNullOrEmpty(a.Record.ChallengeId),"return changed main identity or rolled back resources");})),
            ("Failed return save retains frozen candidate and retries without duplicate cash",()=>FileTest((m,a)=>{Actor("CloseSlayerMartRegion",a.Battle);string before=File.ReadAllText(m.StorePath);m.RemainingFailSaveCount=1;Require(!Commit(m,a)&&a.Battle!=null&&File.ReadAllText(m.StorePath)==before,"failed save published return");Require(Commit(m,a)&&a.Record.SlayerMart.Players.All(p=>p.Cash==5100),"retry lost or duplicated cash");})),
            ("Abandon retains collected cash without boss bonus or shopping",()=>FileTest((m,a)=>{Require(Commit(m,a)&&a.Record.SlayerMart.Players.All(p=>p.Cash==100)&&a.Record.Players.All(p=>p.Lives==3),"abandon cash or life invalid");},false)),
            ("Challenge next-round life is delivered once in return transaction",()=>FileTest((m,a)=>{var shop=a.Battle.SlayerMart;foreach(var owner in shop.Players){shop.GrantSequence++;shop.PendingGrants.Add(new(){EventId=shop.RegionInstanceId+":"+shop.GrantSequence,PlayerId=owner.PlayerId,Effect=SlayerMartEffect.NextRoundLife,Amount=1});}Actor("CloseSlayerMartRegion",a.Battle);m.RemainingFailSaveCount=1;Require(!Commit(m,a),"save injection failed");Require(Commit(m,a)&&a.Record.Players.All(p=>p.Lives==4),"next-round life missing or duplicated");})),            ("Challenge shop ledger still permits later main visit two",()=>{var a=Shop();Actor("CloseSlayerMartRegion",a.Battle);var next=ArcadeSlayerMartRules.BeginVisit("1",31,false,new long[]{1,2},19,ServerConfig.Instance.SlayerMart,a.Battle.SlayerMart);Require(next!=null&&next.Visit==2&&next.Players[0].Cash==5100,"challenge blocked visit two");})
        };
        if(tests.Length>maximum)throw new ArgumentException("Declared maximum too small");int failed=0;foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS SlayerChallengeReturnOnline."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL SlayerChallengeReturnOnline."+name+": "+(e.InnerException??e).Message);}Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
}
sealed class ReturnLogger : ILog
{
    public void Trace(string message) { }
    public void Warning(string message) { }
    public void Info(string message) { }
    public void Debug(string message) { }
    public void Error(string message) { }
    public void Error(Exception error) { }
    public void Trace(ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler message) { }
    public void Warning(ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler message) { }
    public void Info(ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler message) { }
    public void Debug(ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler message) { }
    public void Error(ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler message) { }
}
