using System;
using System.IO;
using System.Reflection;
using Luban;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEditor;

namespace Game.Tests
{
    public sealed class CivilisationProductionTests
    {
        private string root;
        private object production;
        [SetUp] public void SetUp()
        {
            TestReset.ResetArchitecture();
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            root = Path.Combine(Path.GetTempPath(), "AnotherDayCivilisationTests", Guid.NewGuid().ToString("N"));
            var architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(architecture, true);
            architecture.RegisterModel(new SaveModel()); architecture.RegisterModel(new AppStateModel());
            architecture.RegisterModel(new MapSceneModel()); architecture.RegisterModel(new PlayerBagModel());
            architecture.RegisterModel(new WarehouseModel()); architecture.RegisterModel(new CurrencyModel());
            architecture.RegisterModel(new ExpeditionModel());
            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterUtility(new LocalizationUtility());
            var technologyType = typeof(Game).Assembly.GetType("Game.CivilisationTechnologyModel");
            var journeyType = typeof(Game).Assembly.GetType("Game.CivilisationExpeditionModel");
            // 通过泛型反射注册待实现类型，首次运行保持可编译并明确报告行为缺失。
            foreach (var type in new[] { technologyType, journeyType })
            {
                Assert.That(type, Is.Not.Null);
                typeof(Architecture<Game>).GetMethod("RegisterModel").MakeGenericMethod(type).Invoke(architecture, new[] { Activator.CreateInstance(type) });
            }
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>($"Assets/Game/Config/{name}.bytes").bytes))));
            architecture.RegisterSystem(new SaveSystem()); architecture.RegisterSystem(new InventorySystem());
            var productionType = typeof(Game).Assembly.GetType("Game.CivilisationProductionSystem");
            Assert.That(productionType, Is.Not.Null, "基地生产系统尚未实现");
            production = Activator.CreateInstance(productionType);
            typeof(Architecture<Game>).GetMethod("RegisterSystem").MakeGenericMethod(productionType).Invoke(architecture, new[] { production });
            var save = architecture.GetSystem<SaveSystem>();
            save.ConfigureForTests(root);
            Assert.That(save.RegisterSection(architecture.GetModel<CurrencyModel>(), out var registrationError), Is.True, registrationError);
            architecture.GetModel<CurrencyModel>().Restore(new JObject { ["schemaVersion"] = 1, ["gold"] = 8 });
            architecture.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            architecture.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            // 增量 Flush 只追加到已经存在的存档；测试也必须经过正式首次创建入口。
            var initial = new BaseSaveData();
            Assert.That(save.CaptureBaseSections(initial, out var initialError), Is.True, initialError);
            Assert.That(save.SaveBase(initial, out initialError), Is.True, initialError);
            architecture.GetModel<SaveModel>().SetBase(initial, true);
        }
        [TearDown] public void TearDown()
        {
            TestReset.ResetArchitecture();
            ResourceLoader.ResetForTests();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        private bool Produce(out string error)
        {
            var args = new object[] { "production.dry-ration", 1, null };
            var result = (bool)production.GetType().GetMethod("TryProduce").Invoke(production, args);
            error = args[2] as string; return result;
        }
        private static void Unlock()
        {
            var method = typeof(Architecture<Game>).GetMethod("GetModel").MakeGenericMethod(typeof(Game).Assembly.GetType("Game.CivilisationTechnologyModel"));
            var model = (ISaveSectionProvider)method.Invoke(Game.Interface, null);
            model.Restore(new JObject { ["schemaVersion"] = 1, ["unlocked"] = new JArray("technology.dry-ration") });
        }
        [Test] public void LockedTechnologyCannotProduceOrSpendGold()
        {
            Assert.That(Produce(out _), Is.False);
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(8));
        }
        [Test] public void UnlockedProductionPersistsOutputAndGoldTogether()
        {
            Unlock(); Assert.That(Produce(out var error), Is.True, error);
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.Zero);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(30), Is.EqualTo(1));
            Assert.That(Game.Interface.GetSystem<SaveSystem>().InspectBase().Data.Sections[CurrencyModel.SaveSectionId]["gold"].Value<long>(), Is.Zero);
        }
        [Test] public void FullBagDoesNotConsumeGold()
        {
            Unlock(); var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            bag.TryAdd(5, bag.Capacity, 1, out _, out _);
            Assert.That(Produce(out _), Is.False);
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(8));
        }
        [Test] public void SaveFailureRollsBackBothGoldAndOutput()
        {
            // 回滚通过正式装备系统重验库存引用，不用缺失依赖的架构模拟磁盘失败。
            var architecture = (Game)Game.Interface;
            architecture.RegisterModel(new WeaponEquipmentModel());
            architecture.RegisterSystem(new WeaponEquipmentSystem());
            Unlock(); Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "blocked"), "block");
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(Path.Combine(root, "blocked", "child"));
            Assert.That(Produce(out _), Is.False);
            Assert.That(Game.Interface.GetModel<CurrencyModel>().Gold, Is.EqualTo(8));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(30), Is.Zero);
        }
    }
}
