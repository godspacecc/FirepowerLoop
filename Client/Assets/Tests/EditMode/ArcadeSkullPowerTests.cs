using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using UnityEditor;

namespace Game.Tests
{
    /// <summary>从正式拾取、开火和计时入口验证头骨强化，不复制伤害裁决。</summary>
    public sealed class ArcadeSkullPowerTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;

        [SetUp] public void Setup()
        {
            var architecture=new Game();var run=new ArcadeRunModel();architecture.RegisterModel(run);
            architecture.RegisterModel(new ArcadeCombatModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
            run.CommitSafeNode(new ArcadeRunSaveData{RunId="skull-test",RemainingLives=3,IsCasualRoute=true});run.EnterArena();
            runtime=new ArcadeCombatRuntime{ArenaNumber=1,PlayerHealth=100,PlayerMaxHealth=100,AimDirection=Vector2.up};
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);
            Set("catalog",catalog);Set("arena",catalog.Arenas.First(a=>a.ArenaNumber==1));Set("view",new ArcadeCombatViewController());Set("random",new System.Random(5));
        }
        private void Set(string name,object value)=>typeof(ArcadeCombatSystem).GetField(name,Flags).SetValue(system,value);
        private object Call(string name,params object[] values)=>typeof(ArcadeCombatSystem).GetMethod(name,Flags).Invoke(system,values);
        private ArcadeDropDefinition Skull(bool large)
        {
            var id=large?"arcade.drop.skull.large":"arcade.drop.skull.small";
            Assert.That(catalog.Drops.Any(d=>d.Id==id),Is.True,"正式掉落池缺少头骨");return catalog.Drops.First(d=>d.Id==id);
        }
        private void Pickup(ArcadeDropDefinition drop)
        {runtime.Drops.Add(900,new ArcadeDropRuntime{Id=900,Definition=drop,Position=runtime.PlayerPosition});Call("TickDrops",runtime);Assert.That(runtime.Drops,Is.Empty);}

        [TestCase(false,2,"#Red")]
        [TestCase(true,3,"#Purple")]
        public void ContactSkullUpgradesRealWeaponShot(bool large,int multiplier,string suffix)
        {
            var drop=Skull(large);Assert.That(drop.Weight,Is.GreaterThan(0));Pickup(drop);Call("TryFire",runtime);
            var shot=runtime.Projectiles.Values.Single();var weapon=catalog.Weapons.First(w=>w.Id==catalog.Rule.BaseWeaponId);
            Assert.That(shot.Damage,Is.EqualTo(weapon.Damage*multiplier));
            Assert.That(runtime.FireCooldown,Is.EqualTo(weapon.FireIntervalSeconds/multiplier).Within(.0001f));
            Assert.That(shot.ProjectileVisualResourceName,Is.EqualTo(weapon.ProjectileVisualResourceName+suffix));
            Assert.That(shot.SourceWeaponId,Is.EqualTo(weapon.Id));
        }

        [Test] public void SmallerSkullRefreshesWithoutDowngradingAndSwitchClearsOnlyNewShots()
        {
            Pickup(Skull(true));Pickup(Skull(false));Call("TryFire",runtime);
            var old=runtime.Projectiles.Values.Single();Assert.That(old.ProjectileVisualResourceName,Does.EndWith("#Purple"));
            Pickup(catalog.Drops.First(d=>d.Id=="arcade.drop.weapon.rapid"));runtime.FireCooldown=0;Call("TryFire",runtime);
            Assert.That(runtime.Projectiles.Values.Last().ProjectileVisualResourceName,Is.EqualTo("ArcadeBulletRapid"));
            Assert.That(old.ProjectileVisualResourceName,Does.EndWith("#Purple"));
        }

        [Test] public void TemporaryWeaponExpiryClearsSkullAndZeroDeltaFreezesIt()
        {
            Pickup(catalog.Drops.First(d=>d.Id=="arcade.drop.weapon.rapid"));Pickup(Skull(false));
            Call("TickTimers",runtime,0f);runtime.FireCooldown=0;Call("TryFire",runtime);
            Assert.That(runtime.Projectiles.Values.Last().ProjectileVisualResourceName,Does.EndWith("#Red"));
            runtime.TemporaryWeaponRemaining=.01f;Call("TickTimers",runtime,.02f);runtime.FireCooldown=0;Call("TryFire",runtime);
            Assert.That(runtime.Projectiles.Values.Last().ProjectileVisualResourceName,Is.EqualTo("ArcadeBulletBasic"));
        }

        [Test] public void SameWeaponRefreshUpgradesSkullAndItsOwnTimerExpires()
        {
            var weapon=catalog.Drops.First(d=>d.Id=="arcade.drop.weapon.rapid");Pickup(weapon);Pickup(Skull(false));Pickup(weapon);
            Call("TryFire",runtime);Assert.That(runtime.Projectiles.Values.Last().ProjectileVisualResourceName,Does.EndWith("#Purple"));
            Call("TickTimers",runtime,21f);runtime.FireCooldown=0;Call("TryFire",runtime);
            Assert.That(runtime.Projectiles.Values.Last().ProjectileVisualResourceName,Is.EqualTo("ArcadeBulletRapid"));
        }

        [TestCase(false,2)]
        [TestCase(true,3)]
        public void SkullAndMasteryUseSameDamageRounding(bool large,int multiplier)
        {
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetSlayerMart(out var config,out var error),Is.True,error);Set("slayerMartConfig",config);
            runtime.SlayerMartRulesVersion=1;runtime.SlayerMart=new FirepowerLoop.Arcade.ArcadeSlayerMartState{Players=new(){new(){PlayerId=1}}};
            runtime.SlayerMart.Players[0].Owned.Add("slayermart.item.12");Pickup(Skull(large));Call("TryFire",runtime);
            Assert.That(runtime.Projectiles.Values.Single().Damage,Is.EqualTo(Mathf.RoundToInt(catalog.Weapons.First(w=>w.Id==catalog.Rule.BaseWeaponId).Damage*multiplier*1.25f)));
        }

        [Test] public void RoundCheckpointPreservesSkullTierAndRemainingTime()
        {
            Pickup(Skull(true));runtime.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds=7;
            var saved=(ArcadeIslandRoundCheckpoint)Call("CaptureIslandCheckpoint",runtime);
            var restored=new ArcadeCombatRuntime{ArenaNumber=1};Call("RestoreIslandCheckpoint",restored,saved);
            Assert.That(restored.Powerups[ArcadePowerupBehavior.WeaponSkull].Definition.Magnitude,Is.EqualTo(3));
            Assert.That(restored.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds,Is.EqualTo(7));
        }

        [Test] public void ExitWaitStillConsumesSkullLifetime()
        {
            Pickup(Skull(false));runtime.AwaitingExit=true;
            Call("TickTimers",runtime,21f);Assert.That(runtime.Powerups.ContainsKey(ArcadePowerupBehavior.WeaponSkull),Is.False);
        }

        [TestCase("GrayboxBullet","ArcadeBulletRapid")]
        [TestCase("GrayboxArrow","ArcadeFlameBurst")]
        [TestCase("GrayboxArrow","ArcadePanzerschreckRocket")]
        public void ProjectilePowerColorPreservesShapeAndRestoresOnReuse(string prefab,string visual)
        {
            var instance=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/"+prefab+".prefab"));
            try
            {
                var view=instance.GetComponent<ProjectileView>();view.SetArcadePresentation(visual,false);
                var active=instance.GetComponentsInChildren<Transform>(true).First(t=>t.name==visual);
                var renderer=active.GetComponentInChildren<Renderer>();var original=new MaterialPropertyBlock();renderer.GetPropertyBlock(original);
                view.SetArcadePresentation(visual+"#Red",false);Assert.That(active.gameObject.activeSelf,Is.True);
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);Assert.That(block.GetColor("_BaseColor").r,Is.GreaterThan(.9f));
                view.SetArcadePresentation(visual+"#Purple",false);renderer.GetPropertyBlock(block);Assert.That(block.GetColor("_BaseColor").b,Is.GreaterThan(.9f));
                view.SetArcadePresentation(visual,true);renderer.GetPropertyBlock(block);Assert.That(block.GetColor("_BaseColor"),Is.EqualTo(original.GetColor("_BaseColor")));
                view.SetArcadePresentation(visual,false);Assert.That(active.gameObject.activeSelf,Is.True);
                if(prefab=="GrayboxBullet")Assert.That(ProjectileView.UsesOrdinaryArcadeBullet(visual+"#Purple"),Is.True);
            }
            finally{UnityEngine.Object.DestroyImmediate(instance);}
        }

        /// <summary>拾取提示与HUD共用正式语言表；两级骷髅在中英文中均不得回退为内部键。</summary>
        [TestCase("zh-CN","arcade.powerup.skull.small","红骷髅强化")]
        [TestCase("zh-CN","arcade.powerup.skull.large","紫骷髅强化")]
        [TestCase("en-US","arcade.powerup.skull.small","Red Skull Power")]
        [TestCase("en-US","arcade.powerup.skull.large","Purple Skull Power")]
        public void SkullPickupAndHudNamesResolveFromPublishedLanguageAssets(string language,string productId,string expected)
        {
            var previousLoader=ResourceLoader.Current;
            var cache=typeof(LocalizationLanguages).GetField("cachedConfig",BindingFlags.Static|BindingFlags.NonPublic);
            var previousCache=cache.GetValue(null);
            try
            {
                ResourceLoader.Use(new PublishedLanguageLoader());
                LocalizationLanguages.ResetCache();
                var localization=new LocalizationUtility();
                string nameKey=(string)typeof(ArcadeCombatSystem).GetMethod("ProductNameKey",BindingFlags.Static|BindingFlags.NonPublic)
                    .Invoke(null,new object[]{productId});
                string hudKey=(string)typeof(ArcadeHudUICtrl).GetMethod("ProductNameKey",BindingFlags.Static|BindingFlags.NonPublic)
                    .Invoke(null,new object[]{productId});
                Assert.That(localization.Translate(language,nameKey),Is.EqualTo(expected));
                Assert.That(localization.Translate(language,hudKey),Is.EqualTo(expected));
                string pickup=localization.Translate(language,"tip.item_gained",localization.Translate(language,nameKey),1);
                Assert.That(pickup,Does.Contain(expected));
                Assert.That(pickup,Does.Not.Contain("ui."));
            }
            finally { ResourceLoader.Use(previousLoader);cache.SetValue(null,previousCache); }
        }

        /// <summary>只读取正式导入的语言资源，保留实际加载及缓存初始化，不注入测试翻译。</summary>
        private sealed class PublishedLanguageLoader:IResourceLoader
        {
            public T LoadAsset<T>(string path) where T:UnityEngine.Object
                =>AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/"+path);
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> loaded) where T:UnityEngine.Object
            { loaded?.Invoke(LoadAsset<T>(path));return ResourceLoadHandle.Empty; }
            public void Release(ResourceLoadHandle handle) { }
        }

        [Test] public void SkullDropsAndActiveMarkersUseValidColliderFreeResources()
        {
            foreach(bool large in new[]{false,true})
            {
                var drop=Skull(large);var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/"+(large?"ArcadeDropSkullPurple":"ArcadeDropSkullRed")+".prefab");
                Assert.That(prefab,Is.Not.Null);Assert.That(prefab.GetComponentsInChildren<Collider>(true),Is.Empty);
                Assert.That(prefab.GetComponentsInChildren<Renderer>(true).Length,Is.GreaterThan(5));
            }
        }
    }
}
