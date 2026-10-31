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
    public sealed class CharacterEntryFailureTests
    {
        [Test] public void RevalidationFailureUsesLocalizedEntryMessage()
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
                File.WriteAllText(Path.Combine(root,"Saves","survivor-gold-1.json"), "{\"Version\":1,\"Balance\":100,\"ActiveRun\":\"test\",\"Sequence\":0}");
                Assert.That(system.TryPurchaseCharacter("survivor.character.1",out error),Is.True,error);
                Assert.That(system.TrySelectCharacter(1,out error),Is.True,error);
                string path=Path.Combine(root,"Saves","survivor-gold-1.json");
                File.WriteAllText(path,"broken");
                var flow=new SurvivorSceneFlowSystem();((ICanSetArchitecture)flow).SetArchitecture(architecture);
                var result=flow.StartAsync().GetAwaiter().GetResult();
                Assert.That(result.Success,Is.False);Assert.That(result.Error,Is.EqualTo("wallet.read_failed"));
                var format=typeof(StartUICtrl).GetMethod("FormatSurvivorEntryError",BindingFlags.Static|BindingFlags.NonPublic);
                Assert.That(format,Is.Not.Null,"开局失败出口必须映射内部码");
                Assert.That(format.Invoke(null,new object[]{result.Error}),Is.EqualTo("金币记录无法读取，请检查存档。"));
                Assert.That(format.Invoke(null,new object[]{"character.not_owned"}),Is.EqualTo("尚未拥有此角色。"));
                Assert.That(format.Invoke(null,new object[]{"character.config_failed"}),Is.EqualTo("操作失败，请返回后重试。"));
                Assert.That(format.Invoke(null,new object[]{"场景加载超时"}),Is.EqualTo("场景加载超时"));
                Assert.That(system.TrySelectCharacter(0,out error),Is.True,error);
                UnityEngine.TestTools.LogAssert.Expect(LogType.Error,new System.Text.RegularExpressions.Regex(".*开局金币读取失败.*"));
                var defaultResult=flow.StartAsync().GetAwaiter().GetResult();
                Assert.That(defaultResult.Success,Is.False);Assert.That(defaultResult.Error,Is.EqualTo("wallet.read_failed"));
                Assert.That(format.Invoke(null,new object[]{defaultResult.Error}),Is.EqualTo("金币记录无法读取，请检查存档。"));
                loc.LoadFromJson("en-US",File.ReadAllText("Assets/Game/Config/Localization/en-US.json"));
                architecture.GetModel<LocalizationModel>().CurrentLanguageCode.Value="en-US";
                Assert.That(format.Invoke(null,new object[]{result.Error}),Is.EqualTo("Unable to read your gold record."));
                Assert.That(File.ReadAllText(path),Is.EqualTo("broken"));


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
