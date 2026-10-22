using System.Reflection;
using ET;
using ET.Server;
/// <summary>紫火箭的实际权威发射和命中定向检查，不启动服务。</summary>
internal static class PurpleRocketOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks=new (string,Action)[]{("ThreeWithBudget",ThreeWithBudget),("NoPartialVolley",NoPartialVolley),("MovingTargetAfterSwitch",MovingTargetAfterSwitch),("SweptDamageOnce",SweptDamageOnce),("NoTargetExpiryQuiet",NoTargetExpiryQuiet),("NearestCrateBudget",NearestCrateBudget),("ZeroStepAndDeadTarget",ZeroStepAndDeadTarget),("WallBeforeEnemy",WallBeforeEnemy)};
        if(maximum<checks.Length)throw new ArgumentException("PurpleRocketOnline maximum too small");int failures=0;
        foreach(var(name,test)in checks)try{test();Console.WriteLine("PASS PurpleRocketOnline."+name);}catch(Exception e){failures++;Console.WriteLine("FAIL PurpleRocketOnline."+name+": "+(e.InnerException??e));}
        Console.WriteLine($"{checks.Length-failures}/{checks.Length} passed");return failures==0?0:1;
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

    private static OnlineArcadeRunActor Ready(){var a=Create();Apply(a,"weapon.rocket");Apply(a,"skull.large");return a;}
    private static OnlineArcadeEnemyRuntime Enemy(OnlineArcadeRunActor a,long id,float x,float z){var e=new OnlineArcadeEnemyRuntime{RuntimeId=id,PositionX=x,PositionZ=z,CurrentHealth=1000,MaxHealth=1000};a.Battle.Enemies[id]=e;a.Battle.EnemyIterationIds.Add(id);return e;}
    private static void ThreeWithBudget(){var a=Ready();Enemy(a,9,0,10);Shot(a);var shots=a.Battle.Projectiles.Values;Require(shots.Count==3&&shots.Sum(p=>p.Damage)==ServerConfig.Instance.Tables.TbArcadeWeapon.Get("arcade.weapon.rocket").Damage*3,"three-shot budget wrong");Require(shots.All(p=>p.WeaponPowerLevel==2&&p.TrackingEnemyId==9),"tier or target not captured");}
    private static void NoPartialVolley(){var a=Ready();for(int i=0;i<ServerConfig.Instance.ArcadeRule.MaxProjectiles-2;i++)a.Battle.Projectiles[10000+i]=new(){RuntimeId=10000+i,RemainingSeconds=1};Require(!(bool)Call("Fire",a,a.Battle,a.Battle.Players[1],a.Record.Players[0])&&a.Battle.Players[1].FireSequence==0,"partial volley or consumed cooldown");}
    private static void MovingTargetAfterSwitch(){var a=Ready();var e=Enemy(a,9,0,10);Shot(a);var shots=a.Battle.Projectiles.Values.ToArray();var vx=shots.Select(p=>p.VelocityX).ToArray();Apply(a,"weapon.rapid");e.PositionX=5;Call("TickProjectiles",a,a.Battle,.1f);Require(shots.Select((p,i)=>p.VelocityX>vx[i]&&p.WeaponPowerLevel==2).All(x=>x),"in-flight tracking lost");}
    private static void SweptDamageOnce(){var a=Ready();var e=Enemy(a,9,0,5);Shot(a);Call("TickProjectiles",a,a.Battle,.7f);int expected=1000-ServerConfig.Instance.Tables.TbArcadeWeapon.Get("arcade.weapon.rocket").Damage*3;Require(e.CurrentHealth==expected&&a.Battle.Projectiles.Count==0,"swept enemy skipped or damage budget multiplied");Call("TickProjectiles",a,a.Battle,.7f);Require(e.CurrentHealth==expected,"duplicate blast");}
    private static void NoTargetExpiryQuiet(){var a=Ready();var e=Enemy(a,9,0,50);Shot(a);Call("TickProjectiles",a,a.Battle,10f);Require(a.Battle.Projectiles.Count==0&&e.CurrentHealth==1000,"expired rocket exploded or flew past lifetime");}
    private static void NearestCrateBudget(){var a=Ready();Enemy(a,9,0,10);a.Battle.Crates[1]=new(new FirepowerLoop.Arcade.ArcadeBabylonParameters.Crate(1,0,3,.4f,1000));a.Battle.Crates[2]=new(new FirepowerLoop.Arcade.ArcadeBabylonParameters.Crate(2,2,3,.4f,1000));Shot(a);Call("TickProjectiles",a,a.Battle,.6f);int damage=ServerConfig.Instance.Tables.TbArcadeWeapon.Get("arcade.weapon.rocket").Damage*3;Require(a.Battle.Crates[1].Health==1000-damage*2&&a.Battle.Crates[2].Health==1000-damage,"crate direct+area contract differs");}
    private static void ZeroStepAndDeadTarget(){var a=Ready();var first=Enemy(a,9,0,10);Enemy(a,10,5,10);Shot(a);var shots=a.Battle.Projectiles.Values.ToArray();var old=shots.Select(p=>(p.PositionX,p.PositionZ,p.RemainingSeconds)).ToArray();Call("TickProjectiles",a,a.Battle,0f);Require(shots.Select((p,i)=>(p.PositionX,p.PositionZ,p.RemainingSeconds)==old[i]).All(x=>x),"zero step advanced");first.CurrentHealth=0;Call("TickProjectiles",a,a.Battle,.1f);Require(shots.All(p=>p.TrackingEnemyId==10),"dead target retained");}
    private static void WallBeforeEnemy(){var a=Ready();var e=Enemy(a,9,0,8);a.Battle.ArenaConfigNumber=2;a.Battle.RoundLayout=null;var p=a.Battle.Players[1];p.PositionZ=0;p.AimZ=1;Shot(a);Call("TickProjectiles",a,a.Battle,1f);Require(e.CurrentHealth==1000&&a.Battle.Projectiles.Count==0,"legacy arena wall crossed");}
    private static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,args)!;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
