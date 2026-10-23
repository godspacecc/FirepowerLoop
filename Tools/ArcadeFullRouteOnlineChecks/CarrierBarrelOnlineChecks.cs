using System.Reflection;
using ET;
using ET.Server;

/// <summary>真实双人权威命中检查：背桶优先、爆炸安全、卸桶耐久及范围武器。</summary>
internal static class CarrierBarrelOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks=new (string Name,Action Check)[]{("WeakShotHitsBarrelFirst",Weak), ("BreakOnceAndProtectBothPlayers",Break), ("DamagedDropKeepsHealth",Drop), ("ExplosionAndRayHitBarrelFirst",Area), ("OrdinaryEnemyStillTakesDamage",Ordinary)};
        if(maximum!=checks.Length)throw new ArgumentException("CarrierBarrelOnline requires maxTests=5");int failed=0;
        foreach(var check in checks)try{check.Check();Console.WriteLine("PASS CarrierBarrelOnline."+check.Name);}catch(Exception e){failed++;Console.WriteLine("FAIL CarrierBarrelOnline."+check.Name+": "+(e.InnerException??e));}
        Console.WriteLine($"{checks.Length-failed}/{checks.Length} passed");return failed==0?0:1;
    }
    private static (OnlineArcadeRunActor Actor,OnlineArcadeEnemyRuntime Enemy) Ready()
    {
        var record=new OnlineArcadeRunRecord{RunId=71,RoomId=72,Revision=1,NextArena=2,ArenaConfigNumber=21,RoundNumber=1,IsCasualRoute=true,ConfigFingerprint=ServerConfig.Instance.ArcadeRule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=ServerConfig.Instance.ArcadeRule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=ServerConfig.Instance.ArcadeRule.BaseWeaponId}}};
        var actor=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};var b=actor.Battle;
        b.Enemies.Clear();b.EnemyIterationIds.Clear();b.DroppedBarrels.Clear();b.Projectiles.Clear();
        foreach(var player in b.Players.Values){player.ReadyRoundGeneration=b.RoundGeneration;player.PositionX=0;player.PositionZ=0;}
        var enemy=new OnlineArcadeEnemyRuntime{RuntimeId=700,CurrentHealth=1000,MaxHealth=1000,SpecialKind=1};b.Enemies.Add(700,enemy);b.EnemyIterationIds.Add(700);return(actor,enemy);
    }
    private static void Shot(OnlineArcadeRunActor actor,int damage)
    {
        var b=actor.Battle;var p=new OnlineArcadeProjectileRuntime{RuntimeId=900,OwnerPlayerId=1,FromPlayerWeapon=true,PositionX=-1,PositionZ=0,VelocityX=1,VelocityZ=0,RemainingSeconds=3,RemainingHits=1,Damage=damage};
        b.Projectiles.Add(900,p);Call("TickProjectiles",actor,b,1f);
    }
    private static void Weak(){var(a,e)=Ready();Shot(a,1);Require(e.CurrentHealth==1000&&e.SpecialKind==1&&e.CarriedBarrelHealth==a.Battle.RoundLayout.BarrelHealth-1,"shot bypassed barrel");}
    private static void Break()
    {
        var(a,e)=Ready();Shot(a,10000);int hp=e.CurrentHealth;
        Require(e.SpecialKind==0&&hp>0&&hp<1000,"barrel did not explode independently");
        Require(a.Battle.DeathZones.Count(z=>z.Kind==14)==1,"barrel blast missing or duplicated");
        Shot(a,1);Require(e.CurrentHealth==hp-1&&a.Battle.DeathZones.Count(z=>z.Kind==14)==1,"body hit respawned barrel blast");
        Require(a.Battle.Players.Values.All(p=>p.CurrentHealth==p.MaxHealth),"barrel harmed a player");
    }
    private static void Drop(){var(a,e)=Ready();Shot(a,1);a.Battle.ContentClock.Advance(6,false);Call("TickVortexBarrels",a.Battle,.01f);int id=a.Battle.DroppedBarrels.Single().Key;Require(e.SpecialKind==0&&a.Battle.GroundBarrelHealth[id]==a.Battle.RoundLayout.BarrelHealth-1,"unload repaired damaged barrel");}
    private static void Area()
    {
        var(a,e)=Ready();Call("DamageEnemiesInExplosionRadius",a,a.Battle,0f,0f,2f,1,1L);Require(e.CurrentHealth==1000&&e.CarriedBarrelHealth==a.Battle.RoundLayout.BarrelHealth-1,"blast bypassed barrel");
        Call("HitPurpleRay",a,a.Battle,new OnlineArcadeProjectileRuntime{OwnerPlayerId=1,Damage=1},e);Require(e.CurrentHealth==1000&&e.CarriedBarrelHealth==a.Battle.RoundLayout.BarrelHealth-2,"ray bypassed barrel");
    }
    private static void Ordinary(){var(a,e)=Ready();e.SpecialKind=0;Shot(a,1);Require(e.CurrentHealth==999,"ordinary enemy gained barrel armor");}
    private static object Call(string key,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(key,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!.Invoke(null,args)!;
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
