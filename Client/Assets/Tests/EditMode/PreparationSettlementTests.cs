using System;
using System.Linq;
using Luban;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;

namespace Game.Tests
{
    public sealed class PreparationSettlementTests
    {
        private const string Id="0123456789abcdef0123456789abcdef";
        private IRuntimeConfigProvider Config()=>new LubanRuntimeConfigProvider(()=>new global::Tables(name=>new ByteBuf(AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>($"Assets/Game/Config/{name}.bytes").bytes)));
        private BaseSaveData Basis()
        {
            var data=new BaseSaveData{Version=4};
            data.Sections[CurrencyModel.SaveSectionId]=new JObject{["schemaVersion"]=1,["gold"]=100};
            data.Sections["another-day.player-bag"]=new JObject{["capacity"]=12,["slots"]=new JArray(),["pocketSlots"]=new JArray(),["riskState"]="BaseSafe"};
            Assert.That(BasePreparationPolicy.Initialize(data,Config(),false,out var error),Is.True,error);return data;
        }
        private ExpeditionSaveData Journey(JObject item=null)
        {
            var data=new ExpeditionSaveData{ExpeditionId=Id,MapId=MapIds.CivilisationValley,ElapsedGameMinutes=1440,Status=ExpeditionStatus.ReturnQualified};
            data.Sections[ExpeditionPreparationModel.Section]=new JObject{["schemaVersion"]=1,["expeditionId"]=Id,["baseAnchor"]=28800000,["elapsedAnchor"]=0};
            data.Sections["another-day.player-bag"]=new JObject{["capacity"]=12,["slots"]=item==null?new JArray():new JArray(item),["pocketSlots"]=new JArray(),["riskState"]="InExpedition"};return data;
        }
        private JObject Item(int id=30,int amount=1,int acquired=1)=>new JObject{["index"]=0,["itemId"]=id,["amount"]=amount,["loadedAmmo"]=0,["originExpeditionId"]=Id,["acquiredAmount"]=acquired,["loadedAcquiredAmount"]=0};
        private bool Open(BaseSaveData data,int id)=>((JArray)data.Sections[ShopUnlockModel.Section]["items"]).Values<int>().Contains(id);
        [Test] public void SuccessfulReturnCreditsDayAndUnlocksStillCarriedLocalFood()
        {var data=Basis();Assert.That(BasePreparationPolicy.Settle(data,Journey(Item()),false,Config(),out var error),Is.True,error);Assert.That(data.Sections[CurrencyModel.SaveSectionId]["gold"].Value<long>(),Is.EqualTo(260));Assert.That(Open(data,30),Is.True);}
        [Test] public void DeathCreditsElapsedDayWithoutOpeningLocalGoods()
        {var data=Basis();Assert.That(BasePreparationPolicy.Settle(data,Journey(Item()),true,Config(),out var error),Is.True,error);Assert.That(data.Sections[CurrencyModel.SaveSectionId]["gold"].Value<long>(),Is.EqualTo(260));Assert.That(Open(data,30),Is.False);}
        [Test] public void BaseSourcedGoodsDoNotUnlockEvenWithSameItemId()
        {var data=Basis();Assert.That(BasePreparationPolicy.Settle(data,Journey(Item(acquired:0)),false,Config(),out var error),Is.True,error);Assert.That(Open(data,30),Is.False);}
        [Test] public void InvalidLedgerDoesNotMutateMoneyOrCalendar()
        {var data=Basis();var before=JObject.FromObject(data);var journey=Journey(Item());journey.Sections[ExpeditionPreparationModel.Section]["expeditionId"]="wrong";Assert.That(BasePreparationPolicy.Settle(data,journey,false,Config(),out _),Is.False);Assert.That(JToken.DeepEquals(before,JObject.FromObject(data)),Is.True);}
        [Test] public void MissingNewVersionSectionsCannotTriggerAnotherMigrationGrant()
        {var data=new BaseSaveData();data.Sections[CurrencyModel.SaveSectionId]=new JObject{["schemaVersion"]=1,["gold"]=100};Assert.That(BasePreparationPolicy.Initialize(data,Config(),false,out _),Is.False);Assert.That(data.Sections[CurrencyModel.SaveSectionId]["gold"].Value<long>(),Is.EqualTo(100));}
        [Test] public void LegacyMigrationCreditsOnceAndInheritsOwnedOrdinaryGoods()
        {var data=new BaseSaveData{Version=4};data.Sections[CurrencyModel.SaveSectionId]=new JObject{["schemaVersion"]=1,["gold"]=100};data.Sections["another-day.warehouse"]=new JObject{["slots"]=new JArray(Item(34,1,0))};Assert.That(BasePreparationPolicy.Initialize(data,Config(),false,out _),Is.True);Assert.That(BasePreparationPolicy.Initialize(data,Config(),false,out _),Is.True);Assert.That(data.Sections[CurrencyModel.SaveSectionId]["gold"].Value<long>(),Is.EqualTo(180));Assert.That(Open(data,34),Is.True);}
        [Test] public void LoadedLocalAmmoUnlocksAmmoWithoutUnlockingBaseWeapon()
        {var data=Basis();var item=Item(19,1,0);item["loadedAmmo"]=1;item["loadedAcquiredAmount"]=1;Assert.That(BasePreparationPolicy.Settle(data,Journey(item),false,Config(),out var error),Is.True,error);Assert.That(Open(data,26),Is.True);Assert.That(Open(data,19),Is.False);}
        [Test] public void ImpossibleOriginQuantityRejectsWholeSettlement()
        {var data=Basis();var before=JObject.FromObject(data);Assert.That(BasePreparationPolicy.Settle(data,Journey(Item(30,1,2)),false,Config(),out _),Is.False);Assert.That(JToken.DeepEquals(before,JObject.FromObject(data)),Is.True);}
        [Test] public void AnotherExpeditionOriginCannotBeImported()
        {var data=Basis();var item=Item();item["originExpeditionId"]="fedcba9876543210fedcba9876543210";Assert.That(BasePreparationPolicy.Settle(data,Journey(item),false,Config(),out _),Is.False);}
        [Test] public void LegacyStringMoneyCannotBeHealedByMigration()
        {var data=new BaseSaveData{Version=4};data.Sections[CurrencyModel.SaveSectionId]=new JObject{["schemaVersion"]=1,["gold"]="100"};var before=JObject.FromObject(data);Assert.That(BasePreparationPolicy.Initialize(data,Config(),false,out _),Is.False);Assert.That(JToken.DeepEquals(before,JObject.FromObject(data)),Is.True);}
        [Test] public void StringOriginCounterCannotBeCoercedIntoReward()
        {var data=Basis();var item=Item();item["acquiredAmount"]="1";Assert.That(BasePreparationPolicy.Settle(data,Journey(item),false,Config(),out _),Is.False);}
        [Test] public void StringLedgerSchemaCannotBeCoercedIntoTimeCredit()
        {var data=Basis();var journey=Journey();journey.Sections[ExpeditionPreparationModel.Section]["schemaVersion"]="1";Assert.That(BasePreparationPolicy.Settle(data,journey,false,Config(),out _),Is.False);}
    }
}
