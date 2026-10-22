using System.Reflection;
using ET;
using ET.Server;
using MemoryPack;

/// <summary>头骨强化双人权威定向回归，不启动服务器、不写玩家存档。</summary>
internal static class SkullPowerOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks=new (string,Action)[]{("RedAndPurpleScaleActualShots",RedAndPurpleScaleActualShots),
            ("ContactAwardsOnePlayerOnce",ContactAwardsOnePlayerOnce),("SmallerSkullCannotDowngrade",SmallerSkullCannotDowngrade),
            ("SameWeaponRefreshAndDifferentWeaponClear",SameWeaponRefreshAndDifferentWeaponClear),
            ("ExpiryAndZeroDeltaRespectClocks",ExpiryAndZeroDeltaRespectClocks),("SnapshotAndCodecKeepShotTier",SnapshotAndCodecKeepShotTier),
            ("MasteryAndSkullRoundInSameOrder",MasteryAndSkullRoundInSameOrder),("RoundCheckpointKeepsTierAndTime",RoundCheckpointKeepsTierAndTime)};
        if(maximum<checks.Length)throw new ArgumentException("SkullPowerOnline maximum is too small");int failed=0;
        foreach(var check in checks){try{check.Item2();Console.WriteLine("PASS SkullPowerOnline."+check.Item1);}catch(Exception e){failed++;Console.WriteLine("FAIL SkullPowerOnline."+check.Item1+": "+(e.InnerException??e));}}
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
    private static void RedAndPurpleScaleActualShots()
    {
        foreach(var (id,multiplier) in new[]{("skull.small",2),("skull.large",3)})
        {
            var actor=Create();Apply(actor,id);var shot=Shot(actor);var weapon=ServerConfig.Instance.ArcadeRule.BaseWeaponId_Ref;
            Require(shot.Damage==weapon.Damage*multiplier&&shot.WeaponPowerLevel==multiplier-1,"Wrong shot power");
            Require(Math.Abs(actor.Battle.Players[1].FireCooldownSeconds-weapon.FireIntervalSeconds/multiplier)<.0001f,"Wrong fire rate");
            Require(!actor.Battle.Players[2].Powerups.ContainsKey(ArcadePowerupBehavior.WeaponSkull),"Other player inherited skull");
        }
    }
    private static void ContactAwardsOnePlayerOnce()
    {
        var actor=Create();actor.Battle.Drops.Add(901,new OnlineArcadeDropRuntime{RuntimeId=901,DropConfigId=Drop("skull.small").Id});
        Call("TickDrops",actor,actor.Battle);Call("TickDrops",actor,actor.Battle);
        Require(actor.Battle.Drops.Count==0&&actor.Battle.Players.Values.Count(p=>p.Powerups.ContainsKey(ArcadePowerupBehavior.WeaponSkull))==1,"Skull contact duplicated award");
    }
    private static void SmallerSkullCannotDowngrade()
    {
        var actor=Create();Apply(actor,"skull.large");Apply(actor,"skull.small");Require(Shot(actor).WeaponPowerLevel==2,"Small skull downgraded purple");
    }
    private static void SameWeaponRefreshAndDifferentWeaponClear()
    {
        var actor=Create();Apply(actor,"weapon.rapid");Apply(actor,"skull.small");Apply(actor,"weapon.rapid");var old=Shot(actor);
        Require(old.WeaponPowerLevel==2,"Same gun failed to upgrade red to purple");Apply(actor,"weapon.spread");
        Require(Shot(actor).WeaponPowerLevel==0&&old.WeaponPowerLevel==2,"Gun change rewrote in-flight shot or kept power");
    }
    private static void ExpiryAndZeroDeltaRespectClocks()
    {
        var actor=Create();Apply(actor,"weapon.rapid");Apply(actor,"skull.small");var player=actor.Battle.Players[1];
        float duration=player.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds;
        Call("TickPowerups",actor,actor.Battle,0f);Require(player.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds==duration,"Zero delta consumed power");
        player.TemporaryWeaponSeconds=.01f;actor.Battle.SpawnQueueIndex=actor.Battle.SpawnQueue.Count;
        actor.TickBattle(actor.Battle.LastTickAt+200);Require(!player.Powerups.ContainsKey(ArcadePowerupBehavior.WeaponSkull),"Weapon expiry left skull active");
        Apply(actor,"skull.small");actor.Battle.AwaitingExit=true;
        player.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds=.1f;
        actor.TickBattle(actor.Battle.LastTickAt+200);Require(!player.Powerups.ContainsKey(ArcadePowerupBehavior.WeaponSkull),"Exit wait froze skull timer");
    }
    private static void SnapshotAndCodecKeepShotTier()
    {
        var actor=Create();Apply(actor,"skull.large");var shot=Shot(actor);Apply(actor,"weapon.rapid");
        using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",actor.Battle);
        using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);
        using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));
        Require(decoded.Projectiles.Single(p=>p.RuntimeId==shot.RuntimeId).WeaponPowerLevel==2,"Tier lost across snapshot, clone or codec");
    }
    private static void MasteryAndSkullRoundInSameOrder()
    {
        foreach(var (id,multiplier) in new[]{("skull.small",2),("skull.large",3)})
        {
            var actor=Create();actor.Battle.SlayerMartRulesVersion=1;
            actor.Battle.SlayerMart=new FirepowerLoop.Arcade.ArcadeSlayerMartState{Players=new(){new(){PlayerId=1},new(){PlayerId=2}}};
            actor.Battle.SlayerMart.Players[0].Owned.Add("slayermart.item.12");Apply(actor,id);
            var expected=(int)MathF.Round(ServerConfig.Instance.ArcadeRule.BaseWeaponId_Ref.Damage*multiplier*1.25f);
            Require(Shot(actor).Damage==expected,"Skull/mastery rounding order differs from solo");
        }
    }
    private static void RoundCheckpointKeepsTierAndTime()
    {
        var actor=Create();Apply(actor,"skull.large");actor.Battle.Players[1].Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds=7;
        typeof(OnlineArcadeRunManagerSystem).GetMethod("CaptureRoundCheckpoint",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{actor.Record,actor.Battle});
        var restored=OnlineArcadeRunActorSystem.PrepareBattle(actor.Record,false);
        var skull=restored.Players[1].Powerups[ArcadePowerupBehavior.WeaponSkull];Require(skull.Magnitude==3&&skull.RemainingSeconds==7,"Round restore lost skull tier or reset time");
    }
    private static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,args)!;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
