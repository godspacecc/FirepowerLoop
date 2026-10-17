using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>权威风暴运行适配，真实敌人字典和伤害入口；不启动监听或真实用户存档。</summary>
internal static class SlayerStormOnlineChecks
{
    public static int Run(int max)
    {
        var tests=new (string,Action<OnlineArcadeRunActor,OnlineArcadeBattleRuntime,OnlineArcadeBattlePlayerRuntime,ArcadeSlayerMartPlayer>)[]{
            ("Full charge consumes one use",(a,b,p,s)=>{Require((bool)Call("UseSlayerMartUpgrade",a,b,p));Require(!(bool)Call("UseSlayerMartUpgrade",a,b,p)&&s.StormCharge==0);}),
            ("Menu keeps full charge",(a,b,p,s)=>{s.MenuOpen=true;Require(!(bool)Call("UseSlayerMartUpgrade",a,b,p)&&s.StormCharge==100);}),
            ("Respawn cannot activate",(a,b,p,s)=>{p.RespawnSeconds=1;Require(!(bool)Call("UseSlayerMartUpgrade",a,b,p));}),
            ("First cycle only damages real inside targets",(a,b,p,s)=>{Call("UseSlayerMartUpgrade",a,b,p);var e=Enemy(b,1,1);var outside=Enemy(b,2,6);Call("TickSlayerMartStorm",a,b,.49f);Require(e.CurrentHealth==1000);Call("TickSlayerMartStorm",a,b,.01f);Require(e.CurrentHealth==980&&outside.CurrentHealth==1000&&e.LastDamagePlayerId==1);}),
            ("Final cycle reaches twenty hits",(a,b,p,s)=>{Call("UseSlayerMartUpgrade",a,b,p);var e=Enemy(b,1,1);var dying=Enemy(b,2,1);dying.CurrentHealth=400;Call("TickSlayerMartStorm",a,b,10f);Require(e.CurrentHealth==600&&s.StormRemaining==0&&!b.Enemies.ContainsKey(2)&&s.StormCharge==0);}),
            ("Ordinary control leaves immediately outside",(a,b,p,s)=>{Call("UseSlayerMartUpgrade",a,b,p);var e=Enemy(b,1,1);e.MoveSpeed=3;Require((float)Call("SlayerMartStormSpeed",b,e)==0);Call("TickEnemies",a,b,.1f);Require(e.PositionX==1);e.PositionX=6;Require((float)Call("SlayerMartStormSpeed",b,e)==1);Call("TickEnemies",a,b,.1f);Require(e.PositionX<6);}),
            ("Elite speed uses independent half scale",(a,b,p,s)=>{Call("UseSlayerMartUpgrade",a,b,p);var e=Enemy(b,1,1);e.SpecialKind=1;e.MoveSpeed=3;Require((float)Call("SlayerMartStormSpeed",b,e)==.5f&&e.MoveSpeed==3);}),
            ("Boss takes damage without control",(a,b,p,s)=>{Call("UseSlayerMartUpgrade",a,b,p);var e=Enemy(b,1,1);e.ContentBossKind=1;Require((float)Call("SlayerMartStormSpeed",b,e)==1);Call("TickSlayerMartStorm",a,b,.5f);Require(e.CurrentHealth==980);}),
            ("Transition keeps complete remaining storm",(a,b,p,s)=>{Call("UseSlayerMartUpgrade",a,b,p);b.RoundTransitionPending=true;Call("TickSlayerMartStorm",a,b,5f);Require(s.StormRemaining==10);}),
            ("Two owners control takes strongest without multiplying",(a,b,p,s)=>{s.StormRemaining=10;var second=b.SlayerMart.Players[1];second.StormRemaining=10;var e=Enemy(b,1,1);e.SpecialKind=1;Require((float)Call("SlayerMartStormSpeed",b,e)==.5f);})
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)try
        {
            var record=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,IsCasualRoute=true,ArenaConfigNumber=2,NextArena=2,CompletedArena=1,ConfigFingerprint=ServerConfig.Instance.ArcadeRule.ConfigFingerprint,
                SlayerMartRulesVersion=1,SlayerMart=new(){Players=new(){State(1),State(2)}},Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId="arcade.weapon.basic"},new(){PlayerId=2,Lives=3,Ready=true,WeaponId="arcade.weapon.basic"}}};
            var b=OnlineArcadeRunActorSystem.PrepareBattle(record,false);var a=new OnlineArcadeRunActor{Record=record,Battle=b};
            foreach(var p in b.Players.Values){p.ReadyRoundGeneration=b.RoundGeneration;p.PositionX=p.PositionZ=0;}
            test(a,b,b.Players[1],b.SlayerMart.Players[0]);Console.WriteLine("PASS SlayerStormOnline."+name);
        }catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerStormOnline."+name+": "+(ex.InnerException??ex).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static ArcadeSlayerMartPlayer State(long id)=>new(){PlayerId=id,StormCharge=100,Owned=new(){"slayermart.item.16"}};
    private static OnlineArcadeEnemyRuntime Enemy(OnlineArcadeBattleRuntime b,long id,float x)
    {var e=new OnlineArcadeEnemyRuntime{RuntimeId=id,PositionX=x,CurrentHealth=1000,MaxHealth=1000};b.Enemies.Add(id,e);return e;}
    private static object Call(string method,params object[] args)
    {var m=typeof(OnlineArcadeRunActorSystem).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic);if(m==null)throw new InvalidOperationException("Missing formal storm entry: "+method);return m.Invoke(null,args);}
    private static void Require(bool value){if(!value)throw new InvalidOperationException("Storm runtime contract violated");}
}
