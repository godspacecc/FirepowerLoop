using Game;
using Newtonsoft.Json.Linq;

if (args.Length == 3 && args[0] == "testClass=ShopRemovalSave" && args[1] == "maxTests=4" && args[2] == "includePassingTests=true") return ShopRemovalChecks.Run();
if (args.Length == 3 && args[0] == "testClass=ArenaEntrySave" && args[1] == "maxTests=6" && args[2] == "includePassingTests=true") return ArenaEntryChecks.Run();
if (args.Length != 3 || args[0] != "testClass=IslandRoundSave" || args[1] != "maxTests=12" || args[2] != "includePassingTests=true") throw new Exception("Explicit IslandRoundSave scope required");
var tests = new (string, Action)[] {
 ("RejectsInvalidRound", () => Reject(c => c["RoundNumber"] = 5)),
 ("RejectsMismatchedMap", () => Reject(c => c["MapId"] = "island.round.2")),
 ("RejectsNonfiniteTimer", () => Reject(c => c["FireCooldown"] = double.NaN)),
 ("RejectsNegativeGold", () => Reject(c => c["ArenaGold"] = -1)),
 ("RejectsInvalidHealth", () => Reject(c => c["PlayerHealth"] = 101)),
 ("RejectsInvalidProduct", () => Reject(c => c["Powerups"][0]["ProductId"] = "bad id")),
 ("RejectsDuplicatePowerups", () => Reject(c => ((JArray)c["Powerups"]).Add(c["Powerups"][0].DeepClone()))),
 ("RestrictsIslandRoute", () => { var d = Make(); d.ArenaConfigNumber = 2; Assert(!ArcadeRunFileStore.TryValidate(d,out _), "non-island accepted"); }),
 ("LegacyFileStillResumes", () => { var d = Make(); typeof(ArcadeRunSaveData).GetProperty("IslandRoundCheckpoint")?.SetValue(d,null); RoundTrip(d,false); }),
 ("RoundFourRetainsEffectsAndGold", () => { var d=Make(c => { c["RoundNumber"]=4; c["MapId"]="island.round.4"; }); RoundTrip(d,true); }),
 ("CloneOwnsEffectState", () => { var d=Make(); var clone=d.Clone(); var property=typeof(ArcadeRunSaveData).GetProperty("IslandRoundCheckpoint"); Assert(property!=null,"checkpoint contract missing"); dynamic checkpoint=property.GetValue(clone); checkpoint.Powerups[0].Charges=0; var original=JObject.FromObject(d); Assert((int)original["IslandRoundCheckpoint"]["Powerups"][0]["Charges"]==2,"shallow effect clone"); }),
 ("InvalidSaveDoesNotReplaceNode", () => { WithStore(s => { var d=Make(); Assert(s.Save(d,out var e),e); var bad=Make(c=>c["RoundNumber"]=0); Assert(!s.Save(bad,out _),"invalid save accepted"); Assert(s.Inspect().IsValid,"previous node lost"); }); })
};
var failures=0;
foreach(var (name,run) in tests) { try { run(); Console.WriteLine("PASS IslandRoundSave."+name); } catch(Exception e) { failures++; Console.WriteLine("FAIL IslandRoundSave."+name+": "+e.Message); } }
Console.WriteLine($"IslandRoundSave: {tests.Length-failures}/{tests.Length} passed");
return failures==0?0:1;

static ArcadeRunSaveData Make(Action<JObject> edit=null) {
 var d=new ArcadeRunSaveData { RunId=new string('a',32),UpdatedAtUtc=DateTime.UtcNow.ToString("O"),IsCasualRoute=true,ArenaConfigNumber=1,RunGold=7 };
 d.EquipmentBySlot["weapon"]="arcade.weapon.basic";
 var raw=JObject.FromObject(d);
 var checkpoint=JObject.Parse("{ 'RoundNumber':1,'MapId':'island.round.1','PlayerHealth':80,'PlayerMaxHealth':100,'Bombs':2,'Dashes':3,'ArenaGold':19,'TemporaryWeaponId':'arcade.weapon.spread','TemporaryWeaponRemaining':1.25,'FirstPersonRemaining':2.5,'FireCooldown':0.2,'InvulnerabilityRemaining':0.3,'DashRemaining':0.1,'DashDirectionX':1,'DashDirectionY':0,'Powerups':[{'ProductId':'arcade.powerup.shield','RemainingSeconds':1.5,'Charges':2,'CooldownSeconds':0.5,'PositionX':-2,'PositionY':3}] }");
 edit?.Invoke(checkpoint); raw["IslandRoundCheckpoint"]=checkpoint; return raw.ToObject<ArcadeRunSaveData>();
}
static void Reject(Action<JObject> edit) => Assert(!ArcadeRunFileStore.TryValidate(Make(edit),out _),"malformed checkpoint accepted");
static void Assert(bool value,string message) { if(!value)throw new Exception(message); }
static void WithStore(Action<ArcadeRunFileStore> action) { var root=Path.Combine(AppContext.BaseDirectory,"FirepowerLoop-RoundSaveChecks-"+Guid.NewGuid().ToString("N")); try { action(new ArcadeRunFileStore(root)); } finally { if(Directory.Exists(root)) Directory.Delete(root,true); } }
static void RoundTrip(ArcadeRunSaveData d,bool checkpointExpected) => WithStore(s=> { Assert(s.Save(d,out var error),error); var read=s.Inspect(); Assert(read.IsValid,read.Error); Assert(read.Data.RunGold==7,"arena gold double-counted"); var checkpoint=JObject.FromObject(read.Data)["IslandRoundCheckpoint"]; if(checkpointExpected) { Assert(checkpoint is JObject,"checkpoint lost"); Assert(checkpoint["FirstPersonRemaining"]==null,"retired camera timer persisted"); Assert((int)checkpoint["RoundNumber"]==4,"round lost"); Assert((float)checkpoint["Powerups"][0]["RemainingSeconds"]==1.5f,"effect refreshed/lost"); Assert((long)checkpoint["ArenaGold"]==19,"uncommitted arena gold lost"); } else Assert(checkpoint==null||checkpoint.Type==JTokenType.Null,"legacy node invented round"); });
