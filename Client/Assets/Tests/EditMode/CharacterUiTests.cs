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
    public sealed class CharacterUiTests
    {
        [Test] public void PurchaseAndSelectionResetReusableCards()
        {
            const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
            var architectureField=typeof(Architecture<Game>).GetField("mArchitecture",BindingFlags.Static|BindingFlags.NonPublic);
            var cacheField=typeof(LocalizationLanguages).GetField("cachedConfig",BindingFlags.Static|BindingFlags.NonPublic);
            var previous=architectureField.GetValue(null);var previousLoader=ResourceLoader.Current;var previousCache=cacheField.GetValue(null);
            var root=Path.Combine(Path.GetTempPath(),"SurvivorEntry-"+Guid.NewGuid().ToString("N"));
            GameObject instance=null; GameObject selectInstance=null; SurvivorEntryUICtrl selectController=null; MainMenuShopUICtrl controller=null;
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
                Directory.CreateDirectory(Path.Combine(root,"Saves"));
                string path=Path.Combine(root,"Saves","survivor-gold-1.json");
                File.WriteAllText(path,"{\"Version\":1,\"Balance\":100,\"ActiveRun\":\"test\",\"Sequence\":0}");
                controller.RefreshContent();
                var cards=ui.Cards.GetComponentsInChildren<CharacterCardBoxUI>();
                Assert.That(cards.Length,Is.EqualTo(5));
                Assert.That(ui.Cards.itemPrefab,Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Survivor/Box/CharacterCardBox.prefab")));
                cards[0].BtnAction.onClick.Invoke();
                Assert.That(ui.BalanceText.text,Is.EqualTo("金币  50"));
                Assert.That(cards[0].BtnAction.interactable,Is.False);
                string purchased=File.ReadAllText(path);
                cards[0].BtnAction.onClick.Invoke();Assert.That(File.ReadAllText(path),Is.EqualTo(purchased));
                cards[4].BtnAction.onClick.Invoke();Assert.That(File.ReadAllText(path),Is.EqualTo(purchased));Assert.That(ui.ErrorText.text,Is.EqualTo("金币不足。"));
                selectInstance=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Survivor/SurvivorEntry.prefab"));
                var select=selectInstance.GetComponent<SurvivorEntryUI>();selectController=new SurvivorEntryUICtrl();typeof(BaseUICtrl).GetField("BaseUI",flags).SetValue(selectController,select);
                int starts=0;selectController.Show();selectController.Configure(()=>starts++);
                var choices=select.Cards.GetComponentsInChildren<CharacterCardBoxUI>();Assert.That(choices.Length,Is.EqualTo(6));
                Assert.That(choices[0].BtnAction.interactable,Is.True);Assert.That(choices[5].BtnAction.interactable,Is.False);
                choices[5].BtnAction.onClick.Invoke();Assert.That(architecture.GetModel<SurvivorModel>().SelectedCharacter,Is.EqualTo(0));
                choices[1].BtnAction.onClick.Invoke();Assert.That(architecture.GetModel<SurvivorModel>().SelectedCharacter,Is.EqualTo(1));
                select.BtnClose.onClick.Invoke();select.BtnStart.onClick.Invoke();Assert.That(starts,Is.Zero);
                selectController.Show();selectController.Configure(()=>starts++);select.BtnStart.onClick.Invoke();select.BtnStart.onClick.Invoke();Assert.That(starts,Is.EqualTo(1));
                selectController.Show();selectController.Configure(()=>starts++);Assert.That(choices[1].ActionText.text,Is.EqualTo("已选择"));
                Assert.That(File.ReadAllText(path),Is.EqualTo(purchased));


            }
            finally
            {
                selectController?.Hide();if(selectInstance!=null)UnityEngine.Object.DestroyImmediate(selectInstance);controller?.Hide();if(instance!=null)UnityEngine.Object.DestroyImmediate(instance);
                architectureField.SetValue(null,previous);ResourceLoader.Use(previousLoader);cacheField.SetValue(null,previousCache);
                if(Directory.Exists(root))Directory.Delete(root,true);
            }
        }
    }
}
