using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using NUnit.Framework;
using QFramework;
using Luban;
using UnityEngine;
using UnityEditor;
namespace Game.Tests
{
    /// <summary>角色购买业务、真实人物绑定和真实收集器的限定检查。</summary>
    public sealed class SurvivorCharacterTests
    {
        [Test] public void PurchaseSelectionAndStopUseAuthoritativeCatalog()
        {
            var root=Path.Combine(Path.GetTempPath(),"CharacterBusiness-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root,"Saves"));
            try {
                File.WriteAllText(Path.Combine(root,"Saves","survivor-gold-1.json"),"{\"Version\":1,\"Balance\":100,\"ActiveRun\":\"run\",\"Sequence\":0}");
                var arch=new Game();var model=new SurvivorModel();arch.RegisterModel(model);
                arch.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))))));
                var system=new SurvivorSystem();((ICanSetArchitecture)system).SetArchitecture(arch);
                typeof(SurvivorSystem).GetField("goldStore",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(system,new SurvivorGoldFileStore(1,root));
                Assert.That(system.TryReadCharacterShop(out var shop,out var error),Is.True,error);
                Assert.That(shop.Products.Select(p=>p.AppearanceId),Is.EqualTo(new[]{1,7,3,4,9}));
                Assert.That(shop.Products.Select(p=>p.Price),Is.EqualTo(new long[]{50,100,150,200,300}));
                Assert.That(system.TrySelectCharacter(0,out error),Is.True);
                Assert.That(system.TrySelectCharacter(9,out error),Is.False);Assert.That(error,Is.EqualTo("character.not_owned"));
                Assert.That(system.TryPurchaseCharacter("invalid",out error),Is.False);Assert.That(error,Is.EqualTo("character.unknown"));
                Assert.That(system.TryPurchaseCharacter("survivor.character.9",out error),Is.False);Assert.That(error,Is.EqualTo("wallet.insufficient"));
                Assert.That(system.TryPurchaseCharacter("survivor.character.1",out error),Is.True,error);
                Assert.That(system.TryPurchaseCharacter("survivor.character.1",out error),Is.True,error);
                Assert.That(system.TrySelectCharacter(1,out error),Is.True,error);
                system.Stop();Assert.That(model.SelectedCharacter,Is.EqualTo(1));
                Assert.That(system.TrySelectCharacter(model.SelectedCharacter,out error),Is.True,error);
                Assert.That(system.TryReadCharacterShop(out shop,out error),Is.True);Assert.That(shop.Balance,Is.EqualTo(50));
            } finally {Directory.Delete(root,true);}
        }
        [Test] public void RealPlayerWholeBodiesApplyWithoutChangingScale()
        {
            string path=AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("GrayboxPlayer t:Prefab").First());
            var instance=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            try {
                var appearance=instance.GetComponentInChildren<ClothingAppearanceView>(true);Assert.That(appearance,Is.Not.Null);
                var scale=instance.transform.localScale;
                foreach(int id in new[]{1,7,3,4,9,0})Assert.That(appearance.ApplyWholeBody(id),Is.True,id.ToString());
                Assert.That(instance.transform.localScale,Is.EqualTo(scale));
            } finally {UnityEngine.Object.DestroyImmediate(instance);}
        }
        [Test] public void CharacterTableIsReallyCollectedForStartup()
        {
            var setting=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/AssetBundleCollectorSetting.asset");
            var result=setting.GetType().GetMethod("BeginCollect").Invoke(setting,new object[]{"DefaultPackage",true,false});
            var assets=(IEnumerable)result.GetType().GetProperty("CollectAssets").GetValue(result);
            int matches=0;
            foreach(var asset in assets) {
                var type=asset.GetType();var address=(string)type.GetProperty("Address").GetValue(asset);
                if(address!="tbsurvivorcharacter.bytes")continue;
                matches++;Assert.That((IEnumerable)type.GetProperty("AssetTags").GetValue(asset),Does.Contain("startup"));
            }
            Assert.That(matches,Is.EqualTo(1));
        }
    }
}

