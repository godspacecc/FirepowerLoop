using System.Reflection;
using System.Text.Json;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>服务器正式轮级提交、帧接线、硬截止和快照深拷贝，仅操作临时文件。</summary>
internal static class SlayerMartRoundOnlineChecks
{
    private static ArcadeSlayerMartConfiguration Config=>ServerConfig.Instance.SlayerMart;
    public static int Run(int max)
    {
        var tests=new (string,Action<OnlineArcadeRunManagerComponent,OnlineArcadeRunActor>)[]{
            ("Public prepared metadata distinguishes selection from crypt loading",(m,a)=>{Open(a);var f=typeof(OnlineArcadeBattleInfo).GetProperty("SlayerMartPrepared")??throw new InvalidOperationException("Missing explicit prepared metadata");a.Battle.SlayerMartRegionPrepared=false;Require(!(bool)f.GetValue(a.BuildSnapshot().Battle));a.Battle.SlayerMartRegionPrepared=true;var source=a.BuildSnapshot();Require((bool)f.GetValue(source.Battle)&&(bool)f.GetValue(OnlineArcadeRunActorSystem.CloneSnapshot(source).Battle));}),
            ("Eleventh exit prepares store without writing twelfth round",(m,a)=>{a.Battle.RoundTransitionPending=true;Require(Commit(m,a)&&a.Battle.SlayerMart?.Visit==1&&a.Battle.WaveIndex==2&&Read(m).RoundNumber==3&&!a.Battle.RoundTransitionPending);}),
            ("Other round retains original round transaction",(m,a)=>{a.Battle.WaveIndex=1;a.Battle.RoundLayout=OnlineArcadeRunActorSystem.ResolveRoundLayout(3,2);a.Record.RoundNumber=2;a.Record.RoundLayoutId=a.Battle.RoundLayout.Id;a.Record.RoundLayoutVersion=a.Battle.RoundLayout.Version;SyncStored(m,a);a.Battle.RoundTransitionPending=true;Require(Commit(m,a)&&a.Battle.SlayerMart==null&&a.Record.RoundNumber==3);}),
            ("Old rules never prepare new store",(m,a)=>{a.Record.SlayerMartRulesVersion=a.Battle.SlayerMartRulesVersion=0;SyncStored(m,a);a.Battle.RoundTransitionPending=true;Require(Commit(m,a)&&a.Battle.SlayerMart==null&&a.Record.RoundNumber==4);}),
            ("Thirty first exit inherits first store without writing next round",(m,a)=>{var old=Closed();old.Players[0].Cash=500;a.Record.NextArena=a.Battle.ArenaNumber=8;a.Record.CompletedArena=7;a.Battle.SlayerMart=old;a.Record.SlayerMart=old.Clone();SyncStored(m,a);a.Battle.RoundTransitionPending=true;Require(Commit(m,a)&&a.Battle.SlayerMart.Visit==2&&a.Battle.SlayerMart.Players[0].Cash==500&&a.Record.RoundNumber==3);}),
            ("Formal Tick freezes combat effects and drops inside store",(m,a)=>{Open(a);var p=a.Battle.Players[1];p.TemporaryWeaponSeconds=10;p.TemporaryWeaponId="arcade.weapon.rapid";a.Battle.Drops[1]=new(){RuntimeId=1,RemainingSeconds=10,DropConfigId=ServerConfig.Instance.Tables.TbArcadeDrop.DataList.First(p=>p.Kind==ArcadeDropKind.RunGold).Id};a.Battle.LastTickAt=TimeInfo.Instance.ServerNow();a.TickBattle(a.Battle.LastTickAt+1000);Require(p.TemporaryWeaponSeconds==10&&a.Battle.Drops[1].RemainingSeconds==10);}),
            ("Shop input cannot consume dash bomb or enable firing",(m,a)=>{Open(a);a.Record.Players[0].Bombs=a.Record.Players[0].Dashes=2;a.SubmitInput(1,3,1,0,0,0,1,true,true,true,a.Battle.RoundGeneration,true);Require(a.Record.Players[0].Bombs==2&&a.Record.Players[0].Dashes==2&&!a.Battle.Players[1].Firing);}),
            ("Formal Tick processes fresh shop readiness before combat gate",(m,a)=>{Open(a);a.Battle.SlayerMart.Ready=false;a.Battle.SlayerMartRegionPrepared=false;foreach(var p in a.Battle.SlayerMart.Players)p.Choice=1;int generation=a.Battle.RoundGeneration;long now=TimeInfo.Instance.ServerNow();a.Battle.LastTickAt=now;a.TickBattle(now+100);Require(a.Battle.RoundGeneration==generation+1&&!a.Battle.SlayerMart.Ready);foreach(var p in a.Battle.Players.Values)p.ReadyRoundGeneration=a.Battle.RoundGeneration;a.TickBattle(now+200);Require(a.Battle.SlayerMart.Ready&&a.Battle.SlayerMart.RemainingSeconds==300);}),
            ("Public support bodies preserve real positions and instance identity",(m,a)=>{Open(a);a.Battle.SlayerMartReturnWave=0;GrantSupport(a);var snapshot=a.BuildSnapshot();var player=snapshot.Battle.Players.Single(p=>p.PlayerId==1);Require(player.SlayerMartSupports.Count==3&&player.SlayerMartSupports.Select(p=>p.InstanceId).Distinct().Count()==3);var s=a.Battle.SlayerMart.Players[0];foreach(var body in player.SlayerMartSupports){var effect=s.Supports.Single(p=>p.InstanceId==body.InstanceId);var expected=ArcadeSlayerMartSupportRules.Bodies(s,effect,a.Battle.Players[1].PositionX,a.Battle.Players[1].PositionZ,Config.Supports,Config.Parameter)[0];Require(body.PositionX==expected.X&&body.PositionZ==expected.Y);}}),
            ("Directed snapshot clone retains private quote without aliasing",(m,a)=>{Open(a);var source=a.BuildSnapshotForPlayer(1);var clone=OnlineArcadeRunActorSystem.CloneSnapshot(source);Require(clone.SelfSlayerMart?.PlayerId==1&&clone.SelfSlayerMart.Offers.Count==16);source.SelfSlayerMart.Offers.Clear();source.Battle.SlayerMartCashBoxIds.Clear();source.Battle.SlayerMartMembers[0].Choice=2;Require(clone.SelfSlayerMart.Offers.Count==16&&clone.Battle.SlayerMartCashBoxIds.Count==9&&clone.Battle.SlayerMartMembers[0].Choice==1);}),
            ("Closed store life enters one whole next-round candidate",(m,a)=>{ClosingLife(a);Require(Commit(m,a)&&a.Record.RoundNumber==4&&a.Record.Players.All(p=>p.Lives==4)&&Read(m).Players.All(p=>p.Lives==4)&&a.Record.SlayerMart.Players.All(p=>p.AppliedBenefitGrants.Count==1));}),
            ("Zero-life member joins next round through respawn wait",(m,a)=>{ClosingLife(a);a.Record.Players[1].Lives=0;a.Record.Players[1].Ready=false;var dead=a.Battle.Players[2];dead.Alive=false;dead.CurrentHealth=0;dead.RespawnSeconds=0;Require(Commit(m,a)&&a.Record.Players[1].Lives==1&&a.Battle.Players[2].RespawnSeconds>0&&!a.Battle.Players[2].Alive&&a.Battle.Players[2].CurrentHealth==0);}),
            ("File lock preserves original then retry applies life once",(m,a)=>{ClosingLife(a);string original=File.ReadAllText(m.StorePath);using(var locked=new FileStream(m.StorePath,FileMode.Open,FileAccess.Read,FileShare.None)){Require(!Commit(m,a)&&a.Record.Players.All(p=>p.Lives==3));}Require(File.ReadAllText(m.StorePath)==original);Require(Commit(m,a)&&a.Record.Players.All(p=>p.Lives==4));}),
            ("Repeated transition callback never adds a second life",(m,a)=>{ClosingLife(a);Require(Commit(m,a));Require(!Commit(m,a)&&a.Record.Players.All(p=>p.Lives==4));}),
            ("Expired wall deadline rejects purchase before another Tick",(m,a)=>{Open(a);a.Battle.SlayerMartDeadlineAt=TimeInfo.Instance.ServerNow()-1;var s=a.Battle.SlayerMart;Require(!a.OperateSlayerMart(1,new(){Action=SlayerMartAction.Reroll,PlayerId=1,RegionInstanceId=s.RegionInstanceId,Generation=s.Generation,ExpectedRevision=s.Revision,OperationId="late"}).Success&&s.Players[0].Tokens==2);}),
            ("Disconnect pause shifts hard deadline without resetting duration",(m,a)=>{Open(a);a.Battle.SlayerMartDeadlineAt=10000;CallActor("UpdateSlayerMartPause",a.Battle,true,3000L);CallActor("UpdateSlayerMartPause",a.Battle,false,6000L);Require(a.Battle.SlayerMartDeadlineAt==13000&&a.Battle.SlayerMart.RemainingSeconds==300);}),
            ("Stale map input cannot acknowledge new shop generation",(m,a)=>{Open(a);bool rejected=false;try{a.SubmitInput(1,3,1,0,0,0,1,false,false,false,a.Battle.RoundGeneration-1,true);}catch(RpcException){rejected=true;}Require(rejected&&a.Battle.Players[1].LastInputSequence==0);})
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)try{Files(test);Console.WriteLine("PASS SlayerMartRoundOnline."+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerMartRoundOnline."+name+": "+(ex.InnerException??ex).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static ArcadeSlayerMartState Closed(){var s=ArcadeSlayerMartRules.Create("1",1,new long[]{1,2},17,Config.Catalog);s.Ready=s.Closing=true;foreach(var p in s.Players){p.Lives=3;p.Tokens=2;}return s;}
    private static void Open(OnlineArcadeRunActor a){a.Battle.SlayerMart=Closed();a.Battle.SlayerMart.Closing=false;foreach(var p in a.Battle.SlayerMart.Players)p.Choice=1;a.Battle.SlayerMartReturnWave=3;a.Battle.SlayerMartRegionPrepared=true;}
    private static void ClosingLife(OnlineArcadeRunActor a){Open(a);var s=a.Battle.SlayerMart;s.Closing=true;s.TeamLifeBought=true;foreach(var p in s.Players)s.PendingGrants.Add(new(){PlayerId=p.PlayerId,Effect=SlayerMartEffect.NextRoundLife,Amount=1,EventId=s.RegionInstanceId+":"+(++s.GrantSequence)});a.Battle.SlayerMartReturnWave=0;a.Battle.RoundTransitionPending=true;}
    private static void GrantSupport(OnlineArcadeRunActor a){var s=a.Battle.SlayerMart;s.PendingGrants.Add(new(){PlayerId=1,Effect=SlayerMartEffect.TeslaPack,Amount=3,EventId=s.RegionInstanceId+":"+(++s.GrantSequence)});Require(ArcadeSlayerMartSupportRules.TryApplyGrants(s,Config,new Dictionary<long,int>(),out var candidate,out _));a.Battle.SlayerMart=candidate;}
    private static OnlineArcadeRunRecord Read(OnlineArcadeRunManagerComponent m)=>JsonSerializer.Deserialize<OnlineArcadeRunStoreSnapshot>(File.ReadAllText(m.StorePath))!.Runs.Single(p=>p.RunId==1);
    private static void SyncStored(OnlineArcadeRunManagerComponent m,OnlineArcadeRunActor a){CallManager("CommitWithCandidate",m,a.Record,a.Record.RunId);a.CommittedRecord=OnlineArcadeRunManagerSystem.Clone(a.Record);}
    private static bool Commit(OnlineArcadeRunManagerComponent m,OnlineArcadeRunActor a)=>(bool)CallManager("TryCommitRoundTransition",m,a);
    private static object CallManager(string method,params object[] args)=>typeof(OnlineArcadeRunManagerSystem).GetMethod(method,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args)!;
    private static object CallActor(string method,params object[] args)=>(typeof(OnlineArcadeRunActorSystem).GetMethod(method,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)??throw new InvalidOperationException("Missing round store entry: "+method)).Invoke(null,args)!;
    private static void Files(Action<OnlineArcadeRunManagerComponent,OnlineArcadeRunActor> test)
    {
        string root=Path.Combine(Path.GetTempPath(),"SlayerMartRoundOnline",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var layout=OnlineArcadeRunActorSystem.ResolveRoundLayout(3,3);var r=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,Phase=OnlineArcadeRunPhase.SafeCheckpoint,
                IsCasualRoute=true,ArenaConfigNumber=3,NextArena=3,CompletedArena=2,RoundNumber=3,RoundLayoutId=layout.Id,RoundLayoutVersion=layout.Version,SlayerMartRulesVersion=1,
                ConfigFingerprint=ServerConfig.Instance.ArcadeRule.ConfigFingerprint,CreatedAtUtc=DateTime.UtcNow.ToString("O"),UpdatedAtUtc=DateTime.UtcNow.ToString("O"),
                Players=new(){new(){PlayerId=1,Lives=3,Ready=true,RoundHealth=100,RoundAlive=true,WeaponId="arcade.weapon.basic"},new(){PlayerId=2,Lives=3,Ready=true,RoundHealth=100,RoundAlive=true,WeaponId="arcade.weapon.basic"}}};
            var manager=new OnlineArcadeRunManagerComponent{StorePath=Path.Combine(root,"runs.json")};CallManager("CommitWithCandidate",manager,r,0L);
            var actor=new OnlineArcadeRunActor{Record=OnlineArcadeRunManagerSystem.Clone(r),CommittedRecord=OnlineArcadeRunManagerSystem.Clone(r)};
            typeof(Entity).GetProperty("InstanceId")!.SetValue(actor,1L);actor.Battle=OnlineArcadeRunActorSystem.PrepareBattle(actor.Record,false);manager.Runs.Add(1,actor);
            var b=actor.Battle;b.AwaitingExit=true;b.WaveClearAwarded=true;b.SpawnQueueIndex=b.SpawnQueue.Count;foreach(var p in b.Players.Values)p.ReadyRoundGeneration=b.RoundGeneration;
            test(manager,actor);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static void Require(bool ok){if(!ok)throw new InvalidOperationException("Round store contract violated");}
}
