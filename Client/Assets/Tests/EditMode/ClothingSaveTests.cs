using Newtonsoft.Json.Linq;
using System.Linq;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class ClothingSaveTests
    {
        PocketInventoryTests environment;
        ClothingEquipmentModel Model=>Game.Interface.GetModel<ClothingEquipmentModel>();
        [SetUp] public void SetUp(){environment=new PocketInventoryTests();environment.SetUp();}
        [TearDown] public void TearDown()=>environment.TearDown();
        static JObject Data(params JObject[] slots)=>new JObject{["schemaVersion"]=1,["slots"]=new JArray(slots)};
        static JObject Item(int slot,int id)=>new JObject{["index"]=slot,["itemId"]=id,["amount"]=1,["loadedAmmo"]=0};
        [Test] public void MissingLegacySectionRestoresEmptySlots(){Model.Restore(null);Assert.That(Model.GetItemId(ClothingSlot.Torso),Is.Zero);Assert.That(Model.GetItemId(ClothingSlot.Legs),Is.Zero);}
        [Test] public void RoundTripRestoresIndependentClothes(){Model.Restore(Data(Item(0,38),Item(1,40)));var data=Model.Capture();Model.Reset();Model.Restore(data);Assert.That(Model.GetItemId(ClothingSlot.Torso),Is.EqualTo(38));Assert.That(Model.GetItemId(ClothingSlot.Legs),Is.EqualTo(40));}
        [Test] public void SevenPartRoundTripAndLegacyClothesKeepStableSlotIdentity()
        {
            int[] ids={38,40,42,43,44,45,46};
            Model.Restore(Data(ids.Select((id,index)=>Item(index,id)).ToArray()));
            var saved=Model.Capture();Model.Reset();Model.Restore(saved);
            for(int i=0;i<ids.Length;i++)Assert.That(Model.GetItemId((ClothingSlot)(i+1)),Is.EqualTo(ids[i]));
            Model.Restore(Data(Item(0,39),Item(1,41)));
            Assert.That(Model.GetItemId(ClothingSlot.Torso),Is.EqualTo(39));
            Assert.That(Model.GetItemId(ClothingSlot.Legs),Is.EqualTo(41));
            for(int i=3;i<=7;i++)Assert.That(Model.GetItemId((ClothingSlot)i),Is.Zero);
        }
        [Test] public void WrongNewSlotAndOutOfRangeSlotAreRejectedWithoutMutatingModel()
        {
            Model.Restore(Data(Item(2,42)));
            Assert.That(Model.Validate(Data(Item(3,42)),out _),Is.False);
            Assert.That(Model.Validate(Data(Item(7,42)),out _),Is.False);
            Assert.That(Model.GetItemId(ClothingSlot.Head),Is.EqualTo(42));
        }
        [Test] public void DuplicateSlotsAreRejected(){Assert.That(Model.Validate(Data(Item(0,38),Item(0,39)),out _),Is.False);}
        [Test] public void WrongSlotCannotSilentlyLoseSavedItem(){Model.Restore(Data(Item(0,38)));Assert.That(Model.Validate(Data(Item(1,39)),out _),Is.False);Assert.That(Model.GetItemId(ClothingSlot.Torso),Is.EqualTo(38));}
        [Test] public void UnknownSchemaIsRejected(){var data=Data();data["schemaVersion"]=2;Assert.That(Model.Validate(data,out _),Is.False);}
        [Test] public void UnknownItemIsRejected(){Assert.That(Model.Validate(Data(Item(0,999999)),out _),Is.False);}
        [Test] public void RuntimeRollbackRestoresClothingAndInventoryTogether()
        {
            Model.Restore(Data(Item(0,38),Item(1,40)));
            var bag=Game.Interface.GetModel<PlayerBagModel>().Container;bag.TryAdd(39,1,1,out _,out _);
            var save=Game.Interface.GetSystem<SaveSystem>();var before=save.CaptureRuntimeSnapshot();
            Game.Interface.GetSystem<ClothingEquipmentSystem>().ClearForRiskLoss();Game.Interface.GetSystem<InventorySystem>().ClearPlayerBag();
            Assert.That(Model.GetItemId(ClothingSlot.Torso),Is.Zero);
            Assert.That(save.RestoreRuntimeSnapshot(before,out var error),Is.True,error);
            Assert.That(Model.GetItemId(ClothingSlot.Torso),Is.EqualTo(38));Assert.That(Model.GetItemId(ClothingSlot.Legs),Is.EqualTo(40));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(39),Is.EqualTo(1));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void SettlementKeepsExtractedClothesAndClearsDefeatedClothes(bool defeated)
        {
            var save=Game.Interface.GetSystem<SaveSystem>();Assert.That(save.TryCreateBaseCandidate(out var camp,out var error),Is.True,error);
            var trip=new ExpeditionSaveData{ExpeditionId=System.Guid.NewGuid().ToString("N"),MapId=MapIds.SurvivalSample,Status=defeated?ExpeditionStatus.Defeated:ExpeditionStatus.ReturnQualified};
            trip.Sections[ClothingEquipmentModel.Section]=Data(Item(0,38),Item(1,40));
            Assert.That(save.ApplyExpeditionSettlement(camp,trip,false,out _,out error),Is.True,error);
            Assert.That(camp.Sections[ClothingEquipmentModel.Section]["slots"].Count(),Is.EqualTo(defeated?0:2));
        }
    }
}
