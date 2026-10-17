using ET;
using ET.Server;

/// <summary>协议能力指纹独立于存档配置指纹，旧客户端在收到新版战斗载荷前被明确拒绝。</summary>
internal static class SlayerMartProtocolGateChecks
{
    public static int Run(int max)
    {
        string saved=ServerConfig.Instance.ArcadeRule.ConfigFingerprint;string network=saved+"|slayermart.v2|coop-rescue.v2";
        bool Create(string fp)=>OnlineRoomSnapshotHelper.TryValidateCreation((int)OnlineRoomPurpose.Arcade,(int)OnlineRoomVisibility.Private,"test",fp,out _,out _,out _,out _);
        OnlineRoomActor Room(string fp)=>new(){Purpose=OnlineRoomPurpose.Arcade,ClientVersion="test",ArcadeConfigFingerprint=fp};
        var tests=new (string,Action)[]{
            ("Previous development payload cannot enter new shape",()=>Require(!Create(saved+"|slayermart.v1"))),
            ("Battle pushes require protocol acknowledgement in current login epoch",()=>{var type=typeof(OnlineRoomSnapshotHelper).Assembly.GetType("ET.Server.OnlineArcadeProtocolGuard");var method=type.GetMethod("CanSendPush")??throw new InvalidOperationException("Missing push protocol gate");var field=typeof(GatePlayerProxy).GetField("ArcadeProtocolEpoch")??throw new InvalidOperationException("Missing session protocol acknowledgement");var proxy=new GatePlayerProxy{IsAuthenticated=true,SessionEpoch=5};Require(!(bool)method.Invoke(null,new object[]{proxy}));field.SetValue(proxy,5L);Require((bool)method.Invoke(null,new object[]{proxy}));proxy.SessionEpoch=6;Require(!(bool)method.Invoke(null,new object[]{proxy}));}),
            ("Every arcade RPC gate rejects absent or old capability",()=>{var type=typeof(OnlineRoomSnapshotHelper).Assembly.GetType("ET.Server.OnlineArcadeProtocolGuard")??throw new InvalidOperationException("Missing RPC protocol gate");var method=type.GetMethod("Matches");Require(!(bool)method.Invoke(null,new object[]{null})&&!(bool)method.Invoke(null,new object[]{saved})&&(bool)method.Invoke(null,new object[]{network}));}),
            ("Old raw fingerprint cannot create or match arcade room",()=>Require(!Create(saved))),
            ("Current protocol capability can create arcade room",()=>Require(Create(network))),
            ("Different config with correct protocol still rejects",()=>Require(!Create("foreign|slayermart.v2|coop-rescue.v2"))),
            ("General room compatibility stays unchanged",()=>Require(OnlineRoomSnapshotHelper.TryValidateCreation((int)OnlineRoomPurpose.General,(int)OnlineRoomVisibility.Public,"","",out _,out _,out _,out _))),
            ("Old existing arcade room cannot accept old client",()=>Require(!OnlineRoomSnapshotHelper.IsCompatible(Room(saved),(int)OnlineRoomPurpose.Arcade,"test",saved,out _))),
            ("Matching current clients can join current room",()=>Require(OnlineRoomSnapshotHelper.IsCompatible(Room(network),(int)OnlineRoomPurpose.Arcade,"test",network,out _))),
            ("Capability derivation never rewrites saved config fingerprint",()=>{var method=typeof(FirepowerLoop.Arcade.ArcadeSlayerMartConfiguration).GetMethod("NetworkFingerprint")??throw new InvalidOperationException("Missing protocol capability derivation");Require((string)method.Invoke(null,new object[]{saved})==network&&ServerConfig.Instance.ArcadeRule.ConfigFingerprint==saved);})
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS SlayerMartProtocolGate."+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerMartProtocolGate."+name+": "+(ex.InnerException??ex).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static void Require(bool ok){if(!ok)throw new InvalidOperationException("Protocol compatibility gate violated");}
}
