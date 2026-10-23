using System.Collections;
using System.Reflection;
using ET;
using ET.Server;
using MemoryPack;

/// <summary>实际双人机关裁决与消息序列化，不监听端口，不读取任何玩家存档。</summary>
internal static class ElectricTowerOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks=new (string,Action)[]{("FatalAndEnemyHitsSerialize",FatalAndEnemyHitsSerialize),("InvulnerableTargetsHaveNoReceipt",InvulnerableTargetsHaveNoReceipt),("ShieldContactStillReceivesFeedback",ShieldContactStillReceivesFeedback)};
        if(maximum<checks.Length)throw new ArgumentException("ElectricTowerOnline requires maximum >= 3");int failures=0;
        foreach(var (name,check) in checks)try{check();Console.WriteLine("PASS ElectricTowerOnline."+name);}catch(Exception e){failures++;Console.WriteLine("FAIL ElectricTowerOnline."+name+": "+(e.InnerException??e));}
        Console.WriteLine($"{checks.Length-failures}/{checks.Length} passed");return failures==0?0:1;
    }
    private static OnlineArcadeRunActor Spawn()
    {
        var record=new OnlineArcadeRunRecord{RunId=71,RoomId=72,Revision=1,NextArena=2,ArenaConfigNumber=21,RoundNumber=1,IsCasualRoute=true,ConfigFingerprint=ServerConfig.Instance.ArcadeRule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=ServerConfig.Instance.ArcadeRule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=ServerConfig.Instance.ArcadeRule.BaseWeaponId}}};
        var actor=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};var b=actor.Battle;
        var p=b.Hazards.Single().Parameters;b.HazardElapsedSeconds=5.6f;b.Hazards[0].Advance(5.6f);
        foreach(var player in b.Players.Values){player.PositionX=player.HazardPreviousX=p.X;player.PositionZ=player.HazardPreviousZ=p.Y;player.InvulnerabilitySeconds=0;}
        return actor;
    }
    private static IList Receipts(object source)
    {
        var property=source.GetType().GetProperty("ElectricHits");Require(property!=null,"真实命中需要独立有界收据，不能从血量变化推断");
        return (IList)property!.GetValue(source)!;
    }
    private static void FatalAndEnemyHitsSerialize()
    {
        var actor=Spawn();var b=actor.Battle;var p=b.Hazards[0].Parameters;
        b.Players[2].PositionX=b.Players[2].HazardPreviousX=p.X+10;
        b.Enemies.Add(123,new OnlineArcadeEnemyRuntime{RuntimeId=123,PositionX=p.X,PositionZ=p.Y,HazardPreviousX=p.X,HazardPreviousZ=p.Y,CurrentHealth=100,MaxHealth=100});
        Call("TickHazards",actor,b,.1f);Require(Receipts(b).Count==1,"致命玩家接触必须先交付命中再进入复活等待");
        Require(actor.Record.Players[0].Lives==2,"一击只扣原规则一命");b.Players[1].RespawnSeconds=0;
        Call("TickHazards",actor,b,.1f);Require(Receipts(b).Count==2,"敌人电击没有正式反馈");
        using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",b);using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);
        using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));
        Require(Receipts(decoded).Count==2,"双人快照克隆或编码丢失电击事实");
    }
    private static void InvulnerableTargetsHaveNoReceipt()
    {
        var actor=Spawn();foreach(var player in actor.Battle.Players.Values)player.InvulnerabilitySeconds=10;
        Call("TickHazards",actor,actor.Battle,.1f);Require(Receipts(actor.Battle).Count==0,"无敌目标不得产生假命中");
    }
    private static void ShieldContactStillReceivesFeedback()
    {
        var actor=Spawn();var b=actor.Battle;b.Players[2].DashSeconds=1;
        b.Players[1].Powerups.Add(ArcadePowerupBehavior.Shield,new OnlineArcadePowerupRuntime{Behavior=ArcadePowerupBehavior.Shield,Charges=1,RemainingSeconds=10});
        Call("TickHazards",actor,b,.1f);Require(Receipts(b).Count==1,"护盾挡电击仍应显示接触，冲刺目标不应显示");
        Require(actor.Record.Players[0].Lives==3&&b.Players[1].CurrentHealth==b.Players[1].MaxHealth,"不得改变护盾挡伤");
        Call("TickHazards",actor,b,.1f);Require(Receipts(b).Count==1,"同次接触重复反馈");
    }
    private static object Call(string method,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(method,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args)!;
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
