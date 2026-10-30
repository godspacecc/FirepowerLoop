using ET;
using ET.Server;
using FirepowerLoop.Arcade;
using System.Reflection;

// 五项有限的空间接触与同帧继续回归，加载真实服务器配置。
internal static class Program
{
    // 非空类过滤、固定最大数量和通过项输出。
    private static int Main(string[] args)
    {
        if(args.Length!=3||args[0]!="testClass=ArcadeSpatialContinuationChecks"||args[1]!="maxTests=5"||args[2]!="includePassingTests=true")return 2;
        World.Instance.AddSingleton(ServerConfig.CreateForValidation("OnlineLocal",n=>File.ReadAllBytes(Path.Combine("Server/GameServer/Config/Luban",n+".bytes"))));
        World.Instance.AddSingleton<TimeInfo>();World.Instance.AddSingleton<ObjectPool>();
        void Require(bool v,string m){if(!v)throw new Exception(m);}
        object Call(string name,params object[] values)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,values)!;
        OnlineArcadeRunActor Battle()
        {
            var rule=ServerConfig.Instance.ArcadeRule;
            var record=new OnlineArcadeRunRecord{RunId=999,RoomId=1,NextArena=2,ArenaConfigNumber=21,RoundNumber=1,IsCasualRoute=true,CasualRouteVersion=1,ConfigFingerprint=rule.ConfigFingerprint,Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
            var actor=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};
            var b=actor.Battle;b.Enemies.Clear();b.Projectiles.Clear();b.Crates.Clear();b.DroppedBarrels.Clear();
            b.Enemies.Add(1,new(){RuntimeId=1,EnemyConfigId="enemy.arcade.zombie.shambler",PositionX=0,PositionZ=0,CurrentHealth=1000,MaxHealth=1000,DockSeaEntry=new(){Started=true,Age=6.5f,Direction=2,Lane=-3}});
            b.EnemyIterationIds.Clear();b.EnemyIterationIds.Add(1);return actor;
        }
        void Continuation(bool two)
        {
            var actor=Battle();var b=actor.Battle;
            if(two){b.Enemies.Add(2,new(){RuntimeId=2,EnemyConfigId="enemy.arcade.zombie.shambler",PositionX=1,CurrentHealth=1000,MaxHealth=1000,DockSeaEntry=new(){Started=true,Age=6.5f,Direction=2,Lane=-3}});b.EnemyIterationIds.Add(2);}
            var shot=new OnlineArcadeProjectileRuntime{RuntimeId=900,OwnerPlayerId=1,PositionX=-2,VelocityX=4,RemainingSeconds=3,RemainingHits=5,Damage=10,Behavior=ArcadeWeaponBehavior.Piercing};b.Projectiles.Add(900,shot);
            Call("TickProjectiles",actor,b,1f);
            Require(b.Enemies[1].CurrentHealth==990,"First exposed body missed");
            if(two)Require(b.Enemies[2].CurrentHealth==990,"Remaining segment failed to hit second body");
            Require(b.Projectiles.ContainsKey(900)&&Math.Abs(shot.PositionX-2)<1e-5&&Math.Abs(shot.RemainingSeconds-2)<1e-5,"Contact discarded remaining frame travel or altered lifetime");
        }
        var checks=new(string,Action)[]{
            ("PiercingRetainsRemainingTravel",()=>Continuation(false)),
            ("PiercingHitsSecondBodySameFrame",()=>Continuation(true)),
            ("BurningValidatedBodyPreservesTicks",()=>{var tuning=ServerConfig.Instance.WeaponUpgrade.Evolution;var areas=new[]{new ArcadeWeaponBurnArea{OwnerId=1,X=0,Z=0,Damage=10,RemainingSeconds=5}};float contact=0;Require(ArcadeWeaponEvolutionRules.BurningDamage(areas,1,100,100,ref contact,tuning.FlameGroundTickSeconds,tuning)==0,"Legacy circle unexpectedly overlaps");Require(ArcadeWeaponEvolutionRules.BurningDamage(areas,1,100,100,ref contact,tuning.FlameGroundTickSeconds,tuning,true)==10,"Validated area rechecked root circle");}),
            ("HerdValidatedContactStillDeduplicates",()=>{var cow=new ArcadeHerdState{MapInstanceId=1,Id=2};typeof(ArcadeHerdState).GetField("StepVersion",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(cow,1L);var sweep=new ArcadeHerdSweep(1,2,1,0,0,1,0,.5f);Require(!ArcadeHerdRules.TryContact(cow,sweep,1,8,100,100,.5f,true),"Legacy root unexpectedly overlaps");Require(ArcadeHerdRules.TryContact(cow,sweep,1,8,100,100,.5f,true,true),"Validated hit rejected");Require(!ArcadeHerdRules.TryContact(cow,sweep,1,8,100,100,.5f,true,true),"Contact counted twice");}),
            ("SupportValidatedContactStillCoolsDown",()=>{var config=ServerConfig.Instance.SlayerMart;var owner=new ArcadeSlayerMartPlayer{PlayerId=1};var item=ArcadeSlayerMartSupportRules.OrdinaryBody(owner,SlayerMartSupportKind.Saw,10);Require(!ArcadeSlayerMartSupportRules.TryContact(owner,item,"e",100,100,.5f,0,0,config.Supports,config.Parameter),"Legacy root unexpectedly overlaps");Require(ArcadeSlayerMartSupportRules.TryContact(owner,item,"e",100,100,.5f,0,0,config.Supports,config.Parameter,true),"Validated contact rejected");Require(!ArcadeSlayerMartSupportRules.TryContact(owner,item,"e",100,100,.5f,0,0,config.Supports,config.Parameter,true),"Cooldown lost");})
        };
        int failed=0;foreach(var(name,check)in checks)try{check();Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex);}
        Console.WriteLine($"ArcadeSpatialContinuationChecks: {5-failed}/5 passed");return failed==0?0:1;
    }
}
