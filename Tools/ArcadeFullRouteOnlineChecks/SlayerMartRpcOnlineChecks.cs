using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;
using MemoryPack;

/// <summary>正式三跳请求映射、消息编解码及能力输入，不启动真实网络监听。</summary>
internal static class SlayerMartRpcOnlineChecks
{
    public static int Run(int max)
    {
        var tests=new (string,Action)[]{
            ("Gate mapping preserves intent and supplies authenticated identity",()=>{var q=Request();var result=(G2P_OperateOnlineSlayerMart)Map("ToPlayer",q,11L,17L);Require(result.PlayerId==11&&result.SessionEpoch==17&&result.OperationId==q.OperationId&&result.ExpectedRevision==9&&result.ItemId==q.ItemId&&result.RegionInstanceId==q.RegionInstanceId&&result.Generation==2&&result.BoxId==4&&result.ConfirmedAmount==1&&result.Value);}),
            ("Player mapping never trusts incoming player identifier",()=>{var q=new G2P_OperateOnlineSlayerMart{PlayerId=999,OperationId="op",Generation=2,ExpectedRevision=9,RegionInstanceId="1:slayermart:2",ActionKind=1,ItemId="slayermart.item.01",BoxId=4,ConfirmedAmount=1,Value=true};var result=(P2R_OperateOnlineSlayerMart)Map("ToRoom",q,22L);Require(result.PlayerId==22&&result.OperationId=="op"&&result.Generation==2&&result.ExpectedRevision==9&&result.BoxId==4&&result.ConfirmedAmount==1&&result.Value);}),
            ("Room mapping supplies only domain intent without client prices",()=>{var q=new P2R_OperateOnlineSlayerMart{PlayerId=999,OperationId="op",RegionInstanceId="1:slayermart:1",Generation=1,ExpectedRevision=2,ActionKind=4,BoxId=3,ConfirmedAmount=2,Value=true};var result=(ArcadeSlayerMartRequest)Map("ToDomain",q,33L);Require(result.PlayerId==33&&result.Action==SlayerMartAction.SetMenuOpen&&result.BoxId==3&&result.ConfirmedAmount==2&&result.Value&&result.ExpectedRevision==2);}),
            ("Generated operation codec roundtrips complete payload",()=>{var q=Request();var read=MemoryPackSerializer.Deserialize<C2G_OperateOnlineSlayerMart>(MemoryPackSerializer.Serialize(q));Require(read.OperationId==q.OperationId&&read.ProtocolFingerprint==q.ProtocolFingerprint&&read.Generation==2&&read.ExpectedRevision==9&&read.Value&&read.ConfirmedAmount==1);}),
            ("Upgrade input consumes one storm without bomb or dash",()=>{var a=Actor();Input(a,1,a.Battle.RoundGeneration);var p=a.Battle.SlayerMart.Players[0];Require(p.StormCharge==0&&p.StormRemaining==10&&a.Record.Players[0].Bombs==2&&a.Record.Players[0].Dashes==2);}),
            ("Replayed upgrade sequence cannot consume another pulse",()=>{var a=Actor();Input(a,1,a.Battle.RoundGeneration);Input(a,1,a.Battle.RoundGeneration);Require(a.Battle.SlayerMart.Players[0].StormRemaining==10&&a.Record.Players[0].Bombs==2&&a.Record.Players[0].Dashes==2);}),
            ("Stale map upgrade leaves full charge and inventory intact",()=>{var a=Actor();bool rejected=false;try{Input(a,1,a.Battle.RoundGeneration-1);}catch(TargetInvocationException e)when(e.InnerException is RpcException){rejected=true;}Require(rejected&&a.Battle.SlayerMart.Players[0].StormCharge==100&&a.Record.Players[0].Bombs==2);}),
            ("All three handler types bind exact generated request family",()=>{var assembly=typeof(OnlineRoomSnapshotHelper).Assembly;foreach(var pair in new[]{("C2G_OperateOnlineSlayerMartHandler",typeof(C2G_OperateOnlineSlayerMart)),("G2P_OperateOnlineSlayerMartHandler",typeof(G2P_OperateOnlineSlayerMart)),("P2R_OperateOnlineSlayerMartHandler",typeof(P2R_OperateOnlineSlayerMart))}){var type=assembly.GetType("ET.Server."+pair.Item1)??throw new InvalidOperationException("Missing RPC handler: "+pair.Item1);Require(type.BaseType.GetGenericArguments().Contains(pair.Item2));}})
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS SlayerMartRpcOnline."+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerMartRpcOnline."+name+": "+(ex.InnerException??ex).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static C2G_OperateOnlineSlayerMart Request()=>new(){RegionInstanceId="1:slayermart:2",Generation=2,ExpectedRevision=9,OperationId="stable-operation",ActionKind=1,ItemId="slayermart.item.01",BoxId=4,ConfirmedAmount=1,Value=true,ProtocolFingerprint="fingerprint|slayermart.v1"};
    private static object Map(string name,params object[] args)
    {var type=typeof(OnlineRoomSnapshotHelper).Assembly.GetType("ET.Server.OnlineSlayerMartProtocol")??throw new InvalidOperationException("Missing authenticated RPC mapper");return type.GetMethod(name)!.Invoke(null,args)!;}
    private static void Input(OnlineArcadeRunActor a,long sequence,int generation)
    {var method=typeof(OnlineArcadeRunActorSystem).GetMethod("SubmitInput")!;Require(method.GetParameters().Length==16);method.Invoke(null,new object[]{a,1L,3,sequence,0f,0f,0f,1f,false,true,true,generation,true,0,false,true});}
    private static OnlineArcadeRunActor Actor()
    {
        var config=ServerConfig.Instance.SlayerMart;var shop=ArcadeSlayerMartRules.Create("1",1,new long[]{1,2},17,config.Catalog);shop.Ready=shop.Closing=true;shop.Players[0].Owned.Add("slayermart.item.16");shop.Players[0].StormCharge=100;
        var layout=OnlineArcadeRunActorSystem.ResolveRoundLayout(3,4);var r=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,IsCasualRoute=true,ArenaConfigNumber=3,NextArena=3,CompletedArena=2,RoundNumber=4,RoundLayoutId=layout.Id,RoundLayoutVersion=layout.Version,
            SlayerMartRulesVersion=1,SlayerMart=shop,ConfigFingerprint=ServerConfig.Instance.ArcadeRule.ConfigFingerprint,Players=new(){new(){PlayerId=1,Lives=3,Ready=true,RoundHealth=100,RoundAlive=true,Bombs=2,Dashes=2,WeaponId="arcade.weapon.basic"},new(){PlayerId=2,Lives=3,Ready=true,RoundHealth=100,RoundAlive=true,WeaponId="arcade.weapon.basic"}}};
        var battle=OnlineArcadeRunActorSystem.PrepareBattle(r,false);foreach(var p in battle.Players.Values)p.ReadyRoundGeneration=battle.RoundGeneration;
        return new(){Record=r,Battle=battle};
    }
    private static void Require(bool ok){if(!ok)throw new InvalidOperationException("RPC or upgrade input contract violated");}
}
