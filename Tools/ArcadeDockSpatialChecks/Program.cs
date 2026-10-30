using ET;
using ET.Server;
using FirepowerLoop.Arcade;
using System.Reflection;

// 使用真实服务器适配器、正式二进制配置和正式几何入口，不创建替代身体。
internal static class Program
{
    // 六项明确范围检查，始终输出通过项。
    private static int Main(string[] args)
    {
        if(args.Length!=3||args[0]!="testClass=ArcadeDockSpatialChecks"||args[1]!="maxTests=6"||args[2]!="includePassingTests=true")return 2;
        World.Instance.AddSingleton(ServerConfig.CreateForValidation("OnlineLocal",n=>File.ReadAllBytes(Path.Combine("Server/GameServer/Config/Luban",n+".bytes"))));
        var method=typeof(OnlineArcadeRunActorSystem).GetMethod("TryEnemySweep",BindingFlags.Static|BindingFlags.NonPublic)!;
        OnlineArcadeEnemyRuntime Enemy(float age)=>new(){EnemyConfigId="enemy.arcade.zombie.shambler",CurrentHealth=100,DockSeaEntry=new(){Started=true,Age=age,Direction=2,Lane=-3}};
        bool Sweep(OnlineArcadeEnemyRuntime e,float ax=-3,float az=0,float bx=3,float bz=0){object[] a={e,ax,az,bx,bz,1f,0f,0f};return(bool)method.Invoke(null,a)!;}
        void Require(bool v,string reason){if(!v)throw new Exception(reason);}
        var checks=new(string,Action)[]{
            ("EarlyBodyMiss",()=>Require(!Sweep(Enemy(0)),"Water body hit by .75 centerline")),
            ("LateBodyHit",()=>Require(Sweep(Enemy(6.5f)),"Exposed body missed")),
            ("ReverseAndFrozen",()=>{var e=Enemy(6.5f);Require(Sweep(e)&&Sweep(e,3,0,-3,0)&&e.DockSeaEntry.Age==6.5f,"Direction or repeated query changed pose");}),
            ("NoTimeImmunity",()=>{var e=Enemy(0);Require(e.CanBeAttacked,"Time immunity remains");e.CurrentHealth=0;Require(!e.CanBeAttacked,"Dead target still eligible");}),
            ("MissingPoseFailsExplicitly",()=>{var e=Enemy(0);e.EnemyConfigId="enemy.arcade.basic";bool fail=false;try{Sweep(e);}catch(TargetInvocationException ex)when(ex.InnerException is InvalidOperationException){fail=ex.InnerException.Message.Contains("TbArcadeDockHitPose");}Require(fail,"Missing pose silently accepted");}),
            ("NormalEnemyLegacyCircle",()=>{var e=Enemy(0);e.DockSeaEntry=default;Require(Sweep(e),"Normal enemy circle changed");})
        };
        int failed=0;foreach(var(name,check)in checks)try{check();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}
        Console.WriteLine($"ArcadeDockSpatialChecks: {6-failed}/6 passed");return failed==0?0:1;
    }
}
