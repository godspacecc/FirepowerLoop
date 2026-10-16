using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>通用轮前存档的真实文件与兼容验证，所有文件使用隔离临时目录。</summary>
    public sealed class RoundCheckpointTests
    {
        [Test] public void RetainsRoundFourAndCompletePlayerState() => WithStore(store =>
        {
            var data = Make(); var raw = JObject.FromObject(data);
            Require(raw["RoundCheckpoint"] is JObject, "通用轮级合同缺失");
            Require(store.Save(data, out var error), error);
            var saved = JObject.FromObject(store.Inspect().Data)["RoundCheckpoint"];
            Require((int)saved["RoundNumber"] == 4 && (long)saved["PlayerState"]["ArenaGold"] == 19, "第四轮或本场金币丢失");
            Require((float)saved["PlayerState"]["Powerups"][0]["RemainingSeconds"] == 1.5f, "强化剩余量丢失");
            Require((int)saved["RemainingCrateIds"][0] == 7 && (long)saved["EncounterSeed"] == -37, "主题状态丢失");
        });
        [Test] public void RejectsLayoutIdentity() { Reject(c => c["MapId"] = "arena.3.round.4"); Reject(c => c["LayoutVersion"] = 0); }
        [Test] public void RejectsWrongArenaOrConfig() { Reject(c => c["ArenaNumber"] = 3); Reject(c => c["ArenaConfigNumber"] = 21); }
        [Test] public void RejectsRoundAndPlayerIdentity() { Reject(c => c["RoundNumber"] = 5); Reject(c => c["PlayerState"]["RoundNumber"] = 3); Reject(c => c["PlayerState"]["MapId"] = "arena.entry"); }
        [Test] public void RejectsInvalidPlayerTimers() { Reject(c => c["PlayerState"]["FireCooldown"] = double.NaN); Reject(c => c["PlayerState"]["TemporaryWeaponRemaining"] = -1); }
        [Test] public void RejectsHealthAndInventory() { Reject(c => c["PlayerState"]["PlayerHealth"] = 101); Reject(c => c["PlayerState"]["Bombs"] = -1); }
        [Test] public void RejectsInvalidCrateIds() { Reject(c => c["RemainingCrateIds"] = null); Reject(c => c["RemainingCrateIds"] = new JArray(7,7)); Reject(c => c["RemainingCrateIds"] = new JArray(0)); }
        [Test] public void RejectsMissingPlayerState() => Reject(c => c["PlayerState"] = null);
        [Test] public void RejectsSimultaneousLegacyAndEntryNodes()
        {
            var raw = JObject.FromObject(Make()); raw["ArenaEntryState"] = raw["RoundCheckpoint"]?["PlayerState"]?.DeepClone();
            Require(!ArcadeRunFileStore.TryValidate(raw.ToObject<ArcadeRunSaveData>(), out _), "重复入场节点被接受");
            raw = JObject.FromObject(Make()); raw["IslandRoundCheckpoint"] = raw["RoundCheckpoint"]?["PlayerState"]?.DeepClone();
            Require(!ArcadeRunFileStore.TryValidate(raw.ToObject<ArcadeRunSaveData>(), out _), "重复海岛节点被接受");
        }
        [Test] public void RejectsResultCheckpoint() { var data = Make(); data.Outcome = "abandoned"; Require(!ArcadeRunFileStore.TryValidate(data,out _), "结果保留轮节点"); }
        [Test] public void CloneOwnsPlayerEffectsAndCrates()
        {
            var data = Make(); var property = typeof(ArcadeRunSaveData).GetProperty("RoundCheckpoint"); Require(property != null,"通用轮级合同缺失");
            dynamic clone = property.GetValue(data.Clone()); clone.PlayerState.Powerups[0].Charges = 0; clone.RemainingCrateIds[0] = 9;
            var original = JObject.FromObject(data)["RoundCheckpoint"]; Require((int)original["PlayerState"]["Powerups"][0]["Charges"] == 2 && (int)original["RemainingCrateIds"][0] == 7,"浅拷贝污染原节点");
        }
        [Test] public void LegacyVersionOneWithoutRoundStillLoads() => WithStore(store =>
        {
            var raw = JObject.FromObject(Make()); raw.Remove("RoundCheckpoint");
            Require(store.Save(raw.ToObject<ArcadeRunSaveData>(),out var error),error); var read = store.Inspect(); Require(read.IsValid,read.Error);
            Require(JObject.FromObject(read.Data)["RoundCheckpoint"]?.Type != JTokenType.Object,"旧档被猜测补轮");
        });
        [Test] public void InvalidSavePreservesPreviousFile() => WithStore(store =>
        {
            Require(store.Save(Make(),out var error),error); var before = File.ReadAllText(store.RunPath);
            Require(!store.Save(Make(c => c["LayoutVersion"] = -1),out _),"无效节点被写入"); Require(File.ReadAllText(store.RunPath) == before,"原文件被覆盖");
        });
        [Test] public void LockedDestinationPreservesPreviousFile() => WithStore(store =>
        {
            Require(store.Save(Make(),out var error),error); var before = File.ReadAllText(store.RunPath);
            using (var locked = new FileStream(store.RunPath,FileMode.Open,FileAccess.Read,FileShare.None)) Require(!store.Save(Make(),out _),"锁定文件仍保存成功");
            Require(File.ReadAllText(store.RunPath) == before,"写失败改变原文件");
        });
        [Test] public void RetainsCrateHealthAndCloneOwnsDictionary() => WithStore(store =>
        {
            var data=Make(c=>c["CrateHealth"]=new JObject{{"7",3}});
            Require(JObject.FromObject(data)["RoundCheckpoint"]["CrateHealth"] is JObject,"箱体耐久合同缺失");
            Require(store.Save(data,out var error),error);var read=store.Inspect();Require(read.IsValid,read.Error);
            Require((int)JObject.FromObject(read.Data)["RoundCheckpoint"]["CrateHealth"]["7"]==3,"轮前箱体耐久丢失");
            dynamic clone=typeof(ArcadeRunSaveData).GetProperty("RoundCheckpoint").GetValue(data.Clone());clone.CrateHealth[7]=99;
            Require((int)JObject.FromObject(data)["RoundCheckpoint"]["CrateHealth"]["7"]==3,"箱体耐久浅拷贝");
        });
        [Test] public void RejectsCrateHealthOutsideRemainingIds() => Reject(c=>c["CrateHealth"]=new JObject{{"9",3}});
        [Test] public void RejectsNonpositiveOrNullCrateHealth()
        {
            Reject(c=>c["CrateHealth"]=new JObject{{"7",0}});Reject(c=>c["CrateHealth"]=new JObject{{"7",-1}});Reject(c=>c["CrateHealth"]=null);
        }
        private static ArcadeRunSaveData Make(Action<JObject> edit = null)
        {
            var data = new ArcadeRunSaveData { RunId = new string('a',32), UpdatedAtUtc = DateTime.UtcNow.ToString("O"), IsCasualRoute = true, ArenaConfigNumber = 2, CompletedArenaCount = 1, NextArenaNumber = 2, Checkpoint = new ArcadeCheckpointSaveData { ArenaNumber = 2 } };
            data.EquipmentBySlot["weapon"] = "arcade.weapon.basic";
            var checkpoint = JObject.Parse("{'ArenaNumber':2,'ArenaConfigNumber':2,'RoundNumber':4,'LayoutVersion':1,'MapId':'arena.2.round.4','RemainingCrateIds':[7,8],'CratesInitialized':true,'TreasureCompleted':true,'EncounterSeed':-37,'PlayerState':{'RoundNumber':4,'MapId':'arena.2.round.4','PlayerHealth':80,'PlayerMaxHealth':100,'Bombs':2,'Dashes':3,'ArenaGold':19,'Powerups':[{'ProductId':'arcade.powerup.shield','RemainingSeconds':1.5,'Charges':2}]}}");
            edit?.Invoke(checkpoint); var raw = JObject.FromObject(data); raw["RoundCheckpoint"] = checkpoint; return raw.ToObject<ArcadeRunSaveData>();
        }
        private static void Reject(Action<JObject> edit) => Require(!ArcadeRunFileStore.TryValidate(Make(edit),out _),"无效通用轮级节点被接受");
        private static void Require(bool value,string error) { if (!value) throw new InvalidOperationException(error); }
        private static void WithStore(Action<ArcadeRunFileStore> action)
        {
            #if UNITY_EDITOR
            var parent = Path.GetTempPath();
#else
            var parent = AppContext.BaseDirectory;
#endif
            var root = Path.Combine(parent,"FirepowerLoopGeneralRoundChecks",Guid.NewGuid().ToString("N"));
            try { action(new ArcadeRunFileStore(root)); } finally { if (Directory.Exists(root)) Directory.Delete(root,true); }
        }
    }
}
