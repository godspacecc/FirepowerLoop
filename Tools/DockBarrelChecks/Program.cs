using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

// 仅运行指定的四项码头桶回归，错误过滤绝不回退全套。
if (args.Length != 3 || args[0] != "testClass=DockBarrel" || args[1] != "maxTests=4" || args[2] != "includePassingTests=true")
    throw new ArgumentException("Requires testClass=DockBarrel maxTests=4 includePassingTests=true");
World.Instance.AddSingleton(ServerConfig.CreateForValidation("OnlineLocal", n => File.ReadAllBytes(Path.Combine("Server/GameServer/Config/Luban", n + ".bytes"))));
World.Instance.AddSingleton<TimeInfo>(); World.Instance.AddSingleton<ObjectPool>();
return DockBarrelChecks.Run();

/// <summary>消费正式码头配置和真实服务端弹丸入口，复现海中遮挡与载桶状态。</summary>
internal static class DockBarrelChecks
{
    public static int Run()
    {
        var checks = new (string Name, Action Check)[] {
            ("WaterEnemyDoesNotBlockGroundBarrel", Water), ("LandedCarrierBreaksAfterTwoShots", Landed),
            ("CarrierKeepsDamagedBarrel", Persistent), ("OrdinaryEnemyStillBlocksGroundBarrel", Ordinary) };
        int failed = 0;
        foreach (var (name, check) in checks)
            try { check(); Console.WriteLine("PASS DockBarrel." + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL DockBarrel." + name + ": " + (ex.InnerException ?? ex)); }
        Console.WriteLine($"{checks.Length-failed}/{checks.Length} passed"); return failed == 0 ? 0 : 1;
    }
    /// <summary>从正式第二关出生一个海路载桶敌人，清除无关箱体与预置桶以隔离射线。</summary>
    private static (OnlineArcadeRunActor Actor, OnlineArcadeEnemyRuntime Enemy) Ready()
    {
        var rule = ServerConfig.Instance.ArcadeRule;
        var record = new OnlineArcadeRunRecord { RunId=9155, RoomId=2, Revision=1, NextArena=2, ArenaConfigNumber=21,
            RoundNumber=1, IsCasualRoute=true, CasualRouteVersion=1, ConfigFingerprint=rule.ConfigFingerprint,
            Players=new() { new() { PlayerId=1, Lives=3, Ready=true, WeaponId=rule.BaseWeaponId }, new() { PlayerId=2, Lives=3, Ready=true, WeaponId=rule.BaseWeaponId } } };
        var actor = new OnlineArcadeRunActor { Record=record, Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false) };
        var b=actor.Battle;
        foreach (var p in b.Players.Values) { p.ReadyRoundGeneration=b.RoundGeneration; p.PositionX=0; p.PositionZ=-4; }
        b.SpawnQueue.Clear(); b.SpawnQueue.Add("enemy.arcade.basic"); b.SpawnQueueIndex=0; b.SpawnedInWave=1; b.NextSpawnAt=0;
        Require((bool)Call("SpawnQueuedEnemy",b,10000L,2,null), "海路真实出生失败");
        var enemy=b.Enemies.Values.Single(); Require(enemy.SpecialKind==1&&enemy.DockSeaEntry.Started,"海路载桶身份丢失");
        enemy.CurrentHealth=enemy.MaxHealth=1000;
        b.EnemyIterationIds.Clear(); b.EnemyIterationIds.Add(enemy.RuntimeId); b.DroppedBarrels.Clear(); b.Crates.Clear();
        for(int i=0;i<b.RoundLayout.Barrels.Count;i++)b.DestroyedBarrelIds.Add(i);
        return (actor,enemy);
    }
    /// <summary>长帧越过敌人与后方地面桶，验证完整弹丸更新没有截住海中目标。</summary>
    private static void Water()
    {
        foreach(float age in new[]{0f,2.1f,5.74f})
        {
            var(a,e)=Ready(); Call("TickEnemies",a,a.Battle,age);
            e.PositionX=0; e.PositionZ=0; a.Battle.DroppedBarrels.Add(100,new ArcadeRegionPoint(2,0));
            Shot(a,-2,0,5,36);
            Require(a.Battle.GroundBarrelHealth.GetValueOrDefault(100)==4,"海中敌人截住射向后方40耐久桶的36伤害子弹");
            Require(e.CurrentHealth==1000&&e.CarriedBarrelHealth==0&&a.Battle.Projectiles.Count==0,"海中载桶被误伤或子弹未消费");
        }
    }
    private static void Landed()
    {
        var(a,e)=Ready(); Call("TickEnemies",a,a.Battle,ArcadeDockSeaEntryRules.ClimbSeconds);
        Require(e.DockSeaEntry.CanBeAttacked&&a.Battle.RoundLayout.BarrelHealth==40,"登岸或正式40耐久前提失败");
        Shot(a,e.PositionX-1,e.PositionZ,1,36);
        Require(e.SpecialKind==1&&e.CarriedBarrelHealth==4&&e.CurrentHealth==1000,"第一发未先扣背桶");
        Shot(a,e.PositionX-1,e.PositionZ,1,36);
        Require(e.SpecialKind==0&&e.CarriedBarrelHealth==0&&a.Battle.DeathZones.Count(z=>z.Kind==14)==1,"第二发未独立引爆背桶");
        Require(a.Battle.Players.Values.All(p=>p.CurrentHealth==p.MaxHealth),"背桶爆炸误伤玩家");
    }
    private static void Persistent()
    {
        var(a,e)=Ready(); Call("TickEnemies",a,a.Battle,ArcadeDockSeaEntryRules.ClimbSeconds);
        Shot(a,e.PositionX-1,e.PositionZ,1,36);
        a.Battle.ContentClock.Advance(60,false); Call("TickVortexBarrels",a.Battle,1f);
        Require(e.SpecialKind==1&&e.CarriedBarrelHealth==4&&a.Battle.DroppedBarrels.Count==0,"时间到后卸桶或恢复耐久");
    }
    private static void Ordinary()
    {
        var(a,e)=Ready(); e.DockSeaEntry=default; e.SpecialKind=0; e.PositionX=0; e.PositionZ=0;
        a.Battle.DroppedBarrels.Add(100,new ArcadeRegionPoint(2,0)); Shot(a,-2,0,5,36);
        Require(e.CurrentHealth==964&&!a.Battle.GroundBarrelHealth.ContainsKey(100)&&a.Battle.Projectiles.Count==0,"正常敌人未挡住后方桶弹道");
    }
    /// <summary>实际弹丸更新；每次只发一颗36伤害普通弹，命中后允许复用测试身份。</summary>
    private static void Shot(OnlineArcadeRunActor a,float x,float z,float velocity,int damage)
    {
        a.Battle.Projectiles.Add(900,new OnlineArcadeProjectileRuntime { RuntimeId=900, OwnerPlayerId=1, FromPlayerWeapon=true,
            PositionX=x, PositionZ=z, VelocityX=velocity, RemainingSeconds=3, RemainingHits=1, Damage=damage });
        Call("TickProjectiles",a,a.Battle,1f);
    }
    private static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!.Invoke(null,args)!;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
