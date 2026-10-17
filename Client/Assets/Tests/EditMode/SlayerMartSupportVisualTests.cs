using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>正式支援资源的多实例身份、真实坐标、姿态与回收；不创建第二份战斗逻辑。</summary>
    public sealed class SlayerMartSupportVisualTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private ArcadeCombatViewController view;
        private GameObject root;
        private ArcadePowerupDefinition chicken;
        private IDictionary entries;
        private Type entryType;
        [SetUp] public void Setup()
        {
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);
            chicken=catalog.Powerups.Single(p=>p.Behavior==ArcadePowerupBehavior.Chicken);
            view=new();root=new GameObject("SlayerMartVisualTests");typeof(ArcadeCombatViewController).GetField("root",Flags).SetValue(view,root);
            entries=(IDictionary)typeof(ArcadeCombatViewController).GetField("effectVisuals",Flags).GetValue(view);
            entryType=typeof(ArcadeCombatViewController).GetNestedType("EffectVisualEntry",BindingFlags.NonPublic);
        }
        [TearDown] public void Cleanup(){InvokePrivate("ReleaseAllEffectVisuals");if(root!=null)Object.DestroyImmediate(root);}
        [Test] public void SameProductKeepsSeparateInstanceModels()
        {var a=Seed(1,"a",0);var b=Seed(1,"b",0);view.BeginEffectSync();Set(1,"a",0,chicken,new(1,2));Set(1,"b",0,chicken,new(3,4));view.EndEffectSync();Assert.That(entries.Count,Is.EqualTo(2));Assert.That(Get<GameObject>(a,"Instance"),Is.Not.SameAs(Get<GameObject>(b,"Instance")));}
        [Test] public void OwnerAndBodyIdsCannotCollapseIntoOneVisual()
        {Seed(1,"saw",0);Seed(1,"saw",1);Seed(2,"saw",0);view.BeginEffectSync();Set(1,"saw",0,chicken,new(1,0));Set(1,"saw",1,chicken,new(-1,0));Set(2,"saw",0,chicken,new(5,0));view.EndEffectSync();Assert.That(entries.Count,Is.EqualTo(3));}
        [Test] public void RealBodyPositionIgnoresLegacyHorizontalVisualOffset()
        {var e=Seed(1,"a",0);var d=new ArcadePowerupDefinition(chicken.Id,chicken.Behavior,chicken.ActiveVisualPrefabAddress,true,new(10,2,20),0,0,0,0,0,0);Set(1,"a",0,d,new(3,4));Assert.That(Get<GameObject>(e,"Instance").transform.localPosition,Is.EqualTo(new Vector3(3,2,4)));}
        [Test] public void SuccessfulChickenShotSetsRotationAndZeroKeepsIt()
        {var e=Seed(1,"a",0);Set(1,"a",0,chicken,Vector2.zero,Vector2.right);var instance=Get<GameObject>(e,"Instance");Assert.That(Vector3.Distance(instance.transform.forward,Vector3.right),Is.LessThan(.001f));Set(1,"a",0,chicken,Vector2.zero);Assert.That(Vector3.Distance(instance.transform.forward,Vector3.right),Is.LessThan(.001f));}
        [Test] public void ExpiredInstanceIsReleasedWithoutRemovingActiveSibling()
        {var old=Seed(1,"old",0);Seed(1,"next",0);view.BeginEffectSync();Set(1,"next",0,chicken,Vector2.zero);view.EndEffectSync();Assert.That(entries.Count,Is.EqualTo(1));Assert.That(Get<bool>(old,"Released"),Is.True);Assert.That(Get<GameObject>(old,"Instance"),Is.Null);}
        [Test] public void MapReleaseInvalidatesPendingAndLoadedInstances()
        {var loaded=Seed(1,"loaded",0);var pending=Seed(1,"pending",0,false);Set(1,"loaded",0,chicken,Vector2.zero);Set(1,"pending",0,chicken,Vector2.zero);InvokePrivate("ReleaseAllEffectVisuals");Assert.That(entries.Count,Is.Zero);Assert.That(Get<bool>(loaded,"Released")&&Get<bool>(pending,"Released"),Is.True);}
        [Test] public void FormalSupportPrefabsHaveRenderersAndNoPhysicsColliders()
        {
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            var provider=new LubanRuntimeConfigProvider(()=>tables);Assert.That(provider.TryGetArcadeCatalog(out var catalog,out _),Is.True);
            foreach(var kind in new[]{ArcadePowerupBehavior.Chicken,ArcadePowerupBehavior.ElectricOrb,ArcadePowerupBehavior.Saw,ArcadePowerupBehavior.Barrel})
            {var d=catalog.Powerups.Single(p=>p.Behavior==kind);var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/"+d.ActiveVisualPrefabAddress+".prefab");Assert.That(prefab,Is.Not.Null,kind.ToString());Assert.That(prefab.GetComponentsInChildren<Renderer>(true).Length,Is.GreaterThan(0));Assert.That(prefab.GetComponentsInChildren<Collider>(true),Is.Empty);}
        }
        private object Seed(long owner,string id,int body,bool loaded=true)
        {var e=Activator.CreateInstance(entryType,true);Put(e,"Key",Key(owner,id,body));Put(e,"Address",chicken.ActiveVisualPrefabAddress);if(loaded)Put(e,"Instance",Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/"+chicken.ActiveVisualPrefabAddress+".prefab"),root.transform));entries.Add(Key(owner,id,body),e);return e;}
        private static string Key(long owner,string id,int body)=>"slayermart:"+owner+":"+id+":"+body;
        private void Put(object e,string field,object value)=>entryType.GetField(field,Flags).SetValue(e,value);
        private T Get<T>(object e,string field)=>(T)entryType.GetField(field,Flags).GetValue(e);
        private void Set(long owner,string id,int body,ArcadePowerupDefinition d,Vector2 position,Vector2 direction=default)
        {var m=typeof(ArcadeCombatViewController).GetMethod("SetSlayerMartSupport");Assert.That(m,Is.Not.Null,"缺少多实例支援表现入口");m.Invoke(view,new object[]{owner,id,body,d,position,direction});}
        private void InvokePrivate(string method)=>typeof(ArcadeCombatViewController).GetMethod(method,Flags).Invoke(view,null);
    }
}
