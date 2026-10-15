using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Luban;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class WeaponEquipmentTests
    {
        private string tempRoot;
        private TestMapContentLoader mapLoader;
        private readonly List<GameObject> objects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            mapLoader = new TestMapContentLoader(objects);
            ResourceLoader.SetLoaderForTests(new TestResourceLoader(CreatePlayerPrefab(), CreateProjectilePrefab()));
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(new TestConfigProvider());
                architecture.RegisterUtility<IMapContentLoader>(mapLoader);
            };

            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayWeaponEquipmentTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 1f, 1f, 5f),
                false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            _ = Game.Interface.GetSystem<InventorySystem>();
            _ = Game.Interface.GetSystem<WeaponEquipmentSystem>();
        }

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

            objects.Clear();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void ConfigIdentitiesAreFixed()
        {
            var provider = CreateRealConfigProvider();
            Assert.That(provider.TryGetItem(ItemIds.ShortSword, out var sword, out var error), Is.True, error);
            Assert.That(sword.ItemType, Is.EqualTo(ItemType.Weapon));
            Assert.That(sword.MaxStackAmount, Is.EqualTo(1));
            Assert.That(sword.UseEffect, Is.EqualTo(ItemUseEffect.None));
            Assert.That(provider.TryGetItem(ItemIds.Bow, out var bow, out error), Is.True, error);
            Assert.That(bow.ItemType, Is.EqualTo(ItemType.Weapon));
            Assert.That(bow.MaxStackAmount, Is.EqualTo(1));
            Assert.That(provider.TryGetItem(ItemIds.Arrow, out var arrow, out error), Is.True, error);
            Assert.That(arrow.ItemType, Is.EqualTo(ItemType.Ammunition));
            Assert.That(arrow.MaxStackAmount, Is.EqualTo(20));

            Assert.That(provider.TryGetWeapon(ItemIds.ShortSword, out var swordWeapon, out error), Is.True, error);
            Assert.That(swordWeapon.WeaponKind, Is.EqualTo(WeaponKind.Melee));
            Assert.That(swordWeapon.AttackDamage, Is.EqualTo(10));
            Assert.That(swordWeapon.AttackCooldownSeconds, Is.EqualTo(0.45f).Within(0.001f));
            Assert.That(swordWeapon.AttackRange, Is.EqualTo(2.2f).Within(0.001f));
            Assert.That(swordWeapon.MeleeArcDegrees, Is.EqualTo(90f).Within(0.001f));
            Assert.That(swordWeapon.ProjectileConfigId, Is.Empty);
            Assert.That(swordWeapon.AmmoItemId, Is.EqualTo(0));

            Assert.That(provider.TryGetWeapon(ItemIds.Bow, out var bowWeapon, out error), Is.True, error);
            Assert.That(bowWeapon.WeaponKind, Is.EqualTo(WeaponKind.Projectile));
            Assert.That(bowWeapon.ProjectileConfigId, Is.EqualTo("projectile.arrow.basic"));
            Assert.That(bowWeapon.AmmoItemId, Is.EqualTo(ItemIds.Arrow));
        }

        [Test]
        public void WeaponConfigRejectsInvalidRows()
        {
            var tables = CreateTables();
            var row = tables.TbWeapon.Get(ItemIds.Bow);
            typeof(global::WeaponConfig).GetField("ItemId")?.SetValue(row, 0);
            var error = InvokeProviderValidate(tables);
            Assert.That(error, Does.Contain("TbWeapon"));
            Assert.That(error, Does.Contain("ItemId"));

            tables = CreateTables();
            row = tables.TbWeapon.Get(ItemIds.Bow);
            typeof(global::WeaponConfig).GetField("AmmoItemId")?.SetValue(row, ItemIds.MetalParts);
            error = InvokeProviderValidate(tables);
            Assert.That(error, Does.Contain("AmmoItemId"));

            tables = CreateTables();
            row = tables.TbWeapon.Get(ItemIds.ShortSword);
            typeof(global::WeaponConfig).GetField("MeleeArcDegrees")?.SetValue(row, 0f);
            error = InvokeProviderValidate(tables);
            Assert.That(error, Does.Contain("MeleeArcDegrees"));
        }

        [Test]
        public void SaveRoundTripAndValidation()
        {
            var model=Game.Interface.GetModel<WeaponEquipmentModel>();
            var empty=new JObject{["schemaVersion"]=1,["sourceContainerKind"]=-1,["sourceSlotIndex"]=-1,["itemId"]=0};
            Assert.That(model.Validate(empty,out var error),Is.True,error);model.Restore(empty);
            Assert.That(model.HeldSource,Is.Null);Assert.That(model.ActiveSlot,Is.EqualTo(WeaponSlot.Melee));
            var bag=Game.Interface.GetModel<PlayerBagModel>();bag.Pocket.TryAdd(ItemIds.Bow,1,1,out _,out _);
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TryHold(new InventorySlotRef(InventoryContainerKind.Pocket,0),ItemIds.Bow,out error),Is.True,error);
            var saved=(JObject)model.Capture();Assert.That(model.Validate(saved,out error),Is.True,error);model.Reset();model.Restore(saved);
            Game.Interface.GetSystem<WeaponEquipmentSystem>().ValidateSourceAfterInventoryMutation();
            Assert.That(model.RangedWeaponItemId,Is.EqualTo(ItemIds.Bow));Assert.That(model.MeleeWeaponItemId,Is.Zero);
            Assert.That(saved["meleeWeaponItemId"],Is.Null);Assert.That(saved["rangedWeaponItemId"],Is.Null);
            Assert.That(model.Validate(Mutate(saved,"sourceContainerKind",(int)InventoryContainerKind.Warehouse),out _),Is.False);
            Assert.That(model.Validate(Mutate(saved,"itemId",ItemIds.Arrow),out _),Is.False);
            Assert.That(model.Validate(Mutate(saved,"sourceSlotIndex",-1),out _),Is.False);
        }

        [Test]
        public void RangedLoadStaysOnInventoryInstanceAndRiskLossDestroysIt()
        {
            var bag=Game.Interface.GetModel<PlayerBagModel>();var equipment=Game.Interface.GetModel<WeaponEquipmentModel>();var system=Game.Interface.GetSystem<WeaponEquipmentSystem>();
            bag.Container.TryAdd(ItemIds.Bow,1,1,out _,out _);Assert.That(system.TryHold(new InventorySlotRef(InventoryContainerKind.PlayerBag,0),ItemIds.Bow,out var error),Is.True,error);equipment.TrySetRangedLoadedAmmo(1,out _);
            var inventorySave=bag.Capture();var heldSave=equipment.Capture();bag.Reset();equipment.Reset();bag.Restore(inventorySave);equipment.Restore(heldSave);system.ValidateSourceAfterInventoryMutation();
            Assert.That(equipment.RangedLoadedAmmo,Is.EqualTo(1));Assert.That(system.TryHolster(out error),Is.True,error);Assert.That(bag.Container.Slots[0].LoadedAmmo,Is.EqualTo(1));
            Assert.That(system.TryHold(new InventorySlotRef(InventoryContainerKind.PlayerBag,0),ItemIds.Bow,out error),Is.True,error);Assert.That(equipment.RangedLoadedAmmo,Is.EqualTo(1));
            Game.Interface.GetSystem<InventorySystem>().ClearPlayerBag();system.ClearForRiskLoss();system.ClearForRiskLoss();Assert.That(equipment.HeldItemId,Is.Zero);Assert.That(bag.Container.Slots,Is.Empty);
        }

        [Test]
        public void TransactionsAreAtomic()
        {
            var bag=Game.Interface.GetModel<PlayerBagModel>().Container;var model=Game.Interface.GetModel<WeaponEquipmentModel>();var system=Game.Interface.GetSystem<WeaponEquipmentSystem>();
            bag.TryAdd(ItemIds.ShortSword,1,1,out _,out _);bag.TryAdd(ItemIds.Bow,1,1,out _,out _);
            int inventoryEvents=0,heldEvents=0;Game.Interface.RegisterEvent<InventoryChangedEvent>(_=>inventoryEvents++);Game.Interface.RegisterEvent<WeaponEquipmentChangedEvent>(_=>heldEvents++);
            Assert.That(system.TryHold(new InventorySlotRef(InventoryContainerKind.PlayerBag,0),ItemIds.ShortSword,out var error),Is.True,error);
            Assert.That(system.TryHold(new InventorySlotRef(InventoryContainerKind.PlayerBag,1),ItemIds.Bow,out error),Is.True,error);
            var before=model.Capture().ToString();Assert.That(system.TryHold(new InventorySlotRef(InventoryContainerKind.PlayerBag,1),ItemIds.ShortSword,out _),Is.False);Assert.That(model.Capture().ToString(),Is.EqualTo(before));
            Assert.That(bag.Slots.Count,Is.EqualTo(2));Assert.That(inventoryEvents,Is.Zero);Assert.That(heldEvents,Is.EqualTo(2));
        }

        [Test]
        public void ReplacementPreservesBothInventoryInstancesAndSendsOnlyHeldEvent()
        {
            var bag=Game.Interface.GetModel<PlayerBagModel>().Container;var model=Game.Interface.GetModel<WeaponEquipmentModel>();var system=Game.Interface.GetSystem<WeaponEquipmentSystem>();bag.TryAdd(ItemIds.ShortSword,2,1,out _,out _);
            system.TryEquipFromPlayerBag(0,out _);int inventoryEvents=0,heldEvents=0;Game.Interface.RegisterEvent<InventoryChangedEvent>(_=>inventoryEvents++);Game.Interface.RegisterEvent<WeaponEquipmentChangedEvent>(_=>heldEvents++);
            Assert.That(system.TryEquipFromPlayerBag(1,out var error),Is.True,error);Assert.That(model.HeldSource.Value.SlotIndex,Is.EqualTo(1));Assert.That(bag.GetTotalAmount(ItemIds.ShortSword),Is.EqualTo(2));Assert.That(inventoryEvents,Is.Zero);Assert.That(heldEvents,Is.EqualTo(1));
        }

        [Test]
        public void HeldItemRejectsWarehouseSourceWithoutMutation()
        {
            var warehouse=Game.Interface.GetModel<WarehouseModel>().Container;warehouse.TryAdd(ItemIds.ShortSword,1,1,out _,out _);var system=Game.Interface.GetSystem<WeaponEquipmentSystem>();var before=SnapshotText(warehouse);
            Assert.That(system.TryEquipFromSlot(new InventorySlotRef(InventoryContainerKind.Warehouse,0),WeaponSlot.Melee,out _),Is.False);Assert.That(SnapshotText(warehouse),Is.EqualTo(before));Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId,Is.Zero);
        }

        [TestCase("Loading")]
        [TestCase("Paused")]
        [TestCase("EnteringExpedition")]
        [TestCase("Returning")]
        [TestCase("RecoveryRequired")]
        [TestCase("Defeated")]
        [TestCase("Dead")]
        public void EquipmentOperationsRejectBlockedStatesWithoutMutationEventsOrDirty(string state)
        {
            CreateCurrentSave();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.ShortSword, 1, 1, out _, out var error), Is.True, error);
            ApplyBlockedState(state);
            var equipment = Game.Interface.GetModel<WeaponEquipmentModel>();
            var beforeBag = SnapshotText(bag);
            var beforeEquipment = equipment.Capture().ToString();
            var beforeDirty = IsSaveDirty();
            var inventoryEvents = 0;
            var equipmentEvents = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => inventoryEvents++);
            Game.Interface.RegisterEvent<WeaponEquipmentChangedEvent>(_ => equipmentEvents++);

            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(0, out error), Is.False);

            Assert.That(SnapshotText(bag), Is.EqualTo(beforeBag));
            Assert.That(equipment.Capture().ToString(), Is.EqualTo(beforeEquipment));
            Assert.That(inventoryEvents, Is.EqualTo(0));
            Assert.That(equipmentEvents, Is.EqualTo(0));
            Assert.That(IsSaveDirty(), Is.EqualTo(beforeDirty));
        }

        [Test]
        public void ActiveSlotSwitchingValidatesSlotsAndDoesNotMoveItems()
        {
            var bag=Game.Interface.GetModel<PlayerBagModel>().Container;var system=Game.Interface.GetSystem<WeaponEquipmentSystem>();var model=Game.Interface.GetModel<WeaponEquipmentModel>();bag.TryAdd(ItemIds.ShortSword,2,1,out _,out _);bag.TryAdd(ItemIds.Bow,1,1,out _,out _);var before=SnapshotText(bag);
            Assert.That(system.TrySetActiveSlot(WeaponSlot.Melee,out var error),Is.True,error);Assert.That(model.HeldSource.Value.SlotIndex,Is.EqualTo(0));
            system.TrySetActiveSlot(WeaponSlot.Melee,out _);Assert.That(model.HeldSource.Value.SlotIndex,Is.EqualTo(1));system.TrySetActiveSlot(WeaponSlot.Ranged,out _);Assert.That(model.HeldItemId,Is.EqualTo(ItemIds.Bow));Assert.That(model.MeleeWeaponItemId,Is.Zero);
            Assert.That(system.TrySetActiveSlot(WeaponSlot.None,out error),Is.True,error);Assert.That(model.HeldItemId,Is.Zero);Assert.That(SnapshotText(bag),Is.EqualTo(before));
        }

        [Test]
        public void RiskLossClearsBagAndEquipmentOnly()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            var equipment = Game.Interface.GetModel<WeaponEquipmentModel>();
            Assert.That(bag.TryAdd(ItemIds.ShortSword, 1, 1, out _, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(0, out error), Is.True, error);
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 2, 20, out _, out error), Is.True, error);
            Assert.That(warehouse.TryAdd(ItemIds.Cloth, 3, 20, out _, out error), Is.True, error);
            var warehouseBefore = SnapshotText(warehouse);

            Game.Interface.GetSystem<InventorySystem>().ClearPlayerBag();
            Game.Interface.GetSystem<WeaponEquipmentSystem>().ClearForRiskLoss();

            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.Slots.Count, Is.EqualTo(0));
            Assert.That(equipment.MeleeWeaponItemId, Is.EqualTo(0));
            Assert.That(equipment.RangedWeaponItemId, Is.EqualTo(0));
            Assert.That(equipment.ActiveSlot, Is.EqualTo(WeaponSlot.Melee));
            Game.Interface.GetSystem<WeaponEquipmentSystem>().ClearForRiskLoss();
            Assert.That(equipment.ActiveSlot, Is.EqualTo(WeaponSlot.Melee));
            Assert.That(SnapshotText(warehouse), Is.EqualTo(warehouseBefore));
        }

        [Test]
        public void DeathFlowLoadFailureKeepsEquipmentThenSuccessClearsOnceAndPreservesWarehouse()
        {
            CreateCurrentSave();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            var equipment = Game.Interface.GetModel<WeaponEquipmentModel>();
            var weaponSystem = Game.Interface.GetSystem<WeaponEquipmentSystem>();
            Assert.That(bag.TryAdd(ItemIds.ShortSword, 1, 1, out _, out var error), Is.True, error);
            Assert.That(weaponSystem.TryEquipFromPlayerBag(0, out error), Is.True, error);
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 2, 20, out _, out error), Is.True, error);
            Assert.That(warehouse.TryAdd(ItemIds.Cloth, 3, 20, out _, out error), Is.True, error);
            var warehouseBefore = SnapshotText(warehouse);

            var flow = Game.Interface.GetSystem<ExpeditionFlowSystem>();
            LogAssert.Expect(LogType.Error, new Regex(@"\[PlayerCtrl\] 玩家武器外观配置缺失"));
            var entered = flow.EnterSurvivalAsync(MapIds.SurvivalSample).GetAwaiter().GetResult();
            Assert.That(entered.Success, Is.True, entered.Error);
            Game.Interface.GetModel<ExpeditionModel>().MarkDefeated();
            mapLoader.FailNextLoad = true;

            LogAssert.Expect(LogType.Error, new Regex(@"\[UIRoot\] 缺少必需场景对象：UIRoot"));
            var failed = flow.FailExpeditionAndReturnToBaseAsync().GetAwaiter().GetResult();

            Assert.That(failed.Success, Is.False);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(0), failed.Error);
            Assert.That(equipment.MeleeWeaponItemId, Is.EqualTo(0));
            Assert.That(SnapshotText(warehouse), Is.EqualTo(warehouseBefore));

            var repeatWarehouse = SnapshotText(warehouse);
            var repeated = flow.FailExpeditionAndReturnToBaseAsync().GetAwaiter().GetResult();
            Assert.That(repeated.Success, Is.False);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.Slots.Count, Is.EqualTo(0));
            Assert.That(equipment.ActiveSlot, Is.EqualTo(WeaponSlot.Melee));
            Assert.That(SnapshotText(warehouse), Is.EqualTo(repeatWarehouse));
        }

        [Test]
        public void DeathFlowSaveFailureRecoveryDoesNotRestoreOrDuplicateEquipment()
        {
            CreateCurrentSave();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            var equipment = Game.Interface.GetModel<WeaponEquipmentModel>();
            Assert.That(bag.TryAdd(ItemIds.ShortSword, 1, 1, out _, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(0, out error), Is.True, error);
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 2, 20, out _, out error), Is.True, error);
            Assert.That(warehouse.TryAdd(ItemIds.Cloth, 3, 20, out _, out error), Is.True, error);
            var warehouseBefore = SnapshotText(warehouse);
            var flow = Game.Interface.GetSystem<ExpeditionFlowSystem>();
            LogAssert.Expect(LogType.Error, new Regex(@"\[PlayerCtrl\] 玩家武器外观配置缺失"));
            Assert.That(flow.EnterSurvivalAsync(MapIds.SurvivalSample).GetAwaiter().GetResult().Success, Is.True);
            Game.Interface.GetModel<ExpeditionModel>().MarkDefeated();
            var blocked = Path.Combine(tempRoot, "blocked-save-root");
            File.WriteAllText(blocked, "not a directory");
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(blocked);
            var failed = flow.FailExpeditionAndReturnToBaseAsync().GetAwaiter().GetResult();

            Assert.That(failed.Success, Is.False);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState, Is.EqualTo(PlayerBagRiskState.InExpedition));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(2));
            Assert.That(equipment.MeleeWeaponItemId, Is.EqualTo(ItemIds.ShortSword));
            Assert.That(SnapshotText(warehouse), Is.EqualTo(warehouseBefore));

            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot);
            Assert.That(flow.FailExpeditionAndReturnToBaseAsync().GetAwaiter().GetResult().Success, Is.True);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.Slots.Count, Is.EqualTo(0));
            Assert.That(equipment.MeleeWeaponItemId, Is.EqualTo(0));
            Assert.That(equipment.ActiveSlot, Is.EqualTo(WeaponSlot.Melee));
            Assert.That(SnapshotText(warehouse), Is.EqualTo(warehouseBefore));
        }

        private void FillBagWithClothExceptEquippedWeapon()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            bag.RestoreSnapshot(new ItemContainer(InventoryDefaults.PlayerBagCapacity).CaptureSnapshot());
            for (var i = 0; i < InventoryDefaults.PlayerBagCapacity; i++)
            {
                Assert.That(bag.TryAdd(ItemIds.Cloth, 20, 20, out _, out var error), Is.True, error);
            }
        }

        private static void CreateCurrentSave()
        {
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var created, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(created, out error), Is.True, error);
        }

        private static bool IsSaveDirty()
        {
            return (bool)typeof(SaveSystem)
                .GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(Game.Interface.GetSystem<SaveSystem>());
        }

        private static void ApplyBlockedState(string state)
        {
            switch (state)
            {
                case "Loading":
                    Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
                    break;
                case "Paused":
                    Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
                    break;
                case "EnteringExpedition":
                    Assert.That(Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.EnteringExpedition, "test", false, out var enteringError), Is.True, enteringError);
                    break;
                case "Returning":
                    EnterExpeditionState(ExpeditionStatus.Returning);
                    break;
                case "RecoveryRequired":
                    Assert.That(Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.RecoveryRequired, "test", false, out var recoveryError), Is.True, recoveryError);
                    break;
                case "Defeated":
                    EnterExpeditionState(ExpeditionStatus.Defeated);
                    break;
                case "Dead":
                    Game.Interface.GetModel<CombatModel>().SetPlayerHP(0);
                    break;
                default:
                    Assert.Fail("Unknown blocked state: " + state);
                    break;
            }
        }

        private static void EnterExpeditionState(ExpeditionStatus status)
        {
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            var expedition = Game.Interface.GetModel<ExpeditionModel>();
            expedition.BeginSurvival(MapIds.SurvivalSample, 60f);
            if (status == ExpeditionStatus.Returning)
            {
                expedition.QualifyReturn();
                expedition.BeginReturning();
            }
            else if (status == ExpeditionStatus.Defeated)
            {
                expedition.MarkDefeated();
            }

            Assert.That(Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out var error), Is.True, error);
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
            var meleeAttach = new GameObject("melee-attach").transform;
            objects.Add(meleeAttach.gameObject);
            meleeAttach.SetParent(player.transform, false);
            var rangedAttach = new GameObject("ranged-attach").transform;
            objects.Add(rangedAttach.gameObject);
            rangedAttach.SetParent(player.transform, false);

            var view = player.GetComponent<PlayerView>();
            var flashlight = new GameObject("flashlight", typeof(Light));
            flashlight.transform.SetParent(player.transform, false);
            flashlight.GetComponent<Light>().type = LightType.Spot;
            SetPrivateField(view, "flashlight", flashlight.GetComponent<Light>());
            var emitter = player.AddComponent<DamageNumberEmitter>();
            SetPrivateField(emitter, "numberPrefab", UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/WorldHud/DamageNumberBox.prefab").GetComponent<DamageNumberView>());
            SetPrivateField(view, "damageNumberEmitter", emitter);
            SetPrivateField(view, "characterController", player.GetComponent<CharacterController>());
            SetPrivateField(view, "cameraProfile", ScriptableObject.CreateInstance<TopDownCameraProfile>());
            SetPrivateField(view, "tiliRingCanvas", tiliRoot);
            SetPrivateField(view, "tiliRingFill", tiliFillObject.GetComponent<Image>());
            SetPrivateField(view, "meleeWeaponAttachRoot", meleeAttach);
            SetPrivateField(view, "rangedWeaponAttachRoot", rangedAttach);
            return player;
        }

        private GameObject CreateProjectilePrefab()
        {
            var projectile = new GameObject("projectile-prefab", typeof(BoxCollider), typeof(Rigidbody), typeof(ProjectileView));
            objects.Add(projectile);
            projectile.GetComponent<BoxCollider>().isTrigger = true;
            projectile.GetComponent<Rigidbody>().isKinematic = true;
            return projectile;
        }

        private static MapContext CreateMapContext(string mapId)
        {
            var root = new GameObject("map-root-" + mapId);
            var runtime = new GameObject("runtime-root").transform;
            runtime.SetParent(root.transform, false);
            var contextObject = new GameObject("map-context-" + mapId);
            var context = contextObject.AddComponent<MapContext>();
            SetPrivateField(context, "mapContentRoot", root);
            SetPrivateField(context, "dynamicObjectRoot", runtime);
            return context;
        }

        private static void SetPrivateField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
        {
            typeof(TTarget)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        private static JObject Mutate(JObject source, string key, object value)
        {
            var clone = (JObject)source.DeepClone();
            clone[key] = value == null ? JValue.CreateNull() : JToken.FromObject(value);
            return clone;
        }

        private static string SnapshotText(ItemContainer container)
        {
            return string.Join("|", container.Slots.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value.ItemId}:{pair.Value.Amount}"));
        }

        private static LubanRuntimeConfigProvider CreateRealConfigProvider()
        {
            return new LubanRuntimeConfigProvider(CreateTables);
        }

        private static Tables CreateTables()
        {
            return new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", $"{name}.bytes"))));
        }

        private static string InvokeProviderValidate(Tables tables)
        {
            return (string)typeof(LubanRuntimeConfigProvider)
                .GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, new object[] { tables });
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private sealed class TestMapContentLoader : IMapContentLoader
        {
            private readonly List<GameObject> objects;

            public TestMapContentLoader(List<GameObject> objects)
            {
                this.objects = objects;
            }

            public bool FailNextLoad { get; set; }
            public List<string> LoadedMapIds { get; } = new List<string>();

            public UniTask<MapContentLoadResult> LoadAsync(
                string mapId,
                string address,
                Action<float> onProgress,
                CancellationToken cancellationToken = default)
            {
                LoadedMapIds.Add(mapId);
                if (FailNextLoad)
                {
                    FailNextLoad = false;
                    return UniTask.FromResult(MapContentLoadResult.Fail("load failed"));
                }

                var context = CreateMapContext(mapId);
                objects.Add(context.gameObject);
                objects.Add(context.MapContentRoot);
                objects.Add(context.DynamicObjectRoot.gameObject);
                return UniTask.FromResult(MapContentLoadResult.Ok(new TestMapHandle(mapId, context)));
            }

            public UniTask<MapContentUnloadResult> UnloadAsync(
                IMapContentHandle handle,
                CancellationToken cancellationToken = default)
            {
                if (handle is TestMapHandle testHandle)
                {
                    testHandle.Valid = false;
                }

                return UniTask.FromResult(MapContentUnloadResult.Ok());
            }
        }

        private sealed class TestMapHandle : IMapContentHandle
        {
            public TestMapHandle(string mapId, MapContext context)
            {
                MapId = mapId;
                Context = context;
            }

            public string MapId { get; }
            public MapContext Context { get; }
            public bool Valid { get; set; } = true;
            public bool IsValid => Valid;
        }

        private sealed class TestResourceLoader : IResourceLoader
        {
            private readonly GameObject playerPrefab;
            private readonly GameObject projectilePrefab;

            public TestResourceLoader(GameObject playerPrefab, GameObject projectilePrefab)
            {
                this.playerPrefab = playerPrefab;
                this.projectilePrefab = projectilePrefab;
            }

            public T LoadAsset<T>(string path) where T : UnityEngine.Object
            {
                return Select(path) as T;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            {
                onLoaded?.Invoke(Select(path) as T);
                return new ResourceLoadHandle(new object());
            }

            public void Release(ResourceLoadHandle handle)
            {
            }

            private GameObject Select(string path)
            {
                return path == "Prefabs/Combat/GrayboxArrow"
                    ? projectilePrefab
                    : playerPrefab;
            }
        }

        private sealed class TestConfigProvider : IRuntimeConfigProvider
        {
            public bool TryGetExpeditionTasks(string mapId, out IReadOnlyList<ExpeditionTaskDefinition> values, out string error)
            {
                values = Array.Empty<ExpeditionTaskDefinition>();
                error = null;
                return true;
            }

            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                switch (id)
                {
                    case ItemIds.ShortSword:
                        value = new ItemDefinition(id, "Short Sword", string.Empty, string.Empty, string.Empty, string.Empty, ItemType.Weapon, 1, ItemUseEffect.None, 0);
                        error = null;
                        return true;
                    case ItemIds.Bow:
                        value = new ItemDefinition(id, "Bow", string.Empty, string.Empty, string.Empty, string.Empty, ItemType.Weapon, 1, ItemUseEffect.None, 0);
                        error = null;
                        return true;
                    case ItemIds.Arrow:
                        value = new ItemDefinition(id, "Arrow", string.Empty, string.Empty, string.Empty, string.Empty, ItemType.Ammunition, 20, ItemUseEffect.None, 0);
                        error = null;
                        return true;
                    case ItemIds.Cloth:
                        value = new ItemDefinition(id, "Cloth", string.Empty, string.Empty, string.Empty, string.Empty, ItemType.Material, 20, ItemUseEffect.None, 0);
                        error = null;
                        return true;
                    case ItemIds.MetalParts:
                        value = new ItemDefinition(id, "Metal Parts", string.Empty, string.Empty, string.Empty, string.Empty, ItemType.Material, 20, ItemUseEffect.None, 0);
                        error = null;
                        return true;
                    default:
                        value = default;
                        error = "missing item";
                        return false;
                }
            }

            public bool TryGetWeapon(int itemId, out WeaponConfigDefinition value, out string error)
            {
                if (itemId == ItemIds.ShortSword)
                {
                    value = new WeaponConfigDefinition(itemId, WeaponKind.Melee, 10, 0.45f, 2.2f, 90f, string.Empty, 0);
                    error = null;
                    return true;
                }

                if (itemId == ItemIds.Bow)
                {
                    value = new WeaponConfigDefinition(itemId, WeaponKind.Projectile, 10, 0.75f, 14f, 0f, "projectile.arrow.basic", ItemIds.Arrow, 1, 1f);
                    error = null;
                    return true;
                }

                value = default;
                error = "missing weapon";
                return false;
            }

            public bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error)
            {
                if (id == "projectile.arrow.basic")
                {
                    value = new ProjectileConfigDefinition(id, "Prefabs/Combat/GrayboxArrow", 18f, 18f);
                    error = null;
                    return true;
                }

                value = default;
                error = "missing projectile";
                return false;
            }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = new MapAssetDefinition(id, id);
                error = null;
                return true;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = new[]
                {
                    new MapSpawnDefinition("player." + mapId, mapId, MapSpawnObjectType.Player, "player", 0f, 0f, 0f, 0f)
                };
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = new PlayerConfigDefinition(id, "player.prefab", 100, 10, 1f, 1f, 1f, 1f, 5f);
                error = null;
                return true;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetCraftRecipes(out IReadOnlyList<CraftRecipeDefinition> values, out string error)
            {
                values = Array.Empty<CraftRecipeDefinition>();
                error = null;
                return true;
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }
        }
    }
}
