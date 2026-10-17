using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>实际Guardian小Boss三攻击、保护优先及胜利门禁。</summary>
internal static class SlayerGuardianOnlineChecks
{
    static object Call(string name,params object[] args){var m=typeof(OnlineArcadeRunActorSystem).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public);Require(m!=null,"missing Slayer Guardian host");return m!.Invoke(null,args)!;}
    static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    static OnlineArcadeRunActor Boss()
    {
        var a=SlayerChallengeOnlineChecks.Actor();SlayerChallengeOnlineChecks.Start(a);Call("TickSlayerChallenge",a,a.Battle,90f);
        Require(a.Battle.Enemies.Count==1,"actual Guardian missing");var e=a.Battle.Enemies.Values.Single();e.PositionX=e.PositionZ=0;
        a.Battle.Players[1].PositionX=0;a.Battle.Players[1].PositionZ=1;a.Battle.Players[2].PositionX=15;a.Battle.Players[2].PositionZ=15;return a;
    }
    static void Tick(OnlineArcadeRunActor a,float seconds)=>Call("TickSlayerGuardian",a,a.Battle,a.Battle.Enemies.Values.Single(),a.Battle.Players[1],seconds);
    static object Engine(OnlineArcadeRunActor a){var f=a.Battle.GetType().GetField("SlayerGuardian");Require(f!=null,"missing Guardian simulation state");return f!.GetValue(a.Battle)!;}
    static void Elapsed(OnlineArcadeRunActor a,double seconds)=>Engine(a).GetType().GetProperty("Elapsed")!.SetValue(Engine(a),seconds);
    static OnlineArcadeBattleInfo Snapshot(OnlineArcadeRunActor a)=>(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);
    public static int Run(int maximum)
    {
        var tests=new (string,Action)[]{
            ("Unique Guardian scales health by original member count",()=>{var a=Boss();var e=a.Battle.Enemies.Values.Single();Require(e.MaxHealth==3000&&e.CurrentHealth==3000&&e.ContentBossKind==1,"Guardian health or identity invalid");}),
            ("Bomb cannot erase Slayer Guardian",()=>{var a=Boss();a.Record.Players[0].Bombs=1;Call("UseBomb",a,a.Battle,a.Battle.Players[1]);Require(a.Battle.Enemies.Count==1&&a.Battle.Enemies.Values.Single().CurrentHealth>0,"bomb erased Guardian");}),
            ("Hammer warns before lethal impact",()=>{var a=Boss();Tick(a,.1f);using var s=Snapshot(a);Require(a.Battle.Players[1].Alive&&s.ContentAreas.Any(p=>p.Kind==13&&p.Warning),"hammer missing safe warning");}),
            ("Hammer impact uses independent challenge down",()=>{var a=Boss();Tick(a,.1f);Tick(a,1f);Require(!a.Battle.Players[1].Alive&&SlayerChallengeOnlineChecks.State(a.Battle).TeamDowns==1&&a.Record.Players[0].Lives==3,"hammer was not lethal independent down");}),
            ("Existing invulnerability blocks lethal hammer",()=>{var a=Boss();a.Battle.Players[1].InvulnerabilitySeconds=10;Tick(a,.1f);Tick(a,1f);Require(a.Battle.Players[1].Alive&&a.Battle.Players[1].CurrentHealth==100,"hammer bypassed existing protection");}),
            ("Hammer radius does not hit distant member",()=>{var a=Boss();a.Battle.Players[1].PositionZ=4;Tick(a,.1f);Tick(a,1f);Require(a.Battle.Players[1].Alive&&a.Battle.Players[1].CurrentHealth==100,"hammer exceeded radius");}),
            ("Fireball explodes and leaves finite burn area",()=>{var a=Boss();a.Battle.Players[1].PositionZ=10;Elapsed(a,5);Tick(a,.01f);Tick(a,1.01f);Tick(a,.5f);using var s=Snapshot(a);Require(a.Battle.Players[1].CurrentHealth==75&&s.ContentAreas.Any(p=>p.Kind==15&&!p.Warning),"fireball missing explosion or burn");}),
            ("Fireball burn ticks after explosion without becoming lethal",()=>{var a=Boss();a.Battle.Players[1].PositionZ=10;Elapsed(a,5);Tick(a,.01f);Tick(a,1.01f);Tick(a,.5f);a.Battle.Players[1].InvulnerabilitySeconds=0;Tick(a,.5f);Require(a.Battle.Players[1].Alive&&a.Battle.Players[1].CurrentHealth==65,"burn cadence or normal damage incorrect");}),
            ("Flame path crosses borrowed stone obstacle",()=>{var a=Boss();var e=a.Battle.Enemies.Values.Single();e.PositionX=-14;e.PositionZ=7;a.Battle.Players[1].PositionX=-8;a.Battle.Players[1].PositionZ=7;Elapsed(a,10);Tick(a,.01f);Tick(a,1.01f);using var s=Snapshot(a);Require(a.Battle.Players[1].CurrentHealth==90&&s.ContentAreas.Any(p=>p.Kind==14&&p.Segment&&!p.Warning),"flame path blocked by stone or missing direct hit");}),
            ("Guardian body uses configured normal contact between warnings",()=>{var a=Boss();var state=Engine(a);state.GetType().GetField("AttackCycle")!.SetValue(state,0L);Elapsed(a,2);a.Battle.Players[1].PositionZ=.1f;Tick(a,.01f);Require(a.Battle.Players[1].CurrentHealth==100-ServerConfig.Instance.SlayerChallenge.BossContactDamage&&a.Battle.Players[1].Alive,"Guardian contact missing or lethal");}),            ("Guardian victory opens shop instead of ending ordinary arena",()=>{var a=Boss();Call("KillEnemy",a,a.Battle,a.Battle.Enemies.Values.Single(),false);Require(SlayerChallengeOnlineChecks.State(a.Battle).Phase==ArcadeSlayerChallengePhase.Shopping&&!a.Battle.AwaitingExit&&!a.Battle.ChallengeCompletionReady&&SlayerChallengeOnlineChecks.State(a.Battle).RemainingSeconds==300,"Guardian death bypassed shop or entered ordinary exit");})
        };
        if(tests.Length>maximum)throw new ArgumentException("Declared maximum too small");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS SlayerGuardianOnline."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL SlayerGuardianOnline."+name+": "+(e.InnerException??e).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
}
