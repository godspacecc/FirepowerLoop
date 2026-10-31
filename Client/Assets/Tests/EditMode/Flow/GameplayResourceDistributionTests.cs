using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using UnityEditor;

namespace Game.Tests
{
    /// <summary>只读正式收集设置，验证下载范围和混装边界，不构建 Bundle 或 Player。</summary>
    public sealed class GameplayResourceDistributionTests
    {
        [Test]
        public void BootDownloadsCoreAndArcadeTogether()
        {
            var field = typeof(Boot.ResourceBoot).GetField("StartupResourceTags", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field.GetValue(null), Is.EqualTo(new[] { "startup", "arcade" }));
        }

        /// <summary>街机场景必须随街机资源下载，不再沿用已退役地图的分包标签。</summary>
        [Test]
        public void ArcadeSceneIsCollectedWithArcadeTag()
        {
            AssertTags("Scenes/TaskSampleMap", "arcade");
        }

        /// <summary>两级骷髅必须以运行时请求的地址进入街机清单，否则入场预载会失败。</summary>
        [TestCase("Prefabs/SceneObjects/ArcadeDropSkullRed")]
        [TestCase("Prefabs/SceneObjects/ArcadeDropSkullPurple")]
        public void SkullDropsAreCollectedForArcadeEntry(string address)
        {
            var asset = Find(address);
            var assetInfo = Read(asset, "AssetInfo");
            Assert.That(assetInfo.GetType().GetField("AssetPath").GetValue(assetInfo), Is.EqualTo("Assets/Game/" + address + ".prefab"));
            Assert.That(Tags(asset), Does.Contain("arcade"));
        }

        /// <summary>覆盖入场预载的全部正式掉落配置，包含不以 ArcadeDrop 命名的武器和支援资源。</summary>
        [Test]
        public void ConfiguredDropVisualsAreCollectedForArcadeEntry()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes("Assets/Game/Config/" + name + ".bytes"))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            Assert.That(catalog.Drops, Is.Not.Empty);
            Assert.That(provider.TryGetSurvivor(out var survivor, out error), Is.True, error);
            var assets = CollectAssets().Cast<object>().ToDictionary(asset => (string)Read(asset, "Address"), StringComparer.Ordinal);

            var addresses = catalog.Drops.Select(drop => drop.VisualPrefabAddress)
                .Concat(new[] { survivor.ExperienceVisualPrefabAddress })
                .Where(address => !string.IsNullOrWhiteSpace(address)).Distinct().ToArray();
            Assert.That(addresses, Is.Not.Empty);
            Assert.That(addresses.Where(address => !assets.ContainsKey(address)).ToArray(), Is.Empty,
                "正式掉落配置的入场预载地址必须全部收集");
            foreach (var address in addresses)
            {
                var asset = assets[address];
                Assert.That(Tags(asset), Does.Contain("arcade"), address);
                var assetInfo = Read(asset, "AssetInfo");
                var path = (string)assetInfo.GetType().GetField("AssetPath").GetValue(assetInfo);
                var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
                Assert.That(prefab, Is.Not.Null, address);
                Assert.That(prefab.GetComponentsInChildren<UnityEngine.Renderer>(true), Is.Not.Empty, address);
            }
        }

        [Test]
        public void ArcadeEnemiesAreNotStartupAndDoNotSharePlayerBundle()
        {
            var player = Find("Prefabs/Characters/GrayboxPlayer");
            var enemy = Find("Prefabs/Characters/ArcadeEnemyBasic");
            Assert.That(Tags(enemy), Does.Contain("arcade"));
            Assert.That(Tags(enemy), Does.Not.Contain("startup"));
            Assert.That(Read(enemy, "BundleName"), Is.Not.EqualTo(Read(player, "BundleName")));
        }

        /// <summary>激活后的强化、前三关环境与代码寻址特效也必须进入真实收集清单，不能只检查地面掉落。</summary>
        [Test]
        public void ActiveAndStageVisualsAreCollectedForArcadeEntry()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes("Assets/Game/Config/" + name + ".bytes"))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            var addresses = catalog.Powerups.Select(p => p.ActiveVisualPrefabAddress)
                .Concat(catalog.SupportVisuals.Select(p => p.ActiveVisualPrefabAddress))
                .Concat(catalog.EarlyStages.Select(p => p.EnvironmentAddress))
                .Concat(catalog.EarlyStages.Where(p => p.HerdCount > 0).Select(p => p.HerdAddress))
                .Concat(new[] { "Prefabs/SceneObjects/ArcadeDroneExplosion", "Prefabs/SceneObjects/ArcadeRoundRewardVisual", "Prefabs/SceneObjects/ArcadeStagePortalVisual" })
                .Where(address => !string.IsNullOrWhiteSpace(address)).Distinct().ToArray();
            var assets = CollectAssets().Cast<object>().ToDictionary(asset => (string)Read(asset, "Address"), StringComparer.Ordinal);
            Assert.That(addresses.Where(address => !assets.ContainsKey(address)).ToArray(), Is.Empty,
                "激活效果与关卡资源缺少正式动态地址，AssetDatabase替身无法发现此遗漏");
            foreach(var address in addresses)
            {
                var asset = assets[address];
                Assert.That(Tags(asset), Does.Contain("arcade"), address);
                var path = (string)Read(asset, "AssetInfo").GetType().GetField("AssetPath").GetValue(Read(asset, "AssetInfo"));
                Assert.That(path, Is.EqualTo("Assets/Game/" + address + ".prefab"));
                var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
                Assert.That(prefab, Is.Not.Null, address);
                Assert.That(prefab.GetComponentsInChildren<UnityEngine.Renderer>(true), Is.Not.Empty, address);
            }
        }

        /// <summary>独立敌池实际地址及爬出依赖必须进入正式收集，不能只查闯关表。</summary>
        [Test] public void SurvivorRosterDependenciesAreCollectedBeforeEntry()
        {
            AssertTags("tbsurvivorenemy.bytes","startup");
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes("Assets/Game/Config/"+n+".bytes"))));
            Assert.That(provider.TryGetSurvivorEnemyRoster(out var roster,out var error),Is.True,error);
            var assets=CollectAssets().Cast<object>().ToDictionary(a=>(string)Read(a,"Address"),StringComparer.Ordinal);
            foreach(var row in roster.Entries)
            {
                Assert.That(provider.TryGetEnemy(row.EnemyId,out var enemy,out error),Is.True,error);
                Assert.That(assets.ContainsKey(enemy.PrefabAddress),Is.True,enemy.PrefabAddress);
                Assert.That(Tags(assets[enemy.PrefabAddress]),Does.Contain("arcade"));
                var path=(string)Read(assets[enemy.PrefabAddress],"AssetInfo").GetType().GetField("AssetPath").GetValue(Read(assets[enemy.PrefabAddress],"AssetInfo"));
                var prefab=AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
                Assert.That(prefab.GetComponent<EnemyView>().ValidateGroundEntry(out error),Is.True,error);
                var dependencies=AssetDatabase.GetDependencies(path,true);
                Assert.That(dependencies,Does.Contain("Assets/Game/Art/Survivor/GroundEntry/SurvivorGroundSoil.mat"));
                Assert.That(dependencies.Any(p=>p.EndsWith("DockSeaEntry.anim",StringComparison.Ordinal)),Is.True);
            }
        }

        private static void AssertTags(string address, string tag) => Assert.That(Tags(Find(address)), Does.Contain(tag), address);
        private static object Read(object asset, string name) => asset.GetType().GetProperty(name).GetValue(asset);
        private static IEnumerable Tags(object asset) => (IEnumerable)Read(asset, "AssetTags");
        private static object Find(string address)
        {
            foreach (var asset in CollectAssets())
                if ((string)Read(asset, "Address") == address) return asset;
            Assert.Fail("清单缺少地址：" + address);
            return null;
        }

        /// <summary>只查询正式收集器的寻址结果，不构建资源包。</summary>
        private static IEnumerable CollectAssets()
        {
            var setting = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/AssetBundleCollectorSetting.asset");
            var result = setting.GetType().GetMethod("BeginCollect").Invoke(setting, new object[] { "DefaultPackage", true, false });
            return (IEnumerable)Read(result, "CollectAssets");
        }
    }
}
