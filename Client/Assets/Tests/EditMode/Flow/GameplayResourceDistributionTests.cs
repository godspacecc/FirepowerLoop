using System;
using System.Collections;
using System.Reflection;
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

        [Test]
        public void ArcadeEnemiesAreNotStartupAndDoNotSharePlayerBundle()
        {
            var player = Find("Prefabs/Characters/GrayboxPlayer");
            var enemy = Find("Prefabs/Characters/ArcadeEnemyBasic");
            Assert.That(Tags(enemy), Does.Contain("arcade"));
            Assert.That(Tags(enemy), Does.Not.Contain("startup"));
            Assert.That(Read(enemy, "BundleName"), Is.Not.EqualTo(Read(player, "BundleName")));
        }

        private static void AssertTags(string address, string tag) => Assert.That(Tags(Find(address)), Does.Contain(tag), address);
        private static object Read(object asset, string name) => asset.GetType().GetProperty(name).GetValue(asset);
        private static IEnumerable Tags(object asset) => (IEnumerable)Read(asset, "AssetTags");
        private static object Find(string address)
        {
            var setting = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/AssetBundleCollectorSetting.asset");
            var result = setting.GetType().GetMethod("BeginCollect").Invoke(setting, new object[] { "DefaultPackage", true, false });
            foreach (var asset in (IEnumerable)Read(result, "CollectAssets"))
                if ((string)Read(asset, "Address") == address) return asset;
            Assert.Fail("清单缺少地址：" + address);
            return null;
        }
    }
}
