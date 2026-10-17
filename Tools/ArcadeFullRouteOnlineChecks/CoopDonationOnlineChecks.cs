using System.Reflection;
using ET;
using ET.Server;

/// <summary>捐命权威资格、请求幂等及轮节点一致性的独立范围。</summary>
internal static class CoopDonationOnlineChecks
{
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static OnlineArcadeRunActor Downed(){var a=CoopRescueOnlineChecks.Actor();CoopRescueOnlineChecks.Kill(a);return a;}
    static bool Donate(OnlineArcadeRunActor a,long operation=1,long target=1,long run=1,int generation=-1)
    {
        var method=typeof(OnlineArcadeRunActorSystem).GetMethod("DonateCoopLife",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public);
        Require(method!=null,"missing authoritative donation transaction");
        return (bool)method!.Invoke(null,new object[]{a,2L,run,generation<0?a.Battle.RoundGeneration:generation,target,operation})!;
    }
    static bool Rejects(Action action){try{action();return false;}catch(TargetInvocationException e)when(e.InnerException is RpcException){return true;}}
    public static int Run(int maximum)
    {
        var tests=new (string,Action)[]{
            ("One life immediately restores downed teammate",()=>{var a=Downed();Require(Donate(a),"donation rejected");Require(a.Record.Players[1].Lives==2&&a.Record.Players[0].Lives==0&&a.Battle.Players[1].Alive&&a.Battle.Players[1].CurrentHealth==100&&a.Battle.Players[1].InvulnerabilitySeconds>0,"non-atomic cost or invalid recovery");}),
            ("Last donor life is preserved",()=>{var a=Downed();a.Record.Players[1].Lives=1;Require(!Donate(a)&&a.Record.Players[1].Lives==1&&!a.Battle.Players[1].Alive,"last life consumed");}),
            ("Dead donor cannot donate",()=>{var a=Downed();a.Battle.Players[2].Alive=false;Require(!Donate(a)&&a.Record.Players[1].Lives==3,"dead donor charged");}),
            ("Self donation is rejected",()=>{var a=Downed();Require(!Donate(a,target:2)&&a.Record.Players[1].Lives==3,"self donation charged");}),
            ("Active target cannot receive another donation",()=>{var a=Downed();Require(Donate(a)&&!Donate(a,2)&&a.Record.Players[1].Lives==2,"active target charged twice");}),
            ("Identical operation returns original success without double cost",()=>{var a=Downed();Require(Donate(a)&&Donate(a)&&a.Record.Players[1].Lives==2,"duplicate charged again");}),
            ("Operation identity cannot change target",()=>{var a=Downed();Donate(a);Require(Rejects(()=>Donate(a,target:2))&&a.Record.Players[1].Lives==2,"conflict accepted");}),
            ("Replay after next death cannot rescue again",()=>{var a=Downed();Donate(a);a.Battle.Players[1].InvulnerabilitySeconds=0;CoopRescueOnlineChecks.Kill(a);Require(Donate(a)&&!a.Battle.Players[1].Alive&&a.Record.Players[1].Lives==2,"old receipt rescued next death");}),
            ("Old run is rejected before consumption",()=>{var a=Downed();Require(Rejects(()=>Donate(a,run:99))&&a.Record.Players[1].Lives==3,"old run consumed life");}),
            ("Old map generation is rejected",()=>{var a=Downed();Require(Rejects(()=>Donate(a,generation:0))&&a.Record.Players[1].Lives==3,"old generation consumed life");}),
            ("Disconnected team cannot donate",()=>{var a=Downed();a.DisconnectedAtByPlayer[1]=1;Require(!Donate(a)&&a.Record.Players[1].Lives==3,"disconnect donation");}),
            ("Shop and challenge cannot bypass ordinary eligibility",()=>{foreach(bool shop in new[]{true,false}){var a=Downed();if(shop)a.Battle.SlayerMartReturnWave=3;else a.Battle.IsChallenge=true;Require(!Donate(a)&&a.Record.Players[1].Lives==3,"special phase donation");}}),
            ("Treasure region cannot donate across boundary",()=>{var a=Downed();a.Battle.Players[2].InTreasureRoom=true;Require(!Donate(a)&&a.Record.Players[1].Lives==3,"cross-region donation");}),
            ("Unready and transitioning maps reject donation",()=>{foreach(bool loading in new[]{true,false}){var a=Downed();if(loading)a.Battle.Players[2].ReadyRoundGeneration=-1;else a.Battle.RoundTransitionPending=true;Require(!Donate(a)&&a.Record.Players[1].Lives==3,"loading donation");}}),
            ("Round node captures cost and recovery together",()=>{var a=Downed();var before=CoopRescueOnlineChecks.Capture(a);Donate(a);var after=CoopRescueOnlineChecks.Capture(a);var b=OnlineArcadeRunActorSystem.PrepareBattle(after,false);Require(after.Players[1].Lives==2&&b.Players[1].Alive&&before.Players[1].Lives==3&&!before.Players[0].RoundAlive,"checkpoint split cost from recovery");}),
            ("Rejected operation cannot charge after eligibility changes",()=>{var a=Downed();a.Record.Players[1].Lives=1;Require(!Donate(a),"last life accepted");a.Record.Players[1].Lives=3;Require(!Donate(a)&&a.Record.Players[1].Lives==3,"failed operation unexpectedly retried as new donation");}),
            ("Official input consumes donation once",()=>{var a=Downed();Input(a,1);Input(a,1);Require(a.Record.Players[1].Lives==2&&a.Battle.Players[1].Alive,"input missing donation or duplicate cost");}),
            ("Official input rejects different run donation",()=>{var a=Downed();Require(Rejects(()=>Input(a,1,99))&&a.Record.Players[1].Lives==3,"input ignored donation run identity");}),
            ("Outer and inner request shapes carry explicit donation identity",()=>{foreach(var type in new[]{typeof(C2G_SubmitOnlineArcadeInput),typeof(G2P_SubmitOnlineArcadeInput),typeof(P2R_SubmitOnlineArcadeInput)})Require(type.GetProperty("DonationRunId")?.PropertyType==typeof(long)&&type.GetProperty("DonateLifeTargetId")?.PropertyType==typeof(long),"missing generated identity "+type.Name);}),
            ("Downed state is included in authoritative snapshot",()=>{var a=Downed();var method=typeof(OnlineArcadeRunActorSystem).GetMethod("BuildBattleSnapshot",BindingFlags.Static|BindingFlags.NonPublic)!;using var b=(OnlineArcadeBattleInfo)method.Invoke(null,new object[]{a.Battle})!;var p=b.Players.First(p=>p.PlayerId==1);Require(p.GetType().GetProperty("DownedSeconds")?.GetValue(p) is float seconds&&seconds==60,"missing downed protocol projection");})
        };
        if(tests.Length>maximum)throw new ArgumentException("Declared maximum smaller than actual scope");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS CoopDonationOnline."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL CoopDonationOnline."+name+": "+(e.InnerException??e).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }

    static void Input(OnlineArcadeRunActor a,long sequence,long run=1)
    {
        var method=typeof(OnlineArcadeRunActorSystem).GetMethod("SubmitInput")!;
        Require(method.GetParameters().Length==18,"official input has no donation identity");
        method.Invoke(null,new object[]{a,2L,a.Battle.ArenaNumber,sequence,0f,0f,0f,1f,false,false,false,a.Battle.RoundGeneration,true,0,false,false,run,1L});
    }
}
