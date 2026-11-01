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
        /// <summary>角色选择跨系统实例恢复，保存失败不改变选中项，失效商品回到免费默认角色。</summary>
        [Test] public void RememberedCharacterRestoresAcrossSessionsAndRejectsFailedWrites()
        {
            var root=Path.Combine(Path.GetTempPath(),"RememberCharacter-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root,"Saves"));var path=Path.Combine(root,"Saves","survivor-gold-1.json");
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            try{
                File.WriteAllText(path,"{\"Version\":1,\"Balance\":1000,\"ActiveRun\":\"run\",\"Sequence\":0}");
                var arch=new Game();var model=new SurvivorModel();arch.RegisterModel(model);
                arch.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))))));
                var system=new SurvivorSystem();((ICanSetArchitecture)system).SetArchitecture(arch);typeof(SurvivorSystem).GetField("goldStore",flags).SetValue(system,new SurvivorGoldFileStore(1,root));
                Assert.That(system.TryReadCharacterShop(out var shop,out var error),Is.True,error);var chosen=shop.Products.First(p=>p.AppearanceId==1);
                Assert.That(system.TryRestoreSelectedCharacter(out error),Is.True,error);Assert.That(model.SelectedCharacter,Is.Zero,"旧账本缺少偏好时默认角色");
                Assert.That(system.TryPurchaseCharacter(chosen.Id,out error),Is.True,error);Assert.That(system.TrySelectCharacter(1,out error),Is.True,error);
                var restore=typeof(SurvivorSystem).GetMethod("TryRestoreSelectedCharacter");Assert.That(restore,Is.Not.Null,"尚未提供跨会话恢复");
                var next=new SurvivorSystem();((ICanSetArchitecture)next).SetArchitecture(arch);typeof(SurvivorSystem).GetField("goldStore",flags).SetValue(next,new SurvivorGoldFileStore(1,root));model.SelectedCharacter=0;
                Assert.That(restore.Invoke(next,new object[]{null}),Is.True);Assert.That(model.SelectedCharacter,Is.EqualTo(1));
                var saved=File.ReadAllText(path);Assert.That(next.TrySelectCharacter(9,out error),Is.False);Assert.That(File.ReadAllText(path),Is.EqualTo(saved));
                try{File.SetAttributes(path,FileAttributes.ReadOnly);Assert.That(next.TrySelectCharacter(0,out error),Is.False);Assert.That(model.SelectedCharacter,Is.EqualTo(1));Assert.That(File.ReadAllText(path),Is.EqualTo(saved));}
                finally{File.SetAttributes(path,FileAttributes.Normal);}
                var document=Newtonsoft.Json.Linq.JObject.Parse(saved);document["SelectedCharacterProductId"]=shop.Products.First(p=>p.AppearanceId==9).Id;File.WriteAllText(path,document.ToString());
                Assert.That(restore.Invoke(next,new object[]{null}),Is.True);Assert.That(model.SelectedCharacter,Is.Zero,"未拥有商品不能恢复为选中项");
                document["SelectedCharacterProductId"]="removed.product";File.WriteAllText(path,document.ToString());
                Assert.That(restore.Invoke(next,new object[]{null}),Is.True);Assert.That(model.SelectedCharacter,Is.Zero);
                Assert.That(next.TrySelectCharacter(0,out error),Is.True,error);model.SelectedCharacter=1;
                Assert.That(restore.Invoke(next,new object[]{null}),Is.True);Assert.That(model.SelectedCharacter,Is.Zero,"默认角色也要记住");
            }finally{if(File.Exists(path))File.SetAttributes(path,FileAttributes.Normal);if(Directory.Exists(root))Directory.Delete(root,true);}
        }
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
        /// <summary>购买末尾角色后立即前置，多次购买仍保持组内配置顺序；重读保持一致。</summary>
        [Test] public void OwnedCharactersComeFirstWithoutChangingOrderWithinGroups()
        {
            var root=Path.Combine(Path.GetTempPath(),"CharacterOrder-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root,"Saves"));
            try{
                File.WriteAllText(Path.Combine(root,"Saves","survivor-gold-1.json"),"{\"Version\":1,\"Balance\":1000,\"ActiveRun\":\"run\",\"Sequence\":0}");
                var arch=new Game();arch.RegisterModel(new SurvivorModel());
                arch.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))))));
                var system=new SurvivorSystem();((ICanSetArchitecture)system).SetArchitecture(arch);
                typeof(SurvivorSystem).GetField("goldStore",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(system,new SurvivorGoldFileStore(1,root));
                Assert.That(system.TryReadCharacterShop(out var snapshot,out var error),Is.True,error);
                Assert.That(snapshot.Products.Select(p=>p.AppearanceId),Is.EqualTo(new[]{1,7,3,4,9}));
                Assert.That(system.TryPurchaseCharacter("survivor.character.9",out error),Is.True,error);
                Assert.That(system.TryReadCharacterShop(out snapshot,out error),Is.True,error);
                Assert.That(snapshot.Products.Select(p=>p.AppearanceId),Is.EqualTo(new[]{9,1,7,3,4}));
                Assert.That(system.TryPurchaseCharacter("survivor.character.7",out error),Is.True,error);
                Assert.That(system.TryReadCharacterShop(out snapshot,out error),Is.True,error);
                Assert.That(snapshot.Products.Select(p=>p.AppearanceId),Is.EqualTo(new[]{7,9,1,3,4}));
                Assert.That(snapshot.Products.Select(p=>p.Owned),Is.EqualTo(new[]{true,true,false,false,false}));
                Assert.That(system.TryReadCharacterShop(out var again,out error),Is.True,error);
                Assert.That(again.Products.Select(p=>p.Id),Is.EqualTo(snapshot.Products.Select(p=>p.Id)));
                Assert.That(again.Balance,Is.EqualTo(600));
            }finally{Directory.Delete(root,true);}
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

