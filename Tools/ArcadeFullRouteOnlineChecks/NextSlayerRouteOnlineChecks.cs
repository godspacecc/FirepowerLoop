using System.Reflection;
using System.Text.Json;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>真实农场出口、机会准备与挑战返回，覆盖新路线和旧存档路线。</summary>
internal static class NextSlayerRouteOnlineChecks
{
    public static int Run(int maximum)
    {
        var tests=new (string,Action)[]{("NewFarmOpportunityReturnsToGraveyard",()=>BothChoices(1,23)),("LegacyFarmOpportunityReturnsToFive",()=>BothChoices(0,5))};
        if(maximum<tests.Length)throw new ArgumentException("NextSlayerRouteOnline maximum too small");int failures=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS NextSlayerRouteOnline."+name);}catch(Exception error){failures++;Console.WriteLine("FAIL NextSlayerRouteOnline."+name+": "+(error.InnerException??error).Message);}
        Console.WriteLine($"{tests.Length-failures}/{tests.Length} passed");return failures==0?0:1;
    }
    private static void BothChoices(int version,int target)
    {
        foreach(bool enter in new[]{false,true})Flow(version,target,enter);
        // 同一共享边界必须接纳十三关末尾，并继续拒绝越界身份。
        if(version==1){var c=ServerConfig.Instance.SlayerChallenge;Require(ArcadeSlayerChallengeRules.Create(c,"bounds",new long[]{1,2},ArcadeRouteRules.ArenaCount(ArcadeRouteRules.CurrentVersion),25,1).ReturnMap==25,"current route upper bounds rejected");Require(Reject(()=>ArcadeSlayerChallengeRules.Create(c,"bounds",new long[]{1,2},14,25,1))&&Reject(()=>ArcadeSlayerChallengeRules.Create(c,"bounds",new long[]{1,2},13,26,1)),"unknown return identity accepted");}
    }
    private static void Flow(int version,int target,bool enter)
    {
        string parent=Path.GetFullPath("Tools/ArcadeFullRouteOnlineChecks/.test-output");string folder=Path.GetFullPath(Path.Combine(parent,Guid.NewGuid().ToString("N")));
        Require(folder.StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"unsafe test directory");Directory.CreateDirectory(folder);
        try
        {
            var record=NewRun();record.CasualRouteVersion=version;record.CompletedArena=2;record.NextArena=3;record.ArenaConfigNumber=22;
            var shop=ArcadeSlayerMartRules.Create(record.RunId.ToString(),1,new long[]{1,2},17,ServerConfig.Instance.SlayerMart.Catalog);shop.Ready=shop.Closing=true;record.SlayerMart=shop;
            foreach(var member in record.Players)member.Ready=true;
            var actor=new OnlineArcadeRunActor{Record=record,CommittedRecord=OnlineArcadeRunManagerSystem.Clone(record),Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};
            typeof(Entity).GetProperty("InstanceId")!.SetValue(actor,1L);var b=actor.Battle;
            foreach(var p in b.Players.Values)p.ReadyRoundGeneration=b.RoundGeneration;b.WaveIndex=3;b.AwaitingExit=true;
            Actor("TickRoundReward",actor,b,ServerConfig.Instance.RoundReward.PortalAppearSeconds+.01f);Require(b.StagePortal!=null,"farm portal absent");
            var player=b.Players[1];player.PositionX=b.StagePortal!.X;player.PositionZ=b.StagePortal.Z;player.ConfirmedExitTarget=target;
            Require((bool)Actor("TickRoundExit",b),"farm exit refused route target");
            var manager=new OnlineArcadeRunManagerComponent{StorePath=Path.Combine(folder,"runs.json")};manager.Runs[record.RunId]=actor;
            Require((bool)Manager("TryCommitArenaCompletion",manager,actor),"farm completion did not persist");
            Require(actor.Record.SlayerMartRulesVersion==1&&actor.Record.ScoreShopRulesVersion==1&&actor.Record.ChallengeId==ServerConfig.Instance.SlayerChallenge.ChallengeId,"new rules did not configure after-arena3 opportunity");
            foreach(var member in actor.Record.Players)Manager("ApplyCheckpointIntent",actor.Record,member,12,true,null);
            Manager("CommitWithCandidate",manager,actor.Record,actor.Record.RunId);actor.CommittedRecord=OnlineArcadeRunManagerSystem.Clone(actor.Record);
            Require((bool)Manager("TryStartTransitBattle",manager,actor),"opportunity did not publish actual preparation");
            Require(actor.Battle.SlayerChallenge.Phase==ArcadeSlayerChallengePhase.Preparing&&actor.Battle.SlayerChallenge.ReturnMap==target&&actor.Battle.SlayerChallenge.ReturnArena==4,"challenge replaced mainline return identity");
            foreach(var member in actor.Battle.Players.Values)member.ReadyRoundGeneration=actor.Battle.RoundGeneration;
            foreach(long id in new long[]{1,2})Manager("OperateSlayerPreparation",manager,actor,id,"choice-"+id,actor.Record.Revision,enter?7:3,true);
            if(enter)
            {
                Require(actor.Battle.SlayerChallenge.Phase==ArcadeSlayerChallengePhase.Collecting&&actor.Battle.SpawnQueue.Count>0,"enter vote did not start actual collection");
                foreach(long id in new long[]{1,2})Actor("OperateSlayerExit",actor,id,"leave-"+id,actor.Record.Revision,true);
                Require(actor.Battle.SlayerChallenge.Outcome==ArcadeSlayerChallengeOutcome.Abandoned,"actual exit did not end challenge");
            }
            else Require(actor.Battle.SlayerChallenge.Outcome==ArcadeSlayerChallengeOutcome.Skipped,"skip did not end preparation");
            Require(actor.Battle.ChallengeCompletionReady&&(bool)Manager("TryCommitChallengeCompletion",manager,actor),"challenge did not commit safe return");
            var options=(JsonSerializerOptions)Manager("JsonOptions");var saved=JsonSerializer.Deserialize<OnlineArcadeRunStoreSnapshot>(File.ReadAllText(manager.StorePath),options)!;Manager("ValidateStore",saved);var restored=saved.Runs.Single();
            Require(restored.CasualRouteVersion==version&&restored.ArenaConfigNumber==target&&restored.NextArena==4&&restored.CompletedArena==3&&restored.Phase==OnlineArcadeRunPhase.SafeCheckpoint&&restored.ChallengeAttempted&&string.IsNullOrEmpty(restored.ChallengeId),"saved return changed mainline or opportunity state");
            var next=OnlineArcadeRunActorSystem.PrepareBattle(restored,false,false);Require(next.ArenaConfigNumber==target&&next.RoundLayout.ArenaConfigNumber==target&&!next.IsChallenge,"saved return cannot prepare correct mainline map");
        }
        finally{Directory.Delete(folder,true);}
    }
    /// <summary>发布开关在本进程夹具内按正式适配打开，真实新局读取后恢复，不修改配置源。</summary>
    private static OnlineArcadeRunRecord NewRun()
    {
        var config=ServerConfig.Instance;var previous=config.SlayerMart;var row=config.Tables.TbArcadeSlayerMartAbility.Get("release.enabled");float old=row.Value;var field=row.GetType().GetField("Value")!;var property=typeof(ServerConfig).GetProperty("SlayerMart")!;
        try
        {
            field.SetValue(row,1f);property.SetValue(config,ArcadeSlayerMartConfiguration.FromTables(config.Tables));
            var room=new OnlineRoomActor{RoomId=2};room.Members[1]=new OnlineRoomMember{PlayerId=1,JoinSequence=1};room.Members[2]=new OnlineRoomMember{PlayerId=2,JoinSequence=2};
            var record=(OnlineArcadeRunRecord)Manager("BuildInitialRecord",1L,room,config.ArcadeRule);Require(record.SlayerMartRulesVersion==1&&record.CasualRouteVersion==1&&record.ScoreShopRulesVersion==1,"real new run did not capture current rules");return record;
        }
        finally{field.SetValue(row,old);property.SetValue(config,previous);}
    }
    private static object Manager(string name,params object[] values)=>typeof(OnlineArcadeRunManagerSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,values)!;
    private static object Actor(string name,params object[] values)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,values)!;
    private static bool Reject(Action action){try{action();return false;}catch(ArgumentException){return true;}}
    private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
}
