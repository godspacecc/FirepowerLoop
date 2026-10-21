using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using System.Collections;
using Cysharp.Threading.Tasks;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class ArcadeRunSystemTests
    {
        private string runtimeRoot;
        private Game architecture;
        private ArcadeRunSystem arcade;

        [SetUp]
        public void SetUp()
        {
            runtimeRoot = Path.Combine(Path.GetTempPath(), "FirepowerLoopArcade", Guid.NewGuid().ToString("N"));
            ReloadArchitecture();
        }

        /// <summary>重新创建模型与系统，从同一隔离目录读取，避免把内存态误当作落盘恢复。</summary>
        private void ReloadArchitecture()
        {
            TestReset.ResetArchitecture();
            architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(architecture, true);
            architecture.RegisterModel(new ArcadeRunModel());
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(LoadTables));
            architecture.RegisterSystem(new ArcadeRunSystem());
            arcade = architecture.GetSystem<ArcadeRunSystem>();
            arcade.ConfigureForTests(runtimeRoot);
        }

        [TearDown]
        public void TearDown()
        {
            TestReset.ResetArchitecture();
            if (Directory.Exists(runtimeRoot)) Directory.Delete(runtimeRoot, true);
        }

        [Test]
        public void OfflineScoreCheckpointSurvivesFreshArchitecture()
        {
            var policy = Type.GetType("Boot.ClientReleasePolicy, Boot").GetProperty("OfflineOverrideForTests");
            policy.SetValue(null, true);
            try
            {
                Assert.That(arcade.TryStartNewRun(Checkpoint(1), out var initial, out var error), Is.True, error);
                Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
                var completion = Completion(1, 0, 2); completion.Score = 12345;
                Assert.That(arcade.TryCompleteArena(completion, out _, out error), Is.True, error);
                ReloadArchitecture();
                Assert.That(arcade.TryResume(out var restored, out error), Is.True, error);
                Assert.That(restored.RunId, Is.EqualTo(initial.RunId));
                Assert.That(restored.Score, Is.EqualTo(12345));
                Assert.That(restored.Checkpoint.ArenaNumber, Is.EqualTo(2));
                Assert.That(restored.EquipmentBySlot, Is.EquivalentTo(initial.EquipmentBySlot));
                Assert.That(restored.PendingRewards, Is.Empty);
            }
            finally { policy.SetValue(null, null); }
        }

        [Test]
        public void OfflineFailedResultSurvivesFreshArchitecture()
        {
            var policy = Type.GetType("Boot.ClientReleasePolicy, Boot").GetProperty("OfflineOverrideForTests");
            policy.SetValue(null, true);
            try
            {
                Assert.That(arcade.TryStartNewRun(Checkpoint(1), out var initial, out var error), Is.True, error);
                Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
                architecture.GetModel<ArcadeRunModel>().SetTransientTreasureScore(new FirepowerLoop.Arcade.ArcadeTreasureScoreState(54321, 1, 0, 3));
                Assert.That(arcade.TryHandleDeath(out _, out error), Is.EqualTo(ArcadeDeathResult.Respawn), error);
                Assert.That(arcade.TryHandleDeath(out _, out error), Is.EqualTo(ArcadeDeathResult.Respawn), error);
                Assert.That(arcade.TryHandleDeath(out _, out error), Is.EqualTo(ArcadeDeathResult.RunFailed), error);
                ReloadArchitecture();
                Assert.That(arcade.TryResume(out var restored, out error), Is.True, error);
                Assert.That(restored.RunId, Is.EqualTo(initial.RunId));
                Assert.That(restored.HasResult, Is.True);
                Assert.That(restored.Outcome, Is.EqualTo("failed"));
                Assert.That(restored.RemainingLives, Is.Zero);
                Assert.That(restored.Score, Is.EqualTo(54321));
                Assert.That(architecture.GetModel<ArcadeRunModel>().Phase, Is.EqualTo(ArcadeRunPhase.Result));
            }
            finally { policy.SetValue(null, null); }
        }

        [Test]
        public void TransitCompletionSaveFailureKeepsRouteAndRetryPreservesExactResourcesWithoutShop()
        {
            Assert.That(arcade.TryStartConfiguredRun(out var initial,out var error),Is.True,error);
            Assert.That(arcade.TryContinue(out _,out error),Is.True,error);
            var model=architecture.GetModel<ArcadeRunModel>();
            model.SetTransientLives(2);model.SetTransientTreasureScore(new FirepowerLoop.Arcade.ArcadeTreasureScoreState(201000,2,4,2));
            var completion=new ArcadeArenaCompletion{ArenaNumber=1,NextArenaConfigNumber=2,Score=201000,ScoreMultiplier=2,MultiplierProgress=4,
                NextCheckpoint=new ArcadeCheckpointSaveData{ArenaNumber=2},EquipmentBySlot=initial.EquipmentBySlot,
                EmergencyResources=new Dictionary<string,int>{{ArcadeRunDefaults.BombResourceId,3},{ArcadeRunDefaults.DashResourceId,4}}};
            var store=new ArcadeRunFileStore(runtimeRoot);
            using(var locked=new FileStream(store.RunPath,FileMode.Open,FileAccess.Read,FileShare.None))
            {
                Assert.That(arcade.TryCompleteArena(completion,out _,out _),Is.False);
                Assert.That(model.Phase,Is.EqualTo(ArcadeRunPhase.Arena));Assert.That(model.Current.ArenaConfigNumber,Is.EqualTo(1));
            }
            Assert.That(arcade.TryCompleteArena(completion,out var saved,out error),Is.True,error);
            Assert.That(saved.ArenaConfigNumber,Is.EqualTo(2));Assert.That(saved.Checkpoint.ArenaNumber,Is.EqualTo(2));
            Assert.That(saved.RemainingLives,Is.EqualTo(2));Assert.That(saved.Score,Is.EqualTo(201000));
            Assert.That(arcade.TryResume(out var restored,out error),Is.True,error);
            Assert.That(restored.ArenaConfigNumber,Is.EqualTo(2));Assert.That(restored.EmergencyResources[ArcadeRunDefaults.BombResourceId],Is.EqualTo(3));
            Assert.That(restored.EmergencyResources[ArcadeRunDefaults.DashResourceId],Is.EqualTo(4));
            Assert.That(restored.ScoreMultiplier,Is.EqualTo(2));Assert.That(restored.MultiplierProgress,Is.EqualTo(4));
        }

        [Test]
        public void NewRunUsesFixedIsolatedLoadoutAndWritesSafeNode()
        {
            Assert.That(arcade.TryStartNewRun(Checkpoint(1), out var run, out var error), Is.True, error);

            Assert.That(run.RemainingLives, Is.EqualTo(3));
            Assert.That(run.RunGold, Is.Zero);
            Assert.That(run.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId], Is.EqualTo(ArcadeRunDefaults.BasicWeaponId));
            Assert.That(run.NextArenaNumber, Is.EqualTo(1));
            Assert.That(arcade.InspectPending().IsValid, Is.True);
        }

        [Test]
        public void ConfiguredRunUsesOpeningLoadoutAndTransitNodeWithoutShopOffers()
        {
            Assert.That(arcade.TryStartConfiguredRun(out var run, out var error), Is.True, error);

            Assert.That(run.RemainingLives, Is.EqualTo(3));
            Assert.That(run.EmergencyResources[ArcadeRunDefaults.BombResourceId], Is.EqualTo(1));
            Assert.That(run.EmergencyResources[ArcadeRunDefaults.DashResourceId], Is.EqualTo(2));
            Assert.That(run.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId], Is.EqualTo("arcade.weapon.basic"));
        }

        [Test]
        public void GeneratedCatalogHasTwentyFourWaveArenasAndMobileBudgets()
        {
            var provider = Game.Interface.GetUtility<IRuntimeConfigProvider>();
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);

            Assert.That(catalog.Arenas, Has.Count.EqualTo(20));
            Assert.That(catalog.Arenas, Has.All.Property("WaveGroupIds").Count.EqualTo(4));
            Assert.That(catalog.Arenas.Where(value => value.IsBoss).Select(value => value.ArenaNumber),
                Is.EqualTo(new[] { 4, 8, 12, 16, 20 }));
            Assert.That(catalog.Arenas[0].Obstacles, Is.Empty);
            Assert.That(catalog.Arenas[3].Obstacles, Is.Empty);
            Assert.That(catalog.Arenas[3].BossId, Is.EqualTo("arcade.boss.brute-charge"));
            Assert.That(catalog.Bosses, Has.Count.EqualTo(2));
            Assert.That(catalog.Bosses[0].EnemyGroupId, Is.EqualTo("arcade.group.boss.brute"));
            Assert.That(catalog.Bosses[0].ChargeSpeedMultiplier, Is.EqualTo(3.2f));
            Assert.That(catalog.Challenges, Has.Count.EqualTo(2));
            // 完整Slayer已加入正式目录，旧限时挑战按稳定ID验证，不再错误要求目录只有一项。
            var timed = catalog.Challenges.Single(value => value.Id == "arcade.challenge.timed-clear");
            Assert.That(timed.EnemyGroupId, Is.EqualTo("arcade.group.rush.1"));
            Assert.That(timed.TimeLimitSeconds, Is.EqualTo(45f));
            Assert.That(catalog.Rule.MaxAliveEnemies, Is.EqualTo(24));
            Assert.That(catalog.Rule.MaxProjectiles, Is.EqualTo(80));
            Assert.That(catalog.Rule.MaxDrops, Is.EqualTo(18));
            Assert.That(catalog.Rule.PlayerMaxHealth, Is.EqualTo(100));
            Assert.That(catalog.Rule.ArenaHalfWidth, Is.EqualTo(19.5f));
            Assert.That(catalog.Rule.ArenaHalfHeight, Is.EqualTo(19.5f));
            Assert.That(catalog.Weapons.Where(value => value.Id.StartsWith("arcade.weapon.")).Select(value => value.Id), Is.EquivalentTo(new[]
            {
                "arcade.weapon.basic", "arcade.weapon.rapid", "arcade.weapon.spread",
                "arcade.weapon.piercing", "arcade.weapon.rocket", "arcade.weapon.grenade",
                "arcade.weapon.raygun"
            }));
            var piercing = catalog.Weapons.Single(value => value.Id == "arcade.weapon.piercing");
            Assert.That(piercing.Behavior, Is.EqualTo(ArcadeWeaponBehavior.Piercing));
            Assert.That(piercing.MaxHits, Is.EqualTo(5));
            var rocket = catalog.Weapons.Single(value => value.Id == "arcade.weapon.rocket");
            Assert.That(rocket.Behavior, Is.EqualTo(ArcadeWeaponBehavior.Explosive));
            Assert.That(rocket.ExplosionRadius, Is.EqualTo(2.8f));
            Assert.That(rocket.HeldVisualResourceName, Is.EqualTo("ArcadePanzerschreck"));
            Assert.That(rocket.ProjectileVisualResourceName, Is.EqualTo("ArcadePanzerschreckRocket"));
            var grenade = catalog.Weapons.Single(value => value.Id == "arcade.weapon.grenade");
            Assert.That(grenade.Behavior, Is.EqualTo(ArcadeWeaponBehavior.TimedExplosive));
            Assert.That(grenade.ExplosionRadius, Is.EqualTo(3.2f));
            var raygun = catalog.Weapons.Single(value => value.Id == "arcade.weapon.raygun");
            Assert.That(raygun.Behavior, Is.EqualTo(ArcadeWeaponBehavior.Explosive));
            Assert.That(raygun.ExplosionRadius, Is.EqualTo(1.35f));
            Assert.That(catalog.Drops.Select(value => value.ProductId), Does.Contain("arcade.weapon.piercing"));
            Assert.That(catalog.Drops.Select(value => value.ProductId), Does.Contain("arcade.weapon.rocket"));
            Assert.That(catalog.Drops.Select(value => value.ProductId), Does.Contain("arcade.weapon.grenade"));
            Assert.That(catalog.Drops.Select(value => value.ProductId), Does.Contain("arcade.weapon.raygun"));
        }

        [Test]
        public void GeneratedArcadeMap_ReusesTaskSceneWithOnePlayerSpawn()
        {
            var provider = Game.Interface.GetUtility<IRuntimeConfigProvider>();
            Assert.That(provider.TryGetMap(MapIds.Arcade, out var map, out var error), Is.True, error);
            Assert.That(map.SceneAddresses, Is.EqualTo(new[] { "Scenes/TaskSampleMap" }));
            Assert.That(provider.TryGetMapSpawns(MapIds.Arcade, out var spawns, out error), Is.True, error);
            Assert.That(spawns, Has.Count.EqualTo(1));
            Assert.That(spawns[0].ObjectType, Is.EqualTo(MapSpawnObjectType.Player));
        }

        [Test]
        public void ConfiguredRunPersistsInitialSafeNodeBeforeSceneEntry()
        {
            Assert.That(arcade.TryStartConfiguredRun(out _, out var error), Is.True, error);

            Assert.That(Game.Interface.GetModel<ArcadeRunModel>().Phase, Is.EqualTo(ArcadeRunPhase.SafeCheckpoint));
            Assert.That(arcade.InspectPending().IsValid, Is.True);
            Assert.That(arcade.InspectPending().Data.CompletedArenaCount, Is.Zero);
            Assert.That(arcade.InspectPending().Data.NextArenaNumber, Is.EqualTo(1));
        }

        [Test]
        public void ConfiguredEntryFailureKeepsPersistedNodeForRetry()
        {
            Assert.That(arcade.TryStartConfiguredRun(out var saved, out var error), Is.True, error);

            ArcadeResumeUICtrl.RollbackNewRun(arcade, "模拟地图加载失败");
            Assert.That(arcade.InspectPending().Status, Is.EqualTo(SaveFileStatus.Valid));
            Assert.That(arcade.InspectPending().Data.RunId, Is.EqualTo(saved.RunId));
            Assert.That(Game.Interface.GetModel<ArcadeRunModel>().Current.HasResult, Is.False);
            Assert.That(arcade.TryResume(out var retried, out error), Is.True, error);
            Assert.That(retried.RunId, Is.EqualTo(saved.RunId));
        }

        [Test]
        public void ArenaExitRestoresPreviousSafeNodeWithoutLifeCost()
        {
            Assert.That(arcade.TryStartNewRun(Checkpoint(1), out _, out var error), Is.True, error);
            Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
            Assert.That(arcade.TryHandleDeath(out var afterDeath, out error), Is.EqualTo(ArcadeDeathResult.Respawn), error);
            Assert.That(afterDeath.RemainingLives, Is.EqualTo(2));

            Assert.That(arcade.TryReturnToSafeNode(out var restored, out error), Is.True, error);

            Assert.That(restored.RemainingLives, Is.EqualTo(3));
            Assert.That(restored.RunGold, Is.Zero);
            Assert.That(Game.Interface.GetModel<ArcadeRunModel>().Phase, Is.EqualTo(ArcadeRunPhase.SafeCheckpoint));
        }

        [Test]
        public void CompletedArenaCommitsOnlyStableResultsAndNextCheckpoint()
        {
            Assert.That(arcade.TryStartNewRun(Checkpoint(1), out _, out var error), Is.True, error);
            Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
            var completion = Completion(1, 25, 2, "reward.scrap", 4);

            Assert.That(arcade.TryCompleteArena(completion, out var safe, out error), Is.True, error);

            Assert.That(safe.CompletedArenaCount, Is.EqualTo(1));
            Assert.That(safe.NextArenaNumber, Is.EqualTo(2));
            Assert.That(safe.RunGold, Is.EqualTo(25));
            Assert.That(safe.PendingRewards, Is.Empty, "街机不保存局外奖励");
            Assert.That(safe.Checkpoint.ArenaNumber, Is.EqualTo(2));
        }

        [Test]
        public void CompletedCheckpointResumesWithoutTransactions()
        {
            Assert.That(arcade.TryStartNewRun(Checkpoint(1), out _, out var error), Is.True, error);
            Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
            Assert.That(arcade.TryCompleteArena(Completion(1, 10, 2), out _, out error), Is.True, error);
            Assert.That(arcade.TryResume(out var restored, out error), Is.True, error);
            Assert.That(restored.RunGold, Is.EqualTo(10));
            Assert.That(restored.Checkpoint.ArenaNumber, Is.EqualTo(2));
            Assert.That(typeof(ArcadeRunSystem).GetMethod("TryPurchase"), Is.Null);
        }

        [Test]
        public void LastLifeDeletesRunAndClearsAllSessionValue()
        {
            Assert.That(arcade.TryStartNewRun(Checkpoint(1), out _, out var error), Is.True, error);
            Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
            Assert.That(arcade.TryHandleDeath(out _, out error), Is.EqualTo(ArcadeDeathResult.Respawn), error);
            Assert.That(arcade.TryHandleDeath(out _, out error), Is.EqualTo(ArcadeDeathResult.Respawn), error);
            Assert.That(arcade.TryHandleDeath(out var snapshot, out error), Is.EqualTo(ArcadeDeathResult.RunFailed), error);

            Assert.That(snapshot.HasResult, Is.True);
            Assert.That(snapshot.Outcome, Is.EqualTo("failed"));
            Assert.That(arcade.InspectPending().IsValid, Is.True);
            Assert.That(Game.Interface.GetModel<ArcadeRunModel>().Current.HasResult, Is.True);
        }

        [Test]
        public void LastLifeSaveFailurePreservesCheckpointAndCanRetry()
        {
            Assert.That(arcade.TryStartConfiguredRun(out var initial, out var error), Is.True, error);
            Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
            var model = architecture.GetModel<ArcadeRunModel>(); model.SetTransientLives(1);
            var store = new ArcadeRunFileStore(runtimeRoot); byte[] previous = File.ReadAllBytes(store.RunPath);
            using (var locked = new FileStream(store.RunPath, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.That(arcade.TryHandleDeath(out _, out _), Is.EqualTo(ArcadeDeathResult.Rejected));
            Assert.That(model.Phase, Is.EqualTo(ArcadeRunPhase.Arena));
            Assert.That(model.Current.HasResult, Is.False); Assert.That(model.Current.RemainingLives, Is.EqualTo(1));
            Assert.That(File.ReadAllBytes(store.RunPath), Is.EqualTo(previous)); Assert.That(File.Exists(store.TempPath), Is.False);
            Assert.That(arcade.TryHandleDeath(out var failed, out error), Is.EqualTo(ArcadeDeathResult.RunFailed), error);
            Assert.That(failed.RunId, Is.EqualTo(initial.RunId)); Assert.That(failed.RemainingLives, Is.Zero);
            Assert.That(store.Inspect().Data.Outcome, Is.EqualTo("failed"));
        }

        [UnityTest]
        public IEnumerator AsyncLastLifeBlocksCompetingMutationsAndCommitsOnMainThread()
        {
            Assert.That(arcade.TryStartConfiguredRun(out var initial, out var error), Is.True, error);
            Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
            var model = architecture.GetModel<ArcadeRunModel>(); model.SetTransientLives(1);
            var method = typeof(ArcadeRunSystem).GetMethod("HandleLastDeathAsync");
            Assert.That(method, Is.Not.Null, "最后一命写盘必须可异步等待");
            int main = System.Threading.Thread.CurrentThread.ManagedThreadId, committed = 0;
            architecture.RegisterEvent<ArcadeRunChangedEvent>(evt => { if(evt.Phase == ArcadeRunPhase.Result) committed = System.Threading.Thread.CurrentThread.ManagedThreadId; });
            var operation = (UniTask<(ArcadeDeathResult Result, ArcadeRunSaveData Snapshot, string Error)>)method.Invoke(arcade, null);
            Assert.That(model.Phase.ToString(), Is.EqualTo("SavingResult")); Assert.That(model.Current.HasResult, Is.False);
            Assert.That(arcade.TryResume(out _, out _), Is.False); Assert.That(arcade.TryAbandon(out _), Is.False);
            Assert.That(arcade.TryHandleDeath(out _, out _), Is.EqualTo(ArcadeDeathResult.Rejected));
            Assert.That(arcade.TryStartConfiguredRun(out _, out _), Is.False);
            (ArcadeDeathResult Result, ArcadeRunSaveData Snapshot, string Error) value = default;
            yield return operation.ToCoroutine(result => value = result);
            Assert.That(value.Result, Is.EqualTo(ArcadeDeathResult.RunFailed), value.Error);
            Assert.That(value.Snapshot.RunId, Is.EqualTo(initial.RunId)); Assert.That(value.Snapshot.RemainingLives, Is.Zero);
            Assert.That(model.Phase, Is.EqualTo(ArcadeRunPhase.Result)); Assert.That(committed, Is.EqualTo(main));
            Assert.That(arcade.InspectPending().Data.Outcome, Is.EqualTo("failed"));
        }

        [UnityTest]
        public IEnumerator AsyncLastLifeSaveFailureKeepsOriginalNodeAndAllowsRetry()
        {
            Assert.That(arcade.TryStartConfiguredRun(out _, out var error), Is.True, error);
            Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
            var model = architecture.GetModel<ArcadeRunModel>(); model.SetTransientLives(1);
            var store = new ArcadeRunFileStore(runtimeRoot); byte[] previous = File.ReadAllBytes(store.RunPath);
            (ArcadeDeathResult Result, ArcadeRunSaveData Snapshot, string Error) value = default;
            using (var locked = new FileStream(store.RunPath, FileMode.Open, FileAccess.Read, FileShare.None))
                yield return arcade.HandleLastDeathAsync().ToCoroutine(result => value = result);
            Assert.That(value.Result, Is.EqualTo(ArcadeDeathResult.Rejected)); Assert.That(value.Error, Is.Not.Empty);
            Assert.That(model.Phase, Is.EqualTo(ArcadeRunPhase.Arena)); Assert.That(model.Current.HasResult, Is.False);
            Assert.That(model.Current.RemainingLives, Is.EqualTo(1)); Assert.That(File.ReadAllBytes(store.RunPath), Is.EqualTo(previous));
            yield return arcade.HandleLastDeathAsync().ToCoroutine(result => value = result);
            Assert.That(value.Result, Is.EqualTo(ArcadeDeathResult.RunFailed), value.Error);
            Assert.That(store.Inspect().Data.Outcome, Is.EqualTo("failed"));
        }

        [UnityTest]
        public IEnumerator NormalQuitWaitsForPendingLastLifeWrite()
        {
            Assert.That(arcade.TryStartConfiguredRun(out _, out var error), Is.True, error);
            Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
            architecture.GetModel<ArcadeRunModel>().SetTransientLives(1);
            var operation = arcade.HandleLastDeathAsync(); int quits = 0;
            QuitGameCommand.SetQuitHandlerForTests(() => quits++);
            try
            {
                architecture.SendCommand(new QuitGameCommand());
                bool exitedBeforeWrite = quits != 0;
                Assert.That(QuitGameCommand.TryPrepareQuit(architecture), Is.False, "窗口关闭与命令须共用同一退出等待");
                yield return operation.ToCoroutine();
                yield return null;
                Assert.That(exitedBeforeWrite, Is.False, "正常退出不能早于最后一命原子写入");
                Assert.That(quits, Is.EqualTo(1)); Assert.That(arcade.InspectPending().Data.Outcome, Is.EqualTo("failed"));
            }
            finally { QuitGameCommand.SetQuitHandlerForTests(null); }
        }

        [UnityTest]
        public IEnumerator FailedResultSaveCancelsDeferredQuit()
        {
            Assert.That(arcade.TryStartConfiguredRun(out _, out var error), Is.True, error);
            Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
            architecture.GetModel<ArcadeRunModel>().SetTransientLives(1);
            var store = new ArcadeRunFileStore(runtimeRoot); int quits = 0;
            QuitGameCommand.SetQuitHandlerForTests(() => quits++);
            try
            {
                using (var locked = new FileStream(store.RunPath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var operation = arcade.HandleLastDeathAsync(); architecture.SendCommand(new QuitGameCommand());
                    yield return operation.ToCoroutine();
                }
                yield return null;
                Assert.That(quits, Is.Zero); Assert.That(architecture.GetModel<ArcadeRunModel>().Phase, Is.EqualTo(ArcadeRunPhase.Arena));
                Assert.That(store.Inspect().Data.HasResult, Is.False);
                architecture.SendCommand(new QuitGameCommand()); Assert.That(quits, Is.EqualTo(1), "保存失败后新的明确退出仍可执行");
            }
            finally { QuitGameCommand.SetQuitHandlerForTests(null); }
        }

        [UnityTest]
        public IEnumerator LateResultCompletionAfterDeinitCannotPublishOrQuit()
        {
            Assert.That(arcade.TryStartConfiguredRun(out _, out var error), Is.True, error);
            Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
            architecture.GetModel<ArcadeRunModel>().SetTransientLives(1);
            int results = 0, quits = 0;
            architecture.RegisterEvent<ArcadeRunChangedEvent>(evt => { if(evt.Phase == ArcadeRunPhase.Result) results++; });
            QuitGameCommand.SetQuitHandlerForTests(() => quits++);
            try
            {
                var operation = arcade.HandleLastDeathAsync(); architecture.SendCommand(new QuitGameCommand());
                ((ISystem)arcade).Deinit();
                yield return operation.ToCoroutine(); yield return null;
                Assert.That(results, Is.Zero); Assert.That(quits, Is.Zero);
            }
            finally { QuitGameCommand.SetQuitHandlerForTests(null); }
        }

        [Test]
        public void CheckpointContractContainsNoOffers()
        {
            Assert.That(typeof(ArcadeCheckpointSaveData).GetProperty("Offers"), Is.Null);
            Assert.That(typeof(ArcadeRunSystem).GetMethod("TryBuildConfiguredShop"), Is.Null);
        }

        [Test]
        public void ChallengeAttemptPersistsAndCompletionDoesNotAdvanceArena()
        {
            ArcadeCheckpointSaveData shop = Checkpoint(2);
            shop.ChallengeId = "arcade.challenge.timed-clear";
            Assert.That(arcade.TryStartNewRun(shop, out _, out var error), Is.True, error);

            Assert.That(arcade.TryStartChallenge(out var entered, out error), Is.True, error);
            Assert.That(entered.Checkpoint.ChallengeAttempted, Is.True);
            Assert.That(Game.Interface.GetModel<ArcadeRunModel>().Phase, Is.EqualTo(ArcadeRunPhase.Challenge));

            var rewards = Array.Empty<ArcadeRewardSaveData>();
            Assert.That(arcade.TryCompleteChallenge(true, rewards, out var completed, out error), Is.True, error);
            Assert.That(completed.NextArenaNumber, Is.EqualTo(1));
            Assert.That(completed.CompletedArenaCount, Is.Zero);
            Assert.That(completed.Checkpoint.ChallengeAttempted, Is.True);
            Assert.That(completed.PendingRewards, Is.Empty);
            Assert.That(arcade.TryStartChallenge(out _, out _), Is.False);
        }

        [Test]
        public void FailedChallengeReturnsPersistedShopWithoutLifeCost()
        {
            ArcadeCheckpointSaveData shop = Checkpoint(2);
            shop.ChallengeId = "arcade.challenge.timed-clear";
            Assert.That(arcade.TryStartNewRun(shop, out _, out var error), Is.True, error);
            Assert.That(arcade.TryStartChallenge(out _, out error), Is.True, error);

            Assert.That(arcade.TryCompleteChallenge(false, Array.Empty<ArcadeRewardSaveData>(),
                out var restored, out error), Is.True, error);

            Assert.That(restored.RemainingLives, Is.EqualTo(3));
            Assert.That(restored.Checkpoint.ChallengeAttempted, Is.True);
            Assert.That(restored.PendingRewards, Is.Empty);
            Assert.That(Game.Interface.GetModel<ArcadeRunModel>().Phase, Is.EqualTo(ArcadeRunPhase.SafeCheckpoint));
        }

        private static ArcadeCheckpointSaveData Checkpoint(int arenaNumber) => new ArcadeCheckpointSaveData
        {
            ArenaNumber = arenaNumber,
            GenerationSeed = 12345 + arenaNumber
        };

        private static ArcadeArenaCompletion Completion(
            int arenaNumber,
            long gold,
            int nextArena,
            string rewardId = null,
            long rewardAmount = 0)
        {
            var shop = Checkpoint(nextArena);
            var rewards = new List<ArcadeRewardSaveData>();
            if (!string.IsNullOrWhiteSpace(rewardId)) rewards.Add(new ArcadeRewardSaveData { RewardId = rewardId, Amount = rewardAmount });
            return new ArcadeArenaCompletion
            {
                ArenaNumber = arenaNumber,
                EarnedGold = gold,
                EmergencyResources = new Dictionary<string, int>(),
                EquipmentBySlot = new Dictionary<string, string>
                {
                    [ArcadeRunDefaults.WeaponSlotId] = ArcadeRunDefaults.BasicWeaponId
                },
                EarnedRewards = rewards,
                NextCheckpoint = shop
            };
        }

        private static global::Tables LoadTables()
        {
            return new global::Tables(name =>
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>($"Assets/Game/Config/{name}.bytes");
                Assert.That(asset, Is.Not.Null, name);
                return new ByteBuf(asset.bytes);
            });
        }

        /// <summary>模拟 Player 构建中战斗表现初始化失败，并将运行阶段同步退回初始安全节点。</summary>
        private sealed class RejectArcadeEntrySystem : AbstractSystem
        {
            protected override void OnInit()
            {
                this.RegisterEvent<ArcadeRunChangedEvent>(evt =>
                {
                    if (evt.Phase != ArcadeRunPhase.Arena) return;
                    this.GetSystem<ArcadeRunSystem>().TryReturnToSafeNode(out _, out _);
                });
            }
        }
    }
}
