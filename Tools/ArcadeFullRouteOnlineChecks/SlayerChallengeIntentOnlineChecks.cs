using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>独立挑战公共快照与有限退出投票，验证原始成员身份、旧版本和重放。</summary>
internal static class SlayerChallengeIntentOnlineChecks
{
    static object Call(string name,params object[] args){var m=typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public);Require(m!=null,"missing Slayer intent: "+name);return m!.Invoke(null,args)!;}
    static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    static OnlineArcadeRunActor Start(){var a=SlayerChallengeOnlineChecks.Actor();SlayerChallengeOnlineChecks.Start(a);return a;}
    static void Vote(OnlineArcadeRunActor a,long id,bool yes,string op="exit",long revision=1)=>Call("OperateSlayerExit",a,id,op,revision,yes);
    static bool Reject(Action action){try{action();return false;}catch(TargetInvocationException e)when(e.InnerException is RpcException){return true;}}
    static object Property(object x,string key){var p=x.GetType().GetProperty(key);Require(p!=null,"missing snapshot "+key);return p!.GetValue(x)!;}
    public static int Run(int maximum)
    {
        var tests=new (string,Action)[]{
            ("Public snapshot and clone carry independent phase and allowance",()=>{var a=Start();using var s=a.BuildSnapshot();using var clone=OnlineArcadeRunActorSystem.CloneSnapshot(s);Require((int)Property(s.Battle,"SlayerPhase")==2&&(int)Property(clone.Battle,"SlayerDownLimit")==4,"phase clone mismatch");}),
            ("One exit vote does not abandon duo",()=>{var a=Start();Vote(a,1,true);Require(a.Battle.SlayerChallenge.Phase==ArcadeSlayerChallengePhase.Collecting&&a.Battle.SlayerChallenge.ExitVotes.SequenceEqual(new long[]{1}),"single vote ended duo");}),
            ("Unanimous exit returns without victory shopping",()=>{var a=Start();Vote(a,1,true);Vote(a,2,true);Require(a.Battle.ChallengeCompletionReady&&!a.Battle.ChallengeSucceeded&&a.Battle.SlayerChallenge.Outcome==ArcadeSlayerChallengeOutcome.Abandoned&&a.Battle.SlayerMartReturnWave==0,"exit won shop");}),
            ("Exit cancellation leaves combat running",()=>{var a=Start();Vote(a,1,true);Vote(a,1,false,"cancel");Vote(a,2,true);Require(a.Battle.SlayerChallenge.Phase==ArcadeSlayerChallengePhase.Collecting&&!a.Battle.SlayerChallenge.ExitVotes.Contains(1),"cancel failed");}),
            ("Exit replay cannot resurrect cancelled vote",()=>{var a=Start();Vote(a,1,true);Vote(a,1,false,"cancel");Vote(a,1,true);Require(!a.Battle.SlayerChallenge.ExitVotes.Contains(1),"replay changed current vote");}),
            ("Exit payload conflict rejected",()=>{var a=Start();Vote(a,1,true);Require(Reject(()=>Vote(a,1,false)),"conflict accepted");}),
            ("Old revision and nonmember exit rejected",()=>{var a=Start();Require(Reject(()=>Vote(a,1,true,"old",2))&&Reject(()=>Vote(a,99,true)),"invalid exit accepted");}),
            ("Active clock uses elapsed time rather than clamped movement step",()=>{var a=Start();a.TickBattle(a.Battle.LastTickAt+3000);Require(a.Battle.SlayerChallenge.RemainingSeconds==87,"challenge clock extended by long frame");})
        };
        if(tests.Length>maximum)throw new ArgumentException("Declared maximum too small");int failed=0;foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS SlayerChallengeIntentOnline."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL SlayerChallengeIntentOnline."+name+": "+(e.InnerException??e).Message);}Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
}
