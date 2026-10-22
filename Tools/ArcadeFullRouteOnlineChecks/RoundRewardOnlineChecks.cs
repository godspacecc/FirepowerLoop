using System.Reflection;
using ET;using ET.Server;using FirepowerLoop.Arcade;
/// <summary>真实双人清场与拾取入口，不启动服务或读写存档。</summary>
internal static class RoundRewardOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks=new (string,Action)[]{("ClearCreatesOnce",ClearCreatesOnce),("FullEffectAndTwoPlayersAwardOnce",FullEffectAndTwoPlayersAwardOnce),("LeavingAbandonsRemaining",LeavingAbandonsRemaining),("ChallengeHasNoChest",ChallengeHasNoChest),("StagePortalReplacesFinalPassages",StagePortalReplacesFinalPassages),("SnapshotCloneCodecKeepsRewardAndPortal",SnapshotCloneCodecKeepsRewardAndPortal)};
        if(maximum<checks.Length)throw new ArgumentException("RoundRewardOnline maximum too small");int failed=0;
        foreach(var c in checks){try{c.Item2();Console.WriteLine("PASS RoundRewardOnline."+c.Item1);}catch(Exception e){failed++;Console.WriteLine("FAIL RoundRewardOnline."+c.Item1+": "+(e.InnerException??e));}}
        Console.WriteLine($"{checks.Length-failed}/{checks.Length} passed");return failed==0?0:1;
    }
    private static OnlineArcadeRunActor Create()
    {
        var rule=ServerConfig.Instance.ArcadeRule;var record=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,NextArena=1,ArenaConfigNumber=1,IsCasualRoute=true,ConfigFingerprint=rule.ConfigFingerprint,
        Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
        var a=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};a.Battle.SpawnQueue.Clear();
        foreach(var p in a.Battle.Players.Values){p.PositionX=p.PositionZ=6;p.ReadyRoundGeneration=a.Battle.RoundGeneration;}
        return a;
    }
    private static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(null,args)!;
    private static ArcadeRoundRewardState Reward(OnlineArcadeRunActor a)=>(ArcadeRoundRewardState)typeof(OnlineArcadeBattleRuntime).GetField("RoundReward")?.GetValue(a.Battle)!;
    private static void Require(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    private static void ClearCreatesOnce(){var a=Create();Call("TryAdvanceWave",a,a.Battle,0f);Require(Reward(a)!=null,"No chest after clear");var first=Reward(a);Call("TryAdvanceWave",a,a.Battle,0f);Require(ReferenceEquals(first,Reward(a)),"Duplicate chest");}
    private static void FullEffectAndTwoPlayersAwardOnce()
    {
        var a=Create();Call("TryAdvanceWave",a,a.Battle,0f);var r=Reward(a);Require(r!=null,"No chest");var item=r.Items[0];foreach(var p in a.Battle.Players.Values){p.PositionX=item.X;p.PositionZ=item.Z;}
        Call("TickRoundReward",a,a.Battle,3.29f);Call("TickDrops",a,a.Battle);Require(a.Record.Players.Sum(p=>p.Score)==0,"Early award");
        Call("TickRoundReward",a,a.Battle,.02f);Call("TickDrops",a,a.Battle);long total=a.Record.Players.Sum(p=>p.Score);Require(total>0,"No award after effect");
        Require(a.Record.Players[0].Score>0&&a.Record.Players[1].Score==0,"Stable first player must claim once");Call("TickDrops",a,a.Battle);Require(a.Record.Players.Sum(p=>p.Score)==total,"Duplicate award");
    }
    private static void LeavingAbandonsRemaining(){var a=Create();Call("TryAdvanceWave",a,a.Battle,0f);Require(Reward(a)!=null,"No chest");Call("PrepareWave",a.Battle,ServerConfig.Instance.Tables.TbArcadeArena.Get(1),1);Require(Reward(a)==null&&a.Record.Players.Sum(p=>p.Score)==0,"Auto awarded leftovers");}
    private static void ChallengeHasNoChest(){var a=Create();a.Battle.IsChallenge=true;Call("TryAdvanceWave",a,a.Battle,0f);Require(Reward(a)==null,"Independent challenge chest");}
    private static void StagePortalReplacesFinalPassages()
    {
        var a=Create();a.Battle.WaveIndex=3;foreach(var p in a.Battle.Players.Values){p.PositionX=p.PositionZ=0;}
        Call("TryAdvanceWave",a,a.Battle,0f);Call("TickRoundReward",a,a.Battle,1f);
        var portal=(ArcadeStagePortalState)typeof(OnlineArcadeBattleRuntime).GetField("StagePortal")?.GetValue(a.Battle)!;Require(portal!=null,"No central portal");
        var player=a.Battle.Players[1];int target=ServerConfig.Instance.Tables.TbArcadeArena.Get(1).NextArenaNumbers[0];
        player.PositionX=portal.X;player.PositionZ=portal.Z;player.ConfirmedExitTarget=target;Call("TickRoundExit",a.Battle);Require(!a.Battle.ArenaCompletionReady,"Auto left under player");
        player.PositionX=portal.X+4;Call("TickRoundReward",a,a.Battle,.1f);player.PositionX=portal.X;player.ConfirmedExitTarget=target;Call("TickRoundExit",a.Battle);Require(a.Battle.ArenaCompletionReady,"Central contact failed");
        a.Battle.ArenaCompletionReady=false;Call("TickRoundReward",a,a.Battle,40f);Require(a.Battle.RoundReward.Phase==ArcadeRoundRewardPhase.Expired&&portal.ElapsedSeconds>40,"Expired reward froze stage portal");
    }
    private static void SnapshotCloneCodecKeepsRewardAndPortal()
    {
        var a=Create();a.Battle.WaveIndex=3;Call("TryAdvanceWave",a,a.Battle,0f);Call("TickRoundReward",a,a.Battle,1f);
        using var source=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",source);
        using var decoded=MemoryPack.MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPack.MemoryPackSerializer.Serialize(clone));
        Require(decoded.RoundReward!=null&&decoded.RoundReward.Items.Count==24&&decoded.RoundReward.ElapsedSeconds==1,"Reward codec lost state");Require(decoded.StagePortalSeconds==1,"Portal codec lost time");
        source.RoundReward.Items[0].Collected=true;Require(!clone.RoundReward.Items[0].Collected,"Mutable cloned reward");
    }

}
