using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;

/// <summary>宝石和下轮加命使用正式积分规则且整体候选幂等。</summary>
internal static class BenefitChecks
{
    private static readonly Tables Tables=new(n=>new ByteBuf(File.ReadAllBytes("Client/Assets/Game/Config/"+n+".bytes")));
    private static readonly ArcadeSlayerMartConfiguration Config=ArcadeSlayerMartConfiguration.FromTables(Tables);
    private static readonly ArcadeTreasureRoomParameters Treasure=Tables.TbArcadeTreasureRoom.DataList.Select(ArcadeTreasureRoomParameters.FromConfig).First(p=>p.Rewards.Any(r=>r.Kind==FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Gem));
    public static int Run(int max)
    {
        var tests=new (string,Action)[]{
            ("Team life delivers to each original member exactly once",()=>{var s=State(SlayerMartEffect.NextRoundLife,1);s.Players.Add(new(){PlayerId=2,Lives=0});s.GrantSequence=2;s.PendingGrants.Add(new(){EventId="run:slayermart:1:2",PlayerId=2,Effect=SlayerMartEffect.NextRoundLife,Amount=1});var scores=Scores();scores[2]=new(0,1,0,0);Require(Deliver(s,scores,true,out var next,out var result)&&result[1].Lives==4&&result[2].Lives==1);var clone=next.Clone();clone.Players[0].AppliedBenefitGrants.Clear();Require(next.Players[0].AppliedBenefitGrants.Count==1);}),
            ("Personal gems never copy points to teammate",()=>{var s=State(SlayerMartEffect.Gems,8);s.Players.Add(new(){PlayerId=2,Lives=3});var scores=Scores();scores[2]=new(0,1,0,3);Require(Deliver(s,scores,false,out _,out var result)&&result[1].Score>0&&result[2].Score==0);}),
            ("Gems follow actual per-gem score and multiplier order",()=>{var s=State(SlayerMartEffect.Gems,8);var scores=Scores();var expected=scores[1];var gem=Treasure.Rewards.First(r=>r.Kind==FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Gem);for(int i=0;i<8;i++){Require(ArcadeTreasureScoreRules.TryAward(expected,gem,Treasure,out var n));expected=n;}Require(Deliver(s,scores,false,out _,out var result)&&result[1].Score==expected.Score&&result[1].Multiplier==expected.Multiplier&&result[1].Progress==expected.Progress);}),
            ("Replay cannot grant gems twice",()=>{var s=State(SlayerMartEffect.Gems,8);Require(Deliver(s,Scores(),false,out var next,out var scores));long score=scores[1].Score;Require(Deliver(next,scores,false,out _,out var again)&&again[1].Score==score);}),
            ("Gem score overflow leaves original cash and ledger intact",()=>{var s=State(SlayerMartEffect.Gems,8);var scores=Scores(long.MaxValue-1);Require(!Deliver(s,scores,false,out var next,out _)&&next==null&&s.Players[0].Cash==500&&scores[1].Score==long.MaxValue-1);}),
            ("Gem life threshold uses the existing one-time score reward",()=>{var s=State(SlayerMartEffect.Gems,8);Require(Deliver(s,Scores(Treasure.LifeThreshold-1),false,out var next,out var result)&&result[1].Lives==4&&next.Players[0].Lives==4);}),
            ("Purchasing team life never grants it inside store",()=>{var s=State(SlayerMartEffect.NextRoundLife,1);Require(Deliver(s,Scores(),false,out _,out var result)&&result[1].Lives==3);}),
            ("Next ordinary round applies team life once",()=>{var s=State(SlayerMartEffect.NextRoundLife,1);Require(Deliver(s,Scores(),true,out var next,out var result)&&result[1].Lives==4);Require(Deliver(next,result,true,out _,out var again)&&again[1].Lives==4);}),
            ("Full lives clip without storing a future benefit",()=>{var s=State(SlayerMartEffect.NextRoundLife,1);var scores=Scores(lives:9);Require(Deliver(s,scores,true,out var next,out var result)&&result[1].Lives==9);result[1]=new(0,1,0,8);Require(Deliver(next,result,true,out _,out var again)&&again[1].Lives==8);}),
            ("Zero lives receive count only in next round",()=>{var s=State(SlayerMartEffect.NextRoundLife,1);Require(Deliver(s,Scores(lives:0),true,out var next,out var result)&&result[1].Lives==1&&next.Players[0].Lives==1);}),
            ("Unknown beneficiary rejects whole candidate",()=>{var s=State(SlayerMartEffect.Gems,8);s.PendingGrants[0].PlayerId=999;Require(!Deliver(s,Scores(),false,out var next,out _)&&next==null);}),
            ("Rejected late grant cannot publish prior gem award",()=>{var s=State(SlayerMartEffect.Gems,8);s.PendingGrants.Add(new(){EventId="run:slayermart:1:2",PlayerId=1,Effect=SlayerMartEffect.Gems,Amount=999});Require(!Deliver(s,Scores(),false,out var next,out _)&&next==null);})
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS SlayerMartBenefitRules."+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerMartBenefitRules."+name+": "+(ex.InnerException??ex).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static ArcadeSlayerMartState State(SlayerMartEffect effect,int amount)
    {var s=ArcadeSlayerMartRules.Create("run",1,new long[]{1},7,Config.Catalog);s.Ready=true;s.Closing=effect==SlayerMartEffect.NextRoundLife;s.GrantSequence=1;s.Players[0].Cash=500;s.Players[0].Lives=3;s.PendingGrants.Add(new(){EventId="run:slayermart:1:1",PlayerId=1,Effect=effect,Amount=amount});return s;}
    private static Dictionary<long,ArcadeTreasureScoreState> Scores(long score=0,int lives=3)=>new(){{1,new(score,1,0,lives)}};
    private static bool Deliver(ArcadeSlayerMartState state,Dictionary<long,ArcadeTreasureScoreState> scores,bool nextRound,out ArcadeSlayerMartState next,out Dictionary<long,ArcadeTreasureScoreState> result)
    {
        var type=typeof(ArcadeSlayerMartRules).Assembly.GetType("FirepowerLoop.Arcade.ArcadeSlayerMartBenefitRules")??throw new InvalidOperationException("Missing atomic benefits delivery");
        object[] args={state,Config,Treasure,scores,nextRound,null,null,null};bool ok=(bool)type.GetMethod("TryDeliver").Invoke(null,args);next=(ArcadeSlayerMartState)args[5];result=(Dictionary<long,ArcadeTreasureScoreState>)args[6];return ok;
    }
    private static void Require(bool ok){if(!ok)throw new InvalidOperationException("Benefit contract violated");}
}
