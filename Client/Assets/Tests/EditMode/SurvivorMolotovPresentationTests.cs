using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Game.Tests
{
    public sealed class SurvivorMolotovPresentationTests
    {
        /// <summary>正式粘液模板作为Content真实依赖，表现池可复用并在地图释放时清空。</summary>
        [Test] public void SlimeTemplateIsCollectedBoundedAndReleased()
        {
            const string path="Assets/Game/Art/Arcade/VFX/SurvivorSlimeBottle/SurvivorSlimeBottle.prefab";
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content/SurvivorSlimeBottle"),Is.Not.Null);
            var setting=AssetDatabase.LoadAssetAtPath<Object>("Assets/AssetBundleCollectorSetting.asset");
            var result=setting.GetType().GetMethod("BeginCollect").Invoke(setting,new object[]{"DefaultPackage",false,false});
            var entry=((IEnumerable)Read(result,"CollectAssets")).Cast<object>().Single(a=>(string)Read(a,"Address")=="Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet");
            Assert.That(((IEnumerable)Read(entry,"AssetTags")).Cast<string>(),Does.Contain("arcade"));
            var deps=((IEnumerable)entry.GetType().GetField("DependAssets").GetValue(entry)).Cast<object>().Select(x=>(string)x.GetType().GetField("AssetPath").GetValue(x)).ToArray();
            foreach(var name in new[]{"SurvivorSlimeBottle.prefab","Slime.mat","BottleGreen.mat","BottleCap.mat","SlimePool.asset"})Assert.That(deps,Does.Contain(path.Replace("SurvivorSlimeBottle.prefab",name)));
            var root=new GameObject("SlimePresentationTest");var view=new ArcadeCombatViewController();typeof(ArcadeCombatViewController).GetField("root",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,root);
            try {
                Call(view,"BindSlimeBottleVisuals",AssetDatabase.LoadAssetAtPath<GameObject>(path).transform);
                for(int i=0;i<12;i++){view.SetSurvivorSlimeBottle(i,Vector2.zero,1,90,true);view.SetSurvivorSlimeZone(i,Vector2.zero,1.2f,.2f,true);}
                var bottles=(IDictionary)Field(view,"slimeBottleBottles");var zones=(IDictionary)Field(view,"slimeBottleZones");
                Assert.That(bottles.Count,Is.EqualTo(2));Assert.That(zones.Count,Is.EqualTo(4));var zone=(SurvivorSlimeZoneVisual)zones[0];Assert.That(zone.IsValid,Is.True);
                view.SetSurvivorSlimeZone(0,Vector2.zero,1.2f,3,false);view.SetSurvivorSlimeZone(20,Vector2.one,2.1f,.2f,true);Assert.That(zones[20],Is.SameAs(zone));Assert.That(zone.Pool.localScale.x,Is.EqualTo(1.75f).Within(.00001));
                Call(view,"ClearSlimeBottleVisuals");Assert.That(root.transform.childCount,Is.Zero);
            }finally{Call(view,"ClearSlimeBottleVisuals");Object.DestroyImmediate(root);}
        }
        private const string Template="Assets/Game/Art/Arcade/VFX/SurvivorMolotov/SurvivorMolotov.prefab";
        private static object Read(object value,string name)=>value.GetType().GetProperty(name).GetValue(value);
        private static void Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,args);
        private static object Field(object value,string name)=>value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);

        /// <summary>检查真实收集地址、下载标签与递归依赖；不打包、不重建模拟清单。</summary>
        [Test] public void MolotovTemplatesAndIconAreInRealCollectedDependencies()
        {
            var setting=AssetDatabase.LoadAssetAtPath<Object>("Assets/AssetBundleCollectorSetting.asset");
            var result=setting.GetType().GetMethod("BeginCollect").Invoke(setting,new object[]{"DefaultPackage",false,false});
            var assets=((IEnumerable)Read(result,"CollectAssets")).Cast<object>().ToArray();
            var report=new System.Collections.Generic.List<string>();
            foreach(var address in new[]{"Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet","Prefabs/UI/Survivor/SurvivorHud"})
            {
                var matches=assets.Where(a=>(string)Read(a,"Address")==address).ToArray();Assert.That(matches.Length,Is.EqualTo(1));var asset=matches[0];
                Assert.That(((IEnumerable)Read(asset,"AssetTags")).Cast<string>(),Does.Contain("arcade"));
                var dependencies=((IEnumerable)asset.GetType().GetField("DependAssets").GetValue(asset)).Cast<object>().Select(a=>(string)a.GetType().GetField("AssetPath").GetValue(a)).ToArray();
                if(address.Contains("Obstacle"))foreach(var path in new[]{Template,Template.Replace("SurvivorMolotov.prefab","SoftScorch.mat"),Template.Replace("SurvivorMolotov.prefab","SoftScorch.png"),Template.Replace("SurvivorMolotov.prefab","BottleGreen.mat")})Assert.That(dependencies,Does.Contain(path));
                else Assert.That(dependencies,Does.Contain(Template.Replace("SurvivorMolotov.prefab","MolotovIcon.png")));
                report.Add(address+" | arcade | "+string.Join(";",dependencies.Where(p=>p.Contains("SurvivorMolotov"))));
            }
            var tags=(string[])typeof(Boot.ResourceBoot).GetField("StartupResourceTags",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);Assert.That(tags,Does.Not.Contain("arcade"));
            var directory=Path.GetFullPath("../Backup/Artifacts/SurvivorMolotov-20261101/implementation");Directory.CreateDirectory(directory);File.WriteAllLines(Path.Combine(directory,"real-collection.txt"),report);
        }

        /// <summary>实际模板的火焰随权威年龄采样；多次复用不突破容量，离场释放全部实例。</summary>
        [Test] public void MolotovPresentationIsBoundedReusableAndManuallySampled()
        {
            var root=new GameObject("MolotovPresentationTest");var view=new ArcadeCombatViewController();
            typeof(ArcadeCombatViewController).GetField("root",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,root);
            try
            {
                Call(view,"BindMolotovVisuals",AssetDatabase.LoadAssetAtPath<GameObject>(Template).transform);
                Call(view,"BindSlimeBottleVisuals",AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Arcade/VFX/SurvivorSlimeBottle/SurvivorSlimeBottle.prefab").transform);
                Assert.That(view.IsSurvivorMolotovReady,Is.True);
                for(int i=0;i<12;i++){view.SetSurvivorMolotov(i,Vector2.zero,1,90,true);view.SetSurvivorFireZone(i,Vector2.zero,1.2f,.3f,true);}
                var bottles=(IDictionary)Field(view,"molotovBottles");var zones=(IDictionary)Field(view,"molotovZones");Assert.That(bottles.Count,Is.EqualTo(2));Assert.That(zones.Count,Is.EqualTo(4));
                var zone=(SurvivorFireZoneVisual)zones[0];Assert.That(zone.Flames.particleCount,Is.GreaterThan(0));Assert.That(zone.Flames.isPaused,Is.True);Assert.That(zone.Scorch.localScale.x,Is.EqualTo(2.4f));
                foreach(var particle in zone.GetComponentsInChildren<ParticleSystem>(true)){Assert.That(particle.main.playOnAwake,Is.False);Assert.That(particle.main.maxParticles,Is.LessThanOrEqualTo(180));}
                view.SetSurvivorFireZone(0,Vector2.zero,1.2f,3,false);view.SetSurvivorFireZone(20,Vector2.one,2.1f,.2f,true);Assert.That(zones[20],Is.SameAs(zone));Assert.That(zone.Scorch.localScale.x,Is.EqualTo(4.2f));Assert.That(root.transform.childCount,Is.EqualTo(6));
                Call(view,"ClearMolotovVisuals");Assert.That(root.transform.childCount,Is.Zero);Assert.That(view.IsSurvivorMolotovReady,Is.False);
            }
            finally{Call(view,"ClearMolotovVisuals");Object.DestroyImmediate(root);}
        }

        /// <summary>正式槽位图标必须由生成器绑定，投掷武器就绪时显示满圈。</summary>
        [Test] public void MolotovSlotUsesBoundIconWithReadyRing()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Survivor/Box/SurvivorWeaponSlot.prefab");var go=Object.Instantiate(prefab);
            try{var box=go.GetComponent<SurvivorWeaponSlotBoxUI>();Assert.That(box.MolotovIcon,Is.Not.Null);typeof(SurvivorHudUICtrl).GetMethod("RenderWeaponSlot",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{box,new SurvivorModel(),"Molotov"});Assert.That(box.Icon.sprite,Is.SameAs(box.MolotovIcon));Assert.That(box.Icon.gameObject.activeSelf,Is.True);Assert.That(box.AmmoRing.gameObject.activeSelf,Is.True);Assert.That(box.AmmoRing.fillAmount,Is.EqualTo(1f));}
            finally{Object.DestroyImmediate(go);}
        }
    }
}
