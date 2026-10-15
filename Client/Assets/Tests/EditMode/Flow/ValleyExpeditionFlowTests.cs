using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class ValleyExpeditionFlowTests
    {
        private string directory;
        private FakeMapContentLoader loader;
        private StubBuilder builder;
        private ValleyGenerationProfile profile;
        private CivilisationCatalog templates;
        [SetUp] public void SetUp()
        {
            directory=Path.Combine(Path.GetTempPath(),"AnotherDayValleyFlow",Guid.NewGuid().ToString("N"));
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            var config=new TestRuntimeConfigProvider(includeMapPlayerSpawn:true);
            TemporaryBuildingBehaviorTestSupport.InstallArchitecture(config,new FakeTemporaryBuildingViewPort(),new FakeProjectilePort());
            var real=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/"+n+".bytes").bytes)));
            Assert.That(real.TryGetValleyGenerationProfile(MapIds.CivilisationValley,out profile,out var profileError),Is.True,profileError);Assert.That(real.TryGetCivilisationCatalog(out templates,out var catalogError),Is.True,catalogError);
            var game=(Game)Game.Interface;game.RegisterUtility<IRuntimeConfigProvider>(new Config(config,real));
            game.RegisterModel(new CurrencyModel());game.RegisterModel(new BasePreparationModel());game.RegisterModel(new ShopUnlockModel());game.RegisterModel(new ClothingEquipmentModel());
            game.RegisterModel(new ExtractionModel());game.RegisterSystem(new ExtractionSystem());game.RegisterSystem(new CarryLoadSystem());
            game.RegisterSystem(new ClothingEquipmentSystem());
            game.RegisterModel(new ExpeditionPreparationModel());
            game.RegisterModel(new MapExplorationModel());game.RegisterSystem(new MapDisplaySystem());
            game.RegisterModel(new CivilisationTechnologyModel());
            foreach(var section in new IScopedSaveSectionProvider[]{game.GetModel<CurrencyModel>(),game.GetModel<BasePreparationModel>(),game.GetModel<ShopUnlockModel>(),game.GetModel<ExpeditionPreparationModel>()})
                Assert.That(game.GetSystem<SaveSystem>().RegisterSection(section,out var sectionError),Is.True,sectionError);
            game.RegisterModel(new ExpeditionTaskModel());game.RegisterModel(new CivilisationExpeditionModel());
            Assert.That(game.GetSystem<SaveSystem>().RegisterSection(game.GetModel<MapExplorationModel>(),out _),Is.True);
            Assert.That(game.GetSystem<SaveSystem>().RegisterSection(game.GetModel<CivilisationTechnologyModel>(),out _),Is.True);
            Assert.That(game.GetSystem<SaveSystem>().RegisterSection(game.GetModel<CivilisationExpeditionModel>(),out _),Is.True);
            loader=new FakeMapContentLoader();TemporaryBuildingBehaviorTestSupport.RegisterMapSystem(loader);
            builder=new StubBuilder();game.RegisterUtility<IValleyMapBuilder>(builder);
            game.RegisterSystem(new ExpeditionTaskSystem());game.RegisterSystem(new ExpeditionSystem());game.RegisterSystem(new ExpeditionFlowSystem());
            new GameObject("Main Camera",typeof(Camera)).tag="MainCamera";
            TemporaryBuildingBehaviorTestSupport.CreateUiRoot();
            config.TryGetPlayer("player",out var playerConfig,out _);game.GetModel<CombatModel>().ApplyPlayerConfig(playerConfig,false);game.GetModel<CombatModel>().SetPlayerHP(100);
            var save=game.GetSystem<SaveSystem>();save.ConfigureForTests(directory);
            Assert.That(save.CreateBase(out var data,out var error),Is.True,"CreateBase: "+error);
            Assert.That(save.CommitBaseRuntime(data,out error),Is.True,"CommitBaseRuntime: "+error);
            var baseLoad=game.GetSystem<MapSceneSystem>().LoadMapAsync(MapIds.Base).GetAwaiter().GetResult();Assert.That(baseLoad.Success,Is.True,baseLoad.Error);
        }
        [TearDown] public void TearDown(){TemporaryBuildingBehaviorTestSupport.Reset();if(Directory.Exists(directory))Directory.Delete(directory,true);}
        private ExpeditionFlowResult Enter()=>Game.Interface.GetSystem<ExpeditionFlowSystem>().EnterExpeditionAsync(MapIds.CivilisationValley).GetAwaiter().GetResult();
        [Test] public void PendingValleyCannotSettleWithoutLoadingEntity()
        {
            Assert.That(Enter().Success,Is.True);
            var flow=Game.Interface.GetSystem<ExpeditionFlowSystem>();
            Assert.That(flow.SettlePendingWithoutLoadingAsync().GetAwaiter().GetResult().Success,Is.False);
            Assert.That(Game.Interface.GetSystem<SaveSystem>().InspectExpedition().IsValid,Is.True);
        }
        [Test] public void ShoreAndWaterResumeKeepPlayerPoseSwimmingAndHarvestState()
        {
            ((Game)Game.Interface).RegisterSystem(new PlayerMoveSystem());
            Assert.That(Enter().Success,Is.True);
            var save=Game.Interface.GetSystem<SaveSystem>();var saved=save.InspectExpedition().Data;
            var layoutJson=Newtonsoft.Json.JsonConvert.SerializeObject(saved.ValleyLayout);
            var harvested=saved.ValleyLayout.Points.First(p=>p.TemplateId=="tree.valley.5").Id;
            saved.Sections[CivilisationExpeditionModel.Section]["visited"]=new Newtonsoft.Json.Linq.JArray(harvested);
            foreach(var inWater in new[]{false,true})
            {
                var l=saved.ValleyLayout;
                var position=inWater?l.River.First(p=>l.HeightAt(p.X,p.Z)<p.Y-2 && Mathf.Max(Mathf.Abs(p.X),Mathf.Abs(p.Z))<440).ToVector():l.Route[3].ToVector();
                position.y=inWater?position.y-ValleySwimmingRules.SurfaceFootDepth:position.y+.1f;
                saved.World.Players[0].Pose=new ExpeditionPoseData{X=position.x,Y=position.y,Z=position.z,RotationY=73};
                saved.World.Players[0].IsSwimming=inWater;
                Assert.That(save.SaveExpedition(saved,out var error),Is.True,error);
                var result=Game.Interface.GetSystem<ExpeditionFlowSystem>().ResumePendingExpeditionAsync().GetAwaiter().GetResult();
                Assert.That(result.Success,Is.True,result.Error);
                var world=new ExpeditionWorldSaveData();Game.Interface.GetSystem<MapSceneSystem>().CaptureExpeditionWorld(world);
                Assert.That(Vector3.Distance(new Vector3(world.Players[0].Pose.X,world.Players[0].Pose.Y,world.Players[0].Pose.Z),position),Is.LessThan(.001f));
                Assert.That(world.Players[0].IsSwimming,Is.EqualTo(inWater));
                Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(builder.LastLayout),Is.EqualTo(layoutJson));
                Assert.That(save.InspectExpedition().Data.Sections[CivilisationExpeditionModel.Section]["visited"].Values<string>(),Does.Contain(harvested));
            }
        }
        [Test] public void NewValleyHasOneAlwaysOpenExitAndRejectsDirectReturn()
        {
            Assert.That(Enter().Success,Is.True);
            var exits=Game.Interface.GetModel<ExtractionModel>().Extractions.Values.ToArray();
            Assert.That(exits.Length,Is.EqualTo(1));Assert.That(exits[0].Available,Is.True);
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().CanReturn,Is.True);
            Assert.That(Game.Interface.GetSystem<ExpeditionFlowSystem>().ReturnToBaseAsync().GetAwaiter().GetResult().Success,Is.False);
            Assert.That(Game.Interface.GetSystem<ExtractionSystem>().TryReturnToBaseAsync(exits[0].UnitId,1).GetAwaiter().GetResult().Success,Is.False);
        }
        [Test] public void ExitDiscoveryPersistsWithoutCompletingExpedition()
        {
            Assert.That(Enter().Success,Is.True);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(47,1);
            var display=Game.Interface.GetSystem<MapDisplaySystem>();var request=new MapOpenRequest(47,MapIds.CivilisationValley);
            Assert.That(display.TryBuildSnapshot(request,out var before,out _,out var error),Is.True,error);Assert.That(before.Extractions.Count,Is.Zero);
            var layout=Game.Interface.GetModel<ExpeditionModel>().ValleyLayout;var fog=Game.Interface.GetModel<MapExplorationModel>();
            Assert.That(realDisplay(out var definition),Is.True);
            fog.Reveal(definition,layout.Extraction.ToVector(),24);
            var save=Game.Interface.GetSystem<SaveSystem>();save.MarkDirty("test exit discovered");Assert.That(save.Flush(out error),Is.True,error);
            fog.Reset();fog.Restore(save.InspectExpedition().Data.Sections[fog.SectionId]);
            Assert.That(display.TryBuildSnapshot(request,out var after,out _,out error),Is.True,error);Assert.That(after.Extractions.Count,Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().Status,Is.EqualTo(ExpeditionStatus.ReturnQualified));
            bool realDisplay(out MapDisplayDefinition value)=>Game.Interface.GetUtility<IRuntimeConfigProvider>().TryGetMapDisplay(MapIds.CivilisationValley,out value,out _);
        }
        [Test] public void ExitCountdownRequiresPresenceAndCancelsOnLeaving()
        {
            Assert.That(Enter().Success,Is.True);var exit=Game.Interface.GetModel<ExtractionModel>().Extractions.Values.Single();
            var system=Game.Interface.GetSystem<ExtractionSystem>();var actions=Game.Interface.GetSystem<PlayerActionSystem>();
            Assert.That(system.TryBeginExtraction(exit.UnitId,1,out _,out _),Is.False);
            Assert.That(system.TrySetPlayerInRange(exit.UnitId,1,true,out _),Is.True);
            Assert.That(system.TryBeginExtraction(exit.UnitId,1,out _,out var error),Is.True,error);
            Assert.That(system.TryBeginExtraction(exit.UnitId,1,out _,out _),Is.False);
            actions.TickForTests(4.99f);Assert.That(actions.TryGetSnapshot(out var action),Is.True);Assert.That(action.Kind,Is.EqualTo(PlayerActionKind.Extraction));
            system.TrySetPlayerInRange(exit.UnitId,1,false,out _);Assert.That(actions.TryGetSnapshot(out _),Is.False);
        }
        [Test] public void CompletedCountdownAuthorizesReturnAndWriteFailureKeepsTrip()
        {
            Assert.That(Enter().Success,Is.True);var exit=Game.Interface.GetModel<ExtractionModel>().Extractions.Values.Single();
            var system=Game.Interface.GetSystem<ExtractionSystem>();var actions=Game.Interface.GetSystem<PlayerActionSystem>();
            system.TrySetPlayerInRange(exit.UnitId,1,true,out _);Assert.That(system.TryBeginExtraction(exit.UnitId,1,out _,out _),Is.True);
            var started=0;var unregister=Game.Interface.RegisterEvent<ExtractionReturnStartedEvent>(_=>started++);
            try
            {
                actions.TickForTests(4.99f);Assert.That(started,Is.Zero);
                var store=(SaveFileStore)typeof(SaveSystem).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Game.Interface.GetSystem<SaveSystem>());
                var incremental=typeof(SaveFileStore).GetField("incremental",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(store);
                incremental.GetType().GetField("FailNextAppendStageForTests",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(incremental,"payload");
                LogAssert.Expect(LogType.Error,new System.Text.RegularExpressions.Regex("\\[ExtractionSystem\\] 返回基地失败"));
                actions.TickForTests(.02f);Assert.That(started,Is.EqualTo(1));
                Assert.That(Game.Interface.GetSystem<SaveSystem>().InspectExpedition().IsValid,Is.True);
                Assert.That(Game.Interface.GetModel<ExpeditionModel>().CanReturn,Is.True);
            }
            finally{unregister.UnRegister();}
        }
        [Test] public void LoadingStages_ReportTransactionAndRestartLayoutOnRetry()
        {
            var stages=new List<MapLoadingStage>();
            var subscription=Game.Interface.RegisterEvent<MapLoadingStageEvent>(e=>{if(e.MapId==MapIds.CivilisationValley)stages.Add(e.Stage);});
            builder.FailUntil=2;
            try
            {
                var result=Enter();Assert.That(result.Success,Is.True,result.Error);
                Assert.That(stages,Is.EqualTo(new[]{MapLoadingStage.Scene,MapLoadingStage.Layout,MapLoadingStage.Layout,
                    MapLoadingStage.Layout,MapLoadingStage.Players,MapLoadingStage.ReleasePrevious,MapLoadingStage.Activate}));
            }
            finally{subscription.UnRegister();}
        }
        [Test] public void LoadingProgress_ReachesCompletionOnlyAfterValleyPreparation()
        {
            var progress=new List<float>();
            var subscription=Game.Interface.RegisterEvent<MapLoadingProgressEvent>(e=>{if(e.MapId==MapIds.CivilisationValley)progress.Add(e.Progress);});
            float duringBuild=-1;builder.AfterBuild=()=>duringBuild=progress[progress.Count-1];
            try
            {
                var result=Enter();Assert.That(result.Success,Is.True,result.Error);
                Assert.That(duringBuild,Is.InRange(0f,.99f),"河谷准备期间不能显示100%");
                Assert.That(progress[progress.Count-1],Is.EqualTo(1f));
                Assert.That(progress.GetRange(0,progress.Count-1),Has.All.LessThan(1f));
                for(int i=1;i<progress.Count;i++)Assert.That(progress[i],Is.GreaterThanOrEqualTo(progress[i-1]));
            }
            finally{subscription.UnRegister();}
        }
        [Test] public void NewValley_UsesGenerationBeforeCreatingSave()
        {var r=Enter();Assert.That(r.Success,Is.True,r.Error);Assert.That(builder.Calls,Is.EqualTo(1));Assert.That(Game.Interface.GetSystem<SaveSystem>().InspectExpedition().Data.ValleyLayout,Is.Not.Null);}
        [Test] public void OrdinaryMap_DoesNotInvokeValleyBuilder()
        {var r=Game.Interface.GetSystem<ExpeditionFlowSystem>().EnterExpeditionAsync(MapIds.SurvivalSample).GetAwaiter().GetResult();Assert.That(r.Success,Is.True,r.Error);Assert.That(builder.Calls,Is.Zero);}
        [Test] public void LegacySnapshot_KeepsStaticMapBranch()
        {Assert.That(typeof(MapSceneSystem).GetMethod("LoadNewValleyMapAsync"),Is.Not.Null);var r=Game.Interface.GetSystem<MapSceneSystem>().LoadMapAsync(MapIds.CivilisationValley).GetAwaiter().GetResult();Assert.That(r.Success,Is.True,r.Error);Assert.That(builder.Calls,Is.Zero);}
        [Test] public void NewSnapshot_UsesSavedLayoutWithoutChangingSeed()
        {Assert.That(Enter().Success,Is.True);var saved=Game.Interface.GetSystem<SaveSystem>().InspectExpedition().Data;builder.LastLayout=null;var r=Game.Interface.GetSystem<MapSceneSystem>().LoadMapAsync(MapIds.CivilisationValley,saved).GetAwaiter().GetResult();Assert.That(r.Success,Is.True,r.Error);Assert.That(builder.LastLayout.Seed,Is.EqualTo(saved.ValleyLayout.Seed));Assert.That(builder.LastLayout.AttemptIndex,Is.EqualTo(saved.ValleyLayout.AttemptIndex));}
        [Test] public void SavedShortcut_IsOpenedBeforeRestoredMapCommit()
        {
            Assert.That(Enter().Success,Is.True);var saved=Game.Interface.GetSystem<SaveSystem>().InspectExpedition().Data;
            var state=CivilisationExpeditionModel.Empty();((Newtonsoft.Json.Linq.JArray)state["shortcuts"]).Add(saved.ValleyLayout.ShortcutId);saved.Sections[CivilisationExpeditionModel.Section]=state;
            var r=Game.Interface.GetSystem<MapSceneSystem>().LoadMapAsync(MapIds.CivilisationValley,saved).GetAwaiter().GetResult();Assert.That(r.Success,Is.True,r.Error);Assert.That(builder.OpenCalls,Is.EqualTo(1));
        }
        [Test] public void InvalidExploration_RejectsBeforeBuildingCandidate()
        {
            Assert.That(Enter().Success,Is.True);var saved=Game.Interface.GetSystem<SaveSystem>().InspectExpedition().Data;var state=CivilisationExpeditionModel.Empty();state["visited"]=new Newtonsoft.Json.Linq.JArray("foreign.id");saved.Sections[CivilisationExpeditionModel.Section]=state;
            var before=builder.Calls;var old=Game.Interface.GetModel<MapSceneModel>().CurrentHandle;
            var result=Game.Interface.GetSystem<MapSceneSystem>().LoadMapAsync(MapIds.CivilisationValley,saved).GetAwaiter().GetResult();Assert.That(result.Success,Is.False);Assert.That(builder.Calls,Is.EqualTo(before));Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle,Is.SameAs(old));
        }
        [Test] public void EightFailedCandidates_LeaveBaseAndRiskSafe()
        {builder.Fail=true;var old=Game.Interface.GetModel<MapSceneModel>().CurrentHandle;var r=Enter();Assert.That(r.Success,Is.False);Assert.That(builder.Calls,Is.EqualTo(8));Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle,Is.SameAs(old));Assert.That(Game.Interface.GetModel<PlayerBagModel>().RiskState,Is.EqualTo(PlayerBagRiskState.BaseSafe));}
        [Test] public void RetryCandidates_StillStartsAtZeroElapsed()
        {builder.FailUntil=2;var r=Enter();Assert.That(r.Success,Is.True,r.Error);Assert.That(builder.Calls,Is.EqualTo(3));Assert.That(Game.Interface.GetModel<ExpeditionModel>().ElapsedGameMinutes,Is.Zero);Assert.That(Game.Interface.GetModel<ExpeditionModel>().StartMinuteOfDay,Is.InRange(480,720));}
        [Test] public void FileWriteFailure_DoesNotLeaveActiveUnsavedExpedition()
        {builder.AfterBuild=()=>{Directory.CreateDirectory(directory);Directory.CreateDirectory(new SaveFileStore(directory).ExpeditionTempPath);};var r=Enter();Assert.That(r.Success,Is.False);Assert.That(Game.Interface.GetModel<ExpeditionModel>().IsActive,Is.False);}
        [Test] public void LoaderFailure_DoesNotCreateLayoutOrCandidateFile()
        {loader.FailNextLoad=true;var r=Enter();Assert.That(r.Success,Is.False);Assert.That(Game.Interface.GetModel<ExpeditionModel>().ValleyLayout,Is.Null);Assert.That(Game.Interface.GetSystem<SaveSystem>().InspectExpedition().Status,Is.EqualTo(SaveFileStatus.Missing));}
        [Test] public void InvalidSavedLayout_RejectsBeforeReplacingCurrentMap()
        {Assert.That(Enter().Success,Is.True);var saved=Game.Interface.GetSystem<SaveSystem>().InspectExpedition().Data;var old=Game.Interface.GetModel<MapSceneModel>().CurrentHandle;saved.ValleyLayout.GenerationVersion=999;var r=Game.Interface.GetSystem<MapSceneSystem>().LoadMapAsync(MapIds.CivilisationValley,saved).GetAwaiter().GetResult();Assert.That(r.Success,Is.False);Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle,Is.SameAs(old));Assert.That(Game.Interface.GetSystem<SaveSystem>().InspectExpedition().IsValid,Is.True);}
        [Test] public void NewValleyLoadingEntry_IsExplicitAndSeparateFromLegacyLoad()
        {Assert.That(typeof(MapSceneSystem).GetMethod("LoadNewValleyMapAsync"),Is.Not.Null);}
        private sealed class StubBuilder:IValleyMapBuilder
        {
            public int Calls,FailUntil;public bool Fail;public Action AfterBuild;public ValleyLayoutSaveData LastLayout;
            public UniTask<MapContentLoadResult> BuildAsync(IMapContentHandle original,ValleyLayoutSaveData layout,ValleyGenerationProfile p,CancellationToken token)
            {
                Calls++;LastLayout=layout.Clone();if(Fail||Calls<=FailUntil)return UniTask.FromResult(MapContentLoadResult.Fail("injected candidate failure"));
                var handle=new GeneratedValleyContentHandle(original,new EmptyLoader(),layout);var root=new GameObject("StubGeneratedValley");root.transform.SetParent(original.Context.MapContentRoot.transform,false);
                typeof(GeneratedValleyContentHandle).GetField("GeneratedRoot",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(handle,root);AfterBuild?.Invoke();return UniTask.FromResult(MapContentLoadResult.Ok(handle));
            }
            public int OpenCalls;
            public UniTask<MapControllerResult> SetShortcutOpenAsync(GeneratedValleyContentHandle h,string id,bool open,CancellationToken token){if(open)OpenCalls++;return UniTask.FromResult(MapControllerResult.Ok());}
        }
        private sealed class EmptyLoader:IResourceLoader
        {public T LoadAsset<T>(string p)where T:UnityEngine.Object=>null;public ResourceLoadHandle LoadAssetAsync<T>(string p,Action<T> callback)where T:UnityEngine.Object=>ResourceLoadHandle.Empty;public void Release(ResourceLoadHandle h){} }
        private sealed class Config:IRuntimeConfigProvider
        {
            private readonly TestRuntimeConfigProvider inner;private readonly LubanRuntimeConfigProvider real;
            public Config(TestRuntimeConfigProvider inner,LubanRuntimeConfigProvider real){this.inner=inner;this.real=real;}
            public bool TryGetPreparationRule(out BasePreparationRule v,out string e)=>real.TryGetPreparationRule(out v,out e);
            public bool TryGetMapPreparation(string id,out MapPreparationRule v,out string e)=>real.TryGetMapPreparation(id,out v,out e);
            public bool TryGetItemPreparation(int id,out ItemPreparationDefinition v,out string e)=>real.TryGetItemPreparation(id,out v,out e);
            public bool TryGetShopItems(string id,out IReadOnlyList<ShopItemDefinition> v,out string e)=>real.TryGetShopItems(id,out v,out e);
            public bool TryGetMapDisplay(string id,out MapDisplayDefinition v,out string e)=>real.TryGetMapDisplay(id,out v,out e);
            public bool TryGetValleyGenerationProfile(string id,out ValleyGenerationProfile p,out string e)=>real.TryGetValleyGenerationProfile(id,out p,out e);
            public bool TryGetCivilisationCatalog(out CivilisationCatalog c,out string e)=>real.TryGetCivilisationCatalog(out c,out e);
            public bool TryGetExpeditionTasks(string id,out IReadOnlyList<ExpeditionTaskDefinition> values,out string e){values=Array.Empty<ExpeditionTaskDefinition>();e=null;return true;}
            public bool TryGetMap(string id,out MapAssetDefinition v,out string e)=>inner.TryGetMap(id,out v,out e);
            public bool TryGetMapSpawns(string id,out IReadOnlyList<MapSpawnDefinition> v,out string e)=>inner.TryGetMapSpawns(id,out v,out e);
            public bool TryGetPlayer(string id,out PlayerConfigDefinition v,out string e)=>inner.TryGetPlayer(id,out v,out e);
            public bool TryGetEnemy(string id,out EnemyConfigDefinition v,out string e)=>inner.TryGetEnemy(id,out v,out e);
            public bool TryGetLoot(string id,out LootConfigDefinition v,out string e)=>inner.TryGetLoot(id,out v,out e);
            public bool TryGetLootByItemId(int id,out LootConfigDefinition v,out string e)=>inner.TryGetLootByItemId(id,out v,out e);
            public bool TryGetItem(int id,out ItemDefinition v,out string e)=>id==47?real.TryGetItem(id,out v,out e):inner.TryGetItem(id,out v,out e);
            public bool TryGetExtraction(string id,out ExtractionConfigDefinition v,out string e)=>id=="extraction.valley.main"?real.TryGetExtraction(id,out v,out e):inner.TryGetExtraction(id,out v,out e);
            public bool TryGetProjectile(string id,out ProjectileConfigDefinition v,out string e)=>inner.TryGetProjectile(id,out v,out e);
            public bool TryGetTempBuilding(string id,out TempBuildingDefinition v,out string e)=>inner.TryGetTempBuilding(id,out v,out e);
            public bool TryGetTempBuildings(out IReadOnlyList<TempBuildingDefinition> v,out string e)=>inner.TryGetTempBuildings(out v,out e);
            public bool TryGetCraftRecipes(out IReadOnlyList<CraftRecipeDefinition> v,out string e)=>inner.TryGetCraftRecipes(out v,out e);
            public bool TryGetTempBuildingManualAttack(string id,out TempBuildingManualAttackDefinition v)=>inner.TryGetTempBuildingManualAttack(id,out v);
        }
    }
}
