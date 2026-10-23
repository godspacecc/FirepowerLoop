using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>核对已采用九项候选的正式资源及混合爆炸池，不改变战斗现场。</summary>
    public sealed class ArcadeExplosionAdoptionTests
    {
        [TestCase("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab","Content/VortexBarrelBlast","Sparks,BarrelFragments","Burst,PurpleResidualSmoke")]
        [TestCase("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab","Content/BombBlast","Sparks,Shards,Rays","Flash,Shockwave,ShockwaveHalo,Dust")]
        [TestCase("Assets/Game/Prefabs/SceneObjects/ArcadeDroneExplosion.prefab","","Sparks","Burst,Smoke")]
        [TestCase("Assets/Game/Prefabs/SceneObjects/ArcadeWeaponGrenadePrimaryBlast.prefab","","blast-sparks","blast-fire,blast-smoke,blast-ring0")]
        [TestCase("Assets/Game/Prefabs/SceneObjects/ArcadeWeaponGrenadeChildBlast.prefab","","blast-sparks","blast-fire,blast-smoke,blast-ring0")]
        [TestCase("Assets/Game/Art/Arcade/VFX/EnemyExplosionDeath/EnemyExplosionDeath.prefab","","SmallFragments","ChestMist,GroundDust")]
        public void AdoptedTemplatesSuppressOnlyScatter(string path,string scope,string hidden,string kept)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);Assert.That(prefab,Is.Not.Null,path);
            var root=string.IsNullOrEmpty(scope)?prefab.transform:prefab.transform.Find(scope);Assert.That(root,Is.Not.Null,scope);
            var renderers=root.GetComponentsInChildren<Renderer>(true);
            foreach(string name in hidden.Split(','))Assert.That(renderers.Single(r=>r.name==name).enabled,Is.False,path+"/"+name+"仍在飞散");
            foreach(string name in kept.Split(','))Assert.That(renderers.Single(r=>r.name==name).enabled,Is.True,path+"/"+name+"主体不应被关闭");
        }
        [Test] public void EnergyFlashAndOrdinaryFlashRestoreAcrossPoolReuse()
        {
            var root=new GameObject("adopted-pool-test");
            try
            {
                const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var view=new ArcadeCombatViewController();var type=view.GetType();type.GetField("root",flags).SetValue(view,root);
                var template=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content/ProjectileBlast");
                type.GetField("projectileBlastTemplate",flags).SetValue(view,template);var play=type.GetMethod("PlayProjectileBlast",flags);
                var original=template.Find("ImpactFlash").GetComponent<ParticleSystem>().main;
                play.Invoke(view,new object[]{Vector2.zero,1.35f,true});
                var instance=root.transform.GetChild(0);var flash=instance.Find("ImpactFlash").GetComponent<ParticleSystem>();
                Assert.That(instance.Find("Sparks").GetComponent<ParticleSystem>().isPlaying,Is.False,"能量弹也要收掉放射火星");
                Assert.That(flash.main.startLifetime.constantMax,Is.EqualTo(.32f).Within(.001f));
                Assert.That(flash.main.startSize.constantMax,Is.EqualTo(original.startSize.constantMax*1.35f).Within(.001f));
                for(int i=0;i<12;i++)play.Invoke(view,new object[]{Vector2.zero,2.8f,false});
                Assert.That(root.transform.childCount,Is.EqualTo(12));
                Assert.That(flash.main.startLifetime.constantMax,Is.EqualTo(original.startLifetime.constantMax).Within(.001f));
                Assert.That(flash.main.startSize.constantMax,Is.EqualTo(original.startSize.constantMax).Within(.001f));
                Assert.That(instance.Find("Burst").GetComponent<ParticleSystem>().isPlaying,Is.True);
            }
            finally{Object.DestroyImmediate(root);}
        }
    }
}
