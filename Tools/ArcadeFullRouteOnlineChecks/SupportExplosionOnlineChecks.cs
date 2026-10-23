using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>真实权威强化循环补齐诱饵和爆桶反馈，不修改伤害、次数或触发冷却。</summary>
internal static class SupportExplosionOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks=new (string,Action)[]{("DecoyExpiresOnce",DecoyExpiresOnce),("DecoyBeforeExpiryQuiet",DecoyBeforeExpiryQuiet),
            ("DecoyInRoomQuiet",DecoyInRoomQuiet),("BarrelChargesAndCooldown",BarrelChargesAndCooldown),("BarrelWithoutTargetQuiet",BarrelWithoutTargetQuiet),
            ("OrdinaryOrbitAtActualBody",OrdinaryOrbitAtActualBody),("PurchasedOrbitAtActualBody",PurchasedOrbitAtActualBody),("EmptyDecoyStillHasFeedback",EmptyDecoyStillHasFeedback)};
        if(maximum<checks.Length)throw new ArgumentException("Explicit SupportExplosionOnline maximum >= 8 required");int failed=0;
        foreach(var(name,test) in checks)try{test();Console.WriteLine("PASS SupportExplosionOnline."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL SupportExplosionOnline."+name+": "+(e.InnerException??e).Message);}
        Console.WriteLine($"{checks.Length-failed}/{checks.Length} passed");return failed==0?0:1;
    }
    private static object Call(string method,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(method,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,args)!;
    private static object Fixture(string method,params object[] args)=>typeof(SlayerSupportOnlineChecks).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args)!;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static OnlineArcadeRunActor Create(bool modern=false)
    {
        var record=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,NextArena=2,ArenaConfigNumber=21,IsCasualRoute=true,ConfigFingerprint=ServerConfig.Instance.ArcadeRule.ConfigFingerprint,
            SlayerMartRulesVersion=modern?1:0,SlayerMart=modern?new(){RegionInstanceId="1:slayermart:1",Visit=1,Players=new(){new(){PlayerId=1},new(){PlayerId=2}}}:null,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId="arcade.weapon.basic"},new(){PlayerId=2,Lives=3,Ready=true,WeaponId="arcade.weapon.basic"}}};
        var battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false);battle.Enemies.Clear();battle.EnemyIterationIds.Clear();battle.Crates.Clear();battle.DroppedBarrels.Clear();
        foreach(var p in battle.Players.Values){p.PositionX=p.PositionZ=0;p.ReadyRoundGeneration=battle.RoundGeneration;}
        return new OnlineArcadeRunActor{Record=record,Battle=battle};
    }
    private static OnlineArcadeEnemyRuntime Enemy(OnlineArcadeRunActor a,float x=0,float z=0)
    {var e=new OnlineArcadeEnemyRuntime{RuntimeId=99,PositionX=x,PositionZ=z,CurrentHealth=10000,MaxHealth=10000};a.Battle.Enemies[99]=e;a.Battle.EnemyIterationIds.Add(99);return e;}
    private static OnlineArcadePowerupRuntime Power(OnlineArcadeRunActor a,bool decoy)
    {var p=new OnlineArcadePowerupRuntime{ProductId=decoy?"arcade.powerup.decoy":"arcade.powerup.barrel",Behavior=decoy?ArcadePowerupBehavior.Decoy:ArcadePowerupBehavior.Barrel,RemainingSeconds=decoy?.01f:10,Radius=decoy?3.5f:2,Damage=7,Magnitude=2,Charges=3};a.Battle.Players[1].Powerups[p.Behavior]=p;return p;}
    private static void Step(OnlineArcadeRunActor a,float dt=.02f)=>Call("TickPowerups",a,a.Battle,dt);
    private static void Count(OnlineArcadeRunActor a,int count)
    {using var s=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);Require(s.WeaponFeedback.Count==count,"反馈数="+s.WeaponFeedback.Count);Require(s.WeaponFeedback.All(h=>h.Kind==3&&h.Radius>0),"支援只复用普通爆心");}
    private static void DecoyExpiresOnce(){var a=Create();var p=Power(a,true);p.PositionX=3;p.PositionZ=2;var e=Enemy(a,3,2);Step(a);Require(e.CurrentHealth==9993,"诱饵伤害被改");Count(a,1);var h=a.Battle.WeaponFeedback[0];Require(h.PositionX==3&&h.PositionZ==2&&h.Radius==3.5f,"必须使用诱饵实际爆心与半径");Step(a);Count(a,1);}
    private static void DecoyBeforeExpiryQuiet(){var a=Create();Power(a,true).RemainingSeconds=1;Enemy(a);Step(a);Count(a,0);}
    private static void DecoyInRoomQuiet(){var a=Create();Power(a,true);a.Battle.Players[1].InTreasureRoom=true;Step(a);Count(a,0);}
    private static void BarrelChargesAndCooldown(){var a=Create();var p=Power(a,false);var e=Enemy(a);Step(a);Require(e.CurrentHealth==9993&&p.Charges==2,"爆桶伤害或次数改变");Count(a,1);Step(a);Count(a,1);Require(p.Charges==2,"冷却中不能重复触发");}
    private static void BarrelWithoutTargetQuiet(){var a=Create();var p=Power(a,false);Step(a);Count(a,0);Require(p.Charges==3,"空场不消耗次数");}
    private static void OrdinaryOrbitAtActualBody(){var a=Create(true);Fixture("Ordinary",a,SlayerMartSupportKind.Barrel);var state=a.Battle.SlayerMart.Players.Single(p=>p.PlayerId==1);var item=ArcadeSlayerMartSupportRules.OrdinaryBody(state,SlayerMartSupportKind.Barrel,10);var config=ServerConfig.Instance.SlayerMart;var point=ArcadeSlayerMartSupportRules.Bodies(state,item,0,0,config.Supports,config.Parameter)[0];Enemy(a,point.X,point.Y);Step(a,0);Count(a,1);Require(Math.Abs(a.Battle.WeaponFeedback[0].PositionX-point.X)<.001f,"环绕桶不能错误播放在玩家中心");Step(a,0);Count(a,1);}
    private static void PurchasedOrbitAtActualBody(){var a=Create(true);Fixture("Grant",a,SlayerMartEffect.SawBarrel,1,300f,1L);var item=a.Battle.SlayerMart.Players.Single(p=>p.PlayerId==1).Supports.Single(p=>p.Kind==SlayerMartSupportKind.Barrel);Fixture("AtBody",a.Battle,99L,item);Call("TickSlayerMartSupports",a,a.Battle,0f);Count(a,1);Call("TickSlayerMartSupports",a,a.Battle,0f);Count(a,1);}
    private static void EmptyDecoyStillHasFeedback(){var a=Create();Power(a,true);Step(a);Count(a,1);Require(!a.Battle.Players[1].Powerups.ContainsKey(ArcadePowerupBehavior.Decoy),"到期仍须消费诱饵");}
}
