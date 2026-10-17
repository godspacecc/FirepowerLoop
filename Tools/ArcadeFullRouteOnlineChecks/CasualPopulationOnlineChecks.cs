using System.Reflection;
using ET;
using ET.Server;

/// <summary>通过正式服务端准备／生成入口核对休闲普通轮人数预算，不启动在线服务。</summary>
internal static class CasualPopulationOnlineChecks
{
    static object Call(string name,params object[] values)=>typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!.Invoke(null,values)!;
    static OnlineArcadeBattleRuntime Battle(int map,int ordinal,int wave)
    {
        var r=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,IsCasualRoute=true,ArenaConfigNumber=map,CompletedArena=ordinal-1,NextArena=ordinal,ConfigFingerprint=ServerConfig.Instance.ArcadeRule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId="arcade.weapon.basic"},new(){PlayerId=2,Lives=3,Ready=true,WeaponId="arcade.weapon.basic"}}};
        var b=OnlineArcadeRunActorSystem.PrepareBattle(r,false);Call("PrepareWave",b,ServerConfig.Instance.Tables.TbArcadeArena.Get(map),wave);return b;
    }
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static int Run(int maximum)
    {
        var c=ServerConfig.Instance;
        var tests=new (string,Action)[]{
            ("Later two-player round nine uses240",()=>Require(Battle(15,3,0).TotalInWave==240,"round9 expected240")),
            ("Different themes keep same ordinal total",()=>Require(Battle(4,3,0).TotalInWave==240&&Battle(15,3,0).TotalInWave==240,"map identity changed total")),
            ("Round thirty caps at450",()=>Require(Battle(19,8,1).TotalInWave==450,"round30 expected450")),
            ("Round forty remains450",()=>Require(Battle(20,10,3).TotalInWave==450,"round40 expected450")),
            ("Old boss placeholder becomes ordinary population",()=>{var b=Battle(4,3,3);Require(b.TotalInWave==270&&!b.SpawnQueue.Contains("enemy.arcade.brute")&&b.MaxAlive==18,"old boss group was expanded or kept as single");}),
            ("Actual spawn uses selected ordinary group tuning",()=>{var b=Battle(4,3,3);Require((bool)Call("TrySpawnEnemy",b,TimeInfo.Instance.ServerNow()),"spawn rejected");var e=b.Enemies.Values.Single();var g=c.Tables.TbArcadeEnemyGroup.Get("arcade.group.ranged.1");var enemy=c.GetEnemy("enemy.arcade.basic");int health=Math.Max(1,(int)MathF.Round(enemy.MaxHP*g.HealthMultiplier*(1+c.ArcadeRule.HealthGrowthPerArena*2)));Require(e.EnemyConfigId==enemy.Id&&e.MaxHealth==health&&string.IsNullOrEmpty(e.BossId)&&b.MaxAlive==18,"spawn re-resolved old boss multipliers");}),
            ("Ordinary group substitution preserves original wave gold",()=>{var b=Battle(8,6,3);b.SpawnQueueIndex=b.SpawnQueue.Count;foreach(var player in b.Players.Values)player.ReadyRoundGeneration=b.RoundGeneration;var r=new OnlineArcadeRunRecord{Players=new(){new(){PlayerId=1,Lives=3},new(){PlayerId=2,Lives=3}}};var actor=new OnlineArcadeRunActor{Record=r,Battle=b};Require((bool)Call("TryAdvanceWave",actor,b,.01f),"clear rejected");int expected=(int)Call("ScaleWaveGold",40,6,c.ArcadeRule.GoldGrowthPerArena);Require(r.Players.All(p=>p.RunGold==expected),"clear gold changed with spawn group");}),
            ("Legacy sequential mode retains configured total",()=>{var b=Battle(15,3,0);b.IsCasualRoute=false;Call("PrepareWave",b,c.Tables.TbArcadeArena.Get(15),0);Require(b.TotalInWave==32,"legacy changed");}),
            ("Timed challenge keeps its independent scaling",()=>{var b=Battle(15,3,0);b.IsChallenge=true;b.ChallengeId="arcade.challenge.timed-clear";Call("PrepareWave",b,c.Tables.TbArcadeArena.Get(15),0);Require(b.TotalInWave==12,"challenge changed");}),
            ("Independent boss stages never expand ordinary queues",()=>{var b=Battle(20,10,3);Call("BeginFinalBoss",b,c.Tables.TbArcadeArena.Get(20));Require(b.SpawnQueue.Count==1&&b.MaxAlive==1,"final boss expanded");var theme=Battle(4,3,3);Call("PrepareRoundBoss",theme);Require(theme.SpawnQueue.Count==1&&theme.MaxAlive==1,"theme boss expanded");})
        };
        if(tests.Length>maximum)throw new ArgumentException("Declared maximum smaller than actual scope");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS CasualPopulationOnline."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL CasualPopulationOnline."+name+": "+(e.InnerException??e).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
}
