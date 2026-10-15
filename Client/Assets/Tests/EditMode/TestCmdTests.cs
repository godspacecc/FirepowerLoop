using System;
using System.Threading;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ET;
using Game.Network;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class TestCmdTests
    {
        private string tempRoot;
        private TestConfigProvider configs;
        private RoutingNetwork network;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            ResourceLoader.SetLoaderForTests(new LocalizationTestLoader());
            LocalizationLanguages.ResetForTests();
            configs = new TestConfigProvider();
            network = new RoutingNetwork();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(configs);
                architecture.RegisterUtility<INetworkRuntime>(network);
            };

            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayTestCmdTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot);
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            EnterBase();
        }

        [TearDown]
        public void TearDown()
        {
            Architecture<Game>.OnRegisterPatch = _ => { };
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            TestReset.ResetArchitecture();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void AppearanceCommandsValidateAndPublishOnlySessionChanges()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var model = Game.Interface.GetModel<TestCmdModel>();
            var inventory = Game.Interface.GetModel<ClothingEquipmentModel>();
            var before = Enumerable.Range(1, 7).Select(i => inventory.GetItemId((global::ClothingSlot)i)).ToArray();
            Assert.That(system.ExecuteAsync("#appearance list", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            foreach (var text in new[] { "#appearance", "#appearance 10", "#appearance -1", "#appearance 1 extra", "#appearance unknown" })
                Assert.That(system.ExecuteAsync(text, CancellationToken.None).GetAwaiter().GetResult().Success, Is.False, text);
            var property = typeof(TestCmdModel).GetProperty("PlayerAppearanceIndex");
            Assert.That(property, Is.Not.Null);
            Assert.That(system.ExecuteAsync("#appearance 8", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(property.GetValue(model), Is.EqualTo(8));
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null, MapPreparationMode.OnlinePlayersOnly);
            Assert.That(system.ExecuteAsync("#appearance 3", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.StateRejected));
            Assert.That(property.GetValue(model), Is.EqualTo(8));
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetModel<MapSceneModel>().SetStatus(MapLoadStatus.Loading);
            Assert.That(system.ExecuteAsync("#appearance reset", CancellationToken.None).GetAwaiter().GetResult().Success, Is.False);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Assert.That(system.ExecuteAsync("#appearance reset", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(property.GetValue(model), Is.EqualTo(0));
            Assert.That(Enumerable.Range(1, 7).Select(i => inventory.GetItemId((global::ClothingSlot)i)), Is.EqualTo(before));
            Assert.That(IsSaveDirty(), Is.False);
            // 同一指令通过 PlayerCtrl 事件入口驱动正式角色，而非只改变预览或模型值。
            var player=UnityEngine.Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            var controller=new PlayerCtrl(Game.Interface);
            var view=player.GetComponent<PlayerView>();
            var viewController=new PlayerViewCtrl(view,PlayerModel.LocalPlayerUnitId,1,new ViewCtrlRegistry(),new UnityViewInstanceProvider());
            typeof(PlayerCtrl).GetField("activePlayer",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(controller,viewController);
            try
            {
                var animator=view.Animator;
                var weapon=view.GetComponent<CharacterWeaponPresenter>();
                Assert.That(system.ExecuteAsync("#appearance 8",CancellationToken.None).GetAwaiter().GetResult().Success,Is.True);
                Assert.That(player.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.name=="Character_Male_Jacket"),Is.True);
                Assert.That(view.Animator,Is.SameAs(animator));Assert.That(view.GetComponent<CharacterWeaponPresenter>(),Is.SameAs(weapon));
                Assert.That(system.ExecuteAsync("#appearance reset",CancellationToken.None).GetAwaiter().GetResult().Success,Is.True);
                Assert.That(view.Animator.transform.Find("ClothingParts").gameObject.activeSelf,Is.True);
            }
            finally { controller.ReleaseAll(); if(player!=null)UnityEngine.Object.DestroyImmediate(player); }
        }

        [Test]
        public void TerrainInstancingProbeChangesOnlyCurrentMapAndRestoresOriginalSettings()
        {
            var root = new GameObject("terrain-probe-map");
            var outside = new GameObject("outside-map-terrain");
            var data = new TerrainData { heightmapResolution = 33 };
            var outsideData = new TerrainData { heightmapResolution = 33 };
            try
            {
                var context = root.AddComponent<MapContext>();
                typeof(MapContext).GetField("mapContentRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(context, root);
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, new DiagnosticMapHandle(context));
                var terrain = root.AddComponent<Terrain>();
                terrain.terrainData = data;
                terrain.drawInstanced = true;
                var other = outside.AddComponent<Terrain>();
                other.terrainData = outsideData;
                other.drawInstanced = true;
                var system = Game.Interface.GetSystem<TestCmdSystem>();
                var guarded = system.ExecuteAsync("#terrain instancing off", CancellationToken.None).GetAwaiter().GetResult();
                Assert.That(guarded.Code, Is.EqualTo(TestCmdResultCode.StateRejected));
                Assert.That(terrain.drawInstanced, Is.True);
                Assert.That(other.drawInstanced, Is.True);
                UnityEngine.Object.DestroyImmediate(outside);
                var disabled = system.ExecuteAsync("#terrain instancing off", CancellationToken.None).GetAwaiter().GetResult();
                Assert.That(disabled.Success, Is.True, disabled.Message);
                Assert.That(terrain.drawInstanced, Is.False);
                Assert.That(system.ExecuteAsync("#terrain instancing off", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
                Assert.That(system.ExecuteAsync("#terrain instancing restore", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
                Assert.That(system.ExecuteAsync("#terrain instancing off", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
                Game.Interface.SendEvent(new MapLoadingStartedEvent(MapIds.SurvivalSample));
                Assert.That(terrain.drawInstanced, Is.True, "切图开始时应恢复对照开关");
                Assert.That(terrain.drawInstanced, Is.True, "重复关闭不能覆盖原始状态");
                Assert.That(system.ExecuteAsync("#terrain instancing restore", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(outside);
                UnityEngine.Object.DestroyImmediate(data);
                UnityEngine.Object.DestroyImmediate(outsideData);
            }
        }

        [Test]
        public void TerrainCommandReportsOnlyCurrentMapWithoutChangingItsState()
        {
            var root = new GameObject("current-map-diagnostic");
            try
            {
                var context = root.AddComponent<MapContext>();
                typeof(MapContext).GetField("mapContentRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(context, root);
                var handle = new DiagnosticMapHandle(context);
                var map = Game.Interface.GetModel<MapSceneModel>();
                map.SetLoaded(MapIds.Base, handle);
                var result = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#terrain", CancellationToken.None).GetAwaiter().GetResult();
                Assert.That(result.Success, Is.True);
                Assert.That(result.Message, Does.Contain("[TerrainDiagnostic]"));
                Assert.That(result.Message, Does.Contain("TerrainCount=0"));
                Assert.That(map.CurrentHandle, Is.SameAs(handle));
                Assert.That(map.Status.Value, Is.EqualTo(MapLoadStatus.Loaded));
                Assert.That(root.activeSelf, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void GeneratedCatalogIncludesTerrainDiagnosticCommand()
        {
            var table = new global::TbTestCmdEntry(new Luban.ByteBuf(File.ReadAllBytes("Assets/Game/Config/tbtestcmdentry.bytes")));
            Assert.That(table.GetOrDefault("diagnostics.terrain")?.CommandTemplate, Is.EqualTo("#terrain"));
        }

        private sealed class DiagnosticMapHandle : IMapContentHandle
        {
            public DiagnosticMapHandle(MapContext context) { Context = context; }
            public string MapId => MapIds.Base;
            public MapContext Context { get; }
            public bool IsValid => Context != null;
        }

        [Test]
        public void TerrainCommandRejectsExtraArgumentsAndUnavailableMap()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var invalid = system.ExecuteAsync("#terrain extra", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(invalid.Recognized, Is.True);
            Assert.That(invalid.Code, Is.EqualTo(TestCmdResultCode.UsageError));
            var unavailable = system.ExecuteAsync("#terrain", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(unavailable.Recognized, Is.True);
            Assert.That(unavailable.Success, Is.False);
            Assert.That(unavailable.Code, Is.EqualTo(TestCmdResultCode.StateRejected));
        }

        [Test]
        public void LogCommandRequestsRuntimeLogWindowAndRejectsExtraArguments()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();

            var result = system.ExecuteAsync("#log", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.Recognized, Is.True);
            Assert.That(result.Success, Is.True);
            Assert.That(result.Code, Is.EqualTo(TestCmdResultCode.RuntimeLogOpenRequested));

            var invalid = system.ExecuteAsync("#log extra", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(invalid.Recognized, Is.True);
            Assert.That(invalid.Success, Is.False);
            Assert.That(invalid.Code, Is.EqualTo(TestCmdResultCode.UsageError));
        }

        [Test]
        public void GalleryCommandRejectsOutsideOfflineBaseWithoutChangingGameState()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var map = Game.Interface.GetModel<MapSceneModel>();
            var save = Game.Interface.GetModel<SaveModel>();
            map.SetLoaded(MapIds.Arcade, null, MapPreparationMode.ArcadePlayerOnly);

            var result = system.ExecuteAsync("#gallery", CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Recognized, Is.True);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(TestCmdResultCode.StateRejected));
            Assert.That(result.Message, Does.Contain("离线基地"));
            Assert.That(map.CurrentMapId, Is.EqualTo(MapIds.Arcade));
            Assert.That(save.CurrentBase, Is.Not.Null);
        }

        [Test]
        public void ParseCommandsUsesExactCloneAndClearBagGrammar()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;

            var empty = system.ExecuteAsync("  ", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(empty, false, false, TestCmdResultCode.EmptyInput, 0, 0);
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands, Is.Empty);

            var unknown = system.ExecuteAsync("#give item 1", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(unknown, false, false, TestCmdResultCode.UnknownCommand, 0, 0);
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands, Is.EqualTo(new[] { "#give item 1" }));

            var cloneUsage = system.ExecuteAsync("# clone ", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(cloneUsage, true, false, TestCmdResultCode.UsageError, 0, 0);

            var clone = system.ExecuteAsync("#clone 3 5", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(clone, true, true, TestCmdResultCode.CloneComplete, 5, 0);
            Assert.That(clone.Message, Is.EqualTo("获得急救包x5。"));
            Assert.That(bag.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(5));

            var keyClone = system.ExecuteAsync("#clone item.metal_parts.name 1", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(keyClone, true, true, TestCmdResultCode.CloneComplete, 1, 0);
            Assert.That(bag.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(1));
            AssertFailurePreservesBag(system, "clone abc 1", TestCmdResultCode.ItemNotFound);
            AssertFailurePreservesBag(system, "clone 0 1", TestCmdResultCode.UsageError);
            AssertFailurePreservesBag(system, "clone -1 1", TestCmdResultCode.UsageError);
            AssertFailurePreservesBag(system, "clone 2147483648 1", TestCmdResultCode.UsageError);
            AssertFailurePreservesBag(system, "clone 0", TestCmdResultCode.UsageError);
            AssertFailurePreservesBag(system, "clone -1", TestCmdResultCode.UsageError);
            AssertFailurePreservesBag(system, "clone 2147483648", TestCmdResultCode.UsageError);
            AssertFailurePreservesBag(system, "clone 999", TestCmdResultCode.ItemNotFound);
            AssertFailurePreservesBag(system, "clone 3 1 extra", TestCmdResultCode.UsageError);
            AssertFailurePreservesBag(system, "clone abc nope", TestCmdResultCode.InvalidAmount);

            AssertFailurePreservesBag(system, "clone 999 1", TestCmdResultCode.ItemNotFound);
            AssertFailurePreservesBag(system, "clone 3 nope", TestCmdResultCode.InvalidAmount);
            AssertFailurePreservesBag(system, "clone 3 0", TestCmdResultCode.InvalidAmount);
            AssertFailurePreservesBag(system, "clone 3 -1", TestCmdResultCode.InvalidAmount);
            AssertFailurePreservesBag(system, "clone 3 2147483648", TestCmdResultCode.InvalidAmount);

            var clearUsage = system.ExecuteAsync("#clear warehouse", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(clearUsage, true, false, TestCmdResultCode.UsageError, 0, 0);

            var clear = system.ExecuteAsync("# CLEAR   BAG ", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(clear, true, true, TestCmdResultCode.ClearBagComplete, 6, 0);
            var currentBag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(currentBag.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(0));
        }

        [Test]
        public void UnitBarCommandIsSessionOnlyStrictAndIdempotent()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var model = Game.Interface.GetModel<TestCmdModel>();
            var eventCount = 0;
            var lastVisible = false;
            var unregister = Game.Interface.RegisterEvent<UnitHealthBarVisibilityChangedEvent>(evt =>
            {
                eventCount++;
                lastVisible = evt.Visible;
            });

            Assert.That(model.UnitHealthBarsVisible, Is.False);
            AssertResult(system.ExecuteAsync("#unitbar", CancellationToken.None).GetAwaiter().GetResult(), true, false, TestCmdResultCode.UsageError, 0, 0);
            Assert.That(model.UnitHealthBarsVisible, Is.False);
            Assert.That(eventCount, Is.EqualTo(0));

            AssertResult(system.ExecuteAsync("#UNITBAR on", CancellationToken.None).GetAwaiter().GetResult(), true, true, TestCmdResultCode.UnitHealthBarsChanged, 0, 0);
            Assert.That(model.UnitHealthBarsVisible, Is.True);
            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(lastVisible, Is.True);

            AssertResult(system.ExecuteAsync("#unitbar on", CancellationToken.None).GetAwaiter().GetResult(), true, true, TestCmdResultCode.UnitHealthBarsChanged, 0, 0);
            Assert.That(eventCount, Is.EqualTo(1));

            AssertResult(system.ExecuteAsync("#unitbar off", CancellationToken.None).GetAwaiter().GetResult(), true, true, TestCmdResultCode.UnitHealthBarsChanged, 0, 0);
            Assert.That(model.UnitHealthBarsVisible, Is.False);
            Assert.That(eventCount, Is.EqualTo(2));
            Assert.That(lastVisible, Is.False);

            AssertResult(system.ExecuteAsync("#unitbar true", CancellationToken.None).GetAwaiter().GetResult(), true, false, TestCmdResultCode.UsageError, 0, 0);
            Assert.That(model.UnitHealthBarsVisible, Is.False);
            Assert.That(eventCount, Is.EqualTo(2));
            unregister.UnRegister();
        }

        [Test]
        public void ArcadeCommandsUseStrictGrammarAndRejectWithoutLocalCombat()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();

            AssertResult(system.ExecuteAsync("#arcade", CancellationToken.None).GetAwaiter().GetResult(),
                true, false, TestCmdResultCode.UsageError, 0, 0);
            AssertResult(system.ExecuteAsync("#arcade damage 0", CancellationToken.None).GetAwaiter().GetResult(),
                true, false, TestCmdResultCode.UsageError, 0, 0);
            AssertResult(system.ExecuteAsync("#arcade bombs -1", CancellationToken.None).GetAwaiter().GetResult(),
                true, false, TestCmdResultCode.UsageError, 0, 0);
            AssertResult(system.ExecuteAsync("#arcade clear everything", CancellationToken.None).GetAwaiter().GetResult(),
                true, false, TestCmdResultCode.UsageError, 0, 0);

            var inactive = system.ExecuteAsync("#arcade status", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(inactive, true, false, TestCmdResultCode.StateRejected, 0, 0);
            Assert.That(inactive.Message, Does.Contain("本地单人街机战斗"));
        }

        [Test]
        public void ArcadeStatusGoldAndResourcesMutateOnlyTheActiveCombatRuntime()
        {
            var combat = Game.Interface.GetSystem<ArcadeCombatSystem>();
            var arena = new ArcadeArenaDefinition(3, new[] { "test.wave" },
                Array.Empty<ArcadeObstacleDefinition>(), false, string.Empty, 0, 1);
            var catalog = new ArcadeConfigCatalogDefinition(
                default,
                Array.Empty<ArcadeWeaponDefinition>(),
                new[] { arena },
                Array.Empty<ArcadeEnemyGroupDefinition>(),
                Array.Empty<ArcadeBossDefinition>(),
                Array.Empty<ArcadeChallengeDefinition>(),
                Array.Empty<ArcadeDropDefinition>(),
                Array.Empty<ArcadeShopItemDefinition>(),
                Array.Empty<ArcadeUpgradeDefinition>(),
                Array.Empty<ArcadeRewardDefinition>());
            typeof(ArcadeCombatSystem).GetField("catalog", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(combat, catalog);
            typeof(ArcadeCombatSystem).GetField("arena", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(combat, arena);
            var runtime = new ArcadeCombatRuntime
            {
                ArenaNumber = 3,
                WaveIndex = 0,
                PlayerHealth = 40,
                PlayerMaxHealth = 100,
                ArenaGold = 7,
                Bombs = 1,
                Dashes = 2
            };
            Game.Interface.GetModel<ArcadeCombatModel>().Begin(runtime);
            var system = Game.Interface.GetSystem<TestCmdSystem>();

            var status = system.ExecuteAsync("#arcade status", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(status, true, true, TestCmdResultCode.ArcadeCommandComplete, 0, 0);
            Assert.That(status.Message, Does.Contain("竞技场 3").And.Contain("生命 40/100"));
            AssertResult(system.ExecuteAsync("#arcade gold 25", CancellationToken.None).GetAwaiter().GetResult(),
                true, true, TestCmdResultCode.ArcadeCommandComplete, 0, 0);
            AssertResult(system.ExecuteAsync("#arcade bombs 4", CancellationToken.None).GetAwaiter().GetResult(),
                true, true, TestCmdResultCode.ArcadeCommandComplete, 0, 0);
            AssertResult(system.ExecuteAsync("#arcade dashes 6", CancellationToken.None).GetAwaiter().GetResult(),
                true, true, TestCmdResultCode.ArcadeCommandComplete, 0, 0);

            Assert.That(runtime.ArenaGold, Is.EqualTo(32));
            Assert.That(runtime.Bombs, Is.EqualTo(4));
            Assert.That(runtime.Dashes, Is.EqualTo(6));
        }

        [Test]
        public void HistoryKeepsSubmittedUniqueRecentCommandsOnly()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var model = Game.Interface.GetModel<TestToolSettingsModel>();

            system.ExecuteAsync("# ", CancellationToken.None).GetAwaiter().GetResult();
            system.ExecuteAsync("#unknown 1", CancellationToken.None).GetAwaiter().GetResult();
            system.ExecuteAsync("#clone 1 1", CancellationToken.None).GetAwaiter().GetResult();
            system.ExecuteAsync("#clear bag", CancellationToken.None).GetAwaiter().GetResult();
            system.ExecuteAsync("#clone 1 1", CancellationToken.None).GetAwaiter().GetResult();
            for (var i = 0; i < 35; i++)
            {
                system.ExecuteAsync($"#clone 2 {i + 1}", CancellationToken.None).GetAwaiter().GetResult();
            }
            system.ExecuteAsync("#clone 2 35", CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(model.RecentCommands.Count, Is.EqualTo(TestToolSettingsModel.MaxRecentCommandCount));
            Assert.That(model.RecentCommands[0], Is.EqualTo("#clone 2 35"));
            Assert.That(model.RecentCommands.Count(item => item == "#clone 2 35"), Is.EqualTo(1));
            Assert.That(model.RecentCommands, Does.Not.Contain("#clone 1 1"));
            Assert.That(model.RecentCommands, Does.Not.Contain("#unknown 1"));
            Assert.That(Game.Interface.GetModel<TestCmdModel>().HasLastResult, Is.True);
        }

        [Test]
        public void CloneUsesInventoryStackingAndPartialAcceptance()
        {
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 3, 5, out _, out var error), Is.True, error);
            for (var i = 0; i < 11; i++)
            {
                Assert.That(bag.TryAdd(ItemIds.Cloth, 5, 5, out _, out error), Is.True, error);
            }

            var beforeWarehouse = SnapshotText(Game.Interface.GetModel<WarehouseModel>().Container);
            var events = CountInventoryEvents();

            var result = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#clone 1 10", CancellationToken.None).GetAwaiter().GetResult();

            AssertResult(result, true, true, TestCmdResultCode.ClonePartial, 2, 8);
            Assert.That(bag.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(5));
            Assert.That(SnapshotText(Game.Interface.GetModel<WarehouseModel>().Container), Is.EqualTo(beforeWarehouse));
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(IsSaveDirty(), Is.True);
        }

        [Test]
        public void CloneFullOrInvalidInputLeavesInventoryUnchanged()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Cloth, 60, 5, out _, out var error), Is.True, error);
            var beforeBag = SnapshotText(bag);
            var beforeWarehouse = SnapshotText(Game.Interface.GetModel<WarehouseModel>().Container);
            var events = CountInventoryEvents();

            var full = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#clone 1 1", CancellationToken.None).GetAwaiter().GetResult();
            var invalid = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#clone 1 nope", CancellationToken.None).GetAwaiter().GetResult();

            AssertResult(full, true, false, TestCmdResultCode.BagFull, 0, 0);
            AssertResult(invalid, true, false, TestCmdResultCode.InvalidAmount, 0, 0);
            Assert.That(SnapshotText(bag), Is.EqualTo(beforeBag));
            Assert.That(SnapshotText(Game.Interface.GetModel<WarehouseModel>().Container), Is.EqualTo(beforeWarehouse));
            Assert.That(events.Count, Is.EqualTo(0));
            Assert.That(IsSaveDirty(), Is.False);
        }

        [Test]
        public void CloneNumericIdDefaultsToOneThroughInventory()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var beforeWarehouse = SnapshotText(Game.Interface.GetModel<WarehouseModel>().Container);
            var events = CountInventoryEvents();

            var expectedTotal = 0;
            foreach (var command in new[] { "#clone 3", "# ClOnE   3  ", "#clone 3 1" })
            {
                var result = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync(command, CancellationToken.None).GetAwaiter().GetResult();

                AssertResult(result, true, true, TestCmdResultCode.CloneComplete, 1, 0);
                Assert.That(result.AffectedItemId, Is.EqualTo(ItemIds.Medkit));
                Assert.That(bag.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(++expectedTotal));
                Assert.That(SnapshotText(Game.Interface.GetModel<WarehouseModel>().Container), Is.EqualTo(beforeWarehouse));
                Assert.That(events.Count, Is.EqualTo(expectedTotal));
                Assert.That(IsSaveDirty(), Is.True);
            }
        }

        [Test]
        public void CloneUniqueNameSearchAddsDefaultAmountThroughInventory()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var beforeBag = SnapshotText(bag);
            var events = CountInventoryEvents();

            var result = system.ExecuteAsync("#clone 金属", CancellationToken.None).GetAwaiter().GetResult();

            AssertResult(result, true, true, TestCmdResultCode.CloneComplete, 1, 0);
            Assert.That(result.Message, Is.EqualTo("获得金属零件x1。"));
            Assert.That(SnapshotText(bag), Is.Not.EqualTo(beforeBag));
            Assert.That(bag.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(1));
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(IsSaveDirty(), Is.True);
        }

        [Test]
        public void CameraCommandUsesStrictGrammarAndRequiresActivePlayer()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var missing = system.ExecuteAsync("#camera", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(missing, true, false, TestCmdResultCode.UsageError, 0, 0);
            var extra = system.ExecuteAsync("#camera topdown extra", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(extra, true, false, TestCmdResultCode.UsageError, 0, 0);
            var unknown = system.ExecuteAsync("#camera side", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(unknown, true, false, TestCmdResultCode.UsageError, 0, 0);
            var noPlayer = system.ExecuteAsync("#CaMeRa ShOuLdEr", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(noPlayer, true, false, TestCmdResultCode.StateRejected, 0, 0);
        }

        [Test]
        public void CameraCommandSwitchesModeCaseInsensitivelyAndIsIdempotent()
        {
            const long mapInstanceId = 91;
            configs.Player = new PlayerConfigDefinition(
                "player", string.Empty, 100, 10, 1f, 1f, 3f, 360f, 5f);
            var motor = new CameraCommandMotor(mapInstanceId);
            var camera = new CameraCommandCamera();
            Game.Interface.RegisterUtility<IPlayerRuntimePortProvider>(new CameraCommandPorts(motor, camera));
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(
                new UnitSnapshot(PlayerModel.LocalPlayerUnitId, mapInstanceId, UnitType.Player, UnitSource.Player,
                    "player", Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out var error), Is.True, error);
            Game.Interface.SendEvent(new PlayerReadyEvent(PlayerModel.LocalPlayerUnitId, mapInstanceId));

            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var shoulder = system.ExecuteAsync("#CaMeRa ShOuLdEr", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(shoulder, true, true, TestCmdResultCode.CameraModeChanged, 0, 0);
            var repeated = system.ExecuteAsync("#camera shoulder", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(repeated, true, true, TestCmdResultCode.CameraModeChanged, 0, 0);
            var topDown = system.ExecuteAsync("#camera topdown", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(topDown, true, true, TestCmdResultCode.CameraModeChanged, 0, 0);
            Assert.That(Game.Interface.GetModel<PlayerCameraModeModel>().Mode, Is.EqualTo(PlayerCameraMode.TopDown));
        }

        [Test]
        public void CloneResultCarriesAffectedItemIdOnlyForCommittedCompleteOrPartialMutation()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;

            var complete = system.ExecuteAsync("#clone 1 1", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(complete.Code, Is.EqualTo(TestCmdResultCode.CloneComplete));
            Assert.That(complete.AffectedAmount, Is.EqualTo(1));
            Assert.That(complete.AffectedItemId, Is.EqualTo(ItemIds.MetalParts));

            for (var i = 0; i < 11; i++)
            {
                Assert.That(bag.TryAdd(ItemIds.Cloth, 5, 5, out _, out var error), Is.True, error);
            }

            var partial = system.ExecuteAsync("#clone 1 10", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(partial.Code, Is.EqualTo(TestCmdResultCode.ClonePartial));
            Assert.That(partial.AffectedAmount, Is.EqualTo(4));
            Assert.That(partial.AffectedItemId, Is.EqualTo(ItemIds.MetalParts));

            var query = system.ExecuteAsync("#clone item", CancellationToken.None).GetAwaiter().GetResult();
            var rejected = system.ExecuteAsync("#clone 1 1", CancellationToken.None).GetAwaiter().GetResult();
            configs.ThrowOnSecondItemLookup = true;
            LogAssert.Expect(LogType.Error, "[TestCmdSystem] 克隆 失败: InvalidOperationException: test item lookup failure");
            var rollback = system.ExecuteAsync("#clone 2 1", CancellationToken.None).GetAwaiter().GetResult();

            foreach (var result in new[] { query, rejected, rollback })
            {
                Assert.That(result.AffectedAmount, Is.Zero, result.Code.ToString());
                Assert.That(result.AffectedItemId, Is.Zero, result.Code.ToString());
            }

            Assert.That(query.Code, Is.EqualTo(TestCmdResultCode.CloneComplete));
            Assert.That(rejected.Code, Is.EqualTo(TestCmdResultCode.BagFull));
            Assert.That(rollback.Code, Is.EqualTo(TestCmdResultCode.ExecutionFailed));
        }

        [Test]
        public void PrefixBoundaryRejectsWithoutHistoryDomainOrNetworkSideEffects()
        {
            TestCmdSystem system = Game.Interface.GetSystem<TestCmdSystem>();
            TestCmdModel model = Game.Interface.GetModel<TestCmdModel>();
            TestToolSettingsModel settings = Game.Interface.GetModel<TestToolSettingsModel>();

            Assert.That(system.ExecuteAsync(string.Empty, CancellationToken.None).GetAwaiter().GetResult().Code,
                Is.EqualTo(TestCmdResultCode.EmptyInput));
            foreach (string input in new[] { "clone 1 1", "!clone 1 1", "#", "$" })
            {
                Assert.That(system.ExecuteAsync(input, CancellationToken.None).GetAwaiter().GetResult().Code,
                    Is.EqualTo(TestCmdResultCode.PrefixRequired), input);
            }

            Assert.That(settings.RecentCommands, Is.Empty);
            Assert.That(model.UnitHealthBarsVisible, Is.False);
            Assert.That(network.CallCount, Is.Zero);
        }

        [Test]
        public void BodySymbolsStayLiteralAndDollarRoutesWithCompleteHistoryForAllOutcomes()
        {
            // 服务器路由已有登录前置条件；夹具必须显式提供已认证快照。
            network.State=NetworkConnectionState.Disconnected;
            Game.Interface.GetModel<OnlineAccountModel>().Publish(new OnlineAccountSnapshot(
                OnlineConnectionState.Authenticated,OnlineOperationState.Idle,OnlineAccountResultCode.Success,
                "test-user",1,1,1,false,"test-user"));
            // 沿用 ServerTestCmdTests 的隔离方式，不让准备阶段触发房间与库存恢复请求。
            network.State=NetworkConnectionState.Connected;
            TestCmdSystem system = Game.Interface.GetSystem<TestCmdSystem>();
            TestCmdExecutionResult literal = system.ExecuteAsync("#clone #/$", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(literal.Code, Is.EqualTo(TestCmdResultCode.ItemNotFound));
            Assert.That(network.CallCount, Is.Zero);

            network.Response = new G2C_ExecuteServerTestCmd
                { Recognized = false, Success = false, ResultCode = 3, Message = "unknown" };
            Assert.That(system.ExecuteAsync("$missing #/$", CancellationToken.None).GetAwaiter().GetResult().Code,
                Is.EqualTo(TestCmdResultCode.ServerUnknownCommand));
            Assert.That(((C2G_ExecuteServerTestCmd)network.LastRequest).CommandText, Is.EqualTo("missing #/$"));

            network.Response = new G2C_ExecuteServerTestCmd
                { Recognized = true, Success = false, ResultCode = 4, Message = "failed" };
            Assert.That(system.ExecuteAsync("$fail", CancellationToken.None).GetAwaiter().GetResult().Code,
                Is.EqualTo(TestCmdResultCode.ServerExecutionFailed));

            network.Exception = new TimeoutException();
            Assert.That(system.ExecuteAsync("$slow", CancellationToken.None).GetAwaiter().GetResult().Code,
                Is.EqualTo(TestCmdResultCode.ServerTimeout));

            network.State = NetworkConnectionState.Disconnected;
            Assert.That(system.ExecuteAsync("$offline", CancellationToken.None).GetAwaiter().GetResult().Code,
                Is.EqualTo(TestCmdResultCode.ServerNotConnected));

            Assert.That(network.CallCount, Is.EqualTo(3));
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands,
                Is.EqualTo(new[] { "$offline", "$slow", "$fail", "$missing #/$", "#clone #/$" }));
        }

        [Test]
        public void CloneUniqueTextKeyWithAmountCommitsRequestedAmount()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var events = CountInventoryEvents();
            var result = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#clone item.medkit.name 3", CancellationToken.None).GetAwaiter().GetResult();

            AssertResult(result, true, true, TestCmdResultCode.CloneComplete, 3, 0);
            Assert.That(bag.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(3));
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(IsSaveDirty(), Is.True);
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands[0], Is.EqualTo("#clone item.medkit.name 3"));
        }

        [Test]
        public void CloneMultipleTextMatchesNeverMutatesInventoryWithOrWithoutAmount()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var before = SnapshotText(bag);
            var events = CountInventoryEvents();

            var withoutAmount = system.ExecuteAsync("#clone item.", CancellationToken.None).GetAwaiter().GetResult();
            var withAmount = system.ExecuteAsync("#clone item. 4", CancellationToken.None).GetAwaiter().GetResult();

            AssertResult(withoutAmount, true, true, TestCmdResultCode.CloneComplete, 0, 0);
            AssertResult(withAmount, true, true, TestCmdResultCode.CloneComplete, 0, 0);
            Assert.That(withoutAmount.Message, Does.Contain("金属零件: 1"));
            Assert.That(withoutAmount.Message, Does.Contain("布料: 2"));
            Assert.That(withoutAmount.Message, Does.Contain("急救包: 3"));
            Assert.That(SnapshotText(bag), Is.EqualTo(before));
            Assert.That(events.Count, Is.EqualTo(0));
            Assert.That(IsSaveDirty(), Is.False);
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands[0], Is.EqualTo("#clone item. 4"));
        }

        [Test]
        public void CloneUniqueTextQuerySupportsPartialAcceptanceAndRollback()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 3, 5, out _, out var error), Is.True, error);
            for (var i = 0; i < 11; i++)
            {
                Assert.That(bag.TryAdd(ItemIds.Cloth, 5, 5, out _, out error), Is.True, error);
            }

            var events = CountInventoryEvents();
            var partial = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#clone item.metal_parts.name 10", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(partial, true, true, TestCmdResultCode.ClonePartial, 2, 8);
            Assert.That(bag.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(5));
            Assert.That(events.Count, Is.EqualTo(1));

            var before = SnapshotText(bag);
            SetSaveDirty(false);
            configs.ThrowOnFirstItemLookup = true;
            TestCmdExecutionResult failed;
            using (Log.CaptureForTests((_, __) => { }))
            {
                failed = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#clone item.medkit.name 1", CancellationToken.None).GetAwaiter().GetResult();
            }
            AssertResult(failed, true, false, TestCmdResultCode.ExecutionFailed, 0, 0);
            Assert.That(SnapshotText(bag), Is.EqualTo(before));
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(IsSaveDirty(), Is.False);
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands[0], Is.EqualTo("#clone item.medkit.name 1"));
        }

        [Test]
        public void ClearBagOnlyChangesPlayerBagAndSkipsEmptyCommit()
        {
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 2, 5, out _, out var error), Is.True, error);
            Assert.That(warehouse.TryAdd(ItemIds.Cloth, 4, 5, out _, out error), Is.True, error);
            var beforeWarehouse = SnapshotText(warehouse);
            var events = CountInventoryEvents();

            var cleared = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#clear bag", CancellationToken.None).GetAwaiter().GetResult();

            AssertResult(cleared, true, true, TestCmdResultCode.ClearBagComplete, 2, 0);
            Assert.That(SnapshotText(Game.Interface.GetModel<PlayerBagModel>().Container), Is.Empty);
            Assert.That(SnapshotText(warehouse), Is.EqualTo(beforeWarehouse));
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(IsSaveDirty(), Is.True);

            SetSaveDirty(false);
            events.Count = 0;
            var empty = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#clear bag", CancellationToken.None).GetAwaiter().GetResult();

            AssertResult(empty, true, true, TestCmdResultCode.ClearBagComplete, 0, 0);
            Assert.That(events.Count, Is.EqualTo(0));
            Assert.That(IsSaveDirty(), Is.False);
            _ = inventory;
        }

        [Test]
        public void CommandStateGateAllowsOnlyBaseSafeAndActiveExpedition()
        {
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            Assert.That(inventory.CanModifyPlayerBag(out var error), Is.True, error);

            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
            Assert.That(inventory.CanModifyPlayerBag(out _), Is.False);

            EnterBase();
            Assert.That(inventory.TrySetPlayerBagRiskState(PlayerBagRiskState.EnteringExpedition, "test", false, out error), Is.True, error);
            Assert.That(inventory.CanModifyPlayerBag(out _), Is.False);

            EnterExpedition(ExpeditionStatus.Playing, 100);
            Assert.That(inventory.CanModifyPlayerBag(out error), Is.True, error);

            EnterExpedition(ExpeditionStatus.ReturnQualified, 100);
            Assert.That(inventory.CanModifyPlayerBag(out error), Is.True, error);

            EnterExpedition(ExpeditionStatus.Returning, 100);
            Assert.That(inventory.CanModifyPlayerBag(out _), Is.False);

            EnterExpedition(ExpeditionStatus.Playing, 0);
            Assert.That(inventory.CanModifyPlayerBag(out _), Is.False);
        }

        [Test]
        public void PausedBagCommandsCommitWithoutResumingGameplayOrAllowingOrdinaryBagOperations()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var inventory = Game.Interface.GetSystem<InventorySystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Game.Interface.GetSystem<AppStateSystem>().TryPause();
            Assert.That(inventory.CanModifyPlayerBag(out _), Is.False);
            var cloned = system.ExecuteAsync("#clone " + ItemIds.MetalParts + " 2", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(cloned.Success, Is.True);
            Assert.That(bag.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(2));
            var cleared = system.ExecuteAsync("#clear bag", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(cleared.Success, Is.True);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.Zero);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(inventory.CanModifyPlayerBag(out _), Is.False);

            EnterExpedition(ExpeditionStatus.Playing, 100);
            Game.Interface.GetSystem<AppStateSystem>().TryPause();
            Assert.That(system.ExecuteAsync("#clone " + ItemIds.MetalParts, CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(system.ExecuteAsync("#clear bag", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
        }

        [Test]
        public void CommandExceptionRestoresPlayerBagSnapshot()
        {
            configs.ThrowOnSecondItemLookup = true;
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 1, 5, out _, out var error), Is.True, error);
            var before = SnapshotText(bag);
            var events = CountInventoryEvents();

            using var logs = Log.CaptureForTests((_, __) => { });
            var result = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#clone 1 2", CancellationToken.None).GetAwaiter().GetResult();

            AssertResult(result, true, false, TestCmdResultCode.ExecutionFailed, 0, 0);
            Assert.That(SnapshotText(bag), Is.EqualTo(before));
            Assert.That(events.Count, Is.EqualTo(0));
            Assert.That(IsSaveDirty(), Is.False);
        }

        [Test]
        public void CommandWritesExpectedRuntimeLogSeverity()
        {
            var captured = new System.Collections.Generic.List<LogType>();
            using (Log.CaptureForTests((type, _) => captured.Add(type)))
            {
                Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#clone 1 1", CancellationToken.None).GetAwaiter().GetResult();
                Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#clone 1 nope", CancellationToken.None).GetAwaiter().GetResult();
                configs.ThrowOnSecondItemLookup = true;
                Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#clone 2 1", CancellationToken.None).GetAwaiter().GetResult();
            }

            Assert.That(captured, Is.EqualTo(new[] { LogType.Log, LogType.Warning, LogType.Error }));
        }

        [Test]
        public void GeneratedRuntimeCatalogDiscoversCameraCategoryAndCommands()
        {
            var provider = new LubanRuntimeConfigProvider(() => new global::Tables(name =>
                new Luban.ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", name + ".bytes")))));

            Assert.That(provider.TryGetTestCmdCatalog(out var categories, out var entries, out var error), Is.True, error);
            Assert.That(categories.Select(value => value.Id), Does.Contain("camera-view"));
            Assert.That(entries.Select(value => value.Id), Does.Contain("camera.topdown"));
            Assert.That(entries.Select(value => value.Id), Does.Contain("camera.shoulder"));
            Assert.That(entries.Single(value => value.Id == "camera.firstperson").CommandTemplate, Is.EqualTo("#camera firstperson"));
            Assert.That(entries.Single(value => value.Id == "camera.firstperson").CategoryId, Is.EqualTo("camera-view"));
            Assert.That(entries.Single(value => value.Id == "camera.topdown").CategoryId, Is.EqualTo("camera-view"));
            Assert.That(entries.Single(value => value.Id == "camera.shoulder").CategoryId, Is.EqualTo("camera-view"));
            Assert.That(categories.Select(value => value.Id), Does.Contain("diagnostics"));
            Assert.That(entries.Single(value => value.Id == "diagnostics.runtime-log").CommandTemplate, Is.EqualTo("#log"));
            Assert.That(entries.Single(value => value.Id == "diagnostics.runtime-log").CategoryId, Is.EqualTo("diagnostics"));
            Assert.That(entries.Single(value => value.Id == "appearance.list").CommandTemplate, Is.EqualTo("#appearance list"));
            Assert.That(entries.Single(value => value.Id == "appearance.switch").CommandTemplate, Is.EqualTo("#appearance "));
            Assert.That(entries.Single(value => value.Id == "appearance.reset").CommandTemplate, Is.EqualTo("#appearance reset"));
        }

        private static void EnterBase()
        {
            var app = Game.Interface.GetSystem<AppStateSystem>();
            app.TryTransition(AppState.Loading);
            app.TryTransition(AppState.Playing);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.BaseSafe, "test", false, out _);
            Game.Interface.GetModel<ExpeditionModel>().Clear();
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 1f, 1f, 5f),
                false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            SetSaveDirty(false);
        }

        private static void EnterExpedition(ExpeditionStatus status, int health)
        {
            EnterBase();
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            var expedition = Game.Interface.GetModel<ExpeditionModel>();
            expedition.BeginSurvival(MapIds.SurvivalSample, 60f);
            expedition.SetExpeditionId(Guid.NewGuid().ToString("N"));
            if (status == ExpeditionStatus.ReturnQualified)
            {
                expedition.QualifyReturn();
            }
            else if (status == ExpeditionStatus.Returning)
            {
                expedition.QualifyReturn();
                expedition.BeginReturning();
            }
            else if (status == ExpeditionStatus.Defeated)
            {
                expedition.MarkDefeated();
            }

            Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out _);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(health);
            SetSaveDirty(false);
        }

        private static EventCounter CountInventoryEvents()
        {
            var counter = new EventCounter();
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => counter.Count++);
            return counter;
        }

        private static string SnapshotText(ItemContainer container)
        {
            return string.Join("|", container.Slots
                .OrderBy(pair => pair.Key)
                .Select(pair => $"{pair.Key}:{pair.Value.ItemId}:{pair.Value.Amount}"));
        }

        private static bool IsSaveDirty()
        {
            return (bool)typeof(SaveSystem)
                .GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(Game.Interface.GetSystem<SaveSystem>());
        }

        private static void AssertFailurePreservesBag(
            TestCmdSystem system,
            string command,
            TestCmdResultCode expectedCode)
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var before = SnapshotText(bag);
            var result = system.ExecuteAsync("#" + command, CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(result, true, false, expectedCode, 0, 0);
            Assert.That(SnapshotText(bag), Is.EqualTo(before));
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands[0], Is.EqualTo("#" + command));
        }

        private static void SetSaveDirty(bool value)
        {
            typeof(SaveSystem)
                .GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(Game.Interface.GetSystem<SaveSystem>(), value);
        }

        private static void AssertResult(
            TestCmdExecutionResult result,
            bool recognized,
            bool success,
            TestCmdResultCode code,
            int affected,
            int rejected)
        {
            Assert.That(result.Recognized, Is.EqualTo(recognized));
            Assert.That(result.Success, Is.EqualTo(success));
            Assert.That(result.Code, Is.EqualTo(code));
            Assert.That(result.AffectedAmount, Is.EqualTo(affected));
            Assert.That(result.RejectedAmount, Is.EqualTo(rejected));
            Assert.That(result.Message, Is.Not.Empty);
        }

        private sealed class EventCounter
        {
            public int Count;
        }

        private sealed class RoutingNetwork: INetworkRuntime
        {
            public object Response;
            public Exception Exception;
            public IRequest LastRequest;
            public int CallCount;
            public NetworkConnectionState State = NetworkConnectionState.Connected;
            public bool IsInitialized => true;
            public NetworkConnectionState ConnectionState => State;
            public int ConnectionGeneration => 1;
            public void Initialize() { }
            public void Update() { }
            public Task ConnectAsync(NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<TResponse> CallAsync<TResponse>(IRequest request, int timeoutMilliseconds, CancellationToken cancellationToken)
                where TResponse : class, IResponse
            {
                LastRequest = request;
                CallCount++;
                if (Exception != null)
                {
                    Exception value = Exception;
                    Exception = null;
                    return Task.FromException<TResponse>(value);
                }
                object valueResponse = Response;
                Response = null;
                return Task.FromResult((TResponse)valueResponse);
            }
            public IDisposable RegisterPushHandler<TMessage>(Action<TMessage> handler) where TMessage : class, IMessage => EmptyDisposable.Instance;
            public IDisposable RegisterDisconnectedHandler(Action<NetworkDisconnected> handler) => EmptyDisposable.Instance;
            public void Disconnect(NetworkDisconnectReason reason) => State = NetworkConnectionState.Disconnected;
            public void Dispose() => State = NetworkConnectionState.Disposed;
        }

        private sealed class EmptyDisposable: IDisposable
        {
            public static readonly EmptyDisposable Instance = new EmptyDisposable();
            public void Dispose() { }
        }

        private sealed class TestConfigProvider : IRuntimeConfigProvider
        {
            private int itemLookupCount;

            private bool throwOnSecondItemLookup;
            public bool ThrowOnFirstItemLookup { get; set; }
            public PlayerConfigDefinition? Player { get; set; }

            public bool ThrowOnSecondItemLookup
            {
                get => throwOnSecondItemLookup;
                set
                {
                    throwOnSecondItemLookup = value;
                    itemLookupCount = 0;
                }
            }

            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                itemLookupCount++;
                if (ThrowOnFirstItemLookup)
                {
                    throw new InvalidOperationException("test item lookup failure");
                }

                if (throwOnSecondItemLookup && itemLookupCount >= 2)
                {
                    throw new InvalidOperationException("test item lookup failure");
                }

                switch (id)
                {
                    case ItemIds.MetalParts:
                        value = new ItemDefinition(id, "Metal Parts", "Metal Parts", "item.metal_parts.name", "item.metal_parts.desc", string.Empty, global::ItemType.Material, 5, global::ItemUseEffect.None, 0);
                        error = null;
                        return true;
                    case ItemIds.Cloth:
                        value = new ItemDefinition(id, "Cloth", "Cloth", "item.cloth.name", "item.cloth.desc", string.Empty, global::ItemType.Material, 5, global::ItemUseEffect.None, 0);
                        error = null;
                        return true;
                    case ItemIds.Medkit:
                        value = new ItemDefinition(id, "Medkit", "Medkit", "item.medkit.name", "item.medkit.desc", string.Empty, global::ItemType.Consumable, 5, global::ItemUseEffect.RestoreHealth, 30);
                        error = null;
                        return true;
                    default:
                        value = default;
                        error = "missing item";
                        return false;
                }
            }

            public bool TryGetItems(out System.Collections.Generic.IReadOnlyList<ItemDefinition> values, out string error)
            {
                values = new[]
                {
                    new ItemDefinition(ItemIds.MetalParts, "Metal Parts", "Metal Parts", "item.metal_parts.name", "item.metal_parts.desc", string.Empty, global::ItemType.Material, 5, global::ItemUseEffect.None, 0),
                    new ItemDefinition(ItemIds.Cloth, "Cloth", "Cloth", "item.cloth.name", "item.cloth.desc", string.Empty, global::ItemType.Material, 5, global::ItemUseEffect.None, 0),
                    new ItemDefinition(ItemIds.Medkit, "Medkit", "Medkit", "item.medkit.name", "item.medkit.desc", string.Empty, global::ItemType.Consumable, 5, global::ItemUseEffect.RestoreHealth, 30)
                };
                error = null;
                return true;
            }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = new MapAssetDefinition(id, id);
                error = null;
                return true;
            }

            public bool TryGetMapSpawns(string mapId, out System.Collections.Generic.IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = Array.Empty<MapSpawnDefinition>();
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                if (Player.HasValue && string.Equals(Player.Value.Id, id, StringComparison.Ordinal))
                {
                    value = Player.Value;
                    error = null;
                    return true;
                }

                value = default;
                error = "missing player";
                return false;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                value = default;
                error = "missing enemy";
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                value = default;
                error = "missing loot";
                return false;
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                value = default;
                error = "missing extraction";
                return false;
            }
        }

        private sealed class CameraCommandPorts : IPlayerRuntimePortProvider
        {
            private readonly ICharacterMotorPort motor;
            private readonly IPlayerCameraPort camera;
            public CameraCommandPorts(ICharacterMotorPort motor, IPlayerCameraPort camera) { this.motor = motor; this.camera = camera; }
            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort port) { port = unitId == PlayerModel.LocalPlayerUnitId && mapInstanceId == motor.MapInstanceId ? motor : null; return port != null; }
            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port) { port = unitId == PlayerModel.LocalPlayerUnitId && mapInstanceId == motor.MapInstanceId ? camera : null; return port != null; }
            public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider) => false;
        }

        private sealed class CameraCommandCamera : IPlayerCameraPort
        {
            public bool IsValid => true;
            public void ApplyProfile() { }
            public void LateUpdate() { }
            public bool SupportsMode(PlayerCameraMode mode, out string error) { error = null; return true; }
            public void ApplyMode(PlayerCameraMode mode, float yaw, float pitch) { }
            public bool TryGetCameraPose(out Vector3 position, out Vector3 up) { position = Vector3.back; up = Vector3.up; return true; }
        }

        private sealed class CameraCommandMotor : ICharacterMotorPort
        {
            public CameraCommandMotor(long mapInstanceId) { MapInstanceId = mapInstanceId; }
            public int UnitId => PlayerModel.LocalPlayerUnitId;
            public long MapInstanceId { get; }
            public bool IsValid => true;
            public bool IsEnabled => true;
            public Vector3 Position => Vector3.zero;
            public Quaternion Rotation => Quaternion.identity;
            public float MovementYawDegrees => 0f;
            public bool IsGrounded => true;
            public void Move(Vector3 motion) { }
            public void RotateTowards(Quaternion rotation, float maxDegreesDelta) { }
            public void FaceDirection(Vector3 direction) { }
            public void SetTiliRing(float current, float maximum, bool isSprinting, bool exhausted) { }
            public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin) { origin = Vector3.zero; return true; }
            public void FaceWorldHud(Vector3 cameraPosition, Vector3 cameraUp) { }
            public void PlayAttackFeedback() { }
            public void PlayWeaponSwingFeedback() { }
            public void PlayHurtFeedback() { }
            public void TickCombatFeedback(float deltaTime) { }
        }

        private sealed class LocalizationTestLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : UnityEngine.Object
            {
                switch (path)
                {
                    case LocalizationLanguages.LanguagesAddress:
                        return new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"简体中文\"},{\"code\":\"en-US\",\"label\":\"English\"}]}") as T;
                    case "zh-CN.json":
                    case "en-US.json":
                        return new TextAsset(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Localization", path))) as T;
                    default:
                        return null;
                }
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            {
                var asset = LoadAsset<T>(path);
                onLoaded?.Invoke(asset);
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }
    }
}
