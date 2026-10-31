using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>正式入口Controller与Prefab的交互；钱包与架构均隔离到测试实例。</summary>
    public sealed class MainMenuShopInteractionTests
    {
        [Test] public void ReopenLanguageAndCorruptWalletAreReadOnly()
        {
            const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
            var architectureField=typeof(Architecture<Game>).GetField("mArchitecture",BindingFlags.Static|BindingFlags.NonPublic);
            var cacheField=typeof(LocalizationLanguages).GetField("cachedConfig",BindingFlags.Static|BindingFlags.NonPublic);
            var previous=architectureField.GetValue(null);var previousLoader=ResourceLoader.Current;var previousCache=cacheField.GetValue(null);
            var root=Path.Combine(Path.GetTempPath(),"SurvivorEntry-"+Guid.NewGuid().ToString("N"));
            GameObject instance=null;MainMenuShopUICtrl controller=null;
            try
            {
                var architecture=new Game();architectureField.SetValue(null,architecture);
                architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(()=>new Tables(n=>new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))))));
                var system=new SurvivorSystem();architecture.RegisterSystem(system);architecture.RegisterModel(new SurvivorModel());
                var store=new SurvivorGoldFileStore(1,root);typeof(SurvivorSystem).GetField("goldStore",flags).SetValue(system,store);
                Assert.That(store.TryBeginRun("test",out _,out var error),Is.True,error);Assert.That(store.TryCollect("test:1",out _,out error),Is.True,error);
                ResourceLoader.Use(new LocalizationAssetResourceLoader());LocalizationLanguages.ResetForTests();
                architecture.RegisterModel(new LocalizationModel());var loc=new LocalizationUtility(false);loc.LoadFromJson("zh-CN",File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));architecture.RegisterUtility(loc);
                instance=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/MainMenuShop.prefab"));
                var ui=instance.GetComponent<MainMenuShopUI>();controller=new MainMenuShopUICtrl();typeof(BaseUICtrl).GetField("BaseUI",flags).SetValue(controller,ui);
                controller.Show();
                Assert.That(ui.Cards.totalCount, Is.EqualTo(5));
                Assert.That(ui.BalanceText.text,Is.EqualTo("金币  1"));
                ui.BtnClose.onClick.Invoke();Assert.That(controller.IsShow(),Is.False);
                string path=Path.Combine(root,"Saves","survivor-gold-1.json");string good=File.ReadAllText(path);File.WriteAllText(path,"broken");
                controller.Show();Assert.That(ui.BalanceText.text,Does.Contain("—"));Assert.That(ui.ErrorText.text,Is.Not.Empty);
                Assert.That(File.ReadAllText(path),Is.EqualTo("broken"),"只读商店不得覆盖坏档");
                ui.BtnBack.onClick.Invoke();Assert.That(controller.IsShow(),Is.False);
                File.WriteAllText(path,good);controller.Show();
                Assert.That(ui.BalanceText.text,Is.EqualTo("金币  1"));Assert.That(ui.ErrorText.text,Is.Empty);
                loc.LoadFromJson("en-US",File.ReadAllText("Assets/Game/Config/Localization/en-US.json"));
                architecture.GetModel<LocalizationModel>().CurrentLanguageCode.Value="en-US";
                architecture.SendEvent(new LanguageChangedEvent());Assert.That(ui.BalanceText.text,Is.EqualTo("Gold  1"));
                Assert.That(File.ReadAllText(path),Is.EqualTo(good),"打开/刷新/关闭不得写盘");

            }
            finally
            {
                controller?.Hide();if(instance!=null)UnityEngine.Object.DestroyImmediate(instance);
                architectureField.SetValue(null,previous);ResourceLoader.Use(previousLoader);cacheField.SetValue(null,previousCache);
                if(Directory.Exists(root))Directory.Delete(root,true);
            }
        }
    }
}
