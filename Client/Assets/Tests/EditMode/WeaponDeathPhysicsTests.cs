using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>真实僵尸骨架上的力度分层、暂停及复用检查。</summary>
    public sealed class WeaponDeathPhysicsTests
    {
        private GameObject instance;
        private EnemyPhysicalReaction reaction;
        private Rigidbody chest;
        private Vector3 initialPosition;
        [SetUp] public void SetUp()
        {
            instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"));
            initialPosition=instance.transform.position;
            var view=instance.GetComponent<EnemyView>();view.BeginPresentationUse();view.SetNavigationEnabled(false);
            reaction=instance.GetComponent<EnemyPhysicalReaction>();
            chest=(Rigidbody)typeof(EnemyPhysicalReaction).GetField("chestBody",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(reaction);
        }
        [TearDown] public void TearDown()
        {if(instance!=null){instance.GetComponent<EnemyView>().ReleasePresentationResources();Object.DestroyImmediate(instance);}}
        private void Begin(bool explosive,int kind)
        {
            var method=typeof(EnemyPhysicalReaction).GetMethod("BeginDeathWithImpact");
            Assert.NotNull(method,"死亡物理需接收权威表现类型");
            Assert.That(method.Invoke(reaction,new object[]{Vector3.right,explosive,kind}),Is.True);
        }
        [Test] public void EnergyBlastHasLessLiftTravelAndSpinThanHeavyExplosion()
        {
            reaction.BeginDeath(Vector3.right,true);var heavy=chest.velocity;float spin=chest.angularVelocity.magnitude;
            reaction.ResetForReuse();Begin(true,1);
            Assert.That(chest.velocity.x,Is.InRange(2f,heavy.x-.5f));
            Assert.That(chest.velocity.y,Is.InRange(.5f,heavy.y-2f));
            Assert.That(chest.angularVelocity.magnitude,Is.LessThan(spin));
        }
        [Test] public void CloseShotgunTravelsBackWithLowLiftAndLimitedTumbling()
        {
            Begin(false,2);
            Assert.That(reaction.IsSimulating,Is.True);
            Assert.That(chest.velocity.x,Is.InRange(3f,5f));Assert.That(chest.velocity.y,Is.InRange(0f,1f));
            Assert.That(chest.angularVelocity.magnitude,Is.LessThan(2f));
            Assert.That(instance.transform.position,Is.EqualTo(initialPosition));
        }
        [Test] public void TypedDeathPauseAndReuseDoNotLeakIntoOrdinaryDeath()
        {
            Begin(false,2);reaction.Tick(0);reaction.LateTick();Assert.That(reaction.IsSimulating,Is.True);
            reaction.ResetForReuse();reaction.BeginDeath(Vector3.right,false);
            Assert.That(reaction.IsSimulating,Is.False);reaction.Tick(.17f);reaction.LateTick();
            Assert.That(chest.velocity.x,Is.LessThan(2));Assert.That(chest.velocity.y,Is.LessThan(0));
            reaction.Tick(2);Assert.That(reaction.IsSimulating,Is.False);
        }

        [Test] public void ControllerPassesShotgunStyleWithoutSpawningExplosiveMist()
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var root=new GameObject("WeaponDeathTestMap");instance.transform.SetParent(root.transform,true);
            var architecture=new global::Game.Game();architecture.RegisterUtility<IViewInstanceProvider>(new UnityViewInstanceProvider());
            var controller=new EnemyCtrl(architecture);
            typeof(EnemyCtrl).GetField("arcadeRoot",flags).SetValue(controller,root.transform);
            var entryType=typeof(EnemyCtrl).GetNestedType("ArcadeEnemyPresentationEntry",BindingFlags.NonPublic);
            var entries=(System.Collections.IDictionary)typeof(EnemyCtrl).GetField("arcadeEnemies",flags).GetValue(controller);
            entries.Add(1,System.Activator.CreateInstance(entryType,1,"enemy.arcade.basic",instance.GetComponent<EnemyView>(),20,initialPosition,0,false));
            try
            {
                controller.RemoveEnemy(1,true,Vector3.right,false,false,2);
                Assert.That(chest.velocity.x,Is.GreaterThan(3));Assert.That(chest.velocity.y,Is.LessThan(1));
                Assert.That(System.Linq.Enumerable.Any(root.GetComponentsInChildren<ParticleSystem>(true),p=>p.name=="ChestMist"),Is.False);
            }
            finally {controller.ReleaseAll();Object.DestroyImmediate(root);instance=null;}
        }
    }
}
