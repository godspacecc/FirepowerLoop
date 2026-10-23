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
            ("Later two-player round nine uses380",()=>Require(Battle(15,3,0).TotalInWave==380,"round9 expected380")),
            ("Different themes keep same ordinal total",()=>Require(Battle(4,3,0).TotalInWave==380&&Battle(15,3,0).TotalInWave==380,"map identity changed total")),
            ("Round thirty caps at800",()=>Require(Battle(19,8,1).TotalInWave==800,"round30 expected800")),
            ("Round forty remains800",()=>Require(Battle(20,10,3).TotalInWave==800,"round40 expected800")),
            ("Old boss placeholder becomes ordinary population",()=>{var b=Battle(4,3,3);Require(b.TotalInWave==440&&!b.SpawnQueue.Contains("enemy.arcade.brute")&&b.MaxAlive==18,"old boss group was expanded or kept as single");}),
            ("Actual spawn uses selected ordinary group tuning",()=>{var b=Battle(4,3,3);Require((bool)Call("TrySpawnEnemy",b,TimeInfo.Instance.ServerNow()),"spawn rejected");var e=b.Enemies.Values.Single();var g=c.Tables.TbArcadeEnemyGroup.Get("arcade.group.ranged.1");var enemy=c.GetEnemy("enemy.arcade.basic");int health=Math.Max(1,(int)MathF.Round(enemy.MaxHP*g.HealthMultiplier*(1+c.ArcadeRule.HealthGrowthPerArena*2)));Require(e.EnemyConfigId==enemy.Id&&e.MaxHealth==health&&string.IsNullOrEmpty(e.BossId)&&b.MaxAlive==18,"spawn re-resolved old boss multipliers");}),
            ("Ordinary group substitution preserves original wave gold",()=>{var b=Battle(8,6,3);b.SpawnQueueIndex=b.SpawnQueue.Count;foreach(var player in b.Players.Values)player.ReadyRoundGeneration=b.RoundGeneration;var r=new OnlineArcadeRunRecord{Players=new(){new(){PlayerId=1,Lives=3},new(){PlayerId=2,Lives=3}}};var actor=new OnlineArcadeRunActor{Record=r,Battle=b};Require((bool)Call("TryAdvanceWave",actor,b,.01f),"clear rejected");int expected=(int)Call("ScaleWaveGold",40,6,c.ArcadeRule.GoldGrowthPerArena);Require(r.Players.All(p=>p.RunGold==expected),"clear gold changed with spawn group");}),
            ("Legacy sequential mode retains configured total",()=>{var b=Battle(15,3,0);b.IsCasualRoute=false;Call("PrepareWave",b,c.Tables.TbArcadeArena.Get(15),0);Require(b.TotalInWave==32,"legacy changed");}),
            ("Timed challenge keeps its independent scaling",()=>{var b=Battle(15,3,0);b.IsChallenge=true;b.ChallengeId="arcade.challenge.timed-clear";Call("PrepareWave",b,c.Tables.TbArcadeArena.Get(15),0);Require(b.TotalInWave==12,"challenge changed");}),
            ("First four rounds double solo populations",()=>{int[] expected={220,240,260,280};for(int wave=0;wave<4;wave++)Require(Battle(1,1,wave).TotalInWave==expected[wave],"first four rounds must double");}),
            ("Shared rule multiplies solo cap for one through four players",()=>{for(int players=1;players<=4;players++){Require(FirepowerLoop.Arcade.ArcadeOpeningRules.OrdinaryRoundTotal(1,players)==110*players,"first round multiplier incorrect");Require(FirepowerLoop.Arcade.ArcadeOpeningRules.OrdinaryRoundTotal(40,players)==400*players,"solo cap applied after scaling");}}),
            ("Actual two-player spawns reach doubled alive cap without overshoot",()=>{
                var b=Battle(1,1,0);Require(b.MaxAlive==48,"two-player solo24 cap must become48");
                foreach(var player in b.Players.Values)player.ReadyRoundGeneration=b.RoundGeneration;
                for(int tick=0;tick<110;tick++)Call("TrySpawnEnemy",b,10000L+1000L*tick);
                Require(b.Enemies.Count==48&&b.SpawnQueueIndex==48,"actual spawn did not respect doubled cap");
                b.Enemies.Remove(b.Enemies.Keys.First());Call("TrySpawnEnemy",b,200000L);
                Require(b.Enemies.Count==48&&b.SpawnQueueIndex==49,"one available slot must consume exactly one enemy");}),
            ("Theme boss reinforcements scale count without multiplying boss",()=>{
                var solo=Battle(1,1,0);solo.DifficultyPlayerCount=1;var duo=Battle(1,1,0);
                Call("SpawnContentReinforcements",solo,3);Call("SpawnContentReinforcements",duo,3);
                Require(solo.Enemies.Count==3&&duo.Enemies.Count==6,"boss minion count must multiply by players");
                Call("SpawnContentReinforcements",duo,100);Require(duo.Enemies.Count==48,"boss minions still use shared solo cap");}),
            ("Independent boss stages never expand ordinary queues",()=>{var b=Battle(20,10,3);Call("BeginFinalBoss",b,c.Tables.TbArcadeArena.Get(20));Require(b.SpawnQueue.Count==1&&b.MaxAlive==1,"final boss expanded");var theme=Battle(4,3,3);Call("PrepareRoundBoss",theme);Require(theme.SpawnQueue.Count==1&&theme.MaxAlive==1,"theme boss expanded");})
        };
        if(tests.Length>maximum)throw new ArgumentException("Declared maximum smaller than actual scope");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS CasualPopulationOnline."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL CasualPopulationOnline."+name+": "+(e.InnerException??e).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
}
