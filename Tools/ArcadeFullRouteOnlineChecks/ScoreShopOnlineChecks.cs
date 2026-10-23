using System.Reflection;
using System.Text.Json;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>只检查个人积分商店真实权威候选、轮前交付及原子文件；不监听网络。</summary>
internal static class ScoreShopOnlineChecks
{
    private static object Call(string name,params object[] args)
    {var method=typeof(OnlineArcadeRunManagerSystem).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)??throw new InvalidOperationException("Missing authority method: "+name);return method.Invoke(null,args)!;}
    private static PropertyInfo Shop=>typeof(OnlineArcadePlayerRecord).GetProperty("ScoreShop")??throw new InvalidOperationException("Missing personal score shop persistence");
    private static ArcadeScoreShopState State(OnlineArcadePlayerRecord p)=>(ArcadeScoreShopState)Shop.GetValue(p)!;
    public static int Run(int max)
    {
        World.Instance.AddSingleton<Logger>().Log=new ReturnLogger();
        var tests=new (string,Action)[]{
            ("Whole arena opens distinct personal visits",()=>{var r=Record();Call("OpenScoreShops",r);Require(r.Players.All(p=>State(p).NextArena==2&&State(p).Offers.Distinct().Count()==3),"missing offers");State(r.Players[0]).Offers.Clear();Require(State(r.Players[1]).Offers.Count==3,"shared offers");}),
            ("Final victory never opens shop",()=>{var r=Record();r.Phase=OnlineArcadeRunPhase.Victory;Call("OpenScoreShops",r);Require(r.Players.All(p=>Shop.GetValue(p)==null),"final shop");var version=typeof(OnlineArcadeRunRecord).GetProperty("ScoreShopRulesVersion")??throw new InvalidOperationException("Missing explicit score shop compatibility version");r=Record();version.SetValue(r,0);Call("OpenScoreShops",r);Require(r.Players.All(p=>Shop.GetValue(p)==null),"old save upgraded");r=Record();version.SetValue(r,1);r.IsCasualRoute=false;Call("OpenScoreShops",r);Require(r.Players.All(p=>Shop.GetValue(p)==null),"legacy mode upgraded");}),
            ("Purchase spends only authenticated player score",()=>{var r=Opened();Intent(r,10,State(r.Players[0]).Offers[0]);Require(r.Players[0].Score<12000&&r.Players[1].Score==12000&&State(r.Players[0]).Completed&&!State(r.Players[1]).Completed,"personal purchase");}),
            ("Refresh costs 500 and keeps visit open",()=>{var r=Opened();var old=State(r.Players[0]).Offers.ToArray();Intent(r,11);Require(r.Players[0].Score==11500&&!State(r.Players[0]).Completed&&!old.Order().SequenceEqual(State(r.Players[0]).Offers.Order()),"refresh");}),
            ("Insufficient score rejects without mutation",()=>{var r=Opened();r.Players[0].Score=499;var old=JsonSerializer.Serialize(r);Require(Reject(()=>Intent(r,11))&&JsonSerializer.Serialize(r)==old,"insufficient mutated");}),
            ("Skip ends without spending",()=>{var r=Opened();Intent(r,12);Require(r.Players[0].Score==12000&&State(r.Players[0]).Completed&&State(r.Players[0]).PurchasedDropId==null,"skip");}),
            ("Ready and challenge cannot bypass either shopper",()=>{var r=Opened();Require(Reject(()=>Intent(r,3))&&Reject(()=>Intent(r,7)),"ready bypass");Intent(r,12);Require(Reject(()=>Intent(r,3)),"partner bypass");}),
            ("PrepareBattle rejects incomplete shops even without ready requirement",()=>{var r=Opened();Require(Reject(()=>OnlineArcadeRunActorSystem.PrepareBattle(r,false,false)),"prepare bypass");}),
            ("Drop delivery captures full duration and does not redeliver after reload",()=>{var r=Opened();State(r.Players[0]).Offers=new(){"arcade.drop.weapon.flamethrower","arcade.drop.support.drone","arcade.drop.weapon.rapid"};Intent(r,10,"arcade.drop.weapon.flamethrower");Intent(r,12,null,1);Intent(r,3);Intent(r,3,null,1);var b=OnlineArcadeRunActorSystem.PrepareBattle(r,false);Require(State(r.Players[0]).Delivered&&b.Players[1].TemporaryWeaponSeconds==20,"delivery missing");var loading=new OnlineArcadeRunActor{Record=r,Battle=b};loading.TickBattle(b.LastTickAt+10000);Require(b.Players[1].TemporaryWeaponSeconds==20,"loading consumed purchased duration");Call("CaptureRoundCheckpoint",r,b);r.Players[0].RoundTemporaryWeaponSeconds=7;var restored=OnlineArcadeRunActorSystem.PrepareBattle(OnlineArcadeRunManagerSystem.Clone(r),false);Require(restored.Players[1].TemporaryWeaponSeconds==7,"redelivered");}),
            ("Drone delivery uses existing support entry",()=>{var r=Opened();State(r.Players[0]).Offers=new(){"arcade.drop.weapon.flamethrower","arcade.drop.support.drone","arcade.drop.weapon.rapid"};Intent(r,10,"arcade.drop.support.drone");Intent(r,12,null,1);Intent(r,3);Intent(r,3,null,1);var b=OnlineArcadeRunActorSystem.PrepareBattle(r,false);Require(b.Players[1].Powerups[ArcadePowerupBehavior.Drone].RemainingSeconds==25,"drone duration");}),
            ("Clone preserves consumed score life tier and offers",()=>{var r=Opened();typeof(OnlineArcadePlayerRecord).GetProperty("ScoreLifeTier")!.SetValue(r.Players[0],1L);Intent(r,11);var c=OnlineArcadeRunManagerSystem.Clone(r);State(r.Players[0]).Offers.Clear();Require(c.Players[0].Score==11500&&State(c.Players[0]).Offers.Count==3&&(long)typeof(OnlineArcadePlayerRecord).GetProperty("ScoreLifeTier")!.GetValue(c.Players[0])! ==1,"clone lost state");LifeHighWater();}),
            ("Old fingerprint remains compatible and shop includes selected drop",()=>{Require((string)Call("OperationFingerprint",1L,2L,3,true,null)=="1|2|3|True","old fingerprint");Require((string)Call("OperationFingerprint",1L,2L,10,false,"a")!=(string)Call("OperationFingerprint",1L,2L,10,false,"b"),"drop omitted");}),
            ("Atomic failure preserves saved shop and source actor",()=>Files()),
            ("Successful authority request replay never charges again",()=>Transactions()),
            ("Generated request and snapshot retain selected drop and personal visit",()=>{var q=new C2G_OperateOnlineArcadeSafeCheckpoint{OperationId="buy",ActionKind=10,ScoreShopDropId="arcade.drop.support.drone",ExpectedRevision=3};var read=MemoryPack.MemoryPackSerializer.Deserialize<C2G_OperateOnlineArcadeSafeCheckpoint>(MemoryPack.MemoryPackSerializer.Serialize(q));Require(read.ScoreShopDropId==q.ScoreShopDropId&&read.OperationId=="buy","codec lost intent");var r=Opened();var snapshot=OnlineArcadeRunActorSystem.BuildSnapshot(r);var cloned=OnlineArcadeRunActorSystem.CloneSnapshot(snapshot);Require(JsonSerializer.Deserialize<ArcadeScoreShopState>(cloned.Players[0].ScoreShopJson)!.Offers.SequenceEqual(State(r.Players[0]).Offers),"snapshot lost shop");}),
            ("Persisted malformed visit refuses restore",()=>{var r=Opened();State(r.Players[0]).NextArena=3;Require(Reject(()=>Call("ValidateStore",new OnlineArcadeRunStoreSnapshot{Runs=new(){r}})),"invalid visit persisted");})
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS ScoreShopOnline."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL ScoreShopOnline."+name+": "+(e.InnerException??e).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static OnlineArcadeRunRecord Record()=>new(){RunId=100,RoomId=200,Revision=3,ScoreShopRulesVersion=1,Phase=OnlineArcadeRunPhase.SafeCheckpoint,IsCasualRoute=true,CompletedArena=1,NextArena=2,ArenaConfigNumber=3,RoundNumber=1,ConfigFingerprint=ServerConfig.Instance.ArcadeRule.ConfigFingerprint,Players=new(){new(){PlayerId=1,Lives=3,Score=12000,Ready=true,WeaponId="arcade.weapon.basic"},new(){PlayerId=2,Lives=3,Score=12000,Ready=true,WeaponId="arcade.weapon.basic"}}};
    private static OnlineArcadeRunRecord Opened(){var r=Record();Call("OpenScoreShops",r);return r;}
    private static void Intent(OnlineArcadeRunRecord r,int kind,string drop=null,int index=0)=>Call("ApplyCheckpointIntent",r,r.Players[index],kind,true,drop);
    private static bool Reject(Action action){try{action();return false;}catch{return true;}}
    private static void Files()
    {
        string root=Path.Combine(Path.GetTempPath(),"FirepowerLoopScoreShop",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{var r=Opened();var manager=new OnlineArcadeRunManagerComponent{StorePath=Path.Combine(root,"runs.json")};Call("CommitWithCandidate",manager,r,0L);string old=File.ReadAllText(manager.StorePath);var c=OnlineArcadeRunManagerSystem.Clone(r);Intent(c,11);bool rejected;using(var locked=new FileStream(manager.StorePath,FileMode.Open,FileAccess.Read,FileShare.None)){rejected=Reject(()=>Call("CommitWithCandidate",manager,c,c.RunId));}Require(rejected&&File.ReadAllText(manager.StorePath)==old&&r.Players[0].Score==12000,"failed write changed score");var disk=JsonSerializer.Deserialize<OnlineArcadeRunStoreSnapshot>(old)!;Require(State(disk.Runs[0].Players[0]).Offers.SequenceEqual(State(r.Players[0]).Offers),"restore rerolled");}
        finally{Directory.Delete(root,true);}
    }
    private static void Transactions()
    {
        string root=Path.Combine(Path.GetTempPath(),"FirepowerLoopScoreShop",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var r=Opened();var manager=new OnlineArcadeRunManagerComponent{StorePath=Path.Combine(root,"runs.json")};
            var actor=new OnlineArcadeRunActor{Record=OnlineArcadeRunManagerSystem.Clone(r),CommittedRecord=OnlineArcadeRunManagerSystem.Clone(r)};
            typeof(Entity).GetProperty("InstanceId")!.SetValue(actor,100L);manager.Runs.Add(100,actor);manager.RunIdsByPlayer.Add(1,100);manager.RunIdsByPlayer.Add(2,100);
            Call("CommitWithCandidate",manager,r,r.RunId);manager.RemainingFailSaveCount=1;
            Require(Reject(()=>manager.OperateSafeCheckpoint(1,"refresh",3,11,false).GetAwaiter().GetResult()),"save failure accepted");
            Require(actor.Record.Players[0].Score==12000&&actor.Record.Operations.Count==0,"failed transaction changed actor");
            var first=manager.OperateSafeCheckpoint(1,"refresh",3,11,false).GetAwaiter().GetResult();
            var second=manager.OperateSafeCheckpoint(1,"refresh",3,11,false).GetAwaiter().GetResult();
            Require(first.Accepted&&second.Accepted&&actor.Record.Players[0].Score==11500&&actor.Record.Operations.Count==1&&actor.Record.Revision==4,"replay charged twice");
            Require(Reject(()=>manager.OperateSafeCheckpoint(1,"refresh",3,10,false,"arcade.drop.support.drone").GetAwaiter().GetResult()),"id reused for different intent");
            Require(Reject(()=>manager.OperateSafeCheckpoint(1,"removed",4,1,false).GetAwaiter().GetResult()),"legacy shopping reopened");
            var recovered=JsonSerializer.Deserialize<OnlineArcadeRunStoreSnapshot>(File.ReadAllText(manager.StorePath))!.Runs[0];
            Require(recovered.Players[0].Score==11500&&recovered.Operations.Count==1&&State(recovered.Players[0]).Revision==2,"recovery lost receipt");
            string drop=State(actor.Record.Players[0]).Offers[0];
            manager.OperateSafeCheckpoint(1,"buy",4,10,false,drop).GetAwaiter().GetResult();
            manager.OperateSafeCheckpoint(2,"skip",5,12,false).GetAwaiter().GetResult();
            manager.OperateSafeCheckpoint(1,"ready-one",6,3,true).GetAwaiter().GetResult();
            manager.RemainingFailSaveCount=1;
            Require(Reject(()=>manager.OperateSafeCheckpoint(2,"ready-two",7,3,true).GetAwaiter().GetResult()),"failed delivery save accepted");
            Require(actor.Battle==null&&!State(actor.Record.Players[0]).Delivered&&actor.Record.Revision==7,"failed delivery consumed grant or exposed battle");
        }
        finally{Directory.Delete(root,true);}
    }
    private static void LifeHighWater()
    {
        var r=Opened();var player=r.Players[0];player.Score=210000;
        typeof(OnlineArcadePlayerRecord).GetProperty("ScoreLifeTier")!.SetValue(player,1L);
        Intent(r,11);player.Score=199900;
        var actor=new OnlineArcadeRunActor{Record=r};
        var rules=ArcadeTreasureRoomParameters.FromConfig(ServerConfig.Instance.Tables.TbArcadeTreasureRoom.DataList.First());
        var reward=new ArcadeTreasureRewardParameters(0,FirepowerLoop.Arcade.ArcadeTreasureRewardKind.GoldBar,0,0,100,1,1);
        var method=typeof(OnlineArcadeRunActorSystem).GetMethod("ApplyTreasureReward",BindingFlags.Static|BindingFlags.NonPublic)!;
        Require((bool)method.Invoke(null,new object[]{actor,1L,reward,rules,false})!&&player.Score==200000&&player.Lives==3,"consumption duplicated old score life");
        player.Score=399900;
        Require((bool)method.Invoke(null,new object[]{actor,1L,reward,rules,false})!&&player.Score==400000&&player.Lives==4,"next life tier not paid");
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
