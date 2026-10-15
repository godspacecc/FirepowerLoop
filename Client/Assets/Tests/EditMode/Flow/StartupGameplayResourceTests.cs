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
        public void StartupIncludesCoreAndArcadeResources()
        {
            var tags = typeof(Boot.ResourceBoot).GetField("StartupResourceTags", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(tags.GetValue(null), Is.EquivalentTo(new[] { "startup", "arcade" }));
        }

        [Test]
        public void ReadyEntryCompletesWithoutWaitingForSeparateDownloadService()
        {
            var architecture = new ResourceArchitecture();
            var resources = new DeferredResources();
            architecture.RegisterSystem<OptionalGameplayResourceSystem>(resources);
            var controller = new GameplayResourcePreparationCtrl(architecture);
            var operation = controller.PrepareAsync(MapIds.Arcade);
            try
            {
                Assert.That(operation.Status, Is.EqualTo(UniTaskStatus.Succeeded),
                    "启动已准备资源后，入口必须直接完成本地检查，不再等待独立下载服务。");
                Assert.That(operation.GetAwaiter().GetResult(), Is.True);
                Assert.That(resources.LocalQueries, Is.EqualTo(1));
            }
            finally
            {
                // 旧实现的异步检查在断言后完成，防止失败用例留下悬挂任务。
                resources.Pending.TrySetResult(resources.QueryMap(MapIds.Arcade));
            }
        }

        [Test]
        public void CancelledOrStaleEntryDoesNotQueryResources()
        {
            var architecture = new ResourceArchitecture();
            var resources = new DeferredResources();
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

        /// <summary>本地资源已经齐全，但历史独立下载服务仍在等待，入口不应依赖它。</summary>
        private sealed class DeferredResources : OptionalGameplayResourceSystem
        {
            public readonly UniTaskCompletionSource<OptionalGameplayResourceQuery> Pending = new UniTaskCompletionSource<OptionalGameplayResourceQuery>();
            public int LocalQueries;
            public override OptionalGameplayResourceQuery QueryMap(string mapId)
            {
                LocalQueries++;
                return new OptionalGameplayResourceQuery(OptionalGameplayResourceState.Ready, "startup-ready", 0, 0, string.Empty);
            }
            public override UniTask<OptionalGameplayResourceQuery> CheckMapAsync(string mapId) => Pending.Task;
        }
    }
}
