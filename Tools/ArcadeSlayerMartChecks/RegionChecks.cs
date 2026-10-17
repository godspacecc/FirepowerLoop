using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;

/// <summary>商店实际轮号、跨店继承及准备／截止时钟的同源检查。</summary>
internal static class RegionChecks
{
    private static readonly ArcadeSlayerMartConfiguration Config=ArcadeSlayerMartConfiguration.FromTables(new Tables(n=>new ByteBuf(File.ReadAllBytes("Client/Assets/Game/Config/"+n+".bytes"))));
    public static int Run(int max)
    {
        var tests=new (string,Action)[]{
            ("Eleventh actual ordinary round opens first visit",()=>Require(Begin(11)?.Visit==1)),
            ("Thirty first round opens second after closed first",()=>{var old=Begin(11);old.Closing=old.Ready=true;Require(Begin(31,old)?.Visit==2);}),
            ("Other round or challenge never creates a store",()=>{Require(Begin(12)==null);Require(Begin(11,challenge:true)==null);}),
            ("Same visit cannot be entered twice",()=>{var old=Begin(11);old.Closing=true;Require(Begin(11,old)==null);}),
            ("Slow map load cannot consume shopping deadline",()=>{var s=Begin(11);s.Players[0].Choice=1;var next=Step(s,100,false);Require(!next.Ready&&next.RemainingSeconds==300&&s.RemainingSeconds==300);}),
            ("Ready frame starts full deadline without charging load time",()=>{var s=Begin(11);s.Players[0].Choice=1;var next=Step(s,100,true);Require(next.Ready&&next.RemainingSeconds==300);}),
            ("Open menu does not pause ready shopping clock",()=>{var s=Ready();s.Players[0].MenuOpen=true;Require(Step(s,10,true).RemainingSeconds==290);}),
            ("Hard deadline closes with open menu",()=>{var s=Ready();s.Players[0].MenuOpen=true;var next=Step(s,301,true);Require(next.Closing&&next.RemainingSeconds==0&&!next.Players[0].MenuOpen);}),
            ("Pause or disconnect freezes the whole deadline",()=>{var s=Ready();Require(Step(s,100,true,true).RemainingSeconds==300);}),
            ("Skip closes without giving cash",()=>{var s=Begin(11);s.Players[0].Choice=2;var next=Step(s,0,false);Require(next.Closing&&next.Ready&&next.Players[0].Cash==0&&next.CollectedBoxes.Count==0);}),
            ("All finished only closes after every menu is closed",()=>{var s=Ready();s.Players[0].Finished=true;s.Players[0].MenuOpen=true;Require(!Step(s,0,true).Closing);s.Players[0].MenuOpen=false;Require(Step(s,0,true).Closing);}),
            ("Second visit preserves economy and owned abilities without old visit limits",()=>{var s=Ready();s.Closing=true;var p=s.Players[0];p.Cash=700;p.Tokens=2;p.VestSlots=2;p.StormCharge=99;p.Owned.Add("slayermart.item.11");p.Owned.Add("slayermart.item.16");p.Bought.Add("slayermart.item.01");p.OrdinarySawPhase=.7;var next=Begin(31,s);var q=next.Players[0];Require(q.Cash==700&&q.Tokens==2&&q.VestSlots==2&&q.StormCharge==99&&q.Owned.Contains("slayermart.item.11")&&q.Bought.Count==0&&q.Choice==0&&q.OrdinarySawPhase==.7&&q.Offers.Count==16);q.Owned.Clear();Require(p.Owned.Count==2);}),
            ("Conflicting team choices neither enter nor skip",()=>{var s=Begin(11,members:new long[]{1,2});s.Players[0].Choice=1;s.Players[1].Choice=2;var next=Step(s,100,true);Require(!next.Ready&&!next.Closing&&next.RemainingSeconds==300);}),
            ("Different run cannot inherit old shopping state",()=>{var s=Ready();s.Closing=true;s.RegionInstanceId="foreign:slayermart:1";Require(Begin(31,s)==null);})
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS SlayerMartRegionRules."+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerMartRegionRules."+name+": "+(ex.InnerException??ex).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static ArcadeSlayerMartState Begin(int round,ArcadeSlayerMartState previous=null,bool challenge=false,long[] members=null)
        =>(ArcadeSlayerMartState)Method("BeginVisit").Invoke(null,new object[]{"run",round,challenge,members??new long[]{1},17UL,Config,previous});
    private static ArcadeSlayerMartState Step(ArcadeSlayerMartState s,double seconds,bool ready,bool paused=false)
        =>(ArcadeSlayerMartState)Method("AdvanceVisit").Invoke(null,new object[]{s,seconds,ready,paused});
    private static ArcadeSlayerMartState Ready(){var s=Begin(11);s.Players[0].Choice=1;s.Ready=true;return s;}
    private static MethodInfo Method(string name)=>typeof(ArcadeSlayerMartRules).GetMethod(name)??throw new InvalidOperationException("Missing region entry: "+name);
    private static void Require(bool value){if(!value)throw new InvalidOperationException("Region contract violated");}
}
