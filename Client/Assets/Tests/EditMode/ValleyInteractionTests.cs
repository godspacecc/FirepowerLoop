using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class ValleyInteractionTests
    {
        private CivilisationHarvestTests support;
        private GeneratedValleyContentHandle handle;
        private ValleyLayoutSaveData layout;
        private CivilisationCatalog templates;
        private CivilisationSearchPointDefinition tree,water;
        [SetUp] public void SetUp()
        {
            support=new CivilisationHarvestTests();support.SetUp();
            var config=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/"+n+".bytes").bytes)));
            Assert.That(config.TryGetCivilisationCatalog(out templates,out _),Is.True);Assert.That(config.TryGetValleyGenerationProfile(MapIds.CivilisationValley,out var profile,out _),Is.True);
            Assert.That(ValleyLayoutGenerator.TryGenerate(37,0,profile,templates,out layout,out _),Is.True);for(int i=0;i<layout.Heights.Length;i++)layout.Heights[i]=20;
            foreach(var p in layout.Points)p.Pose.Y=20;
            var original=TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.CivilisationValley,1);
            handle=new GeneratedValleyContentHandle(original,new EmptyLoader(),layout);
            var root=new GameObject("GeneratedInteractionFixture");root.transform.SetParent(original.Context.MapContentRoot.transform,false);
            typeof(GeneratedValleyContentHandle).GetField("GeneratedRoot",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(handle,root);
            var points=layout.Points.Select(p=>{var t=templates.Points.Single(v=>v.Id==p.TemplateId);return new CivilisationSearchPointDefinition(p.Id,layout.MapId,t.TechnologyId,t.ItemId,t.Amount,t.Seconds,p.Pose.ToVector(),t.NameKey,t.RequiredWeaponItemId,t.FellTree,t.Repeatable);}).ToArray();
            typeof(GeneratedValleyContentHandle).GetProperty("Points").SetValue(handle,points);
            tree=points.First(p=>p.FellTree);water=points.First(p=>p.Repeatable);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.CivilisationValley,handle);
            typeof(ExpeditionModel).GetMethod("InstallValleyLayout",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Game.Interface.GetModel<ExpeditionModel>(),new object[]{layout});
            // 此夹具在生成布局后发布新快照，顺序与正式新河谷进入流程一致。
            var save=Game.Interface.GetSystem<SaveSystem>();Assert.That(save.DeleteExpeditionFiles(out var initialError),Is.True,initialError);
            var expedition=Game.Interface.GetModel<ExpeditionModel>();var initial=expedition.CaptureSave(expedition.CurrentExpeditionId,DateTime.UtcNow);
            Assert.That(save.CaptureExpeditionSections(initial,out initialError),Is.True,initialError);Assert.That(save.SaveExpedition(initial,out initialError),Is.True,initialError);
            Game.Interface.GetModel<SaveModel>().SetExpedition(initial);
            Move(tree.Position);Equip();
        }
        [TearDown] public void TearDown(){if(handle!=null){handle.ReleaseGeneratedAsync().GetAwaiter().GetResult();UnityEngine.Object.DestroyImmediate(handle.Original.Context.gameObject);}support?.TearDown();}
        private static void Move(Vector3 position)=>Game.Interface.GetSystem<UnitSystem>().TryUpdateTransform(1,position,Quaternion.identity);
        private static void Equip(int item=17)=>CivilisationHarvestTests.Equip(item);
        private bool Begin(CivilisationSearchPointDefinition point,out string error)=>Game.Interface.GetSystem<CivilisationSearchSystem>().TryBeginSearch(1,point.Id,out error);
        private static void Complete()=>Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(3.1f);
        [Test] public void ElevatedTree_UsesThreeDimensionalDistance()
        {Assert.That(Begin(tree,out var e),Is.True,e);Game.Interface.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.Replaced);Move(tree.Position-Vector3.up*10);Assert.That(Begin(tree,out _),Is.False);}
        [Test] public void EquippedTool_GrantsExistingTemplateAmount()
        {Equip(18);Assert.That(Begin(tree,out _),Is.False);Equip();Assert.That(Begin(tree,out var e),Is.True,e);Complete();Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(9),Is.EqualTo(4));}
        [Test] public void HarvestedPoint_StaysVisitedAfterSaveRestore()
        {Assert.That(Begin(tree,out var e),Is.True,e);Complete();var model=Game.Interface.GetModel<CivilisationExpeditionModel>();var state=model.Capture();model.Reset();model.Restore(state);Assert.That(Begin(tree,out _),Is.False);}
        [Test] public void Water_RemainsRepeatableAfterSaveRestore()
        {Move(water.Position);Assert.That(Begin(water,out var e),Is.True,e);Complete();var model=Game.Interface.GetModel<CivilisationExpeditionModel>();model.Restore(model.Capture());Assert.That(Begin(water,out e),Is.True,e);Complete();Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(8),Is.EqualTo(2));}
        [Test] public void Resolver_DoesNotFallBackToStaticPointForGeneratedMap()
        {var type=typeof(ExpeditionModel).Assembly.GetType("Game.CivilisationMapPointResolver");Assert.That(type,Is.Not.Null);var args=new object[]{handle,templates,"tree.valley.5",null};Assert.That((bool)type.GetMethod("TryGetPoint").Invoke(null,args),Is.False);}
        [Test] public void PointIndexOwnsSnapshotAndRejectsReleasedMap()
        {
            var source = handle.Points.ToArray();
            typeof(GeneratedValleyContentHandle).GetProperty("Points").SetValue(handle, source);
            source[0] = null;
            Assert.That(CivilisationMapPointResolver.TryGetPoint(handle, templates, tree.Id, out var indexed), Is.True);
            Assert.That(indexed, Is.SameAs(tree)); Assert.That(handle.Points[0], Is.Not.Null);
            handle.ReleaseGeneratedAsync().GetAwaiter().GetResult();
            Assert.That(CivilisationMapPointResolver.TryGetPoint(handle, templates, tree.Id, out _), Is.False);
        }
        [Test] public void DuplicatePointIndexInstallationKeepsPreviousSnapshot()
        {
            var error = Assert.Throws<TargetInvocationException>(() => typeof(GeneratedValleyContentHandle).GetProperty("Points").SetValue(handle, new[] { tree, tree }));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(CivilisationMapPointResolver.TryGetPoint(handle, templates, water.Id, out var indexed), Is.True);
            Assert.That(indexed, Is.SameAs(water));
        }
        [Test] public void ExpiredMapAction_DoesNotGrantResources()
        {Assert.That(Begin(tree,out var e),Is.True,e);Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base,null);Complete();Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(9),Is.Zero);}
        [Test] public void FullBag_DoesNotVisitGeneratedTree()
        {var bag=Game.Interface.GetModel<PlayerBagModel>().Container;bag.TryAdd(5,bag.Capacity,1,out _,out _);Assert.That(Begin(tree,out var e),Is.True,e);Complete();Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited(tree.Id),Is.False);}
        [Test] public void FailedSave_RollsBackGeneratedHarvest()
        {
            var root=Path.Combine(Path.GetTempPath(),"ValleyInteractionFail",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var blocked=Path.Combine(root,"blocked");File.WriteAllText(blocked,"block");
            try{Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(Path.Combine(blocked,"child"));Assert.That(Begin(tree,out var e),Is.True,e);Complete();Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited(tree.Id),Is.False);Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(9),Is.Zero);}finally{Directory.Delete(root,true);}
        }
        private object Exploration()
        {var type=typeof(ExpeditionModel).Assembly.GetType("Game.CivilisationExplorationSystem");Assert.That(type,Is.Not.Null);var value=Activator.CreateInstance(type);((Game)Game.Interface).RegisterSystem((ISystem)value);return value;}
        [Test] public void ReadingClue_PersistsStableClueIdentity()
        {var system=Exploration();var clue=layout.Clues[0];Move(clue.Pose.ToVector());var args=new object[]{1L,clue.Id,null,null};Assert.That((bool)system.GetType().GetMethod("TryReadClue").Invoke(system,args),Is.True,args[3]?.ToString());Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().Capture()["clues"].Values<string>(),Does.Contain(clue.Id));Assert.That(Game.Interface.GetSystem<SaveSystem>().InspectExpedition().Data.Sections[CivilisationExpeditionModel.Section]["records"].Values<string>(),Does.Contain(clue.MessageKey+".record"));}
        [Test] public void ReadingKnownClue_CanRepeatWithoutReaward()
        {var system=Exploration();var clue=layout.Clues[0];Move(clue.Pose.ToVector());for(int i=0;i<2;i++){var args=new object[]{1L,clue.Id,null,null};Assert.That((bool)system.GetType().GetMethod("TryReadClue").Invoke(system,args),Is.True,args[3]?.ToString());}Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().Capture()["clues"].Count(),Is.EqualTo(1));Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().Records.Length,Is.EqualTo(1));}
        [Test] public void ShortcutState_RoundTripsAndLegacySectionMigrates()
        {var model=Game.Interface.GetModel<CivilisationExpeditionModel>();var legacy=model.Capture();var state=(JObject)legacy.DeepClone();state["schemaVersion"]=2;state["clues"]=new JArray();state["shortcuts"]=new JArray(layout.ShortcutId);Assert.That(model.Validate(state,out var e),Is.True,e);model.Restore(state);Assert.That(model.Capture()["shortcuts"].Values<string>(),Does.Contain(layout.ShortcutId));}
        [Test] public void DestroyMode_CancelsAndBlocksGeneratedHarvest()
        {Assert.That(Begin(tree,out var e),Is.True,e);Assert.That(Game.Interface.GetSystem<TemporaryBuildingSystem>().BeginDestroyMode(out e),Is.True,e);Assert.That(Begin(tree,out _),Is.False);Complete();Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(9),Is.Zero);}
        [Test] public void ShortcutDiscovery_SavesOnlyAfterNavigationOpen()
        {
            var builder=new ShortcutBuilder();((Game)Game.Interface).RegisterUtility<IValleyMapBuilder>(builder);var system=(CivilisationExplorationSystem)Exploration();Move(layout.Camp.ToVector());
            var result=system.DiscoverReturnShortcutAsync(1,layout.ShortcutId).GetAwaiter().GetResult();Assert.That(result.Success,Is.True,result.Error);Assert.That(builder.Open,Is.True);Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasShortcut(layout.ShortcutId),Is.True);
            Assert.That(system.DiscoverReturnShortcutAsync(1,layout.ShortcutId).GetAwaiter().GetResult().Success,Is.True);Assert.That(builder.Calls,Is.EqualTo(1));
        }
        [Test] public void ShortcutSaveFailure_ClosesNavigationAndRestoresState()
        {
            var builder=new ShortcutBuilder();((Game)Game.Interface).RegisterUtility<IValleyMapBuilder>(builder);var system=(CivilisationExplorationSystem)Exploration();Move(layout.Camp.ToVector());
            var folder=Path.Combine(Path.GetTempPath(),"ValleyShortcutFail",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var file=Path.Combine(folder,"blocked");File.WriteAllText(file,"block");
            try{Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(Path.Combine(file,"child"));var result=system.DiscoverReturnShortcutAsync(1,layout.ShortcutId).GetAwaiter().GetResult();Assert.That(result.Success,Is.False);Assert.That(builder.Open,Is.False);Assert.That(builder.Calls,Is.EqualTo(2));Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasShortcut(layout.ShortcutId),Is.False);}
            finally{Directory.Delete(folder,true);}
        }
        [Test] public void ShortcutLateCompletion_DoesNotRestoreSnapshotIntoNewMap()
        {
            var builder=new ShortcutBuilder();((Game)Game.Interface).RegisterUtility<IValleyMapBuilder>(builder);var system=(CivilisationExplorationSystem)Exploration();Move(layout.Camp.ToVector());
            builder.AfterOpen=()=>{Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base,null);Game.Interface.GetModel<CivilisationExpeditionModel>().Reset();};
            var result=system.DiscoverReturnShortcutAsync(1,layout.ShortcutId).GetAwaiter().GetResult();Assert.That(result.Success,Is.False);Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId,Is.EqualTo(MapIds.Base));Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasShortcut(layout.ShortcutId),Is.False);Assert.That(builder.Open,Is.False);
        }
        private sealed class ShortcutBuilder:IValleyMapBuilder
        {
            public bool Open;public int Calls;public Action AfterOpen;public bool ThrowOnClose;
            public Cysharp.Threading.Tasks.UniTask<MapContentLoadResult> BuildAsync(IMapContentHandle h,ValleyLayoutSaveData l,ValleyGenerationProfile p,System.Threading.CancellationToken t)=>Cysharp.Threading.Tasks.UniTask.FromResult(MapContentLoadResult.Fail("unused"));
            public Cysharp.Threading.Tasks.UniTask<MapControllerResult> SetShortcutOpenAsync(GeneratedValleyContentHandle h,string id,bool open,System.Threading.CancellationToken t)
            {if(!open&&ThrowOnClose)throw new InvalidOperationException("injected close failure");Open=open;Calls++;if(open)AfterOpen?.Invoke();return Cysharp.Threading.Tasks.UniTask.FromResult(MapControllerResult.Ok());}
        }
        [Test] public void ShortcutRollbackException_PausesCurrentMapAndReportsFailure()
        {
            ((Game)Game.Interface).RegisterSystem(new AppStateSystem());var builder=new ShortcutBuilder{ThrowOnClose=true};((Game)Game.Interface).RegisterUtility<IValleyMapBuilder>(builder);var system=(CivilisationExplorationSystem)Exploration();Move(layout.Camp.ToVector());
            var folder=Path.Combine(Path.GetTempPath(),"ValleyRollbackFail",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var file=Path.Combine(folder,"blocked");File.WriteAllText(file,"block");
            try{Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(Path.Combine(file,"child"));var result=system.DiscoverReturnShortcutAsync(1,layout.ShortcutId).GetAwaiter().GetResult();Assert.That(result.Success,Is.False);Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value,Is.EqualTo(AppState.Paused));}
            finally{Directory.Delete(folder,true);}
        }
        private sealed class EmptyLoader:IResourceLoader
        {public T LoadAsset<T>(string p)where T:UnityEngine.Object=>null;public ResourceLoadHandle LoadAssetAsync<T>(string p,Action<T> c)where T:UnityEngine.Object=>ResourceLoadHandle.Empty;public void Release(ResourceLoadHandle h){} }
    }
}
