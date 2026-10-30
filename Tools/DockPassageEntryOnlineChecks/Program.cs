using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

// 必须显式匹配真实短类名和最大数量；无效过滤直接退出，不回退其他检查。
var options=args.Select(a=>a.Split('=',2)).ToDictionary(a=>a[0],a=>a.Length==2?a[1]:string.Empty);
if(options.GetValueOrDefault("testClass")!="DockPassageEntryOnline" ||
   options.GetValueOrDefault("includePassingTests")!="true" || options.GetValueOrDefault("maxTests")!="9")
{Console.Error.WriteLine("Requires testClass=DockPassageEntryOnline includePassingTests=true maxTests=9");return 2;}
World.Instance.AddSingleton(ServerConfig.CreateForValidation("OnlineLocal",n=>File.ReadAllBytes(Path.Combine("Server/GameServer/Config/Luban",n+".bytes"))));
World.Instance.AddSingleton<TimeInfo>();World.Instance.AddSingleton<ObjectPool>();
return DockPassageEntryOnlineChecks.Run();

/// <summary>从真实双人战斗、出生、伤害与移动入口检查左桥和上通道入场，不读取玩家存档或启动监听。</summary>
internal static class DockPassageEntryOnlineChecks
{
    public static int Run()
    {
        var checks=new (string Name,Action Check)[]{
            ("BothPassagesImmune",Immune),("BulletAndEvolvedPassThrough",Projectiles),
            ("CarrierAndArmorPreserved",Carrier),("WalkThenVulnerableOnlyOnce",Finish),
            ("RangedCannotAttackAtEntrance",Ranged),("FreezeAndResume",Freeze),
            ("PushAndCrowdingCannotMoveEntrance",Push),("SeaRouteStillProtected",Sea),("BombRespectsEntryProtection",Bomb)};
        int failures=0;
        foreach(var (name,check) in checks)
            try{check();Console.WriteLine("PASS DockPassageEntryOnline."+name);}
            catch(Exception ex){failures++;Console.WriteLine("FAIL DockPassageEntryOnline."+name+": "+(ex.InnerException??ex));}
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
        Require((bool)Call("SpawnQueuedEnemy",b,10000L,passage,null),"real queued spawn failed");
        var enemy=b.Enemies.Values.Single();enemy.CurrentHealth=enemy.MaxHealth=1000;return(actor,enemy);
    }
    private static void Tick(OnlineArcadeRunActor a,float seconds)=>Call("TickEnemies",a,a.Battle,seconds);
    private static void Immune()
    {
        foreach(int passage in new[]{0,1})for(int round=1;round<=4;round++)
        {var(a,e)=Spawn(passage,0,round);e.SpecialKind=0;e.ArmorHealth=20;
         Call("DamageEnemy",a.Battle,e,10);Call("DamageEnemyFromFlame",a.Battle,e,10);
         Require(e.CurrentHealth==1000&&e.ArmorHealth==20&&e.FlameHitRemainingSeconds==0,"entrance accepted damage");}
    }
    private static void Projectiles()
    {
        foreach(int passage in new[]{0,1})foreach(var weapon in new[]{"","arcade.weapon.rocket","arcade.weapon.ray","arcade.weapon.spread"})
        {var(a,e)=Spawn(passage);Shot(a,e,10,weapon,weapon==""?0:2);
         Require(e.CurrentHealth==1000&&a.Battle.Projectiles.ContainsKey(900),"entrance intercepted "+weapon);}
    }
    private static void Carrier()
    {
        foreach(int passage in new[]{0,1})
        {var(a,e)=Spawn(passage,1);e.SpecialKind=1;e.CarriedBarrelHealth=100;e.ArmorHealth=20;
         Shot(a,e,1000);Require(e.CarriedBarrelHealth==100&&e.CurrentHealth==1000&&e.ArmorHealth==20&&e.SpecialKind==1,"entrance lost barrel or armor");}
    }
    private static void Finish()
    {
        foreach(int passage in new[]{0,1})
        {var(a,e)=Spawn(passage);e.SpecialKind=0;e.MoveSpeed=3;float x=e.PositionX,z=e.PositionZ;
         Tick(a,.1f);Require(Math.Abs(e.PositionX-x)+Math.Abs(e.PositionZ-z)>.01f,"did not walk from entrance");
         Call("DamageEnemy",a.Battle,e,1);Require(e.CurrentHealth==1000,"early walk vulnerable");
         for(int i=0;i<100&&e.CurrentHealth==1000;i++){Tick(a,.1f);Call("DamageEnemy",a.Battle,e,1);}
         Require(e.CurrentHealth==999,"never became vulnerable on deck");
         float r=ServerConfig.Instance.ArcadeRule.EnemyContactRadius;
         Require(e.PositionX>=-14+r-.002f&&e.PositionX<=12-r+.002f&&e.PositionZ>=-11.5f+r-.002f&&e.PositionZ<=11.5f-r+.002f,"unlocked outside main deck");
         e.PositionX=x;e.PositionZ=z;Call("DamageEnemy",a.Battle,e,1);Require(e.CurrentHealth==998,"returned to entrance and regained immunity");}
    }
    private static void Ranged()
    {
        foreach(int passage in new[]{0,1})
        {var(a,e)=Spawn(passage);e.MoveSpeed=3;e.AttackRange=100;e.AttackCooldownSeconds=0;e.SpecialKind=0;e.AttackKind=EnemyAttackKind.Projectile;e.ProjectileConfigId=ServerConfig.Instance.Tables.TbEnemy.DataList.First(c=>c.AttackKind==EnemyAttackKind.Projectile).ProjectileConfigId;
         Tick(a,.1f);Require(a.Battle.Projectiles.Count==0&&e.AttackSequence==0&&a.Battle.Players.Values.All(p=>p.CurrentHealth==100),"entrance attacked");
         Tick(a,100f);Require(a.Battle.Projectiles.Count==0&&e.AttackSequence==0,"finish frame attacked");
         Tick(a,.1f);Require(a.Battle.Projectiles.Count==1,"landed ranged enemy never resumed firing");}
    }
    private static void Freeze()
    {
        foreach(int passage in new[]{0,1})
        {var(a,e)=Spawn(passage);e.SpecialKind=0;float x=e.PositionX,z=e.PositionZ;
         foreach(float delta in new[]{0f,-1f,float.NaN,float.PositiveInfinity,float.NegativeInfinity})
         {Call("TickDockPassageEntry",a.Battle,e,delta);Near(e.PositionX,x,"invalid delta X");Near(e.PositionZ,z,"invalid delta Z");Require(e.DockPassageEntering,"invalid delta ended entry");}
         e.ContentStunUntil=100;Tick(a,1);Near(e.PositionX,x,"frozen X");Near(e.PositionZ,z,"frozen Z");
         Call("DamageEnemy",a.Battle,e,1);Require(e.CurrentHealth==1000,"frozen entry lost immunity");
         e.ContentStunUntil=0;e.HerdImpact=new ArcadeHerdImpactState{Remaining=1,DirectionX=1};
         Tick(a,.1f);Call("TickHerdImpacts",a.Battle,.1f);Near(e.PositionX,x,"herd X");Near(e.PositionZ,z,"herd Z");e.HerdImpact=default;
         a.Battle.SlayerMartRulesVersion=1;a.Battle.SlayerMart=new(){Players=new(){new(){PlayerId=1,StormRemaining=10},new(){PlayerId=2}}};
         a.Battle.Players[1].PositionX=x;a.Battle.Players[1].PositionZ=z;
         Require((float)Call("SlayerMartStormSpeed",a.Battle,e)==0,"storm fixture lacks full freeze");
         Tick(a,.1f);Near(e.PositionX,x,"storm X");Near(e.PositionZ,z,"storm Z");a.Battle.SlayerMart.Players[0].StormRemaining=0;
         Tick(a,.1f);Require(Math.Abs(e.PositionX-x)+Math.Abs(e.PositionZ-z)>.01f,"did not resume");}
    }
    private static void Push()
    {
        foreach(int passage in new[]{0,1})
        {var(a,e)=Spawn(passage);e.SpecialKind=0;float x=e.PositionX,z=e.PositionZ;var b=a.Battle;
         b.EnemyIterationIds.Add(e.RuntimeId);Call("DamageAndKnockbackEnemies",a,b,x-1,z,2f,10,2f,1L);
         Near(e.PositionX,x,"knockback X");Near(e.PositionZ,z,"knockback Z");Require(e.CurrentHealth==1000,"knockback damage");
         var shot=new OnlineArcadeProjectileRuntime{OwnerPlayerId=1,FromPlayerWeapon=true,SourceWeaponId="arcade.weapon.spread",WeaponPowerLevel=2,SourcePositionX=x-3,SourcePositionZ=z,VelocityX=1,EvolutionPushedEnemyIds=new()};
         Call("PushPurpleShotgun",b,shot,x,z);Near(e.PositionX,x,"shotgun X");Near(e.PositionZ,z,"shotgun Z");
         b.Enemies.Add(999,new OnlineArcadeEnemyRuntime{RuntimeId=999,PositionX=x+.01f,PositionZ=z,CurrentHealth=1000});b.EnemyIterationIds.Add(999);
         Call("ResolveEnemyCrowding",b,ServerConfig.Instance.ArcadeRule,Call("EffectiveObstacles",b));Near(e.PositionX,x,"crowding X");Near(e.PositionZ,z,"crowding Z");}
    }
    /// <summary>真实炸弹只清除已入场普通敌人，三条尚在入场的通道共存时也保持保护。</summary>
    private static void Bomb()
    {
        var(a,left)=Spawn(0);var b=a.Battle;
        var protectedIds=new List<long>{left.RuntimeId};
        foreach(int passage in new[]{1,2,0})
        {
            b.SpawnQueue.Add("enemy.arcade.basic");
            Require((bool)Call("SpawnQueuedEnemy",b,10000L,passage,null),"bomb fixture real spawn failed");
            var latest=b.Enemies.Values.OrderByDescending(e=>e.RuntimeId).First();latest.SpecialKind=0;
            if(passage!=0)protectedIds.Add(latest.RuntimeId);
            else
            {
                Call("TickDockPassageEntry",b,latest,100f);
                Require(latest.CanBeAttacked,"bomb fixture enemy never entered deck");
            }
        }
        var landed=b.Enemies.Values.Single(e=>!protectedIds.Contains(e.RuntimeId));
        a.Record.Players[0].Bombs=2;
        Call("UseBomb",a,b,b.Players[1]);
        Require(protectedIds.All(id=>b.Enemies.ContainsKey(id)),"bomb killed enemy before entry completed");
        Require(!b.Enemies.ContainsKey(landed.RuntimeId)&&b.Enemies.Count==3,"bomb did not clear only the landed enemy");
        Require(a.Record.Players[0].Bombs==1&&b.BombBlasts.Count==1,"bomb was not consumed exactly once");
    }
    private static void Sea()
    {
        var(a,e)=Spawn(2);Call("DamageEnemy",a.Battle,e,10);Require(e.CurrentHealth==1000,"water vulnerable");
        Tick(a,5.75f);Call("DamageEnemy",a.Battle,e,10);Require(e.CurrentHealth==990,"climbed sea enemy lost existing vulnerability");
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
