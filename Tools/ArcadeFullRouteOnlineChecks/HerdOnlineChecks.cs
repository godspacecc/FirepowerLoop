using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;
using MemoryPack;
internal static class HerdOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks=new (string,Action)[]{("SingleFileAndSpeed",OneRow),("TailClearsBeforeNextBatch",TailClears),("SweepAndSingleDeath",Sweep),("Protections",Protections),("ClearAndLoadingFreeze",Freeze),("FreshRound",FreshRound),("SnapshotCloneAndCodec",Snapshot),("LethalMiddleCowKeepsRow",Lethal),
            ("ImpactFlightAndGroundHold",ImpactFlight),("ImpactBlocksActions",ImpactActions),("ImpactSnapshotCodec",ImpactSnapshot),("CowDeathDirectionAndKind",ImpactDeath),("SurvivingEnemyImpact",LivingImpact),
            ("SideLaunchClearsWholeQueue",SideQueue),("SideLaunchChoosesInsideAtEdges",SideEdges)};
        if(maximum<checks.Length)throw new ArgumentException("HerdOnline maximum too small");int failed=0;
        foreach(var(name,test) in checks)try{test();Console.WriteLine("PASS HerdOnline."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL HerdOnline."+name+": "+(e.InnerException??e).Message);}
        Console.WriteLine($"{checks.Length-failed}/{checks.Length} passed");return failed==0?0:1;
    }
    private static OnlineArcadeRunActor Create()
    {
        var rule=ServerConfig.Instance.ArcadeRule;
        var record=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,NextArena=3,ArenaConfigNumber=22,IsCasualRoute=true,ConfigFingerprint=rule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
        var actor=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};
        foreach(var p in actor.Battle.Players.Values){p.PositionX=-26;p.PositionZ=-14;p.CurrentHealth=p.MaxHealth=100;p.ReadyRoundGeneration=actor.Battle.RoundGeneration;}
        return actor;
    }
    private static List<ArcadeHerdState> Cows(OnlineArcadeRunActor a)=>typeof(OnlineArcadeBattleRuntime).GetField("HerdCows")?.GetValue(a.Battle) as List<ArcadeHerdState>??new();
    private static void Tick(OnlineArcadeRunActor a,float seconds)=>Call("TickRoundContent",a,a.Battle,seconds);
    private static void OneRow(){var a=Create();Tick(a,8);Require(Cows(a).Count==5,"missing cow batch");Require(Cows(a).Select(c=>c.X).Distinct().Count()==5&&Cows(a).Select(c=>c.Z).Distinct().Count()==1,"not a single file");Tick(a,.4f);Require(a.Battle.Players.Values.All(p=>p.CurrentHealth==100),"windup hurt player");float start=Cows(a)[0].X;Tick(a,1.4f);Require(Math.Abs(Cows(a)[0].X-start-ServerConfig.Instance.ArcadeRule.PlayerMoveSpeed*1.1f)<.001f,"cow speed is not 1.1 times player speed");}
    private static void Sweep(){var a=Create();Tick(a,8);a.Battle.Enemies[12]=new OnlineArcadeEnemyRuntime{RuntimeId=12,PositionX=-26,PositionZ=-14,CurrentHealth=10,MaxHealth=10};Tick(a,2);Require(a.Battle.Players.Values.All(p=>p.CurrentHealth==75),"two players did not share authoritative sweep");Require(!a.Battle.Enemies.ContainsKey(12)&&a.Battle.EnemyDeaths.Count==1&&a.Battle.Drops.Count==0,"environment death settlement wrong");foreach(var p in a.Battle.Players.Values)p.InvulnerabilitySeconds=0;Tick(a,.01f);Require(a.Battle.EnemyDeaths.Count==1&&a.Battle.Players.Values.All(p=>p.CurrentHealth==75),"duplicate contact");}
    private static void Protections(){for(int mode=1;mode<=3;mode++){var a=Create();Tick(a,8);Require(Cows(a).Count==5,"missing cow batch");var p=a.Battle.Players[1];if(mode==1)p.InvulnerabilitySeconds=1;if(mode==2)p.DashSeconds=1;if(mode==3)p.Powerups[ArcadePowerupBehavior.Shield]=new OnlineArcadePowerupRuntime{Behavior=ArcadePowerupBehavior.Shield,Charges=1,RemainingSeconds=10};Tick(a,2);Require(p.CurrentHealth==100,"protection bypassed");if(mode==3)Require(!p.Powerups.ContainsKey(ArcadePowerupBehavior.Shield),"shield not consumed");}}
    private static void Freeze(){var a=Create();a.Battle.Players[2].ReadyRoundGeneration=0;Tick(a,8);Require(Cows(a).Count==0,"loading advanced herd");a.Battle.Players[2].ReadyRoundGeneration=a.Battle.RoundGeneration;Tick(a,8);Require(Cows(a).Count==5,"ready cow batch missing");a.Battle.AwaitingExit=true;Tick(a,5);Require(Cows(a).All(c=>c.ElapsedSeconds==0),"clear advanced charge");}
    private static void FreshRound(){var a=Create();Tick(a,8);Require(Cows(a).Count==5,"missing cow batch");a.Record.RoundNumber=2;a.Record.RoundGeneration=a.Battle.RoundGeneration;a.Battle=OnlineArcadeRunActorSystem.PrepareBattle(a.Record,false);Require(Cows(a).Count==0,"half charge persisted");}
    /// <summary>双人权威循环也必须让尾牛完整离场，之后才生成反向纵队。</summary>
    private static void TailClears(){var a=Create();Tick(a,8);foreach(var p in a.Battle.Players.Values){p.PositionX=0;p.PositionZ=0;}var old=Cows(a).ToArray();Tick(a,11.8f);Require(Cows(a).Count==0&&old.All(c=>c.X>30.5f),"tail recycled inside map");Tick(a,6.3f);Require(Cows(a).Count==5&&Cows(a).Select((c,i)=>Math.Abs(c.X-33-i*3)<.001f&&c.Z==-14&&c.DirectionX==-1).All(v=>v),"reverse batch is not a single file");}
    private static void Lethal(){var a=Create();Tick(a,8);a.Battle.Players[1].CurrentHealth=1;Tick(a,2);Require(a.Battle.Players[1].RespawnSeconds>0,"lethal contact did not enter actual respawn");Require(Cows(a).Select((c,i)=>Math.Abs(c.X-Cows(a)[0].X+i*3)<.001f&&c.Z==-14).All(v=>v),"lethal contact split single file");}
    private static void Snapshot(){var a=Create();Tick(a,8);using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);var prop=snapshot.GetType().GetProperty("Herds");Require(prop!=null,"herd protocol absent");using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));var items=((System.Collections.IEnumerable)prop.GetValue(decoded)!).Cast<object>().ToArray();Require(items.Length==5,"clone/codec lost herd");var first=items[0];Require((long)first.GetType().GetProperty("MapInstanceId")!.GetValue(first)! ==a.Battle.RoundGeneration,"map identity lost");}
    private static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,args)!;
    /// <summary>与单人相同的3米飞行，落地完整0.5秒及恢复后解锁。</summary>
    private static void ImpactFlight()
    {
        var a=Create();Tick(a,8);Tick(a,2);var p=a.Battle.Players[1];float start=p.PositionX,startZ=p.PositionZ;
        Require(Math.Abs(p.HerdImpact.Remaining-1.7f)<.001,"no authoritative launch");
        Call("TickHerdImpacts",a.Battle,.475f);Require(Math.Abs(p.PositionX-start-.6f)<.001,"half side flight wrong");
        ArcadeHerdImpactRules.Sample(p.HerdImpact.Remaining,false,out float height,out _);Require(height>2.4,"body did not leave cow back");
        Call("TickHerdImpacts",a.Battle,.475f);float landed=p.PositionX;Call("TickHerdImpacts",a.Battle,.49f);
        Require(Math.Abs(landed-start-1.2f)<.001&&Math.Abs(p.PositionZ-startZ-3*Math.Sqrt(.84))<.001&&p.PositionX==landed&&p.HerdImpact.Remaining>.25,"side flight or grounded half-second hold missing");
        Call("TickHerdImpacts",a.Battle,.26f);Require(!p.HerdImpact.Active,"recovery never ended");
        ArcadeHerdImpactRules.Begin(ref p.HerdImpact,1,0);a.Battle.WaveClearAwarded=true;
        Call("TickPlayers",a,a.Battle,1.7f,TimeInfo.Instance.ServerNow());Require(!p.HerdImpact.Active,"clear phase locked player before exit");
    }
    private static void ImpactActions()
    {
        var a=Create();Tick(a,8);Tick(a,2);var p=a.Battle.Players[1];var record=a.Record.Players[0];record.Bombs=record.Dashes=2;p.AimX=1;p.Firing=true;p.MoveZ=1;
        Require(!(bool)Call("Fire",a,a.Battle,p,record),"fired in flight");Call("UseBomb",a,a.Battle,p);Call("UseDash",a,p);
        Require(record.Bombs==2&&record.Dashes==2&&p.DashSeconds==0,"used action while knocked down");
        float x=p.PositionX,z=p.PositionZ;Call("TickPlayers",a,a.Battle,.1f,TimeInfo.Instance.ServerNow());Require(p.PositionX==x&&p.PositionZ==z,"input moved player during impact");
    }
    private static void ImpactSnapshot()
    {
        var a=Create();Tick(a,8);Tick(a,2);var enemy=new OnlineArcadeEnemyRuntime{RuntimeId=999,CurrentHealth=10,MaxHealth=10};ArcadeHerdImpactRules.Begin(ref enemy.HerdImpact,-1,0);a.Battle.Enemies.Add(999,enemy);
        Call("TickHerdImpacts",a.Battle,1.1f);using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);
        using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));
        Require(Math.Abs(decoded.Players[0].HerdImpactRemaining-.6f)<.001&&Math.Abs(decoded.Players[0].HerdImpactDirectionX-.4f)<.001&&Math.Abs(decoded.Players[0].HerdImpactDirectionZ-Math.Sqrt(.84))<.001,"player side impact lost in clone/codec");
        Require(Math.Abs(decoded.Enemies.Single(e=>e.RuntimeId==999).HerdImpactRemaining-.6f)<.001&&decoded.Enemies.Single(e=>e.RuntimeId==999).HerdImpactDirectionX==-1,"enemy impact lost in clone/codec");
    }
    private static void ImpactDeath()
    {
        var a=Create();Tick(a,8);a.Battle.Enemies[77]=new OnlineArcadeEnemyRuntime{RuntimeId=77,PositionX=-26,PositionZ=-14,CurrentHealth=10,MaxHealth=10};Tick(a,2);
        var death=a.Battle.EnemyDeaths.Single();Require(death.ImpactKind==ArcadeDeathImpactRules.Herd&&!death.Explosive&&death.DirectionX==1&&Math.Abs(death.DirectionZ)<.001,"cow corpse is not launched in cow direction");
    }
    private static void LivingImpact()
    {
        var a=Create();Tick(a,8);a.Battle.Enemies[77]=new OnlineArcadeEnemyRuntime{RuntimeId=77,PositionX=-26,PositionZ=-14,CurrentHealth=10,MaxHealth=10,SpecialKind=3};Tick(a,2);
        Require(a.Battle.Enemies.TryGetValue(77,out var e)&&e.HerdImpact.Active,"survivor not launched");float x=e!.PositionX,z=e.PositionZ;
        Call("TickEnemies",a,a.Battle,.1f);Require(e.PositionX==x&&e.PositionZ==z&&!a.Battle.Projectiles.Values.Any(p=>p.OwnerPlayerId==0),"survivor chased or attacked during flight");
    }
    /// <summary>主动撤掉接触保护，验证双人各自离开完整纵队通路而非追加免伤。</summary>
    private static void PassSideQueue(OnlineArcadeRunActor a,int direction,float lane,float offset,float x=0)
    {
        Tick(a,8);var cows=Cows(a);
        for(int i=0;i<cows.Count;i++){cows[i].X=x+(-7-3*i)*direction;cows[i].Z=lane;cows[i].DirectionX=direction;}
        foreach(var p in a.Battle.Players.Values){p.PositionX=x;p.PositionZ=lane+offset;}
        var obstacles=Call("EffectiveObstacles",a.Battle);
        Require(!(bool)Call("InsideObstacle",x,lane+offset,ServerConfig.Instance.ArcadeRule.PlayerRadius,obstacles),"invalid wall-overlapping fixture start");
        bool hit=false;
        for(int i=0;i<120;i++)
        {
            foreach(var p in a.Battle.Players.Values)p.InvulnerabilitySeconds=0;
            Tick(a,.05f);hit|=a.Battle.Players.Values.Any(p=>p.HerdImpact.Active);
            Require(a.Battle.Players.Values.All(p=>p.CurrentHealth>=75),$"following cow hit again at {i*.05f:F2}s lane={lane}: "+string.Join(";",a.Battle.Players.Values.Select(p=>$"hp={p.CurrentHealth},pos=({p.PositionX},{p.PositionZ}),dir=({p.HerdImpact.DirectionX},{p.HerdImpact.DirectionZ}),remaining={p.HerdImpact.Remaining}")));
        }
        Require(hit&&a.Battle.Players.Values.All(p=>p.CurrentHealth==75&&!p.HerdImpact.Active&&Math.Abs(p.PositionZ-lane)>2.7),"player did not recover outside queue");
    }
    private static void SideQueue()
    {
        foreach(var sample in new[]{(1,0f),(-1,0f),(1,.6f),(1,-.6f)})PassSideQueue(Create(),sample.Item1,-14,sample.Item2);
    }
    private static void SideEdges()
    {
        float half=ServerConfig.Instance.EarlyStages[22].HalfHeight;
        foreach(int edge in new[]{1,-1})
        {
            var a=Create();float lane=edge*(half-.6f);PassSideQueue(a,1,lane,0,2.2f);
            Require(a.Battle.Players.Values.All(p=>p.PositionZ*edge<lane*edge&&Math.Abs(p.PositionZ)<half),"edge launched out of arena");
        }
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
