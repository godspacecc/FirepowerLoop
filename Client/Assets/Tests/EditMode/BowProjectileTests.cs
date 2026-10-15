using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class BowProjectileTests
    {
        private const long MapInstanceId = 73;
        private string tempRoot;
        private FakePlayerPort playerPort;
        private FakeProjectilePort projectilePort;
        private FakeEnemyResolver enemyResolver;
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            ResourceLoader.SetLoaderForTests(new TestResourceLoader());
            LocalizationLanguages.ResetForTests();
            playerPort = new FakePlayerPort();
            projectilePort = new FakeProjectilePort();
            enemyResolver = new FakeEnemyResolver();
            CreateUiRoot();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(new TestConfigProvider());
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(new FakePlayerPortProvider(playerPort));
                architecture.RegisterUtility<IProjectileRuntimePortProvider>(projectilePort);
                architecture.RegisterUtility<IEnemyColliderResolver>(enemyResolver);
            };

            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayBowProjectileTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player", string.Empty, 100, 1, 1f, 1f, 1f, 1f, 5f),
                false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            RegisterPlayer(Vector3.zero);
            _ = Game.Interface.GetSystem<InventorySystem>();
            _ = Game.Interface.GetSystem<WeaponEquipmentSystem>();
            _ = Game.Interface.GetSystem<ProjectileSystem>();
            _ = Game.Interface.GetSystem<WeaponSystem>();
            _ = Game.Interface.GetSystem<CombatSystem>();
            Game.Interface.SendEvent(new PlayerReadyEvent(PlayerModel.LocalPlayerUnitId, MapInstanceId));
        }

        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
            ResetArchitecture();
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            UIRoot.ClearForTests();
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
        public void RealProjectilePresentationAndSweepFollowVerticalFlight()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxArrow.prefab");
            var arrow = UnityEngine.Object.Instantiate(prefab);
            objects.Add(arrow);
            var target = new GameObject("ElevatedProjectileTarget");
            objects.Add(target);
            var origin = new Vector3(1000f, 1f, 1000f);
            var direction = new Vector3(0f, 1f, 1f).normalized;
            target.transform.position = origin + direction * 5f;
            var collider = target.AddComponent<BoxCollider>();
            collider.size = Vector3.one * .5f;
            collider.isTrigger = true;
            var view = arrow.GetComponent<ProjectileView>();
            view.ApplyPose(origin, direction);
            Assert.That(Vector3.Distance(arrow.transform.forward, direction), Is.LessThan(.00001f), "弹体表现与碰撞盒必须沿三维飞行方向旋转");
            Physics.SyncTransforms();
            var hits = new List<ProjectileMovementHit>();
            view.CollectMovementHits(origin, origin + direction * 10f, hits);
            Assert.That(hits.Exists(hit => hit.Collider == collider), Is.True);
        }

        [Test]
        public void ProjectileSpawnPreservesVerticalDirectionAndTravel()
        {
            var direction = new Vector3(0f, 1f, 1f).normalized;
            var request = new ProjectileSpawnRequest(PlayerModel.LocalPlayerUnitId, CombatFaction.Player,
                MapInstanceId, 10, Vector3.up, direction, "projectile.arrow.basic");
            Assert.That(Game.Interface.GetSystem<ProjectileSystem>().TrySpawn(request, out var id, out var error), Is.True, error);
            var state = Game.Interface.GetModel<ProjectileModel>().Projectiles[id];
            Assert.That(Vector3.Distance(state.Direction, direction), Is.LessThan(.00001f));
            InvokeAdvance(id, .1f);
            Assert.That(Game.Interface.GetModel<ProjectileModel>().Projectiles[id].Position.y, Is.GreaterThan(1f));
        }

        [Test]
        public void ShoulderInputShootsTowardsRaisedCrosshairHit() => AssertShoulderShotTowardsHit(.5f);

        [Test]
        public void ShoulderInputShootsTowardsLoweredCrosshairHit() => AssertShoulderShotTowardsHit(-.5f);

        [Test]
        public void FirstPersonInputShootsTowardsRaisedCrosshairHit() => AssertShoulderShotTowardsHit(.5f, PlayerCameraMode.FirstPerson);

        [Test]
        public void FirstPersonInputShootsTowardsLoweredCrosshairHit() => AssertShoulderShotTowardsHit(-.5f, PlayerCameraMode.FirstPerson);

        private void AssertShoulderShotTowardsHit(float vertical, PlayerCameraMode mode = PlayerCameraMode.Shoulder)
        {
            CreateAndCommitSave();
            EquipBowWithArrows(1);
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().TrySetRangedLoadedAmmo(1, out _), Is.True);
            playerPort.Position = new Vector3(1000f, 0f, 1000f);
            Game.Interface.SendEvent(new PlayerReadyEvent(PlayerModel.LocalPlayerUnitId, MapInstanceId));
            Game.Interface.GetModel<PlayerCameraModeModel>().Mode = mode;
            var ray = new Ray(playerPort.Position + new Vector3(.55f, 2f, -3f), new Vector3(0f, vertical, 1f).normalized);
            var target = new GameObject("ThreeDimensionalAimTarget");
            objects.Add(target);
            target.transform.position = ray.GetPoint(10f);
            var collider = target.AddComponent<BoxCollider>();
            collider.size = Vector3.one * .5f;
            var self = new GameObject("PlayerAimSelfCollider");
            objects.Add(self);
            self.transform.position = ray.GetPoint(2f);
            playerPort.SelfCollider = self.AddComponent<BoxCollider>();
            playerPort.SelfCollider.size = Vector3.one * .2f;
            Physics.SyncTransforms();
            Assert.That(collider.Raycast(ray, out var hit, 20f), Is.True);
            var system = Game.Interface.GetSystem<PlayerInputSystem>();
            var reader = new ShotInputReader();
            typeof(PlayerInputSystem).GetField("input", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(system, reader);
            PlayerInputSystem.SetPointerRayResolverForTests(() => ray);
            try
            {
                LifeCycle.Update();
                var state = Game.Interface.GetModel<ProjectileModel>().Projectiles.Values.Single();
                var muzzle = playerPort.Position + Vector3.up;
                var expected = (hit.point - muzzle).normalized;
                Assert.That(Vector3.Distance(state.Direction, expected), Is.LessThan(.00001f), "发射方向必须从实际发射点指向三维准星命中点");
                Assert.That(Mathf.Sign(state.Direction.y), Is.EqualTo(Mathf.Sign(vertical)));
                Assert.That((playerPort.Rotation * Vector3.forward).y, Is.EqualTo(0f).Within(.00001f));
            }
            finally { PlayerInputSystem.SetPointerRayResolverForTests(null); }
        }

        private sealed class ShotInputReader : PlayerInputSystem.IPlayerInputReader
        {
            public Vector2 MoveInput => Vector2.zero;
            public bool IsEnabled => true;
            public bool AttackPressedThisFrame => true;
            public bool HolsterPressedThisFrame { get; set; }
            public bool MeleeSlotPressedThisFrame => false;
            public bool RangedSlotPressedThisFrame => false;
            public bool InteractPressedThisFrame => false;
            public bool SprintHeld => false;
            public bool BuildPressedThisFrame => false;
            public bool DestroyPressedThisFrame => false;
            public bool CancelPressedThisFrame => false;
            public bool ReloadPressedThisFrame => false;
            public void Enable() { }
            public void Disable() { }
            public void Dispose() { }
        }

        [Test]
        public void ShoulderSkyShotUsesDistantAimPointFromMuzzle()
        {
            PrepareLoadedShoulderWeapon();
            var ray = new Ray(playerPort.Position + new Vector3(.55f, 2f, -3f), new Vector3(0f, .5f, 1f));
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward, ray));
            var state = Game.Interface.GetModel<ProjectileModel>().Projectiles.Values.Single();
            var offsetFromRay = state.Position - ray.origin;
            // 从侧置镜头校正回发射点：轨迹延长线应与准星射线相交，而非两条平行线。
            var separation = Mathf.Abs(Vector3.Dot(offsetFromRay, Vector3.Cross(state.Direction, ray.direction).normalized));
            Assert.That(separation, Is.LessThan(.0001f));
            Assert.That(state.Direction.y, Is.GreaterThan(0f));
            Assert.That(Vector3.Distance(state.Direction, ray.direction.normalized), Is.GreaterThan(.001f));
        }

        [Test]
        public void ShoulderTargetBehindMuzzleDoesNotConsumeAmmoOrStartCooldown()
        {
            PrepareLoadedShoulderWeapon();
            var ray = new Ray(playerPort.Position + new Vector3(.55f, 2f, -3f), Vector3.forward);
            var blocker = new GameObject("AimTargetBehindMuzzle");
            objects.Add(blocker);
            blocker.transform.position = ray.GetPoint(1f);
            blocker.AddComponent<BoxCollider>().size = Vector3.one * .2f;
            Physics.SyncTransforms();
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward, ray));
            Assert.That(projectilePort.CreateCount, Is.Zero);
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().RangedLoadedAmmo, Is.EqualTo(1));
            Assert.That(GetWeaponCooldown(), Is.Zero);
        }

        [Test]
        public void TopDownShotIgnoresThreeDimensionalRay()
        {
            PrepareLoadedShoulderWeapon();
            Game.Interface.GetModel<PlayerCameraModeModel>().Mode = PlayerCameraMode.TopDown;
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward,
                new Ray(playerPort.Position + Vector3.up * 2f, new Vector3(0f, 1f, 1f))));
            Assert.That(Game.Interface.GetModel<ProjectileModel>().Projectiles.Values.Single().Direction, Is.EqualTo(Vector3.forward));
        }

        private void PrepareLoadedShoulderWeapon()
        {
            CreateAndCommitSave();
            EquipBowWithArrows(1);
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().TrySetRangedLoadedAmmo(1, out _), Is.True);
            playerPort.Position = new Vector3(1000f, 0f, 1000f);
            Game.Interface.SendEvent(new PlayerReadyEvent(PlayerModel.LocalPlayerUnitId, MapInstanceId));
            Game.Interface.GetModel<PlayerCameraModeModel>().Mode = PlayerCameraMode.Shoulder;
        }

        [Test]
        public void GunProjectilesExpireWithoutCreatingRecoverableAmmunition()
        {
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(CreateRealConfigProvider());
            var recoveries = new List<LootDropRequestedEvent>();
            Game.Interface.RegisterEvent<LootDropRequestedEvent>(evt => recoveries.Add(evt));
            var system = Game.Interface.GetSystem<ProjectileSystem>();
            foreach (var configId in new[] { "projectile.bullet.basic", "projectile.plasma.basic" })
            {
                var request = new ProjectileSpawnRequest(PlayerModel.LocalPlayerUnitId, CombatFaction.Player,
                    MapInstanceId, 10, Vector3.up * 1.5f, Vector3.forward, configId);
                Assert.That(system.TrySpawn(request, out var id, out var error), Is.True, error);
                InvokeAdvance(id, 2f);
                Assert.That(Game.Interface.GetModel<ProjectileModel>().Projectiles.ContainsKey(id), Is.False);
                Assert.That(recoveries, Is.Empty, configId + " 飞完射程不能生成整份弹药掉落");
            }
        }

        [Test]
        public void ProjectileConfigAndBowReferenceAreFixed()
        {
            var provider = CreateRealConfigProvider();
            Assert.That(provider.TryGetProjectile("projectile.arrow.basic", out var projectile, out var error), Is.True, error);
            Assert.That(projectile.PrefabAddress, Is.EqualTo("Prefabs/Combat/GrayboxArrow"));
            Assert.That(projectile.MoveSpeed, Is.EqualTo(14f).Within(0.001f));
            Assert.That(projectile.MaxTravelDistance, Is.EqualTo(14f).Within(0.001f));
            Assert.That(projectile.RecoveryLootConfigId, Is.EqualTo("loot.ammunition.arrow"));

            Assert.That(provider.TryGetWeapon(ItemIds.Bow, out var bow, out error), Is.True, error);
            Assert.That(bow.WeaponKind, Is.EqualTo(WeaponKind.Projectile));
            Assert.That(bow.AttackDamage, Is.EqualTo(10));
            Assert.That(bow.AttackCooldownSeconds, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(bow.ProjectileConfigId, Is.EqualTo("projectile.arrow.basic"));
            Assert.That(bow.AmmoItemId, Is.EqualTo(ItemIds.Arrow));
        }

        [Test]
        public void RequiredRuntimePrefabSourcesAndCollectorsAreValid()
        {
            var type = RequireEditorType("Game.Editor.YooAssetBuildUtility");
            var property = type.GetProperty("RuntimePrefabLocations", BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(property);
            var locations = property.GetValue(null) as System.Collections.IEnumerable;
            Assert.NotNull(locations);
            var count = 0;
            foreach (var _ in locations)
            {
                count++;
            }

            Assert.That(count, Is.EqualTo(17));
            var method = type.GetMethod("ValidateRequiredRuntimePrefabSources", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var args = new object[] { null };
            var success = (bool)method.Invoke(null, args);
            Assert.That(success, Is.True, args[0] as string);
        }

        [Test]
        public void CurrentBuiltinManifestContainsAllRequiredRuntimePrefabs()
        {
            var type = RequireEditorType("Game.Editor.YooAssetBuildUtility");
            var method = type.GetMethod("ValidateRequiredRuntimePrefabManifest", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var packageDirectory = Path.Combine("Assets/StreamingAssets/yoo/DefaultPackage");
            var versionFile = Path.Combine(packageDirectory, "DefaultPackage.version");
            Assert.That(File.Exists(versionFile), Is.True, versionFile);
            var version = File.ReadAllText(versionFile).Trim();
            var args = new object[] { packageDirectory, version, null };
            var success = (bool)method.Invoke(null, args);
            Assert.That(success, Is.True, args[2] as string);
        }

        [Test]
        public void ResourceOnlyBuildEntryDoesNotBuildPlayerOrHybridClr()
        {
            var source = File.ReadAllText("Assets/Editor/Build/BuildUtility.cs");
            var body = ExtractMethodBody(source, "BuildBuiltinResourcesOnlyBatch");
            Assert.That(body, Does.Contain("BuildPackageOrThrow(version, true)"));
            Assert.That(body, Does.Contain("ValidatePackageOrThrow(package.OutputPackageDirectory, version)"));
            Assert.That(body, Does.Contain("YooAssetBuildUtility.GetStreamingAssetsDirectory()"));
            Assert.That(body, Does.Contain("ValidatePackageOrThrow(builtinDir, version)"));
            Assert.That(body, Does.Not.Contain("BuildWindowsPlayer"));
            Assert.That(body, Does.Not.Contain("HybridCLRBuildUtility"));
            Assert.That(body, Does.Not.Contain("StartResourceServer"));
            Assert.That(body, Does.Not.Contain("ResourceServerPublishUtility"));
            Assert.That(body, Does.Not.Contain("SaveSuccessfulState"));
        }

        [Test]
        public void AsyncLoadFailureKeepsHandleForCallerRelease()
        {
            var source = File.ReadAllText("Assets/Game/Scripts/Utility/Res/YooAssetResourceLoader.cs");
            var asyncBody = ExtractMethodBody(source, "LoadAssetAsync");
            Assert.That(asyncBody, Does.Contain("return new ResourceLoadHandle(handle)"));
            Assert.That(asyncBody, Does.Not.Contain("operation.Release()"));

            var syncBody = ExtractMethodBody(source, "LoadAsset");
            Assert.That(syncBody, Does.Contain("handle.Release()"));
        }

        [Test]
        public void ArrowConsumptionIsAtomicAndCooldownStartsOnlyAfterSpawn()
        {
            CreateAndCommitSave();
            EquipBowWithArrows(2);
            var inventoryEvents = 0;
            Game.Interface.RegisterEvent<InventoryChangedEvent>(_ => inventoryEvents++);

            Game.Interface.SendEvent(new PlayerReloadRequestedEvent());
            Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out var reload), Is.True);
            Assert.That(reload.Kind, Is.EqualTo(PlayerActionKind.Reload));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(2));
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().RangedLoadedAmmo, Is.EqualTo(0));
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(0.5f);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(2));
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(0.5f);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().RangedLoadedAmmo, Is.EqualTo(1));
            Assert.That(inventoryEvents, Is.EqualTo(1));

            // 模拟失败库存事务恢复克隆，发射必须扣除容器中的同身份武器。
            var carried = Game.Interface.GetModel<PlayerBagModel>().Container;
            carried.RestoreSnapshot(carried.CaptureSnapshot());
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Assert.That(carried.Slots.Values.Single(stack => stack.ItemId == ItemIds.Bow).LoadedAmmo, Is.Zero);

            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().RangedLoadedAmmo, Is.EqualTo(0));
            Assert.That(projectilePort.CreateCount, Is.EqualTo(1));
            Assert.That(playerPort.BowFireCount, Is.EqualTo(1));
            Assert.That(inventoryEvents, Is.EqualTo(2));
            Assert.That(IsSaveDirty(), Is.True);
            Assert.That(GetWeaponCooldown(), Is.EqualTo(0.75f).Within(0.001f));

            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(1));
            Assert.That(projectilePort.CreateCount, Is.EqualTo(1));
            Assert.That(inventoryEvents, Is.EqualTo(2));

            SetWeaponCooldown(0f);
            SetSaveDirty(false);
            Game.Interface.SendEvent(new PlayerReloadRequestedEvent());
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(1f);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().RangedLoadedAmmo, Is.EqualTo(1));
            SetSaveDirty(false);
            projectilePort.CreateSucceeds = false;
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().RangedLoadedAmmo, Is.EqualTo(1));
            Assert.That(projectilePort.CreateCount, Is.EqualTo(2));
            Assert.That(inventoryEvents, Is.EqualTo(3));
            Assert.That(IsSaveDirty(), Is.False);
            Assert.That(GetWeaponCooldown(), Is.EqualTo(0f).Within(0.001f));

            projectilePort.CreateSucceeds = true;
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().RangedLoadedAmmo, Is.EqualTo(0));
            Assert.That(projectilePort.CreateCount, Is.EqualTo(3));
            Assert.That(inventoryEvents, Is.EqualTo(4));
            Assert.That(GetWeaponCooldown(), Is.EqualTo(0.75f).Within(0.001f));
        }

        [Test]
        public void ReloadRejectsInvalidStatesAndCanceledCompletionNeverConsumesArrow()
        {
            CreateAndCommitSave();
            var actions = Game.Interface.GetSystem<PlayerActionSystem>();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var equipment = Game.Interface.GetModel<WeaponEquipmentModel>();

            Game.Interface.SendEvent(new PlayerReloadRequestedEvent());
            Assert.That(actions.TryGetSnapshot(out _), Is.False, "未装备弓不能开始换弹。");

            Assert.That(bag.TryAdd(ItemIds.Bow, 1, 1, out _, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(0, out error), Is.True, error);
            Game.Interface.SendEvent(new PlayerReloadRequestedEvent());
            Assert.That(actions.TryGetSnapshot(out _), Is.False, "无箭不能开始换弹。");

            Assert.That(bag.TryAdd(ItemIds.Arrow, 4, 20, out _, out error), Is.True, error);
            Assert.That(equipment.TrySetRangedLoadedAmmo(1, out error), Is.True, error);
            Game.Interface.SendEvent(new PlayerReloadRequestedEvent());
            Assert.That(actions.TryGetSnapshot(out _), Is.False, "已满弹匣不能开始换弹。");
            Assert.That(equipment.TrySetRangedLoadedAmmo(0, out error), Is.True, error);

            void AssertCanceled(System.Action cancel, string name)
            {
                var before = bag.GetTotalAmount(ItemIds.Arrow);
                Game.Interface.SendEvent(new PlayerReloadRequestedEvent());
                Assert.That(actions.TryGetSnapshot(out var snapshot), Is.True, name);
                Assert.That(snapshot.Kind, Is.EqualTo(PlayerActionKind.Reload), name);
                cancel();
                actions.TickForTests(2f); // 模拟迟到完成，不得再扣箭。
                Assert.That(bag.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(before), name);
                Assert.That(equipment.RangedLoadedAmmo, Is.Zero, name);
            }

            AssertCanceled(() => actions.CancelActive(PlayerActionCancelReason.UserCanceled), "X 取消");
            AssertCanceled(() => actions.CancelActive(PlayerActionCancelReason.Interaction), "交互取消");
            var arrowsBeforeAttack = bag.GetTotalAmount(ItemIds.Arrow);
            Game.Interface.SendEvent(new PlayerReloadRequestedEvent());
            Assert.That(actions.TryGetSnapshot(out _), Is.True);
            Assert.That(equipment.TrySetRangedLoadedAmmo(1, out error), Is.True, error);
            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            actions.TickForTests(2f);
            Assert.That(bag.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(arrowsBeforeAttack), "攻击取消的迟到完成不得扣箭。");
            Assert.That(equipment.RangedLoadedAmmo, Is.Zero);

            Assert.That(bag.TryAdd(ItemIds.ShortSword, 1, 1, out var swordSlot, out error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(bag.Slots.Single(x=>x.Value.ItemId==ItemIds.ShortSword).Key, out error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TrySetActiveSlot(WeaponSlot.Ranged, out error), Is.True, error);
            AssertCanceled(() => Game.Interface.SendEvent(new WeaponSlotSwitchRequestedEvent(WeaponSlot.Melee)), "切槽取消");
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TrySetActiveSlot(WeaponSlot.Ranged, out error), Is.True, error);
            Assert.That(bag.TryAdd(ItemIds.Bow, 1, 1, out var replacementSlot, out error), Is.True, error);
            AssertCanceled(() => Game.Interface.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(bag.Slots.Last(x=>x.Value.ItemId==ItemIds.Bow).Key, out _), "替换取消");
            AssertCanceled(() => Game.Interface.GetSystem<WeaponEquipmentSystem>().TryUnequipToPlayerBag(WeaponSlot.Ranged, out _), "卸下取消");
        }

        [Test]
        public void ProjectileCollisionAndEndRulesAreDeterministic()
        {
            RegisterEnemy(20, MapInstanceId, new Vector3(0f, 0f, 4f), 30);
            var projectile = Game.Interface.GetSystem<ProjectileSystem>();
            var ended = new List<ProjectileEndReason>();
            var recoveries = new List<LootDropRequestedEvent>();
            Game.Interface.RegisterEvent<ProjectileEndedEvent>(evt => ended.Add(evt.Reason));
            Game.Interface.RegisterEvent<LootDropRequestedEvent>(evt => recoveries.Add(evt));

            Assert.That(projectile.TrySpawn(CreateSpawnRequest(), out var firstId, out var error), Is.True, error);
            var ignoredTrigger = CreateCollider("IgnoredTrigger", true);
            projectile.HandleCollision(firstId, ignoredTrigger);
            Assert.That(Game.Interface.GetModel<ProjectileModel>().Projectiles.ContainsKey(firstId), Is.True);

            var enemyCollider = CreateCollider("Enemy", true);
            enemyResolver.Bind(enemyCollider, 20);
            projectile.HandleCollision(firstId, enemyCollider);
            projectile.HandleCollision(firstId, enemyCollider);

            Assert.That(Game.Interface.GetModel<ProjectileModel>().Projectiles.ContainsKey(firstId), Is.False);
            Assert.That(projectilePort.ReleaseCount, Is.EqualTo(1));
            Assert.That(ended, Is.EqualTo(new[] { ProjectileEndReason.HitCombatant }));
            Assert.That(recoveries, Is.Empty, "命中敌人的箭不得回收。");
            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(20, out var target), Is.True);
            Assert.That(target.HP, Is.EqualTo(20));

            Assert.That(projectile.TrySpawn(CreateSpawnRequest(), out var obstacleId, out error), Is.True, error);
            projectile.HandleCollision(obstacleId, CreateCollider("Wall", false));
            Assert.That(ended[ended.Count - 1], Is.EqualTo(ProjectileEndReason.HitObstacle));
            Assert.That(recoveries.Count, Is.EqualTo(1));
            Assert.That(recoveries[0].LootConfigId, Is.EqualTo("loot.ammunition.arrow"));

            Assert.That(projectile.TrySpawn(CreateSpawnRequest(), out var maxDistanceId, out error), Is.True, error);
            InvokeAdvance(maxDistanceId, 1f);
            Assert.That(ended[ended.Count - 1], Is.EqualTo(ProjectileEndReason.MaxDistance));
            Assert.That(recoveries.Count, Is.EqualTo(2));
            Assert.That(recoveries[1].Position.z, Is.EqualTo(14f).Within(0.001f));

            RegisterEnemy(21, MapInstanceId, Vector3.back, 30);
            var enemyRequest = new ProjectileSpawnRequest(
                21,
                CombatFaction.Enemy,
                MapInstanceId,
                10,
                Vector3.back,
                Vector3.forward,
                "projectile.arrow.basic");
            Assert.That(projectile.TrySpawn(enemyRequest, out var enemyArrowId, out error), Is.True, error);
            projectile.HandleCollision(enemyArrowId, CreateCollider("EnemyArrowWall", false));
            Assert.That(recoveries.Count, Is.EqualTo(3), "敌人射偏的箭也应生成同一种可拾取掉落。");
        }

        [Test]
        public void ProjectilesAreTransientRuntimeState()
        {
            var unitCount = Game.Interface.GetModel<UnitModel>().Units.Count;
            Assert.That(Game.Interface.GetSystem<ProjectileSystem>().TrySpawn(CreateSpawnRequest(), out var id, out var error), Is.True, error);

            Assert.That(Game.Interface.GetModel<UnitModel>().Units.Count, Is.EqualTo(unitCount));
            Assert.That(Game.Interface.GetModel<ProjectileModel>().Projectiles.ContainsKey(id), Is.True);
            Assert.That(Game.Interface.GetModel<SaveModel>().CurrentBase?.Sections.ContainsKey("another-day.projectiles") ?? false, Is.False);

            Game.Interface.GetSystem<ProjectileSystem>().ClearForMap(MapInstanceId);
            Assert.That(Game.Interface.GetModel<ProjectileModel>().Projectiles, Is.Empty);
            Assert.That(Game.Interface.GetSystem<ProjectileSystem>().TryEnd(id, ProjectileEndReason.HitObstacle), Is.False);

            Assert.That(Game.Interface.GetSystem<ProjectileSystem>().TrySpawn(CreateSpawnRequest(), out var nextId, out error), Is.True, error);
            Assert.That(nextId, Is.GreaterThan(id));
        }

        [Test]
        public void ProjectilePresentationFollowsMapTransactionOrder()
        {
            var ctrl = new ProjectileCtrl(Game.Interface);
            Game.Interface.RegisterUtility<IProjectileRuntimePortProvider>(ctrl);
            var projectile = Game.Interface.GetSystem<ProjectileSystem>();
            var ended = new List<ProjectileEndReason>();
            Game.Interface.RegisterEvent<ProjectileEndedEvent>(evt => ended.Add(evt.Reason));

            var mapA = CreateMapContext("projectile-map-a");
            var mapB = CreateMapContext("projectile-map-b");
            var mapC = CreateMapContext("projectile-map-c");
            PrepareAndCommit(ctrl, mapA, 901);
            Assert.That(ctrl.TryCreateProjectile(1001, 901, Vector3.zero, Vector3.forward, "Prefabs/Combat/GrayboxArrow", out var createError), Is.True, createError);
            Assert.That(GetActiveViewCount(ctrl), Is.EqualTo(1));
            ctrl.ReleaseProjectile(1001);
            Assert.That(GetActiveViewCount(ctrl), Is.EqualTo(0));

            Assert.That(ctrl.PrepareMapAsync(CreateContext(mapB, 902), default).GetAwaiter().GetResult().Success, Is.True);
            ctrl.RollbackPreparedMap();
            Assert.That(ctrl.TryCreateProjectile(1002, 901, Vector3.zero, Vector3.forward, "Prefabs/Combat/GrayboxArrow", out createError), Is.True, createError);
            ctrl.ReleaseProjectile(1002);

            MovePlayerUnitToMap(901);
            Assert.That(projectile.TrySpawn(CreateSpawnRequest(901), out var deactivatedId, out var spawnError), Is.True, spawnError);
            ctrl.SetCurrentMapActive(false);
            Assert.That(Game.Interface.GetModel<ProjectileModel>().Projectiles.ContainsKey(deactivatedId), Is.False);
            Assert.That(ended[ended.Count - 1], Is.EqualTo(ProjectileEndReason.MapDeactivated));
            Assert.That(GetActiveViewCount(ctrl), Is.EqualTo(0));
            Assert.That(ctrl.TryCreateProjectile(1003, 901, Vector3.zero, Vector3.forward, "Prefabs/Combat/GrayboxArrow", out createError), Is.True, createError);
            ctrl.ReleaseProjectile(1003);

            Assert.That(ctrl.PrepareMapAsync(CreateContext(mapB, 902), default).GetAwaiter().GetResult().Success, Is.True);
            MovePlayerUnitToMap(901);
            Assert.That(projectile.TrySpawn(CreateSpawnRequest(901), out var releasedId, out spawnError), Is.True, spawnError);
            ctrl.ReleaseCurrentMap();
            Assert.That(Game.Interface.GetModel<ProjectileModel>().Projectiles.ContainsKey(releasedId), Is.False);
            Assert.That(ended[ended.Count - 1], Is.EqualTo(ProjectileEndReason.MapReleased));
            Assert.That(GetActiveViewCount(ctrl), Is.EqualTo(0));
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            Assert.That(ctrl.TryCreateProjectile(1004, 902, Vector3.zero, Vector3.forward, "Prefabs/Combat/GrayboxArrow", out createError), Is.True, createError);
            ctrl.ReleaseProjectile(1004);

            Assert.That(ctrl.PrepareMapAsync(CreateContext(mapC, 903), default).GetAwaiter().GetResult().Success, Is.True);
            MovePlayerUnitToMap(902);
            Assert.That(projectile.TrySpawn(CreateSpawnRequest(902), out var architectureId, out spawnError), Is.True, spawnError);
            ctrl.ReleaseAll();
            Assert.That(Game.Interface.GetModel<ProjectileModel>().Projectiles.ContainsKey(architectureId), Is.False);
            Assert.That(ended[ended.Count - 1], Is.EqualTo(ProjectileEndReason.ArchitectureReleased));
            Assert.That(GetActiveViewCount(ctrl), Is.EqualTo(0));
            Assert.That(ctrl.TryCreateProjectile(1005, 903, Vector3.zero, Vector3.forward, "Prefabs/Combat/GrayboxArrow", out _), Is.False);
        }

        [Test]
        public void PlayerAndProjectilePrefabsProvidePresentationContracts()
        {
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            Assert.NotNull(playerPrefab);
            var playerView = playerPrefab.GetComponent<PlayerView>();
            Assert.NotNull(playerView);
            Assert.That(playerView.Validate(out var playerError), Is.True, playerError);

            var instance = UnityEngine.Object.Instantiate(playerPrefab);
            objects.Add(instance);
            var instanceView = instance.GetComponent<PlayerView>();
            var sword = (GameObject)GetPrivateField(instanceView, "shortSwordObject");
            var bow = (GameObject)GetPrivateField(instanceView, "bowObject");
            var firePoint = (Transform)GetPrivateField(instanceView, "projectileFirePoint");
            Assert.NotNull(sword);
            Assert.NotNull(bow);
            Assert.NotNull(firePoint);

            instanceView.SetWeaponPresentation(WeaponSlot.Melee, true, false);
            Assert.That(sword.activeSelf, Is.True);
            Assert.That(bow.activeSelf, Is.False);
            instanceView.SetWeaponPresentation(WeaponSlot.Ranged, false, true);
            Assert.That(sword.activeSelf, Is.False);
            Assert.That(bow.activeSelf, Is.True);
            Assert.That(instanceView.GetProjectileOrigin(Vector3.forward), Is.EqualTo(firePoint.position));

            var arrowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxArrow.prefab");
            Assert.NotNull(arrowPrefab);
            var projectileView = arrowPrefab.GetComponent<ProjectileView>();
            Assert.NotNull(projectileView);
            Assert.That(projectileView.Validate(out var projectileError), Is.True, projectileError);
            Assert.That(arrowPrefab.GetComponent<Collider>().isTrigger, Is.True);
            var body = arrowPrefab.GetComponent<Rigidbody>();
            Assert.NotNull(body);
            Assert.That(body.isKinematic, Is.True);
        }

        [Test]
        public void ArcadeProjectilePresentation_ReusesRealProjectileViewFromPool()
        {
            UseRealProjectileConfig();
            var ctrl = new ProjectileCtrl(Game.Interface);
            var context = CreateMapContext("arcade-projectile-map");
            var prepare = ctrl.PrepareMapAsync(
                MapPreparationContext.CreateArcade(MapIds.Arcade, 904, context, Array.Empty<MapSpawnDefinition>()),
                default).GetAwaiter().GetResult();
            Assert.That(prepare.Success, Is.True, prepare.Error);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);

            Assert.That(ctrl.TrySetProjectile(2001, Vector3.zero, Vector3.forward, out var error), Is.True, error);
            var active = (Dictionary<int, ProjectileView>)GetPrivateField(ctrl, "activeViews");
            var first = active[2001];
            ctrl.RemoveProjectile(2001);
            Assert.That(first.gameObject.activeSelf, Is.False);

            Assert.That(ctrl.TrySetProjectile(2002, Vector3.right, Vector3.left, out error), Is.True, error);
            Assert.That(active[2002], Is.SameAs(first));
            ctrl.ClearArcadeProjectiles();
            ctrl.ReleaseAll();
        }

        [Test]
        public void ArcadeOrdinaryGunsUseShortPreloadedBulletsIncludingFiveShotSpread()
        {
            var provider = UseRealProjectileConfig();
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            var ctrl = PrepareArcadeProjectileController();
            try
            {
                var active = (Dictionary<int, ProjectileView>)GetPrivateField(ctrl, "activeViews");
                var id = 3000;
                foreach (var weaponId in new[] { "arcade.weapon.basic", "arcade.weapon.rapid", "arcade.weapon.spread", "arcade.weapon.raygun" })
                {
                    var weapon = catalog.Weapons.Single(value => value.Id == weaponId);
                    var count = weaponId == "arcade.weapon.spread" ? 5 : 1;
                    Assert.That(weapon.ProjectileCount, Is.EqualTo(count));
                    foreach (var angle in ArcadeCombatSystem.BuildSpreadAngles(weapon.ProjectileCount, weapon.SpreadDegrees))
                    {
                        Assert.That(ctrl.TrySetProjectile(++id, Vector3.zero, Quaternion.Euler(0, angle, 0) * Vector3.forward,
                            weapon.ProjectileVisualResourceName, false, out error), Is.True, error);
                        var renderer = active[id].GetComponent<Renderer>();
                        Assert.That(renderer.enabled, Is.True);
                        Assert.That(renderer.bounds.size.magnitude, Is.LessThan(0.3f), weaponId);
                        Assert.That(active[id].GetComponentsInChildren<Renderer>().Count(value => value.enabled), Is.EqualTo(1));
                    }
                    Assert.That(active.Count, Is.EqualTo(count));
                    ctrl.ClearArcadeProjectiles();
                }
            }
            finally { ctrl.ReleaseAll(); }
        }

        [Test]
        public void ArcadeProjectileSwitchingPreservesSpecialModelsAndPlayerEnemyArrows()
        {
            var provider = UseRealProjectileConfig();
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            var ctrl = PrepareArcadeProjectileController();
            var arrow = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxArrow.prefab"));
            try
            {
                var active = (Dictionary<int, ProjectileView>)GetPrivateField(ctrl, "activeViews");
                for (var cycle = 0; cycle < 3; cycle++)
                {
                    foreach (var weaponId in new[] { "arcade.weapon.basic", "arcade.weapon.piercing", "arcade.weapon.rapid",
                                 "arcade.weapon.rocket", "arcade.weapon.spread", "arcade.weapon.grenade", "weapon.bow" })
                    {
                        var weapon = catalog.Weapons.Single(value => value.Id == weaponId);
                        Assert.That(ctrl.TrySetProjectile(3100, Vector3.zero, Vector3.forward,
                            weapon.ProjectileVisualResourceName, false, out error), Is.True, error);
                        var renderers = active[3100].GetComponentsInChildren<Renderer>().Where(value => value.enabled).ToArray();
                        Assert.That(renderers.Length, Is.EqualTo(1), weaponId);
                        var special = weaponId == "arcade.weapon.piercing" || weaponId == "arcade.weapon.rocket" || weaponId == "arcade.weapon.grenade";
                        if (special || weaponId == "weapon.bow")
                        {
                            arrow.GetComponent<ProjectileView>().SetArcadePresentation(special ? weapon.ProjectileVisualResourceName : null, false);
                            var expected = arrow.GetComponentsInChildren<Renderer>().Single(value => value.enabled);
                            Assert.That(renderers[0].sharedMaterial, Is.SameAs(expected.sharedMaterial), weaponId);
                            Assert.That(Vector3.Distance(renderers[0].bounds.size, expected.bounds.size), Is.LessThan(0.001f), weaponId);
                        }
                        else Assert.That(renderers[0].bounds.size.magnitude, Is.LessThan(0.3f), weaponId);
                    }
                    Assert.That(ctrl.TrySetProjectile(3100, Vector3.zero, Vector3.forward, "ArcadeGrenade", true, out error), Is.True, error);
                    var hostile = active[3100].GetComponentsInChildren<Renderer>().Where(value => value.enabled).ToArray();
                    Assert.That(hostile.Length, Is.EqualTo(1));
                    Assert.That(hostile[0].bounds.size.z, Is.EqualTo(0.9f).Within(0.001f));
                    ctrl.RemoveProjectile(3100);
                    Assert.That(active, Is.Empty);
                    Assert.That(ctrl.TrySetProjectile(3101, Vector3.zero, Vector3.forward, out error), Is.True, error);
                    Assert.That(active[3101].GetComponent<Renderer>().bounds.size.z, Is.EqualTo(0.22f).Within(0.001f));
                    ctrl.ClearArcadeProjectiles();
                    Assert.That(arrow.transform.localScale.z, Is.EqualTo(0.9f));
                }
            }
            finally { ctrl.ReleaseAll(); UnityEngine.Object.DestroyImmediate(arrow); }
        }

        private static LubanRuntimeConfigProvider UseRealProjectileConfig()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new Luban.ByteBuf(
                File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(provider);
            return provider;
        }

        private ProjectileCtrl PrepareArcadeProjectileController()
        {
            var ctrl = new ProjectileCtrl(Game.Interface);
            var context = CreateMapContext("arcade-projectile-visual-regression");
            var result = ctrl.PrepareMapAsync(MapPreparationContext.CreateArcade(MapIds.Arcade, 905, context,
                Array.Empty<MapSpawnDefinition>()), default).GetAwaiter().GetResult();
            Assert.That(result.Success, Is.True, result.Error);
            var commit = ctrl.CommitPreparedMap();
            Assert.That(commit.Success, Is.True, commit.Error);
            return ctrl;
        }

        [Test]
        public void PlayerBowAmmoPrompt_UsesLocalizedAmmoName()
        {
            EquipBowWithArrows(1);
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var arrowSlot = bag.Slots.First(pair => pair.Value.ItemId == ItemIds.Arrow).Key;
            Assert.That(bag.TryRemove(arrowSlot, 1, out _, out var error), Is.True, error);

            Game.Interface.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));

            var tips = UnityEngine.Object.FindObjectsOfType<FloatTextBoxUI>(true)
                .Select(box => box.TextTips != null ? box.TextTips.text : string.Empty)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();
            Assert.That(tips, Does.Contain("箭矢不足"));
        }

        [Test]
        public void PlayerPrefabFirePointSweepHitsEqualHeightEnemyCombatColliders()
        {
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            var arrowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxArrow.prefab");
            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxEnemy.prefab");
            var archerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxArcher.prefab");
            Assert.NotNull(playerPrefab);
            Assert.NotNull(arrowPrefab);
            Assert.NotNull(enemyPrefab);
            Assert.NotNull(archerPrefab);

            var player = UnityEngine.Object.Instantiate(playerPrefab);
            var arrow = UnityEngine.Object.Instantiate(arrowPrefab);
            try
            {
                player.transform.position = Vector3.zero;
                player.transform.rotation = Quaternion.identity;
                var playerView = player.GetComponent<PlayerView>();
                Assert.NotNull(playerView);
                var firePoint = (Transform)GetPrivateField(playerView, "projectileFirePoint");
                Assert.NotNull(firePoint);
                Assert.That(firePoint.position.y - player.transform.position.y, Is.EqualTo(0.8f).Within(0.001f));

                var projectileView = arrow.GetComponent<ProjectileView>();
                Assert.NotNull(projectileView);
                Assert.That(projectileView.Validate(out var projectileError), Is.True, projectileError);

                AssertEnemySweepHitsCombatCollider(enemyPrefab, "GrayboxEnemy", projectileView, firePoint, player.transform.position.y);
                AssertEnemySweepHitsCombatCollider(archerPrefab, "GrayboxArcher", projectileView, firePoint, player.transform.position.y);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(arrow);
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void ArcherPrefabFirePointSweepHitsPlayerCharacterController()
        {
            var archerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxArcher.prefab");
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            var arrowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxArrow.prefab");
            Assert.NotNull(archerPrefab);
            Assert.NotNull(playerPrefab);
            Assert.NotNull(arrowPrefab);

            var archer = UnityEngine.Object.Instantiate(archerPrefab);
            var player = UnityEngine.Object.Instantiate(playerPrefab);
            var arrow = UnityEngine.Object.Instantiate(arrowPrefab);
            try
            {
                archer.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                player.transform.SetPositionAndRotation(new Vector3(0f, 0f, 4f), Quaternion.identity);
                var archerView = archer.GetComponent<EnemyView>();
                var playerCollider = player.GetComponent<CharacterController>();
                var projectileView = arrow.GetComponent<ProjectileView>();
                Assert.NotNull(archerView);
                Assert.NotNull(playerCollider);
                Assert.NotNull(projectileView);
                Assert.That(archerView.TryGetProjectileOrigin(Vector3.forward, out var origin), Is.True);
                Assert.That(origin.y - archer.transform.position.y, Is.EqualTo(0.8f).Within(0.001f));
                Assert.That(projectileView.Validate(out var error), Is.True, error);

                projectileView.ApplyPose(origin, Vector3.forward);
                Physics.SyncTransforms();
                var hits = new List<ProjectileMovementHit>();
                projectileView.CollectMovementHits(origin, origin + Vector3.forward * 8f, hits);
                Assert.That(hits.Exists(hit => hit.Collider == playerCollider), Is.True,
                    "正式弓手发射点的水平扫掠必须包含正式玩家 CharacterController。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(arrow);
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(archer);
            }
        }

        [Test]
        public void ArrowTowerMuzzleSweepHitsEnemyCombatColliders()
        {
            var towerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Building/GrayboxArrowTower.prefab");
            var arrowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxArrow.prefab");
            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxEnemy.prefab");
            var archerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxArcher.prefab");
            Assert.NotNull(towerPrefab);
            Assert.NotNull(arrowPrefab);
            Assert.NotNull(enemyPrefab);
            Assert.NotNull(archerPrefab);

            var tower = UnityEngine.Object.Instantiate(towerPrefab);
            var arrow = UnityEngine.Object.Instantiate(arrowPrefab);
            try
            {
                tower.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var towerView = tower.GetComponent<TemporaryManualAttackView>();
                Assert.NotNull(towerView);
                Assert.That(towerView.ValidateReferences(out var towerError), Is.True, towerError);
                Assert.NotNull(towerView.Muzzle);

                var projectileView = arrow.GetComponent<ProjectileView>();
                Assert.NotNull(projectileView);
                Assert.That(projectileView.Validate(out var projectileError), Is.True, projectileError);
                AssertEnemySweepHitsCombatCollider(enemyPrefab, "GrayboxEnemy", projectileView, towerView.Muzzle, tower.transform.position.y);
                AssertEnemySweepHitsCombatCollider(archerPrefab, "GrayboxArcher", projectileView, towerView.Muzzle, tower.transform.position.y);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(arrow);
                UnityEngine.Object.DestroyImmediate(tower);
            }
        }

        private ProjectileSpawnRequest CreateSpawnRequest()
        {
            return CreateSpawnRequest(MapInstanceId);
        }

        private ProjectileSpawnRequest CreateSpawnRequest(long mapInstanceId)
        {
            return new ProjectileSpawnRequest(
                PlayerModel.LocalPlayerUnitId,
                CombatFaction.Player,
                mapInstanceId,
                10,
                Vector3.zero,
                Vector3.forward,
                "projectile.arrow.basic");
        }

        private static void AssertEnemySweepHitsCombatCollider(
            GameObject enemyPrefab,
            string enemyName,
            ProjectileView projectileView,
            Transform firePoint,
            float playerRootY)
        {
            var enemy = UnityEngine.Object.Instantiate(enemyPrefab);
            try
            {
                enemy.transform.position = new Vector3(firePoint.position.x, playerRootY, firePoint.position.z + 4f);
                enemy.transform.rotation = Quaternion.identity;
                var enemyView = enemy.GetComponent<EnemyView>();
                Assert.NotNull(enemyView, enemyName);
                var combatCollider = (Collider)GetPrivateField(enemyView, "combatCollider");
                Assert.NotNull(combatCollider, enemyName);
                Assert.That(combatCollider.enabled, Is.True, enemyName);
                Assert.That(combatCollider.isTrigger, Is.True, enemyName);

                var from = firePoint.position;
                var to = from + Vector3.forward * 14f;
                projectileView.ApplyPose(from, Vector3.forward);
                Physics.SyncTransforms();

                var hits = new List<ProjectileMovementHit>();
                projectileView.CollectMovementHits(from, to, hits);
                Assert.That(
                    hits.Exists(hit => hit.Collider == combatCollider),
                    Is.True,
                    enemyName + " combat collider should be included in arrow movement sweep candidates.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        public void RifleReloadFillsMagazineAcrossStacksAndPreservesRemainder()
        {
            PrepareRifleReload(75, 0);
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TrySplitHalf(1, 3, out var error), Is.True, error);
            Assert.That(bag.TryRemove(1, 25, out _, out error), Is.True, error);
            Game.Interface.SendEvent(new PlayerReloadRequestedEvent());
            Assert.That(bag.GetTotalAmount(26), Is.EqualTo(50), "完成前不扣弹药");
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(3f);
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().RangedLoadedAmmo, Is.EqualTo(30));
            Assert.That(bag.GetTotalAmount(26), Is.EqualTo(20));
        }

        public void RifleReloadUsesAvailableAmmoAndCancellationConsumesNothing()
        {
            PrepareRifleReload(7, 4);
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var actions = Game.Interface.GetSystem<PlayerActionSystem>();
            Game.Interface.SendEvent(new PlayerReloadRequestedEvent());
            actions.CancelActive(PlayerActionCancelReason.UserCanceled);
            actions.TickForTests(3f);
            Assert.That(bag.GetTotalAmount(26), Is.EqualTo(7));
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().RangedLoadedAmmo, Is.EqualTo(4));
            Game.Interface.SendEvent(new PlayerReloadRequestedEvent());
            actions.TickForTests(3f);
            Assert.That(bag.GetTotalAmount(26), Is.Zero);
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().RangedLoadedAmmo, Is.EqualTo(11));
        }

        public void RifleReloadOnlyConsumesMagazineGap()
        {
            PrepareRifleReload(60, 28);
            Game.Interface.SendEvent(new PlayerReloadRequestedEvent());
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(3f);
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().RangedLoadedAmmo, Is.EqualTo(30));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(26), Is.EqualTo(58));
        }

        [Test]
        public void ReloadWithoutReserveAmmoShowsLocalizedUniqueTipAndKeepsMagazine()
        {
            PrepareRaygunWithoutReserve(false);
            var config = Game.Interface.GetUtility<IRuntimeConfigProvider>();
            Assert.That(config.TryGetItem(26, out var ammo, out var error), Is.True, error);
            var expected = Texts.CombatAmmoInsufficient(Loc.T(ammo.NameKey));
            for (var i = 0; i < 5; i++) Game.Interface.SendEvent(new PlayerReloadRequestedEvent());
            var ctrl = UIHelper.GetUICtrl<FloatUICtrl>();
            Assert.That(ctrl, Is.Not.Null, "换弹缺弹必须显示现有本地化浮动提示");
            var ui = (FloatUI)typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ctrl);
            var tips = ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false);
            Assert.That(tips, Has.Length.EqualTo(1), "连续点击缺弹按钮不堆叠重复提示");
            Assert.That(tips[0].TextTips.text, Is.EqualTo(expected));
            Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out _), Is.False);
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().RangedLoadedAmmo, Is.Zero);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(26), Is.Zero);
        }

        [Test]
        public void ReloadFullMagazineWithoutReserveDoesNotShowAmmoTip()
        {
            PrepareRaygunWithoutReserve(true);
            var loaded = Game.Interface.GetModel<WeaponEquipmentModel>().RangedLoadedAmmo;
            Game.Interface.SendEvent(new PlayerReloadRequestedEvent());
            Assert.That(UIHelper.GetUICtrl<FloatUICtrl>(), Is.Null, "满弹匣不应误报背包缺弹");
            Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out _), Is.False);
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().RangedLoadedAmmo, Is.EqualTo(loaded));
        }

        private void PrepareRaygunWithoutReserve(bool fullMagazine)
        {
            CreateAndCommitSave();
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(CreateRealConfigProvider());
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(25, 1, 1, out _, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(0, out error), Is.True, error);
            Assert.That(Game.Interface.GetUtility<IRuntimeConfigProvider>().TryGetWeapon(25, out var weapon, out error), Is.True, error);
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().TrySetRangedLoadedAmmo(fullMagazine ? weapon.MagazineCapacity : 0, out error), Is.True, error);
        }

        [TestCase(0)]
        [TestCase(5)]
        public void ReloadIncludesPocketAndPreservesCancellation(int bagAmmo)
        {
            PrepareRifleReload(7, 4);
            var model = Game.Interface.GetModel<PlayerBagModel>();
            Assert.That(model.Container.TryRemove(1, 7 - bagAmmo, out _, out var error), Is.True, error);
            Assert.That(model.Pocket.TryAdd(26, 10, 60, out _, out error), Is.True, error);
            Game.Interface.SendEvent(new PlayerReloadRequestedEvent());
            var actions = Game.Interface.GetSystem<PlayerActionSystem>();
            Assert.That(actions.TryGetSnapshot(out _), Is.True);
            actions.CancelActive(PlayerActionCancelReason.UserCanceled);
            Assert.That(model.Container.GetTotalAmount(26), Is.EqualTo(bagAmmo));
            Assert.That(model.Pocket.GetTotalAmount(26), Is.EqualTo(10));
            Game.Interface.SendEvent(new PlayerReloadRequestedEvent());
            actions.TickForTests(3f);
            Assert.That(model.Container.GetTotalAmount(26), Is.Zero);
            Assert.That(model.Pocket.GetTotalAmount(26), Is.Zero);
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().RangedLoadedAmmo, Is.EqualTo(14 + bagAmmo));
        }

        private void PrepareRifleReload(int ammunition, int loaded)
        {
            CreateAndCommitSave();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(20, 1, 1, out _, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(0, out error), Is.True, error);
            Assert.That(bag.TryAdd(26, ammunition, 60, out _, out error), Is.True, error);
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().TrySetRangedLoadedAmmo(loaded, out error), Is.True, error);
        }

        private void EquipBowWithArrows(int arrowCount)
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Bow, 1, 1, out _, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(0, out error), Is.True, error);
            Assert.That(bag.TryAdd(ItemIds.Arrow, arrowCount, 20, out _, out error), Is.True, error);
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().ActiveSlot, Is.EqualTo(WeaponSlot.Ranged));
            SetWeaponCooldown(0f);
            SetSaveDirty(false);
        }

        private void CreateAndCommitSave()
        {
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var created, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(created, out error), Is.True, error);
        }

        private void CreateUiRoot()
        {
            var root = new GameObject("UIRoot");
            objects.Add(root);
            root.AddComponent<RectTransform>();
            root.AddComponent<Canvas>();
            root.AddComponent<CanvasScaler>();
            root.AddComponent<GraphicRaycaster>();
            objects.Add(new GameObject(SceneRoot.PoolRootName));

            CreateUiLayer(root.transform, "HUD");
            CreateUiLayer(root.transform, "Dialog");
            CreateUiLayer(root.transform, "Popup");
            CreateUiLayer(root.transform, "Tips");
            CreateUiLayer(root.transform, "Top");

            var eventSystem = new GameObject("EventSystem");
            objects.Add(eventSystem);
            eventSystem.transform.SetParent(root.transform, false);
            eventSystem.AddComponent<EventSystem>();
            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputModuleType != null)
            {
                eventSystem.AddComponent(inputModuleType);
            }

            root.AddComponent<UIRoot>();
        }

        private void CreateUiLayer(Transform parent, string layerName)
        {
            var layer = new GameObject(layerName);
            objects.Add(layer);
            layer.AddComponent<RectTransform>();
            layer.transform.SetParent(parent, false);
        }

        private MapContext CreateMapContext(string name)
        {
            var root = new GameObject(name + "-presentation");
            objects.Add(root);
            var runtime = new GameObject(name + "-runtime").transform;
            objects.Add(runtime.gameObject);
            runtime.SetParent(root.transform, false);
            var contextObject = new GameObject(name + "-context");
            objects.Add(contextObject);
            var context = contextObject.AddComponent<MapContext>();
            SetPrivateField(context, "mapContentRoot", root);
            SetPrivateField(context, "dynamicObjectRoot", runtime);
            return context;
        }

        private static MapPreparationContext CreateContext(MapContext context, long mapInstanceId)
        {
            return new MapPreparationContext(MapIds.Base, mapInstanceId, context, Array.Empty<MapSpawnDefinition>());
        }

        private static void PrepareAndCommit(ProjectileCtrl ctrl, MapContext context, long mapInstanceId)
        {
            var prepare = ctrl.PrepareMapAsync(CreateContext(context, mapInstanceId), default).GetAwaiter().GetResult();
            Assert.That(prepare.Success, Is.True, prepare.Error);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
        }

        private static int GetActiveViewCount(ProjectileCtrl ctrl)
        {
            return ((Dictionary<int, ProjectileView>)GetPrivateField(ctrl, "activeViews")).Count;
        }

        private Collider CreateCollider(string name, bool trigger)
        {
            var go = new GameObject(name);
            objects.Add(go);
            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = trigger;
            return collider;
        }

        private static void RegisterPlayer(Vector3 position)
        {
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                PlayerModel.LocalPlayerUnitId,
                MapInstanceId,
                UnitType.Player,
                UnitSource.Player,
                "player",
                position,
                Quaternion.identity,
                UnitAuthorityMode.Local,
                0,
                true), out var error), Is.True, error);
        }

        private static void MovePlayerUnitToMap(long mapInstanceId)
        {
            Game.Interface.GetSystem<UnitSystem>().Remove(PlayerModel.LocalPlayerUnitId, UnitRemovedReason.MapInstanceReleased);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                PlayerModel.LocalPlayerUnitId,
                mapInstanceId,
                UnitType.Player,
                UnitSource.Player,
                "player",
                Vector3.zero,
                Quaternion.identity,
                UnitAuthorityMode.Local,
                0,
                true), out var error), Is.True, error);
        }

        private static void RegisterEnemy(int unitId, long mapInstanceId, Vector3 position, int health)
        {
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                unitId,
                mapInstanceId,
                UnitType.Enemy,
                UnitSource.MapPlaced,
                "enemy",
                position,
                Quaternion.identity,
                UnitAuthorityMode.Local,
                0,
                true), out var error), Is.True, error);
            Game.Interface.GetModel<CombatModel>().RegisterOrResetCombatant(
                unitId,
                CombatFaction.Enemy,
                health,
                1,
                PlayerModel.LocalPlayerUnitId);
        }

        private static void InvokeAdvance(int projectileRuntimeId, float deltaTime)
        {
            var system = Game.Interface.GetSystem<ProjectileSystem>();
            var model = Game.Interface.GetModel<ProjectileModel>();
            Assert.That(model.Projectiles.TryGetValue(projectileRuntimeId, out var state), Is.True);
            typeof(ProjectileSystem)
                .GetMethod("Advance", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(system, new object[] { state, deltaTime });
        }

        private static float GetWeaponCooldown()
        {
            return (float)typeof(WeaponSystem)
                .GetField("cooldownRemainingSeconds", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(Game.Interface.GetSystem<WeaponSystem>());
        }

        private static void SetWeaponCooldown(float value)
        {
            typeof(WeaponSystem)
                .GetField("cooldownRemainingSeconds", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(Game.Interface.GetSystem<WeaponSystem>(), value);
        }

        private static bool IsSaveDirty()
        {
            return (bool)typeof(SaveSystem)
                .GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(Game.Interface.GetSystem<SaveSystem>());
        }

        private static void SetSaveDirty(bool value)
        {
            typeof(SaveSystem)
                .GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(Game.Interface.GetSystem<SaveSystem>(), value);
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            return target.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(target);
        }

        private static void SetPrivateField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
        {
            typeof(TTarget)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        private static Type RequireEditorType(string fullName)
        {
            var type = Type.GetType(fullName + ", Assembly-CSharp-Editor");
            if (type != null)
            {
                return type;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            Assert.Fail("Editor type not found: " + fullName);
            return null;
        }

        private static string ExtractMethodBody(string source, string methodName)
        {
            var match = Regex.Match(
                source,
                $@"(?:public|private|internal|protected)\s+(?:static\s+)?(?:[\w<>,\[\]\.?]+\s+)+{Regex.Escape(methodName)}\s*(?:<[^>]+>)?\s*\(");
            Assert.That(match.Success, Is.True, "method not found: " + methodName);
            var open = source.IndexOf('{', match.Index);
            Assert.That(open, Is.GreaterThanOrEqualTo(0), "method body open not found: " + methodName);
            var depth = 0;
            for (var i = open; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(open, i - open + 1);
                    }
                }
            }

            Assert.Fail("method body close not found: " + methodName);
            return string.Empty;
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private static LubanRuntimeConfigProvider CreateRealConfigProvider()
        {
            return new LubanRuntimeConfigProvider(CreateTables);
        }

        private static Tables CreateTables()
        {
            return new Tables(name => new Luban.ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", $"{name}.bytes"))));
        }

        private sealed class FakePlayerPort : ICharacterMotorPort
        {
            public int UnitId => PlayerModel.LocalPlayerUnitId;
            public long MapInstanceId => BowProjectileTests.MapInstanceId;
            public bool IsValid { get; set; } = true;
            public bool IsEnabled => true;
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; private set; } = Quaternion.identity;
            public float MovementYawDegrees => 0f;
            public bool IsGrounded => true;
            public int BowFireCount { get; private set; }
            public BoxCollider SelfCollider { get; set; }

            public void Move(Vector3 motion) { Position += motion; }
            public void RotateTowards(Quaternion rotation, float maxDegreesDelta) { Rotation = rotation; }
            public void FaceDirection(Vector3 direction)
            {
                if (direction.sqrMagnitude > 0.0001f)
                {
                    Rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                }
            }

            public void SetTiliRing(float current, float maximum, bool isSprinting, bool exhausted) { }
            public void SetWeaponPresentation(WeaponSlot activeSlot, bool showShortSword, bool showBow) { }
            public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin)
            {
                origin = Position + Vector3.up;
                return true;
            }

            public void FaceWorldHud(Vector3 cameraPosition, Vector3 cameraUp) { }
            public void PlayAttackFeedback() { }
            public void PlayWeaponSwingFeedback() { }
            public void PlayBowFireFeedback() { BowFireCount++; }
            public void PlayHurtFeedback() { }
            public void TickCombatFeedback(float deltaTime) { }
        }

        private sealed class FakePlayerPortProvider : IPlayerRuntimePortProvider
        {
            private readonly FakePlayerPort port;

            public FakePlayerPortProvider(FakePlayerPort port)
            {
                this.port = port;
            }

            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort motorPort)
            {
                motorPort = unitId == port.UnitId && mapInstanceId == port.MapInstanceId && port.IsValid
                    ? port
                    : null;
                return motorPort != null;
            }

            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort cameraPort)
            {
                cameraPort = unitId == port.UnitId && mapInstanceId == port.MapInstanceId && port.IsValid
                    ? new FakeCameraPort()
                    : null;
                return cameraPort != null;
            }

            public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider)
            {
                return unitId == port.UnitId && mapInstanceId == port.MapInstanceId && collider == port.SelfCollider;
            }
        }

        private sealed class FakeProjectilePort : IProjectileRuntimePortProvider
        {
            public bool CreateSucceeds { get; set; } = true;
            public int CreateCount { get; private set; }
            public int ReleaseCount { get; private set; }
            public readonly Dictionary<int, Vector3> Positions = new Dictionary<int, Vector3>();

            public bool TryCreateProjectile(int projectileRuntimeId, long mapInstanceId, Vector3 position, Vector3 direction, string prefabAddress, out string error)
            {
                CreateCount++;
                if (!CreateSucceeds)
                {
                    error = "blocked";
                    return false;
                }

                Positions[projectileRuntimeId] = position;
                error = null;
                return true;
            }

            public void MoveProjectile(int projectileRuntimeId, Vector3 position, Vector3 direction)
            {
                Positions[projectileRuntimeId] = position;
            }

            public void CollectMovementHits(int projectileRuntimeId, Vector3 from, Vector3 to, List<ProjectileMovementHit> results)
            {
                results.Clear();
            }

            public void ReleaseProjectile(int projectileRuntimeId)
            {
                ReleaseCount++;
                Positions.Remove(projectileRuntimeId);
            }
        }

        private sealed class FakeCameraPort : IPlayerCameraPort
        {
            public bool IsValid => true;
            public bool SupportsMode(PlayerCameraMode mode, out string error) { error = null; return true; }
            public void ApplyProfile() { }
            public void LateUpdate() { }
            public bool TryGetCameraPose(out Vector3 position, out Vector3 up)
            {
                position = Vector3.back;
                up = Vector3.up;
                return true;
            }
        }

        private sealed class TestResourceLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : UnityEngine.Object
            {
                if (typeof(T) == typeof(TextAsset))
                {
                    var localizationPath = Path.Combine("Assets/Game/Config/Localization", path);
                    if (File.Exists(localizationPath))
                    {
                        return new TextAsset(File.ReadAllText(localizationPath)) as T;
                    }
                }

                var assetPath = Path.Combine("Assets/Game", path + ".prefab").Replace("\\", "/");
                return AssetDatabase.LoadAssetAtPath<T>(assetPath);
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            {
                var assetPath = Path.Combine("Assets/Game", path + ".prefab").Replace("\\", "/");
                onLoaded?.Invoke(AssetDatabase.LoadAssetAtPath<T>(assetPath));
                return new ResourceLoadHandle(new object());
            }

            public void Release(ResourceLoadHandle handle) { }
        }

        private sealed class FakeEnemyResolver : IEnemyColliderResolver
        {
            private readonly Dictionary<Collider, int> map = new Dictionary<Collider, int>();

            public void Bind(Collider collider, int unitId)
            {
                map[collider] = unitId;
            }

            public bool TryResolveEnemyUnitId(Collider collider, out int unitId)
            {
                return map.TryGetValue(collider, out unitId);
            }
        }

        private sealed class TestConfigProvider : IRuntimeConfigProvider
        {
            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                if (id == 20 || id == 26) return CreateRealConfigProvider().TryGetItem(id, out value, out error);
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
                        value = new ItemDefinition(id, "Arrow", string.Empty, "item.ammunition.arrow.name", string.Empty, string.Empty, ItemType.Ammunition, 20, ItemUseEffect.None, 0);
                        error = null;
                        return true;
                    default:
                        value = default;
                        error = "missing";
                        return false;
                }
            }

            public bool TryGetWeapon(int itemId, out WeaponConfigDefinition value, out string error)
            {
                if (itemId == 20) return CreateRealConfigProvider().TryGetWeapon(itemId, out value, out error);
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
                error = "missing";
                return false;
            }

            public bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error)
            {
                if (id == "projectile.arrow.basic")
                {
                    value = new ProjectileConfigDefinition(id, "Prefabs/Combat/GrayboxArrow", 14f, 14f, "loot.ammunition.arrow");
                    error = null;
                    return true;
                }

                value = default;
                error = "missing";
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
                error = "missing";
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
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
