using Game;
using Newtonsoft.Json.Linq;

internal static class ArenaEntryChecks
{
    public static int Run()
    {
        var cases = new (string,Action)[] {
            ("RetainsEntryStateOnDisk",()=> WithStore(store=> { var d=Make(); Check(store.Save(d,out var error),error); var raw=JObject.FromObject(store.Inspect().Data); Check(raw["ArenaEntryState"] is JObject,"entry state lost"); Check((float)raw["ArenaEntryState"]["Powerups"][0]["RemainingSeconds"]==1.5f,"effect state changed"); Check((long)raw["ArenaEntryState"]["ArenaGold"]==0,"old arena gold carried"); })),
            ("RejectsEntryGoldAndTimers",()=> { Check(!ArcadeRunFileStore.TryValidate(Make(c=>c["ArenaGold"]=1),out _),"old gold accepted"); Check(!ArcadeRunFileStore.TryValidate(Make(c=>c["FireCooldown"]=double.NaN),out _),"nonfinite timer accepted"); }),
            ("RejectsWrongEntryIdentity",()=> { Check(!ArcadeRunFileStore.TryValidate(Make(c=>c["MapId"]="island.round.1"),out _),"wrong map accepted"); Check(!ArcadeRunFileStore.TryValidate(Make(c=>c["RoundNumber"]=2),out _),"wrong round accepted"); }),
            ("RejectsFirstArenaAndResult",()=> { var d=Make(); d.CompletedArenaCount=0;d.NextArenaNumber=1;d.Checkpoint.ArenaNumber=1;Check(!ArcadeRunFileStore.TryValidate(d,out _),"first arena entry accepted"); d=Make();d.Outcome="abandoned";Check(!ArcadeRunFileStore.TryValidate(d,out _),"result entry accepted"); }),
            ("RejectsSimultaneousRoundNode",()=> { var raw=JObject.FromObject(Make()); var island=(JObject)raw["ArenaEntryState"].DeepClone(); island["MapId"]="island.round.1"; raw["IslandRoundCheckpoint"]=island;Check(!ArcadeRunFileStore.TryValidate(raw.ToObject<ArcadeRunSaveData>(),out _),"simultaneous node accepted"); }),
            ("CloneOwnsEntryEffects",()=> { var d=Make();var property=typeof(ArcadeRunSaveData).GetProperty("ArenaEntryState");Check(property!=null,"entry contract missing"); dynamic state=property.GetValue(d.Clone());state.Powerups[0].Charges=0;Check((int)JObject.FromObject(d)["ArenaEntryState"]["Powerups"][0]["Charges"]==2,"shallow clone"); })
        };
        int failures=0;
        foreach(var (name,test) in cases) { try {test();Console.WriteLine("PASS ArenaEntrySave."+name);}catch(Exception error){failures++;Console.WriteLine("FAIL ArenaEntrySave."+name+": "+error.Message);} }
        Console.WriteLine($"ArenaEntrySave: {cases.Length-failures}/{cases.Length} passed");return failures==0?0:1;
    }
    private static ArcadeRunSaveData Make(Action<JObject> change=null)
    {
        var d=new ArcadeRunSaveData {RunId=new string('a',32),IsCasualRoute=true,ArenaConfigNumber=2,CompletedArenaCount=1,NextArenaNumber=2,Checkpoint=new ArcadeCheckpointSaveData{ArenaNumber=2},UpdatedAtUtc=DateTime.UtcNow.ToString("O")};
        d.EquipmentBySlot["weapon"]="arcade.weapon.basic";
        var raw=JObject.FromObject(d);var state=JObject.Parse("{'RoundNumber':1,'MapId':'arena.entry','PlayerHealth':80,'PlayerMaxHealth':100,'Powerups':[{'ProductId':'arcade.powerup.shield','RemainingSeconds':1.5,'Charges':2}]} ");change?.Invoke(state);raw["ArenaEntryState"]=state;return raw.ToObject<ArcadeRunSaveData>();
    }
    private static void Check(bool value,string error){if(!value)throw new Exception(error);}
    private static void WithStore(Action<ArcadeRunFileStore> action){var root=Path.Combine(AppContext.BaseDirectory,"EntryChecks-"+Guid.NewGuid().ToString("N"));try{action(new ArcadeRunFileStore(root));}finally{if(Directory.Exists(root))Directory.Delete(root,true);}}
}
