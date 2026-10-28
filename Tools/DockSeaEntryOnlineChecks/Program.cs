using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;
using MemoryPack;

// 必须显式匹配真实短类名和最大数量；无效过滤直接退出，不回退其他检查。
var options=args.Select(a=>a.Split('=',2)).ToDictionary(a=>a[0],a=>a.Length==2?a[1]:string.Empty);
if(options.GetValueOrDefault("testClass")!="DockSeaEntryOnline" ||
   options.GetValueOrDefault("includePassingTests")!="true" || options.GetValueOrDefault("maxTests")!="9")
{Console.Error.WriteLine("Requires testClass=DockSeaEntryOnline includePassingTests=true maxTests=9");return 2;}
World.Instance.AddSingleton(ServerConfig.CreateForValidation("OnlineLocal",n=>File.ReadAllBytes(Path.Combine("Server/GameServer/Config/Luban",n+".bytes"))));
World.Instance.AddSingleton<TimeInfo>();World.Instance.AddSingleton<ObjectPool>();
return DockSeaEntryOnlineChecks.Run();

/// <summary>从真实双人战斗/出生/伤害/快照入口检查右侧进场，不读取玩家存档或启动监听。</summary>
internal static class DockSeaEntryOnlineChecks
{
    public static int Run()
    {
        var checks=new (string Name,Action Check)[]{
            ("FourRoundsRightEntryAndLanes",RightEntry),("OtherPassagesKeepNormalSpawn",OtherPassages),
            ("ActivePathCannotAttack",Path),("StunHerdAndStormFreezeAge",ControlFreeze),
            ("DisconnectLoadingAndTransitionFreeze",BattleFreeze),("FinishFrameThenResumePursuit",Finish),
            ("CrowdingAndPushPreservePathButDamage",Push),("SnapshotCloneAndCodecKeepTerminalState",Snapshot),
            ("CarrierArmorDropAndExplosionRemain",Carrier)};
        int failures=0;
        foreach(var (name,check) in checks)
            try{check();Console.WriteLine("PASS DockSeaEntryOnline."+name);}
            catch(Exception ex){failures++;Console.WriteLine("FAIL DockSeaEntryOnline."+name+": "+(ex.InnerException??ex));}
        Console.WriteLine($"{checks.Length-failures}/{checks.Length} passed");return failures==0?0:1;
    }
    private static (OnlineArcadeRunActor Actor,OnlineArcadeEnemyRuntime Enemy) Spawn(int passage=2,int sequence=0,int round=1)
    {
        var rule=ServerConfig.Instance.ArcadeRule;
        var record=new OnlineArcadeRunRecord{RunId=9155,RoomId=2,Revision=1,NextArena=2,ArenaConfigNumber=21,RoundNumber=round,IsCasualRoute=true,CasualRouteVersion=1,ConfigFingerprint=rule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
        var actor=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};var b=actor.Battle;
        foreach(var p in b.Players.Values){p.ReadyRoundGeneration=b.RoundGeneration;p.PositionX=0;p.PositionZ=-4.1f;p.CurrentHealth=p.MaxHealth=100;}
        b.SpawnQueue.Clear();b.SpawnQueue.Add("enemy.arcade.basic");b.SpawnQueueIndex=0;b.SpawnedInWave=sequence;b.NextSpawnAt=0;
        Require((bool)Call("SpawnQueuedEnemy",b,10000L,passage),"real queued spawn failed");
        var enemy=b.Enemies.Values.Single();enemy.CurrentHealth=enemy.MaxHealth=1000;return(actor,enemy);
    }
    // 反射只兼容实施前的 RED；断言消费真实生成敌人和协议快照，不根据源码文本判断。
    private static object State(OnlineArcadeEnemyRuntime e)=>typeof(OnlineArcadeEnemyRuntime).GetField("DockSeaEntry")?.GetValue(e)??throw new InvalidOperationException("real spawned enemy lacks dock sea-entry state");
    private static T Read<T>(OnlineArcadeEnemyRuntime e,string field)=>(T)State(e).GetType().GetField(field)!.GetValue(State(e))!;
    private static T Property<T>(OnlineArcadeEnemyInfo e,string name)=>(T)(e.GetType().GetProperty(name)?.GetValue(e)??throw new InvalidOperationException("snapshot lost "+name));
    private static void Tick(OnlineArcadeRunActor a,float seconds)=>Call("TickEnemies",a,a.Battle,seconds);
    private static void RightEntry()
    {
        for(int round=1;round<=4;round++)for(int seq=0;seq<3;seq++)
        {var(a,e)=Spawn(2,seq,round);Require(Read<bool>(e,"Started"),"right route missing entry");Near(e.PositionX,18.3f,"sea start");Near(e.PositionZ,new[]{-3f,-4.1f,-5.2f}[seq],"lane");Require(e.StageEnemy.Phase==0,"entry reused hidden/invulnerable phase");Require(a.Battle.SpawnQueueIndex==1&&a.Battle.SpawnedInWave==seq+1,"entry changed queue budget");}
    }
    private static void OtherPassages()
    {
        foreach(int passage in new[]{0,1}){var(a,e)=Spawn(passage);Require(!Read<bool>(e,"Started"),"other passage entered sea path");Near(e.PositionX,a.Battle.RoundLayout.Entries[passage].X,"existing spawn X");Near(e.PositionZ,a.Battle.RoundLayout.Entries[passage].Y,"existing spawn Z");}
        var(a2,e2)=Spawn(-1,2);Require(Read<bool>(e2,"Started"),"fallback third passage missed sea path");
    }
    private static void Path()
    {
        var(a,e)=Spawn();e.AttackCooldownSeconds=0;e.AttackRange=100;e.MoveSpeed=100;
        Tick(a,2.1f);Near(e.PositionX,12.55f,"grab edge");Near(Read<float>(e,"Age"),2.1f,"active age");Near(e.PositionZ,-3,"fixed lane");
        Tick(a,3.65f);Near(e.PositionX,11.8f,"climb complete");Require(a.Battle.Players.Values.All(p=>p.CurrentHealth==100)&&e.AttackSequence==0&&a.Battle.Projectiles.Count==0,"entry attacked or shot before standing");
    }
    private static void ControlFreeze()
    {
        var(a,e)=Spawn();e.SpecialKind=0;Tick(a,.3f);float x=e.PositionX;
        e.ContentStunUntil=10;Tick(a,.7f);Near(Read<float>(e,"Age"),.3f,"stun age");Near(e.PositionX,x,"stun position");e.ContentStunUntil=0;
        e.HerdImpact=new ArcadeHerdImpactState{Remaining=1};Tick(a,.7f);Near(Read<float>(e,"Age"),.3f,"herd age");e.HerdImpact=default;
        a.Battle.SlayerMartRulesVersion=1;a.Battle.SlayerMart=new(){Players=new(){new(){PlayerId=1,StormRemaining=10},new(){PlayerId=2}}};
        a.Battle.Players[1].PositionX=e.PositionX;a.Battle.Players[1].PositionZ=e.PositionZ;
        Require((float)Call("SlayerMartStormSpeed",a.Battle,e)==0,"fixture does not apply real full storm freeze");Tick(a,.7f);Near(Read<float>(e,"Age"),.3f,"storm age");
        a.Battle.SlayerMart.Players[0].StormRemaining=0;Tick(a,.2f);Near(Read<float>(e,"Age"),.5f,"resumed age");
    }
    private static void BattleFreeze()
    {
        var(a,e)=Spawn();var b=a.Battle;b.LastTickAt=10000;a.DisconnectedAtByPlayer[1]=10000;a.TickBattle(10100);Near(Read<float>(e,"Age"),0,"disconnect");a.DisconnectedAtByPlayer.Clear();
        b.Players[1].ReadyRoundGeneration=-1;a.TickBattle(10200);Near(Read<float>(e,"Age"),0,"loading");b.Players[1].ReadyRoundGeneration=b.RoundGeneration;
        b.RoundTransitionPending=true;a.TickBattle(10300);Near(Read<float>(e,"Age"),0,"transition");b.RoundTransitionPending=false;a.TickBattle(10400);Near(Read<float>(e,"Age"),.1f,"active battle");
    }
    private static void Finish()
    {
        var(a,e)=Spawn();e.SpecialKind=0;e.MoveSpeed=3;e.AttackCooldownSeconds=0;e.AttackRange=100;Tick(a,6.75f);
        Near(e.PositionX,10.9f,"standing X");Near(Read<float>(e,"Age"),6.75f,"terminal age");Require(Read<bool>(e,"Started")&&e.AttackSequence==0&&a.Battle.Players[1].CurrentHealth==100,"finish frame attacked or forgot terminal identity");
        e.AttackRange=.1f;Tick(a,.1f);Require(e.PositionX<10.9f,"next frame did not resume pursuit");
        foreach(var p in a.Battle.Players.Values){p.PositionX=e.PositionX;p.PositionZ=e.PositionZ;}e.MoveSpeed=0;
        Tick(a,.01f);Tick(a,e.MeleeWindupSeconds+.01f);Require(e.AttackSequence==1&&a.Battle.Players.Values.Any(p=>p.CurrentHealth<100),"landed enemy never resumed real melee");
    }
    private static void Push()
    {
        foreach(float age in new[]{0f,2.1f,3.4f,5.74f})
        {
            var(waterActor,waterEnemy)=Spawn();waterEnemy.SpecialKind=0;waterEnemy.ArmorHealth=20;Tick(waterActor,age);
            Call("DamageEnemy",waterActor.Battle,waterEnemy,10);
            Call("DamageEnemyFromFlame",waterActor.Battle,waterEnemy,10);
            Require(waterEnemy.CurrentHealth==1000&&waterEnemy.ArmorHealth==20&&waterEnemy.FlameHitRemainingSeconds==0,"water/climbing enemy accepted damage");
        }
        var(a,e)=Spawn();var b=a.Battle;e.SpecialKind=0;Tick(a,5.75f);float x=e.PositionX,z=e.PositionZ;
        var other=new OnlineArcadeEnemyRuntime{RuntimeId=999,PositionX=x+.01f,PositionZ=z,CurrentHealth=1000};b.Enemies.Add(999,other);b.EnemyIterationIds.Add(999);
        Call("ResolveEnemyCrowding",b,ServerConfig.Instance.ArcadeRule,Call("EffectiveObstacles",b));Near(e.PositionX,x,"crowding X");Near(e.PositionZ,z,"crowding Z");
        var shot=new OnlineArcadeProjectileRuntime{OwnerPlayerId=1,FromPlayerWeapon=true,SourceWeaponId="arcade.weapon.spread",WeaponPowerLevel=2,SourcePositionX=x-3,SourcePositionZ=z,VelocityX=1,EvolutionPushedEnemyIds=new()};
        Call("PushPurpleShotgun",b,shot,x,z);Near(e.PositionX,x,"shotgun X");Require(!shot.EvolutionPushedEnemyIds.Contains(e.RuntimeId),"entry consumed volley push identity");
        b.EnemyIterationIds.Clear();b.EnemyIterationIds.Add(e.RuntimeId);Call("DamageAndKnockbackEnemies",a,b,x-1,z,2f,1,2f,1L);
        Near(e.PositionX,x,"knockback X");Require(e.CurrentHealth==999,"entry lost existing damage");
        var config=ServerConfig.Instance.SlayerMart;b.SlayerMartRulesVersion=1;b.SlayerMart=new(){RegionInstanceId="9155:slayermart:1",Visit=1,Players=new(){new(){PlayerId=1},new(){PlayerId=2}}};
        b.SlayerMart.PendingGrants.Add(new(){EventId="9155:slayermart:1:1",PlayerId=1,Effect=SlayerMartEffect.TeslaPack,Amount=3,
            Duration=config.Catalog.Items.Single(p=>p.Effect==SlayerMartEffect.TeslaPack).Duration});
        Require(ArcadeSlayerMartSupportRules.TryApplyGrants(b.SlayerMart,config,new Dictionary<long,int>(),out var candidate,out var error),error);b.SlayerMart=candidate;
        b.Players[1].PositionX=x-config.Parameter("support.orbitRadius");b.Players[1].PositionZ=z;int before=e.CurrentHealth;
        Call("TickSlayerMartSupports",a,b,0f);Near(e.PositionX,x,"orb push X");Near(e.PositionZ,z,"orb push Z");Require(e.CurrentHealth<before,"entry lost real orb contact damage");
    }
    private static void Snapshot()
    {
        var(a,e)=Spawn();Tick(a,2.1f);
        using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);
        var original=snapshot.Enemies.Single();original.PositionX=-999;
        original.GetType().GetProperty("DockSeaEntryAge")?.SetValue(original,99f);Tick(a,4.65f);
        using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));var item=decoded!.Enemies.Single();
        Require(Property<bool>(item,"DockSeaEntryStarted"),"codec lost source identity");Near(Property<float>(item,"DockSeaEntryAge"),2.1f,"clone age independence");Near(Property<float>(item,"DockSeaEntryLane"),-3,"codec lane");
        Near(item.PositionX,12.55f,"deep clone position");
        using var terminal=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);using var terminalClone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",terminal);
        using var terminalDecoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(terminalClone));
        Require(Property<bool>(terminalDecoded!.Enemies.Single(),"DockSeaEntryStarted"),"terminal identity reset");Near(Property<float>(terminalDecoded.Enemies.Single(),"DockSeaEntryAge"),6.75f,"terminal codec age");
    }
    private static void Carrier()
    {
        var(a,e)=Spawn(2,1);var b=a.Battle;Require(e.SpecialKind==1,"real right route did not assign carrier");int hp=e.CurrentHealth;
        int barrel=e.CarriedBarrelHealth;Shot(a,e,1);Require(e.CurrentHealth==hp&&e.CarriedBarrelHealth==barrel&&b.Projectiles.ContainsKey(900),"water enemy intercepted a bullet or lost barrel health");
        var(rocketActor,rocketEnemy)=Spawn();Shot(rocketActor,rocketEnemy,10,"arcade.weapon.rocket",2);
        Require(rocketActor.Battle.Projectiles.ContainsKey(900)&&rocketActor.Battle.Projectiles[900].TrackingEnemyId==0&&rocketEnemy.CurrentHealth==1000,"purple rocket exploded on or tracked water enemy");
        e.ContentStunUntil=100;Tick(a,1);b.ContentClock.Advance(5.01f,false);Call("TickVortexBarrels",b,.01f);
        Require(Read<float>(e,"Age")==0&&e.SpecialKind==0&&b.DroppedBarrels.Count==1&&b.GroundBarrelHealth.Values.All(h=>h==b.RoundLayout.BarrelHealth),"frozen entry paused five-second unload or changed barrel health");
        var(a2,e2)=Spawn(2,1);foreach(var p in a2.Battle.Players.Values){p.PositionX=e2.PositionX;p.PositionZ=e2.PositionZ;}
        Tick(a2,ArcadeDockSeaEntryRules.ClimbSeconds);Shot(a2,e2,10000);Require(e2.SpecialKind==0&&a2.Battle.DeathZones.Count(z=>z.Kind==14)==1&&a2.Battle.Players.Values.All(p=>p.CurrentHealth==100),"landed barrel blast altered player safety or once-only rule");
    }
    private static void Shot(OnlineArcadeRunActor actor,OnlineArcadeEnemyRuntime enemy,int damage,string weapon="",int level=0)
    {
        // 正常活动帧先经TickEnemies生成迭代身份；此处单独调用弹丸入口时补齐这一真实宿主前置条件。
        actor.Battle.EnemyIterationIds.Clear();actor.Battle.EnemyIterationIds.AddRange(actor.Battle.Enemies.Keys);
        actor.Battle.Projectiles.Add(900,new OnlineArcadeProjectileRuntime{RuntimeId=900,OwnerPlayerId=1,FromPlayerWeapon=true,
            SourceWeaponId=weapon,WeaponPowerLevel=level,TrackingEnemyId=enemy.RuntimeId,
            PositionX=enemy.PositionX-1,PositionZ=enemy.PositionZ,VelocityX=1,RemainingSeconds=3,RemainingHits=1,Damage=damage});
        Call("TickProjectiles",actor,actor.Battle,1f);
    }
    private static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!.Invoke(null,args)!;
    private static void Near(float actual,float expected,string label)=>Require(Math.Abs(actual-expected)<.002f,$"{label}: expected {expected}, got {actual}");
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
