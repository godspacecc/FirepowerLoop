using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using QFramework;
using UnityEngine.TestTools;

namespace Game.Tests
{
    /// <summary>入场失败时不启动幸存者局。</summary>
    public sealed class SurvivorEntryTests
    {
        private static async Task<SurvivorSceneFlowResult> Start(Game architecture, CancellationToken token)
        {
            var type = typeof(SurvivorModel).Assembly.GetType("Game.SurvivorSceneFlowSystem");
            Assert.That(type, Is.Not.Null, "幸存者入口事务尚未接入");
            var system = Activator.CreateInstance(type);
            ((ICanSetArchitecture)system).SetArchitecture(architecture);
            return await ((UniTask<SurvivorSceneFlowResult>)type.GetMethod("StartAsync").Invoke(system, new object[] { token })).AsTask();
        }

        [UnityTest] public IEnumerator CancelledEntryDoesNotNeedMapOrSaveServices()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var task = Start(new Game(), cancellation.Token);
            while (!task.IsCompleted) yield return null;
            var result = task.GetAwaiter().GetResult();
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.Not.Empty);
        }

        [UnityTest] public IEnumerator MissingConfigurationLeavesSurvivorInactive()
        {
            var architecture = new Game();
            architecture.RegisterModel(new SurvivorModel());
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() => null));
            var task = Start(architecture, default);
            while (!task.IsCompleted) yield return null;
            var result = task.GetAwaiter().GetResult();
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.Not.Empty);
            Assert.That(architecture.GetModel<SurvivorModel>().Snapshot.Active, Is.False);
        }
    }
}
