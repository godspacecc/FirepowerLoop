using System.Reflection;
using System.Text.Json;
using ET;
using ET.Server;

/// <summary>限定三关真实清轮、奖励和出口保存事务；每项覆盖一个场地的四轮。</summary>
internal static class NextStageFlowOnlineChecks
{
    public static int Run(int maximum)
    {
        if(maximum!=3)throw new ArgumentException("NextStageFlowOnline requires maxTests=3");
        int failures=0;
        foreach(int map in new[]{23,24,25})try{Check(map);Console.WriteLine($"PASS NextStageFlowOnline.Map{map}");}
            catch(Exception ex){failures++;Console.WriteLine($"FAIL NextStageFlowOnline.Map{map}: {ex.InnerException??ex}");}
        Console.WriteLine($"{3-failures}/3 passed");return failures==0?0:1;
    }
    private static void Require(bool value,string why){if(!value)throw new InvalidOperationException(why);}
    private static object Call(Type owner,string name,params object[] args)=>owner.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)
        .Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(null,args)!;
    private static object ActorCall(string name,params object[] args)=>Call(typeof(OnlineArcadeRunActorSystem),name,args);
    private static object ManagerCall(string name,params object[] args)=>Call(typeof(OnlineArcadeRunManagerSystem),name,args);
    private static void Check(int map)
    {
        int order=map-19,target=map==25?5:map+1;
        var actor=(OnlineArcadeRunActor)Call(typeof(NextRouteOnlineChecks),"Actor",1,map,order);
        string folder=Path.GetFullPath(Path.Combine("Tools/ArcadeFullRouteOnlineChecks/.test-output",Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        try
        {
            var manager=new OnlineArcadeRunManagerComponent{StorePath=Path.Combine(folder,"runs.json")};manager.Runs[1]=actor;
            for(int round=0;round<4;round++)
            {
                var b=actor.Battle;Require(b.WaveIndex==round&&b.RoundLayout.RoundNumber==round+1,"saved transition did not prepare correct round");
                foreach(var p in b.Players.Values){p.ReadyRoundGeneration=b.RoundGeneration;p.PositionX=0;p.PositionZ=-6;}
                b.SpawnQueue.Clear();b.Enemies.Clear();ActorCall("TryAdvanceWave",actor,b,0f);
                var reward=b.RoundReward;Require(reward!=null&&reward.RoundIdentity==(order-1)*4+round+1,"missing per-round reward identity");
                ActorCall("TryAdvanceWave",actor,b,0f);Require(ReferenceEquals(reward,b.RoundReward),"duplicate reward box");
                var item=reward.Items[0];var player=b.Players[1];player.PositionX=item.X;player.PositionZ=item.Z;
                long score=actor.Record.Players.Sum(p=>p.Score);ActorCall("TickRoundReward",actor,b,.1f);ActorCall("TickDrops",actor,b);
                Require(actor.Record.Players.Sum(p=>p.Score)==score,"reward collected during opening effect");
                ActorCall("TickRoundReward",actor,b,3.3f);ActorCall("TickDrops",actor,b);
                Require(actor.Record.Players.Sum(p=>p.Score)>score,"reward not collectible after effect");
                if(round<3)
                {
                    Require(b.StagePortal==null&&b.AwaitingExit&&!b.ArenaCompletionReady,"early round incorrectly opened stage portal");
                    var exit=b.RoundLayout.Entries[0];player.PositionX=exit.X;player.PositionZ=exit.Y;player.ConfirmedExitTarget=map;
                    Require((bool)ActorCall("TickRoundExit",b)&&b.RoundTransitionPending,"round passage rejected");
                    Require((bool)ManagerCall("TryCommitRoundTransition",manager,actor),"round checkpoint save failed");
                }
                else
                {
                    Require(b.StagePortal!=null,"final round has no stage portal");
                    player.PositionX=b.StagePortal.X+4;player.PositionZ=b.StagePortal.Z;ActorCall("TickRoundReward",actor,b,.01f);
                    player.PositionX=b.StagePortal.X;player.ConfirmedExitTarget=target;
                    Require((bool)ActorCall("TickRoundExit",b)&&b.ArenaCompletionReady,"stage portal did not accept target");
                    Require((bool)ManagerCall("TryCommitArenaCompletion",manager,actor),"stage completion save failed");
                }
            }
            var options=(JsonSerializerOptions)ManagerCall("JsonOptions");
            var saved=JsonSerializer.Deserialize<OnlineArcadeRunStoreSnapshot>(File.ReadAllText(manager.StorePath),options)!;
            ManagerCall("ValidateStore",saved);var record=saved.Runs.Single();
            Require(record.CasualRouteVersion==1&&record.ArenaConfigNumber==target&&record.CompletedArena==order,"saved next stage is wrong");
        }
        finally{Directory.Delete(folder,true);}
    }
}
