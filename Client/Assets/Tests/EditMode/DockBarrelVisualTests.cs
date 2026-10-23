using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>正式桶资源与丧尸附属物的生命周期；只消费权威时间，不创建替代敌人。</summary>
    public sealed class DockBarrelVisualTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private const string Asset="Assets/Game/Art/Arcade/VFX/Barrel/VortexBarrelVisual.prefab";
        private GameObject root,zombie;
        private ArcadeCombatViewController view;
        private EnemyPort port;
        private IResourceLoader previousLoader;
        private void Field(string key,object value)=>typeof(ArcadeCombatViewController).GetField(key,Hidden).SetValue(view,value);
        [SetUp] public void Setup()
        {
            previousLoader=ResourceLoader.Current;ResourceLoader.Use(new FormalLoader());
            root=new GameObject("DockBarrelVisualTest");zombie=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"));
            zombie.SetActive(true);zombie.GetComponent<EnemyView>().SetNavigationEnabled(false);port=new EnemyPort{Root=zombie.transform};view=new ArcadeCombatViewController();Field("root",root);Field("enemyPresentation",port);
            Field("contentTemplates",AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content"));
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            Field("earlyStage",ArcadeEarlyStageParameters.FromConfig(tables.TbArcadeEarlyStage.Get(21)));
            view.ConfigureRoundBounds(20.5f,19.5f,.55f);view.SetRoundLayout(ArcadeRoundLayoutParameters.FromConfig(tables.TbArcadeRoundLayout.Get("arena.21.round.1")));
            Assert.That(view.IsRoundLayoutReady,Is.True,view.RoundLayoutLoadError);
        }
        [TearDown] public void Cleanup(){view?.End();if(zombie!=null)Object.DestroyImmediate(zombie);if(root!=null)Object.DestroyImmediate(root);ResourceLoader.Use(previousLoader);}
        private Transform Pack()=>zombie.GetComponentsInChildren<Transform>(true).SingleOrDefault(t=>t.name=="CarrierBarrel_7");
        [Test] public void LateContentTemplateRetriesWithoutPublishingPartialGroundOrCarrier()
        {
            var templates=typeof(ArcadeCombatViewController).GetField("contentTemplates",Hidden).GetValue(view);Field("contentTemplates",null);
            var areas=new[]{new ArcadeContentAreaSnapshot(-10000,11,-3,2,.45f,false),new ArcadeContentAreaSnapshot(-10001,13,2,3,.45f,false)};
            Assert.DoesNotThrow(()=>view.SetRoundContent(2,areas));Assert.DoesNotThrow(()=>view.SetEnemyContent(7,1,0,false));Assert.That(Pack(),Is.Null);
            Assert.That(root.GetComponentsInChildren<Transform>(true).Any(t=>t.name.StartsWith("GroundBarrel_")),Is.False);
            Field("contentTemplates",templates);view.SetRoundContent(3,areas);view.SetEnemyContent(7,1,0,false);
            Assert.That(root.GetComponentsInChildren<ParticleSystem>(true).Length,Is.EqualTo(2));Assert.That(Pack(),Is.Not.Null);
            view.SetRoundContent(3,areas);view.SetEnemyContent(7,1,0,false);Assert.That(root.GetComponentsInChildren<ParticleSystem>(true).Length,Is.EqualTo(2));
        }
        [Test] public void FormalTemplateHasApprovedScalePersistentMaterialsAndNoCollider()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Asset);Assert.That(prefab,Is.Not.Null);
            var body=prefab.transform.Find("BarrelBody");Assert.That(body,Is.Not.Null);
            Assert.That(body.GetComponent<Renderer>().bounds.size.y,Is.EqualTo(.8809823f).Within(.003f));
            Assert.That(prefab.GetComponentsInChildren<Collider>(true),Is.Empty);
            Assert.That(prefab.GetComponentInChildren<ParticleSystem>(true),Is.Not.Null);
            foreach(var r in prefab.GetComponentsInChildren<Renderer>(true)){Assert.That(EditorUtility.IsPersistent(r.sharedMaterial),Is.True,r.name);Assert.That(r.sharedMaterial.shader,Is.Not.Null);}
        }
        [Test] public void CarrierKeepsOfficialZombieAndOnlyRemovesPackWhenUnloaded()
        {
            var animator=zombie.GetComponent<EnemyView>().Animator;view.SetEnemyContent(7,1,0,false);
            Assert.That(Pack(),Is.Not.Null,"载桶应挂在原有正式丧尸上");Assert.That(view.HasRoundEnemyContent(7),Is.False,"不能屏蔽原丧尸刷新");Assert.That(port.Removed,Is.Zero);
            view.SetEnemyContent(7,0,0,false);Assert.That(Pack(),Is.Null);Assert.That(animator,Is.Not.Null);Assert.That(port.Removed,Is.Zero);
        }
        [Test] public void RepeatedSnapshotsKeepOnePackAndFreezeAtSameAuthorityTime()
        {
            view.SetRoundContent(1,Array.Empty<ArcadeContentAreaSnapshot>());view.SetEnemyContent(7,1,0,false);var pack=Pack();Assert.That(pack,Is.Not.Null);
            var smoke=pack.GetComponentInChildren<ParticleSystem>(true);var before=new ParticleSystem.Particle[220];int count=smoke.GetParticles(before);Assert.That(count,Is.EqualTo(190));
            view.SetRoundContent(1,Array.Empty<ArcadeContentAreaSnapshot>());view.SetEnemyContent(7,1,0,false);
            Assert.That(Pack(),Is.SameAs(pack));var after=new ParticleSystem.Particle[220];Assert.That(smoke.GetParticles(after),Is.EqualTo(count));Assert.That(after[20].position,Is.EqualTo(before[20].position));
            view.SetRoundContent(2,Array.Empty<ArcadeContentAreaSnapshot>());smoke.GetParticles(after);Assert.That(after[20].position,Is.Not.EqualTo(before[20].position));
        }
        [Test] public void GroundAndDroppedBarrelsUseApprovedVortexAndDisappearWithSnapshot()
        {
            view.SetRoundContent(2,new[]{new ArcadeContentAreaSnapshot(-10000,11,-3,2,.45f,false),new ArcadeContentAreaSnapshot(-10001,13,2,3,.45f,false)});
            Assert.That(root.GetComponentsInChildren<ParticleSystem>(true).Length,Is.EqualTo(2),"固定桶和卸下桶都使用已采用紫雾");
            view.SetRoundContent(3,Array.Empty<ArcadeContentAreaSnapshot>());Assert.That(root.GetComponentsInChildren<ParticleSystem>(true),Is.Empty);
        }
        [Test] public void MapReleaseRemovesPacksOutsideRoundRootAndKeepsZombie()
        {
            view.SetEnemyContent(7,1,0,false);Assert.That(Pack(),Is.Not.Null);
            typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);Assert.That(Pack(),Is.Null);Assert.That(zombie,Is.Not.Null);
        }
        [Test] public void LateEnemyBindingRetriesWithoutLeavingPackOnRecycledRoot()
        {
            port.Root=null;view.SetEnemyContent(7,1,0,false);Assert.That(Pack(),Is.Null);
            port.Root=zombie.transform;view.SetEnemyContent(7,1,0,false);Assert.That(Pack(),Is.Not.Null);
            var replacement=new GameObject("ReplacementEnemy");try
            {
                port.Root=replacement.transform;view.SetEnemyContent(7,1,0,false);Assert.That(Pack(),Is.Null);Assert.That(replacement.transform.Find("CarrierBarrel_7"),Is.Not.Null);
                view.RemoveRoundEnemyContent(7);Assert.That(replacement.transform.childCount,Is.Zero);
            }finally{Object.DestroyImmediate(replacement);}
        }
        private sealed class EnemyPort:IArcadeEnemyPresentationPort
        {
            public Transform Root;public int Removed;
            public bool TryGetEnemyPresentationRoot(int id,out Transform value){value=Root;return value!=null;}
            public bool TrySetEnemy(int id,string config,Vector3 position,Vector3 facing,int health,int maximum,out string error,bool telegraph=false,int sequence=0,bool persistentHealthBar=false){error=null;return true;}
            public void RemoveEnemy(int id,bool playDeath=false){Removed++;}
            public void ClearArcadeEnemies(){}
        }
        private sealed class FormalLoader:IResourceLoader
        {
            public T LoadAsset<T>(string path) where T:Object=>AssetDatabase.LoadAssetAtPath<T>("Assets/Game/"+path+".prefab");
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> loaded) where T:Object{loaded(LoadAsset<T>(path));return ResourceLoadHandle.Empty;}
            public void Release(ResourceLoadHandle handle){}
        }
    }
}
