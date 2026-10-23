using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;
/// <summary>紫级进化真实权威入口，不启动网络服务或写存档。</summary>
internal static class WeaponEvolutionOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks=new (string,Action)[]{("BasicKillThrough",BasicKillThrough),("RapidSwarm",RapidSwarm),("RedUnchanged",RedUnchanged),
            ("ShotgunPush",ShotgunPush),("FlameGround",FlameGround),("ChargedPiercing",ChargedPiercing),("ClusterOnce",ClusterOnce),
            ("DronePair",DronePair),("BudgetKeepsPhase",BudgetKeepsPhase),("OwnerAndZeroStep",OwnerAndZeroStep),("ExtraDroneSnapshot",ExtraDroneSnapshot),("LongStepOrder",LongStepOrder),("SwitchResets",SwitchResets),("InterleavedShock",InterleavedShock),("OverlapStable",OverlapStable),("BarrelAfterEnemy",BarrelAfterEnemy),("WideBeforeBarrel",WideBeforeBarrel),("DryGapResets",DryGapResets),("SkullRepickResets",SkullRepickResets),("LongFrameDryGap",LongFrameDryGap),("ClusterVisualSnapshot",ClusterVisualSnapshot),("BossExplosionCannotRecreateClearedChildren",BossExplosionCannotRecreateClearedChildren),("GuardianCleanup",GuardianCleanup),("FuseFrameStillHitsBarrelBeforeEndpoint",FuseFrameStillHitsBarrelBeforeEndpoint)};
        if(maximum<checks.Length)throw new ArgumentException("WeaponEvolutionOnline maximum too small");int failed=0;
        foreach(var(name,test)in checks)try{test();Console.WriteLine("PASS WeaponEvolutionOnline."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL WeaponEvolutionOnline."+name+": "+(e.InnerException??e));}
        Console.WriteLine($"{checks.Length-failed}/{checks.Length} passed");return failed==0?0:1;
    }
    private static OnlineArcadeRunActor Create(string weapon,bool purple=true,int arena=1)
    {
        var rule=ServerConfig.Instance.ArcadeRule;
        var record=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,NextArena=arena==1?1:2,ArenaConfigNumber=arena,IsCasualRoute=true,ConfigFingerprint=rule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
        var a=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};
        foreach(var p in a.Battle.Players.Values){p.PositionX=p.PositionZ=0;p.AimX=0;p.AimZ=1;p.ReadyRoundGeneration=a.Battle.RoundGeneration;}
        if(weapon!="basic")Apply(a,"weapon."+weapon);Apply(a,purple?"skull.large":"skull.small");return a;
    }
    private static void Apply(OnlineArcadeRunActor a,string suffix)=>Require((bool)Call("ApplyDrop",a,a.Battle.Players[1],ServerConfig.Instance.Tables.TbArcadeDrop.Get("arcade.drop."+suffix)),"Drop rejected "+suffix);
    private static void Fire(OnlineArcadeRunActor a){a.Battle.Players[1].FireCooldownSeconds=0;Require((bool)Call("Fire",a,a.Battle,a.Battle.Players[1],a.Record.Players[0]),"Fire rejected");}
    private static void Tick(OnlineArcadeRunActor a,float dt){a.Battle.EnemyIterationIds.Clear();a.Battle.EnemyIterationIds.AddRange(a.Battle.Enemies.Keys);Call("TickProjectiles",a,a.Battle,dt);}
    private static OnlineArcadeEnemyRuntime Enemy(OnlineArcadeRunActor a,long id,float x,float z,int health=1000)
    {var e=new OnlineArcadeEnemyRuntime{RuntimeId=id,PositionX=x,PositionZ=z,CurrentHealth=health,MaxHealth=health};a.Battle.Enemies[id]=e;return e;}
    private static void BasicKillThrough(){var a=Create("basic");Enemy(a,501,0,1,1);var back=Enemy(a,502,0,3);Fire(a);Tick(a,.022f);Require(a.Battle.Projectiles.Count==1,"kill removed shot");Tick(a,.11f);Require(back.CurrentHealth<1000&&a.Battle.Projectiles.Count==0,"survivor must stop shot");}
    private static void RapidSwarm(){var a=Create("rapid");Fire(a);Fire(a);Fire(a);Require(a.Battle.Projectiles.Count==5&&a.Battle.Projectiles.Values.Select(p=>p.VelocityX).Distinct().Count()==3,"third volley missing side shots");}
    private static void RedUnchanged(){var a=Create("rapid",false);for(int i=0;i<6;i++)Fire(a);Require(a.Battle.Projectiles.Count==6,"red got side shots");var b=Create("grenade",false);Fire(b);Tick(b,1);Require(b.Battle.Projectiles.Count==0,"red grenade split");}
    private static void ShotgunPush(){var a=Create("spread");Enemy(a,501,0,1);var near=Enemy(a,502,.9f,1.2f);Fire(a);Tick(a,.025f);Require(near.PositionZ>1.3f&&Math.Sqrt((near.PositionX-.9f)*(near.PositionX-.9f)+(near.PositionZ-1.2f)*(near.PositionZ-1.2f))<=1.21f,"shockwave absent or repeated per pellet");}
    private static void FlameGround(){var a=Create("flamethrower");Fire(a);Tick(a,.1f);a.Battle.Projectiles.Clear();var e=Enemy(a,501,0,1.4f);for(int i=0;i<5;i++)Tick(a,.1f);Require(e.CurrentHealth<1000&&e.LastDamagePlayerId==1,"ground damage missing owner");for(int i=0;i<30;i++)Tick(a,.1f);int health=e.CurrentHealth;Tick(a,.5f);Require(e.CurrentHealth==health&&a.Battle.WeaponBurnAreas.Count==0,"fire never expired");}
    private static void ChargedPiercing(){var a=Create("piercing");for(int i=0;i<3;i++){Fire(a);a.Battle.Projectiles.Clear();}var e=Enemy(a,501,1.6f,1.3f);Fire(a);Tick(a,.035f);Require(e.CurrentHealth<1000,"fourth shot not wider");}
    private static void ClusterOnce(){var a=Create("grenade");Fire(a);var parent=a.Battle.Projectiles.Values.Single();float z=parent.PositionZ+parent.VelocityZ*parent.RemainingSeconds;Tick(a,1);Require(a.Battle.Projectiles.Count==3&&a.Battle.Projectiles.Values.All(p=>p.OwnerPlayerId==1),"cluster missing owner/children");Require(a.Battle.Projectiles.Values.All(p=>Math.Abs(p.PositionZ-z)<.001f),"long frame flew past primary fuse");var children=a.Battle.Projectiles.Values.ToArray();var expected=children.Select(p=>(p.PositionX+p.VelocityX*p.RemainingSeconds,p.PositionZ+p.VelocityZ*p.RemainingSeconds)).ToArray();Tick(a,1);Require(a.Battle.Projectiles.Count==0,"recursive cluster");Require(a.Battle.WeaponFeedback.Where(h=>h.Kind==5).All(h=>expected.Any(e=>Math.Abs(e.Item1-h.PositionX)<.001f&&Math.Abs(e.Item2-h.PositionZ)<.001f)),"children flew past fuse");}
    private static void DronePair(){var a=Create("rocket");Apply(a,"support.drone");Enemy(a,501,-2,4);Enemy(a,502,2,4);var p=a.Battle.Players[1];Call("TickDrone",a,a.Battle,p,p.Powerups[ArcadePowerupBehavior.Drone],.01f);Require(a.Battle.Projectiles.Count==2&&a.Battle.Projectiles.Values.All(s=>!s.FromPlayerWeapon&&s.Behavior==ArcadeWeaponBehavior.Direct),"drone copied weapon or no pair");Require(a.Battle.Projectiles.Values.Select(s=>Math.Sign(s.VelocityX)).Distinct().Count()==2,"pair did not split targets");a.Battle.Projectiles.Clear();p.Powerups.Remove(ArcadePowerupBehavior.WeaponSkull);Call("TickDrone",a,a.Battle,p,p.Powerups[ArcadePowerupBehavior.Drone],.6f);Require(a.Battle.Projectiles.Count==1&&p.Powerups[ArcadePowerupBehavior.Drone].ExtraDrone==null,"extra drone survived downgrade");}
    private static void BudgetKeepsPhase(){var a=Create("rapid");Fire(a);Fire(a);var b=a.Battle;int cap=ServerConfig.Instance.ArcadeRule.MaxProjectiles;while(b.Projectiles.Count<cap-2){long id=b.NextProjectileRuntimeId++;b.Projectiles[id]=new(){RuntimeId=id};}var p=b.Players[1];p.FireCooldownSeconds=0;Require(!(bool)Call("Fire",a,b,p,a.Record.Players[0])&&p.FireSequence==2,"full budget spent phase");b.Projectiles.Clear();Fire(a);Require(b.Projectiles.Count==3,"failed fire lost swarm");}
    private static void OwnerAndZeroStep(){var a=Create("grenade");Fire(a);var shot=a.Battle.Projectiles.Values.Single();float z=shot.PositionZ;Tick(a,0);Require(shot.PositionZ==z&&a.Battle.Projectiles.Count==1,"zero step detonated");Tick(a,1);Require(a.Battle.Projectiles.Values.All(p=>p.OwnerPlayerId==1&&p.EvolutionClusterChild),"child ownership lost");}
    private static void ExtraDroneSnapshot()
    {
        var a=Create("rocket");Apply(a,"support.drone");Enemy(a,501,-2,4);Enemy(a,502,2,4);var p=a.Battle.Players[1];Call("TickDrone",a,a.Battle,p,p.Powerups[ArcadePowerupBehavior.Drone],.01f);
        using var source=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);
        using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",source);
        using var snapshot=MemoryPack.MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPack.MemoryPackSerializer.Serialize(clone));
        var drone=snapshot.Players.Single(p=>p.PlayerId==1).Powerups.Single(p=>p.ProductId=="arcade.support.drone");
        Require(typeof(OnlineArcadePowerupInfo).GetProperty("HasExtraDrone")?.GetValue(drone) is true,"extra drone absent from network snapshot");
    }
    private static void LongStepOrder(){var a=Create("basic");var back=Enemy(a,502,0,4);Enemy(a,501,0,2,1);Fire(a);Tick(a,.25f);Require(!a.Battle.Enemies.ContainsKey(501)&&back.CurrentHealth<1000,"long step skipped front kill");}
    private static void SwitchResets(){var a=Create("rapid");Fire(a);Fire(a);Apply(a,"weapon.spread");Apply(a,"weapon.rapid");a.Battle.Projectiles.Clear();Fire(a);Require(a.Battle.Projectiles.Count==1,"switch kept charge");}
    private static void InterleavedShock()
    {
        var a=Create("spread");var e=Enemy(a,501,0,1);Fire(a);var first=a.Battle.Projectiles.Values.ToArray();a.Battle.Projectiles.Clear();Fire(a);var second=a.Battle.Projectiles.Values.ToArray();
        Call("PushPurpleShotgun",a.Battle,first[0],e.PositionX,e.PositionZ);Call("PushPurpleShotgun",a.Battle,second[0],e.PositionX,e.PositionZ);float z=e.PositionZ;
        Call("PushPurpleShotgun",a.Battle,first[1],e.PositionX,e.PositionZ);Require(e.PositionZ==z,"interleaved volley pushed twice");
    }
    private static void OverlapStable()
    {
        int Damage(float dt){var a=Create("flamethrower");var e=Enemy(a,501,0,0);a.Battle.WeaponBurnAreas.Add(new(){Id=700,OwnerId=1,X=-.45f,Z=0,Damage=4,RemainingSeconds=2,TickRemainingSeconds=.25f});a.Battle.WeaponBurnAreas.Add(new(){Id=701,OwnerId=1,X=.45f,Z=0,Damage=4,RemainingSeconds=2,TickRemainingSeconds=.125f});for(int i=0;i<(int)MathF.Round(1/dt);i++)Tick(a,dt);return 1000-e.CurrentHealth;}
        Require(Damage(.125f)==Damage(.25f),"overlap damage depends on frame");
    }
    private static void BarrelAfterEnemy()
    {
        var a=Create("piercing",true,3);var b=a.Battle;var p=b.Players[1];p.PositionZ=-8;b.Crates.Clear();b.DroppedBarrels.Clear();b.GroundBarrelHealth.Clear();
        b.DroppedBarrels[100]=new ArcadeRegionPoint(0,-2);Enemy(a,501,0,-5);Fire(a);Tick(a,.6f);
        Require(b.GroundBarrelHealth.ContainsKey(100)||!b.DroppedBarrels.ContainsKey(100),"piercing skipped barrel after enemy");
    }
    private static void WideBeforeBarrel()
    {
        var a=Create("piercing",true,3);var b=a.Battle;b.Players[1].PositionZ=-8;b.Crates.Clear();b.DroppedBarrels.Clear();b.GroundBarrelHealth.Clear();
        b.DroppedBarrels[100]=new ArcadeRegionPoint(0,-2);b.GroundBarrelHealth[100]=1000;
        for(int i=0;i<3;i++){Fire(a);b.Projectiles.Clear();}var enemy=Enemy(a,501,1.6f,-5);Fire(a);b.Projectiles.Values.Single().Damage=1;Tick(a,.6f);
        Require(enemy.CurrentHealth==999,"wide beam skipped side enemy before barrel");
    }
    private static void DryGapResets()
    {
        var a=Create("flamethrower");var enemy=Enemy(a,501,0,0);a.Battle.WeaponBurnAreas.Add(new(){Id=700,OwnerId=1,Damage=4,RemainingSeconds=.2f});
        Tick(a,.2f);Tick(a,.5f);a.Battle.WeaponBurnAreas.Add(new(){Id=701,OwnerId=1,Damage=4,RemainingSeconds=2});Tick(a,.05f);
        Require(enemy.CurrentHealth==1000,"new ground inherited expired contact clock");
    }
    private static void FuseFrameStillHitsBarrelBeforeEndpoint()
    {
        var a=Create("grenade",true,3);var b=a.Battle;b.Crates.Clear();Fire(a);var p=b.Projectiles.Values.Single();p.PositionX=p.PositionZ=0;p.VelocityX=0;p.VelocityZ=3;p.RemainingSeconds=.35f;p.EvolutionClusterChild=true;
        b.DroppedBarrels.Clear();b.DroppedBarrels[100]=new ArcadeRegionPoint(0,.6f);b.GroundBarrelHealth[100]=1000;Tick(a,1);
        Require(b.GroundBarrelHealth[100]<1000,"fuse expiration skipped barrel on valid path");
        Require(b.WeaponFeedback.Single(h=>h.Kind==5).PositionZ<.6f,"blast crossed the first barrel");
        b.DroppedBarrels.Clear();b.GroundBarrelHealth.Clear();b.WeaponFeedback.Clear();Fire(a);p=b.Projectiles.Values.Single();p.PositionX=p.PositionZ=0;p.VelocityX=0;p.VelocityZ=3;p.RemainingSeconds=.35f;p.EvolutionClusterChild=true;
        b.Crates[1]=new ArcadeCrateState(new ArcadeBabylonParameters.Crate(1,0,.6f,.1f,1000));Tick(a,1);
        Require(b.Crates[1].Health<1000&&b.WeaponFeedback.Single(h=>h.Kind==5).PositionZ<.6f,"fuse expiration skipped crate on valid path");
    }
    private static void GuardianCleanup()
    {
        var a=SlayerChallengeOnlineChecks.Actor();SlayerChallengeOnlineChecks.Start(a);Call("TickSlayerChallenge",a,a.Battle,90f);
        Require(a.Battle.Enemies.Count==1,"actual Guardian absent");a.Battle.WeaponBurnAreas.Add(new(){Id=700,RemainingSeconds=2});
        foreach(var p in a.Battle.Players.Values)p.WeaponEvolution.Commit("arcade.weapon.rapid",2,2);
        Call("KillEnemy",a,a.Battle,a.Battle.Enemies.Values.Single(),false);
        Require(SlayerChallengeOnlineChecks.State(a.Battle).Phase==ArcadeSlayerChallengePhase.Shopping&&a.Battle.WeaponBurnAreas.Count==0&&a.Battle.Players.Values.All(p=>p.WeaponEvolution.Shots==0),"shopping kept evolution state");
    }
    private static void BossExplosionCannotRecreateClearedChildren()
    {
        var a=Create("grenade");Fire(a);var parent=a.Battle.Projectiles.Values.Single();var boss=Enemy(a,501,parent.PositionX,parent.PositionZ,1);boss.ContentBossKind=1;
        a.Battle.WeaponBurnAreas.Add(new(){Id=700,OwnerId=1,Damage=4,RemainingSeconds=2});
        a.Battle.EnemyIterationIds.Add(501);Call("DamageEnemiesInProjectileExplosion",a,a.Battle,parent);
        Require(a.Battle.RoundBossDefeated&&a.Battle.AwaitingExit,"boss did not die");
        Require(a.Battle.Projectiles.Count==0&&a.Battle.WeaponBurnAreas.Count==0&&a.Battle.Players.Values.All(p=>p.WeaponEvolution.Shots==0),"boss cleanup recreated evolution state");
    }
    private static void LongFrameDryGap()
    {
        var a=Create("flamethrower");var enemy=Enemy(a,501,0,0);a.Battle.WeaponBurnAreas.Add(new(){Id=700,OwnerId=1,Damage=4,RemainingSeconds=.2f});
        Tick(a,.3f);a.Battle.WeaponBurnAreas.Add(new(){Id=701,OwnerId=1,Damage=4,RemainingSeconds=2});Tick(a,.05f);
        Require(enemy.CurrentHealth==1000,"dry remainder of long frame kept contact");
    }
    private static void ClusterVisualSnapshot()
    {
        var a=Create("grenade");Fire(a);Tick(a,1);
        a.Battle.WeaponBurnAreas.Add(new(){Id=700,OwnerId=1,Damage=4,RemainingSeconds=.8f});
        using var source=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",source);
        using var snapshot=MemoryPack.MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPack.MemoryPackSerializer.Serialize(clone));
        Require(snapshot.ContentAreas.Single(c=>c.Kind==31).RemainingSeconds==.8f,"ground lifetime lost");
        Require(snapshot.Projectiles.Count==3&&snapshot.Projectiles.All(p=>p.EvolutionClusterChild&&p.RemainingSeconds>0),"child travel phase lost");
        Require(snapshot.WeaponFeedback.Any(h=>h.Kind==4&&h.Radius>0),"primary blast receipt absent");
        Tick(a,1);Require(a.Battle.WeaponFeedback.Count(h=>h.Kind==5&&h.Radius>0)==3,"child blast receipts absent");
    }
    private static void SkullRepickResets()
    {
        var a=Create("rapid");Fire(a);Fire(a);a.Battle.Projectiles.Clear();var p=a.Battle.Players[1];p.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds=.01f;
        Call("TickPlayers",a,a.Battle,.02f,TimeInfo.Instance.ServerNow());Apply(a,"skull.large");Fire(a);Require(a.Battle.Projectiles.Count==1,"repick kept old phase");
    }
    private static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,args)!;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
