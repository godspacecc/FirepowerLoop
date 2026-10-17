using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>正式房间新局读取配置门禁，恢复与克隆保留原规则版本。</summary>
internal static class SlayerMartReleaseGateOnlineChecks
{
    static object Call(string name,params object[] values)=>typeof(OnlineArcadeRunManagerSystem).GetMethod(name,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,values)!;
    static OnlineArcadeRunRecord Initial()
    {
        var room=new OnlineRoomActor{RoomId=2,ClientVersion="release-test",Purpose=OnlineRoomPurpose.Arcade};
        room.Members[101]=new OnlineRoomMember{PlayerId=101,JoinSequence=1};room.Members[102]=new OnlineRoomMember{PlayerId=102,JoinSequence=2};
        return (OnlineArcadeRunRecord)Call("BuildInitialRecord",1L,room,ServerConfig.Instance.ArcadeRule);
    }
    static T Gate<T>(bool enabled,Func<T> action)
    {
        var config=ServerConfig.Instance;var previous=config.SlayerMart;var row=config.Tables.TbArcadeSlayerMartAbility.Get("release.enabled");float value=row.Value;
        var field=row.GetType().GetField("Value")!;var property=typeof(ServerConfig).GetProperty("SlayerMart")!;
        try
        {
            // 本进程夹具改内存行并经正式适配重建，始终恢复原值，不改源表或bytes。
            field.SetValue(row,enabled?1f:0f);property.SetValue(config,ArcadeSlayerMartConfiguration.FromTables(config.Tables));return action();
        }
        finally{field.SetValue(row,value);property.SetValue(config,previous);}
    }
    public static int Run(int maximum)
    {
        var tests=new (string,Action)[]{
            ("Closed gate creates original rules",()=>Gate(false,()=>{Require(Initial().SlayerMartRulesVersion==0);return true;})),
            ("Open gate creates version one without granting shop money",()=>Gate(true,()=>{var run=Initial();Require(run.SlayerMartRulesVersion==1&&run.SlayerMart==null&&run.Players.All(p=>p.RunGold==0)&&run.ConfigFingerprint==ServerConfig.Instance.ArcadeRule.ConfigFingerprint);return true;})),
            ("Prepared battle inherits new run version",()=>Gate(true,()=>{var run=Initial();foreach(var player in run.Players)player.Ready=true;var battle=OnlineArcadeRunActorSystem.PrepareBattle(run,false);Require(battle.SlayerMartRulesVersion==1&&battle.SlayerMartReturnWave==0);return true;})),
            ("Existing old run never upgrades when gate opens",()=>{var run=Gate(false,Initial);Gate(true,()=>{Call("NormalizeLoadedCheckpoint",run);Require(run.SlayerMartRulesVersion==0);return true;});}),
            ("Existing new run never downgrades when gate closes",()=>{var run=Gate(true,Initial);Gate(false,()=>{Call("NormalizeLoadedCheckpoint",run);Require(run.SlayerMartRulesVersion==1);return true;});}),
            ("Candidate clone preserves enabled rules",()=>Gate(true,()=>{var run=Initial();var clone=OnlineArcadeRunManagerSystem.Clone(run);Require(clone.SlayerMartRulesVersion==1&&clone.Players.Count==2&&!ReferenceEquals(clone.Players,run.Players));return true;}))
        };
        if(tests.Length>maximum)throw new ArgumentException("Scope exceeds maximum");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS SlayerMartReleaseGateOnline."+name);}catch(Exception error){failed++;Console.WriteLine("FAIL SlayerMartReleaseGateOnline."+name+": "+(error.InnerException??error).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    static void Require(bool condition){if(!condition)throw new InvalidOperationException("New-run release gate contract violated");}
}
