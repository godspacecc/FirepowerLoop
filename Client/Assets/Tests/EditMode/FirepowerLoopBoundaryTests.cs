using System.IO;
using System;
using System.Linq;
using Luban;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>锁定独立街机可进入的地图与奖励边界，防止旧玩法重新进入正式目录。</summary>
    public sealed class FirepowerLoopBoundaryTests
    {
        /// <summary>核对正式入口的序列化依赖，防止保留场景或界面重新引用已退出启动链的业务。</summary>
        [Test]
        public void StartupAssetsDoNotDependOnRetiredGameplay()
        {
            var dependencies = AssetDatabase.GetDependencies(new[] {
                "Assets/Game/Scenes/Boot.unity", "Assets/Game/Scenes/Main.unity",
                "Assets/Game/Prefabs/UI/Home/Start.prefab",
                "Assets/Game/Prefabs/UI/Arcade/ArcadeResume.prefab" }, true);
            Assert.That(dependencies, Is.Not.Empty);
            foreach (var path in dependencies)
                foreach (var retired in new[] { "/Inventory/", "/Craft/", "/Shop/", "/Expedition/" })
                    Assert.That(path.Contains(retired), Is.False, path);
        }

        /// <summary>按真实街机配置检查动态加载的道具及特效收集范围，不用伪造资源加载器掩盖地址遗漏。</summary>
        [Test]
        public void ConfiguredArcadeVisualsAreCollectedWithArcadeTag()
        {
            var provider = CreateProvider();
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            var addresses = catalog.Drops.Select(drop => drop.VisualPrefabAddress)
                .Concat(catalog.Powerups.Select(powerup => powerup.ActiveVisualPrefabAddress))
                .Concat(catalog.SupportVisuals.Select(item => item.ActiveVisualPrefabAddress))
                .Where(address => !string.IsNullOrWhiteSpace(address)).Distinct().ToArray();
            Assert.That(addresses, Is.Not.Empty);
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("YooAsset.Editor.AssetBundleCollectorSettingData"))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null);
            var setting = type.GetProperty("Setting").GetValue(null);
            var package = JObject.Parse(JsonUtility.ToJson(setting))["Packages"]
                .Single(item => (string)item["PackageName"] == "DefaultPackage");
            var collectors = package["Groups"].SelectMany(group => group["Collectors"]).ToArray();
            foreach (var address in addresses)
            {
                var path = "Assets/Game/" + address + ".prefab";
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(path), Is.Not.Null, path);
                var collector = collectors.FirstOrDefault(item => path == (string)item["CollectPath"] ||
                    path.StartsWith((string)item["CollectPath"] + "/", StringComparison.Ordinal));
                Assert.That(collector, Is.Not.Null, "街机资源未收集：" + address);
                Assert.That(((string)collector["AssetTags"]).Split(';'), Does.Contain("arcade"), address);
                Assert.That((string)collector["AddressRuleName"], Is.EqualTo("AddressByGameRelativePathNoExtension"), address);
            }
        }

        [Test]
        public void MapCatalogOnlyAllowsTheArcadeDestination()
        {
            Assert.That(MapCatalog.TryGet("map.firepowerloop.arcade", out _), Is.True);
            foreach (var id in new[] { "map.another-day.base", "map.another-day.civilisation-valley",
                         "map.another-day.survival-sample", "map.another-day.mission-sample", "map.hejia-village" })
                Assert.That(MapCatalog.TryGet(id, out _), Is.False, id);
        }

        [Test]
        public void ArcadeCatalogDoesNotIssueOutOfRunCurrencies()
        {
            var provider = CreateProvider();
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            Assert.That(catalog.Rewards, Is.Empty, "本局金币与宝藏奖励不通过局外货币目录发放");
        }

        private static LubanRuntimeConfigProvider CreateProvider()
        {
            var root = System.Environment.GetEnvironmentVariable("FIREPOWER_LOOP_TEST_ROOT") ??
                       Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../.."));
            return new LubanRuntimeConfigProvider(() => new global::Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine(root, "Client/Assets/Game/Config", name + ".bytes")))));
        }
    }
}
