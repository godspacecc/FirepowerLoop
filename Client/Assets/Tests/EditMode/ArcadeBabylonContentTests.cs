using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>Babylon 正式领域现场定向检查，使用生成配置，不改地图或玩家档。</summary>
    public sealed class ArcadeBabylonContentTests
    {
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        private Game architecture;
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            architecture = new Game(); var model = new ArcadeRunModel(); architecture.RegisterModel(model);
            architecture.RegisterModel(new ArcadeCombatModel());
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog, out var error), Is.True, error);
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            model.CommitSafeNode(new ArcadeRunSaveData { RunId = Guid.NewGuid().ToString("N"), IsCasualRoute = true,
                NextArenaNumber = 2, CompletedArenaCount = 1, ArenaConfigNumber = 2, RemainingLives = 3 });
            model.EnterArena();
            runtime = new ArcadeCombatRuntime { ArenaNumber = 2, PlayerHealth = 100, PlayerMaxHealth = 100 };
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            system = new ArcadeCombatSystem(); ((ICanSetArchitecture)system).SetArchitecture(architecture);
            typeof(ArcadeCombatSystem).GetField("catalog", Flags).SetValue(system, catalog);
            typeof(ArcadeCombatSystem).GetField("random", Flags).SetValue(system, new System.Random(123));
            typeof(ArcadeCombatSystem).GetField("view", Flags).SetValue(system, new TestView());
            typeof(ArcadeCombatSystem).GetField("arena", Flags).SetValue(system, catalog.Arenas.Single(a => a.ArenaNumber == 2));
        }
        private object Call(string name, params object[] args)
        {
            var method = typeof(ArcadeCombatSystem).GetMethod(name, Flags);
            Assert.That(method, Is.Not.Null, name + " 尚未接入"); return method.Invoke(system, args);
        }
        private IDictionary Crates()
        {
            var field = typeof(ArcadeCombatRuntime).GetField("Crates");
            Assert.That(field, Is.Not.Null, "箱子现场尚未接入"); return (IDictionary)field.GetValue(runtime);
        }
        [Test] public void Crates_BuildOnceAndDoNotRespawnOnNextWave()
        {
            Call("BeginWave", runtime, 0); Assert.That(Crates().Count, Is.EqualTo(8));
            Call("DamageCrate", runtime, 1, 100); Assert.That(Crates().Count, Is.EqualTo(7));
            Call("BeginWave", runtime, 1); Assert.That(Crates().Count, Is.EqualTo(7));
        }
        [Test] public void CrateBreak_IsIdempotentAndCreatesPickupInsteadOfCash()
        {
            Call("BeginWave", runtime, 0); Call("DamageCrate", runtime, 1, 100); Call("DamageCrate", runtime, 1, 100);
            Assert.That(runtime.Drops.Count, Is.EqualTo(1)); Assert.That(runtime.ArenaGold, Is.Zero);
        }
        [Test] public void FullDropBudget_KeepsCrateClaimableUntilSpaceReturns()
        {
            Call("BeginWave", runtime, 0);
            for (int i = 0; i < catalog.Rule.MaxDrops; i++) runtime.Drops.Add(1000 + i, new ArcadeDropRuntime());
            Call("DamageCrate", runtime, 1, 100); Assert.That(Crates().Contains(1), Is.True);
            runtime.Drops.Clear(); Call("DamageCrate", runtime, 1, 100);
            Assert.That(Crates().Contains(1), Is.False); Assert.That(runtime.Drops.Count, Is.EqualTo(1));
        }
        [Test] public void FirstWaveArmor_AbsorbsDamageAndLaterWavesStayNormal()
        {
            Call("BeginWave", runtime, 0); Call("TickSpawning", runtime, 1f);
            var enemy = runtime.Enemies.Values.Single();
            var armor = typeof(ArcadeEnemyRuntime).GetField("ArmorHealth");
            Assert.That(armor, Is.Not.Null, "独立护甲尚未接入");
            var health = enemy.Health; Assert.That(armor.GetValue(enemy), Is.EqualTo(40));
            Call("DamageEnemy", enemy, 30); Assert.That(enemy.Health, Is.EqualTo(health));
            Call("DamageEnemy", enemy, 15); Assert.That(armor.GetValue(enemy), Is.EqualTo(0));
            Assert.That(enemy.Health, Is.EqualTo(health - 5));
            runtime.Enemies.Clear(); Call("BeginWave", runtime, 1); Call("TickSpawning", runtime, 1f);
            Assert.That(armor.GetValue(runtime.Enemies.Values.Single()), Is.EqualTo(0));
        }
        [TestCase(false, 2)] [TestCase(true, 3)] [TestCase(false, 3)]
        public void LegacyAndOtherBranch_DoNotCreateCratesOrArmor(bool casual, int map)
        {
            var model = architecture.GetModel<ArcadeRunModel>(); var data = model.Current;
            data.IsCasualRoute = casual; data.ArenaConfigNumber = map; model.CommitSafeNode(data); model.EnterArena();
            typeof(ArcadeCombatSystem).GetField("arena", Flags).SetValue(system, catalog.Arenas.Single(a => a.ArenaNumber == map));
            Call("BeginWave", runtime, 0); Call("TickSpawning", runtime, 1f);
            Assert.That(Crates().Count, Is.Zero); Assert.That(runtime.Enemies.Values.Single().ArmorHealth, Is.Zero);
        }
        [Test] public void PooledEnemy_DoesNotRetainOldArmor()
        {
            var enemy = new ArcadeEnemyRuntime { ArmorHealth = 40 }; enemy.Reset(); Assert.That(enemy.ArmorHealth, Is.Zero);
        }
        [Test] public void RealPlayerProjectile_CrossesSmallCrateAndBreaksOnce()
        {
            Call("BeginWave", runtime, 0);
            runtime.Projectiles.Add(999, new ArcadeProjectileRuntime { Id = 999, Position = new Vector2(-15, -15),
                Velocity = new Vector2(0, 10), Damage = 100, RemainingSeconds = 10, RemainingHits = 1 });
            Call("TickProjectiles", runtime, .6f);
            Assert.That(Crates().Count, Is.EqualTo(7)); Assert.That(runtime.Drops.Count, Is.EqualTo(1));
            Assert.That(runtime.Projectiles.Count, Is.Zero);
        }
        [Test] public void FormalTemplates_HaveNoSpareLibraryDependencyOrCollider()
        {
            const string path = "Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var args = new object[] { prefab, null };
            Assert.That((bool)typeof(ArcadeCombatViewController).GetMethod("ValidateObstacleVisualPrefab", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, args), Is.True, (string)args[1]);
            foreach (var name in new[] { "Crate", "ArmorPlate" })
            {
                var template = prefab.transform.Find(name); Assert.That(template, Is.Not.Null);
                Assert.That(template.gameObject.activeSelf, Is.False);
                Assert.That(template.GetComponentsInChildren<Collider>(true).Length, Is.Zero);
            }
            Assert.That(AssetDatabase.GetDependencies(path).Any(p => p.StartsWith("Assets/Art/", StringComparison.Ordinal)), Is.False);
        }
        [Test] public void ArmorPresentation_RemovesMarkerAfterBreakAndOnRepeatedRelease()
        {
            var root = new GameObject("BabylonArmorTest"); var view = new ArcadeCombatViewController();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab");
            try
            {
                typeof(ArcadeCombatViewController).GetField("root", Flags).SetValue(view, root);
                typeof(ArcadeCombatViewController).GetField("armorTemplate", Flags).SetValue(view, prefab.transform.Find("ArmorPlate").gameObject);
                view.SetEnemyArmor(1, Vector2.zero, 40); Assert.That(root.transform.childCount, Is.EqualTo(1));
                view.SetEnemyArmor(1, Vector2.zero, 0); Assert.That(root.transform.childCount, Is.Zero);
                view.SetEnemyArmor(2, Vector2.one, 40); view.End(); view.End();
                Assert.That(root == null || root.transform.childCount == 0, Is.True);
            }
            finally { view.Dispose(); UnityEngine.Object.DestroyImmediate(root); }
        }
        [Test] public void Challenge_InitializesNeitherCratesNorArmor()
        {
            runtime.IsChallenge = true; Call("InitializeCrates", runtime);
            var enemy = new ArcadeEnemyRuntime(); Call("AssignArmor", runtime, enemy);
            Assert.That(Crates().Count, Is.Zero); Assert.That(enemy.ArmorHealth, Is.Zero);
        }
        private sealed class TestView : IArcadeCombatViewPort
        {
            public bool IsActive => true;
            public void Begin(int n, float w, float h, IReadOnlyList<ArcadeObstacleDefinition> o) { }
            public void End() { }
            public bool TryScreenToArena(Vector2 s, out Vector2 p) { p = default; return false; }
            public void SetPlayer(Vector2 p, Vector2 f, bool v, bool i) { }
            public void SetPlayerHealth(int c, int m) { }
            public void SetEnemy(int id, string c, Vector2 p, int hp, int max, bool t = false, int seq = 0, bool bar = false) { }
            public void RemoveEnemy(int id, bool death = false) { }
            public void SetProjectile(int id, bool h, Vector2 p, Vector2 d) { }
            public void RemoveProjectile(int id) { }
            public void SetDrop(int id, ArcadeDropDefinition d, Vector2 p) { }
            public void RemoveDrop(int id) { }
            public void SetRemotePlayer(long id, Vector2 p, Vector2 f, bool v, bool i, int c, int m) { }
            public void RemoveRemotePlayer(long id) { }
            public void TickPlayerFeedback(float d) { }
        }
    }
}
