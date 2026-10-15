using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Tests
{
    public sealed class ArcherEnemyAndGroupTests
    {
        private const long MapInstanceId = 7;
        private const string BasicEnemyId = "enemy.graybox.basic";
        private const string ArcherEnemyId = "enemy.graybox.archer";
        private const string ArcherPrefabPath = "Assets/Game/Prefabs/Characters/GrayboxArcher.prefab";
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private NavMeshDataInstance navMeshDataInstance;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            ResourceLoader.ResetForTests();
            if (navMeshDataInstance.valid)
            {
                navMeshDataInstance.Remove();
            }

            for (var i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(objects[i]);
                }
            }

            objects.Clear();
            TestReset.ResetArchitecture();
        }

        [Test]
        public void EnemyConfigsAreFixedAndValid()
        {
            var provider = CreateRealConfigProvider();
            Assert.That(provider.TryGetEnemy(BasicEnemyId, out var basic, out var error), Is.True, error);
            Assert.That(basic.PrefabAddress, Is.EqualTo("Prefabs/Characters/GrayboxEnemy"));
            Assert.That(basic.MaxHP, Is.EqualTo(20));
            Assert.That(basic.AttackDamage, Is.EqualTo(10));
            Assert.That(basic.AggroRange, Is.EqualTo(8f).Within(0.001f));
            Assert.That(basic.AttackRange, Is.EqualTo(1.35f).Within(0.001f));
            Assert.That(basic.AttackCooldownSeconds, Is.EqualTo(1f).Within(0.001f));
            Assert.That(basic.MoveSpeed, Is.EqualTo(2.2f).Within(0.001f));
            Assert.That(basic.LootConfigId, Is.EqualTo("loot.metal-parts"));
            Assert.That(basic.AttackKind, Is.EqualTo(EnemyAttackKind.Melee));
            Assert.That(basic.RetreatDistance, Is.EqualTo(0f).Within(0.001f));
            Assert.That(basic.TelegraphSeconds, Is.EqualTo(0f).Within(0.001f));
            Assert.That(basic.ProjectileConfigId, Is.Empty);
            Assert.That(basic.MagazineCapacity, Is.EqualTo(0));
            Assert.That(basic.ReloadSeconds, Is.EqualTo(0f).Within(0.001f));

            Assert.That(provider.TryGetEnemy(ArcherEnemyId, out var archer, out error), Is.True, error);
            Assert.That(archer.PrefabAddress, Is.EqualTo("Prefabs/Characters/GrayboxArcher"));
            Assert.That(archer.MaxHP, Is.EqualTo(15));
            Assert.That(archer.AttackDamage, Is.EqualTo(8));
            Assert.That(archer.AggroRange, Is.EqualTo(12f).Within(0.001f));
            Assert.That(archer.AttackRange, Is.EqualTo(8f).Within(0.001f));
            Assert.That(archer.RetreatDistance, Is.EqualTo(3f).Within(0.001f));
            Assert.That(archer.TelegraphSeconds, Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(archer.AttackCooldownSeconds, Is.EqualTo(2f).Within(0.001f));
            Assert.That(archer.MoveSpeed, Is.EqualTo(1.8f).Within(0.001f));
            Assert.That(archer.ProjectileConfigId, Is.EqualTo("projectile.arrow.basic"));
            Assert.That(archer.LootConfigId, Is.EqualTo("loot.metal-parts"));
            Assert.That(archer.MagazineCapacity, Is.EqualTo(1));
            Assert.That(archer.ReloadSeconds, Is.EqualTo(1f).Within(0.001f));
            Assert.That(provider.TryGetProjectile(archer.ProjectileConfigId, out _, out error), Is.True, error);
        }

        [Test]
        public void ArcherStateMachineIsDeterministic()
        {
            RegisterRuntimeUtilities(out var projectilePort, out _);
            var archer = ArcherConfig();
            RegisterPlayer(Vector3.zero);
            RegisterEnemy(1000, new Vector3(0f, 0f, 9f), archer);
            var port = new TestEnemyPort(1000, MapInstanceId, new Vector3(0f, 0f, 9f));
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryRegisterEnemy(port, archer, out var error), Is.True, error);
            StartPlaying();

            InvokeEnemyTick(1000, 0f);
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetIntent(1000, out var intent), Is.True);
            Assert.That(intent, Is.EqualTo(EnemyIntent.Chase));
            AssertVector3(port.LastDestination, Vector3.zero);

            port.Position = new Vector3(0f, 0f, 2.5f);
            InvokeEnemyTick(1000, 0f);
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetIntent(1000, out intent), Is.True);
            Assert.That(intent, Is.EqualTo(EnemyIntent.Retreat));
            Assert.That(port.LastRetreatRequest.z, Is.GreaterThan(2.5f));

            port.CanRetreat = false;
            port.Position = new Vector3(0f, 0f, 5f);
            InvokeEnemyTick(1000, 0f);
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetIntent(1000, out intent), Is.True);
            Assert.That(intent, Is.EqualTo(EnemyIntent.Telegraph));
            Assert.That(port.TelegraphVisible, Is.True);

            Game.Interface.GetSystem<UnitSystem>().TryUpdateTransform(
                PlayerModel.LocalPlayerUnitId,
                new Vector3(5f, 0f, 0f),
                Quaternion.identity);
            InvokeEnemyTick(1000, 1f);

            Assert.That(projectilePort.CreateCount, Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<ProjectileModel>().Projectiles.TryGetValue(projectilePort.LastProjectileId, out var projectile), Is.True);
            Assert.That(projectile.Damage, Is.EqualTo(8));
            AssertVector3(projectilePort.LastDirection.normalized, (Vector3.zero - port.Position).normalized);
            Assert.That(port.TelegraphVisible, Is.False);
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetRemainingAmmo(1000, out var remainingAmmo), Is.True);
            Assert.That(remainingAmmo, Is.EqualTo(9));
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetReloadProgress(1000, out var reloadProgress, out _), Is.True);
            Assert.That(reloadProgress, Is.EqualTo(0f).Within(0.001f));

            InvokeEnemyTick(1000, 1f);
            Assert.That(projectilePort.CreateCount, Is.EqualTo(1));
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetReloadProgress(1000, out _, out _), Is.False);
            InvokeEnemyTick(1000, 1f);
            Assert.That(port.TelegraphVisible, Is.True);
            InvokeEnemyTick(1000, 1f);
            Assert.That(projectilePort.CreateCount, Is.EqualTo(2));
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetRemainingAmmo(1000, out remainingAmmo), Is.True);
            Assert.That(remainingAmmo, Is.EqualTo(8));
        }

        [Test]
        public void ArcherInvalidRetreatFallsBackToTelegraph()
        {
            RegisterRuntimeUtilities(out _, out _);
            RegisterPlayer(Vector3.zero);
            var archer = ArcherConfig();
            RegisterEnemy(1000, new Vector3(0f, 0f, 2.5f), archer);
            var port = new TestEnemyPort(1000, MapInstanceId, new Vector3(0f, 0f, 2.5f))
            {
                RetreatDestinationOverride = new Vector3(0f, 0f, 2.5f)
            };
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryRegisterEnemy(port, archer, out var error), Is.True, error);
            StartPlaying();

            InvokeEnemyTick(1000, 0f);
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetIntent(1000, out var intent), Is.True);
            Assert.That(intent, Is.EqualTo(EnemyIntent.Telegraph));
            Assert.That(port.TelegraphVisible, Is.True);

            Game.Interface.GetSystem<EnemySystem>().CancelTelegraph(1000);
            port.TelegraphVisible = false;
            port.Position = new Vector3(0f, 0f, 2.5f);
            port.RetreatDestinationOverride = new Vector3(0f, 0f, 1.5f);

            InvokeEnemyTick(1000, 0f);
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetIntent(1000, out intent), Is.True);
            Assert.That(intent, Is.EqualTo(EnemyIntent.Telegraph));
            Assert.That(port.LastDestination, Is.EqualTo(Vector3.zero));
            Assert.That(port.TelegraphVisible, Is.True);
        }

        [Test]
        public void ArcherProjectileUsesCapturedPosition()
        {
            RegisterRuntimeUtilities(out var projectilePort, out var playerProvider);
            RegisterPlayer(Vector3.zero);
            var archer = ArcherConfig();
            RegisterEnemy(1000, new Vector3(0f, 0f, 5f), archer);
            var port = new TestEnemyPort(1000, MapInstanceId, new Vector3(0f, 0f, 5f))
            {
                CanRetreat = false,
                ProjectileOriginOffset = new Vector3(1.1f, 0f, 0.65f)
            };
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryRegisterEnemy(port, archer, out var error), Is.True, error);
            StartPlaying();

            InvokeEnemyTick(1000, 0f);
            Game.Interface.GetSystem<UnitSystem>().TryUpdateTransform(
                PlayerModel.LocalPlayerUnitId,
                new Vector3(4f, 0f, 0f),
                Quaternion.identity);
            InvokeEnemyTick(1000, 1f);

            var expectedOrigin = port.Position + port.ProjectileOriginOffset;
            var expectedDirection = Vector3.zero - expectedOrigin;
            expectedDirection.y = 0f;
            AssertVector3(projectilePort.LastDirection.normalized, expectedDirection.normalized);
            var playerCollider = CreateCollider("PlayerCollider", true);
            playerProvider.PlayerCollider = playerCollider;
            Game.Interface.GetSystem<ProjectileSystem>().HandleCollision(projectilePort.LastProjectileId, playerCollider);
            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(PlayerModel.LocalPlayerUnitId, out var player), Is.True);
            Assert.That(player.HP, Is.EqualTo(92));
            Game.Interface.GetSystem<ProjectileSystem>().HandleCollision(projectilePort.LastProjectileId, playerCollider);
            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(PlayerModel.LocalPlayerUnitId, out player), Is.True);
            Assert.That(player.HP, Is.EqualTo(92));
        }

        [Test]
        public void DeactivatingEnemyMapCancelsUnfinishedTelegraph()
        {
            RegisterRuntimeUtilities(out var projectilePort, out _);
            RegisterPlayer(Vector3.zero);
            var archer = ArcherConfig();
            RegisterEnemy(1000, new Vector3(0f, 0f, 5f), archer);
            var port = new TestEnemyPort(1000, MapInstanceId, new Vector3(0f, 0f, 5f)) { CanRetreat = false };
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryRegisterEnemy(port, archer, out var error), Is.True, error);
            StartPlaying();

            InvokeEnemyTick(1000, 0f);
            Assert.That(port.TelegraphVisible, Is.True);
            Game.Interface.GetSystem<EnemySystem>().CancelTelegraphForMap(MapInstanceId);
            Assert.That(port.TelegraphVisible, Is.False);

            InvokeEnemyTick(1000, 1f);
            Assert.That(projectilePort.CreateCount, Is.EqualTo(0));

            InvokeEnemyTick(1000, 0f);
            Assert.That(port.TelegraphVisible, Is.True);
            InvokeEnemyTick(1000, 1f);
            Assert.That(projectilePort.CreateCount, Is.EqualTo(1));
        }

        [Test]
        public void ArcherPrefabContractIsWired()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArcherPrefabPath);
            Assert.NotNull(prefab);
            var view = prefab.GetComponent<EnemyView>();
            Assert.NotNull(view);
            Assert.NotNull(prefab.GetComponent<Collider>());
            var agent = prefab.GetComponent<NavMeshAgent>();
            Assert.NotNull(agent);
            Assert.That(agent.radius, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(agent.avoidancePriority, Is.EqualTo(50));
            Assert.NotNull(view.HealthBarRoot);
            Assert.NotNull(view.HealthBarFill);
            var actionProgress = typeof(EnemyView)
                .GetField("actionProgressView", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(view);
            Assert.NotNull(actionProgress, "弓箭手必须包含 UnitActionProgressView。");
            Assert.NotNull(
                actionProgress.GetType().GetField("root", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(actionProgress),
                "弓箭手必须引用既有 UnitActionProgress.prefab。");
            var reloadProgress = prefab.transform.Find("ReloadProgress") as RectTransform;
            Assert.NotNull(reloadProgress);
            var sourceProgressPrefab = PrefabUtility.GetCorrespondingObjectFromSource(reloadProgress.gameObject);
            Assert.NotNull(sourceProgressPrefab, "ReloadProgress 必须保持为嵌套 Prefab 实例。");
            Assert.That(
                AssetDatabase.GetAssetPath(sourceProgressPrefab),
                Is.EqualTo("Assets/Game/Prefabs/UI/World/UnitActionProgress.prefab"));
            Assert.NotNull(prefab.transform.Find("ArcherBow"));
            var firePoint = (Transform)GetPrivateField(view, "projectileFirePoint");
            Assert.NotNull(firePoint);
            Assert.That(firePoint.localPosition, Is.EqualTo(new Vector3(0f, 0.4f, 0.65f)));
            Assert.That(firePoint.position.y - prefab.transform.position.y, Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(reloadProgress.localScale, Is.EqualTo(new Vector3(0.01f, 0.01f, 0.01f)));
            Assert.That(reloadProgress.localPosition.y, Is.EqualTo(1.4f).Within(0.001f));
            Assert.That(reloadProgress.gameObject.activeSelf, Is.False);

            var profile = AssetDatabase.LoadAssetAtPath<TopDownCameraProfile>(
                "Assets/Game/Config/DefaultCameraProfile.asset");
            Assert.NotNull(profile);
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var instanceView = instance.GetComponent<EnemyView>();
                var instanceProgress = instance.transform.Find("ReloadProgress") as RectTransform;
                Assert.NotNull(instanceView);
                Assert.NotNull(instanceProgress);
                instanceView.SetActionProgressVisible(true, 0.5f);

                var pitch = Mathf.Clamp(profile.PitchDegrees, 65f, 75f);
                var yaw = profile.YawDegrees;
                var distance = Mathf.Max(0.1f, profile.FollowDistance);
                var cameraRotation = Quaternion.Euler(pitch, yaw, 0f);
                var cameraPosition = instance.transform.position - cameraRotation * Vector3.forward * distance;
                var cameraUp = cameraRotation * Vector3.up;
                instanceView.FaceWorldHuds(cameraPosition, cameraUp);

                var corners = new Vector3[4];
                instanceProgress.GetWorldCorners(corners);
                var width = Vector3.Distance(corners[0], corners[3]);
                var height = Vector3.Distance(corners[0], corners[1]);
                Assert.That(width, Is.InRange(0.45f, 0.7f));
                Assert.That(height, Is.InRange(0.45f, 0.7f));
                Assert.That(width / height, Is.InRange(0.8f, 1.2f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
            var telegraph = (GameObject)GetPrivateField(view, "telegraphObject");
            Assert.NotNull(telegraph);
            Assert.That(telegraph.activeSelf, Is.False);

            var basic = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxEnemy.prefab");
            Assert.NotNull(basic);
            var basicView = basic.GetComponent<EnemyView>();
            Assert.NotNull(basicView);
            Assert.That(GetPrivateField(basicView, "projectileFirePoint"), Is.Null);
            Assert.That(GetPrivateField(basicView, "telegraphObject"), Is.Null);
        }

        [Test]
        public void SurvivalGroupsAndRollbackAreExact()
        {
            var provider = CreateRealConfigProvider();
            Assert.That(provider.TryGetMapSpawns(MapIds.SurvivalSample, out var spawns, out var error), Is.True, error);
            var enemies = spawns.Where(spawn => spawn.ObjectType == MapSpawnObjectType.Enemy).ToArray();
            Assert.That(enemies.Select(item => item.Id), Is.EqualTo(new[]
            {
                "enemy.survival.left.melee-a",
                "enemy.survival.left.melee-b",
                "enemy.survival.left.archer",
                "enemy.survival.right.melee-a",
                "enemy.survival.right.melee-b",
                "enemy.survival.right.archer"
            }));
            AssertSpawn(enemies[0], BasicEnemyId, -12f, -1f);
            AssertSpawn(enemies[1], BasicEnemyId, -8f, 1f);
            AssertSpawn(enemies[2], ArcherEnemyId, -12f, 6f);
            AssertSpawn(enemies[3], BasicEnemyId, 7f, 4f);
            AssertSpawn(enemies[4], BasicEnemyId, 11f, 7f);
            AssertSpawn(enemies[5], ArcherEnemyId, 10f, 12f);

            AddFlatNavMesh(new Bounds(Vector3.zero, new Vector3(80f, 4f, 80f)));
            RegisterCtrlUtilities(provider, new AssetResourceLoader());
            var ctrl = new EnemyCtrl(Game.Interface);
            var context = CreateContext(enemies);
            var prepare = ctrl.PrepareMapAsync(context, default).GetAwaiter().GetResult();
            Assert.That(prepare.Success, Is.True, prepare.Error);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(6));
            ctrl.ReleaseAll();

            var failingLoader = new AssetResourceLoader { FailArcher = true };
            TestReset.ResetArchitecture();
            RegisterCtrlUtilities(provider, failingLoader);
            ctrl = new EnemyCtrl(Game.Interface);
            context = CreateContext(enemies);
            prepare = ctrl.PrepareMapAsync(context, default).GetAwaiter().GetResult();
            Assert.That(prepare.Success, Is.False);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(1000, out _), Is.False);
            ctrl.ReleaseAll();

            TestReset.ResetArchitecture();
            var navMeshFailingProvider = new TestViewInstanceProvider(objects);
            RegisterCtrlUtilities(provider, new AssetResourceLoader(), navMeshFailingProvider);
            ctrl = new EnemyCtrl(Game.Interface);
            context = CreateContext(enemies);
            prepare = ctrl.PrepareMapAsync(context, default).GetAwaiter().GetResult();
            Assert.That(prepare.Success, Is.True, prepare.Error);
            navMeshDataInstance.Remove();
            Assert.That(ctrl.CommitPreparedMap().Success, Is.False);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetUtility<ViewCtrlRegistry>().TryGet(1, out _), Is.False);
            Assert.That(navMeshFailingProvider.ReleaseCount, Is.EqualTo(6));
            ctrl.ReleaseAll();

            AddFlatNavMesh(new Bounds(Vector3.zero, new Vector3(80f, 4f, 80f)));
            TestReset.ResetArchitecture();
            var commitFailingProvider = new TestViewInstanceProvider(objects);
            RegisterCtrlUtilities(provider, new AssetResourceLoader(), commitFailingProvider);
            ctrl = new EnemyCtrl(Game.Interface);
            context = CreateContext(enemies);
            prepare = ctrl.PrepareMapAsync(context, default).GetAwaiter().GetResult();
            Assert.That(prepare.Success, Is.True, prepare.Error);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                1001,
                MapInstanceId,
                UnitType.Enemy,
                UnitSource.MapPlaced,
                BasicEnemyId,
                Vector3.zero,
                Quaternion.identity,
                UnitAuthorityMode.Local,
                0,
                true), out _), Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.False);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(1));
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(1001, out _), Is.True);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(1000, out _), Is.False);
            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(1000, out _), Is.False);
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetIntent(1000, out _), Is.False);
            Assert.That(GetEnemyColliderMapCount(ctrl), Is.EqualTo(0));
            Assert.That(Game.Interface.GetUtility<ViewCtrlRegistry>().TryGet(1, out _), Is.False);
            Assert.That(commitFailingProvider.ReleaseCount, Is.EqualTo(6));
            ctrl.ReleaseAll();
        }

        [Test]
        public void FirstVersionNavigationBoundaryIsPreserved()
        {
            Assert.That(EnemySystem.ResolveProjectileIntent(13f, 12f, 8f, 3f), Is.EqualTo(EnemyIntent.Idle));
            Assert.That(EnemySystem.ResolveProjectileIntent(9f, 12f, 8f, 3f), Is.EqualTo(EnemyIntent.Chase));
            Assert.That(EnemySystem.ResolveProjectileIntent(5f, 12f, 8f, 3f), Is.EqualTo(EnemyIntent.HoldRange));
            Assert.That(EnemySystem.ResolveProjectileIntent(2f, 12f, 8f, 3f), Is.EqualTo(EnemyIntent.Retreat));
            AssertVector3(
                EnemySystem.CalculateRetreatRequest(
                    new Vector3(0f, 0f, 2f),
                    new Vector3(0f, 0f, -2f),
                    3f,
                    2f),
                new Vector3(0f, 0f, 4f));

            var source = File.ReadAllText("Assets/Game/Scripts/System/Combat/EnemySystem.cs");
            Assert.That(source, Does.Not.Contain("OverlapSphere"));
            Assert.That(source, Does.Not.Contain("ObjectPool"));
            Assert.That(source, Does.Not.Contain("separation"));
            Assert.That(source, Does.Not.Contain("wave"));
        }

        private static void StartPlaying()
        {
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Game.Interface.GetModel<ExpeditionModel>().BeginSurvival(MapIds.SurvivalSample, 60f);
        }

        private void RegisterRuntimeUtilities(out FakeProjectilePort projectilePort, out FakePlayerPortProvider playerProvider)
        {
            projectilePort = new FakeProjectilePort();
            playerProvider = new FakePlayerPortProvider();
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(new TestRuntimeConfigProvider());
            Game.Interface.RegisterUtility<IProjectileRuntimePortProvider>(projectilePort);
            Game.Interface.RegisterUtility<IPlayerRuntimePortProvider>(playerProvider);
            Game.Interface.RegisterUtility<IEnemyColliderResolver>(new FakeEnemyResolver());
            _ = Game.Interface.GetSystem<ProjectileSystem>();
            _ = Game.Interface.GetSystem<EnemySystem>();
            _ = Game.Interface.GetSystem<CombatSystem>();
        }

        private void RegisterCtrlUtilities(IRuntimeConfigProvider configProvider, IResourceLoader loader)
        {
            RegisterCtrlUtilities(configProvider, loader, new TestViewInstanceProvider(objects));
        }

        private void RegisterCtrlUtilities(
            IRuntimeConfigProvider configProvider,
            IResourceLoader loader,
            TestViewInstanceProvider viewInstanceProvider)
        {
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(configProvider);
            Game.Interface.RegisterUtility<IViewInstanceProvider>(viewInstanceProvider);
            ResourceLoader.SetLoaderForTests(loader);
        }

        private void AddFlatNavMesh(Bounds bounds)
        {
            if (navMeshDataInstance.valid)
            {
                navMeshDataInstance.Remove();
            }

            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one),
                    size = new Vector3(bounds.size.x, 0.1f, bounds.size.z),
                    area = 0
                }
            };
            var settings = NavMesh.GetSettingsByID(0);
            var data = NavMeshBuilder.BuildNavMeshData(
                settings,
                sources,
                bounds,
                Vector3.zero,
                Quaternion.identity);
            navMeshDataInstance = NavMesh.AddNavMeshData(data);
        }

        private static EnemyConfigDefinition ArcherConfig()
        {
            return new EnemyConfigDefinition(
                ArcherEnemyId,
                "Prefabs/Characters/GrayboxArcher",
                15,
                8,
                12f,
                8f,
                2f,
                1.8f,
                "loot.metal-parts",
                EnemyAttackKind.Projectile,
                3f,
                0.8f,
                "projectile.arrow.basic",
                "loot.weapon.bow",
                "loot.ammunition.arrow",
                10,
                1.35f,
                1,
                1f);
        }

        private static void RegisterPlayer(Vector3 position)
        {
            Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                PlayerModel.LocalPlayerUnitId,
                MapInstanceId,
                UnitType.Player,
                UnitSource.Player,
                "player.graybox.default",
                position,
                Quaternion.identity,
                UnitAuthorityMode.Local,
                0,
                true), out _);
            Game.Interface.GetModel<CombatModel>().RegisterOrResetCombatant(
                PlayerModel.LocalPlayerUnitId,
                CombatFaction.Player,
                100,
                10,
                0);
        }

        private static void RegisterEnemy(int unitId, Vector3 position, EnemyConfigDefinition config)
        {
            Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                unitId,
                MapInstanceId,
                UnitType.Enemy,
                UnitSource.MapPlaced,
                config.Id,
                position,
                Quaternion.identity,
                UnitAuthorityMode.Local,
                0,
                true), out _);
            Game.Interface.GetSystem<CombatSystem>().TryRegisterEnemy(unitId, config, out _);
        }

        private Collider CreateCollider(string name, bool trigger)
        {
            var go = new GameObject(name);
            objects.Add(go);
            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = trigger;
            return collider;
        }

        private MapPreparationContext CreateContext(IReadOnlyList<MapSpawnDefinition> spawns)
        {
            var contextObject = new GameObject("MapContext", typeof(MapContext));
            objects.Add(contextObject);
            var root = new GameObject("MapContentRoot");
            objects.Add(root);
            var runtime = new GameObject("DynamicObjectRoot").transform;
            objects.Add(runtime.gameObject);
            runtime.SetParent(root.transform, false);
            var mapContext = contextObject.GetComponent<MapContext>();
            SetPrivateField(mapContext, "mapContentRoot", root);
            SetPrivateField(mapContext, "dynamicObjectRoot", runtime);
            return new MapPreparationContext(MapIds.SurvivalSample, MapInstanceId, mapContext, spawns);
        }

        private static void AssertSpawn(MapSpawnDefinition spawn, string configId, float x, float z)
        {
            Assert.That(spawn.ConfigId, Is.EqualTo(configId));
            Assert.That(spawn.PositionX, Is.EqualTo(x).Within(0.001f));
            Assert.That(spawn.PositionY, Is.EqualTo(1f).Within(0.001f));
            Assert.That(spawn.PositionZ, Is.EqualTo(z).Within(0.001f));
            Assert.That(spawn.RotationY, Is.EqualTo(180f).Within(0.001f));
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            return target.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(target);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            target.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        private static int GetEnemyColliderMapCount(EnemyCtrl ctrl)
        {
            var field = typeof(EnemyCtrl).GetField(
                "colliderUnitIds",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var value = field?.GetValue(ctrl) as System.Collections.IDictionary;
            return value?.Count ?? -1;
        }

        private static LubanRuntimeConfigProvider CreateRealConfigProvider()
        {
            return new LubanRuntimeConfigProvider(() =>
                new Tables(table => new Luban.ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", $"{table}.bytes")))));
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f));
        }

        private static void InvokeEnemyTick(int unitId, float deltaTime)
        {
            var system = Game.Interface.GetSystem<EnemySystem>();
            var enemies = typeof(EnemySystem)
                .GetField("enemies", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(system);
            var state = enemies.GetType()
                .GetProperty("Item")
                ?.GetValue(enemies, new object[] { unitId });
            Assert.NotNull(state);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var player), Is.True);
            typeof(EnemySystem)
                .GetMethod("TickEnemy", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(system, new object[] { unitId, state, player, deltaTime });
        }

        private sealed class TestEnemyPort : IEnemyNavigationPort
        {
            public TestEnemyPort(int unitId, long mapInstanceId, Vector3 position)
            {
                UnitId = unitId;
                MapInstanceId = mapInstanceId;
                Position = position;
            }

            public int UnitId { get; }
            public long MapInstanceId { get; }
            public bool IsValid { get; set; } = true;
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; } = Quaternion.identity;
            public bool CanRetreat { get; set; } = true;
            public bool TelegraphVisible { get; set; }
            public Vector3 ProjectileOriginOffset { get; set; }
            public Vector3? RetreatDestinationOverride { get; set; }
            public Vector3 LastDestination { get; private set; }
            public Vector3 LastRetreatRequest { get; private set; }

            public bool IsOnNavigableSurface(float sampleRadius)
            {
                return true;
            }

            public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin)
            {
                origin = Position + ProjectileOriginOffset;
                return true;
            }

            public bool TrySetRetreatDestination(Vector3 requestedPosition, float sampleRadius, out Vector3 destination)
            {
                LastRetreatRequest = requestedPosition;
                destination = RetreatDestinationOverride ?? requestedPosition;
                return CanRetreat;
            }

            public void PlayAttackAnimation() { }

            public void SetTelegraphVisible(bool visible)
            {
                TelegraphVisible = visible;
            }

            public void SetDestination(Vector3 position)
            {
                LastDestination = position;
                Position = position;
            }

            public void Stop() { }
        }

        private sealed class FakeProjectilePort : IProjectileRuntimePortProvider
        {
            public int CreateCount { get; private set; }
            public int LastProjectileId { get; private set; }
            public Vector3 LastDirection { get; private set; }

            public bool TryCreateProjectile(int projectileRuntimeId, long mapInstanceId, Vector3 position, Vector3 direction, string prefabAddress, out string error)
            {
                CreateCount++;
                LastProjectileId = projectileRuntimeId;
                LastDirection = direction;
                error = null;
                return true;
            }

            public void MoveProjectile(int projectileRuntimeId, Vector3 position, Vector3 direction) { }
            public void CollectMovementHits(int projectileRuntimeId, Vector3 from, Vector3 to, List<ProjectileMovementHit> results)
            {
                results.Clear();
            }

            public void ReleaseProjectile(int projectileRuntimeId) { }
        }

        private sealed class FakePlayerPortProvider : IPlayerRuntimePortProvider
        {
            public Collider PlayerCollider { get; set; }

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
                return unitId == PlayerModel.LocalPlayerUnitId &&
                       mapInstanceId == MapInstanceId &&
                       collider != null &&
                       collider == PlayerCollider;
            }
        }

        private sealed class FakeEnemyResolver : IEnemyColliderResolver
        {
            public bool TryResolveEnemyUnitId(Collider collider, out int unitId)
            {
                unitId = 0;
                return false;
            }
        }

        private sealed class TestRuntimeConfigProvider : IRuntimeConfigProvider
        {
            public bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error)
            {
                value = new ProjectileConfigDefinition(id, "Prefabs/Combat/GrayboxArrow", 14f, 14f);
                error = null;
                return id == "projectile.arrow.basic";
            }

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
                value = ArcherConfig();
                error = null;
                return id == ArcherEnemyId;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                value = new LootConfigDefinition(id, "loot", ItemIds.MetalParts, 1);
                error = null;
                return id == "loot.metal-parts" ||
                       id == "loot.weapon.bow" ||
                       id == "loot.ammunition.arrow";
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }
        }

        private sealed class TestViewInstanceProvider : IViewInstanceProvider
        {
            private readonly List<UnityEngine.Object> objects;
            public int ReleaseCount { get; private set; }

            public TestViewInstanceProvider(List<UnityEngine.Object> objects)
            {
                this.objects = objects;
            }

            public T Instantiate<T>(T prefab, Transform parent, Vector3 position, Quaternion rotation) where T : Component
            {
                var instance = UnityEngine.Object.Instantiate(prefab, position, rotation, parent);
                objects.Add(instance.gameObject);
                return instance;
            }

            public void Release(GameObject instance)
            {
                ReleaseCount++;
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        private sealed class AssetResourceLoader : IResourceLoader
        {
            public bool FailArcher { get; set; }

            public T LoadAsset<T>(string path) where T : UnityEngine.Object
            {
                return Load(path) as T;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            {
                onLoaded?.Invoke(Load(path) as T);
                return new ResourceLoadHandle(new object());
            }

            public void Release(ResourceLoadHandle handle) { }

            private UnityEngine.Object Load(string path)
            {
                if (FailArcher && path == "Prefabs/Characters/GrayboxArcher")
                {
                    return null;
                }

                return AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab");
            }
        }
    }
}
