using System;
using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Linq;

namespace Tests.EditMode
{
    public sealed class BaseWeatherVisualTests
    {
        [Test]
        public void RainLighting_PreservesOrbitAndRestoresSunnyIntensity()
        {
            var method=typeof(DayNightView).GetMethod("ApplyWeather",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.That(method,Is.Not.Null);
            var go=new GameObject("WeatherLight",typeof(Light));var view=go.AddComponent<DayNightView>();
            try
            {
                // 编辑模式 AddComponent 不执行运行期 Awake，夹具必须显式完成真实灯光引用初始化。
                typeof(DayNightView).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);
                view.Apply(new DayNightVisualSnapshot(.5f,false));var light=go.GetComponent<Light>();
                var rotation=light.transform.rotation;var intensity=light.intensity;
                method.Invoke(view,new object[]{new BaseWeatherSnapshot(1,1,1,"rain")});
                Assert.That(light.intensity,Is.LessThan(intensity));Assert.That(light.transform.rotation,Is.EqualTo(rotation));
                method.Invoke(view,new object[]{new BaseWeatherSnapshot(0,0,0,"dry")});Assert.That(light.intensity,Is.EqualTo(intensity).Within(.0001));
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        [Test]
        public void WetGround_DoesNotModifySharedMaterialAndReleaseRestoresReference()
        {
            var type=typeof(DayNightView).Assembly.GetType("Game.BaseWeatherView");Assert.That(type,Is.Not.Null);
            var go=new GameObject("WeatherSurface");var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);
            var original=new Material(Shader.Find("Universal Render Pipeline/Lit"));original.SetColor("_BaseColor",Color.gray);
            var renderer=ground.GetComponent<Renderer>();renderer.sharedMaterial=original;
            var view=go.AddComponent(type);
            try
            {
                type.GetField("wetSurfaces",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,new[]{renderer});
                type.GetMethod("Initialize",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);
                var copy=renderer.sharedMaterial;
                type.GetMethod("Initialize",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);
                Assert.That(renderer.sharedMaterial,Is.SameAs(copy));
                type.GetMethod("Apply",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,new object[]{new BaseWeatherSnapshot(1,1,1,"rain")});
                Assert.That(renderer.sharedMaterial,Is.Not.SameAs(original));
                Assert.That(original.GetColor("_BaseColor"),Is.EqualTo(Color.gray));
                Assert.That(renderer.sharedMaterial.GetColor("_BaseColor").r,Is.LessThan(.5f));
                type.GetMethod("Apply",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,new object[]{new BaseWeatherSnapshot(0,0,1,"clear")});
                Assert.That(renderer.sharedMaterial.GetColor("_BaseColor").r,Is.LessThan(.5f),"雨停不能立即清除湿地");
                type.GetMethod("Release",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);
                type.GetMethod("Release",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);
                Assert.That(renderer.sharedMaterial,Is.SameAs(original));
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(ground);UnityEngine.Object.DestroyImmediate(original);}
        }
        [TestCase(false)]
        [TestCase(true)]
        public void RainBlocker_ClipsParticleLifetimeAndPauseDoesNotEmit(bool renderOnly)
        {
            var owner=new GameObject("RainBlockerTest");var roof=GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.transform.position=new Vector3(0,4,0);roof.transform.localScale=new Vector3(50,.1f,50);Physics.SyncTransforms();
            var material=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));var view=owner.AddComponent<BaseWeatherView>();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(BaseWeatherView);
            try
            {
                type.GetField("rainMaterial",flags).SetValue(view,material);
                if(renderOnly)
                {
                    roof.GetComponent<Collider>().enabled=false;
                    var field=type.GetField("rainRoofs",flags);Assert.That(field,Is.Not.Null,"无碰撞屋顶也需要表现遮挡");
                    field.SetValue(view,new[]{roof.GetComponent<Renderer>()});
                }
                else type.GetField("rainBlockers",flags).SetValue(view,new[]{roof.GetComponent<Collider>()});
                type.GetMethod("Initialize",flags).Invoke(view,null);view.Apply(new BaseWeatherSnapshot(1,1,1,"rain"));
                view.StepVisual(.05f,Vector3.zero,false);var rain=owner.GetComponentInChildren<ParticleSystem>();var particles=new ParticleSystem.Particle[3000];
                var count=rain.GetParticles(particles);Assert.That(count,Is.GreaterThan(0));
                for(var i=0;i<count;i++)Assert.That(particles[i].startLifetime,Is.LessThan(.43f),"粒子必须在屋顶上方结束");
                view.StepVisual(1,Vector3.zero,true);Assert.That(rain.particleCount,Is.EqualTo(count));
            }
            finally{view.Release();UnityEngine.Object.DestroyImmediate(owner);UnityEngine.Object.DestroyImmediate(roof);UnityEngine.Object.DestroyImmediate(material);}
        }
        [Test]
        public void FormalBase_HasSixWetSurfacesAndProjectOwnedWeatherResources()
        {
            var old=SceneManager.GetActiveScene();var scene=SceneManager.GetSceneByPath("Assets/Game/Scenes/BaseMap.unity");var opened=!scene.IsValid()||!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene("Assets/Game/Scenes/BaseMap.unity",OpenSceneMode.Additive);
            try
            {
                var view=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<BaseWeatherView>(true)).Single();
                var serialized=new SerializedObject(view);Assert.That(serialized.FindProperty("wetSurfaces").arraySize,Is.EqualTo(6));
                Assert.That(serialized.FindProperty("rainRoofs").arraySize,Is.EqualTo(1),"正式木屋屋顶缺少降雨遮挡");
                foreach(var field in new[]{"rainMaterial","splashMaterial","rainClip"})
                {
                    var asset=serialized.FindProperty(field).objectReferenceValue;Assert.That(asset,Is.Not.Null);
                    var path=AssetDatabase.GetAssetPath(asset);Assert.That(path,Does.StartWith("Assets/Game/Art/Environment/BaseWeather/"));
                    Assert.That(AssetDatabase.GetDependencies(path,true).Any(p=>p.StartsWith("Assets/Art/")),Is.False);
                }
            }
            finally{if(opened)EditorSceneManager.CloseScene(scene,true);if(old.IsValid()&&old.isLoaded)SceneManager.SetActiveScene(old);}
        }
    }
}
