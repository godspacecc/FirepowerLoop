using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>在正式敌人资源及控制器上检查爆炸死亡触发、独立粒子预算和地图回收。</summary>
    public sealed class EnemyExplosionFeedbackTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private EnemyCtrl controller;
        private GameObject root;
        private static readonly string[] Names = { "ArcadeEnemyBasic", "ArcadeEnemyArcher", "ArcadeEnemyBrute",
            "ArcadeEnemyBossArcher", "ArcadeZombieShambler", "ArcadeZombieGaunt", "ArcadeZombieBloated" };

        [SetUp] public void SetUp()
        {
            root = new GameObject("ExplosionFeedbackTestMap");
            var architecture = new global::Game.Game();
            architecture.RegisterUtility<IViewInstanceProvider>(new UnityViewInstanceProvider());
            controller = new EnemyCtrl(architecture);
            typeof(EnemyCtrl).GetField("arcadeRoot", Flags).SetValue(controller, root.transform);
        }

        [TearDown] public void TearDown()
        {
            controller?.ReleaseAll();
            if (root != null) Object.DestroyImmediate(root);
        }

        private EnemyView Spawn(int id)
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"), root.transform);
            var view = instance.GetComponent<EnemyView>();
            view.BeginPresentationUse(); view.SetNavigationEnabled(false);
            var type = typeof(EnemyCtrl).GetNestedType("ArcadeEnemyPresentationEntry", BindingFlags.NonPublic);
            var entries = (IDictionary)typeof(EnemyCtrl).GetField("arcadeEnemies", Flags).GetValue(controller);
            entries.Add(id, Activator.CreateInstance(type, id, "enemy.arcade.basic", view, 20, instance.transform.position, 0, false));
            return view;
        }

        private ParticleSystem[] Particles() => root.GetComponentsInChildren<ParticleSystem>(true)
            .Where(p => p.transform.parent.name == "EnemyExplosionDeathInstance").ToArray();
        private object Pool => typeof(EnemyCtrl).GetField("explosionDeaths", Flags)?.GetValue(controller);
        private void Tick(float seconds)
        {
            Assert.NotNull(Pool, "爆炸死亡应创建地图持有的独立特效池");
            Pool.GetType().GetMethod("Tick").Invoke(Pool, new object[] { seconds });
        }

        [Test] public void ExplosionEmitsOnceAndStaysAtDeathPositionWhenCorpseMoves()
        {
            var view = Spawn(1);
            controller.RemoveEnemy(1, true, Vector3.right, true);
            var particles = Particles();
            Assert.That(particles.Length, Is.EqualTo(3));
            Assert.That(particles.Sum(p => p.particleCount), Is.EqualTo(42));
            var positions = particles.Select(p => p.transform.position).ToArray();
            view.transform.position += Vector3.one * 10;
            controller.RemoveEnemy(1, true, Vector3.right, true);
            CollectionAssert.AreEqual(positions, particles.Select(p => p.transform.position).ToArray());
            Assert.That(Particles().Sum(p => p.particleCount), Is.EqualTo(42));
            Assert.That(particles.All(p => p.main.simulationSpace == ParticleSystemSimulationSpace.World), Is.True);
        }

        [TestCase(true, false)] [TestCase(false, true)]
        public void OrdinaryDeathAndSilentRemovalDoNotEmit(bool death, bool explosive)
        {
            Spawn(1); controller.RemoveEnemy(1, death, Vector3.right, explosive);
            Assert.That(Particles(), Is.Empty);
        }

        [TestCase(6)] [TestCase(12)]
        public void DenseExplosionsReuseSlotsAndParticlesStayBounded(int capacity)
        {
            var budget = typeof(EnemyCtrl).GetField("explosionDeathBudget", Flags);
            Assert.NotNull(budget); budget.SetValue(controller, capacity);
            for (int id = 1; id <= capacity + 5; id++)
            { Spawn(id); controller.RemoveEnemy(id, true, Vector3.forward, true); }
            Assert.That(Particles().Length, Is.EqualTo(capacity * 3));
            Assert.That(Particles().Sum(p => p.particleCount), Is.EqualTo(capacity * 42));
            Assert.That(Particles().All(p => !p.collision.enabled), Is.True);
        }

        [Test] public void ZeroStepPausesAndExpiryClearsForReuse()
        {
            Spawn(1); controller.RemoveEnemy(1, true, Vector3.forward, true);
            var mist = Particles().Single(p => p.name == "ChestMist");
            var particles = new ParticleSystem.Particle[64]; mist.GetParticles(particles);
            float lifetime = particles[0].remainingLifetime;
            Tick(0); mist.GetParticles(particles);
            Assert.That(particles[0].remainingLifetime, Is.EqualTo(lifetime));
            Tick(1);
            Assert.That(Particles().Sum(p => p.particleCount), Is.Zero);
            Assert.That(Particles().All(p => !p.gameObject.activeInHierarchy), Is.True);
            Spawn(2); controller.RemoveEnemy(2, true, Vector3.forward, true);
            Assert.That(Particles().Length, Is.EqualTo(3), "空闲槽位应复用");
            Assert.That(Particles().Sum(p => p.particleCount), Is.EqualTo(42));
        }

        [Test] public void MapHideClearAndReleaseRemoveLingeringEffects()
        {
            Spawn(1); controller.RemoveEnemy(1, true, Vector3.forward, true);
            Assert.That(Particles().Sum(p => p.particleCount), Is.GreaterThan(0));
            controller.SetCurrentMapActive(false);
            Assert.That(Particles().Sum(p => p.particleCount), Is.Zero);
            controller.SetCurrentMapActive(true);
            Spawn(2); controller.RemoveEnemy(2, true, Vector3.forward, true);
            controller.ClearArcadeEnemies();
            Assert.That(Particles().Sum(p => p.particleCount), Is.Zero);
            controller.ReleaseCurrentMap();
            Assert.That(Particles(), Is.Empty);
        }

        [Test] public void ExplosionStillEmitsWithAnimationFallback()
        {
            var view = Spawn(1);
            Object.DestroyImmediate(view.GetComponent<EnemyPhysicalReaction>());
            controller.RemoveEnemy(1, true, Vector3.forward, true);
            Assert.That(Particles().Sum(p => p.particleCount), Is.EqualTo(42));
        }

        [Test] public void AllFormalEnemyPrefabsReferenceSharedEffectWithoutLiveParticles()
        {
            var field = typeof(EnemyView).GetField("explosionDeathTemplate", Flags);
            Assert.NotNull(field, "正式敌人需声明爆炸死亡资源依赖");
            foreach (string name in Names)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/" + name + ".prefab");
                var template = (GameObject)field.GetValue(prefab.GetComponent<EnemyView>());
                Assert.NotNull(template, name);
                Assert.That(template.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(3));
                Assert.That(prefab.GetComponentsInChildren<ParticleSystem>(true).Any(p => p.name == "ChestMist"), Is.False,
                    "活敌人不应实例化爆炸粒子");
            }
        }
    }
}
