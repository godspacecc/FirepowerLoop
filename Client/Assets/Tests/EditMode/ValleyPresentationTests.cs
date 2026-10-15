using System;
using System.Linq;
using System.Reflection;
using Luban;
using QFramework;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class ValleyPresentationTests
    {
        private CivilisationHarvestTests support;
        private GeneratedValleyContentHandle handle;
        private CivilisationSceneCtrl controller;
        private CivilisationPointView water,tree,legacy,prototype;
        [SetUp] public void SetUp()
        {
            support=new CivilisationHarvestTests();support.SetUp(); ((Game)Game.Interface).RegisterSystem(new MapSceneSystem(null,null,null,null,null,null));var config=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/"+n+".bytes").bytes)));
            config.TryGetCivilisationCatalog(out var catalog,out _);config.TryGetValleyGenerationProfile(MapIds.CivilisationValley,out var profile,out _);ValleyLayoutGenerator.TryGenerate(37,0,profile,catalog,out var layout,out _);
            var original=TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.CivilisationValley,1);original.Context.MapContentRoot.SetActive(false);
            var root=new GameObject("GeneratedRoot");root.transform.SetParent(original.Context.MapContentRoot.transform,false);var kit=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Environment/CivilisationValleyGenerationKit.prefab"),root.transform);
            handle=new GeneratedValleyContentHandle(original,new EmptyLoader(),layout);Field("GeneratedRoot",root);Field("KitInstance",kit);
            var source=kit.GetComponent<ValleyGenerationKit>();
            CivilisationPointView Copy(string templateId,string id,Transform parent)
            {var v=UnityEngine.Object.Instantiate(source.FindTemplate(templateId),parent);typeof(CivilisationPointView).GetMethod("ConfigureGenerated",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(v,new object[]{CivilisationPointKind.Search,id});return v;}
            water=Copy("water.valley.ford","generated.water",root.transform);tree=Copy("tree.valley.5","generated.tree",root.transform);legacy=Copy("tree.valley.5","tree.valley.5",original.Context.MapContentRoot.transform);prototype=source.FindTemplate("tree.valley.5");
            foreach(var v in new[]{water,tree}){var visual=new GameObject("OwnResource");visual.transform.SetParent(v.transform,false);new SerializedObject(v).Update();typeof(CivilisationPointView).GetField("harvestedVisualRoot",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(v,visual);}
            typeof(GeneratedValleyContentHandle).GetProperty("Points").SetValue(handle,new[]{new CivilisationSearchPointDefinition("generated.water",MapIds.CivilisationValley,null,8,1,3,Vector3.zero,"civilisation.point.water",0,false,true),new CivilisationSearchPointDefinition("generated.tree",MapIds.CivilisationValley,null,9,4,3,Vector3.zero,"civilisation.point.tree",17,true,false)});
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.CivilisationValley,handle);controller=new CivilisationSceneCtrl();controller.Initialize();
        }
        private void Field(string name,object value)=>typeof(GeneratedValleyContentHandle).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(handle,value);
        private void Bind()=>typeof(CivilisationSceneCtrl).GetMethod("BindCommittedMap",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,null);
        private static object Owner(CivilisationPointView v)=>typeof(CivilisationPointView).GetField("controller",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(v);
        private static GameObject Resource(CivilisationPointView v)=>(GameObject)typeof(CivilisationPointView).GetField("harvestedVisualRoot",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(v);
        [Test] public void ClueTemplate_HasVisibleReusablePhysicalMarker()
        {var kit=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Environment/CivilisationValleyGenerationKit.prefab").GetComponent<ValleyGenerationKit>();Assert.That(kit.PreparationTemplate.GetComponentsInChildren<MeshRenderer>(true).Any(r=>r.enabled),Is.True);}
        [Test] public void GeneratedClue_InteractionNameUsesSameLocalizedKeyAsWorldPrompt()
        {
            var model=new LocalizationModel();((Game)Game.Interface).RegisterModel(model);var utility=new LocalizationUtility(false);utility.LoadFromJson(model.CurrentLanguageCode.Value,"{\"civilisation.point.clue\":\"查看沿河线索\"}");((Game)Game.Interface).RegisterUtility(utility);
            typeof(CivilisationPointView).GetMethod("ConfigureGeneratedPrompt",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(tree,new object[]{"civilisation.point.clue"});Assert.That(tree.DisplayName,Is.EqualTo("查看沿河线索"));
        }
        [TearDown] public void TearDown(){controller?.Dispose();if(handle!=null){handle.ReleaseGeneratedAsync().GetAwaiter().GetResult();UnityEngine.Object.DestroyImmediate(handle.Original.Context.gameObject);}support?.TearDown();}
        [Test] public void GeneratedBinding_ExcludesLegacyAndHiddenKitTemplates()
        {Bind();Assert.That(Owner(tree),Is.Not.Null);Assert.That(Owner(legacy),Is.Null);Assert.That(Owner(prototype),Is.Null);}
        [Test] public void RepeatedWater_WithOldVisitedStateKeepsVisualAndCollider()
        {Game.Interface.GetModel<CivilisationExpeditionModel>().Visit("generated.water",null);Bind();Assert.That(Resource(water).activeSelf,Is.True);Assert.That(water.GetComponent<Collider>().enabled,Is.True);}
        [Test] public void NewJourneyReset_RefreshesPreviouslyHiddenHarvestVisual()
        {Game.Interface.GetModel<CivilisationExpeditionModel>().Visit("generated.tree",null);Bind();Assert.That(Resource(tree).activeSelf,Is.False);Game.Interface.GetModel<CivilisationExpeditionModel>().Reset();Game.Interface.SendEvent(new ExpeditionChangedEvent());Assert.That(Resource(tree).activeSelf,Is.True);}
        [Test] public void ResourceCulling_RestoresNearbyRenderersWithoutRevivingHarvestedTree()
        {
            var visual = Resource(tree);
            var renderer = visual.AddComponent<MeshRenderer>();
            using var visibility = new ValleyVisibilityCtrl(Array.Empty<Renderer>(), new Renderer[] { renderer });
            var cull = typeof(ValleyVisibilityCtrl).GetMethod("Cull", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(cull, Is.Not.Null);
            var centers = new[] { Vector3.zero };
            cull.Invoke(null, new object[] { new Renderer[] { renderer }, centers, new Vector3(400,0,0), 180f });
            Assert.That(renderer.enabled, Is.False);
            Game.Interface.GetModel<CivilisationExpeditionModel>().Visit("generated.tree",null); Bind();
            cull.Invoke(null, new object[] { new Renderer[] { renderer }, centers, Vector3.zero, 180f });
            Assert.That(renderer.enabled, Is.True);
            Assert.That(visual.activeSelf, Is.False, "渲染裁剪不能复活已经保存的砍伐状态");
            Assert.That(tree.GetComponent<Collider>().enabled, Is.False);
        }
        private sealed class EmptyLoader:IResourceLoader
        {public T LoadAsset<T>(string p)where T:UnityEngine.Object=>null;public ResourceLoadHandle LoadAssetAsync<T>(string p,Action<T> c)where T:UnityEngine.Object=>ResourceLoadHandle.Empty;public void Release(ResourceLoadHandle h){} }
    }
}
