using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using FirepowerLoop.Arcade;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>使用正式窗口及积分事务，验证继续意图跨商店成功接续；仅写临时存档，不进入地图。</summary>
    public sealed class ScoreShopResumeFlowTests
    {
        private Game architecture;
        private GameObject root;
        private string folder;
        private IResourceLoader previousLoader;
        private ArcadeResumeUICtrl resume;
        private ArcadeResumeUI ui;
        private DeferredPreparation preparation;
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp] public void Setup()
        {
            TestReset.ResetArchitecture();
            architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,architecture);
            typeof(Architecture<Game>).GetField("mInited",Hidden).SetValue(architecture,true);
            previousLoader=ResourceLoader.Current; ResourceLoader.Use(new LocalizationLoader()); LocalizationLanguages.ResetForTests();
            architecture.RegisterModel(new LocalizationModel()); architecture.RegisterUtility(new LocalizationUtility());
            architecture.RegisterModel(new ArcadeRunModel()); architecture.RegisterModel(new MapSceneModel());
            architecture.RegisterModel(new OnlineAccountModel()); architecture.RegisterModel(new OnlineArcadeRunModel());
            architecture.RegisterSystem(new OnlineArcadeRunSystem());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            architecture.RegisterSystem(new ArcadeRunSystem());
            folder=Path.Combine(Application.temporaryCachePath,"ScoreShopResume-"+Guid.NewGuid().ToString("N"));
            var system=architecture.GetSystem<ArcadeRunSystem>(); system.ConfigureForTests(folder);
            Assert.That(system.TryStartConfiguredRun(out var run,out var error),Is.True,error);
            Assert.That(provider.TryGetArcadeCatalog(out var catalog,out error),Is.True,error);
            run.NextArenaNumber=2; run.CompletedArenaCount=1; run.ArenaConfigNumber=21; run.Score=10000;
            run.Checkpoint=new ArcadeCheckpointSaveData{ArenaNumber=2,GenerationSeed=123};
            run.ScoreShop=ArcadeScoreShopRules.Open(catalog.ScoreShop,2,123);
            architecture.GetModel<ArcadeRunModel>().CommitSafeNode(run);
            preparation=new DeferredPreparation(); architecture.RegisterUtility<IGameplayResourcePreparation>(preparation);
            UIHelper.ResetForTests();
            UIHelper.SetPrefabLoaderForTests(path=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/"+path+".prefab"));
            root=new GameObject("UIRoot",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));
            foreach(var layer in new[]{"HUD","Dialog","Popup","Tips","Top"}) new GameObject(layer,typeof(RectTransform)).transform.SetParent(root.transform,false);
            new GameObject("EventSystem",typeof(UnityEngine.EventSystems.EventSystem),typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule)).transform.SetParent(root.transform,false);
            root.AddComponent<UIRoot>();
            resume=UIHelper.ShowUI<ArcadeResumeUICtrl>();
            ui=root.GetComponentInChildren<ArcadeResumeUI>(true);
        }

        [TearDown] public void Cleanup()
        {
            resume?.Hide(); preparation?.CompleteAll(); UIHelper.ResetForTests();
            if(root!=null) Object.DestroyImmediate(root);
            architecture?.Deinit(); TestReset.ResetArchitecture(); ResourceLoader.Use(previousLoader); LocalizationLanguages.ResetForTests();
            if(Directory.Exists(folder)) Directory.Delete(folder,true);
        }

        [Test] public void OpenProgress_DoesNotOpenShopOrDisableContinue()
        {
            Assert.That(UIHelper.GetUICtrl<ArcadeScoreShopUICtrl>()?.IsShow()==true,Is.False,"查看进度不能抢先弹出商店");
            Assert.That(ui.BtnContinue.interactable,Is.True);
        }

        [UnityTest] public IEnumerator SkippingShop_ResumesSameContinueExactlyOnce()=>FinishingShop(false);
        [UnityTest] public IEnumerator BuyingFromShop_ResumesSameContinueExactlyOnce()=>FinishingShop(true);
        private IEnumerator FinishingShop(bool buy)
        {
            ui.BtnContinue.onClick.Invoke();
            Assert.That(UIHelper.GetUICtrl<ArcadeScoreShopUICtrl>().IsShow(),Is.True);
            Assert.That(preparation.Requests.Count,Is.Zero);
            CompleteShop(buy);
            yield return null;
            Assert.That(preparation.Requests.Count,Is.EqualTo(1),"购买或跳过成功后必须自动接回进入关卡流程");
            Assert.That(ui.BtnContinue.interactable,Is.False);
            ui.BtnContinue.onClick.Invoke();
            Assert.That(preparation.Requests.Count,Is.EqualTo(1),"连续点击不能重复加载");
        }

        [UnityTest] public IEnumerator ClosingProgress_DiscardsPendingContinue()
        {
            ui.BtnContinue.onClick.Invoke(); resume.Hide(); CompleteShop(false);
            yield return null;
            Assert.That(preparation.Requests.Count,Is.Zero,"窗口关闭后商店结果不能触发旧的继续请求");
        }

        [UnityTest] public IEnumerator CompletingShopWithoutContinue_DoesNotLoadMap()
        {
            CompleteShop(false);
            yield return null;
            Assert.That(preparation.Requests.Count,Is.Zero,"单独更新商店状态不能冒充玩家点击继续");
        }

        [UnityTest] public IEnumerator ReplacingRun_DiscardsPreviousShopContinue()
        {
            ui.BtnContinue.onClick.Invoke();
            var replacement=architecture.GetModel<ArcadeRunModel>().Current;
            replacement.RunId=Guid.NewGuid().ToString("N");
            architecture.GetModel<ArcadeRunModel>().CommitSafeNode(replacement);
            architecture.SendEvent(new ArcadeRunChangedEvent(ArcadeRunPhase.SafeCheckpoint,replacement));
            CompleteShop(false);
            yield return null;
            Assert.That(preparation.Requests.Count,Is.Zero,"旧本局的继续意图不能应用到新本局");
        }

        [UnityTest] public IEnumerator FailedPreparation_AllowsRetryWithoutReopeningShop()
        {
            ui.BtnContinue.onClick.Invoke(); CompleteShop(false);
            yield return null;
            Assert.That(preparation.Requests.Count,Is.EqualTo(1));
            preparation.Requests[0].TrySetResult(false);
            Assert.That(ui.BtnContinue.interactable,Is.True);
            ui.BtnContinue.onClick.Invoke();
            Assert.That(preparation.Requests.Count,Is.EqualTo(2));
            Assert.That(UIHelper.GetUICtrl<ArcadeScoreShopUICtrl>().IsShow(),Is.False);
            Assert.That(architecture.GetModel<ArcadeRunModel>().Current.Score,Is.EqualTo(10000));
        }

        [UnityTest] public IEnumerator CompletionEvent_FinishesDispatchBeforeStartingResourceEntry()
        {
            bool observed=false;
            architecture.RegisterEvent<ArcadeRunChangedEvent>(evt=>
            {
                if(evt.Phase!=ArcadeRunPhase.SafeCheckpoint || evt.Snapshot.ScoreShop?.Completed!=true)return;
                observed=true;
                Assert.That(preparation.Requests.Count,Is.Zero,"后订阅的HUD必须先收到旧安全节点，再启动新一轮进关");
            });
            ui.BtnContinue.onClick.Invoke(); CompleteShop(false);
            Assert.That(observed,Is.True);
            yield return null;
            Assert.That(preparation.Requests.Count,Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator ChallengePreparationFailure_AfterShopKeepsContinueRetryable()
        {
            var run=architecture.GetModel<ArcadeRunModel>().Current;
            run.Checkpoint.ChallengeId="arcade.challenge.timed-clear";
            architecture.GetModel<ArcadeRunModel>().CommitSafeNode(run);
            architecture.SendEvent(new ArcadeRunChangedEvent(ArcadeRunPhase.SafeCheckpoint,run));
            ui.BtnContinue.onClick.Invoke(); CompleteShop(false);
            yield return null;
            var message=UIHelper.GetUICtrl<MessageUICtrl>();
            Assert.That(message.IsShow(),Is.True);
            var config=(MessageConfig)typeof(MessageUICtrl).GetField("currentConfig",Hidden).GetValue(message);
            typeof(MessageUICtrl).GetMethod("OnMessageButtonClick",Hidden).Invoke(message,new object[]{new object[]{config.Buttons[1]}});
            Assert.That(preparation.Requests.Count,Is.EqualTo(1));
            preparation.Requests[0].TrySetResult(false);
            Assert.That(ui.BtnContinue.interactable,Is.True,"挑战准备失败后必须能重新点击继续选择");
            ui.BtnContinue.onClick.Invoke();
            Assert.That(UIHelper.GetUICtrl<MessageUICtrl>().IsShow(),Is.True);
        }

        private void CompleteShop(bool buy)
        {
            var shop=root.GetComponentInChildren<ArcadeScoreShopUI>(true);
            if(shop!=null && shop.gameObject.activeInHierarchy)
            {
                if(buy) shop.OfferList.GetDraggableItems().First().GetComponent<ArcadeScoreShopOfferBoxUI>().BtnBuy.onClick.Invoke();
                else shop.BtnSkip.onClick.Invoke();
            }
            else
            {
                var current=architecture.GetModel<ArcadeRunModel>().Current;
                Assert.That(architecture.GetSystem<ArcadeRunSystem>().TryOperateScoreShop(ScoreShopAction.Skip,null,current.ScoreShop.Revision,out _,out var error),Is.True,error);
            }
            Assert.That(architecture.GetModel<ArcadeRunModel>().Current.ScoreShop.Completed,Is.True);
        }

        private sealed class DeferredPreparation : IGameplayResourcePreparation
        {
            public readonly List<UniTaskCompletionSource<bool>> Requests=new();
            public UniTask<bool> PrepareAsync(string map,Func<bool> current=null,CancellationToken cancellationToken=default)
            { var completion=new UniTaskCompletionSource<bool>(); Requests.Add(completion); return completion.Task; }
            public void CompleteAll(){foreach(var completion in Requests) completion.TrySetResult(false);}
        }
        private sealed class LocalizationLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T:Object=>AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/"+Path.GetFileName(path));
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> loaded) where T:Object
            {loaded?.Invoke(LoadAsset<T>(path));return ResourceLoadHandle.Empty;}
            public void Release(ResourceLoadHandle handle){}
        }
    }
}
