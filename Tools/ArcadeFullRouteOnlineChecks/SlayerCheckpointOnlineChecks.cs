using System.Reflection;
using System.Text.Json;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>正式服务器保存、克隆和战场捕获；独立临时目录，不监听端口。</summary>
internal static class SlayerCheckpointOnlineChecks
{
    private static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunManagerSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,args)!;
    public static int Run(int max)
    {
        var tests=new (string,Action)[]{
            ("Record clone owns its complete shop ledger",()=>{var r=Record(100);Call("ValidateStore",new OnlineArcadeRunStoreSnapshot{Runs=new(){r}});var c=OnlineArcadeRunManagerSystem.Clone(r);r.SlayerMart.Players[0].Cash=0;r.SlayerMart.Receipts.Clear();Require(c.SlayerMart.Players[0].Cash==15000&&c.SlayerMart.Receipts.Count==1,"clone alias");}),
            ("Unknown shop rules version is rejected",()=>{var r=Record(100);r.SlayerMartRulesVersion=2;Require(Rejected(r),"unknown version loaded");}),
            ("Foreign beneficiary and open shop refuse persistence",()=>{var r=Record(100);r.SlayerMart.Players[1].PlayerId=999;Require(Rejected(r),"foreign member saved");r=Record(100);r.SlayerMart.Closing=false;Require(Rejected(r),"midshop persisted");}),
            ("Old record cannot gain new economy",()=>{var r=Record(100);r.SlayerMartRulesVersion=0;Require(Rejected(r),"old record gained shop");}),
            ("Prepared battle cannot mutate saved shop",()=>{var r=Record(100);var b=OnlineArcadeRunActorSystem.PrepareBattle(r,false);Require(b.SlayerMart!=null,"battle missing shop");b.SlayerMart.Players[0].Cash=0;Require(r.SlayerMart.Players[0].Cash==15000,"prepare shares record");}),
            ("Capture clones all members without publishing candidate",()=>{var r=Record(100);var b=OnlineArcadeRunActorSystem.PrepareBattle(r,false);b.SlayerMart.Players[0].Cash=8000;var c=OnlineArcadeRunManagerSystem.Clone(r);Call("CaptureRoundCheckpoint",c,b);b.SlayerMart.Players[0].Cash=0;Require(c.SlayerMart.Players[0].Cash==8000&&r.SlayerMart.Players[0].Cash==15000,"capture alias or premature commit");}),
            ("Another room save uses only committed shop",()=>Files((manager,actor)=>{actor.Record.SlayerMart.Players[0].Cash=1;var other=Record(200);Call("CommitWithCandidate",manager,other,0L);var disk=JsonSerializer.Deserialize<OnlineArcadeRunStoreSnapshot>(File.ReadAllText(manager.StorePath));Require(disk!.Runs.Single(p=>p.RunId==100).SlayerMart.Players[0].Cash==15000,"other room saved working cash");})),
            ("Real write failure preserves committed and working snapshots",()=>Files((manager,actor)=>{var original=File.ReadAllText(manager.StorePath);var c=OnlineArcadeRunManagerSystem.Clone(actor.Record);c.SlayerMart.Closing=false;bool rejected=false;try{Call("CommitWithCandidate",manager,c,c.RunId);}catch{rejected=true;}Require(rejected&&File.ReadAllText(manager.StorePath)==original,"open shop overwrote shared store");c=OnlineArcadeRunManagerSystem.Clone(actor.Record);c.SlayerMart.Players[0].Cash=4000;bool failed=false;using(var locked=new FileStream(manager.StorePath,FileMode.Open,FileAccess.Read,FileShare.None)){try{Call("CommitWithCandidate",manager,c,c.RunId);}catch{failed=true;}}Require(failed&&File.ReadAllText(manager.StorePath)==original&&actor.CommittedRecord.SlayerMart.Players[0].Cash==15000&&actor.Record.SlayerMart.Players[0].Cash==15000,"failed save changed ledger");}))
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS SlayerCheckpointOnline."+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerCheckpointOnline."+name+": "+(ex.InnerException??ex).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static OnlineArcadeRunRecord Record(long run)
    {
        long first=run*10+1,second=run*10+2;
        return new(){RunId=run,RoomId=run+1000,Revision=3,Phase=OnlineArcadeRunPhase.SafeCheckpoint,IsCasualRoute=true,CompletedArena=2,NextArena=3,ArenaConfigNumber=4,
            RoundNumber=4,RoundLayoutId="arena.4.round.4",RoundLayoutVersion=1,RoundGeneration=1,ConfigFingerprint=ServerConfig.Instance.ArcadeRule.ConfigFingerprint,
            CreatedAtUtc=DateTime.UtcNow.ToString("O"),UpdatedAtUtc=DateTime.UtcNow.ToString("O"),SlayerMartRulesVersion=1,
            Players=new(){new(){PlayerId=first,Lives=3,Ready=true,WeaponId="arcade.weapon.basic"},new(){PlayerId=second,Lives=3,Ready=true,WeaponId="arcade.weapon.basic"}},
            SlayerMart=new(){RegionInstanceId=run+":slayermart:1",Visit=1,Generation=1,Revision=2,Ready=true,Closing=true,RemainingSeconds=200,
                Players=new(){Player(first),Player(second)},Receipts=new(){new(){PlayerId=first,OperationId="buy",Fingerprint="bomb"}}}};
    }
    private static ArcadeSlayerMartPlayer Player(long id)=>new(){PlayerId=id,Cash=15000,Lives=3,Offers=Enumerable.Range(1,16).Select(n=>"slayermart.item."+n.ToString("00")).ToList()};
    private static bool Rejected(OnlineArcadeRunRecord record){try{Call("ValidateStore",new OnlineArcadeRunStoreSnapshot{Runs=new(){record}});return false;}catch(TargetInvocationException e)when(e.InnerException is InvalidOperationException){return true;}}
    private static void Files(Action<OnlineArcadeRunManagerComponent,OnlineArcadeRunActor> body)
    {
        string root=Path.Combine(Path.GetTempPath(),"FirepowerLoopSlayerCheckpoint",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var manager=new OnlineArcadeRunManagerComponent{StorePath=Path.Combine(root,"runs.json")};var record=Record(100);
            var actor=new OnlineArcadeRunActor{Record=OnlineArcadeRunManagerSystem.Clone(record),CommittedRecord=OnlineArcadeRunManagerSystem.Clone(record)};
            typeof(Entity).GetProperty("InstanceId")!.SetValue(actor,record.RunId);Call("CommitWithCandidate",manager,record,0L);manager.Runs.Add(record.RunId,actor);
            body(manager,actor);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}
