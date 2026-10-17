using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>通过正式服务端伤害入口验证个人板与旧局、保护、机关隔离。</summary>
internal static class SlayerArmorOnlineChecks
{
    public static int Run(int max)
    {
        var tests=new (string,Action<OnlineArcadeRunActor,OnlineArcadeBattleRuntime,OnlineArcadeBattlePlayerRuntime,ArcadeSlayerMartPlayer>)[]{
            ("Plate protects health once",(a,b,p,s)=>{Hit(a,b,p);Require(p.CurrentHealth==100&&s.Plates==1);}),
            ("Protection refuses repeated pulse",(a,b,p,s)=>{Hit(a,b,p);Hit(a,b,p);Require(s.Plates==1);}),
            ("Empty vest takes actual damage",(a,b,p,s)=>{s.Plates=0;Hit(a,b,p);Require(p.CurrentHealth==80);}),
            ("Respawn waiting never consumes plate",(a,b,p,s)=>{p.RespawnSeconds=1;Hit(a,b,p);Require(s.Plates==2&&p.CurrentHealth==100);}),
            ("Dash never consumes plate",(a,b,p,s)=>{p.DashSeconds=1;Hit(a,b,p);Require(s.Plates==2&&p.CurrentHealth==100);}),
            ("Lethal mechanism bypasses new armor",(a,b,p,s)=>{Hit(a,b,p,SlayerMartHitKind.LethalMechanism);Require(s.Plates==2&&p.CurrentHealth==80);}),
            ("Legacy run cannot use injected armor",(a,b,p,s)=>{b.SlayerMartRulesVersion=0;Hit(a,b,p);Require(s.Plates==2&&p.CurrentHealth==80);}),
            ("Teammate armor cannot protect owner",(a,b,p,s)=>{s.Plates=0;b.SlayerMart.Players.Add(new(){PlayerId=2,VestSlots=2,Plates=2});Hit(a,b,p);Require(p.CurrentHealth==80&&b.SlayerMart.Players[1].Plates==2);})
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)try
        {
            var s=new ArcadeSlayerMartPlayer{PlayerId=1,VestSlots=2,Plates=2};var b=new OnlineArcadeBattleRuntime{SlayerMartRulesVersion=1,SlayerMart=new(){Players=new(){s}}};
            var p=new OnlineArcadeBattlePlayerRuntime{PlayerId=1,Alive=true,CurrentHealth=100,MaxHealth=100};b.Players.Add(1,p);
            var a=new OnlineArcadeRunActor{Battle=b,Record=new(){Players=new(){new(){PlayerId=1,Lives=3}}}};
            test(a,b,p,s);Console.WriteLine("PASS SlayerArmorOnline."+name);
        }catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerArmorOnline."+name+": "+(ex.InnerException??ex).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static void Hit(OnlineArcadeRunActor a,OnlineArcadeBattleRuntime b,OnlineArcadeBattlePlayerRuntime p,SlayerMartHitKind kind=SlayerMartHitKind.Ordinary)
    {
        var method=typeof(OnlineArcadeRunActorSystem).GetMethod("DamagePlayerCore",BindingFlags.Static|BindingFlags.NonPublic);
        if(method==null)throw new InvalidOperationException("Missing formal source-aware damage entry");method.Invoke(null,new object[]{a,b,p,20,kind});
    }
    private static void Require(bool value){if(!value)throw new InvalidOperationException("Armor flow violated");}
}
