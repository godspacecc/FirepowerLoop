using System.Collections;
using System.Reflection;
using ET;
using ET.Server;
using MemoryPack;

/// <summary>直接推进真实服务器弹丸，验证反馈与结算同源，不启动服务或构建Player。</summary>
internal static class WeaponFeedbackOnlineChecks
{
    public static int Run(int maximum)
    {
        var checks=new (string,Action)[]{("BodyHit",BodyHit),("WallHit",WallHit),("QuietExpiry",QuietExpiry),("QuietFlame",QuietFlame),
            ("Explosion",Explosion),("PurpleRocket",PurpleRocket),("PurpleRayWall",PurpleRayWall),("BoundedHistory",BoundedHistory),("SnapshotCopy",SnapshotCopy),("RoundReset",RoundReset),
            ("ExplosiveWallHasNoExtraSparks",ExplosiveWallHasNoExtraSparks),("EnemyShotHasNoFriendlyFeedback",EnemyShotHasNoFriendlyFeedback),
            ("PiercingHitKeepsSoloFeedback",PiercingHitKeepsSoloFeedback),("DroneHitKeepsSoloFeedback",DroneHitKeepsSoloFeedback),("LegacyWaveSequence",LegacyWaveSequence)};
        if(maximum<checks.Length)throw new ArgumentException("WeaponFeedbackOnline maximum too small");int failures=0;
        foreach(var(name,test) in checks)try{test();Console.WriteLine("PASS WeaponFeedbackOnline."+name);}catch(Exception e){failures++;Console.WriteLine("FAIL WeaponFeedbackOnline."+name+": "+(e.InnerException??e).Message);}
        Console.WriteLine($"{checks.Length-failures}/{checks.Length} passed");return failures==0?0:1;
    }
    private static object Call(string name,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,args)!;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static IList Events(object source)=>(IList)(source.GetType().GetProperty("WeaponFeedback")?.GetValue(source)??throw new InvalidOperationException("真实命中没有同步WeaponFeedback"));
    private static T Value<T>(object source,string name)=>(T)(source.GetType().GetProperty(name)?.GetValue(source)??source.GetType().GetField(name)!.GetValue(source))!;
    private static OnlineArcadeRunActor Create()
    {
        var rule=ServerConfig.Instance.ArcadeRule;
        var record=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,NextArena=2,ArenaConfigNumber=21,IsCasualRoute=true,ConfigFingerprint=rule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
        var actor=new OnlineArcadeRunActor{Record=record,Battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false)};
        actor.Battle.Enemies.Clear();actor.Battle.EnemyIterationIds.Clear();actor.Battle.Projectiles.Clear();actor.Battle.Crates.Clear();actor.Battle.DroppedBarrels.Clear();
        return actor;
    }
    private static OnlineArcadeEnemyRuntime Enemy(OnlineArcadeRunActor a)
    {
        var e=new OnlineArcadeEnemyRuntime{RuntimeId=99,EnemyConfigId="arcade.enemy.basic",PositionZ=2,CurrentHealth=100000,MaxHealth=100000};
        a.Battle.Enemies[99]=e;a.Battle.EnemyIterationIds.Add(99);return e;
    }
    private static OnlineArcadeProjectileRuntime Shot(OnlineArcadeRunActor a)
    {
        var p=new OnlineArcadeProjectileRuntime{RuntimeId=901,OwnerPlayerId=1,VelocityZ=10,RemainingSeconds=2,Damage=7,RemainingHits=1,FromPlayerWeapon=true,SourceWeaponId="arcade.weapon.basic"};
        a.Battle.Projectiles[901]=p;return p;
    }
    private static void Tick(OnlineArcadeRunActor a,float seconds=.2f)=>Call("TickProjectiles",a,a.Battle,seconds);
    private static void Kind(OnlineArcadeRunActor a,int kind,int count=1)
    {using var s=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);var events=Events(s);Require(events.Count==count,"反馈数量错误: "+events.Count);if(count>0)Require(Value<int>(events[count-1]!,"Kind")==kind,"反馈类别错误");}
    private static void BodyHit(){var a=Create();var e=Enemy(a);Shot(a);Tick(a);Require(e.CurrentHealth==99993,"命中未真实结算");Kind(a,1);Tick(a);Kind(a,1);}
    private static void WallHit(){var a=Create();var p=Shot(a);p.PositionX=-6.75f;p.PositionZ=7;Tick(a,.3f);Require(a.Battle.Projectiles.Count==0,"未撞货箱");Kind(a,2);}
    private static void QuietExpiry(){var a=Create();var p=Shot(a);p.RemainingSeconds=.01f;Tick(a);Require(a.Battle.Projectiles.Count==0,"寿命未回收");Kind(a,0,0);}
    private static void QuietFlame(){var a=Create();Enemy(a);var p=Shot(a);p.SourceWeaponId="arcade.weapon.flamethrower";Tick(a);Kind(a,0,0);}
    private static void Explosion(){var a=Create();var e=Enemy(a);var p=Shot(a);p.Behavior=ArcadeWeaponBehavior.Explosive;p.ExplosionRadius=2.8f;Tick(a);Require(e.CurrentHealth==99993,"爆炸伤害改变");Kind(a,3);}
    private static void PurpleRocket(){var a=Create();Enemy(a);var p=Shot(a);p.SourceWeaponId="arcade.weapon.rocket";p.WeaponPowerLevel=2;p.Behavior=ArcadeWeaponBehavior.Explosive;p.ExplosionRadius=2.8f;Tick(a);Kind(a,3);}
    private static void PurpleRayWall(){var a=Create();var p=Shot(a);p.SourceWeaponId="arcade.weapon.raygun";p.WeaponPowerLevel=2;p.PositionX=-6.75f;p.PositionZ=7;Tick(a,.3f);Kind(a,2);}
    private static void BoundedHistory(){var a=Create();Enemy(a);for(int i=0;i<140;i++){Shot(a);Tick(a);}Kind(a,1,128);}
    private static void SnapshotCopy()
    {
        var a=Create();Enemy(a);Shot(a);Tick(a);using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);
        using var clone=(OnlineArcadeBattleInfo)Call("CloneBattleSnapshot",snapshot);
        Require(Events(clone).Count==1&&!ReferenceEquals(Events(snapshot)[0],Events(clone)[0]),"快照反馈未深复制");
        var hit=Events(clone)[0]!;Require(Value<float>(hit,"PositionZ")==2&&Value<float>(hit,"DirectionZ")==10&&Value<long>(hit,"ServerTime")>0,"位置/方向/时间未保留");
        using var decoded=MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone));
        Require(Events(decoded).Count==1&&Value<float>(Events(decoded)[0]!,"DirectionZ")==10,"协议编码丢失武器反馈");
    }
    private static void RoundReset(){var a=Create();Enemy(a);Shot(a);Tick(a);Call("PrepareWave",a.Battle,ServerConfig.Instance.Tables.TbArcadeArena.Get(21),1);Kind(a,0,0);}
    private static void ExplosiveWallHasNoExtraSparks(){var a=Create();var p=Shot(a);p.SourceWeaponId="arcade.weapon.rocket";p.Behavior=ArcadeWeaponBehavior.Explosive;p.ExplosionRadius=2.8f;p.PositionX=-6.75f;p.PositionZ=7;Tick(a,.3f);Kind(a,3);}
    private static void EnemyShotHasNoFriendlyFeedback(){var a=Create();var p=Shot(a);p.OwnerPlayerId=0;p.PositionX=-6.75f;p.PositionZ=7;Tick(a,.3f);Kind(a,0,0);}
    private static void PiercingHitKeepsSoloFeedback(){var a=Create();Enemy(a);var p=Shot(a);p.SourceWeaponId="arcade.weapon.piercing";p.Behavior=ArcadeWeaponBehavior.Piercing;Tick(a);Kind(a,0,0);}
    private static void DroneHitKeepsSoloFeedback(){var a=Create();Enemy(a);var p=Shot(a);p.SourceWeaponId="arcade.powerup.drone";p.FromPlayerWeapon=false;Tick(a);Kind(a,0,0);}
    private static void LegacyWaveSequence()
    {
        var a=Create();Enemy(a);Shot(a);Tick(a);long before=Value<long>(Events(a.Battle)[0]!,"Sequence");
        int generation=a.Battle.RoundGeneration;Call("PrepareWave",a.Battle,ServerConfig.Instance.Tables.TbArcadeArena.Get(21),1);
        Enemy(a);Shot(a);Tick(a);Require(a.Battle.RoundGeneration==generation,"测试必须覆盖不换generation的旧逐波入口");
        Require(Value<long>(Events(a.Battle)[0]!,"Sequence")>before,"旧逐波清场不能重置序列并被客户端去重吞掉");
    }
}
