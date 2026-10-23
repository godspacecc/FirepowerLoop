using System.Reflection;
using ET;
using ET.Server;
using MemoryPack;
/// <summary>从真实服务器牛群伤害入口核对致死、保护和快照复制，不用表现反推命中。</summary>
internal static class HerdContactOnlineChecks
{
    public static int Run(int maximum)
    {
        var cases = new (string, Action)[] { ("LethalAndLivingContacts", Contacts), ("ProtectionDoesNotPublishContact", Protected), ("ContactSnapshotCloneAndCodec", Codec) };
        if (maximum != 3) throw new ArgumentException("HerdContactOnline maxTests=3 required");
        int failed = 0;
        foreach (var (name, test) in cases) try { test(); Console.WriteLine("PASS HerdContactOnline." + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL HerdContactOnline." + name + ": " + (e.InnerException ?? e).Message); }
        Console.WriteLine($"{3-failed}/3 passed"); return failed == 0 ? 0 : 1;
    }
    private static OnlineArcadeRunActor Create() => (OnlineArcadeRunActor)typeof(HerdOnlineChecks).GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
    private static void Tick(OnlineArcadeRunActor a, float dt) => typeof(HerdOnlineChecks).GetMethod("Tick", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { a, dt });
    private static object Field(object value, string name)
    { var f = value.GetType().GetField(name); var p=value.GetType().GetProperty(name); if (f == null&&p==null) throw new Exception("missing authoritative contact field: " + name); return f!=null?f.GetValue(value)!:p!.GetValue(value)!; }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Contacts()
    {
        var a = Create(); Tick(a, 8); a.Battle.Players[1].CurrentHealth = 1; Tick(a, 2);
        foreach (var p in a.Battle.Players.Values)
        {
            Require((long)Field(p, "HerdContactSequence") == 1, "each real health loss needs one contact, including death");
            Require((float)Field(p, "HerdContactX") == -26 && (float)Field(p, "HerdContactZ") == -14, "contact location must precede death/respawn mutation");
        }
        Tick(a, .01f); Require(a.Battle.Players.Values.All(p => (long)Field(p, "HerdContactSequence") == 1), "same cow repeated contact");
    }
    private static void Protected()
    {
        var a = Create(); Tick(a, 8); foreach (var p in a.Battle.Players.Values) p.InvulnerabilitySeconds = 100;
        Tick(a, 2); Require(a.Battle.Players.Values.All(p => (long)Field(p, "HerdContactSequence") == 0), "protection invented damaging contact");
    }
    private static void Codec()
    {
        var a = Create(); Tick(a, 8); Tick(a, 2);
        var methods = typeof(OnlineArcadeRunActorSystem);
        object Call(string name, object value) => methods.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { value })!;
        using var snapshot = (OnlineArcadeBattleInfo)Call("BuildBattleSnapshot", a.Battle);
        using var clone = (OnlineArcadeBattleInfo)Call("CloneBattleSnapshot", snapshot);
        var player = snapshot.Players[0]; Require((long)Field(player, "HerdContactSequence") == 1, "snapshot lost source");
        player.GetType().GetProperty("HerdContactSequence")!.SetValue(player, 99L);
        Require((long)Field(clone.Players[0], "HerdContactSequence") == 1, "clone shares mutable event data");
        using var decoded = MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(clone))!;
        Require((long)Field(decoded.Players[0], "HerdContactSequence") == 1 && (float)Field(decoded.Players[0], "HerdContactX") == -26, "codec lost contact identity or location");
    }
}
