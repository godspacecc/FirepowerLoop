using Game;
using Newtonsoft.Json;
internal static class SystemChecks
{
    public static int Run()
    {
        var tests=new (string,Action<ArcadeRunSystem,ArcadeRunModel,ArcadeRunFileStore>)[] {
            ("CrateDictionaryOrderDoesNotRewriteSameCheckpoint",(system,model,store)=> { var node=Node(1);node.RemainingCrateIds=new List<int>{7,8};node.CrateHealth=new Dictionary<int,int>{{7,2},{8,4}};Check(system.TrySaveRound(node,out var error),error);node.CrateHealth=new Dictionary<int,int>{{8,4},{7,2}};using(var locked=new FileStream(store.RunPath,FileMode.Open,FileAccess.Read,FileShare.None))Check(system.TrySaveRound(node,out error),error); }),
            ("StartsAtOneAndRejectsSkippedOrCrossArena",(system,model,store)=> { Check(!system.TrySaveRound(Node(2),out _),"跳轮被接受"); var bad=Node(1);bad.ArenaNumber=3;Check(!system.TrySaveRound(bad,out _),"跨场被接受");Check(system.TrySaveRound(Node(1),out var error),error); }),
            ("CommitsAllFourRoundsAndCopiesCallerState",(system,model,store)=> { for(int round=1;round<=4;round++){var node=Node(round);Check(system.TrySaveRound(node,out var error),error);node.PlayerState.Powerups[0].Charges=0;}Check(model.Current.RoundCheckpoint.RoundNumber==4 && store.Inspect().Data.RoundCheckpoint.PlayerState.Powerups[0].Charges==2,"第四轮或深拷贝失效"); }),
            ("IdenticalRetryDoesNotWriteAndChangedRetryFails",(system,model,store)=> { Check(system.TrySaveRound(Node(1),out var error),error);var before=File.ReadAllText(store.RunPath);using(var locked=new FileStream(store.RunPath,FileMode.Open,FileAccess.Read,FileShare.None))Check(system.TrySaveRound(Node(1),out error),error);var changed=Node(1);changed.PlayerState.Bombs=3;Check(!system.TrySaveRound(changed,out _),"同轮覆盖成功");Check(File.ReadAllText(store.RunPath)==before,"幂等重试改写文件"); }),
            ("WriteFailurePreservesModelAndDisk",(system,model,store)=> { Check(system.TrySaveRound(Node(1),out var error),error);model.SetTransientTreasureScore(new FirepowerLoop.Arcade.ArcadeTreasureScoreState(321,2,7,2));var before=JsonConvert.SerializeObject(model.Current);var disk=File.ReadAllText(store.RunPath);using(var locked=new FileStream(store.RunPath,FileMode.Open,FileAccess.Read,FileShare.None))Check(!system.TrySaveRound(Node(2),out _),"写锁未失败");Check(JsonConvert.SerializeObject(model.Current)==before && File.ReadAllText(store.RunPath)==disk,"失败改变旧节点或积分命数");Check(system.TrySaveRound(Node(2),out error),error);Check(model.Current.Score==321 && model.Current.RemainingLives==2,"成功重试重置积分命数"); }),
            ("CompletionClearsRoundAndRetainsEntry",(system,model,store)=> { Check(system.TrySaveRound(Node(1),out var error),error);var entry=Node(1).PlayerState;entry.MapId="arena.entry";entry.ArenaGold=0;var completion=new ArcadeArenaCompletion {ArenaNumber=2,NextArenaConfigNumber=3,NextCheckpoint=new ArcadeCheckpointSaveData{ArenaNumber=3},PlayerState=entry,EquipmentBySlot=model.Current.EquipmentBySlot};Check(system.TryCompleteArena(completion,out var saved,out error),error);Check(saved.RoundCheckpoint==null && saved.ArenaEntryState!=null && saved.NextArenaNumber==3,"整场提交未清轮或遗失继承"); }),
            ("FailureClearsRound",(system,model,store)=> { Check(system.TrySaveRound(Node(1),out var error),error);model.SetTransientLives(1);Check(system.TryHandleDeath(out var saved,out error)==ArcadeDeathResult.RunFailed,error);Check(saved.RoundCheckpoint==null && saved.Outcome=="failed","失败保留轮节点"); }),
            ("AbandonAndSettleClearRoundAndFiles",(system,model,store)=> { Check(system.TrySaveRound(Node(1),out var error),error);Check(system.TryAbandon(out error),error);Check(model.Current.RoundCheckpoint==null,"放弃保留轮节点");Check(system.TrySettle(out _,out error),error);Check(!model.HasRun && !File.Exists(store.RunPath),"结果清档失败"); }),
            ("InvalidPlayerCannotMutateNode",(system,model,store)=> { var before=JsonConvert.SerializeObject(model.Current);var invalid=Node(1);invalid.PlayerState.FireCooldown=float.NaN;Check(!system.TrySaveRound(invalid,out _),"非法角色状态被接受");Check(before==JsonConvert.SerializeObject(model.Current),"候选校验改变运行态"); })
        };
        int failures=0;
        foreach(var (name,test) in tests)
        {
            var root=Path.Combine(AppContext.BaseDirectory,"GeneralRoundTransaction-"+Guid.NewGuid().ToString("N"));
            try
            {
                var model=new ArcadeRunModel();var system=new ArcadeRunSystem{Model=model};system.ConfigureForTests(root);var store=new ArcadeRunFileStore(root);
                var run=new ArcadeRunSaveData {RunId=new string('a',32),UpdatedAtUtc=DateTime.UtcNow.ToString("O"),IsCasualRoute=true,ArenaConfigNumber=2,CompletedArenaCount=1,NextArenaNumber=2,Checkpoint=new ArcadeCheckpointSaveData{ArenaNumber=2}};
                run.EquipmentBySlot["weapon"]="arcade.weapon.basic";Check(store.Save(run,out var error),error);model.CommitSafeNode(run);model.EnterArena();
                test(system,model,store);Console.WriteLine("PASS RoundCheckpointTransaction."+name);
            }
            catch(Exception error){failures++;Console.WriteLine("FAIL RoundCheckpointTransaction."+name+": "+error.Message);}
            finally {if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        Console.WriteLine($"RoundCheckpointTransaction: {tests.Length-failures}/{tests.Length} passed");return failures==0?0:1;
    }
    private static ArcadeRoundCheckpoint Node(int round)=>new ArcadeRoundCheckpoint {ArenaNumber=2,ArenaConfigNumber=2,RoundNumber=round,LayoutVersion=1,MapId=$"arena.2.round.{round}",CratesInitialized=true,RemainingCrateIds=new List<int>{7},EncounterSeed=-37,PlayerState=new ArcadeIslandRoundCheckpoint{RoundNumber=round,MapId=$"arena.2.round.{round}",PlayerHealth=80,PlayerMaxHealth=100,Bombs=2,Dashes=3,Powerups=new List<ArcadeIslandPowerupCheckpoint>{new ArcadeIslandPowerupCheckpoint{ProductId="arcade.powerup.shield",RemainingSeconds=1.5f,Charges=2}}}};
    private static void Check(bool value,string error){if(!value)throw new Exception(error);}
}
