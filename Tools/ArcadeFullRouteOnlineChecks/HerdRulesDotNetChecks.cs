using FirepowerLoop.Arcade;

/// <summary>验证服务端实际链接的牛群纯规则，不启动服务器、不修改关卡。</summary>
internal static class HerdRulesDotNetChecks
{
    public static int Run(int maximum)
    {
        var checks = new (string, Action)[] {
            ("WindupAndSweep", () => {
                var s=Start();Require(!ArcadeHerdRules.Step(s,T,.7f,true).Active,"windup moved");
                var sweep=ArcadeHerdRules.Step(s,T,2.1f,true);
                Require(ArcadeHerdRules.TryContact(s,sweep,10,1,0,0,.4f,true),"fast crossing missed");
            }),
            ("ContactIdentity", () => {
                var s=Start();var sweep=ArcadeHerdRules.Step(s,T,2,true);
                Require(ArcadeHerdRules.TryContact(s,sweep,10,-1,-10,0,.4f,true),"player missing");
                Require(ArcadeHerdRules.TryContact(s,sweep,10,1,-10,0,.4f,true),"enemy identity collided");
                Require(!ArcadeHerdRules.TryContact(s,sweep,10,-1,-10,0,.4f,true),"duplicate contact");
            }),
            ("PauseAndMapCancel", () => {
                var s=Start();var sweep=ArcadeHerdRules.Step(s,T,1,true);
                ArcadeHerdRules.Step(s,T,100,false);Require(Math.Abs(s.ElapsedSeconds-1)<.001,"paused clock advanced");
                Require(!ArcadeHerdRules.TryContact(s,sweep,10,1,-17,0,.4f,true),"paused sweep replayed");
                sweep=ArcadeHerdRules.Step(s,T,.1f,true);ArcadeHerdRules.Cancel(s);
                Require(!ArcadeHerdRules.TryContact(s,sweep,10,1,s.X,0,.4f,true),"released sweep replayed");
            }),
            ("FinalStepAndInvalidInputs", () => {
                var s=Start();var sweep=ArcadeHerdRules.Step(s,T,1000,true);
                Require(s.Phase==ArcadeHerdPhase.Expired&&Math.Abs(s.X-18)<.001,"unbounded charge");
                Require(ArcadeHerdRules.TryContact(s,sweep,10,1,0,0,.4f,true),"last sweep lost");
                Require(!ArcadeHerdRules.TryStart(10,1,0,0,0,0,T,out _),"zero direction accepted");
                Require(!ArcadeHerdRules.TryContact(s,sweep,11,2,0,0,.4f,true),"wrong map hit");
            })
        };
        if(maximum<checks.Length)throw new ArgumentException("HerdRulesDotNet maximum too small");
        int failures=0;foreach(var (name,test) in checks)try{test();Console.WriteLine("PASS HerdRulesDotNet."+name);}catch(Exception e){failures++;Console.WriteLine("FAIL HerdRulesDotNet."+name+": "+e);}
        Console.WriteLine($"{checks.Length-failures}/{checks.Length} passed");return failures==0?0:1;
    }
    private static ArcadeHerdTuning T=>new(.8f,12,36,.65f);
    private static ArcadeHerdState Start(){Require(ArcadeHerdRules.TryStart(10,1,-18,0,3,0,T,out var state),"invalid fixture");return state;}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
