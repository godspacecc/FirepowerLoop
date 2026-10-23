using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>用真实收集结果及语言加载链验证新增三关可在正式分发环境寻址。</summary>
    public sealed class NextThreeDistributionTests
    {
        /// <summary>遗漏任一显式收集项会导致对应环境或暗影表现只能在编辑器加载。</summary>
        [Test]
        public void StageAndShadowPrefabsResolveAtCollectedRuntimeAddresses()
        {
            var setting = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/AssetBundleCollectorSetting.asset");
            var result = setting.GetType().GetMethod("BeginCollect").Invoke(setting, new object[] { "DefaultPackage", true, false });
            var assets = ((IEnumerable)Read(result, "CollectAssets")).Cast<object>()
                .ToDictionary(asset => (string)Read(asset, "Address"), StringComparer.Ordinal);
            var addresses = new[]
            {
                "Prefabs/SceneObjects/ArcadeGraveyardVisual",
                "Prefabs/SceneObjects/ArcadeTempleVisual",
                "Prefabs/SceneObjects/ArcadeMarketplaceVisual",
                "Prefabs/SceneObjects/ArcadeShadowCrowVisual",
                "Prefabs/SceneObjects/ArcadeShadowTravelVisual",
                "Prefabs/Characters/ArcadeShadowZombie"
            };
            Assert.That(addresses.Where(address => !assets.ContainsKey(address)).ToArray(), Is.Empty,
                "正式收集结果必须包含全部三关及暗影动态地址，不能依赖AssetDatabase替代加载");
            foreach (var address in addresses)
            {
                var asset = assets[address];
                Assert.That((IEnumerable)Read(asset, "AssetTags"), Does.Contain("arcade"), address);
                var path = (string)Read(asset, "AssetInfo").GetType().GetField("AssetPath").GetValue(Read(asset, "AssetInfo"));
                Assert.That(path, Is.EqualTo("Assets/Game/" + address + ".prefab"), address);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, address);
                Assert.That(prefab.GetComponentsInChildren<Renderer>(true), Is.Not.Empty, address);
            }
        }

        /// <summary>两种语言都必须实际翻译12个轮名，缺项不能靠中文回退掩盖。</summary>
        [Test]
        public void AllTwelveRoundNamesTranslateInBothAuthoritativeLanguages()
        {
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            LocalizationLanguages.ResetForTests();
            try
            {
                var utility = new LocalizationUtility();
                foreach (var language in new[] { "zh-CN", "en-US" })
                {
                    Assert.That(RuntimeJson.TryParseStringTable(File.ReadAllText("Assets/Game/Config/Localization/" + language + ".json"), out var table), Is.True);
                    foreach (var stage in new[] { "graveyard", "temple", "marketplace" })
                    for (var round = 1; round <= 4; round++)
                    {
                        var key = "ui.arcade." + stage + ".round." + round;
                        Assert.That(table.ContainsKey(key), Is.True, language + ": " + key);
                        Assert.That(table[key], Is.Not.Empty, language + ": " + key);
                        Assert.That(utility.Translate(language, key), Is.EqualTo(table[key]), language + ": " + key);
                        Assert.That(utility.Translate(language, key), Is.Not.EqualTo(key), language + ": " + key);
                    }
                }
            }
            finally
            {
                ResourceLoader.ResetForTests();
                LocalizationLanguages.ResetForTests();
            }
        }

        /// <summary>通过编辑器程序集公开属性读取收集结果，不引入运行时对编辑器依赖。</summary>
        private static object Read(object instance, string property) => instance.GetType().GetProperty(property).GetValue(instance);
    }
}
