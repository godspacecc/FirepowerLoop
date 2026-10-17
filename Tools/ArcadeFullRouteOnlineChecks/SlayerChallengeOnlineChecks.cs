using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>首批Slayer实际Actor准备、收币阶段与独立倒地；不运行完整套件。</summary>
internal static class SlayerChallengeOnlineChecks
{
    static object Call(string method,params object[] args)=>typeof(OnlineArcadeRunActorSystem).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!.Invoke(null,args)!;
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    internal static ArcadeSlayerChallengeState State(OnlineArcadeBattleRuntime b)
    {var field=b.GetType().GetField("SlayerChallenge");Require(field!=null,"missing actual Slayer runtime");return (ArcadeSlayerChallengeState)field!.GetValue(b)!;}
    internal static OnlineArcadeRunActor Actor()
    {
        var config=ServerConfig.Instance;var shop=ArcadeSlayerMartRules.Create("1",1,new long[]{1,2},17,config.SlayerMart.Catalog);shop.Ready=shop.Closing=true;
        var r=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,Phase=OnlineArcadeRunPhase.SafeCheckpoint,IsCasualRoute=true,ArenaConfigNumber=5,NextArena=4,CompletedArena=3,
            ConfigFingerprint=config.ArcadeRule.ConfigFingerprint,ChallengeId=config.SlayerChallenge.ChallengeId,SlayerMartRulesVersion=1,SlayerMart=shop,
            CreatedAtUtc=DateTime.UtcNow.ToString("O"),UpdatedAtUtc=DateTime.UtcNow.ToString("O"),
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,ChallengeReady=true,WeaponId="arcade.weapon.basic"},new(){PlayerId=2,Lives=3,Ready=true,ChallengeReady=true,WeaponId="arcade.weapon.basic"}}};
        var b=OnlineArcadeRunActorSystem.PrepareBattle(r,true);foreach(var p in b.Players.Values){p.ReadyRoundGeneration=b.RoundGeneration;p.InvulnerabilitySeconds=0;}
        return new(){Record=r,CommittedRecord=OnlineArcadeRunManagerSystem.Clone(r),Battle=b};
    }
    internal static void Start(OnlineArcadeRunActor a)
    {
        var field=a.Battle.GetType().GetField("SlayerChallenge");Require(field!=null,"missing actual Slayer runtime");var s=State(a.Battle);
        foreach(var id in s.Members)s=ArcadeSlayerChallengeRules.SetEntryChoice(s,ServerConfig.Instance.SlayerChallenge,id,true);
        field!.SetValue(a.Battle,s);Call("BeginSlayerCollectionWave",a.Battle);
    }
    static void Kill(OnlineArcadeRunActor a,long member=1){var p=a.Battle.Players[member];p.InvulnerabilitySeconds=0;p.RespawnSeconds=0;p.Alive=true;p.CurrentHealth=100;Call("DamagePlayerCore",a,a.Battle,p,100000,SlayerMartHitKind.Ordinary);}
    public static int Run(int max)
    {
        var tests=new (string,Action)[]{
            ("Dedicated layout retains actual mainline return identity",()=>{var a=Actor();var s=State(a.Battle);Require(a.Battle.ArenaConfigNumber==4&&a.Battle.RoundLayout?.ArenaConfigNumber==4&&s.ReturnMap==5&&s.ReturnArena==4&&a.Record.ArenaConfigNumber==5,"challenge replaced return map or reused wrong field");}),
            ("Preparation never spawns or accepts combat",()=>{var a=Actor();State(a.Battle);a.SubmitInput(1,4,1,1,0,0,1,true,true,true,a.Battle.RoundGeneration,true);a.TickBattle(a.Battle.LastTickAt+200);Require(a.Battle.Enemies.Count==0&&!a.Battle.Players[1].Firing&&a.Record.Players[0].Lives==3,"preparation started combat");}),
            ("Collection repeats finite batches without early success",()=>{var a=Actor();Start(a);var b=a.Battle;b.SpawnQueueIndex=b.SpawnQueue.Count;Call("TryAdvanceWave",a,b,.1f);Require(State(b).Phase==ArcadeSlayerChallengePhase.Collecting&&!b.ChallengeCompletionReady&&b.SpawnQueue.Count==30&&b.SpawnQueueIndex==0,"old timed-clear path ended Slayer");}),
            ("Collection deadline switches to boss rather than failure",()=>{var a=Actor();Start(a);Call("TickSlayerChallenge",a,a.Battle,90f);Require(State(a.Battle).Phase==ArcadeSlayerChallengePhase.Boss&&!a.Battle.ChallengeCompletionReady,"old timer failed Slayer");}),
            ("First death uses independent allowance without main life cost",()=>{var a=Actor();Start(a);Kill(a);Require(State(a.Battle).TeamDowns==1&&a.Record.Players[0].Lives==3&&a.Battle.Players[1].RespawnSeconds==3&&!a.Battle.ChallengeCompletionReady,"death consumed main life or ended challenge");}),
            ("Fourth team death returns whole challenge",()=>{var a=Actor();Start(a);for(int i=0;i<4;i++)Kill(a,i%2+1);Require(State(a.Battle).TeamDowns==4&&a.Battle.ChallengeCompletionReady&&!a.Battle.AllPlayersDefeated&&a.Record.Players.All(p=>p.Lives==3),"fourth death did not use independent failure");}),
            ("Zero-life active original member uses challenge short revival",()=>{var a=Actor();Start(a);a.Record.Players[0].Lives=0;Kill(a);Call("TickPlayers",a,a.Battle,3f,TimeInfo.Instance.ServerNow());Require(a.Battle.Players[1].Alive&&a.Record.Players[0].Lives==0&&State(a.Battle).TeamDowns==1,"challenge respawn inherited main lives guard");}),
            ("Map loading freezes Slayer collection clock",()=>{var a=Actor();Start(a);a.Battle.Players[1].ReadyRoundGeneration=-1;double before=State(a.Battle).RemainingSeconds;a.TickBattle(a.Battle.LastTickAt+200);Require(State(a.Battle).RemainingSeconds==before,"loading consumed Slayer time");}),
            ("Disconnected team freezes Slayer collection clock",()=>{var a=Actor();Start(a);a.DisconnectedAtByPlayer[2]=1;double before=State(a.Battle).RemainingSeconds;a.TickBattle(a.Battle.LastTickAt+200);Require(State(a.Battle).RemainingSeconds==before,"disconnect consumed Slayer time");}),
            ("Preparation timeout requests safe return instead of battle loss",()=>{var a=Actor();State(a.Battle);Call("TickSlayerChallenge",a,a.Battle,1200f);Require(a.Battle.ChallengeCompletionReady&&State(a.Battle).Outcome==ArcadeSlayerChallengeOutcome.Skipped&&!a.Battle.AllPlayersDefeated,"preparation timeout killed main run");}),
            ("Borrowed layout never injects ordinary theme specials",()=>{var a=Actor();Start(a);for(int i=0;i<8;i++)Call("TrySpawnEnemy",a.Battle,TimeInfo.Instance.ServerNow()+100000L*i);Require(a.Battle.Enemies.Count>0&&a.Battle.Enemies.Values.All(e=>e.SpecialKind==0&&e.ContentBossKind==0),"challenge inherited graveyard special rules");}),
            ("Preparation and terminal phases reject damage",()=>{var a=Actor();State(a.Battle);Call("DamagePlayerCore",a,a.Battle,a.Battle.Players[1],100000,SlayerMartHitKind.Ordinary);Require(a.Battle.Players[1].Alive&&a.Battle.Players[1].CurrentHealth==100,"preparation accepted damage");Start(a);for(int i=0;i<4;i++)Kill(a);var p=a.Battle.Players[2];Call("DamagePlayerCore",a,a.Battle,p,100000,SlayerMartHitKind.Ordinary);Require(p.Alive&&State(a.Battle).TeamDowns==4,"damage continued after fourth failure");})
        };
        if(tests.Length>max)throw new ArgumentException("Declared maximum too small");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS SlayerChallengeOnline."+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerChallengeOnline."+name+": "+(ex.InnerException??ex).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
}
