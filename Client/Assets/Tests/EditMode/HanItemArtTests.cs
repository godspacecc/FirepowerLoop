using System.Linq;
using Game;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>检查首章道具正式配置、同源模型及落地拾取契约，不改写资源。</summary>
    public sealed class HanItemArtTests
    {
        [TestCase(32,"HanForgedAxe")]
        [TestCase(33,"HanForgedPick")]
        [TestCase(34,"HanSteelBlade")]
        [TestCase(35,"HanBow")]
        [TestCase(36,"HanGrainPouch")]
        [TestCase(37,"HanWaterPot")]
        public void ItemUsesDedicatedIconAndGroundModel(int id,string name)
        {
            var config=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/"+n+".bytes").bytes)));
            Assert.That(config.TryGetItem(id,out var item,out var error),Is.True,error);
            Assert.That(item.IconAddress,Does.EndWith("Icon"+name));
            var lootIds=new[]{"loot.han.axe","loot.han.pick","loot.han.blade","loot.han.bow","loot.han.grain","loot.han.pot"};
            Assert.That(config.TryGetLoot(lootIds[id-32],out var loot,out error),Is.True,error);
            Assert.That(loot.PrefabAddress,Does.EndWith(name+"Pickup"));
            var icon=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Art/UI/Items/Icon"+name+".png");Assert.That(icon,Is.Not.Null);
            var path="Assets/Game/Prefabs/SceneObjects/"+name+"Pickup.prefab";
            AssertCollector(path);
            var pickup=AssetDatabase.LoadAssetAtPath<GameObject>(path);Assert.That(pickup,Is.Not.Null);
            Assert.That(pickup.GetComponent<ResourcePickupView>(),Is.Not.Null);
            var trigger=pickup.GetComponent<BoxCollider>();Assert.That(trigger.isTrigger,Is.True);
            Assert.That(trigger.center.y-trigger.size.y*.5f,Is.EqualTo(0).Within(.002f),"落地算法以 Trigger 底面贴地");
            Assert.That(pickup.GetComponentsInChildren<MeshFilter>().Length,Is.EqualTo(1),"只展示正式模型，不残留测试方块");
            Assert.That(AssetDatabase.GetDependencies(path).Any(p=>p.StartsWith("Assets/Art/")),Is.False);
            var modelPath="Assets/Game/Art/Civilisation/HanItems/Models/"+name+".fbx";
            Assert.That(AssetDatabase.GetDependencies(path),Does.Contain(modelPath));
            if(id<36)
            {
                var catalog=AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>("Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
                var entry=catalog.FindByWorldItemId(id);Assert.That(entry,Is.Not.Null,"正式玩家共享目录也必须映射新装备");
                Assert.That(entry.VisualSource.name,Is.EqualTo(name));
                Assert.That(config.TryGetPlayerWeaponAppearance(id,out var appearance,out error),Is.True,error);
                Assert.That(appearance.PrefabAddress,Does.EndWith(name));
                var weapon="Assets/Game/Prefabs/Characters/Weapons/"+name+".prefab";
                AssertCollector(weapon);
                Assert.That(AssetDatabase.GetDependencies(weapon),Does.Contain(modelPath));
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(weapon).GetComponentsInChildren<Collider>(),Is.Empty);
                var scene=EditorSceneManager.NewPreviewScene();
                try
                {
                    var player=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"),scene);
                    var presenter=player.GetComponentInChildren<CharacterWeaponPresenter>(true);
                    Assert.That(presenter.SetWeaponId(entry.Id),Is.True,presenter.LastError);
                    Assert.That(presenter.Visual.GetComponentsInChildren<MeshFilter>().Select(m=>AssetDatabase.GetAssetPath(m.sharedMesh)),Does.Contain(modelPath));
                }
                finally{EditorSceneManager.ClosePreviewScene(scene);}
            }
        }

        private static void AssertCollector(string path)
        {
            var setting=new SerializedObject(AssetDatabase.LoadAssetAtPath<Object>("Assets/AssetBundleCollectorSetting.asset"));
            var packages=setting.FindProperty("Packages");int matches=0;
            for(int p=0;p<packages.arraySize;p++)
            {
                var package=packages.GetArrayElementAtIndex(p);if(package.FindPropertyRelative("PackageName").stringValue!="DefaultPackage")continue;
                var groups=package.FindPropertyRelative("Groups");
                for(int g=0;g<groups.arraySize;g++)
                {
                    var collectors=groups.GetArrayElementAtIndex(g).FindPropertyRelative("Collectors");
                    for(int c=0;c<collectors.arraySize;c++)
                    {
                        var collector=collectors.GetArrayElementAtIndex(c);
                        if(collector.FindPropertyRelative("CollectPath").stringValue!=path)continue;
                        matches++;
                        Assert.That(collector.FindPropertyRelative("AddressRuleName").stringValue,Is.EqualTo("AddressByGameRelativePathNoExtension"));
                        Assert.That(collector.FindPropertyRelative("AssetTags").stringValue.Split(';'),Does.Contain("expedition"));
                        Assert.That(collector.FindPropertyRelative("CollectorGUID").stringValue,Is.EqualTo(AssetDatabase.AssetPathToGUID(path)));
                    }
                }
            }
            Assert.That(matches,Is.EqualTo(1),"正式资源必须且只能登记一次："+path);
        }
    }
}
