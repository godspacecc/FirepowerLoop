using System.Reflection;
using ET;
using ET.Server;

/// <summary>真实权威方法的限定检查，不启动网络或完整套件。</summary>
internal static class ArcadeCommandOnlineChecks
{
    public static int Run()
    {
        var actor = (OnlineArcadeRunActor)typeof(HerdOnlineChecks).GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        var player = actor.Battle.Players[1];
        actor.SetInvincibleForTests(player, true);
        actor.DamagePlayerForTests(player, 1);
        Require(player.CurrentHealth == 100 && !actor.Battle.Players[2].TestInvincible, "god must protect only host");
        actor.SetInvincibleForTests(player, false);
        actor.DamagePlayerForTests(player, 1);
        Require(player.CurrentHealth == 99, "god off did not restore damage");
        actor.Record.CasualRouteVersion=2;
        var original=actor.Record;
        var prepared=OnlineArcadeRunManagerSystem.PrepareGotoForTests(actor,2,3,out var candidate);
        Require(prepared.ArenaConfigNumber==21&&prepared.WaveIndex==2&&candidate.NextArena==2,"goto route ordinal mapping wrong");
        Require(ReferenceEquals(actor.Record,original)&&actor.NormalRecordBeforeTestGoto==null,"preparation mutated actor");
        var repeatActor=new OnlineArcadeRunActor{Record=candidate,Battle=prepared};
        prepared.BattleRevision=999;
        var repeated=OnlineArcadeRunManagerSystem.PrepareGotoForTests(repeatActor,2,3,out var repeatedRecord);
        Require(repeated.RoundGeneration>prepared.RoundGeneration&&repeatedRecord.RoundGeneration==repeated.RoundGeneration,"repeated same-arena goto reused generation");
        repeatActor.Record=repeatedRecord;repeatActor.Battle=repeated;
        bool staleReady=false;
        try{repeatActor.SubmitInput(1,repeated.ArenaNumber,1,0,0,1,0,false,false,false,prepared.RoundGeneration,true);}catch(RpcException){staleReady=true;}
        Require(staleReady&&repeated.Players[1].ReadyRoundGeneration!=repeated.RoundGeneration,"old map ready acknowledged new goto");
        repeatActor.SubmitInput(1,repeated.ArenaNumber,2,0,0,1,0,false,false,false,repeated.RoundGeneration,true);
        Require(repeated.Players[1].ReadyRoundGeneration==repeated.RoundGeneration,"new map readiness not accepted");
        foreach(var invalid in new[]{(0,1),(11,1),(2,0),(2,5)})
        {bool rejected=false;try{OnlineArcadeRunManagerSystem.PrepareGotoForTests(actor,invalid.Item1,invalid.Item2,out _);}catch(RpcException){rejected=true;}Require(rejected,"invalid goto accepted");}
        actor.Record.CasualRouteVersion=1;
        bool ambiguous=false;try{OnlineArcadeRunManagerSystem.PrepareGotoForTests(actor,9,1,out _);}catch(RpcException){ambiguous=true;}Require(ambiguous,"old route ambiguity guessed");
        actor.Record.CasualRouteVersion=0;
        var queue = actor.Battle.SpawnQueue.ToArray(); int cursor = actor.Battle.SpawnQueueIndex;
        int spawned = actor.Battle.SpawnedInWave;
        actor.SpawnEnemiesForTests("enemy.arcade.basic", 1);
        Require(actor.Battle.Enemies.Values.Any(e => e.EnemyConfigId == "enemy.arcade.basic"), "requested enemy missing");
        Require(queue.SequenceEqual(actor.Battle.SpawnQueue) && cursor == actor.Battle.SpawnQueueIndex && spawned == actor.Battle.SpawnedInWave, "test spawn consumed wave budget");
        foreach (int count in new[] { 0, -1, 21 })
        { bool rejected=false; try { actor.SpawnEnemiesForTests("enemy.arcade.basic",count); } catch(RpcException) { rejected=true; } Require(rejected,"invalid count accepted"); }
        while(actor.Battle.Enemies.Count < actor.Battle.MaxAlive) actor.SpawnEnemiesForTests("enemy.arcade.basic",1);
        bool capacity=false;try{actor.SpawnEnemiesForTests("enemy.arcade.basic",1);}catch(RpcException){capacity=true;}Require(capacity,"capacity exceeded");
        typeof(SlayerMartRoundOnlineChecks).GetMethod("Open",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{actor});
        actor.Battle.SlayerMart.Ready=false;actor.Battle.SlayerMart.Closing=false;actor.Battle.SlayerMartRegionPrepared=false;
        foreach(var member in actor.Battle.SlayerMart.Players)member.Choice=1;
        actor.SetInvincibleForTests(player,true);
        int beforeRegion=actor.Battle.RoundGeneration;
        typeof(OnlineArcadeRunActorSystem).GetMethod("TickSlayerMartRegion",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{actor,actor.Battle,.01f});
        Require(actor.Battle.RoundGeneration>beforeRegion&&!player.TestInvincible,"same-runtime region switch retained god");
        var root=Path.Combine(Path.GetTempPath(),"FirepowerLoopArcadeCommandChecks",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var manager=new OnlineArcadeRunManagerComponent{StorePath=Path.Combine(root,"runs.json")};
            File.WriteAllText(manager.StorePath,"normal-save-sentinel");
            typeof(Entity).GetProperty("InstanceId")!.SetValue(actor,1L);
            actor.CommittedRecord=OnlineArcadeRunManagerSystem.Clone(actor.Record);
            actor.NormalRecordBeforeTestGoto=OnlineArcadeRunManagerSystem.Clone(actor.Record);
            manager.Runs.Add(actor.Record.RunId,actor);
            typeof(OnlineArcadeRunManagerSystem).GetMethod("CommitWithCandidate",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{manager,actor.Record,actor.Record.RunId});
            Require(File.ReadAllText(manager.StorePath)=="normal-save-sentinel","test checkpoint overwrote normal disk");
            actor.Record.Revision=500;
            var restore=typeof(OnlineArcadeRunManagerSystem).GetMethod("RestoreNormalAfterTestGoto");
            Require(restore!=null,"normal restore needs a monotonic revision transaction");
            Require((bool)restore.Invoke(null,new object[]{actor}),"normal restore failed");
            Require(actor.Record.Revision>500&&actor.NormalRecordBeforeTestGoto==null,"restored normal revision rejected by client");
            Require(File.ReadAllText(manager.StorePath)=="normal-save-sentinel","restoration touched normal disk");
        }
        finally{Directory.Delete(root,true);}
        Console.WriteLine("PASS ArcadeCommandOnline.AuthorityCommands (god damage, host scope, spawn queue/capacity, transient save)");
        return 0;
    }
}
