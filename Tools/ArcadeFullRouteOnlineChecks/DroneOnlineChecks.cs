using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;
using MemoryPack;

/// <summary>限时无人机真实双人权威入口、快照与恢复检查；不启动服务器或写玩家存档。</summary>
internal static class DroneOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks=new (string,Action)[]{("SupportKeepsBothHeldWeapons",SupportKeepsBothHeldWeapons),
            ("ActualShotsTrackTierExpiry",ActualShotsTrackTierExpiry),("ActualRamExplodesOnce",ActualRamExplodesOnce),
            ("NoEnemyExitWaitStillExpires",NoEnemyExitWaitStillExpires),("SnapshotCloneCodecKeepsDroneState",SnapshotCloneCodecKeepsDroneState),
            ("RoundCloneKeepsTimeWithoutOldTarget",RoundCloneKeepsTimeWithoutOldTarget),("InvalidTuningIsRejected",InvalidTuningIsRejected)};
        if(maximum<checks.Length)throw new ArgumentException("DroneOnline maximum too small");int failed=0;
        foreach(var c in checks){try{c.Item2();Console.WriteLine("PASS DroneOnline."+c.Item1);}catch(Exception e){failed++;Console.WriteLine("FAIL DroneOnline."+c.Item1+": "+(e.InnerException??e));}}
        Console.WriteLine($"{checks.Length-failed}/{checks.Length} passed");return failed==0?0:1;
    }
    private static OnlineArcadeRunActor Create()
    {
        var rule=ServerConfig.Instance.ArcadeRule;
        var record=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,NextArena=1,ArenaConfigNumber=1,IsCasualRoute=true,ConfigFingerprint=rule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
        var actor=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};
        foreach(var p in actor.Battle.Players.Values){p.PositionX=p.PositionZ=0;p.ReadyRoundGeneration=actor.Battle.RoundGeneration;}
        return actor;
    }
    private static void Apply(OnlineArcadeRunActor a,string suffix,long id=1)=>Require((bool)Call("ApplyDrop",a,a.Battle.Players[id],ServerConfig.Instance.Tables.TbArcadeDrop.Get("arcade.drop."+suffix)),"Drop rejected");
    private static void Tick(OnlineArcadeRunActor a,float seconds)=>Call("TickPlayers",a,a.Battle,seconds,a.Battle.LastTickAt);
    private static OnlineArcadeEnemyRuntime Enemy(OnlineArcadeRunActor a,float x=3)
    {var e=new OnlineArcadeEnemyRuntime{RuntimeId=900,PositionX=x,PositionZ=.35f,CurrentHealth=1000,MaxHealth=1000};a.Battle.Enemies.Add(900,e);return e;}
    private static void SupportKeepsBothHeldWeapons()
    {
        var a=Create();Apply(a,"weapon.flamethrower");Apply(a,"support.drone");Apply(a,"support.drone",2);
        Require(a.Battle.Players[1].TemporaryWeaponId=="arcade.weapon.flamethrower","Drone replaced held weapon");
        Require(a.Battle.Players[2].TemporaryWeaponId==string.Empty,"Drone created a held weapon");
        Tick(a,.1f);Require(a.Battle.Players.Values.All(p=>p.Powerups.ContainsKey(ArcadePowerupBehavior.Drone)),"Players overwrote one drone");
        Require(a.Battle.Players[1].Powerups[ArcadePowerupBehavior.Drone].Drone!=a.Battle.Players[2].Powerups[ArcadePowerupBehavior.Drone].Drone,"Shared mutable drone");
    }
    private static void ActualShotsTrackTierExpiry()
    {
        var a=Create();Apply(a,"support.drone");Apply(a,"skull.large");Enemy(a);Tick(a,.01f);
        Require(a.Battle.Projectiles.Values.Single().Damage==36,"Purple shot damage");
        Require(a.Battle.Projectiles.Values.Single().WeaponPowerLevel==2,"Purple projectile tint lost");
        a.Battle.Projectiles.Clear();var p=a.Battle.Players[1];p.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds=.005f;
        p.Powerups[ArcadePowerupBehavior.Drone].Drone.FireCooldownSeconds=0;Tick(a,.01f);
        Require(a.Battle.Projectiles.Values.Single().Damage==12&&p.Powerups[ArcadePowerupBehavior.Drone].DronePowerTier==1,"Expired skull stayed on drone");
    }
    private static void ActualRamExplodesOnce()
    {
        var a=Create();Apply(a,"support.drone");Apply(a,"skull.large");var p=a.Battle.Players[1];p.Powerups[ArcadePowerupBehavior.Drone].RemainingSeconds=.2f;
        var enemy=Enemy(a,1.85f);Tick(a,.1f);Require(enemy.CurrentHealth==820,"Expected one purple explosion");
        Tick(a,.1f);Require(enemy.CurrentHealth==820,"Repeated explosion damage");
        typeof(OnlineArcadeRunManagerSystem).GetMethod("CaptureRoundCheckpoint",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{a.Record,a.Battle});
        Require(a.Record.Players[0].RoundPowerups.All(p=>p.Behavior!=ArcadePowerupBehavior.Drone),"Consumed drone carried into next round");
        Tick(a,1);
        Require(!p.Powerups.ContainsKey(ArcadePowerupBehavior.Drone),"Drone outlived explosion");
    }
    private static void NoEnemyExitWaitStillExpires()
    {
        var a=Create();Apply(a,"support.drone");a.Battle.AwaitingExit=true;
        Tick(a,26);Require(!a.Battle.Players[1].Powerups.ContainsKey(ArcadePowerupBehavior.Drone)&&a.Battle.Projectiles.Count==0,"Empty drone froze or fired");
    }
    private static void SnapshotCloneCodecKeepsDroneState()
    {
        var a=Create();Apply(a,"support.drone");Apply(a,"skull.small");Tick(a,.1f);
        var power=a.Battle.Players[1].Powerups[ArcadePowerupBehavior.Drone];power.Drone.Phase=ArcadeDronePhase.Ramming;
        using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);
        using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);
        using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));
        var drone=decoded.Players.Single(p=>p.PlayerId==1).Powerups.Single(p=>p.ProductId=="arcade.support.drone");
        Require(drone.DronePowerTier==2&&drone.DronePhase==1&&drone.PositionX==power.PositionX&&drone.RemainingSeconds==power.RemainingSeconds,"Drone projection lost");
    }
    private static void RoundCloneKeepsTimeWithoutOldTarget()
    {
        var a=Create();Apply(a,"support.drone");Tick(a,.1f);var original=a.Battle.Players[1].Powerups[ArcadePowerupBehavior.Drone];original.Drone.TargetId=900;
        var clone=OnlineArcadeRunActorSystem.ClonePowerup(original);
        Require(clone.Drone==null&&clone.RemainingSeconds==original.RemainingSeconds,"Round clone shared target or lost lifetime");
    }
    private static void InvalidTuningIsRejected()
    {
        var config=ServerConfig.CreateForValidation("OnlineLocal",n=>File.ReadAllBytes(Path.Combine("Server/GameServer/Config/Luban",n+".bytes")));
        var row=config.Tables.TbArcadeDrone.DataList.Single();typeof(ArcadeDroneConfig).GetField("RamSpeed")!.SetValue(row,float.NaN);
        bool rejected=false;try{typeof(ServerConfig).GetMethod("ValidateArcadeConfig",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(config,null);}
        catch(TargetInvocationException e)when(e.InnerException?.Message.Contains("TbArcadeDrone")==true){rejected=true;}
        Require(rejected,"Malformed drone config accepted");
    }
    private static object Call(string name,params object[] values)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,values)!;
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
