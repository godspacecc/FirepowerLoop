using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Game.Tests
{
    /// <summary>正式进化特效的异步失效与暂停检查，不推进战斗或裁决伤害。</summary>
    public sealed class ArcadeWeaponEvolutionPresentationTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private ArcadeCombatViewController view;
        private GameObject root;
        private Loader loader;
        private IResourceLoader previous;
        private IDictionary Entries=>(IDictionary)typeof(ArcadeCombatViewController).GetField("effectVisuals",Flags).GetValue(view);
        [SetUp]public void Setup(){root=new GameObject("evolution-presentation-test");view=new ArcadeCombatViewController();typeof(ArcadeCombatViewController).GetField("root",Flags).SetValue(view,root);previous=ResourceLoader.Current;loader=new Loader();ResourceLoader.Use(loader);}
        [TearDown]public void Cleanup(){try{view.End();loader.Complete();}finally{ResourceLoader.Use(previous);Object.DestroyImmediate(root);}}
        private void Area(int id,float radius=.65f){var method=typeof(ArcadeCombatViewController).GetMethod("SetWeaponBurnArea",Flags);Assert.That(method,Is.Not.Null,"已采用残火需要正式表现入口");method.Invoke(view,new object[]{id,Vector2.zero,radius,2f});}
        [Test]public void RemovedAreaCannotReturnFromLateLoad()
        {view.BeginEffectSync();Area(1);view.EndEffectSync();view.BeginEffectSync();view.EndEffectSync();loader.Complete();Assert.That(Entries.Count,Is.Zero);Assert.That(root.transform.childCount,Is.Zero);}
        [Test]public void LoadedGroundKeepsRadiusAndPausesItsParticleClock()
        {
            view.BeginEffectSync();Area(1,1.3f);view.EndEffectSync();loader.Complete();
            var instance=root.transform.GetChild(0).gameObject;Assert.That(instance.transform.localScale.x,Is.EqualTo(2).Within(.001f));
            var visual=instance.GetComponent("ArcadeWeaponEvolutionVisual");Assert.That(visual,Is.Not.Null);var tick=visual.GetType().GetMethod("Tick");var clock=visual.GetType().GetProperty("ElapsedSeconds");
            tick.Invoke(visual,new object[]{.1f});var elapsed=(float)clock.GetValue(visual);tick.Invoke(visual,new object[]{0f});Assert.That(clock.GetValue(visual),Is.EqualTo(elapsed));
            view.End();Assert.That(Entries.Count,Is.Zero);
        }
        [Test]public void ClusterReceiptsPlayOnceAndRejectInvalidRadius()
        {
            var type=typeof(OnlineArcadePresentationSystem).Assembly.GetType("Game.OnlineWeaponFeedbackPresentationState");var state=Activator.CreateInstance(type,true);
            var played=new List<OnlineArcadeWeaponFeedbackSnapshot>();
            var hits=new[]{new OnlineArcadeWeaponFeedbackSnapshot(1,4,0,0,0,1,2.8f,1,100000),new OnlineArcadeWeaponFeedbackSnapshot(2,5,0,0,0,1,1.25f,1,100000),new OnlineArcadeWeaponFeedbackSnapshot(3,5,0,0,0,1,float.NaN,1,100000)};
            object[] args={hits,false,1,true,100100L,10.1,(Action<OnlineArcadeWeaponFeedbackSnapshot>)(h=>played.Add(h))};
            type.GetMethod("Observe").Invoke(state,args);type.GetMethod("Observe").Invoke(state,args);
            Assert.That(played.Count,Is.EqualTo(2));Assert.That(played[0].Kind,Is.EqualTo(4));Assert.That(played[1].Kind,Is.EqualTo(5));
        }
        [Test]public void ProtocolCopyKeepsGroundLifetimeAndGrenadePhase()
        {
            var source=new ET.OnlineArcadeBattleInfo();source.ContentAreas.Add(new ET.OnlineArcadeContentAreaInfo{Id=12,Kind=31,Radius=.65f,RemainingSeconds=.8f});
            source.Projectiles.Add(new ET.OnlineArcadeProjectileInfo{RuntimeId=3,OwnerPlayerId=1,SourceWeaponId="arcade.weapon.grenade",WeaponPowerLevel=2,EvolutionClusterChild=true,RemainingSeconds=.2f});
            var snapshot=(OnlineArcadeBattleSnapshot)typeof(OnlineArcadeRunSystem).GetMethod("CopyBattle",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{source});
            source.ContentAreas.Clear();source.Projectiles.Clear();
            Assert.That(snapshot.ContentAreas[0].RemainingSeconds,Is.EqualTo(.8f));Assert.That(snapshot.Projectiles[0].RemainingSeconds,Is.EqualTo(.2f));Assert.That(snapshot.Projectiles[0].EvolutionClusterChild,Is.True);
        }
        [Test]public void GrenadeBurstEmitsParticlesAndGroundTailFinishes()
        {
            view.BeginEffectSync();Area(1);view.EndEffectSync();loader.Complete();
            view.PlayEvolvedGrenadeExplosion(Vector2.zero,2.8f,false);loader.Complete();view.TickPlayerFeedback(.1f);
            int count=0;foreach(var p in root.GetComponentsInChildren<ParticleSystem>())count+=p.particleCount;
            Assert.That(count,Is.GreaterThan(0),"正式爆炸模板必须实际发射粒子");
            view.BeginEffectSync();view.EndEffectSync();view.TickPlayerFeedback(2);Assert.That(Entries.Count,Is.Zero,"烟尾必须有界回收");
        }
        [Test]public void DelayedBlastLoadResumesCurrentAgeInsteadOfReplaying()
        {
            view.PlayEvolvedGrenadeExplosion(Vector2.zero,2.8f,false);view.TickPlayerFeedback(1);loader.Complete();
            var visual=root.GetComponentInChildren<ArcadeWeaponEvolutionVisual>();Assert.That(visual.ElapsedSeconds,Is.EqualTo(1).Within(.001f));
            foreach(var line in visual.GetComponentsInChildren<LineRenderer>())Assert.That(line.enabled,Is.False,"晚到资源不补播过期冲击环");
            view.TickPlayerFeedback(.7f);Assert.That(Entries.Count,Is.Zero);
        }
        private sealed class Loader:IResourceLoader
        {
            private readonly List<Action> pending=new List<Action>();
            public T LoadAsset<T>(string path)where T:Object=>AssetDatabase.LoadAssetAtPath<T>("Assets/Game/"+path+".prefab");
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> callback)where T:Object{pending.Add(()=>callback(LoadAsset<T>(path)));return ResourceLoadHandle.Empty;}
            public void Release(ResourceLoadHandle handle){}
            public void Complete(){var callbacks=pending.ToArray();pending.Clear();foreach(var callback in callbacks)callback();}
        }
    }
}
