using System;
using System.Reflection;
using Luban;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;

namespace Game.Tests
{
    public sealed class CivilisationSettlementTests
    {
        private const string TechnologySection = "another-day.civilisation-technology";
        private const string JourneySection = "another-day.civilisation-expedition";

        private static Type Required(string name)
        {
            var type = typeof(Game).Assembly.GetType("Game." + name);
            Assert.That(type, Is.Not.Null, name + " 尚未实现");
            return type;
        }

        private static IRuntimeConfigProvider Config() => new LubanRuntimeConfigProvider(() => new Tables(name =>
            new ByteBuf(AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>($"Assets/Game/Config/{name}.bytes").bytes)));

        private static BaseSaveData Base(long gold = 10) => new BaseSaveData
        {
            Sections = new System.Collections.Generic.Dictionary<string, JToken>
            {
                [CurrencyModel.SaveSectionId] = new JObject { ["schemaVersion"] = 1, ["gold"] = gold },
                [TechnologySection] = new JObject { ["schemaVersion"] = 1, ["unlocked"] = new JArray() }
            }
        };

        private static ExpeditionSaveData Journey(int item = 29, int amount = 2) => new ExpeditionSaveData
        {
            ExpeditionId = "0123456789abcdef0123456789abcdef", MapId = "map.another-day.civilisation-valley",
            Sections = new System.Collections.Generic.Dictionary<string, JToken>
            {
                ["another-day.player-bag"] = new JObject
                {
                    ["capacity"] = InventoryDefaults.PlayerBagCapacity, ["riskState"] = "InExpedition",
                    ["slots"] = new JArray(new JObject { ["index"] = 0, ["itemId"] = item, ["amount"] = amount }),
                    ["quickSlots"] = new JArray(29, 0, 0, 0)
                },
                [JourneySection] = new JObject
                {
                    ["schemaVersion"] = 1, ["technologies"] = new JArray("technology.dry-ration"),
                    ["visited"] = new JArray("camp.fire"), ["preparationClaimed"] = false,
                    ["freeFoodRemaining"] = 0
                }
            }
        };

        private static bool Apply(BaseSaveData basis, ExpeditionSaveData journey, bool defeated, out string error)
        {
            var args = new object[] { basis, journey, defeated, Config(), null };
            var success = (bool)Required("CivilisationSettlementPolicy").GetMethod("TryApply", BindingFlags.Public | BindingFlags.Static).Invoke(null, args);
            error = args[4] as string;
            return success;
        }

        [Test] public void SuccessfulReturnConvertsItemsUnlocksTechnologyAndStoresReceipt()
        {
            var basis = Base();
            Assert.That(Apply(basis, Journey(), false, out var error), Is.True, error);
            Assert.That(basis.Sections[CurrencyModel.SaveSectionId]["gold"].Value<long>(), Is.EqualTo(14));
            Assert.That(basis.Sections["another-day.player-bag"]["slots"], Is.Empty);
            Assert.That(basis.Sections["another-day.player-bag"]["quickSlots"], Is.Null,
                "结算新保存不得重新带回旧快捷槽字段");
            Assert.That(basis.Sections[TechnologySection]["unlocked"].Values<string>(), Does.Contain("technology.dry-ration"));
            Assert.That(basis.Sections[TechnologySection]["receipt"]["gold"].Value<long>(), Is.EqualTo(4));
        }

        [Test] public void PocketItemsParticipateInSuccessfulReturnAndAreCleared()
        {
            var journey = Journey();
            journey.Sections["another-day.player-bag"]["pocketSlots"] =
                new JArray(new JObject { ["index"] = 3, ["itemId"] = 29, ["amount"] = 2 });
            var basis = Base();
            Assert.That(Apply(basis, journey, false, out var error), Is.True, error);
            Assert.That(basis.Sections[CurrencyModel.SaveSectionId]["gold"].Value<long>(), Is.EqualTo(18));
            Assert.That(basis.Sections["another-day.player-bag"]["pocketSlots"], Is.Empty);
        }

        [Test] public void InvalidPocketSlotsRejectSettlementWithoutChangingBase()
        {
            var journey = Journey();
            journey.Sections["another-day.player-bag"]["pocketSlots"] =
                new JArray(new JObject { ["index"] = 4, ["itemId"] = 29, ["amount"] = 2 });
            var basis = Base();
            var before = JObject.FromObject(basis.Sections);
            Assert.That(Apply(basis, journey, false, out _), Is.False);
            Assert.That(JToken.DeepEquals(before, JObject.FromObject(basis.Sections)), Is.True);
        }

        [Test] public void FailedJourneyDoesNotGrantGoldOrKnowledge()
        {
            var basis = Base();
            Assert.That(Apply(basis, Journey(), true, out var error), Is.True, error);
            Assert.That(basis.Sections[CurrencyModel.SaveSectionId]["gold"].Value<long>(), Is.EqualTo(10));
            Assert.That(basis.Sections[TechnologySection]["unlocked"], Is.Empty);
        }

        [Test] public void OverflowLeavesAllBaseSectionsUnchanged()
        {
            var basis = Base(long.MaxValue);
            var before = JObject.FromObject(basis.Sections);
            Assert.That(Apply(basis, Journey(), false, out _), Is.False);
            Assert.That(JToken.DeepEquals(before, JObject.FromObject(basis.Sections)), Is.True);
        }

        [Test] public void UnknownReturnItemFailsWithoutPartialSettlement()
        {
            var basis = Base();
            var before = JObject.FromObject(basis.Sections);
            Assert.That(Apply(basis, Journey(99999), false, out _), Is.False);
            Assert.That(JToken.DeepEquals(before, JObject.FromObject(basis.Sections)), Is.True);
        }

        [Test] public void FreePreparationFoodCannotEarnMoney()
        {
            var journey = Journey();
            journey.Sections[JourneySection]["freeFoodRemaining"] = 1;
            journey.Sections[JourneySection]["preparationClaimed"] = true;
            var basis = Base();
            Assert.That(Apply(basis, journey, false, out var error), Is.True, error);
            Assert.That(basis.Sections[CurrencyModel.SaveSectionId]["gold"].Value<long>(), Is.EqualTo(12));
        }

        [Test] public void OldSaveMissingTechnologySectionMigratesButWrongVersionFails()
        {
            var model = (IScopedSaveSectionProvider)Activator.CreateInstance(Required("CivilisationTechnologyModel"));
            Assert.That(model.Validate(null, out var error), Is.True, error);
            model.Restore(null);
            Assert.That(model.Capture()["unlocked"], Is.Empty);
            Assert.That(model.Validate(new JObject { ["schemaVersion"] = 9, ["unlocked"] = new JArray() }, out _), Is.False);
        }
    }
}
