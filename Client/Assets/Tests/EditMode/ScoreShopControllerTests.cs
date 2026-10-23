using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>使用正式Prefab和商品目录，验证显示生命周期及真实积分事务。</summary>
    public sealed class ScoreShopControllerTests
    {
        private Game architecture;private ArcadeScoreShopUICtrl ctrl;private ArcadeScoreShopUI ui;private string folder;private IResourceLoader previousLoader;
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            TestReset.ResetArchitecture();architecture=new Game();
            typeof(Architecture<Game>).GetField("mArchitecture",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,architecture);
            typeof(Architecture<Game>).GetField("mInited",Hidden).SetValue(architecture,true);
            architecture.RegisterModel(new ArcadeRunModel());
            previousLoader=ResourceLoader.Current;ResourceLoader.Use(new LocalizationLoader());LocalizationLanguages.ResetForTests();
            architecture.RegisterModel(new LocalizationModel());architecture.RegisterUtility(new LocalizationUtility());
            architecture.RegisterModel(new OnlineAccountModel());architecture.RegisterModel(new OnlineArcadeRunModel());architecture.RegisterSystem(new OnlineArcadeRunSystem());
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            object[] args={tables,null};typeof(LubanRuntimeConfigProvider).GetMethod("BuildArcadeSnapshot",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
            architecture.RegisterUtility<IRuntimeConfigProvider>(new CatalogProvider((ArcadeConfigCatalogDefinition)args[1]));
            architecture.RegisterSystem(new ArcadeRunSystem());folder=Path.Combine(Path.GetTempPath(),"ScoreShopUI",Guid.NewGuid().ToString("N"));
            var runSystem=architecture.GetSystem<ArcadeRunSystem>();runSystem.ConfigureForTests(folder);
            Assert.That(runSystem.TryStartConfiguredRun(out var run,out var error),Is.True,error);
            run.NextArenaNumber=2;run.CompletedArenaCount=1;run.ArenaConfigNumber=21;run.Checkpoint=new ArcadeCheckpointSaveData{ArenaNumber=2,GenerationSeed=123};
            run.Score=10000;run.ScoreShop=ArcadeScoreShopRules.Open(((ArcadeConfigCatalogDefinition)args[1]).ScoreShop,2,123);
            architecture.GetModel<ArcadeRunModel>().CommitSafeNode(run);
            ui=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeScoreShop.prefab")).GetComponent<ArcadeScoreShopUI>();
            ctrl=new ArcadeScoreShopUICtrl();typeof(BaseUICtrl).GetField("BaseUI",Hidden).SetValue(ctrl,ui);
            typeof(BaseUICtrl).GetField("_isActive",Hidden).SetValue(ctrl,true);Invoke("OnInit");Invoke("OnShow");
        }
        [TearDown] public void Cleanup()
        {
            if(ui!=null){Invoke("OnHide");UnityEngine.Object.DestroyImmediate(ui.gameObject);}architecture?.Deinit();TestReset.ResetArchitecture();
            ResourceLoader.Use(previousLoader);LocalizationLanguages.ResetForTests();
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
        private void Invoke(string name)=>typeof(ArcadeScoreShopUICtrl).GetMethod(name,Hidden).Invoke(ctrl,null);
        [Test] public void ShowsThreePersonalOffers_AndUnchangedScoreWhenReopened()
        {
            Assert.That(ui.OfferList.GetDraggableItems().Count,Is.EqualTo(3));
            Assert.That(ui.TextScore.text,Is.EqualTo("10,000"));
            Invoke("OnHide");Invoke("OnShow");
            Assert.That(architecture.GetModel<ArcadeRunModel>().Current.Score,Is.EqualTo(10000));
            Assert.That(ui.OfferList.GetDraggableItems().Count,Is.EqualTo(3));
        }
        [Test] public void RefreshAfterReopen_ChargesOnceAndSkipKeepsRemainingScore()
        {
            Invoke("OnHide");Invoke("OnShow");ui.BtnRefresh.onClick.Invoke();
            var run=architecture.GetModel<ArcadeRunModel>().Current;Assert.That(run.Score,Is.EqualTo(9500));Assert.That(run.ScoreShop.Revision,Is.EqualTo(2));
            ui.BtnSkip.onClick.Invoke();run=architecture.GetModel<ArcadeRunModel>().Current;
            Assert.That(run.Score,Is.EqualTo(9500));Assert.That(run.ScoreShop.Completed,Is.True);Assert.That(ctrl.IsShow(),Is.False);
        }
        [Test] public void BuyButton_ChargesOriginalOfferOnceAndEndsVisit()
        {
            var card=ui.OfferList.GetDraggableItems().First().GetComponent<ArcadeScoreShopOfferBoxUI>();
            card.BtnBuy.onClick.Invoke();var run=architecture.GetModel<ArcadeRunModel>().Current;
            Assert.That(run.Score,Is.LessThan(10000));Assert.That(run.ScoreShop.PurchasedDropId,Is.Not.Null);Assert.That(run.ScoreShop.Completed,Is.True);
            long score=run.Score;card.BtnBuy.onClick.Invoke();Assert.That(architecture.GetModel<ArcadeRunModel>().Current.Score,Is.EqualTo(score));
        }
        [Test] public void LowScore_DisablesSpendingAndKeepsSkipAvailable()
        {
            var run=architecture.GetModel<ArcadeRunModel>().Current;run.Score=100;
            architecture.GetModel<ArcadeRunModel>().CommitSafeNode(run);Invoke("OnShow");
            Assert.That(ui.BtnRefresh.interactable,Is.False);Assert.That(ui.BtnSkip.interactable,Is.True);
            Assert.That(ui.OfferList.GetDraggableItems().All(t=>!t.GetComponent<ArcadeScoreShopOfferBoxUI>().BtnBuy.interactable),Is.True);
            ui.BtnSkip.onClick.Invoke();Assert.That(architecture.GetModel<ArcadeRunModel>().Current.Score,Is.EqualTo(100));
        }
        /// <summary>仅此UI单元使用正式目录，不把无关主题整表校验作为按钮事务的前置条件。</summary>
        private sealed class CatalogProvider:IRuntimeConfigProvider
        {
            private readonly ArcadeConfigCatalogDefinition catalog;public CatalogProvider(ArcadeConfigCatalogDefinition value){catalog=value;}
            public bool TryGetArcadeCatalog(out ArcadeConfigCatalogDefinition value,out string error){value=catalog;error=null;return true;}
            public bool TryGetMap(string id,out MapAssetDefinition value,out string error){value=default;error=null;return false;}
            public bool TryGetMapSpawns(string id,out IReadOnlyList<MapSpawnDefinition> value,out string error){value=Array.Empty<MapSpawnDefinition>();error=null;return false;}
            public bool TryGetPlayer(string id,out PlayerConfigDefinition value,out string error){value=default;error=null;return false;}
            public bool TryGetEnemy(string id,out EnemyConfigDefinition value,out string error){value=default;error=null;return false;}
            public bool TryGetLoot(string id,out LootConfigDefinition value,out string error){value=default;error=null;return false;}
        }
        private sealed class LocalizationLoader:IResourceLoader
        {
            public T LoadAsset<T>(string path) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/"+Path.GetFileName(path));
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> loaded) where T:UnityEngine.Object
            {loaded?.Invoke(LoadAsset<T>(path));return ResourceLoadHandle.Empty;}
            public void Release(ResourceLoadHandle handle){}
        }
    }
}
