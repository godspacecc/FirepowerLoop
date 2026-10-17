using FirepowerLoop.Arcade;

/// <summary>通过正式保存校验入口检查坏节点，不用序列化字符串比较替代领域断言。</summary>
internal static class SaveChecks
{
    public static int Run(int max)
    {
        ArcadeSlayerMartState State()=>new(){RegionInstanceId="run1:slayermart:1",Visit=1,Generation=1,Revision=2,Seed=314,RemainingSeconds=250,Ready=true,Closing=true,
            Players=new(){new(){PlayerId=1,Lives=3,Cash=18000,Offers=Enumerable.Range(1,16).Select(n=>"slayermart.item."+n.ToString("00")).ToList()}}};
        bool Valid(ArcadeSlayerMartState s,int completed=11)=>ArcadeSlayerMartSaveRules.TryValidate(s,"run1",new long[]{1},completed,out _);
        var tests=new (string,Action)[]{
            ("Closed region attached to next ordinary round is valid",()=>Require(Valid(State()),"valid snapshot rejected")),
            ("Open shopping state cannot become save node",()=>{var s=State();s.Closing=false;Require(!Valid(s),"midshop saved");}),
            ("Foreign run or member cannot restore",()=>{var s=State();s.RegionInstanceId="other:slayermart:1";Require(!Valid(s),"foreign run");s=State();s.Players[0].PlayerId=2;Require(!Valid(s),"foreign member");}),
            ("Unknown version or early visit is rejected",()=>{var s=State();s.RulesVersion=2;Require(!Valid(s),"unknown version");Require(!Valid(State(),10),"early shop");s=State();s.Visit=2;s.RegionInstanceId="run1:slayermart:2";Require(!Valid(s,30),"early second shop");}),
            ("Invalid balances timers and duplicate cards cannot restore",()=>{var s=State();s.Players[0].Cash=-1;Require(!Valid(s),"negative cash");s=State();s.RemainingSeconds=float.NaN;Require(!Valid(s),"NaN timer");s=State();s.Players[0].Offers[0]=s.Players[0].Offers[1];Require(!Valid(s),"duplicate card");}),
            ("Duplicate receipts cannot replay ambiguously",()=>{var s=State();s.Receipts.Add(new(){PlayerId=1,OperationId="op",Fingerprint="buy"});s.Receipts.Add(new(){PlayerId=1,OperationId="op",Fingerprint="reroll"});Require(!Valid(s),"ambiguous operation");}),
            ("Duplicate or foreign grants are refused",()=>{var s=State();s.PendingGrants.Add(new(){EventId="run1:slayermart:1:1",PlayerId=2,Effect=SlayerMartEffect.Bomb,Amount=2});Require(!Valid(s),"foreign beneficiary");s.PendingGrants[0].PlayerId=1;s.PendingGrants.Add(s.PendingGrants[0].Clone());Require(!Valid(s),"duplicate event");}),
            ("Caller clone cannot change committed state",()=>{var s=State();Require(Valid(s),"valid clone prerequisite");var saved=s.Clone();s.Players[0].Cash=0;s.Players[0].Offers.Clear();Require(saved.Players[0].Cash==18000&&saved.Players[0].Offers.Count==16&&Valid(saved),"snapshot aliases caller");})
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS SlayerMartSave."+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerMartSave."+name+": "+ex.Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}
