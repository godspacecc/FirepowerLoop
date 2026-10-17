using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>真实权威Actor的个人交易、私有报价与区域同步，范围固定且不启动全套。</summary>
internal static class SlayerMartOnlineChecks
{
    private static ArcadeSlayerMartConfiguration Config=>ServerConfig.Instance.SlayerMart;
    public static int Run(int max)
    {
        var tests=new (string,Action<OnlineArcadeRunActor>)[]{
            ("One cash box grants equal private shares only once",a=>{P(a,1).Cash=P(a,2).Cash=0;Require(Op(a,Q(a,1,SlayerMartAction.CollectCash,box:1)).Success);Require(!Op(a,Q(a,2,SlayerMartAction.CollectCash,box:1)).Success);Require(P(a,1).Cash==2000&&P(a,2).Cash==2000);}),
            ("Accepted purchase replay never charges or grants twice",a=>{var q=Q(a,1,SlayerMartAction.Buy,1,2,operation:"buy");Require(Op(a,q).Success);a.Record.Players[0].Bombs=1;a.Battle.SlayerMart.Closing=true;a.Battle.SlayerMart.RemainingSeconds=0;a.Battle.SlayerMartReturnWave=0;var again=Op(a,q);Require(again.Success&&again.Replayed&&P(a,1).Cash==97000&&a.Record.Players[0].Bombs==1);}),
            ("Same operation different payload cannot replay",a=>{var q=Q(a,1,SlayerMartAction.Buy,1,2,operation:"buy");Require(Op(a,q).Success);q.ItemId="slayermart.item.02";Require(!Op(a,q).Success&&P(a,1).Cash==97000);}),
            ("Concurrent team benefit charges only the first member",a=>{Require(Op(a,Q(a,1,SlayerMartAction.Buy,24)).Success);Require(!Op(a,Q(a,2,SlayerMartAction.Buy,24)).Success);Require(P(a,1).Cash==92500&&P(a,2).Cash==100000&&a.Battle.SlayerMart.PendingGrants.Count==2);}),
            ("Personal reroll never changes teammate offers or tokens",a=>{var before=P(a,2).Offers.ToArray();Require(Op(a,Q(a,1,SlayerMartAction.Reroll)).Success);Require(P(a,1).Tokens==1&&P(a,2).Tokens==2&&P(a,2).Offers.SequenceEqual(before));}),
            ("Foreign region identity is rejected",a=>{var q=Q(a,1,SlayerMartAction.Reroll);q.RegionInstanceId="old:slayermart:1";Require(!Op(a,q).Success);}),
            ("Old generation cannot mutate current store",a=>{var q=Q(a,1,SlayerMartAction.Reroll);q.Generation++;Require(!Op(a,q).Success&&P(a,1).Tokens==2);}),
            ("Authenticated member cannot operate teammate inventory",a=>{var q=Q(a,2,SlayerMartAction.Reroll);Require(!Op(a,q,1).Success&&P(a,2).Tokens==2);}),
            ("Disconnect rejects new transactions",a=>{a.DisconnectedAtByPlayer[2]=1;Require(!Op(a,Q(a,1,SlayerMartAction.Reroll)).Success&&P(a,1).Tokens==2);}),
            ("Dead member cannot buy personal supplies",a=>{a.Record.Players[0].Lives=0;Require(!Op(a,Q(a,1,SlayerMartAction.Buy,1,2)).Success&&P(a,1).Cash==100000);}),
            ("Confirmed actual bomb amount clips capacity before charge",a=>{a.Record.Players[0].Bombs=8;Require(!Op(a,Q(a,1,SlayerMartAction.Buy,1,2)).Success);Require(Op(a,Q(a,1,SlayerMartAction.Buy,1,1)).Success&&a.Record.Players[0].Bombs==9&&P(a,1).Cash==97000);}),
            ("Chicken pack overflow rejects entire payment",a=>{var s=a.Battle.SlayerMart;s.GrantSequence++;s.PendingGrants.Add(new(){EventId=s.RegionInstanceId+":"+s.GrantSequence,PlayerId=1,Effect=SlayerMartEffect.ChickenPack,Amount=3,Duration=300});Require(ArcadeSlayerMartSupportRules.TryApplyGrants(s,Config,new Dictionary<long,int>(),out var candidate,out _));a.Battle.SlayerMart=candidate;Require(!Op(a,Q(a,1,SlayerMartAction.Buy,20)).Success&&P(a,1).Cash==100000&&P(a,1).Supports.Count==3);}),
            ("Gem overflow cannot charge cash or affect ally",a=>{a.Record.Players[0].Score=long.MaxValue-1;Require(!Op(a,Q(a,1,SlayerMartAction.Buy,17)).Success&&P(a,1).Cash==100000&&a.Record.Players[1].Score==0&&a.Battle.SlayerMart.PendingGrants.Count==0);}),
            ("Gems affect only buyer and replay is stable",a=>{var q=Q(a,1,SlayerMartAction.Buy,17,operation:"gems");Require(Op(a,q).Success);long score=a.Record.Players[0].Score;Require(score>0&&a.Record.Players[1].Score==0&&Op(a,q).Replayed&&a.Record.Players[0].Score==score);}),
            ("An open menu cannot mark member finished",a=>{P(a,1).MenuOpen=true;Require(!Op(a,Q(a,1,SlayerMartAction.SetFinished,value:true)).Success&&!P(a,1).Finished);}),
            ("Private quotes contain only requested member economy",a=>{P(a,2).Cash=77777;var own=Private(a,1);Require(own.PlayerId==1&&own.Cash==100000&&own.Offers.Count==16&&own.Members.Count==2);Require(Private(a,2).Cash==77777);}),
            ("Public broadcast never contains a private quote",a=>{Require(Private(a,1)!=null);Require(a.BuildSnapshot().SelfSlayerMart==null);}),
            ("Foreign player receives no private quote",a=>Require(Private(a,999)==null)),
            ("Conflicting choices keep full unstarted deadline",a=>{Choosing(a);P(a,1).Choice=1;P(a,2).Choice=2;Step(a,100);Require(!a.Battle.SlayerMart.Ready&&!a.Battle.SlayerMart.Closing&&a.Battle.SlayerMart.RemainingSeconds==300);}),
            ("Both enter choices require fresh all-member map readiness",a=>{Choosing(a);P(a,1).Choice=P(a,2).Choice=1;int generation=a.Battle.RoundGeneration;Step(a,0);Require(a.Battle.RoundGeneration==generation+1&&!a.Battle.SlayerMart.Ready&&a.Battle.Players.Values.All(p=>p.ReadyRoundGeneration!=a.Battle.RoundGeneration));}),
            ("Last map ready starts full deadline without charging load time",a=>{Choosing(a);P(a,1).Choice=P(a,2).Choice=1;Step(a,0);a.Battle.Players[1].ReadyRoundGeneration=a.Battle.RoundGeneration;Step(a,100);Require(!a.Battle.SlayerMart.Ready);a.Battle.Players[2].ReadyRoundGeneration=a.Battle.RoundGeneration;Step(a,100);Require(a.Battle.SlayerMart.Ready&&a.Battle.SlayerMart.RemainingSeconds==300);}),
            ("Shopping menu never pauses hard deadline",a=>{P(a,1).MenuOpen=true;Step(a,10);Require(a.Battle.SlayerMart.RemainingSeconds==290);}),
            ("Hard deadline closes menus and requests next ordinary round",a=>{P(a,1).MenuOpen=true;a.Battle.SlayerMart.RemainingSeconds=.01f;var accepted=Q(a,1,SlayerMartAction.Buy,1,2,operation:"last");Require(Op(a,accepted).Success);Step(a,1);Require(a.Battle.SlayerMart.Closing&&!P(a,1).MenuOpen&&a.Battle.RoundTransitionPending&&Op(a,accepted).Replayed&&!Op(a,Q(a,1,SlayerMartAction.Reroll)).Success);}),
            ("Disconnect freezes shopping deadline",a=>{a.DisconnectedAtByPlayer[2]=1;Step(a,100);Require(a.Battle.SlayerMart.RemainingSeconds==300);})
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)try{var a=Actor();test(a);Console.WriteLine("PASS SlayerMartOnline."+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerMartOnline."+name+": "+(ex.InnerException??ex).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static OnlineArcadeRunActor Actor()
    {
        var s=ArcadeSlayerMartRules.Create("1",1,new long[]{1,2},17,Config.Catalog);s.Ready=true;
        foreach(var p in s.Players){p.Lives=3;p.Cash=100000;p.Tokens=2;p.Choice=1;foreach(int n in new[]{1,2,17,20,24}){string id="slayermart.item."+n.ToString("00");if(!p.Offers.Contains(id)){int index=p.Offers.FindIndex(v=>!new[]{"slayermart.item.01","slayermart.item.02","slayermart.item.17","slayermart.item.20","slayermart.item.24"}.Contains(v));p.Offers[index]=id;}}}
        var r=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,IsCasualRoute=true,ArenaConfigNumber=3,NextArena=3,CompletedArena=2,RoundNumber=3,RoundLayoutId="arena.3.round.3",RoundLayoutVersion=OnlineArcadeRunActorSystem.ResolveRoundLayout(3,3).Version,ConfigFingerprint=ServerConfig.Instance.ArcadeRule.ConfigFingerprint,
            SlayerMartRulesVersion=1,SlayerMart=s,Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId="arcade.weapon.basic"},new(){PlayerId=2,Lives=3,Ready=true,WeaponId="arcade.weapon.basic"}}};
        var b=OnlineArcadeRunActorSystem.PrepareBattle(r,false);b.SlayerMartReturnWave=3;b.SlayerMartRegionPrepared=true;
        var point=Config.Regions[0].Boxes[0];foreach(var p in b.Players.Values){p.PositionX=point.X;p.PositionZ=point.Y;p.ReadyRoundGeneration=b.RoundGeneration;}
        return new(){Record=r,Battle=b};
    }
    private static ArcadeSlayerMartPlayer P(OnlineArcadeRunActor a,long player)=>a.Battle.SlayerMart.Players.Single(p=>p.PlayerId==player);
    private static ArcadeSlayerMartRequest Q(OnlineArcadeRunActor a,long player,SlayerMartAction action,int item=0,int amount=0,int box=0,bool value=false,string operation=null)
    {var s=a.Battle.SlayerMart;return new(){PlayerId=player,Action=action,ItemId=item==0?null:"slayermart.item."+item.ToString("00"),ConfirmedAmount=amount,BoxId=box,Value=value,RegionInstanceId=s.RegionInstanceId,Generation=s.Generation,ExpectedRevision=s.Revision,OperationId=operation??Guid.NewGuid().ToString("N")};}
    private static ArcadeSlayerMartResult Op(OnlineArcadeRunActor a,ArcadeSlayerMartRequest q,long caller=0)=>(ArcadeSlayerMartResult)Call("OperateSlayerMart",a,caller==0?q.PlayerId:caller,q);
    private static OnlineSlayerMartSnapshotInfo Private(OnlineArcadeRunActor a,long player)=>(OnlineSlayerMartSnapshotInfo)Call("BuildPersonalSlayerMart",a,player);
    private static void Choosing(OnlineArcadeRunActor a){a.Battle.SlayerMart.Ready=false;a.Battle.SlayerMartRegionPrepared=false;foreach(var p in a.Battle.SlayerMart.Players)p.Choice=0;}
    private static void Step(OnlineArcadeRunActor a,float seconds)=>Call("TickSlayerMartRegion",a,a.Battle,seconds);
    private static object Call(string name,params object[] args)=>(typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)??throw new InvalidOperationException("Missing authoritative store entry: "+name)).Invoke(null,args);
    private static void Require(bool ok){if(!ok)throw new InvalidOperationException("Authoritative store contract violated");}
}
