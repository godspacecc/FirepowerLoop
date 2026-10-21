using System;
using System.Collections;
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
    /// <summary>桶专属爆炸的正式资源、快照去重及地图资源释放。</summary>
    public sealed class BarrelExplosionEffectsTests
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        ArcadeCombatViewController view;GameObject root;
        Transform Kit=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content");
        object Get(string name){var field=view.GetType().GetField(name,Hidden);Assert.That(field,Is.Not.Null,"桶专属运行态缺失："+name);return field.GetValue(view);}
        void Set(string name,object value){var field=view.GetType().GetField(name,Hidden);Assert.That(field,Is.Not.Null,"桶专属入口缺失："+name);field.SetValue(view,value);}
        void Call(string method,params object[] args){var m=view.GetType().GetMethod(method,Hidden|BindingFlags.Public);Assert.That(m,Is.Not.Null,"桶专属表现缺失："+method);m.Invoke(view,args);}
        [SetUp] public void Setup()
        {
            root=new GameObject("BarrelExplosionOwner");view=new ArcadeCombatViewController();Set("root",root);view.ConfigureRoundBounds(19.5f,19.5f,.55f);
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            view.SetRoundLayout(ArcadeRoundLayoutParameters.FromConfig(tables.TbArcadeRoundLayout.DataList.First(p=>p.ArenaConfigNumber==3&&p.RoundNumber==1)));
        }
        [TearDown] public void Cleanup(){view?.End();if(root!=null)Object.DestroyImmediate(root);}
        [Test] public void FormalTemplateKeepsPurpleLayersAndOriginalGunfireMaterial()
        {
            var template=Kit.Find("VortexBarrelBlast");Assert.That(template,Is.Not.Null,"正式租约缺少紫色桶爆炸");
            Assert.That(template.GetComponentsInChildren<Collider>(true),Is.Empty);
            foreach(string name in new[]{"ImpactFlash","Burst","Sparks","Smoke","BarrelFragments","PurpleResidualSmoke"})
            {
                var particle=template.Find(name)?.GetComponent<ParticleSystem>();Assert.That(particle,Is.Not.Null,name);
                Assert.That(particle.main.loop,Is.False);Assert.That(particle.main.startColor.color.b,Is.GreaterThan(particle.main.startColor.color.g),name);
            }
            var original=Kit.Find("ProjectileBlast/Burst").GetComponent<ParticleSystemRenderer>().sharedMaterial;
            Assert.That(AssetDatabase.GetAssetPath(original),Does.EndWith("/Fire.mat"),"普通爆炸不能被紫色材质覆盖");
            Assert.That(File.Exists(Path.Combine(Application.dataPath,"Game/Audio/Sound/ArcadeExplosionSciFi.wav")),Is.True);
        }
        [Test] public void SnapshotRepeatsPlayOnlyOnceAndRoofImpactsKeepTheirIdentity()
        {
            Set("barrelBlastTemplate",Kit.Find("VortexBarrelBlast"));
            var area=new ArcadeContentAreaSnapshot(42,14,2,3,3,false);
            view.SetRoundContent(1,new[]{area});view.SetRoundContent(1.1,new[]{area});view.SetRoundContent(1.2,Array.Empty<ArcadeContentAreaSnapshot>());view.SetRoundContent(1.3,new[]{area});
            Assert.That(((IList)Get("barrelBlastPool")).Count,Is.EqualTo(1),"快照重放不能重复播放");
            Assert.That(((IDictionary)Get("roundAreas")).Count,Is.Zero,"桶爆炸不能生成持续危险圈");
            view.SetRoundContent(1.4,new[]{new ArcadeContentAreaSnapshot(43,3,0,0,3,false)});
            Assert.That(((IDictionary)Get("roundAreas")).Count,Is.EqualTo(1),"屋顶落桶仍使用原事件投影");
        }
        [Test] public void UnreadyTemplateDoesNotConsumeExplosionAndRoundReleaseResetsIdentity()
        {
            var area=new ArcadeContentAreaSnapshot(42,14,2,3,3,false);view.SetRoundContent(1,new[]{area});
            Set("barrelBlastTemplate",Kit.Find("VortexBarrelBlast"));view.SetRoundContent(1.1,new[]{area});
            var pool=(IList)Get("barrelBlastPool");Assert.That(pool.Count,Is.EqualTo(1));
            Call("ReleaseRoundContent");Assert.That(pool.Count,Is.Zero);Assert.That(((IEnumerable)Get("roundSeenBarrelBlasts")).Cast<object>().Count(),Is.Zero);
        }
        [Test] public void DenseBarrelBlastsUseIndependentBoundedPoolAndReleaseAtEnd()
        {
            Set("barrelBlastTemplate",Kit.Find("VortexBarrelBlast"));Set("projectileBlastTemplate",Kit.Find("ProjectileBlast"));
            for(int i=0;i<40;i++)Call("PlayBarrelExplosion",new Vector2(i,2),3f);
            Assert.That(((IList)Get("barrelBlastPool")).Count,Is.EqualTo(12));
            Assert.That(((IList)Get("projectileBlastPool")).Count,Is.Zero);
            Call("PlayBarrelExplosion",Vector2.zero,float.NaN);Assert.That(((IList)Get("barrelBlastPool")).Count,Is.EqualTo(12));
            var pool=(IList)Get("barrelBlastPool");view.End();Assert.That(pool.Count,Is.Zero);
            Call("PlayBarrelExplosion",Vector2.zero,3f);Assert.That(pool.Count,Is.Zero);
        }
    }
}
