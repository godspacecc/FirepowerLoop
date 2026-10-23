using System.Reflection;
using ET;
using ET.Server;

/// <summary>新三关敌人从真实服务器出生、伤害及击杀入口检查，不使用替代伤害桩。</summary>
internal static class NextEnemyOnlineChecks
{
    /// <summary>阶段推力门禁单独执行，使用真实出生/蓝影转换和正式紫霰弹入口。</summary>
    public static int RunPush()
    {
        try{PurplePush();Console.WriteLine("PASS NextStagePushOnline.HiddenStagesRejectShotgunPush");Console.WriteLine("1/1 passed");return 0;}
        catch(Exception e){Console.WriteLine("FAIL NextStagePushOnline.HiddenStagesRejectShotgunPush: "+(e.InnerException??e).Message);Console.WriteLine("0/1 passed");return 1;}
    }
    private static void PurplePush()
    {
        foreach(int phase in new[]{1,2,3})
        {
            var(a,e)=Create(phase==1?23:24);var b=a.Battle;
            if(phase==3){Call("TickEnemies",a,b,1.26f);Call("DamageEnemy",b,e,e.MaxHealth);Call("KillEnemy",a,b,e,false);}
            Require(Read<int>(e,"Phase")==phase,"actual stage initialization mismatch");e.PositionX=0;e.PositionZ=-6;
            var shot=new OnlineArcadeProjectileRuntime{OwnerPlayerId=1,FromPlayerWeapon=true,SourceWeaponId="arcade.weapon.spread",WeaponPowerLevel=2,
                SourcePositionX=-3,SourcePositionZ=-6,VelocityX=1,EvolutionPushedEnemyIds=new HashSet<long>()};
            Call("PushPurpleShotgun",b,shot,0f,-6f);
            Require(e.PositionX==0&&e.PositionZ==-6,"purple shotgun moved hidden stage "+phase);
            Require(!shot.EvolutionPushedEnemyIds.Contains(e.RuntimeId),"hidden target consumed volley push identity");
            e.StageEnemy.Phase=0;Call("PushPurpleShotgun",b,shot,0f,-6f);Require(e.PositionX>0,"active enemy lost existing shotgun push");
        }
    }
    /// <summary>独立单项入口，不扩大既有十二项检查的执行范围。</summary>
    public static int RunAttack()
    {
        try{AttackSequence();Console.WriteLine("PASS NextStageAttackOnline.AuthoritativeMeleeSequence");Console.WriteLine("1/1 passed");return 0;}
        catch(Exception e){Console.WriteLine("FAIL NextStageAttackOnline.AuthoritativeMeleeSequence: "+(e.InnerException??e).Message);Console.WriteLine("0/1 passed");return 1;}
    }
    private static int Sequence(OnlineArcadeEnemyRuntime enemy)=>(int)(typeof(OnlineArcadeEnemyRuntime).GetField("AttackSequence")?.GetValue(enemy)??0);
    private static void AttackSequence()
    {
        var(a,e)=Create(24);var b=a.Battle;e.PositionX=0;e.PositionZ=-6;e.MoveSpeed=0;e.AttackCooldownSeconds=0;
        Call("TickEnemies",a,b,.5f);Require(Sequence(e)==0&&b.Players[1].CurrentHealth==100,"hidden crow advanced melee sequence");
        Call("TickEnemies",a,b,.76f);Require(Sequence(e)==0,"arrival completion attacked");
        Call("TickEnemies",a,b,.01f);Call("TickEnemies",a,b,e.MeleeWindupSeconds+.01f);
        Require(b.Players[1].CurrentHealth<100,"real melee swing did not complete");Require(Sequence(e)==1,"completed authoritative melee did not advance attack sequence");
        Call("TickEnemies",a,b,.01f);Require(Sequence(e)==1,"cooldown replayed melee sequence");
        using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",b);using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);
        using var decoded=MemoryPack.MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPack.MemoryPackSerializer.Serialize(clone));
        var item=decoded!.Enemies.Single();Require((int)(item.GetType().GetProperty("AttackSequence")?.GetValue(item)??0)==1,"snapshot clone or generated codec lost attack sequence");
        Call("DamageEnemy",b,e,e.MaxHealth);Call("KillEnemy",a,b,e,false);Call("TickEnemies",a,b,.5f);
        Require(Read<int>(e,"Phase")==3&&Sequence(e)==1,"hidden blue travel advanced attack sequence");
    }
    public static int Run(int maximum)
    {
        var tests=new (string,Action)[]{("GraveBornAndImmune",GraveBorn),("GraveSlotsAreUnique",GraveSlots),("CrowCannotAttackBeforeLanding",CrowLanding),("ShadowFirstKillHasNoReward",FirstKill),("ShadowIgnoresRepeatedKill",RepeatedKill),("ShadowSecondKillEndsEnemy",SecondKill),("HazardCleanupBypassesRevival",Hazard),("HazardFirstKillStillRevives",HazardRevive),("LoadingAndDisconnectFreezeAge",Freeze),("SnapshotCloneRetainsStage",Snapshot),("ActualElectricKillRevivesShadow",Electric),("BlockedTombsPreserveQueue",Blocked)};
        if(maximum<tests.Length)throw new ArgumentException("NextEnemyOnline maximum too small");int failed=0;
        foreach(var(name,run)in tests)try{run();Console.WriteLine("PASS NextEnemyOnline."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL NextEnemyOnline."+name+": "+(e.InnerException??e).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static (OnlineArcadeRunActor,OnlineArcadeEnemyRuntime) Create(int map)
    {
        var rule=ServerConfig.Instance.ArcadeRule;var record=new OnlineArcadeRunRecord{RunId=9153,RoomId=2,Revision=1,NextArena=map==23?4:5,ArenaConfigNumber=map,RoundNumber=1,IsCasualRoute=true,CasualRouteVersion=1,ConfigFingerprint=rule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
        var actor=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};var b=actor.Battle;
        foreach(var p in b.Players.Values){p.ReadyRoundGeneration=b.RoundGeneration;p.PositionX=0;p.PositionZ=-6;p.CurrentHealth=p.MaxHealth=100;}
        b.SpawnQueue.Clear();b.SpawnQueue.Add("enemy.arcade.basic");b.SpawnQueueIndex=0;b.SpawnedInWave=(int)Math.Ceiling(1/b.RoundLayout.SpecialRatio)-1;
        Require((bool)Call("SpawnQueuedEnemy",b,10000L,0),"real spawn did not deliver special enemy");return(actor,b.Enemies.Values.Single());
    }
    private static object State(OnlineArcadeEnemyRuntime e)=>typeof(OnlineArcadeEnemyRuntime).GetField("StageEnemy")?.GetValue(e)??throw new InvalidOperationException("stage enemy state missing");
    private static T Read<T>(OnlineArcadeEnemyRuntime e,string name)=>(T)State(e).GetType().GetField(name)!.GetValue(State(e))!;
    private static void GraveBorn(){var(a,e)=Create(23);Require(e.SpecialKind==18&&Read<int>(e,"Phase")==1,"grave did not start emergence");Require(Math.Abs(e.PositionZ-(Read<float>(e,"OriginY")-FirepowerLoop.Arcade.ArcadeNextStageEnemyRules.GraveLandingOffset+FirepowerLoop.Arcade.ArcadeNextStageEnemyRules.GraveOriginOffset))<.001,"grave landing not outside full foundation");Safe(a,e);Immune(a,e);var x=e.PositionX;var z=e.PositionZ;a.Battle.EnemyIterationIds.Clear();a.Battle.EnemyIterationIds.Add(e.RuntimeId);Call("DamageAndKnockbackEnemies",a,a.Battle,x-1,z,5f,1000,2f,1L);Require(e.PositionX==x&&e.PositionZ==z&&e.CurrentHealth==e.MaxHealth,"hidden grave displaced by actual knockback");Call("TickEnemies",a,a.Battle,2f);Require(e.PositionX==x&&e.PositionZ==z&&Read<int>(e,"Phase")==1,"grave moved before rising");Call("TickEnemies",a,a.Battle,.11f);Require(Read<int>(e,"Phase")==0,"grave never became active");Call("DamageEnemy",a.Battle,e,1);Require(e.CurrentHealth==e.MaxHealth-1,"landed grave remains immune");}
    private static void GraveSlots(){var(a,e)=Create(23);var b=a.Battle;var origins=new HashSet<string>{Read<float>(e,"OriginX")+","+Read<float>(e,"OriginY")};for(int i=1;i<8;i++){b.SpawnQueue.Add("enemy.arcade.basic");b.SpawnedInWave=1;Require((bool)Call("SpawnQueuedEnemy",b,10000L,0),"safe tomb unexpectedly unavailable");var next=b.Enemies.Values.OrderBy(v=>v.RuntimeId).Last();Require(origins.Add(Read<float>(next,"OriginX")+","+Read<float>(next,"OriginY")),"occupied grave reused");Safe(a,next);}}
    private static void CrowLanding(){var(a,e)=Create(24);Require(Read<int>(e,"Phase")==2,"crow missing landing phase");e.PositionX=0;e.PositionZ=-6;e.AttackCooldownSeconds=0;Immune(a,e);Call("TickEnemies",a,a.Battle,1.2f);Require(a.Battle.Players[1].CurrentHealth==100&&Read<int>(e,"Phase")==2,"crow attacked during arrival");Call("TickEnemies",a,a.Battle,.06f);Require(Read<int>(e,"Phase")==0&&a.Battle.Players[1].CurrentHealth==100,"arrival completion attacked");}
    private static void FirstKill(){var(a,e)=Landed();var b=a.Battle;Call("DamageEnemy",b,e,e.MaxHealth);Call("KillEnemy",a,b,e,false);Require(b.Enemies.ContainsKey(e.RuntimeId)&&e.CurrentHealth==e.MaxHealth&&Read<int>(e,"Phase")==3&&Read<int>(e,"RevivesRemaining")==0,"first shadow kill did not consume single revival");Require(b.EnemyDeaths.Count==0&&b.Drops.Count==0&&a.Record.Players.All(p=>p.Score==0),"first shadow death awarded kill side effects");Safe(a,e);Immune(a,e);}
    private static void RepeatedKill(){var(a,e)=Landed();Call("DamageEnemy",a.Battle,e,e.MaxHealth);Call("KillEnemy",a,a.Battle,e,false);var x=e.PositionX;var z=e.PositionZ;Call("KillEnemy",a,a.Battle,e,false);Require(a.Battle.Enemies.ContainsKey(e.RuntimeId)&&Read<int>(e,"Phase")==3&&e.PositionX==x&&e.PositionZ==z,"repeated hidden kill consumed entity");}
    private static void SecondKill(){var(a,e)=Landed();Call("DamageEnemy",a.Battle,e,e.MaxHealth);Call("KillEnemy",a,a.Battle,e,false);Call("TickEnemies",a,a.Battle,1.36f);Call("DamageEnemy",a.Battle,e,e.MaxHealth);Call("KillEnemy",a,a.Battle,e,false);Require(!a.Battle.Enemies.ContainsKey(e.RuntimeId)&&a.Battle.EnemyDeaths.Count==1,"second shadow defeat did not use normal death");}
    private static void Hazard(){var(a,e)=Create(24);Call("RemoveEnemyForCleanup",a,a.Battle,e);Require(!a.Battle.Enemies.ContainsKey(e.RuntimeId)&&a.Battle.Drops.Count==0,"hazard cleanup was revived");}
    private static void HazardRevive(){var(a,e)=Landed();Call("DamageEnemy",a.Battle,e,e.MaxHealth);Call("KillEnemy",a,a.Battle,e,true);Require(a.Battle.Enemies.ContainsKey(e.RuntimeId)&&Read<int>(e,"Phase")==3,"actual lethal mechanism bypassed revival");}
    private static void Freeze(){var(a,e)=Create(23);a.Battle.LastTickAt=10000;a.DisconnectedAtByPlayer[1]=10000;a.TickBattle(10100);Require(Read<float>(e,"Age")==0,"disconnect advanced emergence");a.DisconnectedAtByPlayer.Clear();a.Battle.Players[1].ReadyRoundGeneration=-1;a.TickBattle(10200);Require(Read<float>(e,"Age")==0,"loading advanced emergence");a.Battle.Players[1].ReadyRoundGeneration=a.Battle.RoundGeneration;a.TickBattle(10300);Require(Read<float>(e,"Age")>.09f,"active battle did not advance emergence");}
    private static (OnlineArcadeRunActor,OnlineArcadeEnemyRuntime) Landed(){var pair=Create(24);Call("TickEnemies",pair.Item1,pair.Item1.Battle,1.26f);Require(Read<int>(pair.Item2,"Phase")==0,"crow failed to land");return pair;}
    private static void Immune(OnlineArcadeRunActor a,OnlineArcadeEnemyRuntime e){var hp=e.CurrentHealth;Call("DamageEnemy",a.Battle,e,e.MaxHealth*2);Require(e.CurrentHealth==hp,"real DamageEnemy damaged hidden enemy");}
    private static void Safe(OnlineArcadeRunActor a,OnlineArcadeEnemyRuntime e){var b=a.Battle;var obstacles=ServerConfig.Instance.Tables.TbArcadeRoundLayout.Get(b.RoundLayout.Id).Obstacles;Require(!(bool)Call("InsideObstacle",e.PositionX,e.PositionZ,ServerConfig.Instance.ArcadeRule.EnemyContactRadius,obstacles),"landing inside obstacle");Require(b.Players.Values.All(p=>!p.Alive||Math.Pow(e.PositionX-p.PositionX,2)+Math.Pow(e.PositionZ-p.PositionZ,2)>=16),"landing too near player");}
    private static void Snapshot()
    {
        var(a,e)=Create(23);Call("TickEnemies",a,a.Battle,.4f);
        using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);
        using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);
        using var decoded=MemoryPack.MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPack.MemoryPackSerializer.Serialize(clone));
        var enemy=decoded!.Enemies.Single();Require(enemy.StageEnemyPhase==1&&Math.Abs(enemy.StageEnemyAge-.4f)<.001f&&enemy.StageEnemyOriginX==Read<float>(e,"OriginX")&&enemy.StageEnemyOriginZ==Read<float>(e,"OriginY"),"generated battle codec or clone lost emergence phase");
        var(shadow,sh)=Landed();Call("DamageEnemy",shadow.Battle,sh,sh.MaxHealth);Call("KillEnemy",shadow,shadow.Battle,sh,false);
        using var snap2=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",shadow.Battle);using var clone2=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snap2);
        using var decoded2=MemoryPack.MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPack.MemoryPackSerializer.Serialize(clone2));
        Require(decoded2!.Enemies.Single().StageEnemyPhase==3&&decoded2.Enemies.Single().StageEnemyRevives==0,"snapshot replenished consumed revival");
    }
    private static void Electric()
    {
        var(a,e)=Landed();var record=OnlineArcadeRunManagerSystem.Clone(a.Record);record.ArenaConfigNumber=21;record.NextArena=2;
        var source=OnlineArcadeRunActorSystem.PrepareBattle(record,false);var hazard=source.Hazards.Single();var p=hazard.Parameters;
        a.Battle.Hazards.Add(hazard);hazard.Advance(5.6f);a.Battle.HazardElapsedSeconds=5.6f;
        foreach(var player in a.Battle.Players.Values){player.PositionX=player.HazardPreviousX=-10;player.PositionZ=player.HazardPreviousZ=-10;}
        e.PositionX=e.HazardPreviousX=p.X;e.PositionZ=e.HazardPreviousZ=p.Y;
        Call("TickHazards",a,a.Battle,.1f);Require(a.Battle.Enemies.ContainsKey(e.RuntimeId)&&Read<int>(e,"Phase")==3&&e.CurrentHealth==e.MaxHealth,"real electric hazard bypassed first revival");
        var age=Read<float>(e,"Age");Call("TickHazards",a,a.Battle,.1f);Require(e.CurrentHealth==e.MaxHealth&&Read<float>(e,"Age")==age,"hidden shadow damaged by same electric contact");
    }
    private static void Blocked()
    {
        var(a,e)=Create(23);var b=a.Battle;
        // 占满所有通过实体半径校验的墓位，保持真实队列不向前消费。
        while(true){b.SpawnQueue.Add("enemy.arcade.basic");b.SpawnedInWave=1;if(!(bool)Call("SpawnQueuedEnemy",b,10000L,0))break;Require(b.Enemies.Count<=16,"grave reservation was unbounded");}
        var index=b.SpawnQueueIndex;var total=b.SpawnedInWave;Call("TrySpawnEnemy",b,20000L);Call("TrySpawnEnemy",b,30000L);
        Require(b.SpawnQueueIndex==index&&b.SpawnedInWave==total,"blocked tomb consumed queue budget");
        b.Enemies.Remove(e.RuntimeId);Call("TrySpawnEnemy",b,31000L);
        Require(b.SpawnQueueIndex>index,"freeing tomb did not resume existing queue");
    }
    private static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args)!;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
