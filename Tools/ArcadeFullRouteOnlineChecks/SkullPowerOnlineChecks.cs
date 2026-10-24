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
            ("SameWeaponRefreshAndDifferentWeaponPreserve",SameWeaponRefreshAndDifferentWeaponPreserve),
            ("ExpiryAndZeroDeltaRespectClocks",ExpiryAndZeroDeltaRespectClocks),("SnapshotAndCodecKeepShotTier",SnapshotAndCodecKeepShotTier),
            ("MasteryAndSkullRoundInSameOrder",MasteryAndSkullRoundInSameOrder),("RoundCheckpointKeepsTierAndTime",RoundCheckpointKeepsTierAndTime),
            ("SkullExpiryLeavesLongerWeaponActive",SkullExpiryLeavesLongerWeaponActive),
            ("RetiredMeleeFallsBackToBasicRifle",RetiredMeleeFallsBackToBasicRifle)};
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
    /// <summary>退役近战不再占用街机配置；旧永久和临时身份仍经真实开火入口回退基础枪。</summary>
    private static void RetiredMeleeFallsBackToBasicRifle()
    {
        foreach(var id in new[]{"weapon.knife","weapon.axe","weapon.pickaxe"})
        {
            Require(ServerConfig.Instance.Tables.TbArcadeWeapon.GetOrDefault(id)==null,"Retired config remains: "+id);
            var actor=Create();actor.Record.Players[0].WeaponId=id;
            var weapon=ServerConfig.Instance.ArcadeRule.BaseWeaponId_Ref;
            Require(Shot(actor).SourceWeaponId==weapon.Id,"Saved retired weapon did not fall back: "+id);
            var player=actor.Battle.Players[1];player.TemporaryWeaponId=id;player.TemporaryWeaponSeconds=5;
            Require(Shot(actor).SourceWeaponId==weapon.Id&&Math.Abs(player.FireCooldownSeconds-weapon.FireIntervalSeconds)<.0001f,
                "Temporary retired weapon did not fall back: "+id);
        }
    }
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
    private static void SameWeaponRefreshAndDifferentWeaponPreserve()
    {
        var actor=Create();Apply(actor,"weapon.rapid");Apply(actor,"skull.small");Apply(actor,"weapon.rapid");var old=Shot(actor);
        Require(old.WeaponPowerLevel==2,"Same gun failed to upgrade red to purple");
        var skull=actor.Battle.Players[1].Powerups[ArcadePowerupBehavior.WeaponSkull];skull.RemainingSeconds=7;
        Apply(actor,"weapon.spread");
        Require(Shot(actor).WeaponPowerLevel==2&&old.WeaponPowerLevel==2,"Gun change cleared independent power or rewrote old shot");
        Require(skull.RemainingSeconds==7,"Different weapon refilled independent skull clock");
    }
    private static void ExpiryAndZeroDeltaRespectClocks()
    {
        foreach(var (id,multiplier) in new[]{("skull.small",2),("skull.large",3)})
        {
            var actor=Create();Apply(actor,"weapon.rapid");Apply(actor,id);var player=actor.Battle.Players[1];
            var skull=player.Powerups[ArcadePowerupBehavior.WeaponSkull];skull.RemainingSeconds=15;player.TemporaryWeaponSeconds=5;
            Call("TickPlayers",actor,actor.Battle,0f,actor.Battle.LastTickAt);
            Require(skull.RemainingSeconds==15&&player.TemporaryWeaponSeconds==5,"Zero delta consumed a clock");
            Call("TickPlayers",actor,actor.Battle,5f,actor.Battle.LastTickAt);
            Require(string.IsNullOrEmpty(player.TemporaryWeaponId)&&player.TemporaryWeaponSeconds==0,"Weapon did not expire");
            Require(player.Powerups.ContainsKey(ArcadePowerupBehavior.WeaponSkull)&&skull.RemainingSeconds==10,"Weapon expiry cleared independent skull clock");
            var shot=Shot(actor);var weapon=ServerConfig.Instance.ArcadeRule.BaseWeaponId_Ref;
            Require(shot.SourceWeaponId==weapon.Id&&shot.Damage==weapon.Damage*multiplier&&shot.WeaponPowerLevel==multiplier-1,"Fallback gun lost skull power");
            Require(Math.Abs(player.FireCooldownSeconds-weapon.FireIntervalSeconds/multiplier)<.0001f,"Fallback fire rate lost power");
            Require(!actor.Battle.Players[2].Powerups.ContainsKey(ArcadePowerupBehavior.WeaponSkull),"Fallback transferred power to teammate");
            Call("TickPlayers",actor,actor.Battle,10f,actor.Battle.LastTickAt);
            Require(!player.Powerups.ContainsKey(ArcadePowerupBehavior.WeaponSkull),"Skull did not expire on its own clock");
            Require(Shot(actor).WeaponPowerLevel==0&&shot.WeaponPowerLevel==multiplier-1,"Skull expiry failed or rewrote old shot");
            // 等待出口不允许开火，在进入等待前检查普通弹，等待期间只检查时钟。
            Apply(actor,id);skull=player.Powerups[ArcadePowerupBehavior.WeaponSkull];
            actor.Battle.AwaitingExit=true;skull.RemainingSeconds=.1f;
            actor.TickBattle(actor.Battle.LastTickAt+200);
            Require(!player.Powerups.ContainsKey(ArcadePowerupBehavior.WeaponSkull),"Exit wait froze skull timer");
        }
    }

    /// <summary>反向到期只结束强化，不缩短仍在使用的临时武器。</summary>
    private static void SkullExpiryLeavesLongerWeaponActive()
    {
        foreach(var id in new[]{"skull.small","skull.large"})
        {
            var actor=Create();Apply(actor,"weapon.rapid");Apply(actor,id);var player=actor.Battle.Players[1];
            player.TemporaryWeaponSeconds=40;player.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds=5;
            Call("TickPlayers",actor,actor.Battle,5f,actor.Battle.LastTickAt);
            var shot=Shot(actor);var weapon=Drop("weapon.rapid").ProductId;
            Require(player.TemporaryWeaponSeconds==35&&shot.SourceWeaponId==weapon&&shot.WeaponPowerLevel==0,"Skull expiry ended or powered the remaining weapon");
        }
    }
    private static void SnapshotAndCodecKeepShotTier()
    {
        var actor=Create();Apply(actor,"skull.large");var shot=Shot(actor);Apply(actor,"weapon.rapid");
        var player=actor.Battle.Players[1];player.TemporaryWeaponSeconds=5;
        player.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds=15;
        Call("TickPlayers",actor,actor.Battle,5f,actor.Battle.LastTickAt);
        using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",actor.Battle);
        using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);
        using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));
        Require(decoded.Projectiles.Single(p=>p.RuntimeId==shot.RuntimeId).WeaponPowerLevel==2,"Tier lost across snapshot, clone or codec");
        var decodedPlayer=decoded.Players.Single(p=>p.PlayerId==1);
        Require(string.IsNullOrEmpty(decodedPlayer.TemporaryWeaponId)&&decodedPlayer.TemporaryWeaponSeconds==0,"Snapshot retained expired weapon");
        Require(decodedPlayer.Powerups.Single(p=>p.ProductId=="arcade.powerup.skull.large").RemainingSeconds==10,"Independent skull clock lost across snapshot, clone or codec");
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
        var actor=Create();Apply(actor,"weapon.rapid");Apply(actor,"skull.large");var player=actor.Battle.Players[1];
        player.TemporaryWeaponSeconds=5;player.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds=12;
        Call("TickPlayers",actor,actor.Battle,5f,actor.Battle.LastTickAt);
        typeof(OnlineArcadeRunManagerSystem).GetMethod("CaptureRoundCheckpoint",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{actor.Record,actor.Battle});
        var restored=OnlineArcadeRunActorSystem.PrepareBattle(actor.Record,false);
        var restoredPlayer=restored.Players[1];
        Require(restoredPlayer.Powerups.ContainsKey(ArcadePowerupBehavior.WeaponSkull),"Round checkpoint lost independent skull");
        var skull=restoredPlayer.Powerups[ArcadePowerupBehavior.WeaponSkull];
        Require(string.IsNullOrEmpty(restoredPlayer.TemporaryWeaponId)&&restoredPlayer.TemporaryWeaponSeconds==0,"Round restore revived expired weapon");
        Require(skull.Magnitude==3&&skull.RemainingSeconds==7,"Round restore lost independent skull tier or reset time");
        Call("TickPlayers",actor,restored,1f,restored.LastTickAt);Require(skull.RemainingSeconds==6,"Restored skull clock failed to continue");
    }
    private static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,args)!;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
