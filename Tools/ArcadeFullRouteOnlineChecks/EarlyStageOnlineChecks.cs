using System.Reflection;
using ET;
using ET.Server;
internal static class EarlyStageOnlineChecks
{
 public static int Run(int maximum)
 {
  var checks=new (string,Action)[]{("FarmOuterYard",FarmOuterYard),("FarmWallAndBreach",FarmWallAndBreach),("DockWater",DockWater),("FarmPortal",FarmPortal),("LegacyBounds",LegacyBounds),("OneTimedEntry",OneTimedEntry),("LegacySequence",LegacySequence),("LegacyCompletionUsesActualStage",LegacyCompletionUsesActualStage),("FirstTwelveRoundsThroughActualExits",FirstTwelveRounds),("DockWeaponTimer",DockWeaponTimer),("PreviousVersionOldThemes",PreviousVersions),("UnknownVersionRefused",UnknownVersion)};
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
 private static void DockWeaponTimer(){foreach(int map in new[]{21,2}){var a=Create(map);a.Battle.ArenaNumber=2;a.Battle.WaveIndex=3;a.Battle.SpawnedInWave=0;var p=a.Battle.Players[1];p.TemporaryWeaponId="arcade.weapon.rapid";p.TemporaryWeaponSeconds=10;Call("TickPlayers",a,a.Battle,.1f,1000L);Require(Math.Abs(p.TemporaryWeaponSeconds-(map==21?9.9f:10f))<.001,"dock inherited armory hold or legacy hold lost");}}
 private static void Normalize(OnlineArcadeRunRecord record)=>typeof(OnlineArcadeRunManagerSystem).GetMethod("NormalizeLoadedCheckpoint",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{record});
 private static void PreviousVersions()
 {
  foreach(string version in new[]{"firepowerloop-20261022-v20-purple-ray","firepowerloop-20261022-v21-early-stage-data"})foreach(int map in new[]{2,3,4,15})
  {
   var a=Create(map);a.Record.RoundNumber=2;var layout=ServerConfig.Instance.Tables.TbArcadeRoundLayout.Get($"arena.{map}.round.2");a.Record.RoundLayoutId=layout.Id;a.Record.RoundLayoutVersion=layout.Version;
   a.Record.ConfigFingerprint=version;Normalize(a.Record);var battle=OnlineArcadeRunActorSystem.PrepareBattle(a.Record,false);
   Require(battle.ArenaConfigNumber==map&&battle.WaveIndex==1&&a.Record.ArenaConfigNumber==map,"old theme identity replaced by new route");
  }
 }
 private static void UnknownVersion()
 {var a=Create(2);a.Record.ConfigFingerprint="firepowerloop-unknown-version";Normalize(a.Record);bool rejected=false;try{OnlineArcadeRunActorSystem.PrepareBattle(a.Record,false);}catch(RpcException){rejected=true;}Require(rejected&&a.Record.ConfigFingerprint=="firepowerloop-unknown-version","unknown config version accepted or rewritten");}
 private static void OneTimedEntry(){var a=Create(21);a.Battle.NextSpawnAt=0;Require((bool)Call("TrySpawnEnemy",a.Battle,1000L),"first spawn refused");var first=a.Battle.Enemies.Values.Single();Require(!(bool)Call("TrySpawnEnemy",a.Battle,1000L),"same passage burst without interval");Require((bool)Call("TrySpawnEnemy",a.Battle,2000L),"second spawn refused");var second=a.Battle.Enemies.Values.OrderBy(p=>p.RuntimeId).Last();Require(first.PositionX==second.PositionX&&first.PositionZ==second.PositionZ,"passage changed before half batch");}
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
 private static void FirstTwelveRounds()
 {
  string folder=Path.Combine(Path.GetTempPath(),"FirepowerEarlyOnline-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  try
  {
   var a=Create(1);a.Record.NextArena=1;a.Record.CompletedArena=0;a.Record.CreatedAtUtc=a.Record.UpdatedAtUtc=DateTime.UtcNow.ToString("O");a.Record.Phase=OnlineArcadeRunPhase.SafeCheckpoint;
   a.CommittedRecord=OnlineArcadeRunManagerSystem.Clone(a.Record);typeof(Entity).GetProperty("InstanceId")!.SetValue(a,1L);
   var manager=new OnlineArcadeRunManagerComponent{StorePath=Path.Combine(folder,"runs.json")};manager.Runs[1]=a;
   bool Commit(string name)=>(bool)typeof(OnlineArcadeRunManagerSystem).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{manager,a})!;
   int[] maps={1,21,22};int rounds=0;
   foreach(int map in maps)
   {
    foreach(var p in a.Record.Players)p.Ready=true;
    a.Battle=OnlineArcadeRunActorSystem.PrepareBattle(a.Record,false);Require(a.Battle.ArenaConfigNumber==map,"wrong next map");
    for(int round=0;round<4;round++)
    {
     var b=a.Battle;foreach(var p in b.Players.Values)p.ReadyRoundGeneration=b.RoundGeneration;
     Call("TickTreasureRoom",a,b,0f);Require(b.TreasureRoom==null,"new docks inherited old second-stage armory");
     b.SpawnQueueIndex=b.SpawnQueue.Count;b.Enemies.Clear();Call("TickTreasureRoom",a,b,0f);Call("TryAdvanceWave",a,b,0f);rounds++;
     Require(b.RoundReward!=null,"missing one chest per round");Require(a.Record.Players.All(p=>p.RunGold==0),"new early map auto-granted cash");
     var player=b.Players[1];int next=map==1?21:map==21?22:5;
     if(round<3)
     {
      Require(b.StagePortal==null,"ordinary round exposed stage portal");var stage=ServerConfig.Instance.EarlyStages.GetValueOrDefault(map);var point=b.RoundLayout.GetExitPosition(0,stage?.HalfWidth??19.5f,stage?.HalfHeight??19.5f,.55f);
      player.PositionX=point.X;player.PositionZ=point.Y;player.ConfirmedExitTarget=map;Require((bool)Call("TickRoundExit",b)&&b.RoundTransitionPending,"physical round exit rejected");Require(Commit("TryCommitRoundTransition"),"round checkpoint save failed");
      Require(a.Battle.WaveIndex==round+1,"wrong restored next round");
     }
     else
     {
      foreach(var p in b.Players.Values){p.PositionX=b.RoundLayout.SpawnX;p.PositionZ=b.RoundLayout.SpawnY;}
      Call("TickRoundReward",a,b,1f);Require(b.StagePortal!=null,"fourth clear missing portal");player.PositionX=b.StagePortal.X;player.PositionZ=b.StagePortal.Z;player.ConfirmedExitTarget=next;
      Require((bool)Call("TickRoundExit",b)&&b.ArenaCompletionReady,"stage portal exit rejected");Require(Commit("TryCommitArenaCompletion"),"stage completion save failed");Require(a.Record.ArenaConfigNumber==next,"wrong route after stage clear");
     }
    }
   }
   Require(rounds==12&&a.Record.CompletedArena==3&&a.Record.ArenaConfigNumber==5,"new first twelve rounds did not return to main line");
  }
  finally{Directory.Delete(folder,true);}
 }
 private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
