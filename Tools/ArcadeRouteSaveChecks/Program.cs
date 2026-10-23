using Game;
using Newtonsoft.Json.Linq;

if(args.SequenceEqual(new[]{"testClass=NextNodeSave","maxTests=3","includePassingTests=true"}))return NextNodeSaveChecks.Run();
if(args.Length!=3 || args[0]!="testClass=RouteSave" || args[1]!="maxTests=8" || args[2]!="includePassingTests=true")throw new ArgumentException("Explicit RouteSave maxTests=8 includePassingTests=true required");
void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
ArcadeRunSaveData Make(int version,int completed=0,bool victory=false)
{
    var data=new ArcadeRunSaveData {RunId=new string('a',32),IsCasualRoute=true,CasualRouteVersion=version,ArenaConfigNumber=victory?20:1,CompletedArenaCount=completed,NextArenaNumber=completed+1,Victory=victory,Outcome=victory?"victory":null,Checkpoint=new ArcadeCheckpointSaveData {ArenaNumber=completed+1},UpdatedAtUtc=DateTime.UtcNow.ToString("O")};
    data.EquipmentBySlot["weapon"]="arcade.weapon.basic";return data;
}
void WithStore(Action<ArcadeRunFileStore> test)
{
    string parent=Path.GetFullPath("Tools/ArcadeRouteSaveChecks/.test-output");string folder=Path.GetFullPath(Path.Combine(parent,Guid.NewGuid().ToString("N")));
    Require(folder.StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"test path escaped");
    try{test(new ArcadeRunFileStore(folder));}finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
}
var tests=new (string,Action)[]
{
    ("MissingVersionRetainsLegacy",()=>WithStore(store=>{var raw=JObject.FromObject(Make(0,9));raw.Remove("CasualRouteVersion");Directory.CreateDirectory(store.SavesDirectory);File.WriteAllText(store.RunPath,raw.ToString());var read=store.Inspect();Require(read.IsValid&&read.Data.CasualRouteVersion==0,"legacy record failed to load");})),
    ("Legacy40VictoryRoundTrips",()=>WithStore(store=>{Require(store.Save(Make(0,10,true),out var error),error);Require(store.Inspect().Data.CompletedArenaCount==10&&store.Inspect().Data.CasualRouteVersion==0,"legacy victory changed");})),
    ("New52VictoryRoundTrips",()=>WithStore(store=>{Require(store.Save(Make(1,13,true),out var error),error);Require(store.Inspect().Data.CompletedArenaCount==13&&store.Inspect().Data.CasualRouteVersion==1,"new victory changed");})),
    ("NewRound40IsStillPending",()=>{var data=Make(1,10);data.ArenaConfigNumber=18;Require(ArcadeRunFileStore.TryValidate(data,out var error),error);Require(!data.Victory,"new run ended after forty rounds");}),
    ("CannotExchangeVictoryCounts",()=>{Require(!ArcadeRunFileStore.TryValidate(Make(0,13,true),out _),"legacy became52");Require(!ArcadeRunFileStore.TryValidate(Make(1,10,true),out _),"new became40");}),
    ("UnknownVersionRefused",()=>{Require(!ArcadeRunFileStore.TryValidate(Make(2),out _),"unknown route accepted");Require(!ArcadeRunFileStore.TryValidate(Make(-1),out _),"negative route accepted");}),
    ("LegacyCannotInsertNewStage",()=>{var data=Make(0,3);data.ArenaConfigNumber=23;Require(!ArcadeRunFileStore.TryValidate(data,out _),"old run inserted cemetery");data.CasualRouteVersion=1;Require(ArcadeRunFileStore.TryValidate(data,out var error),error);Require(data.Clone().CasualRouteVersion==1,"clone lost route version");}),
    ("InvalidRouteCannotReplaceNode",()=>WithStore(store=>{Require(store.Save(Make(0),out var error),error);Require(!store.Save(Make(2),out _),"bad route replaced node");Require(store.Inspect().IsValid&&store.Inspect().Data.CasualRouteVersion==0,"previous node lost");}))
};
int failed=0;foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS RouteSave."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL RouteSave."+name+": "+e.Message);}
Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
