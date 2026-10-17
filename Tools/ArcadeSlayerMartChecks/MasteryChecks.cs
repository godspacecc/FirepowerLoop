using FirepowerLoop.Arcade;

/// <summary>精通仅求值声明来源，正式表提供所有倍率。</summary>
internal static class MasteryChecks
{
    public static int Run(int max)
    {
        var tables=new Tables(n=>new Luban.ByteBuf(File.ReadAllBytes(Path.Combine("Client/Assets/Game/Config",n+".bytes"))));
        var config=ArcadeSlayerMartConfiguration.FromTables(tables);float P(string key)=>config.Parameter(key);
        ArcadeSlayerMartPlayer S(params int[] ids)=>new(){Owned=ids.Select(n=>"slayermart.item."+n.ToString("00")).ToHashSet()};
        float Duration(SlayerMartEffect effect,ArcadeSlayerMartPlayer state)=>ArcadeSlayerMartAbilityRules.MasteryDuration(10,effect,state,P);
        float Radius(SlayerMartEffect effect,ArcadeSlayerMartPlayer state)=>ArcadeSlayerMartAbilityRules.MasteryRadius(4,effect,state,P);
        var tests=new (string,Action)[]{
            ("Magnet lifetime is one and a half",()=>Require(Duration(SlayerMartEffect.MagnetMastery,S(7)),15)),
            ("Magnet radius is one and a half",()=>Require(Radius(SlayerMartEffect.MagnetMastery,S(7)),6)),
            ("Decoy radius doubles without lifetime bonus",()=>{Require(Radius(SlayerMartEffect.DecoyMastery,S(8)),8);Require(Duration(SlayerMartEffect.DecoyMastery,S(8)),10);}),
            ("Gloves lifetime is one point six",()=>Require(Duration(SlayerMartEffect.GlovesMastery,S(9)),16)),
            ("Chicken lifetime is one and a quarter",()=>Require(Duration(SlayerMartEffect.ChickenMastery,S(10)),12.5f)),
            ("Barrel lifetime is one and a quarter",()=>Require(Duration(SlayerMartEffect.BarrelMastery,S(22)),12.5f)),
            ("Barrel orbit speeds without granting charges",()=>{var s=S(22);Require(ArcadeSlayerMartAbilityRules.MasteryOrbitSpeed(4,s,P),5);Require(s.StormCharge,0);}),
            ("Saw distance doubles without damage bonus",()=>{var s=S(11);Require(Radius(SlayerMartEffect.SawMastery,s),8);Require(ArcadeSlayerMartAbilityRules.ResolveDamage(20,SlayerMartDamageSource.Saw,s,P),20);}),
            ("Missing mastery preserves original duration",()=>Require(Duration(SlayerMartEffect.ChickenMastery,S()),10)),
            ("Other mastery cannot change magnet",()=>{Require(Duration(SlayerMartEffect.MagnetMastery,S(10)),10);Require(Radius(SlayerMartEffect.MagnetMastery,S(8)),4);}),
            ("Duplicate ownership never stacks multiplier",()=>{var s=S(7);s.Owned.Add("slayermart.item.07");Require(Radius(SlayerMartEffect.MagnetMastery,s),6);Require(Radius(SlayerMartEffect.MagnetMastery,s),6);}),
            ("Lifetime query cannot refresh active remaining",()=>{var s=S(10);s.StormRemaining=3;Require(Duration(SlayerMartEffect.ChickenMastery,s),12.5f);Require((float)s.StormRemaining,3);})
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS SlayerMartMasteryRules."+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerMartMasteryRules."+name+": "+ex.Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static void Require(float actual,float expected){if(Math.Abs(actual-expected)>.0001f)throw new InvalidOperationException($"Expected {expected}, actual {actual}");}
}
