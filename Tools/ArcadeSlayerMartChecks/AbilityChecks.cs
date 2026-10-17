using FirepowerLoop.Arcade;

/// <summary>能力参数直接读取生成配置；每项只验证一个外部可观察合同。</summary>
internal static class AbilityChecks
{
    public static int Run(int max)
    {
        var tables=new Tables(n=>new Luban.ByteBuf(File.ReadAllBytes(Path.Combine("Client/Assets/Game/Config",n+".bytes"))));
        var config=ArcadeSlayerMartConfiguration.FromTables(tables);
        float P(string id)=>config.Parameter(id);
        ArcadeSlayerMartPlayer State(int charge=0)=>new(){VestSlots=2,Plates=2,StormCharge=charge,Owned=new(){"slayermart.item.16"}};
        SlayerMartBlockResult Block(ArcadeSlayerMartPlayer s,bool shield=false,bool protect=false,SlayerMartHitKind kind=SlayerMartHitKind.Ordinary)=>ArcadeSlayerMartAbilityRules.TryBlockHit(kind,s,protect,shield);
        var tests=new (string,Action)[]{
            ("Protection consumes neither shield nor plate",()=>{var s=State();Require(Block(s,true,true)==SlayerMartBlockResult.Protected&&s.Plates==2);}),
            ("Shield precedes plate",()=>{var s=State();Require(Block(s,true)==SlayerMartBlockResult.Shield&&s.Plates==2);}),
            ("One legal hit consumes one plate",()=>{var s=State();Require(Block(s)==SlayerMartBlockResult.Plate&&s.Plates==1);}),
            ("Empty vest cannot block",()=>{var s=State();s.Plates=0;Require(Block(s)==SlayerMartBlockResult.None);}),
            ("Lethal mechanism bypasses new plates",()=>{var s=State();Require(Block(s,kind:SlayerMartHitKind.LethalMechanism)==SlayerMartBlockResult.None&&s.Plates==2);}),
            ("Legacy shield still blocks lethal mechanism",()=>{var s=State();Require(Block(s,true,kind:SlayerMartHitKind.LethalMechanism)==SlayerMartBlockResult.Shield&&s.Plates==2);}),
            ("Clone owns ability charge and mastery",()=>{var s=State(99);var copy=s.Clone();copy.StormCharge=100;copy.Owned.Clear();Require(s.StormCharge==99&&s.Owned.Contains("slayermart.item.16"));}),
            ("Weapon mastery rounds once",()=>{var s=State();s.Owned.Add("slayermart.item.12");Require(ArcadeSlayerMartAbilityRules.ResolveDamage(10,SlayerMartDamageSource.Weapon,s,P)==12);}),
            ("Chicken receives dead operator once",()=>{var s=State();s.Owned.Add("slayermart.item.12");Require(ArcadeSlayerMartAbilityRules.ResolveDamage(20,SlayerMartDamageSource.Chicken,s,P)==25);}),
            ("Unrelated damage never receives dead operator",()=>{var s=State();s.Owned.Add("slayermart.item.12");foreach(var source in new[]{SlayerMartDamageSource.Bomb,SlayerMartDamageSource.Barrel,SlayerMartDamageSource.Orb,SlayerMartDamageSource.Saw,SlayerMartDamageSource.Gloves,SlayerMartDamageSource.Storm})Require(ArcadeSlayerMartAbilityRules.ResolveDamage(20,source,s,P)==20);}),
            ("Sentry gets only own mastery",()=>{var s=State();s.Owned.UnionWith(new[]{"slayermart.item.12","slayermart.item.23"});Require(ArcadeSlayerMartAbilityRules.ResolveDamage(20,SlayerMartDamageSource.Sentry,s,P)==35);}),
            ("Ninety nine kills cannot activate",()=>{var s=State(99);Require(!ArcadeSlayerMartAbilityRules.TryActivate(s,true,P)&&s.StormCharge==99);}),
            ("Hundredth own kill charges exactly once",()=>{var s=State(99);ArcadeSlayerMartAbilityRules.RegisterKill(s,true,false,P);ArcadeSlayerMartAbilityRules.RegisterKill(s,true,false,P);Require(s.StormCharge==100);}),
            ("Unknown ownership and storm kills cannot charge",()=>{var s=State();ArcadeSlayerMartAbilityRules.RegisterKill(s,false,false,P);ArcadeSlayerMartAbilityRules.RegisterKill(s,true,true,P);Require(s.StormCharge==0);}),
            ("Locked ability cannot charge",()=>{var s=State();s.Owned.Clear();ArcadeSlayerMartAbilityRules.RegisterKill(s,true,false,P);Require(s.StormCharge==0);}),
            ("Blocked use retains full charge",()=>{var s=State(100);Require(!ArcadeSlayerMartAbilityRules.TryActivate(s,false,P)&&s.StormCharge==100);}),
            ("Activation consumes one use without immediate tick",()=>{var s=State(100);Require(ArcadeSlayerMartAbilityRules.TryActivate(s,true,P)&&s.StormCharge==0&&s.StormRemaining==10);Require(ArcadeSlayerMartAbilityRules.AdvanceStorm(s,0,true,P)==0);}),
            ("Full half second causes first tick",()=>{var s=State(100);ArcadeSlayerMartAbilityRules.TryActivate(s,true,P);Require(ArcadeSlayerMartAbilityRules.AdvanceStorm(s,.49,true,P)==0);Require(ArcadeSlayerMartAbilityRules.AdvanceStorm(s,.01,true,P)==1);}),
            ("Ten second boundary produces twenty ticks",()=>{var s=State(100);ArcadeSlayerMartAbilityRules.TryActivate(s,true,P);Require(ArcadeSlayerMartAbilityRules.AdvanceStorm(s,11,true,P)==20&&s.StormRemaining==0);Require(ArcadeSlayerMartAbilityRules.AdvanceStorm(s,1,true,P)==0);s.StormRemaining=10;s.StormTickElapsed=60;bool rejected=false;try{ArcadeSlayerMartAbilityRules.AdvanceStorm(s,.5,true,P);}catch(InvalidOperationException){rejected=true;}Require(rejected);}),
            ("Frozen clock retains complete phase",()=>{var s=State(100);ArcadeSlayerMartAbilityRules.TryActivate(s,true,P);Require(ArcadeSlayerMartAbilityRules.AdvanceStorm(s,5,false,P)==0&&s.StormRemaining==10&&s.StormTickElapsed==0);}),
            ("Active storm cannot recharge",()=>{var s=State(100);ArcadeSlayerMartAbilityRules.TryActivate(s,true,P);ArcadeSlayerMartAbilityRules.RegisterKill(s,true,false,P);Require(s.StormCharge==0);}),
            ("Control leaves bosses and outside enemies free",()=>{Require(ArcadeSlayerMartAbilityRules.StormSpeedScale(true,false,false,P)==0);Require(ArcadeSlayerMartAbilityRules.StormSpeedScale(true,false,true,P)==.5f);Require(ArcadeSlayerMartAbilityRules.StormSpeedScale(true,true,false,P)==1&&ArcadeSlayerMartAbilityRules.StormSpeedScale(false,false,false,P)==1);}),
            ("Death retains vest mastery and unused charge",()=>{var s=State(100);s.StormRemaining=4;ArcadeSlayerMartAbilityRules.OnDeath(s);Require(s.VestSlots==2&&s.Plates==0&&s.StormCharge==100&&s.Owned.Contains("slayermart.item.16")&&s.StormRemaining==0&&s.StormTickElapsed==0);}),
            ("Respawn mastery affects only new respawn protection",()=>{var s=State();Require(ArcadeSlayerMartAbilityRules.RespawnProtection(2,s,P)==4);s.Owned.Add("slayermart.item.21");Require(ArcadeSlayerMartAbilityRules.RespawnProtection(2,s,P)==6);})
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS SlayerMartAbilityRules."+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerMartAbilityRules."+name+": "+ex.Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static void Require(bool value){if(!value)throw new InvalidOperationException("Ability contract violated");}
}
