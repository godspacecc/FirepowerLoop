using System.Reflection;
using ET;
using ET.Server;
using MemoryPack;

/// <summary>地图武器点位的双人权威回归：全地图生成、共同快照、单次领取及预算。</summary>
internal static class MapWeaponPickupsOnlineChecks
{
    /// <summary>执行明确声明的空投与击杀来源隔离检查，失败不扩大测试范围。</summary>
    public static int Run(int maximum)
    {
        var checks = new (string Name, Action Check)[]
        {
            ("AllMapsGenerateEligibleWeaponsAtDistinctConfiguredPoints", AllMapsGenerateEligibleWeaponsAtDistinctConfiguredPoints),
            ("SharedSnapshotAndContactAwardEachPickupOnce", SharedSnapshotAndContactAwardEachPickupOnce),
            ("CapacityAndSpecialBattleGuards", CapacityAndSpecialBattleGuards),
            ("SpawnTimingStaggersAndFreezes", SpawnTimingStaggersAndFreezes),
            ("AirdropWaitsForOpenAndPreservesGroundLifetime", AirdropWaitsForOpenAndPreservesGroundLifetime),
            ("EveryArenaEnemyKillsOnlyDropGoldAndWeaponSkulls", EveryArenaEnemyKillsOnlyDropGoldAndWeaponSkulls),
            ("NegativeEnemyDropGateIsRejectedByServerValidation", NegativeEnemyDropGateIsRejectedByServerValidation),
        };
        if(maximum<checks.Length)throw new ArgumentException("Declared maximum smaller than MapWeaponPickupsOnline scope");
        int failed=0;
        foreach(var check in checks)
        {
            try {check.Check();Console.WriteLine($"PASS MapWeaponPickupsOnline.{check.Name}");}
            catch(Exception e){failed++;Console.WriteLine($"FAIL MapWeaponPickupsOnline.{check.Name}: {e.InnerException??e}");}
        }
        Console.WriteLine($"{checks.Length-failed}/{checks.Length} passed");return failed==0?0:1;
    }

    /// <summary>从真实生成配置创建普通双人轮候选现场，不启动服务器或写入存档。</summary>
    private static OnlineArcadeRunActor Create(int arena=1,int round=1,long runId=1,bool advance=true)
    {
        var rule=ServerConfig.Instance.ArcadeRule;
        var record=new OnlineArcadeRunRecord {RunId=runId,RoomId=2,Revision=1,NextArena=arena,ArenaConfigNumber=arena,
            RoundNumber=round,IsCasualRoute=true,ConfigFingerprint=rule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},
                new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
        var actor=new OnlineArcadeRunActor {Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};
        Require(actor.Battle.Drops.Count==0,"Opening weapons must not appear together at preparation");
        if(advance)
        {
            Call("TickWeaponPickups",actor.Battle,100f);
            Require(actor.Battle.Drops.Count==1,"One tick must spawn at most one weapon");
            Call("TickWeaponPickups",actor.Battle,100f);
        }
        return actor;
    }

    /// <summary>所有轮地图提供两把可用武器；本局身份变化能改变点位和武器，不越过进度门槛。</summary>
    private static void AllMapsGenerateEligibleWeaponsAtDistinctConfiguredPoints()
    {
        var tables=ServerConfig.Instance.Tables;
        foreach(var layout in tables.TbArcadeRoundLayout.DataList)
        {
            var actor=Create(layout.ArenaConfigNumber,layout.RoundNumber);
            var drops=actor.Battle.Drops.Values.ToArray();
            Require(drops.Length==2,layout.Id+": expected two opening weapons");
            Require(drops.Select(d=>(d.PositionX,d.PositionZ)).Distinct().Count()==2,"Duplicate weapon point");
            foreach(var drop in drops)
            {
                var definition=tables.TbArcadeDrop.Get(drop.DropConfigId);
                Require(definition.Kind==ArcadeDropKind.TemporaryWeapon&&definition.MinArena<=actor.Battle.ArenaNumber&&
                    (definition.MaxArena<=0||definition.MaxArena>=actor.Battle.ArenaNumber),"Wrong kind or progression gate");
                Require(layout.WeaponSpawnPoints.Any(p=>p.X==drop.PositionX&&p.Y==drop.PositionZ),"Point is not configured");
                Require(drop.RemainingSeconds>0,"Opening pickup has no ground lifetime");
            }
        }
        var combinations=new HashSet<string>();var products=new HashSet<string>();
        for(long runId=1;runId<=20;runId++)
        {
            var battle=Create(runId:runId).Battle;
            combinations.Add(string.Join(";",battle.Drops.Values.Select(d=>$"{d.PositionX}|{d.PositionZ}").OrderBy(p=>p)));
            foreach(var drop in battle.Drops.Values)products.Add(tables.TbArcadeDrop.Get(drop.DropConfigId).ProductId);
        }
        Require(combinations.Count>4&&products.Count>1&&products.Contains("arcade.weapon.flamethrower"),"Random choices do not vary");
    }

    /// <summary>通过真实投影及生成协议保留同一组实例；两人同时到达时只允许一个领取者。</summary>
    private static void SharedSnapshotAndContactAwardEachPickupOnce()
    {
        var actor=Create();var battle=actor.Battle;
        using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",battle);
        using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);
        using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));
        Require(decoded.Drops.Count==2,"Shared snapshot lost opening pickups");
        foreach(var drop in decoded.Drops)
        {
            var actual=battle.Drops[drop.RuntimeId];
            Require(drop.DropConfigId==actual.DropConfigId&&drop.PositionX==actual.PositionX&&drop.PositionZ==actual.PositionZ,"Shared pickup identity or position changed");
            Require(drop.WeaponAirdrop&&drop.DeliveryRemainingSeconds==actual.DeliveryRemainingSeconds,"Snapshot/clone/codec lost airdrop phase");
        }
        var selected=battle.Drops.Values.First();
        var definition=ServerConfig.Instance.Tables.TbArcadeDrop.Get(selected.DropConfigId);
        foreach(var player in battle.Players.Values){player.PositionX=selected.PositionX;player.PositionZ=selected.PositionZ;}
        Call("TickDropLifetimes",battle,ServerConfig.Instance.ArcadeRule.WeaponDeliverySeconds);
        Call("TickDrops",actor,battle);
        Require(!battle.Drops.ContainsKey(selected.RuntimeId)&&battle.Drops.Count==1,"Pickup did not disappear once");
        Require(battle.Players.Values.Count(p=>p.TemporaryWeaponId==definition.ProductId)==1,"Same pickup equipped both players");
        var winner=battle.Players.Values.Single(p=>p.TemporaryWeaponId==definition.ProductId);
        winner.TemporaryWeaponSeconds=1;
        Call("TickDrops",actor,battle);
        Require(winner.TemporaryWeaponSeconds==1&&battle.Drops.Count==1,"Consumed pickup awarded twice");
    }

    /// <summary>两人站在点位也不能空中领取；落地开箱后才开始30秒地面寿命。</summary>
    private static void AirdropWaitsForOpenAndPreservesGroundLifetime()
    {
        var actor=Create();var battle=actor.Battle;var drop=battle.Drops.Values.First();
        foreach(var player in battle.Players.Values){player.PositionX=drop.PositionX;player.PositionZ=drop.PositionZ;}
        Call("TickDrops",actor,battle);Require(battle.Drops.ContainsKey(drop.RuntimeId),"Airdrop collected in air");
        Call("TickDropLifetimes",battle,.5f);Require(drop.RemainingSeconds==30&&drop.DeliveryRemainingSeconds>0,"Flight consumed ground lifetime");
        float left=drop.DeliveryRemainingSeconds;Call("TickDropLifetimes",battle,0f);Require(drop.DeliveryRemainingSeconds==left,"Zero step advanced delivery");
        Call("TickDropLifetimes",battle,left);Require(drop.RemainingSeconds==30&&drop.DeliveryRemainingSeconds==0,"Landing lost ground lifetime");
        Call("TickDrops",actor,battle);Require(!battle.Drops.ContainsKey(drop.RuntimeId),"Opened crate could not be collected");
    }

    /// <summary>已有物资不被替换，生成不越过总预算；挑战和独立Boss不混入普通地图武器。</summary>
    private static void CapacityAndSpecialBattleGuards()
    {
        var actor=Create(advance:false);var battle=actor.Battle;int maximum=ServerConfig.Instance.ArcadeRule.MaxDrops;
        battle.Drops.Clear();
        for(int i=0;i<maximum-1;i++)battle.Drops.Add(1000+i,new OnlineArcadeDropRuntime {RuntimeId=1000+i,DropConfigId="arcade.drop.gold.small"});
        Call("TickWeaponPickups",battle,100f);
        Require(battle.Drops.Count==maximum&&battle.Drops.ContainsKey(1000),"Opening weapons exceeded capacity or replaced loot");
        Call("TickWeaponPickups",battle,100f);
        Require(battle.Drops.Count==maximum,"Full ground budget was exceeded");
        Require(battle.WeaponSpawns.RemainingCount==1,"Full budget discarded pending weapon");
        foreach(bool challenge in new[]{true,false})
        {
            battle.Drops.Clear();battle.IsChallenge=challenge;actor.Record.RoundBossEncounter=!challenge;
            Call("InitializeWeaponPickups",actor.Record,battle);
            Call("TickWeaponPickups",battle,100f);
            Require(battle.Drops.Count==0,"Special battle received ordinary weapon pickups");
        }
    }

    /// <summary>下一把重新等待正间隔，场景、房间、清场和零步长都不会追赶生成。</summary>
    private static void SpawnTimingStaggersAndFreezes()
    {
        var actor=Create(advance:false);var battle=actor.Battle;var schedule=battle.WeaponSpawns;
        Require(schedule.RemainingSeconds>=3&&schedule.RemainingSeconds<=8,"Invalid first delay");
        Call("TickWeaponPickups",battle,schedule.RemainingSeconds-.01f);
        Require(battle.Drops.Count==0,"First weapon appeared early");
        Call("TickWeaponPickups",battle,.02f);
        Require(battle.Drops.Count==1&&schedule.RemainingSeconds>=20&&schedule.RemainingSeconds<=30,"Second weapon should wait 20-30 active seconds after the first");
        float delay=schedule.RemainingSeconds;
        foreach(string gate in new[]{"transition","room","clear","zero"})
        {
            battle.RoundTransitionPending=gate=="transition";battle.Players[1].InTreasureRoom=gate=="room";battle.WaveClearAwarded=gate=="clear";
            Call("TickWeaponPickups",battle,gate=="zero"?0f:100f);
            Require(battle.Drops.Count==1&&schedule.RemainingSeconds==delay,"Clock advanced through "+gate);
        }
        battle.WaveClearAwarded=false;
        Call("TickWeaponPickups",battle,delay-.01f);
        Require(battle.Drops.Count==1,"Second weapon appeared early");
        Call("TickWeaponPickups",battle,.02f);
        Require(battle.Drops.Count==2,"Second weapon failed to appear after its delay");
    }

    /// <summary>经过真实服务端死亡入口覆盖前中后期，禁止武器及补给混入击杀来源。</summary>
    private static void EveryArenaEnemyKillsOnlyDropGoldAndWeaponSkulls()
    {
        var allowed = new HashSet<string> { "arcade.drop.gold.small", "arcade.drop.skull.small", "arcade.drop.skull.large" };
        foreach (int arena in new[] { 1, 2, 6, 12, 20 })
        {
            var actor = Create(arena, advance: false);
            var battle = actor.Battle;
            var seen = new HashSet<string>();
            for (int id = 1; id <= 1024; id++)
            {
                battle.Drops.Clear();
                var enemy = new OnlineArcadeEnemyRuntime { RuntimeId = id, EnemyConfigId = "enemy.arcade.basic", MaxHealth = 100 };
                battle.Enemies.Add(id, enemy);
                Call("KillEnemy", actor, battle, enemy, false);
                foreach (var drop in battle.Drops.Values)
                {
                    Require(allowed.Contains(drop.DropConfigId), $"Arena {arena} received forbidden kill loot: {drop.DropConfigId}");
                    seen.Add(drop.DropConfigId);
                }
            }
            Require(seen.SetEquals(allowed), $"Arena {arena} did not exercise all three allowed loot kinds");
        }
    }

    /// <summary>模拟损坏表行经过真实服务端校验，负数须报错，结束后恢复共用内存配置。</summary>
    private static void NegativeEnemyDropGateIsRejectedByServerValidation()
    {
        var config = ServerConfig.Instance;
        var drop = config.Tables.TbArcadeDrop.DataList.First();
        var field = typeof(ArcadeDropConfig).GetField("EnemyMinArena")!;
        int previous = drop.EnemyMinArena;
        bool rejected = false;
        try
        {
            field.SetValue(drop, -1);
            try { typeof(ServerConfig).GetMethod("ValidateArcadeConfig", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(config, null); }
            catch (TargetInvocationException error) when (error.InnerException?.Message.Contains("TbArcadeDrop") == true)
            { rejected = true; }
        }
        finally { field.SetValue(drop, previous); }
        Require(rejected, "Server accepted a negative EnemyMinArena instead of rejecting the malformed table");
    }

    private static object Call(string name,params object[] values)=>typeof(OnlineArcadeRunActorSystem)
        .GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,values)!;
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
