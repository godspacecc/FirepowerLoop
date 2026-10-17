using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>真实临时文件验证准备选择与进入尝试位，失败不发布半份挑战状态。</summary>
internal static class SlayerChallengeTransitionOnlineChecks
{
    static object Call(string method,params object[] args)
    {var m=typeof(OnlineArcadeRunManagerSystem).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public);Require(m!=null,"missing Slayer preparation transaction");return m!.Invoke(null,args)!;}
    static void Require(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
    static ArcadeSlayerChallengeState State(OnlineArcadeRunActor a)=>SlayerChallengeOnlineChecks.State(a.Battle);
    static void Vote(OnlineArcadeRunManagerComponent m,OnlineArcadeRunActor a,long member,bool enter,string operation,long revision=0)
        =>Call("OperateSlayerPreparation",m,a,member,operation,revision==0?a.Record.Revision:revision,enter?7:3,true);
    static bool Rejected(Action action){try{action();return false;}catch(TargetInvocationException e)when(e.InnerException is RpcException){return true;}}
    static void Files(Action<OnlineArcadeRunManagerComponent,OnlineArcadeRunActor> test)
    {
        string prefix=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"FirepowerSlayerEntry"));string root=Path.Combine(prefix,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var a=SlayerChallengeOnlineChecks.Actor();typeof(Entity).GetProperty("InstanceId")!.SetValue(a,1L);var m=new OnlineArcadeRunManagerComponent{StorePath=Path.Combine(root,"runs.json")};
            Call("CommitWithCandidate",m,a.Record,0L);m.Runs.Add(1,a);m.RunIdsByPlayer[1]=m.RunIdsByPlayer[2]=1;test(m,a);
        }
        finally{Require(Path.GetFullPath(root).StartsWith(prefix+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"unsafe test root");Directory.Delete(root,true);}
    }
    public static int Run(int maximum)
    {
        var tests=new (string,Action<OnlineArcadeRunManagerComponent,OnlineArcadeRunActor>)[]{
            ("First vote saves choice without consuming attempt",(m,a)=>{Vote(m,a,1,true,"one");Require(!a.Record.ChallengeAttempted&&State(a).Phase==ArcadeSlayerChallengePhase.Preparing&&State(a).EntryChoices[1]==1,"first vote consumed attempt or failed to publish");}),
            ("Both votes persist attempt before publishing collection",(m,a)=>{Vote(m,a,1,true,"one");Vote(m,a,2,true,"two");var saved=System.Text.Json.JsonSerializer.Deserialize<OnlineArcadeRunStoreSnapshot>(File.ReadAllText(m.StorePath))!.Runs.Single();Require(saved.ChallengeAttempted&&a.Record.ChallengeAttempted&&State(a).Phase==ArcadeSlayerChallengePhase.Collecting&&a.Battle.SpawnQueue.Count==30,"entry published without attempt save");}),
            ("Unanimous skip requests safe return without reward",(m,a)=>{Vote(m,a,1,false,"one");Vote(m,a,2,false,"two");Require(a.Record.ChallengeAttempted&&State(a).Outcome==ArcadeSlayerChallengeOutcome.Skipped&&a.Battle.ChallengeCompletionReady&&!a.Battle.ChallengeSucceeded,"skip opened combat or shop");}),
            ("Failed entry save leaves preparation and retry identity intact",(m,a)=>{Vote(m,a,1,true,"one");string before=File.ReadAllText(m.StorePath);long rev=a.Record.Revision;m.RemainingFailSaveCount=1;bool failed=false;try{Vote(m,a,2,true,"two");}catch(TargetInvocationException e)when(e.InnerException is IOException){failed=true;}Require(failed&&before==File.ReadAllText(m.StorePath)&&a.Record.Revision==rev&&!a.Record.ChallengeAttempted&&State(a).Phase==ArcadeSlayerChallengePhase.Preparing,"failed save published partial entry");Vote(m,a,2,true,"two");Require(a.Record.ChallengeAttempted&&State(a).Phase==ArcadeSlayerChallengePhase.Collecting,"same operation failed retry");}),
            ("Stale revision cannot change entry choice",(m,a)=>{Vote(m,a,1,true,"one");Require(Rejected(()=>Vote(m,a,2,true,"old",1))&&!a.Record.ChallengeAttempted,"stale vote applied");}),
            ("Same operation replay never advances choice twice",(m,a)=>{Vote(m,a,1,true,"one",1);long rev=a.Record.Revision;Vote(m,a,1,true,"one",1);Require(a.Record.Revision==rev&&!a.Record.ChallengeAttempted,"replay changed entry");}),
            ("Operation conflict cannot switch enter to skip",(m,a)=>{Vote(m,a,1,true,"one",1);Require(Rejected(()=>Vote(m,a,1,false,"one",1))&&State(a).EntryChoices[1]==1,"conflict overwrote vote");}),
            ("Mainline eliminated member cannot enter free challenge revival",(m,a)=>{a.Record.Players[0].Lives=0;a.Record.Players[0].CoopRescueEligible=false;Require(Rejected(()=>Vote(m,a,1,true,"one"))&&!a.Record.ChallengeAttempted,"eliminated member entered");}),
            ("Opportunity is unique to rule1 after arena3",(m,a)=>{for(int completed=2;completed<=4;completed++){var r=OnlineArcadeRunManagerSystem.Clone(a.Record);r.CompletedArena=completed;r.NextArena=completed+1;r.ChallengeId=string.Empty;Call("ConfigureSlayerOpportunity",r);Require((r.ChallengeId==ServerConfig.Instance.SlayerChallenge.ChallengeId)==(completed==3),"wrong opportunity ordinal");}var old=OnlineArcadeRunManagerSystem.Clone(a.Record);old.SlayerMartRulesVersion=0;old.ChallengeId=string.Empty;Call("ConfigureSlayerOpportunity",old);Require(string.IsNullOrEmpty(old.ChallengeId),"rule0 gained shop challenge");})
        };
        if(tests.Length>maximum)throw new ArgumentException("Declared maximum too small");int failed=0;
        foreach(var(name,test)in tests)try{Files((m,a)=>test(m,a));Console.WriteLine("PASS SlayerChallengeTransitionOnline."+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerChallengeTransitionOnline."+name+": "+(ex.InnerException??ex).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
}
