using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Game.Tests
{
    public sealed class SurvivorDroneFlameVisualTests
    {
        /// <summary>真实无人机实例只在选择改装后挂出喷嘴，开火结束仅清空粒子。</summary>
        [Test] public void NozzleRequiresUpgradeAndStaysVisibleWhenFireStops()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeSupportDroneVisual.prefab");
            var body=Object.Instantiate(prefab);
            try
            {
                const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
                var view=new ArcadeCombatViewController();
                var entryType=typeof(ArcadeCombatViewController).GetNestedType("EffectVisualEntry",BindingFlags.NonPublic);
                var entry=Activator.CreateInstance(entryType,true);
                entryType.GetField("DisplayName").SetValue(entry,"survivor.drone");
                var entries=(IDictionary)typeof(ArcadeCombatViewController).GetField("effectVisuals",hidden).GetValue(view);
                entries.Add("powerup:1:"+(int)global::ArcadePowerupBehavior.Drone,entry);
                var nozzle=body.transform.Find("FlameRetrofit").gameObject;
                var flame=nozzle.GetComponentInChildren<ParticleSystem>(true);
                Assert.That(nozzle.activeSelf,Is.False);
                view.SetDroneFlame(true,Vector2.zero,Vector2.up,false,0);
                entryType.GetField("Instance").SetValue(entry,body);
                typeof(ArcadeCombatViewController).GetMethod("BindDroneNozzle",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{entry});
                Assert.That(nozzle.activeSelf,Is.True,"加载完成后恢复改装状态");
                Assert.That(flame.particleCount,Is.Zero);
                view.SetDroneFlame(true,Vector2.zero,Vector2.up,true,.2f);
                Assert.That(flame.particleCount,Is.GreaterThan(0));
                view.SetDroneFlame(true,Vector2.zero,Vector2.up,false,0);
                Assert.That(nozzle.activeSelf,Is.True);
                Assert.That(flame.particleCount,Is.Zero);
                view.SetDroneFlame(false,Vector2.zero,Vector2.up,false,0);
                Assert.That(nozzle.activeSelf,Is.False);
            }
            finally{Object.DestroyImmediate(body);}
        }
    }
}
