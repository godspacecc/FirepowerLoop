using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using QFramework;

namespace Game.Tests
{
    /// <summary>通过正式生成目录验证休闲路线与终局边界，不写产品存档、不改场景资源。</summary>
    public sealed class ArcadeCasualFinalBossTests
    {
        private static ArcadeConfigCatalogDefinition Catalog()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out var value, out var error), Is.True, error);
            return value;
        }

        private static object Read(object value, string name)
        {
            var property = value.GetType().GetProperty(name);
            Assert.That(property, Is.Not.Null, name + " 尚未接入正式运行时合同");
            return property.GetValue(value);
        }

        [Test]
        public void GeneratedRoute_AllPathsHaveTenFourRoundArenasAndEndAtFinalBoss()
        {
            var catalog = Catalog();
            var arenas = catalog.Arenas.ToDictionary(value => value.ArenaNumber);
            var path = new HashSet<int>();
            void Visit(int id, int depth)
            {
                Assert.That(path.Add(id), Is.True, "路线不能循环");
                Assert.That(arenas.ContainsKey(id), Is.True, "出口引用不存在的竞技场");
                var arena = arenas[id];
                Assert.That(arena.WaveGroupIds.Count, Is.EqualTo(4));
                var next = (IReadOnlyList<int>)Read(arena, "NextArenaNumbers");
                if (next.Count == 0)
                {
                    Assert.That(depth, Is.EqualTo(10), "每条休闲路线须恰好四十轮");
                    Assert.That(Read(arena, "FinalBossId"), Is.EqualTo("arcade.boss.final"));
                }
                else
                {
                    Assert.That(depth, Is.LessThan(10));
                    foreach (var destination in next) Visit(destination, depth + 1);
                }
                path.Remove(id);
            }
            Visit(1, 1);
        }

        [Test]
        public void GeneratedFinalBoss_HasChargeRayAndOrdinaryReinforcements()
        {
            var catalog = Catalog();
            Assert.That(catalog.Bosses.Any(value => value.Id == "arcade.boss.final"), Is.True,
                "正式配置尚无独立最终 Boss");
            var boss = catalog.Bosses.Single(value => value.Id == "arcade.boss.final");
            Assert.That(boss.TelegraphSeconds, Is.GreaterThan(0));
            Assert.That(boss.ChargeSpeedMultiplier, Is.GreaterThan(1));
            Assert.That((float)Read(boss, "RayIntervalSeconds"), Is.GreaterThan(0));
            Assert.That((float)Read(boss, "RayTelegraphSeconds"), Is.GreaterThan(0));
            Assert.That((float)Read(boss, "RaySpeed"), Is.GreaterThan(0));
            var reinforcement = (string)Read(boss, "ReinforcementGroupId");
            Assert.That(catalog.EnemyGroups.Any(value => value.Id == reinforcement &&
                value.Members.Sum(member => member.Count) > 1), Is.True);
        }

        [Test]
        public void FourthMainWave_StartsIndependentBossWithoutAdvancingRoundOrCompletingArena()
        {
            WithCombat((system, runtime) =>
            {
                var field = typeof(ArcadeCombatRuntime).GetField("IsFinalBoss");
                Assert.That(field, Is.Not.Null, "独立最终 Boss 现场尚未实现");
                Call(system, "TickWaveTransition", runtime, 2f);
                Call(system, "TickWaveTransition", runtime, 2f);
                Assert.That(field.GetValue(runtime), Is.True);
                Assert.That(runtime.WaveIndex, Is.EqualTo(3));
                Assert.That(runtime.CompletionSubmitted, Is.False);
            });
        }

        [Test]
        public void FinalBossRay_TelegraphsThenFiresAndRespectsProjectileBudget()
        {
            WithCombat((system, runtime) =>
            {
                Call(system, "BeginFinalBoss", runtime);
                Call(system, "TickSpawning", runtime, .1f);
                var enemy = runtime.Enemies.Values.Single();
                // 避开冲锋冷却，以很长一步推进到射线预警，再用短步验证不能提前发射。
                enemy.BossSkillCooldown = 100f;
                Call(system, "TickBoss", runtime, enemy, Vector2.right, false, 3f, 3f);
                Assert.That(enemy.BossPhase.ToString(), Is.EqualTo("RayTelegraph"));
                Assert.That(runtime.Projectiles.Count, Is.Zero);
                Call(system, "TickBoss", runtime, enemy, Vector2.up, false, .1f, .1f);
                Assert.That(runtime.Projectiles.Count, Is.Zero);
                Call(system, "TickBoss", runtime, enemy, Vector2.up, false, 1f, 1f);
                Assert.That(runtime.Projectiles.Count, Is.EqualTo(1));
                Assert.That(runtime.Projectiles.Values.Single().Hostile, Is.True);
                Assert.That(runtime.Projectiles.Values.Single().Velocity.x, Is.GreaterThan(0));
                Assert.That(runtime.Projectiles.Values.Single().Velocity.y, Is.Zero.Within(.001f), "预警后不能追踪改向");
                var cap = Catalog().Rule.MaxProjectiles;
                for (var id = 1000; runtime.Projectiles.Count < cap; id++)
                    runtime.Projectiles.Add(id, new ArcadeProjectileRuntime { Id = id });
                Call(system, "TickBoss", runtime, enemy, Vector2.right, false, 3f, 3f);
                Call(system, "TickBoss", runtime, enemy, Vector2.right, false, 1f, 1f);
                Assert.That(runtime.Projectiles.Count, Is.EqualTo(cap));
            });
        }

        [Test]
        public void FinalBossDeath_StopsReinforcementsAndWaitsForVictoryExit()
        {
            WithCombat((system, runtime) =>
            {
                Call(system, "BeginFinalBoss", runtime);
                Call(system, "TickSpawning", runtime, .1f);
                var enemy = runtime.Enemies.Values.Single();
                Call(system, "TickFinalBossReinforcements", runtime, 5f);
                Assert.That(runtime.SpawnQueue.Count, Is.GreaterThan(0));
                Call(system, "KillEnemy", runtime, enemy, true);
                Call(system, "TickFinalBossReinforcements", runtime, 5f);
                Assert.That(runtime.SpawnQueue.Count, Is.Zero);
                Assert.That(runtime.Enemies.Count, Is.Zero);
                var exit = typeof(ArcadeCombatRuntime).GetField("AwaitingExit");
                Assert.That(exit, Is.Not.Null);
                Assert.That(exit.GetValue(runtime), Is.True);
                Assert.That(runtime.CompletionSubmitted, Is.False, "胜利必须由出口提交，不能在 Boss 死亡时提前发奖");
            });
        }

        [Test]
        public void FinalBossBomb_DoesNotClearBossOrOpenVictoryExit()
        {
            WithCombat((system, runtime) =>
            {
                Call(system, "BeginFinalBoss", runtime);
                Call(system, "TickSpawning", runtime, .1f);
                runtime.Bombs = 1;
                Call(system, "UseBomb", runtime);
                Assert.That(runtime.Enemies.ContainsKey(runtime.FinalBossEnemyId), Is.True);
                Assert.That(runtime.Bombs, Is.Zero);
                Assert.That(runtime.AwaitingExit, Is.False);
            });
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(20)]
        public void ExitVisuals_AreStableColliderFreeAndReleasedAfterSceneEnds(int arenaNumber)
        {
            WithExitView(arenaNumber, true, (view, root) =>
            {
                view.SetExits(new[] { 2, 3 }, 20f, false);
                Assert.That(root.transform.childCount, Is.EqualTo(2), "普通地图保留指路箭头");
                Assert.That(root.GetComponentsInChildren<MeshRenderer>(true), Is.Empty, "箭头不附带旧门框和牌子");
                view.SetExits(Array.Empty<int>(), 20f, true);
                Assert.That(root.transform.childCount, Is.EqualTo(1));
                Assert.That(root.transform.GetChild(0).name, Is.EqualTo("ArcadeVictoryExit"));
                Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
                view.End();
                Assert.That(root == null, Is.True);
            });
        }

        /// <summary>异步模板就绪后补上指路箭头；胜利出口仍可创建并被下一地图快照清除。</summary>
        [Test]
        public void ExitVisuals_OrdinarySnapshotRestoresOnlyArrowsAfterContentLoads()
        {
            WithExitView(1, false, (view, root) =>
            {
                view.SetExits(new[] { 2, 3 }, 20f, false);
                Assert.That(root.transform.childCount, Is.Zero);
                Call(view, "LoadContentAsync", 1);
                Assert.That(root.transform.childCount, Is.EqualTo(2));
                Assert.That(root.GetComponentsInChildren<MeshRenderer>(true), Is.Empty);
                view.SetExits(Array.Empty<int>(), 20f, true);
                Assert.That(root.transform.childCount, Is.EqualTo(1));
                var victory = root.transform.GetChild(0);
                view.SetExits(Array.Empty<int>(), 20f, true);
                Assert.That(root.transform.GetChild(0), Is.SameAs(victory));
                view.SetExits(new[] { 2, 3 }, 20f, false);
                Assert.That(root.transform.Find("ArcadeVictoryExit"), Is.Null, "普通快照须清除前一张地图残留的胜利出口");
                Assert.That(root.transform.childCount, Is.EqualTo(2));
            });
        }

        /// <summary>提供真实模板与本地语言表，隔离展示测试对全局架构及资源加载器的影响。</summary>
        [Test]
        public void TreasureGroundRewards_FadeWithoutFadingReturnExitAndReleaseMaterials()
        {
            WithExitView(1, true, (view, root) =>
            {
                var room = Catalog().TreasureRooms.First();
                var reward = room.Rewards.First(r => r.Kind == FirepowerLoop.Arcade.ArcadeTreasureRewardKind.GoldBar);
                view.SetTreasureRoom(room, true, true, true, new[] { reward.Id });
                var model = root.transform.Find("TreasureReward_" + reward.Id).GetComponentInChildren<Renderer>();
                var exit = root.transform.Find("TreasureReturn").GetComponentInChildren<Renderer>();
                var original = model.sharedMaterial;
                var exitMaterial = exit.sharedMaterial;
                var alpha = original.GetColor("_BaseColor").a;
                view.SetTreasureRewardOpacity(.5f);
                var faded = model.sharedMaterial;
                Assert.That(faded, Is.Not.SameAs(original));
                Assert.That(faded.GetColor("_BaseColor").a, Is.EqualTo(alpha * .5f).Within(.001f));
                Assert.That(exit.sharedMaterial, Is.SameAs(exitMaterial));
                Assert.That(original.GetColor("_BaseColor").a, Is.EqualTo(alpha));
                view.End();
                Assert.That(faded == null, Is.True);
            });
        }

        /// <summary>提供真实模板与本地语言表，隔离展示测试对全局架构及资源加载器的影响。</summary>
        /// <summary>军械库使用正式六种武器表现地址，渐隐只作用于物资，离场清理所有实例。</summary>
        [Test] public void ArmoryVisualsUseSixWeaponPrefabsAndFadeWithoutRemovingExit()
        {
            WithExitView(2, true, (view, root) =>
            {
                var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                    new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
                Game.Interface.RegisterUtility<IRuntimeConfigProvider>(provider);
                var loot = new ArmoryLootSpy();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(ArcadeCombatViewController).GetField("lootPresentation", flags).SetValue(view, loot);
                typeof(ArcadeCombatViewController).GetField("mapRoot", flags).SetValue(view, root.transform);
                var room = Catalog().TreasureRooms.Single(r => r.IsArmory);
                view.SetTreasureRoom(room, false, true, true, Array.Empty<int>());
                Assert.That(root.GetComponentsInChildren<TextMeshPro>().Any(t => t.text.Contains("军械库") || t.text.Contains("Armory")), Is.True);
                view.SetTreasureRoom(room, true, true, true, room.Rewards.Select(r => r.Id).ToArray());
                Assert.That(loot.Addresses.Count, Is.EqualTo(6));
                Assert.That(loot.Addresses.Values.Distinct().Count(), Is.EqualTo(6));
                foreach (var address in loot.Addresses.Values)
                    Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + address + ".prefab"), Is.Not.Null, address);
                view.SetTreasureRewardOpacity(.5f);
                Assert.That(loot.Opacities.Values.All(a => a == .5f), Is.True);
                view.SetTreasureRoom(room, true, true, true, Array.Empty<int>());
                Assert.That(loot.Addresses, Is.Empty);
                Assert.That(root.transform.Find("TreasureReturn"), Is.Not.Null);
                view.End(); Assert.That(loot.Addresses, Is.Empty);
            });
        }
        private sealed class ArmoryLootSpy : IArcadeLootPresentationPort
        {
            public readonly Dictionary<int, string> Addresses = new();
            public readonly Dictionary<int, float> Opacities = new();
            public bool TrySetDrop(int id, Vector3 position, string name, int amount, out string error)
            { error = "必须使用正式武器地址"; return false; }
            public bool TrySetDrop(int id, Vector3 position, string name, int amount, string address, out string error)
            { Addresses[id] = address; error = null; return true; }
            public void SetDropOpacity(int id, float opacity) { Opacities[id] = opacity; }
            public void RemoveDrop(int id) { Addresses.Remove(id); Opacities.Remove(id); }
            public void ClearArcadeDrops() { Addresses.Clear(); Opacities.Clear(); }
        }
        private static void WithExitView(int arenaNumber, bool templatesReady,
            Action<ArcadeCombatViewController, GameObject> action)
        {
            var architectureField = typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
            var previousArchitecture = architectureField.GetValue(null);
            var previousLoader = ResourceLoader.Current;
            var view = new ArcadeCombatViewController();
            var root = new GameObject("ArcadeExitTest");
            try
            {
                ResourceLoader.SetLoaderForTests(new ExitVisualResourceLoader());
                LocalizationLanguages.ResetForTests();
                var architecture = new Game();
                architectureField.SetValue(null, architecture);
                typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(architecture, true);
                architecture.RegisterModel(new LocalizationModel());
                architecture.RegisterUtility(new LocalizationUtility());
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(ArcadeCombatViewController).GetField("root", flags).SetValue(view, root);
                typeof(ArcadeCombatViewController).GetField("contentArena", flags).SetValue(view, arenaNumber);
                if (templatesReady)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab");
                    Assert.That(prefab, Is.Not.Null);
                    var templates = prefab.transform.Find("Content");
                    Assert.That(templates, Is.Not.Null);
                    typeof(ArcadeCombatViewController).GetField("contentTemplates", flags).SetValue(view, templates);
                }
                action(view, root);
            }
            finally
            {
                try { view.End(); }
                finally
                {
                    if (root != null) UnityEngine.Object.DestroyImmediate(root);
                    architectureField.SetValue(null, previousArchitecture);
                    ResourceLoader.Use(previousLoader);
                    LocalizationLanguages.ResetForTests();
                }
            }
        }

        /// <summary>仅加载工程内现有语言表和正式表现 Prefab，不启动 YooAsset、网络或玩家存档。</summary>
        private sealed class ExitVisualResourceLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : UnityEngine.Object =>
                AssetDatabase.LoadAssetAtPath<T>(typeof(T) == typeof(TextAsset)
                    ? "Assets/Game/Config/Localization/" + path : "Assets/Game/" + path + ".prefab");
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            { onLoaded?.Invoke(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
            public void Release(ResourceLoadHandle handle) { }
        }

        [Test]
        public void SaveFailure_PreservesArenaAndCanRetrySameRoute()
        {
            WithRun(1, 1, (system, model) =>
            {
                var store = (ArcadeRunFileStore)typeof(ArcadeRunSystem).GetField("store", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(system);
                Directory.CreateDirectory(store.TempPath);
                Assert.That(system.TryCompleteArena(Completion(1, 21, false), out _, out _), Is.False);
                Assert.That(model.Phase, Is.EqualTo(ArcadeRunPhase.Arena));
                Assert.That(model.Current.ArenaConfigNumber, Is.EqualTo(1));
                Directory.Delete(store.TempPath);
                Assert.That(system.TryCompleteArena(Completion(1, 21, false), out var saved, out var error), Is.True, error);
                Assert.That(saved.NextArenaNumber, Is.EqualTo(2));
                Assert.That(saved.ArenaConfigNumber, Is.EqualTo(21));
            });
        }

        private static void Call(object target, string name, params object[] arguments)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name + " 尚未接入真实战斗入口");
            method.Invoke(target, arguments);
        }

        [Test]
        public void Completion_PersistsSelectedMapSeparatelyFromProgress()
        {
            WithRun(1, 1, (system, model) =>
            {
                var completion = Completion(1, 21, false);
                Assert.That(system.TryCompleteArena(completion, out var saved, out var error), Is.True, error);
                Assert.That(saved.NextArenaNumber, Is.EqualTo(2));
                Assert.That(saved.ArenaConfigNumber, Is.EqualTo(21));
                Assert.That(system.TryResume(out var resumed, out error), Is.True, error);
                Assert.That(resumed.ArenaConfigNumber, Is.EqualTo(21));
            });
        }

        [Test]
        public void Completion_RejectsUnreachableDestinationAndPrematureVictory()
        {
            WithRun(1, 1, (system, model) =>
            {
                Assert.That(system.TryCompleteArena(Completion(1, 15, false), out _, out _), Is.False);
                Assert.That(system.TryCompleteArena(Completion(1, 0, true), out _, out _), Is.False);
                Assert.That(model.Phase, Is.EqualTo(ArcadeRunPhase.Arena));
                Assert.That(model.Current.CompletedArenaCount, Is.Zero);
            });
        }

        [Test]
        public void Victory_PersistsRetryNodeAndRejectsContinuePurchaseAndDuplicateCompletion()
        {
            WithRun(10, 20, (system, model) =>
            {
                Assert.That(system.TryCompleteArena(Completion(10, 0, true), out var saved, out var error), Is.True, error);
                Assert.That(saved.Victory, Is.True);
                Assert.That(model.Phase, Is.EqualTo(ArcadeRunPhase.Victory));
                Assert.That(system.InspectPending().IsValid, Is.True);
                Assert.That(system.TryResume(out var resumed, out error), Is.True, error);
                Assert.That(resumed.Victory, Is.True);
                Assert.That(model.Phase, Is.EqualTo(ArcadeRunPhase.Victory));
                Assert.That(system.TryContinue(out _, out _), Is.False);
                Assert.That(typeof(ArcadeRunSystem).GetMethod("TryPurchase"), Is.Null);
                Assert.That(system.TryCompleteArena(Completion(10, 0, true), out _, out _), Is.False);
            });
        }

        private static ArcadeArenaCompletion Completion(int ordinal, int destination, bool victory) =>
            new ArcadeArenaCompletion
            {
                ArenaNumber = ordinal, NextArenaConfigNumber = destination, Victory = victory,
                EmergencyResources = new Dictionary<string, int>(),
                EquipmentBySlot = new Dictionary<string, string> { [ArcadeRunDefaults.WeaponSlotId] = ArcadeRunDefaults.BasicWeaponId },
                EarnedRewards = new[] { new ArcadeRewardSaveData { RewardId = "arcade.test", Amount = 1 } },
                NextCheckpoint = victory ? null : new ArcadeCheckpointSaveData { ArenaNumber = ordinal + 1 }
            };

        private static void WithRun(int ordinal, int configId, Action<ArcadeRunSystem, ArcadeRunModel> action)
        {
            var root = Path.Combine(Path.GetTempPath(), "FirepowerLoopFinalBoss", Guid.NewGuid().ToString("N"));
            var architecture = new Game();
            var model = new ArcadeRunModel();
            architecture.RegisterModel(model);
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes"))))));
            var system = new ArcadeRunSystem();
            ((ICanSetArchitecture)system).SetArchitecture(architecture);
            system.ConfigureForTests(root);
            var run = new ArcadeRunSaveData
            {
                RunId = Guid.NewGuid().ToString("N"), IsCasualRoute = true, ArenaConfigNumber = configId,
                NextArenaNumber = ordinal, CompletedArenaCount = ordinal - 1, RemainingLives = 3,
                UpdatedAtUtc = DateTime.UtcNow.ToString("O"), Checkpoint = new ArcadeCheckpointSaveData { ArenaNumber = ordinal }
            };
            run.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId] = ArcadeRunDefaults.BasicWeaponId;
            model.CommitSafeNode(run);
            model.EnterArena();
            try { action(system, model); }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void WithCombat(Action<ArcadeCombatSystem, ArcadeCombatRuntime> action)
        {
            var architecture = new Game();
            var model = new ArcadeRunModel();
            architecture.RegisterModel(model);
            architecture.RegisterModel(new ArcadeCombatModel());
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes"))))));
            var run = new ArcadeRunSaveData { NextArenaNumber = 10, CompletedArenaCount = 9, RemainingLives = 3 };
            typeof(ArcadeRunSaveData).GetProperty("IsCasualRoute")?.SetValue(run, true);
            typeof(ArcadeRunSaveData).GetProperty("ArenaConfigNumber")?.SetValue(run, 20);
            model.CommitSafeNode(run);
            model.EnterArena();
            var catalog = Catalog();
            var system = new ArcadeCombatSystem();
            ((ICanSetArchitecture)system).SetArchitecture(architecture);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(ArcadeCombatSystem).GetField("catalog", flags).SetValue(system, catalog);
            typeof(ArcadeCombatSystem).GetField("arena", flags).SetValue(system, catalog.Arenas.Single(value => value.ArenaNumber == 20));
            typeof(ArcadeCombatSystem).GetField("view", flags).SetValue(system, new TestView());
            typeof(ArcadeCombatSystem).GetField("random", flags).SetValue(system, new System.Random(123));
            var runtime = new ArcadeCombatRuntime { ArenaNumber = 10, WaveIndex = 3, PlayerMaxHealth = 100, PlayerHealth = 100 };
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            try { action(system, runtime); }
            finally { architecture.GetModel<ArcadeCombatModel>().Clear(); }
        }

        private sealed class TestView : IArcadeCombatViewPort
        {
            public bool IsActive => true;
            public void Begin(int id, float width, float height, IReadOnlyList<ArcadeObstacleDefinition> obstacles) { }
            public void End() { }
            public bool TryScreenToArena(Vector2 screen, out Vector2 value) { value = default; return false; }
            public void SetPlayer(Vector2 position, Vector2 facing, bool visible, bool invulnerable) { }
            public void SetPlayerHealth(int current, int maximum) { }
            public void SetEnemy(int id, string config, Vector2 position, int current, int maximum, bool telegraph = false, int attackSequence = 0, bool persistentHealthBar = false) { }
            public void RemoveEnemy(int id, bool death = false) { }
            public void SetProjectile(int id, bool hostile, Vector2 position, Vector2 direction) { }
            public void RemoveProjectile(int id) { }
            public void SetDrop(int id, ArcadeDropDefinition definition, Vector2 position) { }
            public void RemoveDrop(int id) { }
            public void SetRemotePlayer(long id, Vector2 position, Vector2 facing, bool visible, bool invulnerable, int current, int maximum) { }
            public void RemoveRemotePlayer(long id) { }
            public void TickPlayerFeedback(float seconds) { }
        }
    }
}
