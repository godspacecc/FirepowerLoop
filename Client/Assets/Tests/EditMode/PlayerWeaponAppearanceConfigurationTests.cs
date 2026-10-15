using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static Game.Tests.PlayerWeaponAppearanceTestHelpers;

namespace Game.Tests
{
    public sealed class LubanRuntimeConfigProviderWeaponAppearanceTests
    {
        [Test]
        public void TryGetPlayerWeaponAppearanceReturnsConfiguredRow()
        {
            var provider = CreateRealConfigProvider();

            Assert.That(provider.TryGetPlayerWeaponAppearance(ItemIds.ShortSword, out var sword, out var error), Is.True, error);
            Assert.That(sword.PrefabAddress, Is.EqualTo("Prefabs/Characters/Weapons/GrayboxShortSword"));
            Assert.That(sword.AttachPoint, Is.EqualTo(PlayerWeaponAttachPoint.MeleeWeapon));
            Assert.That(sword.LocalScale, Is.EqualTo(Vector3.one));

            Assert.That(provider.TryGetPlayerWeaponAppearance(ItemIds.Bow, out var bow, out error), Is.True, error);
            Assert.That(bow.PrefabAddress, Is.EqualTo("Prefabs/Characters/Weapons/GrayboxBow"));
            Assert.That(bow.AttachPoint, Is.EqualTo(PlayerWeaponAttachPoint.LeftHandWeapon));
        }

        [Test]
        public void TryGetProjectilesReturnsConfiguredRows()
        {
            var provider = CreateRealConfigProvider();

            Assert.That(provider.TryGetProjectiles(out var projectiles, out var error), Is.True, error);
            Assert.That(projectiles.Select(item => item.Id), Does.Contain("projectile.arrow.basic"));
            Assert.That(projectiles.All(item => !string.IsNullOrWhiteSpace(item.PrefabAddress)), Is.True);
        }

        [Test]
        public void ValidatesEveryWeaponHasAppearance()
        {
            var tables = CreateTables();
            tables.TbWeaponAppearance.DataMap.Remove(ItemIds.Bow);
            tables.TbWeaponAppearance.DataList.RemoveAll(row => row.ItemId == ItemIds.Bow);

            var error = InvokeProviderValidate(tables);

            Assert.That(error, Does.Contain("TbWeaponAppearance"));
            Assert.That(error, Does.Contain(ItemIds.Bow.ToString()));
        }

        [Test]
        public void RejectsInvalidAppearanceRows()
        {
            var tables = CreateTables();
            SetReadonlyField(tables.TbWeaponAppearance.Get(ItemIds.ShortSword), "AttachPoint", PlayerWeaponAttachPoint.RangedWeapon);
            Assert.That(InvokeProviderValidate(tables), Does.Contain("AttachPoint"));

            tables = CreateTables();
            SetReadonlyField(tables.TbWeaponAppearance.Get(ItemIds.ShortSword), "LocalScaleX", 0f);
            Assert.That(InvokeProviderValidate(tables), Does.Contain("LocalScale"));

            tables = CreateTables();
            SetReadonlyField(tables.TbWeaponAppearance.Get(ItemIds.ShortSword), "PrefabName", string.Empty);
            Assert.That(InvokeProviderValidate(tables), Does.Contain("PrefabName"));
        }

        private static LubanRuntimeConfigProvider CreateRealConfigProvider()
        {
            return new LubanRuntimeConfigProvider(CreateTables);
        }

        private static Tables CreateTables()
        {
            return new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", name + ".bytes"))));
        }

        private static string InvokeProviderValidate(Tables tables)
        {
            return (string)typeof(LubanRuntimeConfigProvider)
                .GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, new object[] { tables });
        }

        private static void SetReadonlyField(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public)?.SetValue(target, value);
        }
    }

    public sealed class PlayerWeaponAppearancePrefabTests
    {
        [Test]
        public void GrayboxPlayerHasWeaponAttachRoots()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            Assert.NotNull(prefab);
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var runtimeView = instance.GetComponent<PlayerView>();
                Assert.NotNull(runtimeView);
                Assert.That(runtimeView.Validate(out var error), Is.True, error);
                Assert.That(runtimeView.TryGetWeaponAttachRoot(PlayerWeaponAttachPoint.MeleeWeapon,
                    out var meleeRoot), Is.True);
                Assert.That(runtimeView.TryGetWeaponAttachRoot(PlayerWeaponAttachPoint.RangedWeapon,
                    out var rangedRoot), Is.True);
                Assert.NotNull(meleeRoot);
                Assert.NotNull(rangedRoot);
                Assert.That(runtimeView.TryGetWeaponAttachRoot(PlayerWeaponAttachPoint.LeftHandWeapon,
                    out var bowRoot), Is.True);
                Assert.That(bowRoot,
                    Is.SameAs(runtimeView.Animator.GetBoneTransform(HumanBodyBones.LeftHand)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void WeaponAppearancePrefabsCanBeInstantiated()
        {
            foreach (var path in new[]
                     {
                         "Assets/Game/Prefabs/Characters/Weapons/GrayboxShortSword.prefab",
                         "Assets/Game/Prefabs/Characters/Weapons/GrayboxBow.prefab"
                     })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.NotNull(prefab, path);
                var instance = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    instance.transform.localPosition = Vector3.one;
                    instance.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
                    instance.transform.localScale = new Vector3(1f, 2f, 3f);
                    Assert.That(instance.transform.localScale.x, Is.EqualTo(1f).Within(0.001f));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }
    }

    public sealed class WeaponSystemProjectileWeaponTests
    {
        private const int CrossbowItemId = 105;
        private const long MapInstanceId = 991;
        private string tempRoot;
        private FakePlayerPort playerPort;
        private AppearanceFakeProjectilePort projectilePort;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            playerPort = new FakePlayerPort(MapInstanceId);
            projectilePort = new AppearanceFakeProjectilePort();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(new TestConfigProvider());
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(new FakePlayerPortProvider(playerPort));
                architecture.RegisterUtility<IProjectileRuntimePortProvider>(projectilePort);
                architecture.RegisterUtility<IEnemyColliderResolver>(new NullEnemyResolver());
            };

            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayWeaponAppearanceTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(new TestConfigProvider());
            Game.Interface.RegisterUtility<IPlayerRuntimePortProvider>(new FakePlayerPortProvider(playerPort));
            Game.Interface.RegisterUtility<IProjectileRuntimePortProvider>(projectilePort);
            Game.Interface.RegisterUtility<IEnemyColliderResolver>(new NullEnemyResolver());
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(new PlayerConfigDefinition("player", string.Empty, 100, 1, 1f, 1f, 1f, 1f, 5f), false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(PlayerModel.LocalPlayerUnitId, MapInstanceId, UnitType.Player, UnitSource.Player, "player", Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out _);
            _ = Game.Interface.GetSystem<InventorySystem>();
            _ = Game.Interface.GetSystem<WeaponEquipmentSystem>();
            _ = Game.Interface.GetSystem<ProjectileSystem>();
            _ = Game.Interface.GetSystem<WeaponSystem>();
            Game.Interface.SendEvent(new PlayerReadyEvent(PlayerModel.LocalPlayerUnitId, MapInstanceId));
            CreateAndCommitSave();
        }

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = _ => { };
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void AcceptsProjectileWeaponThatIsNotBow()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(CrossbowItemId, 1, 1, out _, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(0, out error), Is.True, error);
            Assert.That(bag.TryAdd(ItemIds.Arrow, 3, 20, out _, out error), Is.True, error);
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().ActiveSlot, Is.EqualTo(WeaponSlot.Ranged));

            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));

            Assert.That(projectilePort.CreateCount, Is.EqualTo(1));
            Assert.That(projectilePort.LastPrefabAddress, Is.EqualTo("Prefabs/Combat/CrossbowBolt"));
            Assert.That(bag.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(2));
            Assert.That(playerPort.ProjectileFireCount, Is.EqualTo(1));
        }

        private void CreateAndCommitSave()
        {
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var created, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(created, out error), Is.True, error);
        }

        private sealed class TestConfigProvider : IRuntimeConfigProvider
        {
            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                if (id == CrossbowItemId)
                {
                    value = new ItemDefinition(id, "Crossbow", string.Empty, string.Empty, string.Empty, string.Empty, ItemType.Weapon, 1, ItemUseEffect.None, 0);
                    error = null;
                    return true;
                }

                if (id == ItemIds.Arrow)
                {
                    value = new ItemDefinition(id, "Arrow", string.Empty, string.Empty, string.Empty, string.Empty, ItemType.Ammunition, 20, ItemUseEffect.None, 0);
                    error = null;
                    return true;
                }

                value = default;
                error = "missing item";
                return false;
            }

            public bool TryGetWeapon(int itemId, out WeaponConfigDefinition value, out string error)
            {
                if (itemId == CrossbowItemId)
                {
                    value = new WeaponConfigDefinition(itemId, WeaponKind.Projectile, 12, 0.5f, 12f, 0f, "projectile.bolt.test", ItemIds.Arrow);
                    error = null;
                    return true;
                }

                value = default;
                error = "missing weapon";
                return false;
            }

            public bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error)
            {
                value = new ProjectileConfigDefinition(id, "Prefabs/Combat/CrossbowBolt", 20f, 16f);
                error = null;
                return true;
            }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = new MapAssetDefinition(id, id);
                error = null;
                return true;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = Array.Empty<MapSpawnDefinition>();
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = new PlayerConfigDefinition(id, string.Empty, 100, 1, 1f, 1f, 1f, 1f, 5f);
                error = null;
                return true;
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
    }

    public sealed class ProjectileCtrlPrefabCacheTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
            ResourceLoader.ResetForTests();
            Architecture<Game>.OnRegisterPatch = _ => { };
            for (var i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(objects[i]);
                }
            }
        }

        [Test]
        public void PreloadsAllProjectilePrefabAddresses()
        {
            ResetArchitecture();
            var loader = new ProjectileResourceLoader(CreateProjectilePrefab("arrow"), CreateProjectilePrefab("bolt"));
            ResourceLoader.SetLoaderForTests(loader);
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(new ProjectileConfigProvider());
                architecture.RegisterUtility<IViewInstanceProvider>(new TrackingViewInstanceProvider(objects));
            };

            var ctrl = new ProjectileCtrl(Game.Interface);
            var context = CreatePreparationContext();
            var prepare = ctrl.PrepareMapAsync(context, default).GetAwaiter().GetResult();
            Assert.That(prepare.Success, Is.True, prepare.Error);

            Assert.That(loader.LoadedAddresses, Is.EquivalentTo(new[] { "Prefabs/Combat/GrayboxArrow", "Prefabs/Combat/CrossbowBolt" }));
        }

        [Test]
        public void CreatesProjectileFromConfiguredAddress()
        {
            ResetArchitecture();
            var loader = new ProjectileResourceLoader(CreateProjectilePrefab("arrow"), CreateProjectilePrefab("bolt"));
            ResourceLoader.SetLoaderForTests(loader);
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(new ProjectileConfigProvider());
                architecture.RegisterUtility<IViewInstanceProvider>(new TrackingViewInstanceProvider(objects));
            };

            var ctrl = new ProjectileCtrl(Game.Interface);
            Assert.That(ctrl.PrepareMapAsync(CreatePreparationContext(), default).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);

            Assert.That(ctrl.TryCreateProjectile(1, 77, Vector3.zero, Vector3.forward, "Prefabs/Combat/CrossbowBolt", out var error), Is.True, error);
            Assert.That(ctrl.TryCreateProjectile(2, 77, Vector3.zero, Vector3.forward, "Prefabs/Combat/Missing", out error), Is.False);
            Assert.That(error, Does.Contain("未预加载"));
        }

        [Test]
        public void ReleasesAllPrefabCachesOnMapRelease()
        {
            ResetArchitecture();
            var loader = new ProjectileResourceLoader(CreateProjectilePrefab("arrow"), CreateProjectilePrefab("bolt"));
            ResourceLoader.SetLoaderForTests(loader);
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(new ProjectileConfigProvider());
                architecture.RegisterUtility<IViewInstanceProvider>(new TrackingViewInstanceProvider(objects));
            };

            var ctrl = new ProjectileCtrl(Game.Interface);
            Assert.That(ctrl.PrepareMapAsync(CreatePreparationContext(), default).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);

            ctrl.ReleaseCurrentMap();

            Assert.That(loader.ReleaseCount, Is.EqualTo(2));
        }

        private GameObject CreateProjectilePrefab(string name)
        {
            var prefab = new GameObject(name, typeof(BoxCollider), typeof(Rigidbody), typeof(ProjectileView));
            objects.Add(prefab);
            prefab.GetComponent<BoxCollider>().isTrigger = true;
            prefab.GetComponent<Rigidbody>().isKinematic = true;
            return prefab;
        }

        private static MapPreparationContext CreatePreparationContext()
        {
            return new MapPreparationContext(MapIds.Base, 77, CreateMapContext(), Array.Empty<MapSpawnDefinition>());
        }

        private static MapContext CreateMapContext()
        {
            var presentation = new GameObject("presentation");
            var runtime = new GameObject("runtime").transform;
            runtime.SetParent(presentation.transform, false);
            var contextObject = new GameObject("map-context");
            var context = contextObject.AddComponent<MapContext>();
            SetPrivateField(context, "mapContentRoot", presentation);
            SetPrivateField(context, "dynamicObjectRoot", runtime);
            return context;
        }
    }

    public sealed class PlayerWeaponAppearanceControllerTests
    {
        private const int BrokenRangedWeaponItemId = 106;
        private readonly List<GameObject> objects = new List<GameObject>();
        private string tempRoot;

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
            ResourceLoader.ResetForTests();
            Architecture<Game>.OnRegisterPatch = _ => { };
            for (var i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(objects[i]);
                }
            }

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void RefreshesAfterCommitAndEquipmentChanged()
        {
            var harness = CreateCommittedHarness();

            var bag = harness.Bag;
            Assert.That(bag.TryAdd(ItemIds.ShortSword, 1, 1, out _, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(0, out error), Is.True, error);
            Assert.That(harness.Loader.LoadedAddresses, Does.Contain("Prefabs/Characters/Weapons/GrayboxShortSword"));

            Assert.That(bag.TryAdd(ItemIds.Bow, 1, 1, out _, out error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(0, out error), Is.True, error);
            Assert.That(harness.Loader.LoadedAddresses, Does.Contain("Prefabs/Characters/Weapons/GrayboxBow"));
            Assert.That(harness.Instances.CreatedAppearances.Count, Is.EqualTo(2));

            harness.Controller.ReleaseCurrentMap();
            Assert.That(harness.Instances.ReleasedAppearances.Count, Is.EqualTo(2));
        }

        [Test]
        public void SwitchingWeaponSlotOnlyTogglesVisibilityWithoutDuplicatingAppearance()
        {
            var harness = CreateCommittedHarness();
            var equipment = Game.Interface.GetModel<WeaponEquipmentModel>();
            equipment.SetSlotItem(WeaponSlot.Melee, ItemIds.ShortSword);
            equipment.SetSlotItem(WeaponSlot.Ranged, ItemIds.Bow);
            Assert.That(equipment.TrySetActiveSlot(WeaponSlot.Melee, out var error), Is.True, error);
            Game.Interface.SendEvent(new WeaponEquipmentChangedEvent());

            var sword = harness.Instances.RequireAppearance("sword-appearance");
            var bow = harness.Instances.RequireAppearance("bow-appearance");
            Assert.That(harness.Instances.CreatedAppearances.Count, Is.EqualTo(2));
            Assert.That(sword.activeSelf, Is.True);
            Assert.That(bow.activeSelf, Is.False);

            Assert.That(equipment.TrySetActiveSlot(WeaponSlot.Ranged, out error), Is.True, error);
            Game.Interface.SendEvent(new WeaponEquipmentChangedEvent());

            Assert.That(harness.Instances.CreatedAppearances.Count, Is.EqualTo(2));
            Assert.That(ReferenceEquals(sword, harness.Instances.RequireAppearance("sword-appearance")), Is.True);
            Assert.That(ReferenceEquals(bow, harness.Instances.RequireAppearance("bow-appearance")), Is.True);
            Assert.That(sword.activeSelf, Is.False);
            Assert.That(bow.activeSelf, Is.True);

            Assert.That(equipment.TrySetActiveSlot(WeaponSlot.Melee, out error), Is.True, error);
            Game.Interface.SendEvent(new WeaponEquipmentChangedEvent());

            Assert.That(harness.Instances.CreatedAppearances.Count, Is.EqualTo(2));
            Assert.That(sword.activeSelf, Is.True);
            Assert.That(bow.activeSelf, Is.False);
        }

        [Test]
        public void AppearanceLoadFailureHidesSlotAndDoesNotMutateEquipmentBagOrCombat()
        {
            var harness = CreateCommittedHarness();
            var equipment = Game.Interface.GetModel<WeaponEquipmentModel>();
            equipment.SetSlotItem(WeaponSlot.Ranged, ItemIds.Bow);
            Assert.That(equipment.TrySetActiveSlot(WeaponSlot.Ranged, out var error), Is.True, error);
            Game.Interface.SendEvent(new WeaponEquipmentChangedEvent());
            var bow = harness.Instances.RequireAppearance("bow-appearance");
            Assert.That(bow.activeSelf, Is.True);

            Assert.That(harness.Bag.TryAdd(ItemIds.Arrow, 3, 20, out _, out error), Is.True, error);
            equipment.SetSlotItem(WeaponSlot.Ranged, BrokenRangedWeaponItemId);
            Assert.That(equipment.TrySetActiveSlot(WeaponSlot.Ranged, out error), Is.True, error);
            var equipmentBefore = equipment.CaptureSnapshot();
            var arrowsBefore = harness.Bag.GetTotalAmount(ItemIds.Arrow);
            var projectileCountBefore = Game.Interface.GetModel<ProjectileModel>().Projectiles.Count;
            var hpBefore = Game.Interface.GetModel<CombatModel>().PlayerHP.Value;

            LogAssert.Expect(LogType.Error, $"[PlayerCtrl] 玩家武器外观加载失败：TbWeaponAppearance[{BrokenRangedWeaponItemId}].PrefabAddress=Prefabs/Characters/Weapons/Missing");
            Game.Interface.SendEvent(new WeaponEquipmentChangedEvent());

            Assert.That(harness.Instances.ReleasedAppearances, Does.Contain(bow));
            Assert.That(harness.Instances.CreatedAppearances.Count, Is.EqualTo(1));
            Assert.That(equipment.CaptureSnapshot().RangedWeaponItemId, Is.EqualTo(equipmentBefore.RangedWeaponItemId));
            Assert.That(equipment.CaptureSnapshot().ActiveSlot, Is.EqualTo(equipmentBefore.ActiveSlot));
            Assert.That(harness.Bag.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(arrowsBefore));
            Assert.That(Game.Interface.GetModel<ProjectileModel>().Projectiles.Count, Is.EqualTo(projectileCountBefore));
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(hpBefore));
        }

        [UnityTest]
        public IEnumerator ExpiredAsyncLoadDoesNotInstantiateAfterMapRelease()
        {
            var harness = CreateCommittedHarness();
            harness.Loader.DeferredAddresses.Add("Prefabs/Characters/Weapons/GrayboxShortSword");
            var equipment = Game.Interface.GetModel<WeaponEquipmentModel>();
            equipment.SetSlotItem(WeaponSlot.Melee, ItemIds.ShortSword);
            Assert.That(equipment.TrySetActiveSlot(WeaponSlot.Melee, out var error), Is.True, error);
            Game.Interface.SendEvent(new WeaponEquipmentChangedEvent());
            Assert.That(harness.Instances.CreatedAppearances.Count, Is.Zero);

            harness.Controller.ReleaseCurrentMap();
            harness.Loader.Complete("Prefabs/Characters/Weapons/GrayboxShortSword");
            yield return null;

            Assert.That(harness.Instances.CreatedAppearances.Count, Is.Zero);
            Assert.That(harness.Loader.ReleaseCount, Is.GreaterThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator ExpiredAsyncLoadDoesNotInstantiateAfterEquipmentChanged()
        {
            var harness = CreateCommittedHarness();
            harness.Loader.DeferredAddresses.Add("Prefabs/Characters/Weapons/GrayboxShortSword");
            var equipment = Game.Interface.GetModel<WeaponEquipmentModel>();
            equipment.SetSlotItem(WeaponSlot.Melee, ItemIds.ShortSword);
            Assert.That(equipment.TrySetActiveSlot(WeaponSlot.Melee, out var error), Is.True, error);
            Game.Interface.SendEvent(new WeaponEquipmentChangedEvent());
            Assert.That(harness.Instances.CreatedAppearances.Count, Is.Zero);

            equipment.SetSlotItem(WeaponSlot.Melee, 0);
            Game.Interface.SendEvent(new WeaponEquipmentChangedEvent());
            harness.Loader.Complete("Prefabs/Characters/Weapons/GrayboxShortSword");
            yield return null;

            Assert.That(harness.Instances.CreatedAppearances.Count, Is.Zero);
            Assert.That(harness.Loader.ReleaseCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void EquipmentChangedEventDrivesAppearanceRefreshAndWeaponSystemHasNoPresentationBranch()
        {
            var harness = CreateCommittedHarness();
            var equipment = Game.Interface.GetModel<WeaponEquipmentModel>();
            equipment.SetSlotItem(WeaponSlot.Melee, ItemIds.ShortSword);
            Assert.That(equipment.TrySetActiveSlot(WeaponSlot.Melee, out var error), Is.True, error);
            Assert.That(harness.Loader.LoadedAddresses, Does.Not.Contain("Prefabs/Characters/Weapons/GrayboxShortSword"));

            Game.Interface.SendEvent(new WeaponSlotSwitchRequestedEvent(WeaponSlot.Ranged));
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Assert.That(harness.Loader.LoadedAddresses, Does.Not.Contain("Prefabs/Characters/Weapons/GrayboxShortSword"));

            Game.Interface.SendEvent(new WeaponEquipmentChangedEvent());
            Assert.That(harness.Loader.LoadedAddresses, Does.Contain("Prefabs/Characters/Weapons/GrayboxShortSword"));

            var weaponSystemSource = File.ReadAllText("Assets/Game/Scripts/System/Combat/WeaponSystem.cs");
            Assert.That(weaponSystemSource, Does.Not.Contain("SetWeaponPresentation"));
            Assert.That(weaponSystemSource, Does.Not.Contain("ItemIds.ShortSword"));
            Assert.That(weaponSystemSource, Does.Not.Contain("ItemIds.Bow"));
        }

        private void CreateAndCommitSave()
        {
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var created, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(created, out error), Is.True, error);
        }

        private ControllerHarness CreateCommittedHarness()
        {
            ResetArchitecture();
            var playerPrefab = CreatePlayerPrefab();
            var swordPrefab = new GameObject("sword-appearance");
            var bowPrefab = new GameObject("bow-appearance");
            objects.Add(swordPrefab);
            objects.Add(bowPrefab);
            var loader = new PlayerAppearanceResourceLoader(playerPrefab, swordPrefab, bowPrefab);
            var instances = new TrackingViewInstanceProvider(objects);
            ResourceLoader.SetLoaderForTests(loader);
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(new PlayerAppearanceConfigProvider());
                architecture.RegisterUtility<IViewInstanceProvider>(instances);
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(new FakePlayerPortProvider(new FakePlayerPort(88)));
                architecture.RegisterUtility<IProjectileRuntimePortProvider>(new AppearanceFakeProjectilePort());
                architecture.RegisterUtility<IEnemyColliderResolver>(new NullEnemyResolver());
            };

            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayPlayerAppearanceTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(new PlayerConfigDefinition("player", "Prefabs/Characters/TestPlayer", 100, 1, 1f, 1f, 1f, 1f, 5f), false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            _ = Game.Interface.GetSystem<UnitSystem>();
            _ = Game.Interface.GetSystem<InventorySystem>();
            _ = Game.Interface.GetSystem<WeaponEquipmentSystem>();
            _ = Game.Interface.GetSystem<ProjectileSystem>();
            _ = Game.Interface.GetSystem<WeaponSystem>();
            CreateAndCommitSave();

            var camera = new GameObject("Main Camera", typeof(Camera));
            camera.tag = "MainCamera";
            objects.Add(camera);
            var ctrl = new PlayerCtrl(Game.Interface);
            var prepare = ctrl.PrepareMapAsync(CreatePlayerPreparationContext(), default).GetAwaiter().GetResult();
            Assert.That(prepare.Success, Is.True, prepare.Error);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            return new ControllerHarness(ctrl, loader, instances, Game.Interface.GetModel<PlayerBagModel>().Container);
        }

        private GameObject CreatePlayerPrefab()
        {
            var player = new GameObject("player-prefab", typeof(CharacterController), typeof(PlayerView));
            objects.Add(player);
            var tiliRoot = new GameObject("tili-root");
            objects.Add(tiliRoot);
            tiliRoot.transform.SetParent(player.transform, false);
            var tiliFillObject = new GameObject("tili-fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            objects.Add(tiliFillObject);
            tiliFillObject.transform.SetParent(tiliRoot.transform, false);
            var meleeRoot = new GameObject("melee-root").transform;
            objects.Add(meleeRoot.gameObject);
            meleeRoot.SetParent(player.transform, false);
            var rangedRoot = new GameObject("ranged-root").transform;
            objects.Add(rangedRoot.gameObject);
            rangedRoot.SetParent(player.transform, false);
            var firePoint = new GameObject("fire-point").transform;
            objects.Add(firePoint.gameObject);
            firePoint.SetParent(rangedRoot, false);

            var view = player.GetComponent<PlayerView>();
            SetPrivateField(view, "characterController", player.GetComponent<CharacterController>());
            SetPrivateField(view, "cameraProfile", ScriptableObject.CreateInstance<TopDownCameraProfile>());
            SetPrivateField(view, "tiliRingCanvas", tiliRoot);
            SetPrivateField(view, "tiliRingFill", tiliFillObject.GetComponent<Image>());
            SetPrivateField(view, "meleeWeaponAttachRoot", meleeRoot);
            SetPrivateField(view, "weaponSwingRoot", meleeRoot);
            SetPrivateField(view, "rangedWeaponAttachRoot", rangedRoot);
            SetPrivateField(view, "projectileFirePoint", firePoint);
            return player;
        }

        private static MapPreparationContext CreatePlayerPreparationContext()
        {
            var spawn = new MapSpawnDefinition("player-spawn", MapIds.Base, MapSpawnObjectType.Player, "player", 0f, 0f, 0f, 0f);
            return new MapPreparationContext(MapIds.Base, 88, ProjectileCtrlPrefabCacheTestsHelper.CreateMapContext(), new[] { spawn });
        }

        private readonly struct ControllerHarness
        {
            public ControllerHarness(
                PlayerCtrl controller,
                PlayerAppearanceResourceLoader loader,
                TrackingViewInstanceProvider instances,
                ItemContainer bag)
            {
                Controller = controller;
                Loader = loader;
                Instances = instances;
                Bag = bag;
            }

            public PlayerCtrl Controller { get; }
            public PlayerAppearanceResourceLoader Loader { get; }
            public TrackingViewInstanceProvider Instances { get; }
            public ItemContainer Bag { get; }
        }
    }

    internal static class ProjectileCtrlPrefabCacheTestsHelper
    {
        public static MapContext CreateMapContext()
        {
            var presentation = new GameObject("presentation");
            var runtime = new GameObject("runtime").transform;
            runtime.SetParent(presentation.transform, false);
            var contextObject = new GameObject("map-context");
            var context = contextObject.AddComponent<MapContext>();
            SetPrivateField(context, "mapContentRoot", presentation);
            SetPrivateField(context, "dynamicObjectRoot", runtime);
            return context;
        }
    }

    internal sealed class PlayerAppearanceConfigProvider : MinimalRuntimeConfigProvider
    {
        public override bool TryGetItem(int id, out ItemDefinition value, out string error)
        {
            if (id == ItemIds.ShortSword || id == ItemIds.Bow || id == 106)
            {
                var name = id == ItemIds.ShortSword ? "Sword" : id == ItemIds.Bow ? "Bow" : "Broken Bow";
                value = new ItemDefinition(id, name, string.Empty, string.Empty, string.Empty, string.Empty, ItemType.Weapon, 1, ItemUseEffect.None, 0);
                error = null;
                return true;
            }

            value = default;
            error = "missing";
            return false;
        }

        public override bool TryGetWeapon(int itemId, out WeaponConfigDefinition value, out string error)
        {
            if (itemId == ItemIds.ShortSword)
            {
                value = new WeaponConfigDefinition(itemId, WeaponKind.Melee, 10, 0.45f, 2f, 90f, string.Empty, 0);
                error = null;
                return true;
            }

            if (itemId == ItemIds.Bow)
            {
                value = new WeaponConfigDefinition(itemId, WeaponKind.Projectile, 10, 0.75f, 12f, 0f, "projectile.arrow.basic", ItemIds.Arrow);
                error = null;
                return true;
            }

            if (itemId == 106)
            {
                value = new WeaponConfigDefinition(itemId, WeaponKind.Projectile, 10, 0.75f, 12f, 0f, "projectile.arrow.basic", ItemIds.Arrow);
                error = null;
                return true;
            }

            value = default;
            error = "missing";
            return false;
        }

        public override bool TryGetPlayerWeaponAppearance(int itemId, out PlayerWeaponAppearanceDefinition value, out string error)
        {
            if (itemId == ItemIds.ShortSword)
            {
                value = new PlayerWeaponAppearanceDefinition(itemId, "Prefabs/Characters/Weapons/GrayboxShortSword", PlayerWeaponAttachPoint.MeleeWeapon, Vector3.zero, Vector3.zero, Vector3.one);
                error = null;
                return true;
            }

            if (itemId == ItemIds.Bow)
            {
                value = new PlayerWeaponAppearanceDefinition(itemId, "Prefabs/Characters/Weapons/GrayboxBow", PlayerWeaponAttachPoint.RangedWeapon, Vector3.zero, Vector3.zero, Vector3.one);
                error = null;
                return true;
            }

            if (itemId == 106)
            {
                value = new PlayerWeaponAppearanceDefinition(itemId, "Prefabs/Characters/Weapons/Missing", PlayerWeaponAttachPoint.RangedWeapon, Vector3.zero, Vector3.zero, Vector3.one);
                error = null;
                return true;
            }

            value = default;
            error = "missing";
            return false;
        }

        public override bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
        {
            value = new PlayerConfigDefinition(id, "Prefabs/Characters/TestPlayer", 100, 1, 1f, 1f, 1f, 1f, 5f);
            error = null;
            return true;
        }
    }

    internal sealed class ProjectileConfigProvider : MinimalRuntimeConfigProvider
    {
        private static readonly ProjectileConfigDefinition[] Projectiles =
        {
            new ProjectileConfigDefinition("projectile.arrow.basic", "Prefabs/Combat/GrayboxArrow", 14f, 14f),
            new ProjectileConfigDefinition("projectile.bolt.test", "Prefabs/Combat/CrossbowBolt", 20f, 16f)
        };

        public override bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error)
        {
            for (var i = 0; i < Projectiles.Length; i++)
            {
                if (Projectiles[i].Id == id)
                {
                    value = Projectiles[i];
                    error = null;
                    return true;
                }
            }

            value = default;
            error = "missing projectile";
            return false;
        }

        public override bool TryGetProjectiles(out IReadOnlyList<ProjectileConfigDefinition> values, out string error)
        {
            values = Projectiles;
            error = null;
            return true;
        }
    }

    internal sealed class PlayerAppearanceResourceLoader : IResourceLoader
    {
        private readonly GameObject playerPrefab;
        private readonly GameObject swordPrefab;
        private readonly GameObject bowPrefab;
        private readonly Dictionary<string, List<Action<GameObject>>> deferredLoads = new Dictionary<string, List<Action<GameObject>>>();

        public PlayerAppearanceResourceLoader(GameObject playerPrefab, GameObject swordPrefab, GameObject bowPrefab)
        {
            this.playerPrefab = playerPrefab;
            this.swordPrefab = swordPrefab;
            this.bowPrefab = bowPrefab;
        }

        public List<string> LoadedAddresses { get; } = new List<string>();
        public HashSet<string> DeferredAddresses { get; } = new HashSet<string>();
        public int ReleaseCount { get; private set; }

        public T LoadAsset<T>(string path) where T : UnityEngine.Object
        {
            return Select(path) as T;
        }

        public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
        {
            LoadedAddresses.Add(path);
            if (DeferredAddresses.Contains(path) && typeof(T) == typeof(GameObject))
            {
                if (!deferredLoads.TryGetValue(path, out var callbacks))
                {
                    callbacks = new List<Action<GameObject>>();
                    deferredLoads.Add(path, callbacks);
                }

                callbacks.Add(prefab => onLoaded?.Invoke(prefab as T));
                return new ResourceLoadHandle(new object());
            }

            onLoaded?.Invoke(Select(path) as T);
            return new ResourceLoadHandle(new object());
        }

        public void Release(ResourceLoadHandle handle)
        {
            if (handle.IsValid)
            {
                ReleaseCount++;
            }
        }

        public void Complete(string path)
        {
            if (!deferredLoads.TryGetValue(path, out var callbacks))
            {
                return;
            }

            deferredLoads.Remove(path);
            var prefab = Select(path);
            for (var i = 0; i < callbacks.Count; i++)
            {
                callbacks[i]?.Invoke(prefab);
            }
        }

        private GameObject Select(string path)
        {
            if (path == "Prefabs/Characters/TestPlayer") return playerPrefab;
            if (path == "Prefabs/Characters/Weapons/GrayboxShortSword") return swordPrefab;
            if (path == "Prefabs/Characters/Weapons/GrayboxBow") return bowPrefab;
            return null;
        }
    }

    internal sealed class ProjectileResourceLoader : IResourceLoader
    {
        private readonly GameObject arrowPrefab;
        private readonly GameObject boltPrefab;

        public ProjectileResourceLoader(GameObject arrowPrefab, GameObject boltPrefab)
        {
            this.arrowPrefab = arrowPrefab;
            this.boltPrefab = boltPrefab;
        }

        public List<string> LoadedAddresses { get; } = new List<string>();
        public int ReleaseCount { get; private set; }

        public T LoadAsset<T>(string path) where T : UnityEngine.Object
        {
            return Select(path) as T;
        }

        public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
        {
            LoadedAddresses.Add(path);
            onLoaded?.Invoke(Select(path) as T);
            return new ResourceLoadHandle(new object());
        }

        public void Release(ResourceLoadHandle handle)
        {
            if (handle.IsValid)
            {
                ReleaseCount++;
            }
        }

        private GameObject Select(string path)
        {
            if (path == "Prefabs/Combat/GrayboxArrow") return arrowPrefab;
            if (path == "Prefabs/Combat/CrossbowBolt") return boltPrefab;
            return null;
        }
    }

    internal sealed class TrackingViewInstanceProvider : IViewInstanceProvider
    {
        private readonly List<GameObject> objects;

        public TrackingViewInstanceProvider(List<GameObject> objects)
        {
            this.objects = objects;
        }

        public List<GameObject> Created { get; } = new List<GameObject>();
        public List<GameObject> CreatedAppearances { get; } = new List<GameObject>();
        public List<GameObject> ReleasedAppearances { get; } = new List<GameObject>();
        public int ReleaseCount { get; private set; }

        public T Instantiate<T>(T prefab, Transform parent, Vector3 position, Quaternion rotation) where T : Component
        {
            var instance = UnityEngine.Object.Instantiate(prefab, position, rotation, parent);
            Created.Add(instance.gameObject);
            objects.Add(instance.gameObject);
            if (prefab.name.Contains("appearance"))
            {
                CreatedAppearances.Add(instance.gameObject);
            }

            return instance;
        }

        public void Release(GameObject instance)
        {
            ReleaseCount++;
            if (instance != null)
            {
                if (instance.name.Contains("appearance"))
                {
                    ReleasedAppearances.Add(instance);
                }

                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        public GameObject RequireAppearance(string prefabName)
        {
            var instance = CreatedAppearances.LastOrDefault(go => go != null && go.name.StartsWith(prefabName, StringComparison.Ordinal));
            Assert.NotNull(instance, prefabName);
            return instance;
        }
    }

    internal sealed class FakePlayerPort : ICharacterMotorPort
    {
        public FakePlayerPort(long mapInstanceId)
        {
            MapInstanceId = mapInstanceId;
        }

        public int UnitId => PlayerModel.LocalPlayerUnitId;
        public long MapInstanceId { get; }
        public bool IsValid => true;
        public bool IsEnabled => true;
        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; } = Quaternion.identity;
        public float MovementYawDegrees => 0f;
        public bool IsGrounded => true;
        public int ProjectileFireCount { get; private set; }
        public void Move(Vector3 motion) { Position += motion; }
        public void RotateTowards(Quaternion rotation, float maxDegreesDelta) { Rotation = rotation; }
        public void FaceDirection(Vector3 direction) { Rotation = Quaternion.LookRotation(direction.normalized, Vector3.up); }
        public void SetTiliRing(float current, float maximum, bool isSprinting, bool exhausted) { }
        public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin) { origin = Position + Vector3.up; return true; }
        public void FaceWorldHud(Vector3 cameraPosition, Vector3 cameraUp) { }
        public void PlayAttackFeedback() { }
        public void PlayWeaponSwingFeedback() { }
        public void PlayProjectileFireFeedback() { ProjectileFireCount++; }
        public void PlayHurtFeedback() { }
        public void TickCombatFeedback(float deltaTime) { }
    }

    internal sealed class FakePlayerPortProvider : IPlayerRuntimePortProvider
    {
        private readonly FakePlayerPort port;
        public FakePlayerPortProvider(FakePlayerPort port) { this.port = port; }
        public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort motorPort)
        {
            motorPort = unitId == port.UnitId && mapInstanceId == port.MapInstanceId ? port : null;
            return motorPort != null;
        }

        public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort cameraPort)
        {
            cameraPort = unitId == port.UnitId && mapInstanceId == port.MapInstanceId
                ? new FakeCameraPort()
                : null;
            return cameraPort != null;
        }

        public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider) { return false; }
    }

    internal sealed class AppearanceFakeProjectilePort : IProjectileRuntimePortProvider
    {
        public int CreateCount { get; private set; }
        public string LastPrefabAddress { get; private set; }
        public bool TryCreateProjectile(int projectileRuntimeId, long mapInstanceId, Vector3 position, Vector3 direction, string prefabAddress, out string error)
        {
            CreateCount++;
            LastPrefabAddress = prefabAddress;
            error = null;
            return true;
        }

        public void MoveProjectile(int projectileRuntimeId, Vector3 position, Vector3 direction) { }
        public void CollectMovementHits(int projectileRuntimeId, Vector3 from, Vector3 to, List<ProjectileMovementHit> results) { results.Clear(); }
        public void ReleaseProjectile(int projectileRuntimeId) { }
    }

    internal sealed class FakeCameraPort : IPlayerCameraPort
    {
        public bool IsValid => true;
        public void ApplyProfile() { }
        public void LateUpdate() { }
        public bool TryGetCameraPose(out Vector3 position, out Vector3 up)
        {
            position = Vector3.back;
            up = Vector3.up;
            return true;
        }
    }

    internal sealed class NullEnemyResolver : IEnemyColliderResolver
    {
        public bool TryResolveEnemyUnitId(Collider collider, out int unitId)
        {
            unitId = 0;
            return false;
        }
    }

    internal abstract class MinimalRuntimeConfigProvider : IRuntimeConfigProvider
    {
        public virtual bool TryGetItem(int id, out ItemDefinition value, out string error)
        {
            value = default;
            error = "missing item";
            return false;
        }

        public virtual bool TryGetWeapon(int itemId, out WeaponConfigDefinition value, out string error)
        {
            value = default;
            error = "missing weapon";
            return false;
        }

        public virtual bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error)
        {
            value = default;
            error = "missing projectile";
            return false;
        }

        public virtual bool TryGetProjectiles(out IReadOnlyList<ProjectileConfigDefinition> values, out string error)
        {
            values = Array.Empty<ProjectileConfigDefinition>();
            error = "missing projectiles";
            return false;
        }

        public virtual bool TryGetPlayerWeaponAppearance(int itemId, out PlayerWeaponAppearanceDefinition value, out string error)
        {
            value = default;
            error = "missing player weapon appearance";
            return false;
        }

        public virtual bool TryGetMap(string id, out MapAssetDefinition value, out string error)
        {
            value = new MapAssetDefinition(id, id);
            error = null;
            return true;
        }

        public virtual bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
        {
            values = Array.Empty<MapSpawnDefinition>();
            error = null;
            return true;
        }

        public virtual bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
        {
            value = default;
            error = "missing player";
            return false;
        }

        public virtual bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
        {
            value = default;
            error = "missing enemy";
            return false;
        }

        public virtual bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
        {
            value = default;
            error = "missing loot";
            return false;
        }

        public virtual bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
        {
            value = default;
            error = "missing extraction";
            return false;
        }
    }

    internal static class PlayerWeaponAppearanceTestHelpers
    {
        public static void SetPrivateField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
        {
            typeof(TTarget).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);
        }

        public static void ResetArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }
    }
}
