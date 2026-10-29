using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>用户已确认的AK与空投尺寸契约，正式资源只读，临时实例检查后销毁。</summary>
    public sealed class ConfirmedWeaponPresentationTests
    {
        private ArcadeConfigCatalogDefinition catalog;
        [SetUp] public void Setup()
        {
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
        }
        [Test] public void BasicGunUsesSeparateAKWithFasterCadenceAndPreservesMP40()
        {
            var gun=catalog.Weapons.Single(w=>w.Id=="arcade.weapon.basic");
            Assert.That(gun.HeldVisualResourceName,Is.EqualTo("ArcadeBasicRifle"));
            Assert.That(gun.Damage,Is.EqualTo(36));Assert.That(gun.FireIntervalSeconds,Is.EqualTo(.18f));
            var rapidGun=catalog.Weapons.Single(w=>w.Id=="arcade.weapon.rapid");
            Assert.That(rapidGun.FireIntervalSeconds,Is.EqualTo(.10f));
            foreach(var other in catalog.Weapons.Where(w=>w.Id.StartsWith("arcade.weapon.")&&w.Id!=rapidGun.Id))
                Assert.That(other.FireIntervalSeconds,Is.GreaterThan(rapidGun.FireIntervalSeconds),other.Id);
            var player=Load("Characters/GrayboxPlayer").GetComponent<CharacterWeaponPresenter>();
            var basic=player.Catalog.Find("arcade.weapon.basic");var rapid=player.Catalog.Find("arcade.weapon.rapid");
            Assert.That(AssetDatabase.GetAssetPath(basic.VisualSource),Does.EndWith("/ArcadeBasicRifle.prefab"));
            Assert.That(AssetDatabase.GetAssetPath(rapid.VisualSource),Does.EndWith("/ArcadeRapidWeaponShared.prefab"));
            Assert.That(basic.Animations.Idle,Is.SameAs(rapid.Animations.Idle));
            Assert.That(basic.VisualSource.GetComponentsInChildren<MeshFilter>(true).Any(m=>AssetDatabase.GetAssetPath(m.sharedMesh).EndsWith("/ArcadeRifle.fbx")),Is.True);
        }
        [Test] public void BasicAKSwitchesBackAfterTemporaryGunWithoutLeakingVisuals()
        {
            var player=Object.Instantiate(Load("Characters/GrayboxPlayer"));
            try
            {
                var presenter=player.GetComponent<CharacterWeaponPresenter>();
                foreach(var id in new[]{"arcade.weapon.basic","arcade.weapon.flamethrower","arcade.weapon.basic"})
                {Assert.That(presenter.SetWeaponId(id),Is.True,presenter.LastError);Assert.That(presenter.Current.Id,Is.EqualTo(id));}
                Assert.That(presenter.Visual.GetComponentsInChildren<MeshFilter>(true).Any(m=>AssetDatabase.GetAssetPath(m.sharedMesh).EndsWith("/ArcadeRifle.fbx")),Is.True);
                Assert.That(player.GetComponentsInChildren<Transform>(true).Count(t=>t.name.StartsWith("HeldWeapon_")),Is.EqualTo(1));
            }
            finally{Object.DestroyImmediate(player);}
        }
        [Test] public void UnusedMeleeConfigsAreRemovedWhileEnemyWeaponsRemainValid()
        {
            var presentation=Load("Characters/GrayboxPlayer").GetComponent<CharacterWeaponPresenter>().Catalog;
            foreach(var id in new[]{"weapon.knife","weapon.axe","weapon.pickaxe","weapon.han.axe","weapon.han.pick","weapon.han.blade","weapon.han.bow","tool.torch"})
            {
                Assert.That(catalog.Weapons.Any(w=>w.Id==id),Is.False,id);
                Assert.That(presentation.Find(id),Is.Null,id+" 已退役的目录项不能重新暴露到姿态调试");
            }
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            foreach(var enemy in tables.TbEnemy.DataList)
                Assert.That(catalog.Weapons.Any(w=>w.Id==enemy.WeaponId),Is.True,enemy.Id);
        }
        [Test] public void OpenedAirdropIsPlayerSizedAndCrateIsSmaller()
        {
            WithPickup("ArcadeFlamethrower",(go,pickup)=>
            {
                pickup.SetWeaponDelivery(true,0,2);
                var visual=go.transform.Find("ArcadeDropVisual");var box=go.GetComponentInChildren<ArcadeWeaponDeliveryView>(true);
                Assert.That(Longest(visual.gameObject),Is.EqualTo(1.7f).Within(.01f));
                Assert.That(box.transform.lossyScale.x,Is.EqualTo(.6f).Within(.001f));
                var before=visual.localScale;pickup.SetWeaponDelivery(true,0,2);
                Assert.That(visual.localScale,Is.EqualTo(before),"重复同步不能叠乘放大");
            });
        }
        [Test] public void OrdinarySkullsKeepOriginalSizeAndCrateReuseRestoresPose()
        {
            WithPickup("ArcadeDropSkullRed",(go,pickup)=>
            {
                var visual=go.transform.Find("ArcadeDropVisual");float original=Longest(visual.gameObject);
                Assert.That(original,Is.LessThan(.6f));
                pickup.SetWeaponDelivery(true,0,2);pickup.SetWeaponDelivery(false,0,2);
                Assert.That(Longest(visual.gameObject),Is.EqualTo(original).Within(.001f));
            });
        }
        /// <summary>红紫掉落的实体最长边与正式金币一致；重复同步和空投回池不能污染普通尺寸。</summary>
        [Test]
        public void ApprovedSkullDropMatchesCoinAndKeepsSizeAfterReuse()
        {
            float coinSize=0;
            WithPickup("ArcadeDropCoin",(go,pickup)=>coinSize=Longest(go.transform.Find("ArcadeDropVisual").gameObject));
            foreach(var skull in new[]{"ArcadeDropSkullRed","ArcadeDropSkullPurple"})
            WithPickup(skull,(go,pickup)=>
            {
                var visual=go.transform.Find("ArcadeDropVisual");
                Assert.That(Longest(visual.gameObject),Is.EqualTo(coinSize).Within(.001f));
                pickup.SetArcadePresentation(Load("SceneObjects/"+skull),"Prefabs/SceneObjects/"+skull);
                Assert.That(Longest(visual.gameObject),Is.EqualTo(coinSize).Within(.001f));
                pickup.SetWeaponDelivery(true,0,2);pickup.SetWeaponDelivery(false,0,2);
                Assert.That(Longest(visual.gameObject),Is.EqualTo(coinSize).Within(.001f));
            });
        }
        [Test] public void AllGroundDropNamesAreEmptyAtTheArcadeProjectionSource()
        {
            var resolve=typeof(ArcadeCombatViewController).GetMethod("ResolveDropDisplayName",BindingFlags.Static|BindingFlags.NonPublic);
            foreach(var drop in catalog.Drops)
                Assert.That(resolve.Invoke(null,new object[]{drop}),Is.EqualTo(string.Empty),drop.Id);
        }
        [Test] public void DeliveryRestoresOrdinaryRotationWhenPooled()
        {
            WithPickup("ArcadeFlamethrower",(go,pickup)=>
            {
                var visual=go.transform.Find("ArcadeDropVisual");var rotation=visual.localRotation;
                pickup.SetWeaponDelivery(true,0,2);pickup.SetWeaponDelivery(false,0,2);
                Assert.That(Quaternion.Angle(visual.localRotation,rotation),Is.LessThan(.001f));
            });
        }
        private static void WithPickup(string name,System.Action<GameObject,ResourcePickupView> check)
        {
            var go=Object.Instantiate(Load("SceneObjects/TestResourcePickup"));
            try{go.transform.position=Vector3.up*.3f;var pickup=go.GetComponent<ResourcePickupView>();pickup.SetArcadePresentation(Load("SceneObjects/"+name),"Prefabs/SceneObjects/"+name);check(go,pickup);}
            finally{Object.DestroyImmediate(go);}
        }
        private static GameObject Load(string name)=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/"+name+".prefab");
        private static float Longest(GameObject go)
        {
            var meshes=go.GetComponentsInChildren<MeshRenderer>(true);var b=meshes.First().bounds;
            foreach(var mesh in meshes.Skip(1))b.Encapsulate(mesh.bounds);
            return Mathf.Max(b.size.x,b.size.y,b.size.z);
        }
    }
}
