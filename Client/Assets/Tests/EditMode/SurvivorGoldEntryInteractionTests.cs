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
    public sealed class SurvivorGoldEntryInteractionTests
    {
        [Test] public void CloseDoubleClickAndCorruptWalletRespectEntryLifecycle()
        {
            const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
            var architectureField=typeof(Architecture<Game>).GetField("mArchitecture",BindingFlags.Static|BindingFlags.NonPublic);
            var cacheField=typeof(LocalizationLanguages).GetField("cachedConfig",BindingFlags.Static|BindingFlags.NonPublic);
            var previous=architectureField.GetValue(null);var previousLoader=ResourceLoader.Current;var previousCache=cacheField.GetValue(null);
            var root=Path.Combine(Path.GetTempPath(),"SurvivorEntry-"+Guid.NewGuid().ToString("N"));
            GameObject instance=null;SurvivorEntryUICtrl controller=null;
            try
            {
                var architecture=new Game();architectureField.SetValue(null,architecture);
                var system=new SurvivorSystem();architecture.RegisterSystem(system);architecture.RegisterModel(new SurvivorModel());
                var store=new SurvivorGoldFileStore(1,root);typeof(SurvivorSystem).GetField("goldStore",flags).SetValue(system,store);
                Assert.That(store.TryBeginRun("test",out _,out var error),Is.True,error);Assert.That(store.TryCollect("test:1",out _,out error),Is.True,error);
                ResourceLoader.Use(new LocalizationAssetResourceLoader());LocalizationLanguages.ResetForTests();
                architecture.RegisterModel(new LocalizationModel());var loc=new LocalizationUtility(false);loc.LoadFromJson("zh-CN",File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));architecture.RegisterUtility(loc);
                instance=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Survivor/SurvivorEntry.prefab"));
                var ui=instance.GetComponent<SurvivorEntryUI>();controller=new SurvivorEntryUICtrl();typeof(BaseUICtrl).GetField("BaseUI",flags).SetValue(controller,ui);
                int starts=0;controller.Show();controller.Configure(()=>starts++);
                Assert.That(ui.BalanceText.text,Is.EqualTo("1"));Assert.That(ui.BtnStart.interactable,Is.True);
                ui.BtnClose.onClick.Invoke();Assert.That(controller.IsShow(),Is.False);
                ui.BtnStart.onClick.Invoke();Assert.That(starts,Is.Zero,"关闭后旧回调必须失效");
                controller.Show();controller.Configure(()=>starts++);ui.BtnStart.onClick.Invoke();ui.BtnStart.onClick.Invoke();Assert.That(starts,Is.EqualTo(1));
                string path=Path.Combine(root,"Saves","survivor-gold-1.json");string good=File.ReadAllText(path);File.WriteAllText(path,"broken");
                controller.Show();controller.Configure(()=>starts++);Assert.That(ui.BtnStart.interactable,Is.False);Assert.That(ui.ErrorText.text,Is.Not.Empty);
                ui.BtnBack.onClick.Invoke();Assert.That(controller.IsShow(),Is.False);Assert.That(starts,Is.EqualTo(1));
                File.WriteAllText(path,good);controller.Show();controller.Configure(()=>starts++);
                Assert.That(ui.BtnStart.interactable,Is.True);Assert.That(ui.BalanceText.text,Is.EqualTo("1"));Assert.That(ui.ErrorText.text,Is.Empty);
                architecture.SendEvent(new LanguageChangedEvent());Assert.That(ui.BalanceText.text,Is.EqualTo("1"));
                ui.BtnStart.onClick.Invoke();Assert.That(starts,Is.EqualTo(2));
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
