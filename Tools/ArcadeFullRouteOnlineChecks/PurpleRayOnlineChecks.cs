using System.Reflection;
using ET;
using ET.Server;
using MemoryPack;
internal static class PurpleRayOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks=new (string,Action)[]{("FirstSolidWallConsumes",SolidWall),("FirstPlusTwoOnce",Chain),("FourMetreRange",Range),("LifetimeAndPause",Lifetime),("RedWall",RedWall),("FinalLifetimeBarrel",FinalLifetimeBarrel),("ArcSnapshotAndExpiry",ArcSnapshotAndExpiry)};
        if(maximum<checks.Length)throw new ArgumentException("PurpleRayOnline maximum too small");int failed=0;
        foreach(var(name,test) in checks)try{test();Console.WriteLine("PASS PurpleRayOnline."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL PurpleRayOnline."+name+": "+(e.InnerException??e).Message);}
        Console.WriteLine($"{checks.Length-failed}/{checks.Length} passed");return failed==0?0:1;
    }
    private static OnlineArcadeRunActor Create()
    {
        var rule=ServerConfig.Instance.ArcadeRule;
        var record=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,NextArena=1,ArenaConfigNumber=1,IsCasualRoute=true,ConfigFingerprint=rule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
        var actor=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};
        foreach(var player in actor.Battle.Players.Values){player.PositionX=player.PositionZ=0;player.AimX=0;player.AimZ=1;player.ReadyRoundGeneration=actor.Battle.RoundGeneration;}
        return actor;
    }
    private static ArcadeDropConfig Drop(string suffix)=>ServerConfig.Instance.Tables.TbArcadeDrop.Get("arcade.drop."+suffix);
    private static void Apply(OnlineArcadeRunActor actor,string id)=>Require((bool)Call("ApplyDrop",actor,actor.Battle.Players[1],Drop(id)),"Drop rejected: "+id);
    private static OnlineArcadeProjectileRuntime Shot(OnlineArcadeRunActor actor)
    {
        var old=actor.Battle.Projectiles.Keys.ToHashSet();Require((bool)Call("Fire",actor,actor.Battle,actor.Battle.Players[1],actor.Record.Players[0]),"Fire failed");
        return actor.Battle.Projectiles.Values.First(p=>!old.Contains(p.RuntimeId));
    }


    private static OnlineArcadeRunActor Ready(bool purple=true){var a=Create();Apply(a,"weapon.raygun");Apply(a,purple?"skull.large":"skull.small");return a;}
    private static OnlineArcadeEnemyRuntime Enemy(OnlineArcadeRunActor a,long id,float x,float z){var e=new OnlineArcadeEnemyRuntime{RuntimeId=id,PositionX=x,PositionZ=z,CurrentHealth=1000,MaxHealth=1000};a.Battle.Enemies[id]=e;a.Battle.EnemyIterationIds.Add(id);return e;}
    private static void SolidWall()
    {
        var a=Ready();a.Battle.ArenaConfigNumber=21;a.Battle.RoundLayout=OnlineArcadeRunActorSystem.ResolveRoundLayout(21,1);
        var p=Shot(a);p.PositionX=-6.75f;p.PositionZ=7;p.VelocityX=0;p.VelocityZ=10;p.RemainingReflections=2;
        Call("TickProjectiles",a,a.Battle,.3f);Require(a.Battle.Projectiles.Count==0,"solid cargo must consume purple ray without reflection");
    }
    private static void Chain(){var a=Ready();var first=Enemy(a,9,0,4);var second=Enemy(a,10,.5f,4);var third=Enemy(a,11,-.5f,4);var extra=Enemy(a,12,2,4);int d=Shot(a).Damage;Call("TickProjectiles",a,a.Battle,.5f);Require(first.CurrentHealth==1000-d&&second.CurrentHealth==1000-d&&third.CurrentHealth==1000-d&&extra.CurrentHealth==1000,"first+two distinct damage wrong");Require(first.LastDamagePlayerId==1&&third.LastDamagePlayerId==1,"owner attribution lost");Call("TickProjectiles",a,a.Battle,.5f);Require(first.CurrentHealth==1000-d,"duplicate chain");}
    private static void Range(){var a=Ready();Enemy(a,9,0,4);var edge=Enemy(a,10,4,4);var outside=Enemy(a,11,-4.01f,4);int d=Shot(a).Damage;Call("TickProjectiles",a,a.Battle,.5f);Require(edge.CurrentHealth==1000-d&&outside.CurrentHealth==1000,"four metre boundary wrong");}
    private static void Lifetime(){var a=Ready();var e=Enemy(a,9,0,10);var p=Shot(a);p.RemainingSeconds=.1f;float z=p.PositionZ;Apply(a,"weapon.rapid");Call("TickProjectiles",a,a.Battle,0f);Require(p.PositionZ==z&&p.RemainingSeconds==.1f&&p.WeaponPowerLevel==2,"pause or captured tier lost");Call("TickProjectiles",a,a.Battle,10f);Require(a.Battle.Projectiles.Count==0&&e.CurrentHealth==1000,"past lifetime hit");}
    private static void RedWall(){var a=Ready(false);a.Battle.ArenaConfigNumber=2;a.Battle.RoundLayout=null;Shot(a);Call("TickProjectiles",a,a.Battle,.12f);Require(a.Battle.Projectiles.Count==0,"red should stop at wall");}

    private static void FinalLifetimeBarrel()
    {
        var a=Create();a.Record.ArenaConfigNumber=3;a.Record.NextArena=2;a.Battle=OnlineArcadeRunActorSystem.PrepareBattle(a.Record,false);Apply(a,"weapon.raygun");Apply(a,"skull.large");
        var point=a.Battle.RoundLayout.Barrels[0];var p=Shot(a);p.PositionX=point.X;p.PositionZ=point.Y-1;p.VelocityX=0;p.VelocityZ=22;p.RemainingSeconds=.1f;
        Call("TickProjectiles",a,a.Battle,.2f);Require(a.Battle.DestroyedBarrelIds.Contains(0)&&a.Battle.Projectiles.Count==0,"valid final-lifetime segment skipped barrel");
    }
    private static void ArcSnapshotAndExpiry()
    {
        var a=Ready();Enemy(a,9,0,4);Enemy(a,10,2,4);Enemy(a,11,-2,4);Shot(a);Call("TickProjectiles",a,a.Battle,.3f);
        using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);
        using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));Require(decoded.ContentAreas.Count(p=>p.Kind==30&&p.Segment)==2,"arc projection/clone/codec lost endpoints");
        Call("TickProjectiles",a,a.Battle,0f);Require(a.Battle.PurpleRayArcs.Count==2,"pause expired arcs");Call("TickProjectiles",a,a.Battle,.2f);Require(a.Battle.PurpleRayArcs.Count==0,"arcs kept after projectiles empty");
    }
    private static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,args)!;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
