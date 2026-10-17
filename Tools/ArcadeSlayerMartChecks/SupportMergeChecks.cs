using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;

/// <summary>普通槽向购买候选交接，不回填次数、相位和冷却。</summary>
internal static class SupportMergeChecks
{
    private static readonly ArcadeSlayerMartConfiguration Config=ArcadeSlayerMartConfiguration.FromTables(new Tables(n=>new ByteBuf(File.ReadAllBytes("Client/Assets/Game/Config/"+n+".bytes"))));
    public static int Run(int max)
    {
        var tests=new (string,Action)[]{
            ("Combo extends relay active share without refilling or breaking duration bounds",()=>{var s=State(SlayerMartEffect.BarrelRelay,3);Require(Apply(s,null,out var n));n.Players[0].Supports[0].Charges=1;n.PendingGrants.Add(new(){EventId="run:slayermart:1:2",PlayerId=1,Effect=SlayerMartEffect.SawBarrel,Amount=1,Duration=300});n.GrantSequence=2;Require(Apply(n,null,out var next));var barrel=next.Players[0].Supports.Single(p=>p.Kind==SlayerMartSupportKind.Barrel);Require(barrel.Charges==1&&barrel.RemainingSeconds==300&&next.Players[0].Queues.Single().Waiting.Count==2);}),
            ("Combo preserves spent ordinary barrel charges and phase",()=>{var s=State(SlayerMartEffect.SawBarrel,1,300);var old=Ordinary(SlayerMartSupportKind.Barrel);Require(Apply(s,old,out var n));var barrel=n.Players[0].Supports.Single(p=>p.Kind==SlayerMartSupportKind.Barrel);Require(barrel.RemainingSeconds==300&&barrel.Charges==1&&barrel.Phase==.7&&barrel.CooldownSeconds==.2);}),
            ("Saw keeps phase and target cooldown across source handoff",()=>{var s=State(SlayerMartEffect.Saw,1,300);var old=Ordinary(SlayerMartSupportKind.Saw);s.Players[0].SupportHits.Add(new(){GroupId=old.GroupId,TargetKey="map:1",RemainingSeconds=.1});Require(Apply(s,old,out var n));var p=n.Players[0];Require(p.Supports.Single().Phase==.7&&p.SupportHits.Single().GroupId==p.Supports.Single().GroupId&&p.SupportHits.Single().RemainingSeconds==.1);}),
            ("Relay waits behind remaining ordinary share",()=>{var s=State(SlayerMartEffect.BarrelRelay,3);var old=Ordinary(SlayerMartSupportKind.Barrel);Require(Apply(s,old,out var n));var p=n.Players[0];Require(p.Supports.Single().Charges==1&&p.Supports.Single().RemainingSeconds==8&&p.Queues.Single().Waiting.Count==3);ArcadeSlayerMartSupportRules.Advance(p,8,true,Config.Supports,Config.Parameter);Require(p.Supports.Single().Charges==3&&p.Supports.Single().RemainingSeconds==Config.Supports.Get(SlayerMartSupportKind.Barrel).Duration&&p.Queues.Single().Waiting.Count==2);}),
            ("Invalid ordinary snapshot rejects whole delivery",()=>{var s=State(SlayerMartEffect.SawBarrel,1,300);var old=Ordinary(SlayerMartSupportKind.Barrel);old.RemainingSeconds=double.NaN;Require(!Apply(s,old,out var n)&&n==null&&s.Players[0].Supports.Count==0);}),
            ("Existing purchased barrel never refills from ordinary handoff",()=>{var s=State(SlayerMartEffect.SawBarrel,1,300);Require(Apply(s,null,out var n));n.Players[0].Supports.Single(p=>p.Kind==SlayerMartSupportKind.Barrel).Charges=1;n.PendingGrants.Add(new(){EventId="run:slayermart:1:2",PlayerId=1,Effect=SlayerMartEffect.SawBarrel,Amount=1,Duration=300});n.GrantSequence=2;var old=Ordinary(SlayerMartSupportKind.Barrel);old.Charges=3;Require(Apply(n,old,out var next)&&next.Players[0].Supports.Single(p=>p.Kind==SlayerMartSupportKind.Barrel).Charges==1);})
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS SlayerMartSupportMergeTests."+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerMartSupportMergeTests."+name+": "+(ex.InnerException??ex).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static ArcadeSlayerMartState State(SlayerMartEffect effect,int amount,float duration=0)
    {var s=ArcadeSlayerMartRules.Create("run",1,new long[]{1},7,Config.Catalog);s.Ready=true;s.GrantSequence=1;s.Players[0].Lives=3;s.PendingGrants.Add(new(){EventId="run:slayermart:1:1",PlayerId=1,Effect=effect,Amount=amount,Duration=duration});return s;}
    private static ArcadeSlayerMartEffectInstance Ordinary(SlayerMartSupportKind kind)=>new(){OwnerId=1,Kind=kind,ProductId=Config.Supports.Get(kind).ProductId,GroupId="ordinary:1:"+kind,RemainingSeconds=8,Charges=kind==SlayerMartSupportKind.Barrel?1:0,Phase=.7,CooldownSeconds=.2};
    private static bool Apply(ArcadeSlayerMartState s,ArcadeSlayerMartEffectInstance ordinary,out ArcadeSlayerMartState next)
    {
        var method=typeof(ArcadeSlayerMartSupportRules).GetMethods().SingleOrDefault(p=>p.Name=="TryApplyGrants"&&p.GetParameters().Length==6)??throw new InvalidOperationException("Missing ordinary support bridge");
        IReadOnlyDictionary<long,IReadOnlyList<ArcadeSlayerMartEffectInstance>> sources=new Dictionary<long,IReadOnlyList<ArcadeSlayerMartEffectInstance>>{{1,ordinary==null?Array.Empty<ArcadeSlayerMartEffectInstance>():new[]{ordinary}}};
        object[] args={s,Config,new Dictionary<long,int>(),null,null,sources};
        bool ok=(bool)method.Invoke(null,args);next=(ArcadeSlayerMartState)args[3];return ok;
    }
    private static void Require(bool ok){if(!ok)throw new InvalidOperationException("Support handoff contract violated");}
}
