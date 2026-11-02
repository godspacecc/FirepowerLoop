using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>正式短爆燃的资源、权威采样、暂停和对象池复用回归，不修改产品资源。</summary>
    public sealed class SurvivorBlastVisualTests
    {
        private GameObject root;private SurvivorBlastView view;
        [SetUp] public void Setup(){var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/SurvivorWeaponBlast.prefab");root=UnityEngine.Object.Instantiate(prefab);root.SetActive(true);view=root.GetComponent<SurvivorBlastView>();}
        [TearDown] public void Cleanup(){if(root!=null)UnityEngine.Object.DestroyImmediate(root);}
        private ParticleSystem[] Particles()=>root.GetComponentsInChildren<ParticleSystem>(true);
        private void RequireParticles(){Assert.That(Particles().Length,Is.EqualTo(3),"已采用的闪光/爆燃/短烟尚未接入");}
        [Test] public void FormalPrefabUsesAdoptedSettingsAndKeepsProjectileReference()
        {
            RequireParticles();Assert.That(root.GetComponentsInChildren<LineRenderer>(true),Is.Empty);
            foreach(var p in Particles()){
                Assert.That(p.main.loop,Is.False);Assert.That(p.main.playOnAwake,Is.False);Assert.That(p.GetComponent<ParticleSystemRenderer>().sharedMaterial,Is.Not.Null);
                Assert.That(p.main.startLifetime.constant,Is.EqualTo(p.name=="ImpactFlash"?.14f:p.name=="Burst"?.28f:.4f).Within(.00001));
            }
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxBullet.prefab").GetComponent<ProjectileView>().SurvivorBlastTemplate,Is.Not.Null);
        }
        [Test] public void RepeatedProgressHoldsParticlesAndKeepsManualClock()
        {
            RequireParticles();view.Apply(Vector3.zero,1.4f,.25f);var times=Particles().Select(p=>p.time).ToArray();var counts=Particles().Select(p=>p.particleCount).ToArray();view.Apply(Vector3.zero,1.4f,.25f);
            Assert.That(Particles().Select(p=>p.time),Is.EqualTo(times));Assert.That(Particles().Select(p=>p.particleCount),Is.EqualTo(counts));Assert.That(Particles().All(p=>p.isPaused),Is.True);
        }
        [Test] public void AuthoritativeProgressAdvancesParticleSeconds()
        {RequireParticles();view.Apply(Vector3.zero,1.4f,.1f);view.Apply(Vector3.zero,1.4f,.3f);foreach(var p in Particles())Assert.That(p.time,Is.EqualTo(.12f).Within(.0001));}
        [Test] public void DisableClearsSmokeAndReuseStartsAtZero()
        {
            RequireParticles();view.Apply(Vector3.zero,1.4f,.5f);Assert.That(Particles().Sum(p=>p.particleCount),Is.GreaterThan(0));root.SetActive(false);Assert.That(Particles().Sum(p=>p.particleCount),Is.Zero);root.SetActive(true);view.Apply(Vector3.one,1.4f,0);foreach(var p in Particles())Assert.That(p.time,Is.Zero.Within(.0001));
        }
        [Test] public void RewindingPresentationRestartsInsteadOfAccumulating()
        {RequireParticles();view.Apply(Vector3.zero,1.4f,.8f);view.Apply(Vector3.zero,1.4f,.2f);foreach(var p in Particles())Assert.That(p.time,Is.EqualTo(.08f).Within(.0001));}
        [TestCase(1.4f,.5f)][TestCase(2.2f,.7857143f)] public void VisualRadiusUsesAdoptedArcadeScale(float radius,float scale)
        {RequireParticles();var point=new Vector3(5,0,8);view.Apply(point,radius,0);Assert.That(Vector3.Distance(root.transform.position,point+Vector3.up*.75f),Is.LessThan(.0001));Assert.That(root.transform.localScale.x,Is.EqualTo(scale).Within(.0001));}
        [Test] public void MissingParticleDependencyFailsProjectilePreloadValidation()
        {
            RequireParticles();var validate=typeof(SurvivorBlastView).GetMethod("Validate");Assert.That(validate,Is.Not.Null);object[] args={null};Assert.That(validate.Invoke(view,args),Is.EqualTo(true));
            var bullet=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxBullet.prefab"));
            try
            {
                var serialized=new SerializedObject(bullet.GetComponent<ProjectileView>());serialized.FindProperty("survivorBlastTemplate").objectReferenceValue=view;serialized.ApplyModifiedPropertiesWithoutUndo();
                var prepare=typeof(ProjectileCtrl).GetMethod("ValidatePrefab",BindingFlags.NonPublic|BindingFlags.Static);object[] entry={"Prefabs/Combat/GrayboxBullet",bullet,null};Assert.That(prepare.Invoke(null,entry),Is.EqualTo(true));
                UnityEngine.Object.DestroyImmediate(Particles()[0].gameObject);args[0]=null;Assert.That(validate.Invoke(view,args),Is.EqualTo(false));Assert.That(args[0],Is.Not.Null);
                entry[2]=null;Assert.That(prepare.Invoke(null,entry),Is.EqualTo(false));Assert.That(entry[2],Is.Not.Null);
            }finally{UnityEngine.Object.DestroyImmediate(bullet);}
        }
    }
}
