using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class BasePreparationTransactionTests
    {
        private string root;
        private SaveSystem Saves=>Game.Interface.GetSystem<SaveSystem>();
        private CurrencyModel Coins=>Game.Interface.GetModel<CurrencyModel>();
        private BasePreparationModel Clock=>Game.Interface.GetModel<BasePreparationModel>();
        [SetUp] public void Setup()
        {
            TestReset.ResetArchitecture();root=Path.Combine(Path.GetTempPath(),"AnotherDayPreparation",Guid.NewGuid().ToString("N"));
            ResourceLoader.SetLoaderForTests(new PreparationTextLoader());LocalizationLanguages.ResetForTests();
            var game=new Game();typeof(Architecture<Game>).GetField("mArchitecture",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,game);typeof(Architecture<Game>).GetField("mInited",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(game,true);
            game.RegisterModel(new LocalizationModel());game.RegisterUtility(new LocalizationUtility(loadConfiguredTables:false));
            game.RegisterModel(new SaveModel());game.RegisterModel(new AppStateModel());game.RegisterModel(new MapSceneModel());game.RegisterModel(new UnitModel());game.RegisterModel(new PlayerModel());
            game.RegisterModel(new PlayerBagModel());game.RegisterModel(new WarehouseModel());game.RegisterModel(new CurrencyModel());game.RegisterModel(new BasePreparationModel());game.RegisterModel(new ShopUnlockModel());game.RegisterModel(new ExpeditionPreparationModel());game.RegisterModel(new ExpeditionModel());game.RegisterModel(new PlayerConditionModel());game.RegisterModel(new CombatModel());game.RegisterModel(new PlayerAttributeModel());game.RegisterModel(new WeaponEquipmentModel());game.RegisterModel(new ClothingEquipmentModel());
            var config=new LubanRuntimeConfigProvider(()=>new global::Tables(name=>new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/Game/Config/{name}.bytes").bytes)));game.RegisterUtility<IRuntimeConfigProvider>(config);
            game.RegisterSystem(new SaveSystem());game.RegisterSystem(new UnitSystem());game.RegisterSystem(new InventorySystem());game.RegisterSystem(new WeaponEquipmentSystem());game.RegisterSystem(new ClothingEquipmentSystem());game.RegisterSystem(new CombatSystem());game.RegisterSystem(new PlayerConditionSystem());game.RegisterSystem(new ShopSystem());game.RegisterSystem(new BasePreparationSystem());game.RegisterSystem(new CarryLoadSystem());
            Saves.ConfigureForTests(root);game.GetModel<CombatModel>().ApplyPlayerMaxHP(100);game.GetModel<CombatModel>().SetPlayerHP(100);
            Assert.That(Saves.CreateBase(out var data,out var error),Is.True,error);Assert.That(Saves.CommitBaseRuntime(data,out error),Is.True,error);
            game.GetModel<AppStateModel>().CurrentState.Value=AppState.Playing;game.GetModel<MapSceneModel>().SetLoaded(MapIds.Base,null);
        }
        [TearDown] public void Cleanup()
        {
            TestReset.ResetArchitecture();var path=Path.GetFullPath(root);var allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"AnotherDayPreparation"))+Path.DirectorySeparatorChar;
            if(path.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(path))Directory.Delete(path,true);
        }
        [Test] public void NewBaseHasExactlyOneInitialGrantAndSavedCalendar()
        {Assert.That(Coins.Gold,Is.EqualTo(80));Assert.That(Clock.TotalMilliseconds,Is.EqualTo(28800000));Assert.That(Saves.CommitBaseRuntime(Saves.InspectBase().Data,out var error),Is.True,error);Assert.That(Coins.Gold,Is.EqualTo(80));}
        [Test] public void RestCommitsMoneyDateAndAllSurvivalValuesTogether()
        {
            var condition=Game.Interface.GetModel<PlayerConditionModel>();condition.SetPower(10);condition.SetWater(20);condition.SetTili(5);condition.SetTemperature(33);Game.Interface.GetModel<CombatModel>().SetPlayerHP(25);
            Assert.That(Game.Interface.GetSystem<BasePreparationSystem>().TryRest(out var error),Is.True,error);Assert.That(Coins.Gold,Is.EqualTo(160));Assert.That(Clock.TotalMilliseconds,Is.EqualTo(115200000));Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value,Is.EqualTo(100));Assert.That(condition.Power,Is.EqualTo(condition.MaxPower));Assert.That(condition.Water,Is.EqualTo(condition.MaxWater));Assert.That(condition.Tili,Is.EqualTo(condition.MaxTili));Assert.That(condition.Temperature,Is.EqualTo(37));
            Assert.That(Saves.InspectBase().Data.Sections[CurrencyModel.SaveSectionId]["gold"].Value<long>(),Is.EqualTo(160));
        }
        [Test] public void LockedJournalMakesRestRollBackDateMoneyAndHealth()
        {
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(25);var condition=Game.Interface.GetModel<PlayerConditionModel>();condition.SetTili(5);condition.SetSprintReleaseRequired(true);
            var store=new SaveFileStore(root);var manifest=JObject.Parse(File.ReadAllText(store.BasePath));var journal=Path.Combine(Path.GetDirectoryName(store.BasePath),manifest["Journal"].Value<string>());
            using(var locked=new FileStream(journal,FileMode.Open,FileAccess.Read,FileShare.None))Assert.That(Game.Interface.GetSystem<BasePreparationSystem>().TryRest(out _),Is.False);
            Assert.That(Coins.Gold,Is.EqualTo(80));Assert.That(Clock.TotalMilliseconds,Is.EqualTo(28800000));Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value,Is.EqualTo(25));Assert.That(condition.Tili,Is.EqualTo(5));Assert.That(condition.SprintReleaseRequired,Is.True);
        }
        [Test] public void RepeatedCompletedRestAdvancesToDistinctDays()
        {var system=Game.Interface.GetSystem<BasePreparationSystem>();Assert.That(system.TryRest(out _),Is.True);Assert.That(system.TryRest(out _),Is.True);Assert.That(Coins.Gold,Is.EqualTo(240));Assert.That(Clock.LastGrantedDay,Is.EqualTo(3));}
        [Test] public void CarryIncludesPocketClothingAndLoadedAmmoWithoutHeldDuplicate()
        {
            var bag=Game.Interface.GetModel<PlayerBagModel>();bag.Container.TryAdd(19,1,1,out _,out _);bag.Container.TryGet(0,out var weapon);weapon.LoadedAmmo=3;
            bag.Pocket.TryAdd(8,2,5,out _,out _);Game.Interface.GetModel<ClothingEquipmentModel>().Container.TryAdd(38,1,1,out _,out _);
            typeof(WeaponEquipmentModel).GetMethod("SetHeld",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Game.Interface.GetModel<WeaponEquipmentModel>(),new object[]{new InventorySlotRef(InventoryContainerKind.PlayerBag,0),19,weapon});
            var load=Game.Interface.GetSystem<CarryLoadSystem>().Query(MapIds.CivilisationValley);Assert.That(load.Error,Is.Null);Assert.That(load.Grams,Is.EqualTo(2860));Assert.That(load.CanDepart,Is.False);Assert.That(load.Forbidden.Count,Is.EqualTo(2));
        }
        [Test] public void WarehouseDoesNotContributeAndOverweightIsNotPickupRejection()
        {Game.Interface.GetModel<WarehouseModel>().Container.TryAdd(31,20,20,out _,out _);Assert.That(Game.Interface.GetSystem<CarryLoadSystem>().Query(MapIds.CivilisationValley).Grams,Is.Zero);Assert.That(Game.Interface.GetSystem<InventorySystem>().TryPreviewAddPlayerBagItem(31,20,out var accepted,out var error),Is.True,error);Assert.That(accepted,Is.EqualTo(20));var load=Game.Interface.GetSystem<CarryLoadSystem>().Query(MapIds.CivilisationValley);Assert.That(load.Grams,Is.EqualTo(30000));Assert.That(load.CanDepart,Is.False);Assert.That(load.Speed,Is.EqualTo(.8).Within(.0001));Assert.That(load.Sprint,Is.EqualTo(1.5).Within(.0001));}
        [Test] public void DirectPurchaseCannotBypassExplorationUnlock()
        {
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(1001,1,UnitType.Npc,UnitSource.MapPlaced,"npc.base.merchant",Vector3.zero,Quaternion.identity,UnitAuthorityMode.Local,0,true),out var error),Is.True,error);
            var shop=Game.Interface.GetSystem<ShopSystem>();Assert.That(shop.TryOpen("npc.base.merchant",1001,"shop.base.supplies",out var snapshot,out error),Is.True,error);Assert.That(snapshot.Items.Select(x=>x.ItemId),Is.EquivalentTo(new[]{3,4,7,8}));Assert.That(shop.TryBuy(5,1).Failure,Is.EqualTo(ShopFailureReason.NotForSale));Assert.That(Coins.Gold,Is.EqualTo(80));
        }
        [Test] public void ResumeRestoresBaseAuthoritiesAndKeepsOnlyFutureTimeIncrement()
        {
            var journey=SavedJourney(600);Assert.That(Saves.SaveExpedition(journey,out var error),Is.True,error);
            Clock.Reset();Coins.Restore(new JObject{["schemaVersion"]=1,["gold"]=0});Game.Interface.GetModel<ShopUnlockModel>().Reset();
            Assert.That(Saves.CommitExpeditionRuntime(Saves.InspectExpedition().Data,out error),Is.True,error);
            Assert.That(Coins.Gold,Is.EqualTo(80));Assert.That(Clock.TotalMilliseconds,Is.EqualTo(28800000));
            var ledger=Game.Interface.GetModel<ExpeditionPreparationModel>();Assert.That(ledger.Project(660),Is.EqualTo(68400000));
        }
        [Test] public void DuplicateSettlementAndDeathCaptureDoNotLoseOrRepeatDailyIncome()
        {
            var basis=Saves.InspectBase().Data;var journey=SavedJourney(1440);
            Assert.That(Saves.ApplyExpeditionSettlement(basis,journey,true,out var duplicate,out var error),Is.True,error);Assert.That(duplicate,Is.False);
            Assert.That(basis.Sections[CurrencyModel.SaveSectionId]["gold"].Value<long>(),Is.EqualTo(160));
            Assert.That(Saves.ApplyExpeditionSettlement(basis,journey,true,out duplicate,out error),Is.True,error);Assert.That(duplicate,Is.True);
            Game.Interface.GetModel<SaveModel>().SetExpedition(journey);
            Assert.That(Saves.CaptureBaseSections(basis,out error),Is.True,error);Assert.That(basis.Sections[CurrencyModel.SaveSectionId]["gold"].Value<long>(),Is.EqualTo(160));
            Assert.That(Coins.Gold,Is.EqualTo(80),"远征运行态旧金币不能覆盖基地结算候选");
        }
        [Test] public void StandaloneSnapshotOwnsValidLedgerWithoutChangingActiveJourney()
        {
            const string id="abcdef0123456789abcdef0123456789";
            var data=Saves.CaptureExpedition(id,MapIds.SurvivalSample,DateTime.UtcNow,out var error);
            Assert.That(data,Is.Not.Null,error);Assert.That(data.Sections[ExpeditionPreparationModel.Section]["expeditionId"].Value<string>(),Is.EqualTo(id));
            Assert.That(data.Sections[ExpeditionPreparationModel.Section]["baseAnchor"].Value<long>(),Is.EqualTo(Clock.TotalMilliseconds));
            Assert.That(Game.Interface.GetModel<ExpeditionPreparationModel>().ExpeditionId,Is.Null);
        }
        private ExpeditionSaveData SavedJourney(float elapsed)
        {
            const string id="0123456789abcdef0123456789abcdef";
            var data=new ExpeditionSaveData{ExpeditionId=id,MapId=MapIds.SurvivalSample,ElapsedGameMinutes=elapsed};
            foreach(var section in Saves.InspectBase().Data.Sections)
                if(section.Key!=CurrencyModel.SaveSectionId&&section.Key!=BasePreparationModel.Section&&section.Key!=ShopUnlockModel.Section&&section.Key!="another-day.warehouse")data.Sections[section.Key]=section.Value?.DeepClone();
            data.Sections[ExpeditionPreparationModel.Section]=new JObject{["schemaVersion"]=1,["expeditionId"]=id,["baseAnchor"]=28800000L,["elapsedAnchor"]=0};
            data.Sections[ExpeditionTaskModel.SaveSectionId]=new JObject{["schemaVersion"]=1,["mapId"]=data.MapId,["expeditionId"]=id,["tasks"]=new JArray()};
            return data;
        }
        private sealed class PreparationTextLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/"+path);
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> callback) where T : UnityEngine.Object {callback?.Invoke(LoadAsset<T>(path));return ResourceLoadHandle.Empty;}
            public void Release(ResourceLoadHandle handle){}
        }
    }
}
