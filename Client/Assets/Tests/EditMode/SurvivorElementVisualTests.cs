using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
using QFramework;
namespace Game.Tests
{
    /// <summary>正式丧尸上的元素表现及回收、受击色、暂停验证，不修改资源或玩家生命。</summary>
    public sealed class SurvivorElementVisualTests
    {
        private static GameObject Create(string name="ArcadeZombieShambler")=>Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/"+name+".prefab"));
        private static Component Feedback(GameObject root)
        {var value=root.GetComponentsInChildren<MonoBehaviour>(true).FirstOrDefault(c=>c.GetType().Name=="EnemyElementFeedback");Assert.That(value,Is.Not.Null,"正式丧尸尚未接入采用版元素效果");return value;}
        private static void Call(Component target,string name,params object[] args)
        {var method=target.GetType().GetMethod(name,BindingFlags.Public|BindingFlags.Instance);Assert.That(method,Is.Not.Null,name);method.Invoke(target,args);}
        private static ParticleSystem Particles(Component feedback,string name)=>feedback.GetComponentsInChildren<ParticleSystem>(true).Single(p=>p.name==name);
        [TestCase("ArcadeZombieShambler",true)][TestCase("ArcadeZombieGaunt",true)][TestCase("ArcadeZombieBloated",true)]
        [TestCase("ArcadeZombieShambler",false)][TestCase("ArcadeZombieGaunt",false)][TestCase("ArcadeZombieBloated",false)]
        public void FormalEnemyStartsIdleShowsOnlyChosenEffectAndClears(string name,bool fire)
        {
            var root=Create(name);try
            {
                var f=Feedback(root);Assert.That(f.GetComponentsInChildren<ParticleSystem>().All(p=>p.particleCount==0),Is.True);
                Call(f,"SetState",fire,!fire);Call(f,"Tick",.2f,false);
                var active=Particles(f,fire?"ElementFire":"ElementFrost");Assert.That(active.particleCount,Is.GreaterThan(0));Assert.That(active.isPaused,Is.True,"粒子必须由既有表现帧手动推进");
                Assert.That(Particles(f,fire?"ElementFrost":"ElementFire").particleCount,Is.Zero);
                Call(f,"SetState",false,false);Assert.That(f.GetComponentsInChildren<ParticleSystem>().All(p=>p.particleCount==0),Is.True);
            }finally{Object.DestroyImmediate(root);}
        }
        [Test] public void RepeatedSyncDoesNotRestartParticlesAndZeroTickFreezes()
        {
            var root=Create();try
            {
                var f=Feedback(root);Call(f,"SetState",true,false);Call(f,"Tick",.2f,false);var ps=Particles(f,"ElementFire");float time=ps.time;
                Call(f,"SetState",true,false);Call(f,"Tick",0f,false);Assert.That(ps.time,Is.EqualTo(time));
                Call(f,"Tick",.1f,false);Assert.That(ps.time,Is.GreaterThan(time));
            }finally{Object.DestroyImmediate(root);}
        }
        [TestCase(false)][TestCase(true)] public void ColdColorRestoresEveryMaterialAndSurvivesHitFlash(bool expiresDuringFlash)
        {
            var root=Create();try
            {
                var f=Feedback(root);var view=root.GetComponent<EnemyView>();
                var slots=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.sharedMaterials.Select((m,i)=>new{Renderer=r,Material=m,Slot=i,Color=m.GetColor("_BaseColor")})).ToArray();Assert.That(slots,Is.Not.Empty);var block=new MaterialPropertyBlock();
                view.SetElementState(false,true);view.TickElementFeedback(.1f);
                foreach(var slot in slots){slot.Renderer.GetPropertyBlock(block,slot.Slot);Assert.That(Vector4.Distance(block.GetColor("_BaseColor"),Color.Lerp(slot.Color,new Color(.4f,.85f,1),.24f)),Is.LessThan(.00001f));}
                view.PlayHitFeedback();view.TickElementFeedback(0);
                foreach(var slot in slots){slot.Renderer.GetPropertyBlock(block,slot.Slot);Assert.That(block.isEmpty,Is.True,"真实闪红期间必须归还每个材质槽");}
                if(expiresDuringFlash)view.SetElementState(false,false);
                view.TickCombatFeedback(1);view.TickElementFeedback(0);
                foreach(var slot in slots){slot.Renderer.GetPropertyBlock(block,slot.Slot);Assert.That(block.isEmpty,Is.EqualTo(expiresDuringFlash));}
                view.ResetPhysicalFeedback();
                foreach(var slot in slots){slot.Renderer.GetPropertyBlock(block,slot.Slot);Assert.That(block.isEmpty,Is.True);Assert.That(slot.Material.GetColor("_BaseColor"),Is.EqualTo(slot.Color));}
            }finally{Object.DestroyImmediate(root);}
        }
        [Test] public void ViewReuseDeathAndDisableClearElementState()
        {
            var root=Create();try
            {
                var view=root.GetComponent<EnemyView>();var f=Feedback(root);
                Call(view,"SetElementState",true,false);Call(view,"TickElementFeedback",.2f);Assert.That(Particles(f,"ElementFire").particleCount,Is.GreaterThan(0));
                view.ResetPhysicalFeedback();Assert.That(Particles(f,"ElementFire").particleCount,Is.Zero);
                Call(view,"SetElementState",false,true);Call(view,"TickElementFeedback",.2f);Call(view,"ResetElementFeedback");Assert.That(Particles(f,"ElementFrost").particleCount,Is.Zero);
                Call(view,"SetElementState",true,false);Call(view,"TickElementFeedback",.2f);root.SetActive(false);
                Assert.That(f.GetComponentsInChildren<ParticleSystem>(true).All(p=>p.particleCount==0),Is.True);
                root.SetActive(true);view.BeginPresentationUse();Assert.That(f.GetComponentsInChildren<ParticleSystem>(true).All(p=>p.particleCount==0),Is.True);
            }finally{Object.DestroyImmediate(root);}
        }
        [Test] public void SourceEffectsHaveSerializedMaterialsTexturesAndNoAutoClock()
        {
            var root=Create();try
            {
                var f=Feedback(root);foreach(var p in f.GetComponentsInChildren<ParticleSystem>(true))
                {
                    Assert.That(p.main.playOnAwake,Is.False);Assert.That(p.main.maxParticles,Is.LessThanOrEqualTo(80));
                    var material=p.GetComponent<ParticleSystemRenderer>().sharedMaterial;Assert.That(AssetDatabase.GetAssetPath(material),Does.StartWith("Assets/Game/Art/Survivor/Elements/"));
                    Assert.That(material.GetTexture("_BaseMap"),Is.Not.Null);Assert.That(material.shader,Is.Not.Null);
                }
            }finally{Object.DestroyImmediate(root);}
        }
        [Test] public void EffectFollowsCrawlingBodyWithoutChangingStandingScale()
        {
            var root=Create();try
            {
                var view=root.GetComponent<EnemyView>();view.BeginPresentationUse();view.SetNavigationEnabled(false);view.Animator.Rebind();view.Animator.Update(0);
                var f=Feedback(root);var visual=view.Animator.transform.parent;Assert.That(f.transform.parent,Is.EqualTo(visual));
                var before=f.transform.position;var local=visual.InverseTransformPoint(before);Assert.That(Vector3.Distance(f.transform.lossyScale,root.transform.lossyScale),Is.LessThan(.001f));
                view.SetGroundEntry(new SurvivorGroundEntryState{Started=true,Age=1.25f,ForwardZ=1},Vector3.forward,Vector3.up);view.LateTickCombatFeedback();
                Assert.That(Vector3.Distance(f.transform.position,visual.TransformPoint(local)),Is.LessThan(.001f));Assert.That(Vector3.Distance(before,f.transform.position),Is.GreaterThan(.1f));
                view.ResetPhysicalFeedback();Assert.That(Vector3.Distance(before,f.transform.position),Is.LessThan(.001f));
            }finally{Object.DestroyImmediate(root);}
        }
        [Test] public void MissingParticleOrMaterialRejectsPreparationValidation()
        {
            var root=Create();try
            {
                var view=root.GetComponent<EnemyView>();var feedback=Feedback(root);Assert.That(view.ValidateElementFeedback(out var error),Is.True,error);
                var serialized=new SerializedObject(feedback);var field=serialized.FindProperty("fireParticles");var fire=field.objectReferenceValue;field.objectReferenceValue=null;serialized.ApplyModifiedPropertiesWithoutUndo();Assert.That(view.ValidateElementFeedback(out error),Is.False);Assert.That(error,Is.Not.Empty);
                field.objectReferenceValue=fire;serialized.ApplyModifiedPropertiesWithoutUndo();((ParticleSystem)fire).GetComponent<ParticleSystemRenderer>().sharedMaterial=null;Assert.That(view.ValidateElementFeedback(out error),Is.False);
            }finally{Object.DestroyImmediate(root);}
        }
        [Test] public void HealthDisplayWithoutFillDoesNotRestartElementParticles()
        {
            var root=Create();try
            {
                var view=root.GetComponent<EnemyView>();var f=Feedback(root);view.SetElementState(true,false);view.TickElementFeedback(.2f);var ps=Particles(f,"ElementFire");float before=ps.time;
                typeof(EnemyView).GetField("healthBarFill",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(view,null);view.SetHealthDisplay(90,100);view.SetElementState(true,false);
                Assert.That(ps.time,Is.EqualTo(before));Assert.That(ps.particleCount,Is.GreaterThan(0));
            }finally{Object.DestroyImmediate(root);}
        }
        [TestCase(false)][TestCase(true)]
        public void RealControllerBridgePausesAndClearsOnSuspendOrDeath(bool death)
        {
            var root=Create();var map=new GameObject("ElementControllerTest");var architecture=new Game();
            var survivor=new SurvivorModel{Active=true};architecture.RegisterModel(survivor);architecture.RegisterModel(new AppStateModel());architecture.GetModel<AppStateModel>().CurrentState.Value=AppState.Playing;
            architecture.RegisterUtility<IViewInstanceProvider>(new Instances());var enemy=new EnemyCtrl(architecture);var bridge=new ArcadeCombatViewController();const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            try
            {
                UIHelper.ResetForTests();var view=root.GetComponent<EnemyView>();view.SetNavigationEnabled(false);
                typeof(EnemyCtrl).GetField("arcadeRoot",flags).SetValue(enemy,map.transform);
                var type=typeof(EnemyCtrl).GetNestedType("ArcadeEnemyPresentationEntry",BindingFlags.NonPublic);
                var entry=Activator.CreateInstance(type,new object[]{42,"test",view,100,root.transform.position,0,false});
                ((System.Collections.IDictionary)typeof(EnemyCtrl).GetField("arcadeEnemies",flags).GetValue(enemy)).Add(42,entry);
                typeof(ArcadeCombatViewController).GetField("enemyPresentation",flags).SetValue(bridge,enemy);
                bridge.SetEnemyElementState(42,!death,death);var f=Feedback(root);var ps=Particles(f,death?"ElementFrost":"ElementFire");
                var tick=typeof(EnemyCtrl).GetMethod("TickArcadeFeedback",flags);for(int i=0;i<3;i++)tick.Invoke(enemy,new object[]{.2f,0f});Assert.That(ps.particleCount,Is.GreaterThan(0));float time=ps.time;
                survivor.Paused=true;tick.Invoke(enemy,new object[]{1f,1f});Assert.That(ps.time,Is.EqualTo(time));
                if(death)enemy.RemoveEnemy(42,true);else enemy.SuspendEnemy(42);
                Assert.That(f.GetComponentsInChildren<ParticleSystem>(true).All(p=>p.particleCount==0),Is.True);
                if(death)
                {
                    view.SetCorpseFade(.2f);view.ResetPhysicalFeedback();view.BeginPresentationUse();
                    var block=new MaterialPropertyBlock();foreach(var body in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))for(int i=0;i<body.sharedMaterials.Length;i++){body.GetPropertyBlock(block,i);Assert.That(block.isEmpty,Is.True,"冰霜死亡淡出后再使用不能遗留冷色");}
                }
            }
            finally{enemy.ReleaseAll();if(root!=null)Object.DestroyImmediate(root);Object.DestroyImmediate(map);UIHelper.ResetForTests();}
        }
        private sealed class Instances:IViewInstanceProvider
        {
            public T Instantiate<T>(T prefab,Transform parent,Vector3 position,Quaternion rotation)where T:Component=>Object.Instantiate(prefab,position,rotation,parent);
            public void Release(GameObject instance){if(instance!=null)Object.DestroyImmediate(instance);}
        }
    }
}
