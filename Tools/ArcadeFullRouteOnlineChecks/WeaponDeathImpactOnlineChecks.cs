using System.Reflection;
using ET;
using ET.Server;
using MemoryPack;

/// <summary>在真实服务端开火、死亡记录和协议编解码上验证武器死亡力度；仅执行指定九项。</summary>
internal static class WeaponDeathImpactOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks=new (string Name,Action Check)[]
        {
            ("RaygunKeepsEnergyStyleAfterWeaponChange",()=>Explosion("arcade.weapon.raygun",1)),
            ("RocketKeepsHeavyExplosion",()=>Explosion("arcade.weapon.rocket",0)),
            ("GrenadeKeepsHeavyExplosion",()=>Explosion("arcade.weapon.grenade",0)),
            ("CloseShotgunUsesOriginalFiringPosition",()=>Shotgun(2,2)),
            ("DistantShotgunKeepsOrdinaryDeath",()=>Shotgun(4,0)),
            ("ShotgunThreeMeterBoundary",()=>{Shotgun(3,2);Shotgun(3.01f,0);}),
            ("LivingShotgunVictimDoesNotMoveOrProduceDeath",NonLethal),
            ("DeathStyleSurvivesSnapshotCloneAndCodec",RoundTrip),
            ("OtherPlayerWeaponDoesNotChangeProjectileSource",OtherPlayer),
        };
        if(maximum<checks.Length)throw new ArgumentException("Maximum below declared WeaponDeathImpactOnline scope");
        int failed=0;
        foreach(var check in checks)
        {
            try{check.Check();Console.WriteLine($"PASS WeaponDeathImpactOnline.{check.Name}");}
            catch(Exception error){failed++;Console.WriteLine($"FAIL WeaponDeathImpactOnline.{check.Name}: {error.InnerException??error}");}
        }
        Console.WriteLine($"{checks.Length-failed}/{checks.Length} passed");return failed==0?0:1;
    }

    private static OnlineArcadeRunActor Fire(string weapon)
    {
        var rule=ServerConfig.Instance.ArcadeRule;
        var record=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,NextArena=1,ArenaConfigNumber=1,
            IsCasualRoute=true,ConfigFingerprint=rule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
        var actor=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};
        var player=actor.Battle.Players[1];player.PositionX=player.PositionZ=0;player.AimX=0;player.AimZ=1;
        player.TemporaryWeaponId=weapon;player.TemporaryWeaponSeconds=10;
        Require((bool)Call("Fire",actor,actor.Battle,player,record.Players[0]),"Weapon did not fire");return actor;
    }
    private static OnlineArcadeEnemyRuntime Target(OnlineArcadeRunActor actor,float z,int hp=1,long id=1)
    {
        var enemy=new OnlineArcadeEnemyRuntime{RuntimeId=id,PositionZ=z,CurrentHealth=hp,MaxHealth=hp};
        actor.Battle.Enemies.Add(id,enemy);actor.Battle.EnemyIterationIds.Add(id);return enemy;
    }
    private static OnlineArcadeRunActor Explosion(string weapon,int expected)
    {
        var actor=Fire(weapon);var p=actor.Battle.Projectiles.Values.Single();
        Target(actor,p.PositionZ+p.VelocityZ*.1f);
        actor.Battle.Players[1].TemporaryWeaponId="arcade.weapon.basic";
        Call("TickProjectiles",actor,actor.Battle,.1f);
        var death=actor.Battle.EnemyDeaths.Single();
        Require(death.Explosive&&death.ImpactKind==expected,"Explosion changed death style");return actor;
    }
    private static void Shotgun(float distance,int expected)
    {
        var actor=Fire("arcade.weapon.spread");Target(actor,distance);
        var player=actor.Battle.Players[1];player.PositionZ=20;player.TemporaryWeaponId="arcade.weapon.raygun";
        float step=(distance-ServerConfig.Instance.ArcadeRule.PlayerRadius)/15f;
        Call("TickProjectiles",actor,actor.Battle,step);
        var death=actor.Battle.EnemyDeaths.Single();
        Require(!death.Explosive&&death.ImpactKind==expected,"Shotgun distance or firing source lost");
        Require(expected==2?death.DirectionZ>.95f:death.DirectionZ<-.95f,
            "Near shotgun lost shot direction or ordinary death direction changed");
    }
    private static void NonLethal()
    {
        var actor=Fire("arcade.weapon.spread");var enemy=Target(actor,2,1000);
        Call("TickProjectiles",actor,actor.Battle,(2-ServerConfig.Instance.ArcadeRule.PlayerRadius)/15f);
        Require(enemy.CurrentHealth<1000&&enemy.PositionX==0&&enemy.PositionZ==2&&actor.Battle.EnemyDeaths.Count==0,
            "Nonlethal hit changed alive position or produced corpse");
    }
    private static void RoundTrip()
    {
        var actor=Explosion("arcade.weapon.raygun",1);
        using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",actor.Battle);
        using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);
        snapshot.EnemyDeaths[0].ImpactKind=0;
        using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));
        Require(decoded.EnemyDeaths.Single().ImpactKind==1,"Death style lost across immutable copies or protocol");
    }
    private static void OtherPlayer()
    {
        var actor=Fire("arcade.weapon.spread");Target(actor,2);
        var other=actor.Battle.Players[2];other.PositionZ=-10;other.AimZ=1;other.TemporaryWeaponId="arcade.weapon.rocket";other.TemporaryWeaponSeconds=10;
        Require((bool)Call("Fire",actor,actor.Battle,other,actor.Record.Players[1]),"Other player did not fire");
        Call("TickProjectiles",actor,actor.Battle,(2-ServerConfig.Instance.ArcadeRule.PlayerRadius)/15f);
        Require(actor.Battle.EnemyDeaths.Single().ImpactKind==2,"Other player's gun changed shotgun death");
    }
    private static object Call(string method,params object[] args)=>typeof(OnlineArcadeRunActorSystem)
        .GetMethod(method,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,args)!;
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
