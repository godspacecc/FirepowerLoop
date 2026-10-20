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

        [TearDown] public void Cleanup()
        {
            if (instance == null) return;
            instance.GetComponent<EnemyView>()?.ReleasePresentationResources();
            Object.DestroyImmediate(instance);
        }

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
            Call("Tick", .18f); Call("LateTick");
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
            var visibleBudget = typeof(EnemyCtrl).GetField("visibleCorpseBudget", flags);
            Assert.NotNull(visibleBudget);
            visibleBudget.SetValue(controller, budget * 3);
            var type = typeof(EnemyCtrl).GetNestedType("ArcadeEnemyPresentationEntry", BindingFlags.NonPublic);
            var alive = (System.Collections.IDictionary)typeof(EnemyCtrl).GetField("arcadeEnemies", flags).GetValue(controller);
            var dying = (System.Collections.IList)typeof(EnemyCtrl).GetField("dyingArcadeEnemies", flags).GetValue(controller);
            try
            {
                for (int id = 1; id <= 40; id++)
                {
                    Spawn();
                    alive.Add(id, Activator.CreateInstance(type, id, "enemy.arcade.basic", view, 20, instance.transform.position, 0, false));
                    instance = null; // 生命周期从此交给控制器。
                    controller.RemoveEnemy(id, true);
                    Assert.That(dying.Count, Is.LessThanOrEqualTo(budget * 3));
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

        [TestCase("ArcadeEnemyBasic")] [TestCase("ArcadeEnemyArcher")]
        [TestCase("ArcadeEnemyBrute")] [TestCase("ArcadeEnemyBossArcher")]
        public void NormalDeathStaggersBeforeHandingCurrentPoseToPhysics(string name)
        {
            Spawn(name); var bodies = instance.GetComponentsInChildren<Rigidbody>();
            var chest = view.Animator.GetBoneTransform(HumanBodyBones.Chest);
            var hips = view.Animator.GetBoneTransform(HumanBodyBones.Hips);
            var pose = chest.localRotation; var hipPosition = hips.position; var root = instance.transform.position;
            Assert.True(view.TryPlayPhysicalDeath(Vector3.forward, false, out var duration));
            Assert.True(view.Animator.enabled, "普通死亡首帧应保留动画");
            Assert.True(bodies.All(body => body.isKinematic && !body.detectCollisions));
            Call("Tick", .08f); Call("LateTick");
            Assert.Greater(Quaternion.Angle(pose, chest.localRotation), 1);
            Assert.Less(hips.position.y, hipPosition.y);
            Assert.AreEqual(root, instance.transform.position);
            Call("Tick", .09f); Call("LateTick");
            Assert.False(view.Animator.enabled); Assert.True(bodies.All(body => !body.isKinematic));
            Assert.Greater(Quaternion.Angle(pose, chest.localRotation), 8, "交接时不能撤销刚形成的失衡姿态");
            Assert.GreaterOrEqual(duration, 6f, "物理冻结后仍应保留可见尸体");
        }
        [Test] public void PausedStaggerDoesNotAdvanceOrAccumulateAndRepeatedDeathCannotSkipIt()
        {
            Spawn(); Call("BeginDeath", Vector3.forward, false); Call("Tick", .08f); Call("LateTick");
            var chest = view.Animator.GetBoneTransform(HumanBodyBones.Chest);
            var hips = view.Animator.GetBoneTransform(HumanBodyBones.Hips);
            var rotation = chest.localRotation; var position = hips.localPosition;
            for (int i = 0; i < 10; i++) { Call("Tick", 0f); Call("LateTick"); }
            Assert.Less(Quaternion.Angle(rotation, chest.localRotation), .01f);
            Assert.Less(Vector3.Distance(position, hips.localPosition), .001f);
            Assert.False((bool)Call("BeginDeath", Vector3.right, true));
            Assert.True(view.Animator.enabled);
            Assert.True(instance.GetComponentsInChildren<Rigidbody>().All(body => body.isKinematic));
        }
        [Test] public void RecycleDuringStaggerCancelsPendingPhysicsAndRestoresBones()
        {
            Spawn(); var chest = view.Animator.GetBoneTransform(HumanBodyBones.Chest);
            var hips = view.Animator.GetBoneTransform(HumanBodyBones.Hips);
            var rotation = chest.localRotation; var position = hips.localPosition;
            view.TryPlayPhysicalDeath(Vector3.forward, false, out _); view.TickCombatFeedback(.08f); view.LateTickCombatFeedback();
            instance.SetActive(false); view.BeginPresentationUse(); instance.SetActive(true);
            view.TickCombatFeedback(.3f); view.LateTickCombatFeedback();
            Assert.Less(Quaternion.Angle(rotation, chest.localRotation), .01f);
            Assert.Less(Vector3.Distance(position, hips.localPosition), .001f);
            Assert.True(view.Animator.enabled);
            Assert.True(instance.GetComponentsInChildren<Rigidbody>().All(body => body.isKinematic && !body.detectCollisions));
        }
        [TestCase("ArcadeEnemyBasic")] [TestCase("ArcadeEnemyArcher")]
        [TestCase("ArcadeEnemyBrute")] [TestCase("ArcadeEnemyBossArcher")]
        public void ExplosionPushesTorsoInsteadOfAssigningEveryBoneTheSameVelocity(string name)
        {
            Spawn(name); Call("BeginDeath", Vector3.forward, true);
            var hips = view.Animator.GetBoneTransform(HumanBodyBones.Hips).GetComponent<Rigidbody>();
            var chest = view.Animator.GetBoneTransform(HumanBodyBones.Chest).GetComponent<Rigidbody>();
            var arm = view.Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm).GetComponent<Rigidbody>();
            var leg = view.Animator.GetBoneTransform(HumanBodyBones.RightLowerLeg).GetComponent<Rigidbody>();
            Assert.False(view.Animator.enabled);
            Assert.Greater(chest.velocity.y, arm.velocity.y + 3);
            Assert.Greater(hips.velocity.z, leg.velocity.z + 3);
            Assert.Less(arm.velocity.magnitude, .1f); Assert.Less(leg.velocity.magnitude, .1f);
            Assert.Greater((chest.angularVelocity - hips.angularVelocity).magnitude, 1);
        }
        [Test] public void ControllerLateUpdateHandsDyingEnemyOverToPhysics()
        {
            Spawn(); var architecture = new global::Game.Game();
            architecture.RegisterUtility<IViewInstanceProvider>(new UnityViewInstanceProvider());
            var controller = new EnemyCtrl(architecture); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(EnemyCtrl).GetNestedType("ArcadeEnemyPresentationEntry", BindingFlags.NonPublic);
            var alive = (System.Collections.IDictionary)typeof(EnemyCtrl).GetField("arcadeEnemies", flags).GetValue(controller);
            alive.Add(1, Activator.CreateInstance(type, 1, "enemy.arcade.basic", view, 20, instance.transform.position, 0, false));
            instance = null;
            try
            {
                controller.RemoveEnemy(1, true); view.TickCombatFeedback(.18f);
                Assert.True(view.Animator.enabled, "交接应等动画后的LateUpdate");
                typeof(EnemyCtrl).GetMethod("OnLateUpdate", flags).Invoke(controller, null);
                Assert.False(view.Animator.enabled);
            }
            finally { controller.ReleaseAll(); }
        }
        [Test] public void LongFrameExpiresNormalPhysicsWithoutLeavingPendingImpulse()
        {
            Spawn(); Call("BeginDeath", Vector3.forward, false); Call("Tick", 2f); Call("LateTick");
            Assert.True(instance.GetComponentsInChildren<Rigidbody>().All(body => body.isKinematic && !body.detectCollisions));
            Assert.False((bool)ReactionType.GetProperty("IsSimulating").GetValue(reaction));
            Assert.False((bool)Call("BeginDeath", Vector3.forward, true));
        }
        [Test] public void WarmStaggerExplosionAndReuseDoNotAllocateManagedMemory()
        {
            Spawn(); var feedback = (EnemyPhysicalReaction)reaction;
            view.Animator.Rebind(); view.Animator.Update(0f);
            void Cycle()
            {
                feedback.BeginDeath(Vector3.forward, false); feedback.Tick(.08f); feedback.LateTick();
                feedback.Tick(.09f); feedback.LateTick(); feedback.ResetForReuse();
                feedback.BeginDeath(Vector3.right, true); feedback.Tick(2f); feedback.ResetForReuse();
            }
            Cycle(); Cycle();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) Cycle();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, "预热后的过渡、躯干施力与回池不应分配托管数组或对象");
        }

        [Test]
        public void FrozenCorpseFadesWithoutChangingSharedMaterialsAndReuseRestoresOpacity()
        {
            Spawn("ArcadeZombieShambler");
            var renderer = instance.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.enabled);
            var original = renderer.sharedMaterials;
            Assert.True(view.TryPlayPhysicalDeath(Vector3.forward, true, out var duration));
            view.TickCombatFeedback(2f);
            Assert.False(((EnemyPhysicalReaction)reaction).IsSimulating);
            Assert.Greater(duration, 6f);
            var fade = typeof(EnemyView).GetMethod("SetCorpseFade");
            Assert.NotNull(fade, "尸体应支持独立的回收前淡出");
            fade.Invoke(view, new object[] { .6f });
            Assert.AreNotSame(original[0], renderer.sharedMaterial);
            Assert.That(renderer.sharedMaterial.GetFloat("_Surface"), Is.EqualTo(1));
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, 0);
            Assert.That(block.GetColor("_BaseColor").a, Is.EqualTo(.5f).Within(.01f));
            Assert.That(original[0].GetColor("_BaseColor").a, Is.EqualTo(1f));
            view.ResetPhysicalFeedback();
            CollectionAssert.AreEqual(original, renderer.sharedMaterials);
            renderer.GetPropertyBlock(block, 0);
            Assert.True(block.isEmpty, "复用不得残留透明覆盖");
        }

        [TestCase(4)] [TestCase(8)]
        public void PhysicsBudgetFreezesOldCorpseWhileKeepingItVisible(int budget)
        {
            var architecture = new global::Game.Game();
            architecture.RegisterUtility<IViewInstanceProvider>(new UnityViewInstanceProvider());
            var controller = new EnemyCtrl(architecture);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(EnemyCtrl).GetField("corpseBudget", flags).SetValue(controller, budget);
            var type = typeof(EnemyCtrl).GetNestedType("ArcadeEnemyPresentationEntry", BindingFlags.NonPublic);
            var alive = (System.Collections.IDictionary)typeof(EnemyCtrl).GetField("arcadeEnemies", flags).GetValue(controller);
            var corpses = new System.Collections.Generic.List<EnemyView>();
            try
            {
                for (int id = 1; id <= budget + 1; id++)
                {
                    Spawn(); corpses.Add(view);
                    alive.Add(id, Activator.CreateInstance(type, id, "enemy.arcade.basic", view, 20, instance.transform.position, 0, false));
                    instance = null;
                    controller.RemoveEnemy(id, true, Vector3.forward, true);
                }
                Assert.True(corpses[0].gameObject.activeSelf, "物理预算不应立即移除可见尸体");
                Assert.False(corpses[0].GetComponent<EnemyPhysicalReaction>().IsSimulating);
                Assert.AreEqual(budget, corpses.Count(v => v.GetComponent<EnemyPhysicalReaction>().IsSimulating));
            }
            finally { controller.ReleaseAll(); }
        }

        [TestCase(false)] [TestCase(true)]
        public void BudgetFreezeBeforeLandingUsesGroundedDeathPoseWithoutReactivatingPhysics(bool explosive)
        {
            Spawn("ArcadeZombieShambler");
            var feedback = (EnemyPhysicalReaction)reaction;
            var hips = instance.GetComponentsInChildren<Transform>().Single(t => t.name == "Abdomen");
            var rootPosition = instance.transform.position; var standingRotation = hips.rotation;
            feedback.BeginDeath(Vector3.forward, explosive);
            if (explosive) hips.position += Vector3.up * 3f;
            feedback.StopDeathSimulation();
            feedback.Tick(.2f); feedback.LateTick();
            Assert.Less(hips.position.y - instance.transform.position.y, .65f, "预算冻结不能留下站立或悬空8秒的尸体");
            Assert.Greater(Quaternion.Angle(standingRotation, hips.rotation), 70f);
            Assert.AreEqual(rootPosition, instance.transform.position);
            Assert.False(feedback.HasPendingPhysics);
            Assert.False(view.Animator.enabled);
            Assert.True(instance.GetComponentsInChildren<Rigidbody>().All(b => b.isKinematic && !b.detectCollisions));
        }

        [Test]
        public void FadePreservesSlotOverridesReusesMaterialsAndDestroysOwnedCopies()
        {
            Spawn("ArcadeZombieShambler");
            var renderer = instance.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.enabled);
            var originals = renderer.sharedMaterials;
            var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", new Color(.2f, .4f, .6f, .8f));
            block.SetFloat("_Cutoff", .17f); renderer.SetPropertyBlock(block, 0);
            Material fading = null;
            for (int cycle = 0; cycle < 2; cycle++)
            {
                view.SetCorpseFade(.6f);
                if (cycle == 0) fading = renderer.sharedMaterial;
                else Assert.AreSame(fading, renderer.sharedMaterial, "复用同一实例时不得重复创建淡出材质");
                renderer.GetPropertyBlock(block, 0);
                Assert.That(block.GetColor("_BaseColor").a, Is.EqualTo(.4f).Within(.001f));
                Assert.That(block.GetFloat("_Cutoff"), Is.EqualTo(.17f));
                view.ResetPhysicalFeedback(); renderer.GetPropertyBlock(block, 0);
                Assert.That(block.GetColor("_BaseColor").a, Is.EqualTo(.8f));
                CollectionAssert.AreEqual(originals, renderer.sharedMaterials);
            }
            view.ReleasePresentationResources();
            Object.DestroyImmediate(instance); instance = null;
            Assert.True(fading == null, "最终销毁实例时必须释放淡出材质副本");
        }

        [Test]
        public void ControllerTickFadesThenRecyclesAtExpiry()
        {
            Spawn("ArcadeZombieShambler");
            var architecture = new global::Game.Game();
            architecture.RegisterUtility<IViewInstanceProvider>(new UnityViewInstanceProvider());
            var controller = new EnemyCtrl(architecture);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(EnemyCtrl).GetNestedType("ArcadeEnemyPresentationEntry", BindingFlags.NonPublic);
            var alive = (System.Collections.IDictionary)typeof(EnemyCtrl).GetField("arcadeEnemies", flags).GetValue(controller);
            var entry = Activator.CreateInstance(type, 1, "enemy.arcade.basic", view, 20, instance.transform.position, 0, false);
            var renderer = instance.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.enabled);
            var originals = renderer.sharedMaterials;
            alive.Add(1, entry); instance = null;
            try
            {
                controller.RemoveEnemy(1, true, Vector3.forward, true);
                type.GetProperty("ReleaseTime").SetValue(entry, Time.time + .6f);
                typeof(EnemyCtrl).GetMethod("OnUpdate", flags).Invoke(controller, null);
                Assert.AreNotSame(originals[0], renderer.sharedMaterial);
                Assert.True(view.gameObject.activeSelf);
                type.GetProperty("ReleaseTime").SetValue(entry, Time.time - 1f);
                typeof(EnemyCtrl).GetMethod("OnUpdate", flags).Invoke(controller, null);
                Assert.False(view.gameObject.activeSelf);
                CollectionAssert.AreEqual(originals, renderer.sharedMaterials);
            }
            finally { controller.ReleaseAll(); }
        }
    }
}
