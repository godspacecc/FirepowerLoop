using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>原普通道具、发射和复起入口的精通行为；全部配置来自正式双端表。</summary>
internal static class SlayerMasteryOnlineChecks
{
    public static int Run(int max)
    {
        var tests=new (string,Action<OnlineArcadeRunActor,OnlineArcadeBattleRuntime,OnlineArcadeBattlePlayerRuntime,ArcadeSlayerMartPlayer>)[]{
            ("Chicken pickup gets lifetime mastery",(a,b,p,s)=>Lifetime(a,p,s,ArcadePowerupBehavior.Chicken,10,1.25f)),
            ("Magnet pickup gets lifetime mastery",(a,b,p,s)=>Lifetime(a,p,s,ArcadePowerupBehavior.Magnet,7,1.5f)),
            ("Gloves pickup gets lifetime mastery",(a,b,p,s)=>Lifetime(a,p,s,ArcadePowerupBehavior.Gloves,9,1.6f)),
            ("Barrel pickup gets lifetime mastery",(a,b,p,s)=>Lifetime(a,p,s,ArcadePowerupBehavior.Barrel,22,1.25f)),
            ("Sentry emission gets only own multiplier",(a,b,p,s)=>{s.Owned.UnionWith(new[]{"slayermart.item.12","slayermart.item.23"});Enemy(b,1);Emit(b,p,ArcadePowerupBehavior.Sentry);Require(b.Projectiles.Values.Single().Damage==35);}),
            ("Chicken emission gets dead operator once",(a,b,p,s)=>{s.Owned.Add("slayermart.item.12");Enemy(b,1);Emit(b,p,ArcadePowerupBehavior.Chicken);Require(b.Projectiles.Values.Single().Damage==25);}),
            ("Player weapon emission gets dead operator once",(a,b,p,s)=>{s.Owned.Add("slayermart.item.12");p.AimX=1;p.AimZ=0;Call("Fire",a,b,p,a.Record.Players[0]);var weapon=ServerConfig.Instance.Tables.TbArcadeWeapon.Get("arcade.weapon.basic");Require(b.Projectiles.Values.First().Damage==(int)Math.Round(weapon.Damage*1.25));}),
            ("Magnet mastery changes real pickup radius only",(a,b,p,s)=>{s.Owned.Add("slayermart.item.07");p.Powerups[ArcadePowerupBehavior.Magnet]=new(){Behavior=ArcadePowerupBehavior.Magnet,RemainingSeconds=10,Radius=4};var d=ServerConfig.Instance.Tables.TbArcadeDrop.DataList.First(x=>x.Kind==ArcadeDropKind.RunGold);b.Drops[1]=new(){RuntimeId=1,DropConfigId=d.Id,PositionX=5,RemainingSeconds=10};Call("TickDrops",a,b);Require(b.Drops.Count==0);}),
            ("Decoy mastery changes actual expiration blast",(a,b,p,s)=>{s.Owned.Add("slayermart.item.08");var e=Enemy(b,6);p.Powerups[ArcadePowerupBehavior.Decoy]=new(){Behavior=ArcadePowerupBehavior.Decoy,RemainingSeconds=.01f,Radius=4,Damage=20};Call("TickPowerups",a,b,.1f);Require(e.CurrentHealth==980);}),
            ("Actual respawn applies one new protection multiplier",(a,b,p,s)=>{s.Owned.Add("slayermart.item.21");p.Alive=false;p.RespawnSeconds=.01f;p.CurrentHealth=0;Call("TickPlayers",a,b,.02f,TimeInfo.Instance.ServerNow());Require(p.Alive&&Near(p.InvulnerabilitySeconds,ServerConfig.Instance.ArcadeRule.ContactInvulnerabilitySeconds*3));}),
            ("Buying mastery does not refresh active lifetime",(a,b,p,s)=>{var d=Drop(ArcadePowerupBehavior.Chicken);Call("ApplyDrop",a,p,d);p.Powerups[ArcadePowerupBehavior.Chicken].RemainingSeconds=7;s.Owned.Add("slayermart.item.10");Call("TickPowerups",a,b,.5f);Require(Near(p.Powerups[ArcadePowerupBehavior.Chicken].RemainingSeconds,6.5f));}),
            ("Round restore never multiplies remaining again",(a,b,p,s)=>{s.Owned.Add("slayermart.item.10");Call("ApplyDrop",a,p,Drop(ArcadePowerupBehavior.Chicken));p.Powerups[ArcadePowerupBehavior.Chicken].RemainingSeconds=5;ManagerCall("CaptureRoundCheckpoint",a.Record,b);var restored=OnlineArcadeRunActorSystem.PrepareBattle(a.Record,false);Require(Near(restored.Players[1].Powerups[ArcadePowerupBehavior.Chicken].RemainingSeconds,5));})
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)try
        {
            var record=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,IsCasualRoute=true,ArenaConfigNumber=2,NextArena=2,CompletedArena=1,ConfigFingerprint=ServerConfig.Instance.ArcadeRule.ConfigFingerprint,
                SlayerMartRulesVersion=1,SlayerMart=new(){Players=new(){new(){PlayerId=1},new(){PlayerId=2}}},Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId="arcade.weapon.basic"},new(){PlayerId=2,Lives=3,Ready=true,WeaponId="arcade.weapon.basic"}}};
            var b=OnlineArcadeRunActorSystem.PrepareBattle(record,false);var a=new OnlineArcadeRunActor{Record=record,Battle=b};
            foreach(var p in b.Players.Values){p.ReadyRoundGeneration=b.RoundGeneration;p.PositionX=p.PositionZ=0;}b.Players[2].PositionX=100;
            test(a,b,b.Players[1],b.SlayerMart.Players[0]);Console.WriteLine("PASS SlayerMasteryOnline."+name);
        }catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerMasteryOnline."+name+": "+(ex.InnerException??ex).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static ArcadeDropConfig Drop(ArcadePowerupBehavior behavior)
        =>ServerConfig.Instance.Tables.TbArcadeDrop.DataList.First(d=>d.Kind==ArcadeDropKind.Powerup&&ServerConfig.Instance.Tables.TbArcadePowerup.Get(d.ProductId).Behavior==behavior);
    private static void Lifetime(OnlineArcadeRunActor a,OnlineArcadeBattlePlayerRuntime p,ArcadeSlayerMartPlayer s,ArcadePowerupBehavior behavior,int mastery,float scale)
    {s.Owned.Add("slayermart.item."+mastery.ToString("00"));var d=Drop(behavior);Require((bool)Call("ApplyDrop",a,p,d));Require(Near(p.Powerups[behavior].RemainingSeconds,d.DurationSeconds*scale));}
    private static OnlineArcadeEnemyRuntime Enemy(OnlineArcadeBattleRuntime b,float x)
    {var e=new OnlineArcadeEnemyRuntime{RuntimeId=1,PositionX=x,CurrentHealth=1000,MaxHealth=1000};b.Enemies.Add(1,e);b.EnemyIterationIds.Add(1);return e;}
    private static void Emit(OnlineArcadeBattleRuntime b,OnlineArcadeBattlePlayerRuntime p,ArcadePowerupBehavior behavior)
    {Call("SpawnPowerupProjectile",b,p,new OnlineArcadePowerupRuntime{Behavior=behavior,Damage=20,ProjectileSpeed=10,IntervalSeconds=1},0f,0f,0f);}
    private static object Call(string method,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args)!;
    private static object ManagerCall(string method,params object[] args)=>typeof(OnlineArcadeRunManagerSystem).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args)!;
    private static bool Near(float actual,float expected)=>Math.Abs(actual-expected)<.0001f;
    private static void Require(bool value){if(!value)throw new InvalidOperationException("Ordinary mastery flow violated");}
}
