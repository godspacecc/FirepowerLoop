using Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>仅验证商店移除后的本项目旧局恢复与节点持久化。</summary>
internal static class ShopRemovalChecks
{
    public static int Run()
    {
        var tests = new (string Name, Action Run)[] {
            ("OldQuotesAreDiscardedWithoutLosingProgress", () => {
                var raw = JObject.FromObject(Make());
                raw.Remove("Checkpoint");
                raw["Shop"] = JObject.Parse("{'ArenaNumber':1,'GenerationSeed':42,'ChallengeId':'challenge.test','ChallengeAttempted':true,'Offers':[{'OfferId':'old','Price':999}]}");
                var data = raw.ToObject<ArcadeRunSaveData>();
                var saved = JObject.FromObject(data);
                Assert(saved["Shop"] == null && saved["Checkpoint"] != null, "交易节点仍被写入");
                Assert((bool)saved["Checkpoint"]["ChallengeAttempted"], "挑战恢复标记丢失");
                Assert(data.Score == 1234 && data.RemainingLives == 2, "进度丢失");
                Assert(ArcadeRunFileStore.TryValidate(data, out var error), error);
            }),
            ("NewCheckpointHasNoOffers", () => {
                var raw = JObject.FromObject(Make());
                Assert(raw["Checkpoint"] is JObject && raw["Checkpoint"]["Offers"] == null, "新节点仍含商品");
            }),
            ("PurchaseApiIsAbsent", () => {
                var source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Client/Assets/Game/Scripts/System/Arcade/ArcadeRunSystem.cs"));
                Assert(!source.Contains("TryPurchase("), "单人购买接口仍存在");
            }),
            ("LegacyOwnedEffectsSurvive", () => {
                var data = Make(); data.PurchasedUpgradeStacks["arcade.upgrade.damage"] = 2;
                data.EquipmentBySlot["support"] = "arcade.support.drone";
                var restored = JsonConvert.DeserializeObject<ArcadeRunSaveData>(JsonConvert.SerializeObject(data));
                Assert(restored.PurchasedUpgradeStacks["arcade.upgrade.damage"] == 2 && restored.EquipmentBySlot["support"] == "arcade.support.drone", "旧局效果丢失");
            })
        };
        int failed = 0;
        foreach (var test in tests)
            try { test.Run(); Console.WriteLine("PASS ShopRemovalSave." + test.Name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL ShopRemovalSave." + test.Name + ": " + error.Message); }
        return failed == 0 ? 0 : 1;
    }
    private static ArcadeRunSaveData Make()
    {
        var data = new ArcadeRunSaveData { RunId = new string('b', 32), UpdatedAtUtc = DateTime.UtcNow.ToString("O"), RemainingLives = 2, Score = 1234 };
        data.EquipmentBySlot["weapon"] = ArcadeRunDefaults.BasicWeaponId;
        return data;
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
