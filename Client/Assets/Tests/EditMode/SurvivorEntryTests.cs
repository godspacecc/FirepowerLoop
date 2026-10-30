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
    /// <summary>入场失败必须发生在地图及闯关状态变更之前。</summary>
    public sealed class SurvivorEntryTests
    {
        private static async Task<ArcadeSceneFlowResult> Start(Game architecture, CancellationToken token)
        {
            var type = typeof(SurvivorModel).Assembly.GetType("Game.SurvivorSceneFlowSystem");
            Assert.That(type, Is.Not.Null, "幸存者入口事务尚未接入");
            var system = Activator.CreateInstance(type);
            ((ICanSetArchitecture)system).SetArchitecture(architecture);
            return await ((UniTask<ArcadeSceneFlowResult>)type.GetMethod("StartAsync").Invoke(system, new object[] { token })).AsTask();
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

        [UnityTest] public IEnumerator MissingConfigurationLeavesExistingArcadeCheckpointUntouched()
        {
            var architecture = new Game();
            var run = new ArcadeRunModel();
            architecture.RegisterModel(run);
            architecture.RegisterModel(new SurvivorModel());
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() => null));
            run.CommitSafeNode(new ArcadeRunSaveData { RunId = "preserved-arcade", RemainingLives = 3,
                NextArenaNumber = 4, Checkpoint = new ArcadeCheckpointSaveData { ArenaNumber = 4 } });
            var task = Start(architecture, default);
            while (!task.IsCompleted) yield return null;
            var result = task.GetAwaiter().GetResult();
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.Not.Empty);
            Assert.That(run.Current.RunId, Is.EqualTo("preserved-arcade"));
            Assert.That(run.Current.NextArenaNumber, Is.EqualTo(4));
            Assert.That(run.Current.RemainingLives, Is.EqualTo(3));
            Assert.That(architecture.GetModel<SurvivorModel>().Snapshot.Active, Is.False);
        }
    }
}
