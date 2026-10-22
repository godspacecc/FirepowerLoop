using System.Reflection;
using ET;
using ET.Server;
internal static class EarlyStageOnlineChecks
{
 public static int Run(int maximum)
 {
  var checks=new (string,Action)[]{("FarmOuterYard",FarmOuterYard),("FarmWallAndBreach",FarmWallAndBreach),("DockWater",DockWater),("FarmPortal",FarmPortal),("LegacyBounds",LegacyBounds),("OneTimedEntry",OneTimedEntry),("LegacySequence",LegacySequence),("LegacyCompletionUsesActualStage",LegacyCompletionUsesActualStage)};
  if(maximum<checks.Length)throw new ArgumentException("EarlyStageOnline maximum too small");int failed=0;
  foreach(var(name,test) in checks)try{test();Console.WriteLine("PASS EarlyStageOnline."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL EarlyStageOnline."+name+": "+(e.InnerException??e).Message);}
  Console.WriteLine($"{checks.Length-failed}/{checks.Length} passed");return failed==0?0:1;
 }
    private static OnlineArcadeRunActor Create(int arena=22)
    {
        var rule=ServerConfig.Instance.ArcadeRule;
        var record=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,NextArena=3,ArenaConfigNumber=arena,IsCasualRoute=true,ConfigFingerprint=rule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
        var actor=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};
        foreach(var player in actor.Battle.Players.Values){player.PositionX=player.PositionZ=0;player.AimX=0;player.AimZ=1;player.ReadyRoundGeneration=actor.Battle.RoundGeneration;}
        return actor;
    }

 private static void Move(OnlineArcadeRunActor a,float x,float z,float seconds){var p=a.Battle.Players[1];p.PositionX=x;p.PositionZ=z;p.MoveX=1;p.MoveZ=0;p.LastInputAt=1000;Call("TickPlayers",a,a.Battle,seconds,1000L);}
 private static void FarmOuterYard(){var a=Create();Move(a,20,-15,.1f);Require(a.Battle.Players[1].PositionX>20,"new yard clipped to legacy boundary");}
 private static void FarmWallAndBreach(){var a=Create();Move(a,-2,0,1f);Require(a.Battle.Players[1].PositionX<=-.89f,"wall crossed on long step");Move(a,-2,-14,1f);Require(a.Battle.Players[1].PositionX>1,"approved breach blocked");}
 private static void DockWater(){var a=Create(21);Move(a,11,5,1f);Require(a.Battle.Players[1].PositionX<=11.46f,"crossed water to remote platform");}
 private static void FarmPortal(){var a=Create();a.Battle.AwaitingExit=true;a.Battle.WaveIndex=3;Call("TickRoundReward",a,a.Battle,.01f);Require(a.Battle.StagePortal!=null&&Math.Abs(a.Battle.StagePortal.X)<.001f&&Math.Abs(a.Battle.StagePortal.Z-14)<.001f,"portal inside centre cross wall");}
 private static void LegacyBounds(){var a=Create(2);Move(a,20,10,.1f);Require(Math.Abs(a.Battle.Players[1].PositionX-18.95f)<.001f,"legacy boundary changed");}
 private static void OneTimedEntry(){var a=Create(21);a.Battle.NextSpawnAt=0;Require((bool)Call("TrySpawnEnemy",a.Battle,0L),"first spawn refused");var first=a.Battle.Enemies.Values.Single();a.Battle.NextSpawnAt=0;Require((bool)Call("TrySpawnEnemy",a.Battle,0L),"second spawn refused");var second=a.Battle.Enemies.Values.OrderBy(p=>p.RuntimeId).Last();Require(first.PositionX==second.PositionX&&first.PositionZ==second.PositionZ,"same active interval changed entry by spawn budget");}
 private static void LegacySequence(){var a=Create();a.Record.IsCasualRoute=false;a.Record.NextArena=21;a.Record.ArenaConfigNumber=0;var b=OnlineArcadeRunActorSystem.PrepareBattle(a.Record,false);Require(b.ArenaConfigNumber==1,"legacy sequential cycle entered new casual map21");}
 private static void LegacyCompletionUsesActualStage()
 {
  var parent=Path.GetFullPath("Tools/ArcadeFullRouteOnlineChecks/.test-output");var folder=Path.GetFullPath(Path.Combine(parent,Guid.NewGuid().ToString("N")));
  if(!folder.StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("test directory escaped");Directory.CreateDirectory(folder);
  try
  {
   var a=Create();a.Record.IsCasualRoute=false;a.Record.NextArena=21;a.Record.ArenaConfigNumber=0;a.Record.Phase=OnlineArcadeRunPhase.SafeCheckpoint;a.Record.CreatedAtUtc=a.Record.UpdatedAtUtc=DateTime.UtcNow.ToString("O");a.Battle=OnlineArcadeRunActorSystem.PrepareBattle(a.Record,false);
   a.Battle.WaveIndex=3;a.Battle.AwaitingExit=true;a.Battle.ArenaCompletionReady=true;a.CommittedRecord=OnlineArcadeRunManagerSystem.Clone(a.Record);typeof(Entity).GetProperty("InstanceId")!.SetValue(a,1L);
   var manager=new OnlineArcadeRunManagerComponent{StorePath=Path.Combine(folder,"runs.json")};manager.Runs[1]=a;
   var method=typeof(OnlineArcadeRunManagerSystem).GetMethod("TryCommitArenaCompletion",BindingFlags.Static|BindingFlags.NonPublic)!;
   Require((bool)method.Invoke(null,new object[]{manager,a})!,"legacy completion save rejected");int expected=(int)MathF.Round(12*(1+20*ServerConfig.Instance.ArcadeRule.GoldGrowthPerArena));Require(a.Record.Players.All(p=>p.RunGold==expected),"legacy arena1 clear paid using unrelated map21");
  }
  finally{Directory.Delete(folder,true);}
 }
 private static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,args)!;
 private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
