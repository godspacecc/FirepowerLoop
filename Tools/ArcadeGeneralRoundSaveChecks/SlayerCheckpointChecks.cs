using Game;
using FirepowerLoop.Arcade;
using Newtonsoft.Json;

/// <summary>正式RunSystem与文件事务，所有文件写入已验证的独立临时根。</summary>
internal static class SlayerCheckpointChecks
{
    public static int Run(int max)
    {
        var tests=new (string,Action<ArcadeRunSystem,ArcadeRunModel,ArcadeRunFileStore>)[]{
            ("Old node defaults to no new shop",(sys,model,store)=>{var data=JsonConvert.DeserializeObject<ArcadeRunSaveData>(File.ReadAllText(store.RunPath));Require(GetVersion(data)==0&&GetShop(data)==null,"old node enabled shop");}),
            ("Next round atomically commits shop ledger",(sys,model,store)=>{Enable(model);var n=Node(4);SetShop(n,Shop());Require(sys.TrySaveRound(n,out var error),error);Require(GetShop(store.Inspect().Data).Players[0].Cash==15000&&GetShop(model.Current).Receipts.Count==1,"shop missing from node");}),
            ("Save lock preserves complete previous node",(sys,model,store)=>{Enable(model);var before=File.ReadAllText(store.RunPath);var n=Node(4);SetShop(n,Shop());using(var locked=new FileStream(store.RunPath,FileMode.Open,FileAccess.Read,FileShare.None))Require(!sys.TrySaveRound(n,out _),"locked save succeeded");Require(GetShop(model.Current)==null&&File.ReadAllText(store.RunPath)==before,"failed save leaked shopping");Require(sys.TrySaveRound(n,out var error),error);Require(GetShop(store.Inspect().Data).Players[0].Cash==15000,"retry lost ledger");}),
            ("Caller mutation cannot change committed shop",(sys,model,store)=>{Enable(model);var n=Node(4);var s=Shop();SetShop(n,s);Require(sys.TrySaveRound(n,out var error),error);s.Players[0].Cash=0;s.Receipts.Clear();Require(GetShop(model.Current).Players[0].Cash==15000&&GetShop(store.Inspect().Data).Receipts.Count==1,"shallow copy");}),
            ("Unknown version preserves original file",(sys,model,store)=>{var d=model.Current;SetVersion(d,2);var before=File.ReadAllText(store.RunPath);Require(!store.Save(d,out _),"unknown version saved");Require(File.ReadAllText(store.RunPath)==before,"unknown version destroyed original");}),
            ("Old run refuses a shop payload",(sys,model,store)=>{var n=Node(4);SetShop(n,Shop());Require(!sys.TrySaveRound(n,out _),"old run accepted new shop");}),
            ("Same round cannot overwrite shop balances",(sys,model,store)=>{Enable(model);var n=Node(4);SetShop(n,Shop());Require(sys.TrySaveRound(n,out var error),error);GetShop(n).Players[0].Cash++;Require(!sys.TrySaveRound(n,out _),"same round ledger rewritten");Require(GetShop(store.Inspect().Data).Players[0].Cash==15000,"disk overwritten");}),
            ("Failure result removes local shop economy",(sys,model,store)=>{Enable(model);var n=Node(4);SetShop(n,Shop());Require(sys.TrySaveRound(n,out var error),error);model.SetTransientLives(1);Require(sys.TryHandleDeath(out var result,out error)==ArcadeDeathResult.RunFailed,error);Require(GetShop(result)==null&&GetShop(store.Inspect().Data)==null,"result retained shop economy");})
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)
        {
            string root=Path.Combine(AppContext.BaseDirectory,"SlayerCheckpoint-"+Guid.NewGuid().ToString("N"));
            try
            {
                var model=new ArcadeRunModel();var sys=new ArcadeRunSystem{Model=model};sys.ConfigureForTests(root);var store=new ArcadeRunFileStore(root);
                var d=new ArcadeRunSaveData{RunId=new string('a',32),IsCasualRoute=true,CompletedArenaCount=2,NextArenaNumber=3,ArenaConfigNumber=4,
                    UpdatedAtUtc=DateTime.UtcNow.ToString("O"),Checkpoint=new(){ArenaNumber=3},RoundCheckpoint=Node(3)};
                d.EquipmentBySlot["weapon"]="arcade.weapon.basic";Require(store.Save(d,out var error),error);model.CommitSafeNode(d);model.EnterArena();
                test(sys,model,store);Console.WriteLine("PASS SlayerCheckpointTransaction."+name);
            }
            catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerCheckpointTransaction."+name+": "+ex.Message);}
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static ArcadeRoundCheckpoint Node(int round)=>new(){ArenaNumber=3,ArenaConfigNumber=4,RoundNumber=round,LayoutVersion=1,MapId="arena.4.round."+round,
        PlayerState=new(){RoundNumber=round,MapId="arena.4.round."+round,PlayerHealth=100,PlayerMaxHealth=100,Bombs=1,Dashes=2}};
    private static ArcadeSlayerMartState Shop()=>new(){RegionInstanceId=new string('a',32)+":slayermart:1",Visit=1,Generation=1,Revision=3,Ready=true,Closing=true,RemainingSeconds=200,
        Players=new(){new(){PlayerId=1,Cash=15000,Lives=3,Offers=Enumerable.Range(1,16).Select(i=>"slayermart.item."+i.ToString("00")).ToList()}},Receipts=new(){new(){PlayerId=1,OperationId="buy1",Fingerprint="bomb"}}};
    private static void Enable(ArcadeRunModel model){var d=model.Current;SetVersion(d,1);model.CommitSafeNode(d);model.EnterArena();}
    private static void SetVersion(ArcadeRunSaveData d,int version)=>(typeof(ArcadeRunSaveData).GetProperty("SlayerMartRulesVersion")??throw new Exception("new run rules version missing")).SetValue(d,version);
    private static int GetVersion(ArcadeRunSaveData d)=>(int)(typeof(ArcadeRunSaveData).GetProperty("SlayerMartRulesVersion")??throw new Exception("new run rules version missing")).GetValue(d);
    private static void SetShop(object d,ArcadeSlayerMartState s)=>(d.GetType().GetProperty("SlayerMart")??throw new Exception("checkpoint shop snapshot missing")).SetValue(d,s);
    private static ArcadeSlayerMartState GetShop(object d)=>(ArcadeSlayerMartState)(d.GetType().GetProperty("SlayerMart")??throw new Exception("checkpoint shop snapshot missing")).GetValue(d);
    private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}
