using System;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using QFramework;

namespace Game.Tests
{
    /// <summary>验证独立游戏启动资源范围与入口本地检查；不联网、不加载地图、不执行构建。</summary>
    public sealed class StartupGameplayResourceTests
    {
        [Test]
        public void StartupDownloadsOnlyCoreResources()
        {
            var tags = typeof(Boot.ResourceBoot).GetField("StartupResourceTags", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(tags.GetValue(null), Is.EquivalentTo(new[] { "startup" }));
        }

        [Test]
        public void ReadyEntryCompletesWithoutWaitingForSeparateDownloadService()
        {
            var architecture = new ResourceArchitecture();
            var resources = new CachedResources();
            architecture.RegisterSystem<OptionalGameplayResourceSystem>(resources);
            var controller = new GameplayResourcePreparationCtrl(architecture);
            var operation = controller.PrepareAsync(MapIds.Arcade);
            Assert.That(operation.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(operation.GetAwaiter().GetResult(), Is.True);
            Assert.That(resources.LocalQueries, Is.EqualTo(1));
        }

        [Test]
        public void CancelledOrStaleEntryDoesNotQueryResources()
        {
            var architecture = new ResourceArchitecture();
            var resources = new CachedResources();
            architecture.RegisterSystem<OptionalGameplayResourceSystem>(resources);
            var controller = new GameplayResourcePreparationCtrl(architecture);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.That(controller.PrepareAsync(MapIds.Arcade, cancellationToken: cancellation.Token).GetAwaiter().GetResult(), Is.False);
            Assert.That(controller.PrepareAsync(MapIds.Arcade, () => false).GetAwaiter().GetResult(), Is.False);
            Assert.That(resources.LocalQueries, Is.Zero);
        }

        /// <summary>仅注册当前测试所需的资源端口，避免启动真实游戏架构。</summary>
        private sealed class ResourceArchitecture : Architecture<ResourceArchitecture>
        {
            protected override void Init() { }
        }

        /// <summary>已缓存的入口直接放行。</summary>
        private sealed class CachedResources : OptionalGameplayResourceSystem
        {
            public int LocalQueries;
            public override OptionalGameplayResourceQuery QueryMap(string mapId)
            {
                LocalQueries++;
                return new OptionalGameplayResourceQuery(OptionalGameplayResourceState.Ready, "startup-ready", 0, 0, string.Empty);
            }

        }
    }
}
