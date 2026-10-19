using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    /// <summary>真实黑幕 Prefab 的引用、逐帧透明度与回收；不启动游戏或改写资源。</summary>
    public sealed class ArcadeRoundTransitionTests
    {
        private GameObject root;
        private ArcadeRoundTransitionUI view;
        private ArcadeRoundTransitionUICtrl controller;
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeRoundTransition.prefab"));
            view=root.GetComponent<ArcadeRoundTransitionUI>();controller=new ArcadeRoundTransitionUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI",Hidden).SetValue(controller,view);
            typeof(BaseUICtrl).GetField("_isActive",Hidden).SetValue(controller,true);
            typeof(ArcadeRoundTransitionUICtrl).GetMethod("OnShow",Hidden).Invoke(controller,null);
        }
        [TearDown] public void Cleanup(){controller.Abort();Object.DestroyImmediate(root);}
        private void Tick(float seconds)=>typeof(ArcadeRoundTransitionUICtrl).GetMethod("TickFade",Hidden).Invoke(controller,new object[]{seconds});
        [Test] public void CurtainPrefabIsCollectedAtItsRuntimeAddress()
        {
            var setting=AssetDatabase.LoadAssetAtPath<Object>("Assets/AssetBundleCollectorSetting.asset");
            var result=setting.GetType().GetMethod("BeginCollect").Invoke(setting,new object[]{"DefaultPackage",true,false});
            foreach(var asset in (System.Collections.IEnumerable)result.GetType().GetProperty("CollectAssets").GetValue(result))
            {
                if((string)asset.GetType().GetProperty("Address").GetValue(asset)!="Prefabs/UI/Arcade/ArcadeRoundTransition")continue;
                Assert.That(asset.GetType().GetProperty("AssetTags").GetValue(asset),Does.Contain("arcade"));return;
            }
            Assert.Fail("资源清单必须包含黑幕运行时地址");
        }
        [Test] public void FormalCurtainHasGeneratedReferenceAndCoversWholeScreen()
        {
            Assert.That(view,Is.Not.Null);Assert.That(view.Curtain,Is.Not.Null);
            Assert.That(root.GetComponent<ReferenceCollector>().data.Find(e=>e.key=="Curtain").gameObject,Is.SameAs(view.Curtain));
            Assert.That(view.Curtain.GetComponent<Image>().color,Is.EqualTo(Color.black));
            var rect=(RectTransform)view.Curtain.transform;Assert.That(rect.anchorMin,Is.EqualTo(Vector2.zero));Assert.That(rect.anchorMax,Is.EqualTo(Vector2.one));
            Assert.That(view.Curtain.GetComponentsInChildren<TMPro.TMP_Text>(true),Is.Empty);
            Assert.That(controller.BlocksGame,Is.True);Assert.That(controller.Layer(),Is.EqualTo(UILayer.Top));
        }
        [Test] public void FadeHoldsBlackAcrossDuplicateRequestsUntilResourcesReady()
        {
            controller.Begin();Tick(.1f);Assert.That(view.Curtain.alpha,Is.EqualTo(.5f).Within(.001f));Assert.That(controller.Covered,Is.False);
            controller.Begin();Tick(.1f);Assert.That(controller.Covered,Is.True);Tick(20);Assert.That(view.Curtain.alpha,Is.EqualTo(1));
            controller.Finish();Tick(.1f);Assert.That(view.Curtain.alpha,Is.EqualTo(.5f).Within(.001f));Assert.That(controller.IsShow(),Is.True);
            Tick(.1f);Assert.That(controller.IsShow(),Is.False);Assert.That(view.Curtain.alpha,Is.Zero);
        }
        [Test] public void AbortClearsBlackAndNewTransitionDoesNotInheritOpacity()
        {
            controller.Begin();Tick(.2f);controller.Abort();Assert.That(controller.IsShow(),Is.False);Assert.That(view.Curtain.alpha,Is.Zero);
        }
        [Test] public void NormalRoundPreparationDoesNotOpenFullLoadingScreen()
        {
            var loadingRoot=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Common/Loading.prefab"));
            try
            {
                var loading=new LoadingUICtrl();typeof(BaseUICtrl).GetField("BaseUI",Hidden).SetValue(loading,loadingRoot.GetComponent<LoadingUI>());
                typeof(LoadingUICtrl).GetMethod("OnArcadePreparationChanged",Hidden).Invoke(loading,new object[]{
                    new ArcadeCombatHudChangedEvent(new ArcadeCombatHudSnapshot{IslandTransitioning=true})});
                Assert.That(loading.IsShow(),Is.False,"正常局内准备不再弹出背景图和进度条");
            }
            finally{Object.DestroyImmediate(loadingRoot);}
        }
    }
}
