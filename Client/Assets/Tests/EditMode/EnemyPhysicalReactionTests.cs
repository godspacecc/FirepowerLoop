using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>在真实敌人Prefab上验证局部受击、死亡物理和池化复位，不接触玩家数据。</summary>
    public sealed class EnemyPhysicalReactionTests
    {
        private GameObject instance;
        private EnemyView view;
        private Component reaction;
        private static Type ReactionType => typeof(EnemyView).Assembly.GetType("Game.EnemyPhysicalReaction");

        private void Spawn(string name = "ArcadeEnemyBasic")
        {
            instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/" + name + ".prefab"));
            view = instance.GetComponent<EnemyView>();
            instance.SetActive(false); view.BeginPresentationUse(); instance.SetActive(true);
            view.SetNavigationEnabled(false);
            Assert.That(ReactionType, Is.Not.Null, "正式敌人尚未接入物理受击组件");
            reaction = instance.GetComponent(ReactionType);
            Assert.That(reaction, Is.Not.Null, name);
        }

        private object Call(string method, params object[] args) => ReactionType.GetMethod(method,
            BindingFlags.Public | BindingFlags.Instance).Invoke(reaction, args);

        [TearDown] public void Cleanup() { if (instance != null) Object.DestroyImmediate(instance); }

        [TestCase("ArcadeEnemyBasic")]
        [TestCase("ArcadeEnemyArcher")]
        [TestCase("ArcadeEnemyBrute")]
        [TestCase("ArcadeEnemyBossArcher")]
        public void PrefabHasPrebuiltSleepingElevenBodyRig(string name)
        {
            Spawn(name);
            var bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            Assert.That(bodies.Length, Is.EqualTo(11));
            Assert.That(instance.GetComponentsInChildren<CharacterJoint>(true).Length, Is.EqualTo(10));
            Assert.That(bodies.All(body => body.isKinematic && !body.detectCollisions), Is.True);
            Assert.That(bodies.All(body => !body.GetComponent<Collider>().enabled), Is.True);
        }

        [Test]
        public void HitRecoilMovesOnlyChestAndReturnsWithoutAccumulating()
        {
            Spawn();
            var chest = view.Animator.GetBoneTransform(HumanBodyBones.Chest);
            var pose = chest.localRotation; var position = instance.transform.position;
            for (int i = 0; i < 10; i++)
            {
                Call("PlayHit", Vector3.forward); Call("Tick", .035f); Call("LateTick");
                Assert.That(Quaternion.Angle(pose, chest.localRotation), Is.InRange(.1f, 20f));
            }
            Call("Tick", 1f); Call("LateTick");
            Assert.That(Quaternion.Angle(pose, chest.localRotation), Is.LessThan(.01f));
            Assert.That(instance.transform.position, Is.EqualTo(position));
            Assert.That(instance.GetComponentsInChildren<Rigidbody>(true).All(body => body.isKinematic), Is.True);
        }

        [Test]
        public void DeathActivatesPhysicsAndExplosionProducesStrongerLift()
        {
            Spawn();
            Assert.That((bool)Call("BeginDeath", Vector3.forward, false), Is.True);
            var bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            Assert.That(view.Animator.enabled, Is.False);
            Assert.That(bodies.All(body => !body.isKinematic && body.detectCollisions), Is.True);
            float normalLift = bodies[0].velocity.y;
            Call("ResetForReuse");
            Assert.That((bool)Call("BeginDeath", Vector3.forward, true), Is.True);
            Assert.That(bodies[0].velocity.y, Is.GreaterThan(normalLift + 2f));
            Assert.That(bodies[0].velocity.z, Is.GreaterThan(3f));
        }

        [Test]
        public void DeathPhysicsFreezesThenDisableAndReuseRestoreAllBones()
        {
            Spawn();
            var bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            var positions = bodies.Select(body => body.transform.localPosition).ToArray();
            var rotations = bodies.Select(body => body.transform.localRotation).ToArray();
            Call("BeginDeath", Vector3.right, true);
            bodies[0].transform.localPosition += Vector3.one;
            bodies[1].transform.localRotation *= Quaternion.Euler(20, 10, 0);
            Call("Tick", 2f);
            Assert.That(bodies.All(body => body.isKinematic && !body.detectCollisions), Is.True);
            instance.SetActive(false);
            view.BeginPresentationUse(); instance.SetActive(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                Assert.That(Vector3.Distance(bodies[i].transform.localPosition, positions[i]), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(bodies[i].transform.localRotation, rotations[i]), Is.LessThan(.01f));
                Assert.That(bodies[i].GetComponent<Collider>().enabled, Is.False);
            }
            Assert.That(view.Animator.enabled, Is.True);
            Assert.That(instance.GetComponent<Collider>().enabled, Is.True);
        }

        [Test]
        public void RepeatedDeathDoesNotApplyImpulseAgainAndZeroDeltaPreservesRecoil()
        {
            Spawn();
            Call("PlayHit", Vector3.forward); Call("Tick", .035f); Call("LateTick");
            var chest = view.Animator.GetBoneTransform(HumanBodyBones.Chest);
            var pose = chest.localRotation;
            Call("Tick", 0f); Call("LateTick");
            Assert.That(Quaternion.Angle(pose, chest.localRotation), Is.LessThan(.01f));
            Assert.That((bool)Call("BeginDeath", Vector3.forward, false), Is.True);
            var body = instance.GetComponentInChildren<Rigidbody>(); var velocity = body.velocity;
            Assert.That((bool)Call("BeginDeath", Vector3.right, true), Is.False);
            Assert.That(body.velocity, Is.EqualTo(velocity));
        }

        [TestCase(4)]
        [TestCase(8)]
        public void ControllerBoundsCorpsesAndClearsThemBackToSleepingPool(int budget)
        {
            var architecture = new global::Game.Game();
            architecture.RegisterUtility<IViewInstanceProvider>(new UnityViewInstanceProvider());
            var controller = new EnemyCtrl(architecture);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(EnemyCtrl).GetField("corpseBudget", flags).SetValue(controller, budget);
            var type = typeof(EnemyCtrl).GetNestedType("ArcadeEnemyPresentationEntry", BindingFlags.NonPublic);
            var alive = (System.Collections.IDictionary)typeof(EnemyCtrl).GetField("arcadeEnemies", flags).GetValue(controller);
            var dying = (System.Collections.IList)typeof(EnemyCtrl).GetField("dyingArcadeEnemies", flags).GetValue(controller);
            try
            {
                for (int id = 1; id <= 20; id++)
                {
                    Spawn();
                    alive.Add(id, Activator.CreateInstance(type, id, "enemy.arcade.basic", view, 20, instance.transform.position, 0, false));
                    instance = null; // 生命周期从此交给控制器。
                    controller.RemoveEnemy(id, true);
                    Assert.That(dying.Count, Is.LessThanOrEqualTo(budget));
                }
                controller.ClearArcadeEnemies();
                Assert.That(dying.Count, Is.Zero);
                var pools = (System.Collections.IDictionary)typeof(EnemyCtrl).GetField("arcadeEnemyPool", flags).GetValue(controller);
                foreach (System.Collections.IEnumerable pool in pools.Values)
                foreach (EnemyView pooled in pool)
                {
                    Assert.That(pooled.gameObject.activeSelf, Is.False);
                    Assert.That(pooled.GetComponentsInChildren<Rigidbody>(true).All(body => body.isKinematic && !body.detectCollisions), Is.True);
                }
            }
            finally { controller.ReleaseAll(); }
        }

        [Test]
        public void ExplosionKeepsSimulatingPastFirstSecondInsteadOfFreezingInAir()
        {
            Spawn(); Call("BeginDeath", Vector3.forward, true); Call("Tick", 1.05f);
            Assert.That(ReactionType.GetProperty("IsSimulating").GetValue(reaction), Is.True);
        }
    }
}
