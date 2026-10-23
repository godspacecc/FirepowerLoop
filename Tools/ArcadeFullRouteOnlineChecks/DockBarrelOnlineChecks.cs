using System.Reflection;
using ET;
using ET.Server;
using MemoryPack;

/// <summary>用正式码头配置及权威出生入口验证载桶、卸桶与双人快照。</summary>
internal static class DockBarrelOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks=new (string Name,Action Check)[]{("FourRoundsSpawnCarriers",FourRoundsSpawnCarriers),("NaturalCarriersDropAndSerializeOnce",NaturalCarriersDropAndSerializeOnce),("EarlyDeathKeepsPurpleBlastAndBothPlayersSafe",EarlyDeathKeepsPurpleBlastAndBothPlayersSafe)};
        if(maximum<checks.Length)throw new ArgumentException("DockBarrelOnline requires maximum >= 3");int failures=0;
        foreach(var check in checks)try{check.Check();Console.WriteLine("PASS DockBarrelOnline."+check.Name);}catch(Exception e){failures++;Console.WriteLine("FAIL DockBarrelOnline."+check.Name+": "+(e.InnerException??e));}
        Console.WriteLine($"{checks.Length-failures}/{checks.Length} passed");return failures==0?0:1;
    }
    /// <summary>不启动监听或读取玩家档；从实际第二关第指定轮建立双人战斗。</summary>
    private static OnlineArcadeRunActor Spawn(int round)
    {
        var record=new OnlineArcadeRunRecord{RunId=71,RoomId=72,Revision=1,NextArena=2,ArenaConfigNumber=21,RoundNumber=round,IsCasualRoute=true,ConfigFingerprint=ServerConfig.Instance.ArcadeRule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=ServerConfig.Instance.ArcadeRule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=ServerConfig.Instance.ArcadeRule.BaseWeaponId}}};
        var actor=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};var b=actor.Battle;
        foreach(var player in b.Players.Values)player.ReadyRoundGeneration=b.RoundGeneration;
        int budget=b.SpawnQueue.Count;b.MaxAlive=budget;
        for(long now=1000;b.SpawnQueueIndex<budget&&now<=301000;now+=1000)Call("TrySpawnEnemy",b,now);
        Require(b.Enemies.Count==budget&&b.SpawnedInWave==budget,"实际通道未交付既有出生预算");return actor;
    }
    private static void FourRoundsSpawnCarriers()
    {
        for(int round=1;round<=4;round++)
        {var b=Spawn(round).Battle;int count=b.Enemies.Values.Count(e=>e.SpecialKind==1);Require(count>0&&count<b.Enemies.Count,"第"+round+"轮载桶缺失或替换了全部普通敌人");}
    }
    private static void NaturalCarriersDropAndSerializeOnce()
    {
        var actor=Spawn(2);var b=actor.Battle;var carriers=b.Enemies.Values.Where(e=>e.SpecialKind==1).ToArray();Require(carriers.Length>0,"正式出生没有载桶");
        b.ContentClock.Advance(b.RoundLayout.BarrelDropSeconds-.01f,false);Call("TickVortexBarrels",b,.01f);Require(b.DroppedBarrels.Count==0,"未满时提前卸桶");
        b.ContentClock.Advance(.02f,false);Call("TickVortexBarrels",b,.02f);Call("TickVortexBarrels",b,.02f);
        Require(b.DroppedBarrels.Count==carriers.Length&&carriers.All(e=>e.SpecialKind==0),"卸桶重复或身份未清除");
        using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",b);
        using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);
        using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));
        Require(decoded.ContentAreas.Count(a=>a.Kind==13)==carriers.Length,"双人快照丢失动态桶");
        Require(decoded.ContentAreas.Count(a=>a.Kind==11)==b.RoundLayout.Barrels.Count,"固定桶与动态桶混淆");
    }
    private static void EarlyDeathKeepsPurpleBlastAndBothPlayersSafe()
    {
        var actor=Spawn(1);var b=actor.Battle;var carrier=b.Enemies.Values.First(e=>e.SpecialKind==1);
        foreach(var player in b.Players.Values){player.PositionX=carrier.PositionX;player.PositionZ=carrier.PositionZ;}
        Call("SpawnContentDeathZone",actor,b,carrier);Require(b.DroppedBarrels.Count==0&&b.DeathZones.Any(z=>z.Kind==14),"提前击杀没有紫爆或误卸桶");
        Require(b.Players.Values.All(p=>p.CurrentHealth==p.MaxHealth),"载桶紫爆伤害玩家");
    }
    private static object Call(string method,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(method,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args)!;
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
