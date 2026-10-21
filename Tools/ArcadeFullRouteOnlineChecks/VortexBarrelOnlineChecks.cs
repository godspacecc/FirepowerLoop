using System.Reflection;
using ET;
using ET.Server;
using MemoryPack;

/// <summary>漩涡桶双人权威检查；不启动监听、不读取玩家档。</summary>
internal static class VortexBarrelOnlineChecks
{
    internal static int Run(int maximum)
    {
        var checks=new (string Name,Action Check)[]{
            ("CarrierDropsOnceAndSnapshotCodecKeepsIndependentBarrel",CarrierDropsOnceAndSnapshotCodecKeepsIndependentBarrel),
            ("PullConsumesWithoutAwardAndFreezesAtBoundaries",PullConsumesWithoutAwardAndFreezesAtBoundaries),
            ("ShotAccumulatesDamageAndExplosionProtectsBothPlayers",ShotAccumulatesDamageAndExplosionProtectsBothPlayers),
            ("CarrierExplosionProtectsBothPlayers",CarrierExplosionProtectsBothPlayers),
            ("AccumulatedBarrelsNeverBlockRelease",AccumulatedBarrelsNeverBlockRelease),
            ("ControlledCarrierUsesActiveLifetimeFromBirth",ControlledCarrierUsesActiveLifetimeFromBirth),
            ("PurpleExplosionSurvivesClearAndSnapshotCodec",PurpleExplosionSurvivesClearAndSnapshotCodec)};
        if(maximum<checks.Length)throw new ArgumentException("VortexBarrelOnline requires maximum >= 7");
        int failures=0;
        foreach(var check in checks)try{check.Check();Console.WriteLine("PASS VortexBarrelOnline."+check.Name);}
            catch(Exception e){failures++;Console.WriteLine("FAIL VortexBarrelOnline."+check.Name+": "+(e.InnerException??e));}
        Console.WriteLine($"{checks.Length-failures}/{checks.Length} passed");return failures==0?0:1;
    }

    private static OnlineArcadeRunActor Create()
    {
        var record=new OnlineArcadeRunRecord{RunId=71,RoomId=72,Revision=1,NextArena=2,ArenaConfigNumber=3,RoundNumber=1,IsCasualRoute=true,
            ConfigFingerprint=ServerConfig.Instance.ArcadeRule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=ServerConfig.Instance.ArcadeRule.BaseWeaponId},
                new(){PlayerId=2,Lives=3,Ready=true,WeaponId=ServerConfig.Instance.ArcadeRule.BaseWeaponId}}};
        return new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};
    }
    private static object Call(string name,params object[] values)
    {
        var method=typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static);
        Require(method!=null,"Missing authority entry: "+name);return method.Invoke(null,values);
    }
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}

    private static void CarrierDropsOnceAndSnapshotCodecKeepsIndependentBarrel()
    {
        var actor=Create();var b=actor.Battle;
        var enemy=new OnlineArcadeEnemyRuntime{RuntimeId=7,SpecialKind=1,CurrentHealth=100,MaxHealth=100};b.Enemies.Add(7,enemy);
        b.ContentClock.Advance(20,false);Call("TickVortexBarrels",b,.1f);
        Require(enemy.SpecialKind==0,"Carrier kept its barrel");Call("TickVortexBarrels",b,.1f);
        using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",b);
        using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);
        using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));
        Require(decoded.ContentAreas.Count(a=>a.Kind==13)==1,"Dropped barrel lost or duplicated in codec");
        int count=b.DeathZones.Count;Call("SpawnContentDeathZone",actor,b,enemy);Require(b.DeathZones.Count==count,"Released carrier exploded twice");
    }
    private static void PullConsumesWithoutAwardAndFreezesAtBoundaries()
    {
        var actor=Create();var b=actor.Battle;var point=b.RoundLayout.Barrels[0];
        var drop=new OnlineArcadeDropRuntime{RuntimeId=99,DropConfigId=ServerConfig.Instance.Tables.TbArcadeDrop.DataList[0].Id,PositionX=point.X,PositionZ=point.Y+2};b.Drops.Add(99,drop);
        long score=actor.Record.Players[0].Score;Call("TickVortexBarrels",b,.1f);Require(drop.PositionZ<point.Y+2&&b.Drops.ContainsKey(99),"Drop did not visibly move");
        float position=drop.PositionZ;b.RoundTransitionPending=true;Call("TickVortexBarrels",b,20f);Require(drop.PositionZ==position,"Transition advanced pull");b.RoundTransitionPending=false;
        drop.DeliveryRemainingSeconds=1;Call("TickVortexBarrels",b,20f);Require(drop.PositionZ==position,"Unopened airdrop was pulled");drop.DeliveryRemainingSeconds=0;
        Call("TickVortexBarrels",b,20f);Require(!b.Drops.ContainsKey(99)&&actor.Record.Players[0].Score==score,"Consumption awarded pickup");
    }
    private static void AccumulatedBarrelsNeverBlockRelease()
    {
        var actor=Create();var b=actor.Battle;
        for(int id=1;id<=30;id++)b.Enemies.Add(id,new OnlineArcadeEnemyRuntime{RuntimeId=id,SpecialKind=1,CurrentHealth=100,MaxHealth=100,ContentElapsed=20});
        b.ContentClock.Advance(20,false);Call("TickVortexBarrels",b,.1f);
        Require(b.DroppedBarrels.Count==30&&b.Enemies.Values.All(e=>e.SpecialKind==0),"Accumulated barrels prevented release");
    }
    private static void ControlledCarrierUsesActiveLifetimeFromBirth()
    {
        var actor=Create();var b=actor.Battle;b.ContentClock.Advance(20,false);
        var enemy=new OnlineArcadeEnemyRuntime{RuntimeId=7,CurrentHealth=100,MaxHealth=100,ContentStunUntil=100};
        Call("InitializeEnemyContent",b,enemy);enemy.SpecialKind=1;b.Enemies.Add(7,enemy);
        Call("TickRoundContent",actor,b,.1f);Require(enemy.SpecialKind==1,"Late spawn inherited arena age");
        for(int tick=0;tick<6;tick++){Call("TickEnemies",actor,b,1f);Call("TickRoundContent",actor,b,1f);}
        Require(enemy.ContentElapsed==0,"Fixture did not follow controlled enemy path");
        Require(enemy.SpecialKind==0&&b.DroppedBarrels.Count==1,"Controlled carrier paused its release lifetime");
    }
    private static void ShotAccumulatesDamageAndExplosionProtectsBothPlayers()
    {
        var actor=Create();var b=actor.Battle;var point=b.RoundLayout.Barrels[0];
        foreach(var player in b.Players.Values){player.PositionX=point.X;player.PositionZ=point.Y;}
        var enemy=new OnlineArcadeEnemyRuntime{RuntimeId=8,CurrentHealth=100,MaxHealth=100,PositionX=point.X,PositionZ=point.Y+2};b.Enemies.Add(8,enemy);
        var shot=new OnlineArcadeProjectileRuntime{FromPlayerWeapon=true,OwnerPlayerId=1,RemainingSeconds=1,Damage=1};
        Require((bool)Call("TryHitGroundBarrel",actor,b,shot,point.X-1,point.Y,point.X,point.Y),"Weak shot did not hit");Require(b.DestroyedBarrelIds.Count==0,"Weak shot exploded barrel");
        shot.Damage=100;Require((bool)Call("TryHitGroundBarrel",actor,b,shot,point.X-1,point.Y,point.X,point.Y),"Strong shot missed");
        Require(b.DeathZones.Any(z=>z.Kind==14),"Missing purple barrel authority event");
        Require(b.DestroyedBarrelIds.Count==1&&enemy.CurrentHealth<100,"Barrel did not damage enemy");
        Require(b.Players.Values.All(p=>p.CurrentHealth==p.MaxHealth&&p.Alive),"Barrel hurt players");
    }
    private static void CarrierExplosionProtectsBothPlayers()
    {
        var actor=Create();var b=actor.Battle;
        foreach(var player in b.Players.Values){player.PositionX=0;player.PositionZ=0;}
        Call("SpawnContentDeathZone",actor,b,new OnlineArcadeEnemyRuntime{SpecialKind=1,PositionX=0,PositionZ=0});
        Require(b.DeathZones.Any(z=>z.Kind==14),"Carrier did not publish purple explosion");
        Call("TickRoundContent",actor,b,.1f);
        Require(b.Players.Values.All(p=>p.CurrentHealth==p.MaxHealth),"Carrier blast hurt a player");
    }
    private static void PurpleExplosionSurvivesClearAndSnapshotCodec()
    {
        var actor=Create();var b=actor.Battle;var point=b.RoundLayout.Barrels[0];
        Call("TryHitGroundBarrel",actor,b,new OnlineArcadeProjectileRuntime{FromPlayerWeapon=true,OwnerPlayerId=1,RemainingSeconds=1,Damage=100},point.X-1,point.Y,point.X,point.Y);
        b.WaveClearAwarded=true;b.AwaitingExit=true;
        using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",b);
        using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(snapshot));
        Require(decoded.ContentAreas.Count(a=>a.Kind==14)==1,"Last kill explosion missing or duplicated in frozen clear snapshot");
    }
}
