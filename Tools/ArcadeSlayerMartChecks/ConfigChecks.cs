using FirepowerLoop.Arcade;
using Luban;

/// <summary>从本项目真实双端bytes读取新表，不拿测试fixture替代配置生成证据。</summary>
internal static class ConfigChecks
{
    public static int Run(int max)
    {
        var table=new Tables(name=>new ByteBuf(File.ReadAllBytes(Path.Combine("Client/Assets/Game/Config",name+".bytes"))));
        var tests=new (string,Action)[]{
            ("Twenty four formal items and closed release gate",()=>{var c=ArcadeSlayerMartConfiguration.FromTables(table);Require(c.Catalog.Items.Count==24&&!c.Enabled,"incomplete pool or early release");Require(c.Catalog.Items[0].Price==3000&&c.Catalog.Items[23].Price==7500,"prices changed");}),
            ("Two regions retain exact personal cash budgets",()=>{var c=ArcadeSlayerMartConfiguration.FromTables(table);Require(c.Catalog.Regions[0].AfterRound==11&&c.Catalog.Regions[0].CashBoxes*c.Catalog.Regions[0].CashPerMember==18000,"first budget");Require(c.Catalog.Regions[1].AfterRound==31&&c.Catalog.Regions[1].CashBoxes*c.Catalog.Regions[1].CashPerMember==22000,"second budget");}),
            ("Storm configured exactly as approved",()=>{var c=ArcadeSlayerMartConfiguration.FromTables(table);Require(c.Parameter("storm.kills")==100&&c.Parameter("storm.duration")==10&&c.Parameter("storm.radius")==5&&c.Parameter("storm.damage")==20&&c.Parameter("storm.interval")==.5f,"storm differs");}),
            ("Every new binary identical across client and server",()=>{foreach(string name in new[]{"tbarcadeslayermartitem","tbarcadeslayermartregion","tbarcadeslayermartability"})Require(File.ReadAllBytes("Client/Assets/Game/Config/"+name+".bytes").SequenceEqual(File.ReadAllBytes("Server/GameServer/Config/Luban/"+name+".bytes")),"binary mismatch "+name);}),
            ("Duplicate cash anchors are rejected",()=>{var row=table.TbArcadeSlayerMartRegion.DataList[0];var old=row.CashBoxes[1];try{row.CashBoxes[1]=row.CashBoxes[0];bool bad=false;try{ArcadeSlayerMartConfiguration.FromTables(table);}catch(ArgumentException){bad=true;}Require(bad,"duplicate anchor accepted");}finally{row.CashBoxes[1]=old;}}),
            ("Missing ability parameter cannot use hidden fallback",()=>{var map=table.TbArcadeSlayerMartAbility.DataMap;var old=map["storm.kills"];try{map.Remove("storm.kills");bool bad=false;try{ArcadeSlayerMartConfiguration.FromTables(table);}catch(ArgumentException){bad=true;}Require(bad,"missing parameter silently defaulted");}finally{map.Add(old.Id,old);}}),
            ("Float rounded Int32 overflow is rejected",()=>{var map=table.TbArcadeSlayerMartAbility.DataMap;var old=map["storm.kills"];try{var bytes=new ByteBuf();bytes.WriteString(old.Id);bytes.WriteFloat(2147483648f);map[old.Id]=new ArcadeSlayerMartAbilityConfig(new ByteBuf(bytes.CopyData()));bool bad=false;try{ArcadeSlayerMartConfiguration.FromTables(table);}catch(ArgumentException){bad=true;}Require(bad,"2147483648 accepted as Int32");}finally{map[old.Id]=old;}})
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS SlayerMartConfig."+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerMartConfig."+name+": "+ex.Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}
