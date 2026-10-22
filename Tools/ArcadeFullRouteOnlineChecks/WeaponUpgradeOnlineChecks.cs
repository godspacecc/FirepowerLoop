using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;
/// <summary>同枪升档的实际权威拾取、移动和奖励房入口定向检查。</summary>
internal static class WeaponUpgradeOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks=new (string,Action)[]{("RepeatedLevels",RepeatedLevels),("SeparateClocks",SeparateClocks),("SkullRefill",SkullRefill),("TreasureWeaponUsesUpgrade",TreasureWeaponUsesUpgrade),("SharedPickupOnce",SharedPickupOnce),("PurpleMovement",PurpleMovement)};
        if(maximum<checks.Length)throw new ArgumentException("WeaponUpgradeOnline maximum too small");int failed=0;
        foreach(var(name,test)in checks)try{test();Console.WriteLine("PASS WeaponUpgradeOnline."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL WeaponUpgradeOnline."+name+": "+(e.InnerException??e));}
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

    private static int Level(OnlineArcadeRunActor a)=>a.Battle.Players[1].Powerups.TryGetValue(ArcadePowerupBehavior.WeaponSkull,out var s)?(int)s.Magnitude-1:0;
    private static void RepeatedLevels(){var a=Create();Apply(a,"weapon.rapid");Require(Level(a)==0,"new weapon starts powered");Apply(a,"weapon.rapid");Require(Level(a)==1,"repeat not red");Apply(a,"weapon.rapid");Apply(a,"weapon.rapid");Require(Level(a)==2,"purple cap wrong");Apply(a,"weapon.spread");Require(Level(a)==0,"different weapon retained power");}
    private static void SeparateClocks(){var a=Create();Apply(a,"weapon.rapid");Apply(a,"skull.large");var p=a.Battle.Players[1];p.TemporaryWeaponSeconds=40;p.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds=45;Apply(a,"weapon.rapid");Require(p.TemporaryWeaponSeconds==40&&p.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds==45,"long clocks shortened");}
    private static void SkullRefill(){var a=Create();Apply(a,"weapon.rapid");Apply(a,"skull.large");var p=a.Battle.Players[1];p.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds=45;Apply(a,"skull.large");Apply(a,"skull.small");Require(Level(a)==2&&p.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds==45,"skull shortened or downgraded");p.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds=1;Apply(a,"weapon.rapid");Require(p.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds>=20,"repeat did not refill minimum");}
    private static void TreasureWeaponUsesUpgrade(){var a=Create();Apply(a,"weapon.rapid");var r=new ArcadeTreasureRewardParameters(1,FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Weapon,0,0,0,0,1,Drop("weapon.rapid").Id);Require((bool)Call("ApplyTreasureReward",a,1L,r,null!,false),"treasure rejected");Require(Level(a)==1,"treasure bypassed upgrade");}
    private static void SharedPickupOnce(){var a=Create();foreach(var p in a.Battle.Players.Values){p.TemporaryWeaponId=Drop("weapon.rapid").ProductId;p.TemporaryWeaponSeconds=10;}a.Battle.Drops[901]=new OnlineArcadeDropRuntime{RuntimeId=901,DropConfigId=Drop("weapon.rapid").Id};Call("TickDrops",a,a.Battle);Call("TickDrops",a,a.Battle);Require(a.Battle.Drops.Count==0&&a.Battle.Players.Values.Count(p=>p.Powerups.ContainsKey(ArcadePowerupBehavior.WeaponSkull))==1,"competitive pickup not consumed once");}
    private static void PurpleMovement(){var a=Create();var p=a.Battle.Players[1];p.MoveZ=1;p.LastInputAt=TimeInfo.Instance.ServerNow();float Move(){p.PositionX=p.PositionZ=0;Call("TickPlayers",a,a.Battle,.01f,p.LastInputAt);return Math.Abs(p.PositionZ);}float normal=Move();Apply(a,"skull.large");Require(Math.Abs(Move()-normal*1.1f)<.0001f,"purple speed not applied");Apply(a,"powerup.boots");p.DashSeconds=1;p.DashDirectionZ=1;float purple=Move();p.Powerups.Remove(ArcadePowerupBehavior.WeaponSkull);Require(Math.Abs(purple-Move()*1.1f)<.0001f,"boots/dash composition differs");}
    private static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,args)!;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
