using System.Reflection;
using ET;
using ET.Server;

/// <summary>离线调用真实双人权威战斗循环，覆盖全部轮图、旧主题和挑战的弹道契约。</summary>
internal static class AllMapProjectileOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks=new (string,Action)[]{("AllRoundEdges",AllRoundEdges),("LegacyAndChallengeEdges",LegacyAndChallengeEdges),
            ("DockWater",DockWater),("NoSolidWallReflection",NoSolidWallReflection),("Lifetime",Lifetime),("CargoBlocksAllKinds",CargoBlocksAllKinds),("OutsideSnapshot",OutsideSnapshot)};
        if(maximum<checks.Length)throw new ArgumentException("AllMapProjectileOnline maximum too small");int failed=0;
        foreach(var(name,test) in checks)try{test();Console.WriteLine("PASS AllMapProjectileOnline."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL AllMapProjectileOnline."+name+": "+(e.InnerException??e).Message);}
        Console.WriteLine($"{checks.Length-failed}/{checks.Length} passed");return failed==0?0:1;
    }
    private static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,args)!;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static OnlineArcadeRunActor Create(int number=21,int round=1,bool casual=true)
    {
        var rule=ServerConfig.Instance.ArcadeRule;
        var record=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,NextArena=2,ArenaConfigNumber=number,IsCasualRoute=casual,ConfigFingerprint=rule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
        var actor=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};
        actor.Battle.ArenaConfigNumber=number;
        actor.Battle.RoundLayout=casual?OnlineArcadeRunActorSystem.ResolveRoundLayout(number,round):null;
        actor.Battle.Enemies.Clear();actor.Battle.Projectiles.Clear();actor.Battle.Crates.Clear();actor.Battle.DroppedBarrels.Clear();
        foreach(var player in actor.Battle.Players.Values){player.PositionX=player.PositionZ=0;player.CurrentHealth=100;}
        return actor;
    }
    private static OnlineArcadeProjectileRuntime Shot(OnlineArcadeRunActor a,int kind,float x,float z,float vx,float vz,float lifetime=2)
    {
        var p=new OnlineArcadeProjectileRuntime{RuntimeId=901,OwnerPlayerId=kind==1?0:1,PositionX=x,PositionZ=z,VelocityX=vx,VelocityZ=vz,
            RemainingSeconds=lifetime,Damage=1,RemainingHits=1,FromPlayerWeapon=kind!=1,SourceWeaponId=kind==2?"arcade.weapon.rocket":kind==3?"arcade.weapon.raygun":"arcade.weapon.basic",
            WeaponPowerLevel=kind>=2?2:0,RemainingReflections=2};a.Battle.Projectiles[901]=p;return p;
    }
    private static void Tick(OnlineArcadeRunActor a,float seconds)=>Call("TickProjectiles",a,a.Battle,seconds);
    private static void AllRoundEdges()
    {
        int count=0;
        foreach(var layout in ServerConfig.Instance.Tables.TbArcadeRoundLayout.DataList)foreach(int kind in Enumerable.Range(0,4))foreach(var(dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
        {
            var a=Create(layout.ArenaConfigNumber,layout.RoundNumber);float half=layout.ArenaConfigNumber==22?30.5f:layout.ArenaConfigNumber==21?(dx!=0?20.5f:19.5f):19.5f;
            var p=Shot(a,kind,dx*(half-.1f),dz*(half-.1f),dx*10,dz*10);Tick(a,.1f);
            Require(a.Battle.Projectiles.ContainsKey(901),$"{layout.Id}, kind={kind}, direction={dx},{dz} removed at outside edge");
            Tick(a,.1f);Require(a.Battle.Projectiles.ContainsKey(901),"shot removed on second outside frame");
            Require(p.VelocityX==dx*10&&p.VelocityZ==dz*10,"outside edge reflected velocity");
            Require(Math.Abs(p.PositionX*dx+p.PositionZ*dz-(half+1.9f))<.002f,"outside edge lost travel");count++;
        }
        Require(count==1408,"expected 88 layouts x 4 projectile kinds x 4 edges");
    }
    private static void LegacyAndChallengeEdges()
    {
        foreach(var arena in ServerConfig.Instance.Tables.TbArcadeArena.DataList)foreach(bool challenge in new[]{false,true})foreach(int kind in Enumerable.Range(0,4))
        {var a=Create(arena.ArenaNumber,1,false);a.Battle.IsChallenge=challenge;Shot(a,kind,100,0,10,0);Tick(a,.1f);Require(a.Battle.Projectiles.ContainsKey(901),$"legacy arena {arena.ArenaNumber}, challenge={challenge}, kind={kind}");}
    }
    private static void DockWater()
    {
        for(int round=1;round<=4;round++)foreach(int kind in Enumerable.Range(0,4))
        {var a=Create(21,round);var p=Shot(a,kind,10,7,10,0);Tick(a,.6f);Require(a.Battle.Projectiles.ContainsKey(901)&&Math.Abs(p.PositionX-16)<.001f,$"dock water consumed kind {kind}, round {round}");}
    }
    private static void NoSolidWallReflection()
    {
        var a=Create();Shot(a,3,-6.75f,7,0,10);Tick(a,.3f);Require(a.Battle.Projectiles.Count==0,"purple ray reflected off solid cargo");
    }
    private static void Lifetime()
    {
        foreach(int kind in Enumerable.Range(0,4)){var a=Create();Shot(a,kind,25,0,10,0,.15f);Tick(a,.1f);Require(a.Battle.Projectiles.ContainsKey(901),"shot removed before lifetime");Tick(a,.1f);Require(a.Battle.Projectiles.Count==0,"outside shot never expired");}
    }
    private static void CargoBlocksAllKinds()
    {
        foreach(int kind in Enumerable.Range(0,4)){var a=Create();Shot(a,kind,-6.75f,7,0,10);Tick(a,.3f);Require(a.Battle.Projectiles.Count==0,$"kind {kind} passed through cargo");}
    }
    private static void OutsideSnapshot()
    {
        var a=Create();Shot(a,0,25,0,10,0);Tick(a,.1f);using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);
        Require(snapshot.Projectiles.Any(p=>p.RuntimeId==901&&p.PositionX>25),"outside shot missing from online snapshot");
    }
}
