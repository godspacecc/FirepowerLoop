using System.Reflection;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class HeldItemMigrationTests
    {
        private IRuntimeConfigProvider Config => (IRuntimeConfigProvider)typeof(ItemInventoryFoundationTests).GetMethod("CreateRealConfigProvider",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
        private JObject Sections(int count=0)
        {
            var slots=new JArray();for(int i=0;i<count;i++)slots.Add(new JObject{["index"]=i,["itemId"]=ItemIds.ShortSword,["amount"]=1});
            return new JObject { ["another-day.player-bag"]=new JObject{["capacity"]=12,["riskState"]="BaseSafe",["slots"]=slots,["pocketSlots"]=new JArray()},[WeaponEquipmentModel.Section]=new JObject{["meleeWeaponItemId"]=ItemIds.ShortSword,["rangedWeaponItemId"]=ItemIds.Bow,["activeSlot"]="Ranged",["rangedLoadedAmmo"]=0} };
        }
        private JObject Migrate(JObject sections) {Assert.That(HeldItemSaveMigration.TryMigrate(sections,Config,out var next,out _,out var error),Is.True,error);return next;}
        [Test] public void TwoWeaponsPreserveCountsAndActiveSource(){var next=Migrate(Sections());Assert.That(next["another-day.player-bag"]["slots"].Count(),Is.EqualTo(2));Assert.That(next[WeaponEquipmentModel.Section]["itemId"].Value<int>(),Is.EqualTo(ItemIds.Bow));Assert.That(next[WeaponEquipmentModel.Section]["sourceSlotIndex"].Value<int>(),Is.EqualTo(1));}
        [Test] public void FullBagExpandsByMinimum(){var next=Migrate(Sections(12));Assert.That(next["another-day.player-bag"]["capacity"].Value<int>(),Is.EqualTo(14));Assert.That(next["another-day.player-bag"]["slots"].Count(),Is.EqualTo(14));}
        [Test] public void EmptySlotsAvoidExpansion(){var next=Migrate(Sections(10));Assert.That(next["another-day.player-bag"]["capacity"].Value<int>(),Is.EqualTo(12));}
        [Test] public void RepeatedMigrationAddsNothing(){var next=Migrate(Sections(12));Assert.That(JToken.DeepEquals(next,Migrate(next)),Is.True);}
        [Test] public void InputCandidateRemainsUnchanged(){var old=Sections(12);var before=old.DeepClone();Migrate(old);Assert.That(JToken.DeepEquals(old,before),Is.True);}
        [Test] public void CorruptLegacyStateRejects(){var old=Sections();old[WeaponEquipmentModel.Section]["meleeWeaponItemId"]=ItemIds.Arrow;Assert.That(HeldItemSaveMigration.TryMigrate(old,Config,out _,out _,out _),Is.False);}
        [Test] public void LoadedAmmoReturnsWithoutLoss(){var old=Sections(12);old[WeaponEquipmentModel.Section]["rangedLoadedAmmo"]=1;var next=Migrate(old);Assert.That(next["another-day.player-bag"]["slots"].ToString(),Does.Contain("\"itemId\": "+ItemIds.Arrow));}
        [Test] public void NewHeldReferenceMustBelongToInventory(){var next=Migrate(Sections());next[WeaponEquipmentModel.Section]["sourceSlotIndex"]=11;Assert.That(HeldItemSaveMigration.TryMigrate(next,Config,out _,out _,out _),Is.False);}
        [Test] public void LegacyNullWeaponIdsRejectWithoutChangingCandidate()
        {
            var old = Sections();
            old[WeaponEquipmentModel.Section]["meleeWeaponItemId"] = JValue.CreateNull();
            old[WeaponEquipmentModel.Section]["rangedWeaponItemId"] = JValue.CreateNull();
            old[WeaponEquipmentModel.Section]["activeSlot"] = "None";
            var before = old.DeepClone();
            Assert.That(HeldItemSaveMigration.TryMigrate(old, Config, out _, out _, out _), Is.False);
            Assert.That(JToken.DeepEquals(old, before), Is.True);
        }
    }
}
