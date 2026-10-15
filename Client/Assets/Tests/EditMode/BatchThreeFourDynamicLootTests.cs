using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class BatchThreeFourDynamicLootTests
    {
        private TestResourceLoader loader;
        private TestViewInstanceProvider instances;
        private TestConfigProvider configs;
        private TestPlayerRuntimeProvider playerPorts;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            ResourceLoader.ResetForTests();
            loader = new TestResourceLoader();
            instances = new TestViewInstanceProvider();
            configs = new TestConfigProvider();
            playerPorts = new TestPlayerRuntimeProvider();
            ResourceLoader.SetLoaderForTests(loader);
            Game.Interface.RegisterUtility<IViewInstanceProvider>(instances);
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(configs);
            Game.Interface.RegisterUtility<IPlayerRuntimePortProvider>(playerPorts);
            Assert.That(Game.Interface.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(AppState.Playing), Is.True);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 1f, 1f, 5f),
                false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
        }

        [TearDown]
        public void TearDown()
        {
            ResourceLoader.ResetForTests();
            TestReset.ResetArchitecture();
        }

        [Test]
        public void LootPrefabAndConfigContainRequiredPresentationAndGameplayData()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<ResourcePickupView>(
                "Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab");

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<Collider>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<Collider>().isTrigger, Is.True);
            Assert.That(prefab.NameRoot, Is.Not.Null);
            Assert.That(prefab.NameLabel, Is.Not.Null);
            var nameCanvas = prefab.NameRoot.transform.Find("Canvas") as RectTransform;
            Assert.That(nameCanvas, Is.Not.Null);
            Assert.That(nameCanvas.localScale.x, Is.EqualTo(0.018f).Within(0.0001f));
            Assert.That(nameCanvas.sizeDelta, Is.EqualTo(new Vector2(320f, 60f)));
            Assert.That(prefab.NameLabel.enableAutoSizing, Is.True);
            Assert.That(prefab.NameLabel.fontSizeMax, Is.EqualTo(34f));
            var coveredArcadeVisuals = new[]
            {
                "ArcadeDropCoin",
                "ArcadeDropBomb",
                "ArcadeDropDash",
                "ArcadeRifle",
                "ArcadeScifiShotgun",
                "ArcadeScifiPlasmaRifle",
                "ArcadePiercingGun",
                "ArcadePanzerschreck",
                "ArcadeGrenade",
                "ArcadeDropShield",
                "ArcadeDropBoots",
                "ArcadeDropMagnet",
                "ArcadeChickenCompanion",
                "ArcadeSentryTurret",
                "ArcadeSawBlade",
                "ArcadeDropElectricOrb",
                "ArcadeDropLightning",
                "ArcadeDropDecoy",
                "ArcadeDropBarrel",
                "ArcadeDropGloves",
                "ArcadeDropClock"
            };
            foreach (var visualName in coveredArcadeVisuals)
            {
                var visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/Game/Prefabs/SceneObjects/{visualName}.prefab");
                Assert.That(visualPrefab, Is.Not.Null, visualName);
                Assert.That(visualPrefab.GetComponentsInChildren<Renderer>(true), Is.Not.Empty, visualName);
                Assert.That(visualPrefab.GetComponentsInChildren<Collider>(true), Is.Empty, visualName);
            }

            var chickenDependencies = AssetDatabase.GetDependencies(
                "Assets/Game/Prefabs/SceneObjects/ArcadeChickenCompanion.prefab", true);
            Assert.That(chickenDependencies.Any(path => path.StartsWith("Assets/Art/", StringComparison.Ordinal)),
                Is.False, "战斗伙伴表现必须完整复制到 Game，不能运行时引用临时 Art 资源");
            var chickenPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/SceneObjects/ArcadeChickenCompanion.prefab");
            Assert.That(chickenPrefab.GetComponentInChildren<Animator>(true)?.runtimeAnimatorController,
                Is.Not.Null, "战斗伙伴需要保留飞行动画");
            var sawPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/SceneObjects/ArcadeSawBlade.prefab");
            var sawController = sawPrefab.GetComponent<Animator>()?.runtimeAnimatorController;
            Assert.That(sawController,
                Is.Not.Null, "旋转锯需要保留自转动画");
            Assert.That(sawController.animationClips, Is.Not.Empty, "旋转锯控制器需要包含动画片段");
            Assert.That(sawController.animationClips[0].isLooping, Is.True, "锯片自转应持续循环");

            var instance = Object.Instantiate(prefab);
            try
            {
                var magnetAddress = "Prefabs/SceneObjects/ArcadeDropMagnet";
                var magnetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Prefabs/SceneObjects/ArcadeDropMagnet.prefab");
                Assert.That(instance.SetArcadePresentation(magnetPrefab, magnetAddress), Is.True);
                Assert.That(instance.ActiveArcadeVisualPrefabAddress, Is.EqualTo(magnetAddress));
                instance.ClearArcadePresentation();
                Assert.That(instance.ActiveArcadeVisualPrefabAddress, Is.Null);

                var flatPoses = new[]
                {
                    ("ArcadeDropBomb", new Vector3(0f, 0.22f, 0f), new Vector3(0f, 0f, 90f)),
                    ("ArcadeRifle", new Vector3(0f, 0.12f, 0f), new Vector3(0f, 0f, 90f)),
                    ("ArcadeScifiShotgun", new Vector3(0f, 0.06f, 0f), new Vector3(0f, 0f, 90f)),
                    ("ArcadePiercingGun", new Vector3(0f, 0.10f, 0f), new Vector3(0f, 0f, 90f)),
                    ("ArcadePanzerschreck", new Vector3(0f, 0.18f, 0f), new Vector3(0f, 0f, 90f)),
                    ("ArcadeGrenade", new Vector3(0f, 0.30f, 0f), new Vector3(90f, 0f, 0f)),
                    ("ArcadeScifiPlasmaRifle", new Vector3(0f, 0.11f, 0f), new Vector3(0f, 0f, 90f))
                };
                foreach (var (visualName, expectedPosition, expectedEuler) in flatPoses)
                {
                    var address = $"Prefabs/SceneObjects/{visualName}";
                    var visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        $"Assets/Game/Prefabs/SceneObjects/{visualName}.prefab");
                    Assert.That(instance.SetArcadePresentation(visualPrefab, address), Is.True, visualName);
                    var visual = instance.transform.Find("ArcadeDropVisual");
                    Assert.That(visual, Is.Not.Null, visualName);
                    Assert.That(visual.localPosition, Is.EqualTo(expectedPosition), visualName);
                    Assert.That(Quaternion.Angle(visual.localRotation, Quaternion.Euler(expectedEuler)),
                        Is.LessThan(0.1f), visualName);
                }
            }
            finally
            {
                Object.DestroyImmediate(instance.gameObject);
            }

            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes($"Assets/Game/Config/{name}.bytes"))));
            Assert.That(provider.TryGetLoot("loot.metal-parts", out var config, out var error), Is.True, error);
            Assert.That(config.PrefabAddress, Is.EqualTo("Prefabs/SceneObjects/TestResourcePickup"));
            Assert.That(config.ItemId, Is.EqualTo(ItemIds.MetalParts));
            Assert.That(config.Amount, Is.EqualTo(4));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out error), Is.True, error);
            var configuredVisualDrops = catalog.Drops
                .Where(drop => !string.IsNullOrWhiteSpace(drop.VisualPrefabAddress))
                .ToArray();
            Assert.That(configuredVisualDrops, Has.Length.EqualTo(20));
            Assert.That(configuredVisualDrops.All(drop =>
                AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{drop.VisualPrefabAddress}.prefab") != null), Is.True);
            Assert.That(catalog.Drops.Count(drop => string.IsNullOrWhiteSpace(drop.VisualPrefabAddress)),
                Is.EqualTo(0));
            Assert.That(catalog.Drops.Single(drop => drop.ProductId == "arcade.powerup.magnet").VisualPrefabAddress,
                Is.EqualTo("Prefabs/SceneObjects/ArcadeDropMagnet"));
            Assert.That(catalog.Drops.Single(drop => drop.ProductId == "arcade.weapon.rocket").VisualPrefabAddress,
                Is.EqualTo("Prefabs/SceneObjects/ArcadePanzerschreck"));
            Assert.That(catalog.Drops.Single(drop => drop.ProductId == "arcade.powerup.chicken").VisualPrefabAddress,
                Is.EqualTo("Prefabs/SceneObjects/ArcadeChickenCompanion"));
            Assert.That(catalog.Drops.Single(drop => drop.ProductId == "arcade.powerup.sentry").VisualPrefabAddress,
                Is.EqualTo("Prefabs/SceneObjects/ArcadeSentryTurret"));
            Assert.That(catalog.Drops.Single(drop => drop.ProductId == "arcade.powerup.saw").VisualPrefabAddress,
                Is.EqualTo("Prefabs/SceneObjects/ArcadeSawBlade"));
        }

        [Test]
        public void ArcadePowerupsAndSupportEquipmentProvideReplaceableActiveVisualPrefabs()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes($"Assets/Game/Config/{name}.bytes"))));

            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            Assert.That(catalog.Powerups, Has.Count.EqualTo(11));
            Assert.That(catalog.Powerups.All(powerup =>
                !string.IsNullOrWhiteSpace(powerup.ActiveVisualPrefabAddress)), Is.True);
            Assert.That(catalog.Powerups.All(powerup =>
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/Game/{powerup.ActiveVisualPrefabAddress}.prefab") != null), Is.True);

            var sentry = catalog.Powerups.Single(powerup => powerup.Id == "arcade.powerup.sentry");
            Assert.That(sentry.VisualFollowPlayer, Is.False);
            Assert.That(sentry.ActiveVisualPrefabAddress,
                Is.EqualTo("Prefabs/SceneObjects/ArcadeSentryTurret"));
            var saw = catalog.Powerups.Single(powerup => powerup.Id == "arcade.powerup.saw");
            Assert.That(saw.ActiveVisualPrefabAddress,
                Is.EqualTo("Prefabs/SceneObjects/ArcadeSawBlade"));
            var chicken = catalog.Powerups.Single(powerup => powerup.Id == "arcade.powerup.chicken");
            Assert.That(chicken.VisualFollowPlayer, Is.True);
            Assert.That(chicken.ActiveVisualPrefabAddress,
                Is.EqualTo("Prefabs/SceneObjects/ArcadeChickenCompanion"));

            var drone = catalog.ShopItems.Single(item => item.ProductId == "arcade.support.drone");
            Assert.That(drone.EquipmentSlotId, Is.EqualTo(ArcadeRunDefaults.SupportSlotId));
            Assert.That(drone.ActiveVisualPrefabAddress, Is.Not.Empty);
            Assert.That(drone.ActiveVisualPrefabAddress,
                Is.EqualTo("Prefabs/SceneObjects/ArcadeSupportDroneVisual"));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(
                $"Assets/Game/{drone.ActiveVisualPrefabAddress}.prefab"), Is.Not.Null);
        }

        [Test]
        public void MapsWithNoStaticLootPrepareAndCommitSuccessfully()
        {
            var ctrl = new LootCtrl(Game.Interface);
            var context = CreateContext(10, Array.Empty<MapSpawnDefinition>());

            var result = Prepare(ctrl, context);
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot.Count, Is.EqualTo(0));
        }

        [Test]
        public void ArcadeLootPresentation_ReusesFormalPickupPrefabWithoutRegisteringLootDomains()
        {
            loader.Asset = CreateLootPrefab("arcade-loot-prefab");
            configs.Loots[ArcadeRunDefaults.DropPresentationLootConfigId] = new LootConfigDefinition(
                ArcadeRunDefaults.DropPresentationLootConfigId,
                "Prefabs/SceneObjects/TestResourcePickup",
                ItemIds.MetalParts,
                1);
            var ctrl = new LootCtrl(Game.Interface);
            var standard = CreateContext(130, Array.Empty<MapSpawnDefinition>());
            var context = MapPreparationContext.CreateArcade(MapIds.Arcade, 130, standard.Scene,
                Array.Empty<MapSpawnDefinition>());

            var prepare = Prepare(ctrl, context);
            Assert.That(prepare.Success, Is.True, prepare.Error);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            Assert.That(ctrl.TrySetDrop(3001, Vector3.zero, "街机金币", 6, out var error), Is.True, error);
            Assert.That(instances.Created, Has.Count.EqualTo(1));
            var first = instances.Created[0];
            Assert.That(first.GetComponent<Collider>().enabled, Is.False);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot.Count, Is.EqualTo(0));

            ctrl.RemoveDrop(3001);
            Assert.That(first.activeSelf, Is.False);
            Assert.That(ctrl.TrySetDrop(3002, Vector3.right, "炸弹补给", 1, out error), Is.True, error);
            Assert.That(instances.Created, Has.Count.EqualTo(1));
            Assert.That(first.activeSelf, Is.True);

            ctrl.ClearArcadeDrops();
            ctrl.ReleaseAll();
        }

        [Test]
        public void ArcadeLootPresentation_LoadsConfiguredVisualPrefabAndAttachesItToPickup()
        {
            loader.Asset = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab");
            configs.Loots[ArcadeRunDefaults.DropPresentationLootConfigId] = new LootConfigDefinition(
                ArcadeRunDefaults.DropPresentationLootConfigId,
                "Prefabs/SceneObjects/TestResourcePickup",
                ItemIds.MetalParts,
                1);
            const string visualAddress = "Prefabs/SceneObjects/ArcadeDropMagnet";
            var visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/SceneObjects/ArcadeDropMagnet.prefab");
            loader.AssetsByPath[visualAddress] = visualPrefab;
            configs.ArcadeDrops = new[]
            {
                new ArcadeDropDefinition("drop", 1, 0, 1, ArcadeDropKind.Powerup,
                    "arcade.powerup.magnet", visualAddress, 1, 20f, 1)
            };
            var ctrl = new LootCtrl(Game.Interface);
            var standard = CreateContext(131, Array.Empty<MapSpawnDefinition>());
            var context = MapPreparationContext.CreateArcade(
                MapIds.Arcade,
                131,
                standard.Scene,
                Array.Empty<MapSpawnDefinition>());

            Assert.That(Prepare(ctrl, context).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            Assert.That(ctrl.TrySetDrop(
                3101,
                Vector3.zero,
                "磁铁",
                1,
                visualAddress,
                out var error), Is.True, error);
            Assert.That(instances.Created.Single().GetComponent<ResourcePickupView>().ActiveArcadeVisualPrefabAddress,
                Is.EqualTo(visualAddress));

            ctrl.ReleaseAll();
        }

        [Test]
        public void PrepareCreatesConfiguredStaticLootAtLocalPosesAndLoadsConfigOnce()
        {
            loader.Asset = CreateLootPrefab("loot-prefab");
            var ctrl = new LootCtrl(Game.Interface);
            var spawns = new[]
            {
                LootSpawn("a", new Vector3(-5f, 0.45f, -4f)),
                LootSpawn("b", new Vector3(3f, 0.45f, 2f)),
                LootSpawn("c", new Vector3(8f, 0.45f, 7f))
            };
            var context = CreateContext(11, spawns);

            var result = Prepare(ctrl, context);
            Assert.That(result.Success, Is.True, result.Error);

            Assert.That(loader.LoadCount("Prefabs/SceneObjects/TestResourcePickup"), Is.EqualTo(1));
            Assert.That(instances.Created.Count, Is.EqualTo(3));
            Assert.That(instances.Created.Select(item => item.transform.localPosition), Is.EquivalentTo(spawns.Select(item => item.LocalPosition)));
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot.Count, Is.EqualTo(0));
        }

        [Test]
        public void PreparePreloadsEnemyDropLootWithoutCreatingStaticInstances()
        {
            loader.Asset = CreateLootPrefab("loot-prefab");
            configs.Enemies["enemy.with-loot"] = new EnemyConfigDefinition("enemy.with-loot", "enemy", 10, 1, 5f, 1f, 1f, 1f, "loot.test-resource");
            var ctrl = new LootCtrl(Game.Interface);
            var context = CreateContext(12, new[] { EnemySpawn("enemy.with-loot") });

            Assert.That(Prepare(ctrl, context).Success, Is.True);

            Assert.That(loader.LoadCount("Prefabs/SceneObjects/TestResourcePickup"), Is.EqualTo(1));
            Assert.That(instances.Created.Count, Is.EqualTo(0));
        }

        [Test]
        public void CraftOutputPreloadReturnsRecipeLootLoadAndPrefabValidationErrors()
        {
            configs.CraftRecipesAvailable = false;
            configs.CraftRecipesError = "recipeError";
            var recipeFailure = Prepare(new LootCtrl(Game.Interface), CreateContext(120, Array.Empty<MapSpawnDefinition>()));
            Assert.That(recipeFailure.Success, Is.False);
            Assert.That(recipeFailure.Error, Does.Contain("recipeError"));

            configs.CraftRecipesAvailable = true;
            configs.CraftRecipes = new[] { CreateCraftOutputRecipe(ItemIds.Arrow) };
            configs.CraftLootByItemIdAvailable = false;
            configs.CraftLootByItemIdError = "lootError";
            var lootFailure = Prepare(new LootCtrl(Game.Interface), CreateContext(121, Array.Empty<MapSpawnDefinition>()));
            Assert.That(lootFailure.Success, Is.False);
            Assert.That(lootFailure.Error, Does.Contain("lootError"));

            configs.CraftLootByItemIdAvailable = true;
            configs.CraftRecipes = new[] { CreateCraftOutputRecipe(ItemIds.MetalParts) };
            loader.Asset = null;
            var loadFailure = Prepare(new LootCtrl(Game.Interface), CreateContext(122, Array.Empty<MapSpawnDefinition>()));
            Assert.That(loadFailure.Success, Is.False);
            Assert.That(loadFailure.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(loadFailure.Error, Does.Contain("Prefab"));

            loader.Asset = new GameObject("invalid-craft-output");
            var prefabFailure = Prepare(new LootCtrl(Game.Interface), CreateContext(123, Array.Empty<MapSpawnDefinition>()));
            Assert.That(prefabFailure.Success, Is.False);
            Assert.That(prefabFailure.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(prefabFailure.Error, Does.Contain("ResourcePickupView"));

            Assert.That(instances.Created, Is.Empty);
        }

        [Test]
        public void CraftOverflowDropCreatesOneGroundedLootAndRollbackReleasesAllDomains()
        {
            loader.Asset = CreateLootPrefab("craft-overflow-prefab");
            configs.CraftRecipes = new[] { CreateCraftOutputRecipe(ItemIds.MetalParts) };
            var ctrl = new LootCtrl(Game.Interface);
            const long mapInstanceId = 124;
            Assert.That(Prepare(ctrl, CreateContext(mapInstanceId, Array.Empty<MapSpawnDefinition>())).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            var playerPosition = new Vector3(3f, 4f, 5f);
            var playerRotation = Quaternion.Euler(20f, 90f, 15f);
            playerPorts.PoseAvailable = true;
            playerPorts.Pose = new PlayerPoseSnapshot(PlayerModel.LocalPlayerUnitId, mapInstanceId, playerPosition, playerRotation, true);

            Assert.That(ctrl.TryCreateCraftOverflowDrop(ItemIds.MetalParts, 17, out var receipt, out var error), Is.True, error);
            Assert.That(receipt.LootUnitIds, Has.Count.EqualTo(1));
            Assert.That(instances.Created, Has.Count.EqualTo(1));
            var unitId = receipt.LootUnitIds.Single();
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(unitId, out var unit), Is.True);
            Assert.That(unit.UnitSource, Is.EqualTo(UnitSource.RuntimeSpawned));
            Assert.That(Game.Interface.GetModel<LootModel>().TryGet(unitId, out var loot), Is.True);
            Assert.That(loot.ItemId, Is.EqualTo(ItemIds.MetalParts));
            Assert.That(loot.Amount, Is.EqualTo(17));
            var forward = playerRotation * Vector3.forward;
            forward.y = 0f;
            forward.Normalize();
            Assert.That(loot.Position.x, Is.EqualTo((playerPosition + forward * 0.8f).x).Within(0.001f));
            Assert.That(loot.Position.z, Is.EqualTo((playerPosition + forward * 0.8f).z).Within(0.001f));
            Assert.That(instances.Created.Single().GetComponent<Collider>().bounds.min.y, Is.EqualTo(playerPosition.y).Within(0.001f));

            ctrl.RollbackCraftOverflowDrop(receipt);
            ctrl.RollbackCraftOverflowDrop(receipt);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot, Is.Empty);
            Assert.That(instances.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public void PlayerInventoryDropPreloadsAllItemsCreatesWholeStackAndRollsBackIdempotently()
        {
            loader.Asset = CreateLootPrefab("player-inventory-drop-prefab");
            configs.ItemsAvailable = true;
            configs.Items = new[]
            {
                CreateItem(ItemIds.MetalParts, "Metal Parts"),
                CreateItem(ItemIds.Cloth, "Cloth")
            };
            configs.Loots["loot.cloth"] = new LootConfigDefinition(
                "loot.cloth",
                "Prefabs/SceneObjects/TestResourcePickup",
                ItemIds.Cloth,
                1);
            var ctrl = new LootCtrl(Game.Interface);
            const long mapInstanceId = 127;

            Assert.That(Prepare(ctrl, CreateContext(mapInstanceId, Array.Empty<MapSpawnDefinition>())).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            Assert.That(loader.LoadCount("Prefabs/SceneObjects/TestResourcePickup"), Is.EqualTo(1));
            var playerPosition = new Vector3(3f, 4f, 5f);
            var playerRotation = Quaternion.Euler(20f, 90f, 15f);
            playerPorts.PoseAvailable = true;
            playerPorts.Pose = new PlayerPoseSnapshot(
                PlayerModel.LocalPlayerUnitId,
                mapInstanceId,
                playerPosition,
                playerRotation,
                true);

            Assert.That(ctrl.TryCreatePlayerInventoryDrop(
                ItemIds.Cloth,
                9,
                out var receipt,
                out var error), Is.True, error);

            Assert.That(receipt.LootUnitIds, Has.Count.EqualTo(1));
            var unitId = receipt.LootUnitIds.Single();
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(unitId, out var unit), Is.True);
            Assert.That(unit.UnitSource, Is.EqualTo(UnitSource.RuntimeSpawned));
            Assert.That(Game.Interface.GetModel<LootModel>().TryGet(unitId, out var loot), Is.True);
            Assert.That(loot.ItemId, Is.EqualTo(ItemIds.Cloth));
            Assert.That(loot.Amount, Is.EqualTo(9));
            var forward = playerRotation * Vector3.forward;
            forward.y = 0f;
            forward.Normalize();
            Assert.That(loot.Position.x, Is.EqualTo((playerPosition + forward * 0.8f).x).Within(0.001f));
            Assert.That(loot.Position.z, Is.EqualTo((playerPosition + forward * 0.8f).z).Within(0.001f));
            Assert.That(instances.Created.Single().GetComponent<Collider>().bounds.min.y,
                Is.EqualTo(playerPosition.y).Within(0.001f));

            ctrl.RollbackPlayerInventoryDrop(receipt);
            ctrl.RollbackPlayerInventoryDrop(receipt);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.Zero);
            Assert.That(Game.Interface.GetModel<LootModel>().Loot, Is.Empty);
            Assert.That(instances.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public void PlayerInventoryDropSkippedItemEnumerationLeavesUncachedItemSafe()
        {
            loader.Asset = CreateLootPrefab("player-inventory-drop-skipped-prefab");
            var ctrl = new LootCtrl(Game.Interface);
            const long mapInstanceId = 128;
            Assert.That(Prepare(ctrl, CreateContext(mapInstanceId, Array.Empty<MapSpawnDefinition>())).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            playerPorts.PoseAvailable = true;
            playerPorts.Pose = new PlayerPoseSnapshot(
                PlayerModel.LocalPlayerUnitId,
                mapInstanceId,
                Vector3.zero,
                Quaternion.identity,
                true);

            Assert.That(ctrl.TryCreatePlayerInventoryDrop(
                ItemIds.MetalParts,
                1,
                out var receipt,
                out var error), Is.False);

            Assert.That(error, Does.Contain("Prefab"));
            Assert.That(receipt.LootUnitIds, Is.Empty);
            Assert.That(instances.Created, Is.Empty);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.Zero);
            Assert.That(Game.Interface.GetModel<LootModel>().Loot, Is.Empty);
        }

        [Test]
        public void PlayerInventoryDropCandidateFailureCleansViewAndDomains()
        {
            loader.Asset = CreateLootPrefab("player-inventory-drop-failure-prefab");
            configs.ItemsAvailable = true;
            configs.Items = new[] { CreateItem(ItemIds.MetalParts, "Metal Parts") };
            var ctrl = new LootCtrl(Game.Interface);
            const long mapInstanceId = 129;
            Assert.That(Prepare(ctrl, CreateContext(mapInstanceId, Array.Empty<MapSpawnDefinition>())).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            playerPorts.PoseAvailable = true;
            playerPorts.Pose = new PlayerPoseSnapshot(
                PlayerModel.LocalPlayerUnitId,
                mapInstanceId,
                Vector3.zero,
                Quaternion.identity,
                true);
            configs.LootByItemIdOverride = new LootConfigDefinition(
                "loot.test-resource",
                "Prefabs/SceneObjects/TestResourcePickup",
                0,
                1);

            Assert.That(ctrl.TryCreatePlayerInventoryDrop(
                ItemIds.MetalParts,
                3,
                out var receipt,
                out _), Is.False);

            Assert.That(receipt.LootUnitIds, Is.Empty);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.Zero);
            Assert.That(Game.Interface.GetModel<LootModel>().Loot, Is.Empty);
            Assert.That(instances.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public void CraftOverflowDropReleaseAllAndMapReleaseCleanUpAndOldReceiptsStaySafe()
        {
            loader.Asset = CreateLootPrefab("craft-overflow-release-prefab");
            configs.CraftRecipes = new[] { CreateCraftOutputRecipe(ItemIds.MetalParts) };
            var ctrl = new LootCtrl(Game.Interface);
            const long mapInstanceId = 125;
            Assert.That(Prepare(ctrl, CreateContext(mapInstanceId, Array.Empty<MapSpawnDefinition>())).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            playerPorts.PoseAvailable = true;
            playerPorts.Pose = new PlayerPoseSnapshot(PlayerModel.LocalPlayerUnitId, mapInstanceId, Vector3.zero, Quaternion.identity, true);

            Assert.That(ctrl.TryCreateCraftOverflowDrop(ItemIds.MetalParts, 3, out var firstReceipt, out var error), Is.True, error);
            ctrl.ReleaseCurrentMap();
            ctrl.RollbackCraftOverflowDrop(firstReceipt);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot, Is.Empty);

            Assert.That(Prepare(ctrl, CreateContext(mapInstanceId + 1, Array.Empty<MapSpawnDefinition>())).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            playerPorts.Pose = new PlayerPoseSnapshot(PlayerModel.LocalPlayerUnitId, mapInstanceId + 1, Vector3.zero, Quaternion.identity, true);
            Assert.That(ctrl.TryCreateCraftOverflowDrop(ItemIds.MetalParts, 4, out var secondReceipt, out error), Is.True, error);
            ctrl.ReleaseAll();
            ctrl.RollbackCraftOverflowDrop(secondReceipt);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot, Is.Empty);
            Assert.That(instances.ReleaseCount, Is.EqualTo(2));
        }

        [Test]
        public void CraftOverflowDropRejectsMissingInactiveAndWrongMapPlayerPoseWithoutSideEffects()
        {
            loader.Asset = CreateLootPrefab("craft-overflow-reject-prefab");
            configs.CraftRecipes = new[] { CreateCraftOutputRecipe(ItemIds.MetalParts) };
            var ctrl = new LootCtrl(Game.Interface);
            const long mapInstanceId = 126;
            Assert.That(Prepare(ctrl, CreateContext(mapInstanceId, Array.Empty<MapSpawnDefinition>())).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);

            foreach (var pose in new[]
                     {
                         new PlayerPoseSnapshot(PlayerModel.LocalPlayerUnitId, mapInstanceId, Vector3.zero, Quaternion.identity, false),
                         new PlayerPoseSnapshot(PlayerModel.LocalPlayerUnitId, mapInstanceId + 1, Vector3.zero, Quaternion.identity, true)
                     })
            {
                playerPorts.PoseAvailable = true;
                playerPorts.Pose = pose;
                Assert.That(ctrl.TryCreateCraftOverflowDrop(ItemIds.MetalParts, 1, out var receipt, out _), Is.False);
                Assert.That(receipt.LootUnitIds, Is.Empty);
            }

            playerPorts.PoseAvailable = false;
            Assert.That(ctrl.TryCreateCraftOverflowDrop(ItemIds.MetalParts, 1, out var unavailableReceipt, out _), Is.False);
            Assert.That(unavailableReceipt.LootUnitIds, Is.Empty);
            Assert.That(instances.Created, Is.Empty);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot, Is.Empty);
        }

        [Test]
        public void PrepareRejectsMissingConfigOrBrokenPrefabAndRollsBackCandidate()
        {
            var missing = new LootCtrl(Game.Interface);
            var missingResult = Prepare(missing, CreateContext(13, new[] { LootSpawn("missing", Vector3.zero, "loot.missing") }));
            Assert.That(missingResult.Success, Is.False);
            Assert.That(instances.Created.Count, Is.EqualTo(0));

            loader.Asset = new GameObject("broken");
            var broken = new LootCtrl(Game.Interface);
            var brokenResult = Prepare(broken, CreateContext(14, new[] { LootSpawn("broken", Vector3.zero) }));
            Assert.That(brokenResult.Success, Is.False);
            Assert.That(instances.ReleaseCount, Is.EqualTo(0));
        }

        [Test]
        public void CancelledLootLoadReturnsBeforeCallbackAndBlocksConcurrentLoad()
        {
            loader.DeferCallbacks = true;
            loader.Asset = CreateLootPrefab("delayed-loot");
            var ctrl = new LootCtrl(Game.Interface);
            using var cts = new CancellationTokenSource();
            var context = CreateContext(31, new[] { LootSpawn("a", Vector3.zero) });

            var first = ctrl.PrepareMapAsync(context, cts.Token);
            var concurrent = Prepare(ctrl, context);
            cts.Cancel();
            var cancelled = first.GetAwaiter().GetResult();

            Assert.That(concurrent.Success, Is.False);
            Assert.That(cancelled.Success, Is.False);
            Assert.That(instances.Created.Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot.Count, Is.EqualTo(0));
        }

        [Test]
        public void LateLootLoadCallbackReleasesHandleOnceAndAllowsRetry()
        {
            loader.DeferCallbacks = true;
            loader.Asset = CreateLootPrefab("late-loot");
            var ctrl = new LootCtrl(Game.Interface);
            using var cts = new CancellationTokenSource();

            var first = ctrl.PrepareMapAsync(CreateContext(32, new[] { LootSpawn("a", Vector3.zero) }), cts.Token);
            cts.Cancel();
            Assert.That(first.GetAwaiter().GetResult().Success, Is.False);

            loader.CompletePending();
            Assert.That(loader.ReleaseCount, Is.EqualTo(1));

            loader.DeferCallbacks = false;
            Assert.That(Prepare(ctrl, CreateContext(33, new[] { LootSpawn("b", Vector3.one) })).Success, Is.True);
            Assert.That(loader.LoadCount("Prefabs/SceneObjects/TestResourcePickup"), Is.EqualTo(2));
            Assert.That(instances.Created.Count, Is.EqualTo(1));

            ctrl.RollbackPreparedMap();
            Assert.That(loader.ReleaseCount, Is.EqualTo(2));
            Assert.That(instances.ReleaseCount, Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot.Count, Is.EqualTo(0));
        }

        [Test]
        public void CommitRegistersLootUnitAndDomainFromConfig()
        {
            loader.Asset = CreateLootPrefab("loot-prefab");
            var ctrl = new LootCtrl(Game.Interface);

            AssertRejectedRegisterLeavesNoLoot(9100, new LootConfigDefinition("loot.test-resource", "", ItemIds.MetalParts, 1), active: true);
            AssertRejectedRegisterLeavesNoLoot(9101, configs.Loots["loot.test-resource"], active: false);

            Assert.That(Prepare(ctrl, CreateContext(15, new[] { LootSpawn("a", new Vector3(1f, 0f, 2f)) })).Success, Is.True);

            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);

            var unit = Game.Interface.GetModel<UnitModel>().Units.Values.Single();
            Assert.That(unit.UnitType, Is.EqualTo(UnitType.Loot));
            Assert.That(unit.UnitSource, Is.EqualTo(UnitSource.MapPlaced));
            Assert.That(unit.ConfigId, Is.EqualTo("loot.test-resource"));
            Assert.That(Game.Interface.GetModel<LootModel>().TryGet(unit.UnitId, out var loot), Is.True);
            Assert.That(loot.ConfigId, Is.EqualTo("loot.test-resource"));
            Assert.That(loot.ItemId, Is.EqualTo(ItemIds.MetalParts));
            Assert.That(loot.Amount, Is.EqualTo(1));
        }

        [Test]
        public void CommitFailureRollsBackUnitsDomainInstancesAndTemporaryHandles()
        {
            loader.Asset = CreateLootPrefab("loot-prefab");
            configs.Loots["loot.test-resource"] = new LootConfigDefinition("loot.test-resource", "Prefabs/SceneObjects/TestResourcePickup", 0, 1);
            var ctrl = new LootCtrl(Game.Interface);

            Assert.That(Prepare(ctrl, CreateContext(16, new[] { LootSpawn("a", Vector3.zero) })).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.False);

            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot.Count, Is.EqualTo(0));
            Assert.That(instances.ReleaseCount, Is.EqualTo(1));
            Assert.That(loader.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public void DeactivateRestoreReleaseAndReleaseAllAreIdempotent()
        {
            loader.Asset = CreateLootPrefab("loot-prefab");
            var ctrl = new LootCtrl(Game.Interface);
            Assert.That(Prepare(ctrl, CreateContext(17, new[] { LootSpawn("a", Vector3.zero) })).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            var unitId = Game.Interface.GetModel<UnitModel>().Units.Keys.Single();

            ctrl.SetCurrentMapActive(false);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(unitId, out var inactive), Is.True);
            Assert.That(inactive.IsActive, Is.False);
            Assert.That(instances.Created[0].activeSelf, Is.False);

            ctrl.SetCurrentMapActive(true);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(unitId, out var active), Is.True);
            Assert.That(active.IsActive, Is.True);
            Assert.That(instances.Created[0].activeSelf, Is.True);

            ctrl.ReleaseCurrentMap();
            ctrl.ReleaseCurrentMap();
            ctrl.ReleaseAll();
            ctrl.ReleaseAll();
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot.Count, Is.EqualTo(0));
        }

        [Test]
        public void DropRequestCreatesRuntimeLootFromCachedConfigForCurrentMap()
        {
            loader.Asset = CreateLootPrefab("loot-prefab");
            var ctrl = new LootCtrl(Game.Interface);
            configs.Enemies["enemy.with-loot"] = new EnemyConfigDefinition("enemy.with-loot", "enemy", 10, 1, 5f, 1f, 1f, 1f, "loot.test-resource");
            Assert.That(Prepare(ctrl, CreateContext(18, new[] { EnemySpawn("enemy.with-loot") })).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);

            Game.Interface.SendEvent(new LootDropRequestedEvent(18, "loot.test-resource", new Vector3(2f, 0f, 2f), Quaternion.Euler(0f, 45f, 0f)));

            var unit = Game.Interface.GetModel<UnitModel>().Units.Values.Single();
            Assert.That(unit.UnitSource, Is.EqualTo(UnitSource.RuntimeSpawned));
            Assert.That(unit.ConfigId, Is.EqualTo("loot.test-resource"));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot.Count, Is.EqualTo(1));
        }

        [Test]
        public void AirborneRecoveryLandsOnSceneGroundAndCanBeCollected()
        {
            var ground = new GameObject("recovery-ground", typeof(BoxCollider));
            ground.transform.position = new Vector3(2f, 2.5f, 2f);
            ground.GetComponent<BoxCollider>().size = new Vector3(4f, 1f, 4f);
            try
            {
                loader.Asset = CreateLootPrefab("recovery-prefab");
                var ctrl = new LootCtrl(Game.Interface);
                configs.Enemies["enemy.with-loot"] = new EnemyConfigDefinition("enemy.with-loot", "enemy", 10, 1, 5f, 1f, 1f, 1f, "loot.test-resource");
                var context = CreateContext(182, new[] { EnemySpawn("enemy.with-loot") });
                Assert.That(Prepare(ctrl, context).Success, Is.True);
                Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
                // 动态物体不是地面；回收探测必须忽略运行根下的实体碰撞体。
                var blocker = new GameObject("runtime-blocker", typeof(BoxCollider));
                blocker.transform.SetParent(context.Scene.DynamicObjectRoot);
                blocker.transform.position = new Vector3(2f, 4f, 2f);
                Physics.SyncTransforms();

                Game.Interface.SendEvent(new LootDropRequestedEvent(182, "loot.test-resource",
                    new Vector3(2f, 5f, 2f), Quaternion.identity));

                var lootObject = instances.Created.Single();
                var trigger = lootObject.GetComponent<Collider>();
                Assert.That(trigger.bounds.min.y, Is.EqualTo(3f).Within(0.001f), "应落在真实地面，而非弹丸高度或动态实体上");
                Assert.That(lootObject.transform.position.x, Is.EqualTo(2f).Within(0.001f));
                Assert.That(lootObject.transform.position.z, Is.EqualTo(2f).Within(0.001f));
                RegisterPlayer(182, new Vector3(2f, 3f, 2f));
                var playerCollider = new GameObject("recovery-player-collider").AddComponent<BoxCollider>();
                playerPorts.AcceptedCollider = playerCollider;
                playerPorts.MapInstanceId = 182;
                Game.Interface.GetModel<ExpeditionModel>().BeginSurvival(MapIds.SurvivalSample, 60f);
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
                Game.Interface.GetModel<SaveModel>().SetExpedition(new ExpeditionSaveData { MapId = MapIds.SurvivalSample, Status = ExpeditionStatus.Playing });
                Assert.That(Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out var error), Is.True, error);
                Trigger(lootObject, playerCollider);
                Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(1));
                Assert.That(Game.Interface.GetModel<LootModel>().Loot, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(ground);
            }
        }

        [Test]
        public void RuntimeDropAlignsColliderBottomToGroundAndCollectsThroughTrigger()
        {
            loader.Asset = CreateLootPrefab("scaled-loot-prefab");
            loader.Asset.transform.localScale = Vector3.one * 0.45f;
            var ctrl = new LootCtrl(Game.Interface);
            configs.Enemies["enemy.with-loot"] = new EnemyConfigDefinition("enemy.with-loot", "enemy", 10, 1, 5f, 1f, 1f, 1f, "loot.test-resource");
            Assert.That(Prepare(ctrl, CreateContext(181, new[] { EnemySpawn("enemy.with-loot") })).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            var dropGroundPosition = new Vector3(2f, 0f, 2f);
            var dropRotation = Quaternion.Euler(0f, 45f, 0f);

            Game.Interface.SendEvent(new LootDropRequestedEvent(181, "loot.test-resource", dropGroundPosition, dropRotation));

            Assert.That(instances.Created.Count, Is.EqualTo(1));
            var lootObject = instances.Created[0];
            var view = lootObject.GetComponent<ResourcePickupView>();
            var trigger = lootObject.GetComponent<Collider>();
            Assert.That(trigger.bounds.min.y, Is.EqualTo(dropGroundPosition.y).Within(0.001f));
            Assert.That(view.transform.position.x, Is.EqualTo(dropGroundPosition.x).Within(0.001f));
            Assert.That(view.transform.position.z, Is.EqualTo(dropGroundPosition.z).Within(0.001f));
            Assert.That(Quaternion.Angle(view.transform.rotation, dropRotation), Is.LessThan(0.001f));

            var unit = Game.Interface.GetModel<UnitModel>().Units.Values.Single();
            Assert.That(unit.UnitSource, Is.EqualTo(UnitSource.RuntimeSpawned));
            AssertVector3(unit.Position, view.transform.position);
            Assert.That(Game.Interface.GetModel<LootModel>().TryGet(unit.UnitId, out var loot), Is.True);
            AssertVector3(loot.Position, view.transform.position);

            RegisterPlayer(181, view.transform.position);
            var playerCollider = new GameObject("runtime-drop-player-collider").AddComponent<BoxCollider>();
            playerPorts.AcceptedCollider = playerCollider;
            playerPorts.MapInstanceId = 181;
            Game.Interface.GetModel<ExpeditionModel>().BeginSurvival(MapIds.SurvivalSample, 60f);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            Game.Interface.GetModel<SaveModel>().SetExpedition(new ExpeditionSaveData { MapId = MapIds.SurvivalSample, Status = ExpeditionStatus.Playing });
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out var riskError), Is.True, riskError);

            Trigger(lootObject, playerCollider);

            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(1));
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(unit.UnitId, out _), Is.False);
            Assert.That(Game.Interface.GetModel<LootModel>().TryGet(unit.UnitId, out _), Is.False);
            Assert.That(instances.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public void DropRequestRejectsWrongMapMissingCacheAndCleansCreationFailure()
        {
            loader.Asset = CreateLootPrefab("loot-prefab");
            var ctrl = new LootCtrl(Game.Interface);
            configs.Loots["loot.bad"] = new LootConfigDefinition("loot.bad", "Prefabs/SceneObjects/TestResourcePickup", 0, 1);
            configs.Enemies["enemy.bad-loot"] = new EnemyConfigDefinition("enemy.bad-loot", "enemy", 10, 1, 5f, 1f, 1f, 1f, "loot.bad");
            Assert.That(Prepare(ctrl, CreateContext(19, new[]
            {
                LootSpawn("static", Vector3.zero),
                EnemySpawn("enemy.bad-loot")
            })).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            var existingUnit = Game.Interface.GetModel<UnitModel>().Units.Values.Single(item => item.ConfigId == "loot.test-resource").UnitId;

            Game.Interface.SendEvent(new LootDropRequestedEvent(20, "loot.test-resource", Vector3.zero, Quaternion.identity));
            Game.Interface.SendEvent(new LootDropRequestedEvent(19, "loot.unknown", Vector3.zero, Quaternion.identity));
            Game.Interface.SendEvent(new LootDropRequestedEvent(19, "loot.bad", Vector3.one, Quaternion.identity));

            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(existingUnit, out _), Is.True);
            Assert.That(Game.Interface.GetModel<LootModel>().TryGet(existingUnit, out _), Is.True);
            Assert.That(Game.Interface.GetModel<UnitModel>().Units.Values.Count(item => item.ConfigId == "loot.bad"), Is.EqualTo(0));
            Assert.That(instances.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public void CollectRequiresPlayerIdentitySameMapActiveRangeAndInventorySuccess()
        {
            loader.Asset = CreateLootPrefab("loot-prefab");
            var ctrl = new LootCtrl(Game.Interface);
            Assert.That(Prepare(ctrl, CreateContext(21, new[] { LootSpawn("a", Vector3.zero) })).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            var loot = Game.Interface.GetModel<UnitModel>().Units.Keys.Single();
            RegisterPlayer(21, Vector3.one * 5f);
            var wrongCollider = new GameObject("wrong").AddComponent<BoxCollider>();
            var playerCollider = new GameObject("player-collider").AddComponent<BoxCollider>();

            Trigger(instances.Created[0], wrongCollider);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(0));

            playerPorts.AcceptedCollider = playerCollider;
            playerPorts.MapInstanceId = 999;
            Trigger(instances.Created[0], playerCollider);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(0));

            playerPorts.MapInstanceId = 21;
            Trigger(instances.Created[0], playerCollider);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(0));

            Game.Interface.GetSystem<UnitSystem>().TryUpdateTransform(PlayerModel.LocalPlayerUnitId, new Vector3(0.5f, 0f, 0f), Quaternion.identity);
            Trigger(instances.Created[0], playerCollider);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(0));

            Game.Interface.GetSystem<UnitSystem>().TrySetActive(loot, false);
            Trigger(instances.Created[0], playerCollider);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(0));
            Game.Interface.GetSystem<UnitSystem>().TrySetActive(loot, true);

            Game.Interface.GetModel<ExpeditionModel>().BeginSurvival(MapIds.SurvivalSample, 60f);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out var riskError), Is.True, riskError);
            Trigger(instances.Created[0], playerCollider);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(1));
            Assert.That(Game.Interface.GetSystem<LootSystem>().TryCollect(loot, PlayerModel.LocalPlayerUnitId), Is.False);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(1));
        }

        [Test]
        public void CollectedLootUpdatesInventoryOnceAndReleasesPresentationOnce()
        {
            loader.Asset = CreateLootPrefab("loot-prefab");
            var ctrl = new LootCtrl(Game.Interface);
            Assert.That(Prepare(ctrl, CreateContext(22, new[] { LootSpawn("a", Vector3.zero) })).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            var unitId = Game.Interface.GetModel<UnitModel>().Units.Keys.Single();
            RegisterPlayer(22, Vector3.zero);
            Game.Interface.GetModel<ExpeditionModel>().BeginSurvival(MapIds.SurvivalSample, 60f);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out var riskError), Is.True, riskError);

            Assert.That(Game.Interface.GetSystem<LootSystem>().TryCollect(unitId, PlayerModel.LocalPlayerUnitId), Is.True);
            Assert.That(Game.Interface.GetSystem<LootSystem>().TryCollect(unitId, PlayerModel.LocalPlayerUnitId), Is.False);

            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(1));
            Assert.That(instances.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public void LootNameDisplayUsesConfiguredAmountRangeAndCameraPose()
        {
            Assert.That(LootCtrl.IsWithinDisplayRange(Vector3.zero, new Vector3(6f, 0f, 0f)), Is.True);
            Assert.That(LootCtrl.IsWithinDisplayRange(Vector3.zero, new Vector3(6.1f, 0f, 0f)), Is.False);
            Assert.That(ResourcePickupView.FormatDisplayName("测试物资", 2), Is.EqualTo("测试物资x2"));
            Assert.That(ResourcePickupView.FormatDisplayName("测试物资", 1), Is.EqualTo("测试物资"));
        }

        [Test]
        public void LootPrepareFailureKeepsCurrentLootAndCleansCandidate()
        {
            loader.Asset = CreateLootPrefab("loot-prefab");
            var current = new LootCtrl(Game.Interface);
            Assert.That(Prepare(current, CreateContext(23, new[] { LootSpawn("a", Vector3.zero) })).Success, Is.True);
            Assert.That(current.CommitPreparedMap().Success, Is.True);
            var currentUnit = Game.Interface.GetModel<UnitModel>().Units.Keys.Single();

            var next = new LootCtrl(Game.Interface);
            loader.Asset = null;
            var failed = Prepare(next, CreateContext(24, new[] { LootSpawn("b", Vector3.one) }));

            Assert.That(failed.Success, Is.False);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(currentUnit, out _), Is.True);
            Assert.That(Game.Interface.GetModel<LootModel>().TryGet(currentUnit, out _), Is.True);
            Assert.That(instances.Created.Count, Is.EqualTo(1));
        }

        [Test]
        public void ScenesContainNoPreplacedLootAndMapContextHasNoLootFields()
        {
            foreach (var path in new[]
                     {
                         "Assets/Game/Scenes/BaseMap.unity",
                         "Assets/Game/Scenes/SurvivalSampleMap.unity",
                         "Assets/Game/Scenes/TaskSampleMap.unity"
                     })
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Assert.That(FindInScene<ResourcePickupView>(scene).Count, Is.EqualTo(0), path);
            }

            var fields = typeof(MapContext).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(fields.Any(item => item.Name.Contains("loot", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(fields.Any(item => item.Name.Contains("testResourcePickupPrefab", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(fields.Any(item => item.Name == "extractionPoints"), Is.False);
        }

        [Test]
        public void HarvestDropLandsAtResourceAndRestoresFromWorldSnapshot()
        {
            loader.Asset = CreateLootPrefab("harvest-drop-prefab");
            configs.ItemsAvailable = true;
            configs.Items = new[] { CreateItem(ItemIds.MetalParts, "Metal Parts") };
            var ctrl = new LootCtrl(Game.Interface);
            var restored = new LootCtrl(Game.Interface);
            var context = CreateContext(128, Array.Empty<MapSpawnDefinition>());
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(1000, 3.5f, 1000);
            ground.transform.localScale = new Vector3(10, 1, 10);
            Physics.SyncTransforms();
            try
            {
                Assert.That(Prepare(ctrl, context).Success, Is.True);
                Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
                var position = new Vector3(1000, 4.3f, 1000);
                Assert.That(ctrl.TryCreateHarvestDrop(127, position, ItemIds.MetalParts, 4, out _, out _), Is.False);
                Assert.That(ctrl.TryCreateHarvestDrop(128, position, ItemIds.MetalParts, 4, out var ids, out var error), Is.True, error);
                Assert.That(ids, Has.Count.EqualTo(1));
                var loot = Game.Interface.GetModel<LootModel>().Loot.Values.Single();
                Assert.That(loot.Amount, Is.EqualTo(4));
                Assert.That(loot.Position.x, Is.EqualTo(position.x + .6f).Within(.001f));
                Assert.That(loot.Position.z, Is.EqualTo(position.z).Within(.001f));
                Assert.That(instances.Created.Single().GetComponent<Collider>().bounds.min.y, Is.EqualTo(4f).Within(.001f));
                var world = new ExpeditionWorldSaveData();
                ctrl.CaptureExpeditionWorld(world);
                Assert.That(world.Loot.Single().RemainingAmount, Is.EqualTo(4));
                ctrl.RollbackHarvestDrop(ids); ctrl.RollbackHarvestDrop(ids);
                Assert.That(Game.Interface.GetModel<LootModel>().Loot, Is.Empty);
                Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(ids.Single(), out _), Is.False);
                ctrl.ReleaseAll();
                var restoreContext = new MapPreparationContext(context.MapId, 129, context.Scene, Array.Empty<MapSpawnDefinition>(),
                    new ExpeditionSaveData { MapId = context.MapId, World = world });
                Assert.That(Prepare(restored, restoreContext).Success, Is.True);
                Assert.That(restored.CommitPreparedMap().Success, Is.True);
                var savedLoot = Game.Interface.GetModel<LootModel>().Loot.Values.Single();
                Assert.That(savedLoot.UnitId, Is.EqualTo(ids.Single()));
                Assert.That(savedLoot.MapInstanceId, Is.EqualTo(129));
                Assert.That(savedLoot.Amount, Is.EqualTo(4));
                AssertVector3(savedLoot.Position, loot.Position);
            }
            finally { ctrl.ReleaseAll(); restored.ReleaseAll(); Object.DestroyImmediate(ground); }
        }

        private static MapControllerResult Prepare(LootCtrl ctrl, MapPreparationContext context)
        {
            return ctrl.PrepareMapAsync(context, default).GetAwaiter().GetResult();
        }

        private static CraftRecipeDefinition CreateCraftOutputRecipe(int outputItemId)
        {
            return new CraftRecipeDefinition(
                "craft.output.preload.test",
                outputItemId,
                1,
                new[] { new CraftMaterialDefinition(ItemIds.MetalParts, 1) },
                1);
        }

        private static ItemDefinition CreateItem(int itemId, string name)
        {
            return new ItemDefinition(
                itemId,
                name,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                global::ItemType.Material,
                20,
                global::ItemUseEffect.None,
                0);
        }

        private int RegisterLootAndPlayer(long mapInstanceId, Vector3 lootPosition, Vector3 playerPosition)
        {
            RegisterPlayer(mapInstanceId, playerPosition);
            var lootId = 2000;
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                    lootId,
                    mapInstanceId,
                    UnitType.Loot,
                    UnitSource.MapPlaced,
                    "loot.test-resource",
                    lootPosition,
                    Quaternion.identity,
                    UnitAuthorityMode.Local,
                    0,
                    true),
                out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<LootSystem>().TryRegisterLoot(
                    lootId,
                    configs.Loots["loot.test-resource"],
                    out error),
                Is.True,
                error);
            return lootId;
        }

        private void AssertRejectedRegisterLeavesNoLoot(int unitId, LootConfigDefinition config, bool active)
        {
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                    unitId,
                    15,
                    UnitType.Loot,
                    UnitSource.MapPlaced,
                    "loot.test-resource",
                    Vector3.zero,
                    Quaternion.identity,
                    UnitAuthorityMode.Local,
                    0,
                    active),
                out var error), Is.True, error);

            Assert.That(Game.Interface.GetSystem<LootSystem>().TryRegisterLoot(unitId, config, out error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
            Assert.That(Game.Interface.GetModel<LootModel>().TryGet(unitId, out _), Is.False);
            Game.Interface.GetSystem<UnitSystem>().Remove(unitId, UnitRemovedReason.InvalidPresentation);
        }

        private void RegisterPlayer(long mapInstanceId, Vector3 position)
        {
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                    PlayerModel.LocalPlayerUnitId,
                    mapInstanceId,
                    UnitType.Player,
                    UnitSource.Player,
                    "player",
                    position,
                    Quaternion.identity,
                    UnitAuthorityMode.Local,
                    0,
                    true),
                out var error), Is.True, error);
        }

        private static MapPreparationContext CreateContext(long mapInstanceId, IReadOnlyList<MapSpawnDefinition> spawns)
        {
            var root = new GameObject("map-root");
            var runtime = new GameObject("DynamicObjectRoot").transform;
            runtime.SetParent(root.transform, false);
            var map = new GameObject("MapContext");
            var context = map.AddComponent<MapContext>();
            SetField(context, "mapContentRoot", root);
            SetField(context, "dynamicObjectRoot", runtime);
            return new MapPreparationContext("test.map", mapInstanceId, context, spawns);
        }

        private static MapSpawnDefinition LootSpawn(string id, Vector3 localPosition, string configId = "loot.test-resource")
        {
            return new MapSpawnDefinition(id, "test.map", MapSpawnObjectType.Loot, configId, localPosition.x, localPosition.y, localPosition.z, 0f);
        }

        private static MapSpawnDefinition EnemySpawn(string configId)
        {
            return new MapSpawnDefinition("enemy", "test.map", MapSpawnObjectType.Enemy, configId, 0f, 0f, 0f, 0f);
        }

        private static GameObject CreateLootPrefab(string name)
        {
            var prefab = new GameObject(name, typeof(BoxCollider), typeof(ResourcePickupView));
            var trigger = prefab.GetComponent<BoxCollider>();
            trigger.isTrigger = true;
            return prefab;
        }

        private static List<T> FindInScene<T>(UnityEngine.SceneManagement.Scene scene) where T : Component
        {
            var result = new List<T>();
            foreach (var root in scene.GetRootGameObjects())
            {
                result.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return result;
        }

        private static void SetField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
        {
            typeof(TTarget)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f));
        }

        private static void Trigger(GameObject lootObject, Collider collider)
        {
            typeof(ResourcePickupView)
                .GetMethod("OnTriggerEnter", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(lootObject.GetComponent<ResourcePickupView>(), new object[] { collider });
        }

        private sealed class TestConfigProvider : IRuntimeConfigProvider
        {
            public IReadOnlyList<ArcadeDropDefinition> ArcadeDrops { get; set; }
            public bool ItemsAvailable { get; set; }
            public IReadOnlyList<ItemDefinition> Items { get; set; } = Array.Empty<ItemDefinition>();
            public LootConfigDefinition? LootByItemIdOverride { get; set; }
            public bool CraftRecipesAvailable { get; set; } = true;
            public string CraftRecipesError { get; set; } = "missing craft recipes";
            public IReadOnlyList<CraftRecipeDefinition> CraftRecipes { get; set; } = Array.Empty<CraftRecipeDefinition>();
            public bool CraftLootByItemIdAvailable { get; set; } = true;
            public string CraftLootByItemIdError { get; set; } = "missing loot by item id";

            public readonly Dictionary<string, LootConfigDefinition> Loots =
                new Dictionary<string, LootConfigDefinition>
                {
                    ["loot.test-resource"] = new LootConfigDefinition(
                        "loot.test-resource",
                        "Prefabs/SceneObjects/TestResourcePickup",
                        ItemIds.MetalParts,
                        1)
                };

            public bool TryGetArcadeCatalog(out ArcadeConfigCatalogDefinition value, out string error)
            {
                if (ArcadeDrops == null)
                {
                    value = null;
                    error = "test arcade catalog unavailable";
                    return false;
                }
                value = new ArcadeConfigCatalogDefinition(default,
                    Array.Empty<ArcadeWeaponDefinition>(), Array.Empty<ArcadeArenaDefinition>(),
                    Array.Empty<ArcadeEnemyGroupDefinition>(), Array.Empty<ArcadeBossDefinition>(),
                    Array.Empty<ArcadeChallengeDefinition>(), ArcadeDrops,
                    Array.Empty<ArcadeShopItemDefinition>(), Array.Empty<ArcadeUpgradeDefinition>(),
                    Array.Empty<ArcadeRewardDefinition>());
                error = null;
                return true;
            }

            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                if (id == ItemIds.MetalParts)
                {
                    value = new ItemDefinition(id, "Metal Parts", string.Empty, string.Empty, string.Empty, string.Empty, global::ItemType.Material, 20, global::ItemUseEffect.None, 0);
                    error = null;
                    return true;
                }

                value = default;
                error = "missing item";
                return false;
            }

            public bool TryGetItems(out IReadOnlyList<ItemDefinition> values, out string error)
            {
                values = Items;
                error = ItemsAvailable ? null : "test items unavailable";
                return ItemsAvailable;
            }

            public readonly Dictionary<string, EnemyConfigDefinition> Enemies =
                new Dictionary<string, EnemyConfigDefinition>();

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = Array.Empty<MapSpawnDefinition>();
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                if (Enemies.TryGetValue(id, out value))
                {
                    error = null;
                    return true;
                }

                error = "missing enemy";
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                if (Loots.TryGetValue(id, out value))
                {
                    error = null;
                    return true;
                }

                error = "missing loot";
                return false;
            }

            public bool TryGetCraftRecipes(out IReadOnlyList<CraftRecipeDefinition> values, out string error)
            {
                values = CraftRecipes;
                error = CraftRecipesAvailable ? null : CraftRecipesError;
                return CraftRecipesAvailable;
            }

            public bool TryGetLootByItemId(int itemId, out LootConfigDefinition value, out string error)
            {
                if (LootByItemIdOverride.HasValue)
                {
                    value = LootByItemIdOverride.Value;
                    error = null;
                    return true;
                }

                value = Loots.Values.FirstOrDefault(item => item.ItemId == itemId);
                if (!CraftLootByItemIdAvailable)
                {
                    value = default;
                    error = CraftLootByItemIdError;
                    return false;
                }

                if (value.ItemId == itemId)
                {
                    error = null;
                    return true;
                }

                value = default;
                error = CraftLootByItemIdError;
                return false;
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }
        }

        private sealed class TestResourceLoader : IResourceLoader
        {
            private readonly Dictionary<string, int> loads = new Dictionary<string, int>();
            private readonly List<PendingLoad> pending = new List<PendingLoad>();
            public GameObject Asset { get; set; }
            public readonly Dictionary<string, GameObject> AssetsByPath = new Dictionary<string, GameObject>();
            public bool DeferCallbacks { get; set; }
            public int ReleaseCount { get; private set; }

            public T LoadAsset<T>(string path) where T : Object
            {
                return (AssetsByPath.TryGetValue(path, out var mapped) ? mapped : Asset) as T;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                loads[path] = LoadCount(path) + 1;
                var handle = new ResourceLoadHandle(new object());
                if (DeferCallbacks)
                {
                    pending.Add(new PendingLoad(() => onLoaded?.Invoke(
                        (AssetsByPath.TryGetValue(path, out var mapped) ? mapped : Asset) as T)));
                    return handle;
                }

                onLoaded?.Invoke((AssetsByPath.TryGetValue(path, out var mapped) ? mapped : Asset) as T);
                return handle;
            }

            public void Release(ResourceLoadHandle handle)
            {
                if (handle.IsValid)
                {
                    ReleaseCount++;
                }
            }

            public int LoadCount(string path)
            {
                return loads.TryGetValue(path, out var count) ? count : 0;
            }

            public void CompletePending()
            {
                var callbacks = pending.ToArray();
                pending.Clear();
                for (var i = 0; i < callbacks.Length; i++)
                {
                    callbacks[i].Complete();
                }
            }

            private readonly struct PendingLoad
            {
                private readonly Action complete;

                public PendingLoad(Action complete)
                {
                    this.complete = complete;
                }

                public void Complete()
                {
                    complete?.Invoke();
                }
            }
        }

        private sealed class TestViewInstanceProvider : IViewInstanceProvider
        {
            public readonly List<GameObject> Created = new List<GameObject>();
            public int ReleaseCount { get; private set; }

            public T Instantiate<T>(T prefab, Transform parent, Vector3 position, Quaternion rotation) where T : Component
            {
                var instance = Object.Instantiate(prefab.gameObject, position, rotation, parent);
                Created.Add(instance);
                return instance.GetComponent<T>();
            }

            public void Release(GameObject instance)
            {
                if (instance == null)
                {
                    return;
                }

                ReleaseCount++;
                Object.DestroyImmediate(instance);
            }
        }

        private sealed class TestPlayerRuntimeProvider : IPlayerRuntimePortProvider
        {
            public Collider AcceptedCollider { get; set; }
            public long MapInstanceId { get; set; }
            public bool PoseAvailable { get; set; }
            public PlayerPoseSnapshot Pose { get; set; }

            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort port)
            {
                port = null;
                return false;
            }

            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port)
            {
                port = null;
                return false;
            }

            public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider)
            {
                return collider != null &&
                       collider == AcceptedCollider &&
                       unitId == PlayerModel.LocalPlayerUnitId &&
                       mapInstanceId == MapInstanceId;
            }

            public bool TryGetCurrentPlayerPose(out PlayerPoseSnapshot snapshot, out string error)
            {
                snapshot = Pose;
                error = PoseAvailable ? null : "test player pose unavailable";
                return PoseAvailable;
            }
        }
    }
}
